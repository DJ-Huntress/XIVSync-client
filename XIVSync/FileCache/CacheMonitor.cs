using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using XIVSync.Interop.Ipc;
using XIVSync.MareConfiguration;
using XIVSync.Services;
using XIVSync.Services.Mediator;
using XIVSync.Utils;

namespace XIVSync.FileCache;

public sealed class CacheMonitor : DisposableMediatorSubscriberBase
{
	private record WatcherChange(WatcherChangeTypes ChangeType, string? OldPath = null);

	private readonly MareConfigService _configService;

	private readonly DalamudUtilService _dalamudUtil;

	private readonly FileCompactor _fileCompactor;

	private readonly FileCacheManager _fileDbManager;

	private readonly IpcManager _ipcManager;

	private readonly PerformanceCollectorService _performanceCollector;

	private long _currentFileProgress;

	private CancellationTokenSource _scanCancellationTokenSource = new CancellationTokenSource();

	private readonly CancellationTokenSource _periodicCalculationTokenSource = new CancellationTokenSource();

	public static readonly IImmutableList<string> AllowedFileExtensions;

	private readonly Dictionary<string, WatcherChange> _watcherChanges = new Dictionary<string, WatcherChange>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, WatcherChange> _mareChanges = new Dictionary<string, WatcherChange>(StringComparer.OrdinalIgnoreCase);

	private CancellationTokenSource _penumbraFswCts = new CancellationTokenSource();

	private CancellationTokenSource _mareFswCts = new CancellationTokenSource();

	public long CurrentFileProgress => _currentFileProgress;

	public long FileCacheSize { get; set; }

	public long FileCacheDriveFree { get; set; }

	public ConcurrentDictionary<string, int> HaltScanLocks { get; set; } = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);


	public bool IsScanRunning
	{
		get
		{
			if (CurrentFileProgress <= 0)
			{
				return TotalFiles > 0;
			}
			return true;
		}
	}

	public long TotalFiles { get; private set; }

	public long TotalFilesStorage { get; private set; }

	public bool StorageisNTFS { get; private set; }

	public FileSystemWatcher? PenumbraWatcher { get; private set; }

	public FileSystemWatcher? MareWatcher { get; private set; }

	public CacheMonitor(ILogger<CacheMonitor> logger, IpcManager ipcManager, MareConfigService configService, FileCacheManager fileDbManager, MareMediator mediator, PerformanceCollectorService performanceCollector, DalamudUtilService dalamudUtil, FileCompactor fileCompactor) : base(logger, mediator)
	{
		CacheMonitor cacheMonitor = this;
		_ipcManager = ipcManager;
		_configService = configService;
		_fileDbManager = fileDbManager;
		_performanceCollector = performanceCollector;
		_dalamudUtil = dalamudUtil;
		_fileCompactor = fileCompactor;
		base.Mediator.Subscribe<PenumbraInitializedMessage>(this, delegate
		{
			cacheMonitor.StartPenumbraWatcher(cacheMonitor._ipcManager.Penumbra.ModDirectory);
			cacheMonitor.StartMareWatcher(configService.Current.CacheFolder);
			cacheMonitor.InvokeScan();
		});
		base.Mediator.Subscribe(this, delegate(HaltScanMessage msg)
		{
			cacheMonitor.HaltScan(msg.Source);
		});
		base.Mediator.Subscribe(this, delegate(ResumeScanMessage msg)
		{
			cacheMonitor.ResumeScan(msg.Source);
		});
		base.Mediator.Subscribe<DalamudLoginMessage>(this, delegate
		{
			cacheMonitor.StartMareWatcher(configService.Current.CacheFolder);
			cacheMonitor.StartPenumbraWatcher(cacheMonitor._ipcManager.Penumbra.ModDirectory);
			cacheMonitor.InvokeScan();
		});
		base.Mediator.Subscribe(this, delegate(PenumbraDirectoryChangedMessage msg)
		{
			cacheMonitor.StartPenumbraWatcher(msg.ModDirectory);
			cacheMonitor.InvokeScan();
		});
		if (_ipcManager.Penumbra.APIAvailable && !string.IsNullOrEmpty(_ipcManager.Penumbra.ModDirectory))
		{
			StartPenumbraWatcher(_ipcManager.Penumbra.ModDirectory);
		}
		if (configService.Current.HasValidSetup())
		{
			StartMareWatcher(configService.Current.CacheFolder);
			InvokeScan();
		}
		CancellationToken token = _periodicCalculationTokenSource.Token;
		Task.Run(async delegate
		{
			cacheMonitor.Logger.LogInformation("Starting Periodic Storage Directory Calculation Task");
			token = cacheMonitor._periodicCalculationTokenSource.Token;
			while (!token.IsCancellationRequested)
			{
				try
				{
					while (cacheMonitor._dalamudUtil.IsOnFrameworkThread && !token.IsCancellationRequested)
					{
						await Task.Delay(1).ConfigureAwait(continueOnCapturedContext: false);
					}
					cacheMonitor.RecalculateFileCacheSize(token);
				}
				catch
				{
				}
				await Task.Delay(TimeSpan.FromMinutes(1L), token).ConfigureAwait(continueOnCapturedContext: false);
			}
		}, token);
	}

	public void HaltScan(string source)
	{
		if (!HaltScanLocks.ContainsKey(source))
		{
			HaltScanLocks[source] = 0;
		}
		HaltScanLocks[source]++;
	}

	public void StopMonitoring()
	{
		base.Logger.LogInformation("Stopping monitoring of Penumbra and Mare storage folders");
		MareWatcher?.Dispose();
		PenumbraWatcher?.Dispose();
		MareWatcher = null;
		PenumbraWatcher = null;
	}

	public void StartMareWatcher(string? marePath)
	{
		MareWatcher?.Dispose();
		if (string.IsNullOrEmpty(marePath) || !Directory.Exists(marePath))
		{
			MareWatcher = null;
			base.Logger.LogWarning("Mare file path is not set, cannot start the FSW for Mare.");
			return;
		}
		DriveInfo di = new DriveInfo(new DirectoryInfo(_configService.Current.CacheFolder).Root.FullName);
		StorageisNTFS = string.Equals("NTFS", di.DriveFormat, StringComparison.OrdinalIgnoreCase);
		base.Logger.LogInformation("Mare Storage is on NTFS drive: {isNtfs}", StorageisNTFS);
		base.Logger.LogDebug("Initializing Mare FSW on {path}", marePath);
		MareWatcher = new FileSystemWatcher
		{
			Path = marePath,
			InternalBufferSize = 8388608,
			NotifyFilter = (NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite | NotifyFilters.CreationTime),
			Filter = "*.*",
			IncludeSubdirectories = false
		};
		MareWatcher.Deleted += MareWatcher_FileChanged;
		MareWatcher.Created += MareWatcher_FileChanged;
		MareWatcher.EnableRaisingEvents = true;
	}

	private void MareWatcher_FileChanged(object sender, FileSystemEventArgs e)
	{
		base.Logger.LogTrace("Mare FSW: FileChanged: {change} => {path}", e.ChangeType, e.FullPath);
		if (AllowedFileExtensions.Any((string ext) => e.FullPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
		{
			lock (_watcherChanges)
			{
				_mareChanges[e.FullPath] = new WatcherChange(e.ChangeType);
			}
			MareWatcherExecution();
		}
	}

	public void StartPenumbraWatcher(string? penumbraPath)
	{
		PenumbraWatcher?.Dispose();
		if (string.IsNullOrEmpty(penumbraPath))
		{
			PenumbraWatcher = null;
			base.Logger.LogWarning("Penumbra is not connected or the path is not set, cannot start FSW for Penumbra.");
			return;
		}
		base.Logger.LogDebug("Initializing Penumbra FSW on {path}", penumbraPath);
		PenumbraWatcher = new FileSystemWatcher
		{
			Path = penumbraPath,
			InternalBufferSize = 8388608,
			NotifyFilter = (NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite | NotifyFilters.CreationTime),
			Filter = "*.*",
			IncludeSubdirectories = true
		};
		PenumbraWatcher.Deleted += Fs_Changed;
		PenumbraWatcher.Created += Fs_Changed;
		PenumbraWatcher.Changed += Fs_Changed;
		PenumbraWatcher.Renamed += Fs_Renamed;
		PenumbraWatcher.EnableRaisingEvents = true;
	}

	private void Fs_Changed(object sender, FileSystemEventArgs e)
	{
		if (Directory.Exists(e.FullPath) || !AllowedFileExtensions.Any((string ext) => e.FullPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
		{
			return;
		}
		WatcherChangeTypes changeType = e.ChangeType;
		if (((uint)(changeType - 1) <= 1u || changeType == WatcherChangeTypes.Changed) ? true : false)
		{
			lock (_watcherChanges)
			{
				_watcherChanges[e.FullPath] = new WatcherChange(e.ChangeType);
			}
			base.Logger.LogTrace("FSW {event}: {path}", e.ChangeType, e.FullPath);
			PenumbraWatcherExecution();
		}
	}

	private void Fs_Renamed(object sender, RenamedEventArgs e)
	{
		if (Directory.Exists(e.FullPath))
		{
			string[] directoryFiles = Directory.GetFiles(e.FullPath, "*.*", SearchOption.AllDirectories);
			lock (_watcherChanges)
			{
				string[] array = directoryFiles;
				foreach (string file in array)
				{
					if (AllowedFileExtensions.Any((string ext) => file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
					{
						string oldPath = file.Replace(e.FullPath, e.OldFullPath, StringComparison.OrdinalIgnoreCase);
						_watcherChanges.Remove(oldPath);
						_watcherChanges[file] = new WatcherChange(WatcherChangeTypes.Renamed, oldPath);
						base.Logger.LogTrace("FSW Renamed: {path} -> {new}", oldPath, file);
					}
				}
			}
		}
		else
		{
			if (!AllowedFileExtensions.Any((string ext) => e.FullPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
			{
				return;
			}
			lock (_watcherChanges)
			{
				_watcherChanges.Remove(e.OldFullPath);
				_watcherChanges[e.FullPath] = new WatcherChange(WatcherChangeTypes.Renamed, e.OldFullPath);
			}
			base.Logger.LogTrace("FSW Renamed: {path} -> {new}", e.OldFullPath, e.FullPath);
		}
		PenumbraWatcherExecution();
	}

	private async Task MareWatcherExecution()
	{
		_mareFswCts = _mareFswCts.CancelRecreate();
		CancellationToken token = _mareFswCts.Token;
		TimeSpan delay = TimeSpan.FromSeconds(5L);
		Dictionary<string, WatcherChange> changes;
		lock (_mareChanges)
		{
			changes = _mareChanges.ToDictionary<KeyValuePair<string, WatcherChange>, string, WatcherChange>((KeyValuePair<string, WatcherChange> t) => t.Key, (KeyValuePair<string, WatcherChange> t) => t.Value, StringComparer.Ordinal);
		}
		try
		{
			do
			{
				await Task.Delay(delay, token).ConfigureAwait(continueOnCapturedContext: false);
			}
			while (HaltScanLocks.Any<KeyValuePair<string, int>>((KeyValuePair<string, int> f) => f.Value > 0));
		}
		catch (TaskCanceledException)
		{
			return;
		}
		lock (_mareChanges)
		{
			foreach (string key in changes.Keys)
			{
				_mareChanges.Remove(key);
			}
		}
		HandleChanges(changes);
	}

	private void HandleChanges(Dictionary<string, WatcherChange> changes)
	{
		lock (_fileDbManager)
		{
			IEnumerable<string> deletedEntries = from c in changes
				where c.Value.ChangeType == WatcherChangeTypes.Deleted
				select c.Key;
			IEnumerable<KeyValuePair<string, WatcherChange>> renamedEntries = changes.Where<KeyValuePair<string, WatcherChange>>((KeyValuePair<string, WatcherChange> c) => c.Value.ChangeType == WatcherChangeTypes.Renamed);
			IEnumerable<string> remainingEntries = from c in changes
				where c.Value.ChangeType != WatcherChangeTypes.Deleted
				select c.Key;
			foreach (string entry in deletedEntries)
			{
				base.Logger.LogDebug("FSW Change: Deletion - {val}", entry);
			}
			foreach (KeyValuePair<string, WatcherChange> entry in renamedEntries)
			{
				base.Logger.LogDebug("FSW Change: Renamed - {oldVal} => {val}", entry.Value.OldPath, entry.Key);
			}
			foreach (string entry in remainingEntries)
			{
				base.Logger.LogDebug("FSW Change: Creation or Change - {val}", entry);
			}
			string[] allChanges = deletedEntries.Concat<string>(renamedEntries.Select((KeyValuePair<string, WatcherChange> c) => c.Value.OldPath)).Concat(renamedEntries.Select((KeyValuePair<string, WatcherChange> c) => c.Key)).Concat(remainingEntries)
				.ToArray();
			_fileDbManager.GetFileCachesByPaths(allChanges);
			_fileDbManager.WriteOutFullCsv();
		}
	}

	private async Task PenumbraWatcherExecution()
	{
		_penumbraFswCts = _penumbraFswCts.CancelRecreate();
		CancellationToken token = _penumbraFswCts.Token;
		Dictionary<string, WatcherChange> changes;
		lock (_watcherChanges)
		{
			changes = _watcherChanges.ToDictionary<KeyValuePair<string, WatcherChange>, string, WatcherChange>((KeyValuePair<string, WatcherChange> t) => t.Key, (KeyValuePair<string, WatcherChange> t) => t.Value, StringComparer.Ordinal);
		}
		TimeSpan delay = TimeSpan.FromSeconds(10L);
		try
		{
			do
			{
				await Task.Delay(delay, token).ConfigureAwait(continueOnCapturedContext: false);
			}
			while (HaltScanLocks.Any<KeyValuePair<string, int>>((KeyValuePair<string, int> f) => f.Value > 0));
		}
		catch (TaskCanceledException)
		{
			return;
		}
		lock (_watcherChanges)
		{
			foreach (string key in changes.Keys)
			{
				_watcherChanges.Remove(key);
			}
		}
		HandleChanges(changes);
	}

	public void InvokeScan()
	{
		TotalFiles = 0L;
		_currentFileProgress = 0L;
		_scanCancellationTokenSource = _scanCancellationTokenSource?.CancelRecreate() ?? new CancellationTokenSource();
		CancellationToken token = _scanCancellationTokenSource.Token;
		Task.Run(async delegate
		{
			base.Logger.LogDebug("Starting Full File Scan");
			TotalFiles = 0L;
			_currentFileProgress = 0L;
			while (_dalamudUtil.IsOnFrameworkThread)
			{
				base.Logger.LogWarning("Scanner is on framework, waiting for leaving thread before continuing");
				await Task.Delay(250, token).ConfigureAwait(continueOnCapturedContext: false);
			}
			Thread scanThread = new Thread((ThreadStart)delegate
			{
				try
				{
					PerformanceCollectorService performanceCollector = _performanceCollector;
					CacheMonitor sender = this;
					MareInterpolatedStringHandler counterName = new MareInterpolatedStringHandler(12, 0);
					counterName.AppendLiteral("FullFileScan");
					performanceCollector.LogPerformance(sender, counterName, delegate
					{
						FullFileScan(token);
					});
				}
				catch (Exception exception)
				{
					base.Logger.LogError(exception, "Error during Full File Scan");
				}
			})
			{
				Priority = ThreadPriority.Lowest,
				IsBackground = true
			};
			scanThread.Start();
			while (scanThread.IsAlive)
			{
				await Task.Delay(250).ConfigureAwait(continueOnCapturedContext: false);
			}
			TotalFiles = 0L;
			_currentFileProgress = 0L;
		}, token);
	}

	public void RecalculateFileCacheSize(CancellationToken token)
	{
		if (string.IsNullOrEmpty(_configService.Current.CacheFolder) || !Directory.Exists(_configService.Current.CacheFolder))
		{
			FileCacheSize = 0L;
			return;
		}
		FileCacheSize = -1L;
		DriveInfo di = new DriveInfo(new DirectoryInfo(_configService.Current.CacheFolder).Root.FullName);
		try
		{
			FileCacheDriveFree = di.AvailableFreeSpace;
		}
		catch (Exception ex)
		{
			base.Logger.LogWarning(ex, "Could not determine drive size for Storage Folder {folder}", _configService.Current.CacheFolder);
		}
		List<FileInfo> files = (from f in Directory.EnumerateFiles(_configService.Current.CacheFolder)
			select new FileInfo(f) into f
			orderby f.LastAccessTime
			select f).ToList();
		FileCacheSize = files.Sum(delegate(FileInfo f)
		{
			token.ThrowIfCancellationRequested();
			try
			{
				return _fileCompactor.GetFileSizeOnDisk(f, StorageisNTFS);
			}
			catch
			{
				return 0L;
			}
		});
		long maxCacheInBytes = (long)(_configService.Current.MaxLocalCacheInGiB * 1024.0 * 1024.0 * 1024.0);
		if (FileCacheSize >= maxCacheInBytes)
		{
			double maxCacheBuffer = (double)maxCacheInBytes * 0.05;
			while (FileCacheSize > maxCacheInBytes - (long)maxCacheBuffer)
			{
				FileInfo oldestFile = files[0];
				FileCacheSize -= _fileCompactor.GetFileSizeOnDisk(oldestFile);
				File.Delete(oldestFile.FullName);
				files.Remove(oldestFile);
			}
		}
	}

	public void ResetLocks()
	{
		HaltScanLocks.Clear();
	}

	public void ResumeScan(string source)
	{
		if (!HaltScanLocks.ContainsKey(source))
		{
			HaltScanLocks[source] = 0;
		}
		HaltScanLocks[source]--;
		if (HaltScanLocks[source] < 0)
		{
			HaltScanLocks[source] = 0;
		}
	}

	protected override void Dispose(bool disposing)
	{
		base.Dispose(disposing);
		_scanCancellationTokenSource?.CancelDispose();
		PenumbraWatcher?.Dispose();
		MareWatcher?.Dispose();
		_penumbraFswCts?.CancelDispose();
		_mareFswCts?.CancelDispose();
		_periodicCalculationTokenSource?.CancelDispose();
	}

	private void FullFileScan(CancellationToken ct)
	{
		TotalFiles = 1L;
		string penumbraDir = _ipcManager.Penumbra.ModDirectory;
		bool penDirExists = true;
		bool cacheDirExists = true;
		if (string.IsNullOrEmpty(penumbraDir) || !Directory.Exists(penumbraDir))
		{
			penDirExists = false;
			base.Logger.LogWarning("Penumbra directory is not set or does not exist.");
		}
		if (string.IsNullOrEmpty(_configService.Current.CacheFolder) || !Directory.Exists(_configService.Current.CacheFolder))
		{
			cacheDirExists = false;
			base.Logger.LogWarning("Mare Cache directory is not set or does not exist.");
		}
		if (!penDirExists || !cacheDirExists)
		{
			return;
		}
		ThreadPriority previousThreadPriority = Thread.CurrentThread.Priority;
		Thread.CurrentThread.Priority = ThreadPriority.Lowest;
		base.Logger.LogDebug("Getting files from {penumbra} and {storage}", penumbraDir, _configService.Current.CacheFolder);
		Dictionary<string, string[]> penumbraFiles = new Dictionary<string, string[]>(StringComparer.Ordinal);
		foreach (string folder in Directory.EnumerateDirectories(penumbraDir))
		{
			try
			{
				penumbraFiles[folder] = Enumerable.ToArray(from f in Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories).AsParallel()
					where AllowedFileExtensions.Any((string e) => f.EndsWith(e, StringComparison.OrdinalIgnoreCase)) && !f.Contains("\\bg\\", StringComparison.OrdinalIgnoreCase) && !f.Contains("\\bgcommon\\", StringComparison.OrdinalIgnoreCase) && !f.Contains("\\ui\\", StringComparison.OrdinalIgnoreCase)
					select f);
			}
			catch (Exception ex)
			{
				base.Logger.LogWarning(ex, "Could not enumerate path {path}", folder);
			}
			Thread.Sleep(50);
			if (ct.IsCancellationRequested)
			{
				return;
			}
		}
		ParallelQuery<string> allCacheFiles = Directory.GetFiles(_configService.Current.CacheFolder, "*.*", SearchOption.TopDirectoryOnly).AsParallel().Where(delegate(string f)
		{
			string text = f.Split('\\')[^1];
			return text.Length == 40 || (text.Split('.').FirstOrDefault()?.Length ?? 0) == 40;
		});
		if (ct.IsCancellationRequested)
		{
			return;
		}
		Dictionary<string, bool> allScannedFiles = penumbraFiles.SelectMany((KeyValuePair<string, string[]> k) => k.Value).Concat(allCacheFiles).Distinct<string>(StringComparer.OrdinalIgnoreCase)
			.ToDictionary<string, string, bool>((string t) => t.ToLowerInvariant(), (string t) => false, StringComparer.OrdinalIgnoreCase);
		TotalFiles = allScannedFiles.Count;
		Thread.CurrentThread.Priority = previousThreadPriority;
		Thread.Sleep(TimeSpan.FromSeconds(2L));
		if (ct.IsCancellationRequested)
		{
			return;
		}
		int threadCount = Math.Clamp((int)((float)Environment.ProcessorCount / 2f), 2, 8);
		List<FileCacheEntity> entitiesToRemove = new List<FileCacheEntity>();
		List<FileCacheEntity> entitiesToUpdate = new List<FileCacheEntity>();
		object sync = new object();
		Thread[] workerThreads = new Thread[threadCount];
		ConcurrentQueue<FileCacheEntity> fileCaches = new ConcurrentQueue<FileCacheEntity>(_fileDbManager.GetAllFileCaches());
		TotalFilesStorage = fileCaches.Count;
		for (int i = 0; i < threadCount; i++)
		{
			base.Logger.LogTrace("Creating Thread {i}", i);
			workerThreads[i] = new Thread(delegate(object? tcounter)
			{
				int num = (int)tcounter;
				base.Logger.LogTrace("Spawning Worker Thread {i}", num);
				FileCacheEntity result;
				while (!ct.IsCancellationRequested && fileCaches.TryDequeue(out result))
				{
					try
					{
						if (ct.IsCancellationRequested)
						{
							return;
						}
						if (!_ipcManager.Penumbra.APIAvailable)
						{
							base.Logger.LogWarning("Penumbra not available");
							return;
						}
						(FileState State, FileCacheEntity FileCache) tuple = _fileDbManager.ValidateFileCacheEntity(result);
						if (tuple.State != FileState.RequireDeletion)
						{
							lock (sync)
							{
								allScannedFiles[tuple.FileCache.ResolvedFilepath] = true;
							}
						}
						if (tuple.State == FileState.RequireUpdate)
						{
							base.Logger.LogTrace("To update: {path}", tuple.FileCache.ResolvedFilepath);
							lock (sync)
							{
								entitiesToUpdate.Add(tuple.FileCache);
							}
						}
						else if (tuple.State == FileState.RequireDeletion)
						{
							base.Logger.LogTrace("To delete: {path}", tuple.FileCache.ResolvedFilepath);
							lock (sync)
							{
								entitiesToRemove.Add(tuple.FileCache);
							}
						}
					}
					catch (Exception exception2)
					{
						base.Logger.LogWarning(exception2, "Failed validating {path}", result.ResolvedFilepath);
					}
					Interlocked.Increment(ref _currentFileProgress);
				}
				base.Logger.LogTrace("Ending Worker Thread {i}", num);
			})
			{
				Priority = ThreadPriority.Lowest,
				IsBackground = true
			};
			workerThreads[i].Start(i);
		}
		while (!ct.IsCancellationRequested && workerThreads.Any((Thread u) => u.IsAlive))
		{
			Thread.Sleep(1000);
		}
		if (ct.IsCancellationRequested)
		{
			return;
		}
		base.Logger.LogTrace("Threads exited");
		if (!_ipcManager.Penumbra.APIAvailable)
		{
			base.Logger.LogWarning("Penumbra not available");
			return;
		}
		if (entitiesToUpdate.Any() || entitiesToRemove.Any())
		{
			foreach (FileCacheEntity entity in entitiesToUpdate)
			{
				_fileDbManager.UpdateHashedFile(entity);
			}
			foreach (FileCacheEntity entity in entitiesToRemove)
			{
				_fileDbManager.RemoveHashedFile(entity.Hash, entity.PrefixedFilePath);
			}
			_fileDbManager.WriteOutFullCsv();
		}
		base.Logger.LogTrace("Scanner validated existing db files");
		if (!_ipcManager.Penumbra.APIAvailable)
		{
			base.Logger.LogWarning("Penumbra not available");
		}
		else
		{
			if (ct.IsCancellationRequested)
			{
				return;
			}
			if (allScannedFiles.Any<KeyValuePair<string, bool>>((KeyValuePair<string, bool> c) => !c.Value))
			{
				Parallel.ForEach(from c in allScannedFiles
					where !c.Value
					select c.Key, new ParallelOptions
				{
					MaxDegreeOfParallelism = threadCount,
					CancellationToken = ct
				}, delegate(string cachePath)
				{
					if (!ct.IsCancellationRequested)
					{
						if (!_ipcManager.Penumbra.APIAvailable)
						{
							base.Logger.LogWarning("Penumbra not available");
						}
						else
						{
							try
							{
								if (_fileDbManager.CreateFileEntry(cachePath) == null)
								{
									_fileDbManager.CreateCacheEntry(cachePath);
								}
							}
							catch (Exception exception)
							{
								base.Logger.LogWarning(exception, "Failed adding {file}", cachePath);
							}
							Interlocked.Increment(ref _currentFileProgress);
						}
					}
				});
				base.Logger.LogTrace("Scanner added {notScanned} new files to db", allScannedFiles.Count<KeyValuePair<string, bool>>((KeyValuePair<string, bool> c) => !c.Value));
			}
			base.Logger.LogDebug("Scan complete");
			TotalFiles = 0L;
			_currentFileProgress = 0L;
			entitiesToRemove.Clear();
			allScannedFiles.Clear();
			if (!_configService.Current.InitialScanComplete)
			{
				_configService.Current.InitialScanComplete = true;
				_configService.Save();
				StartMareWatcher(_configService.Current.CacheFolder);
				StartPenumbraWatcher(penumbraDir);
			}
		}
	}

	static CacheMonitor()
	{
        AllowedFileExtensions = ImmutableList.Create([".mdl", ".tex", ".mtrl", ".tmb", ".pap", ".avfx", ".atex", ".sklb", ".eid", ".phyb", ".pbd", ".scd", ".skp", ".shpk"]);
    }
}
