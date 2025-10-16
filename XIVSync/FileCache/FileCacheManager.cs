using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using K4os.Compression.LZ4.Legacy;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XIVSync.Interop.Ipc;
using XIVSync.MareConfiguration;
using XIVSync.MareConfiguration.Models;
using XIVSync.Services.Mediator;
using XIVSync.Utils;

namespace XIVSync.FileCache;

public sealed class FileCacheManager : IHostedService
{
	public const string CachePrefix = "{cache}";

	public const string CsvSplit = "|";

	public const string PenumbraPrefix = "{penumbra}";

	private readonly MareConfigService _configService;

	private readonly MareMediator _mareMediator;

	private readonly string _csvPath;

	private readonly ConcurrentDictionary<string, List<FileCacheEntity>> _fileCaches = new ConcurrentDictionary<string, List<FileCacheEntity>>(StringComparer.Ordinal);

	private readonly SemaphoreSlim _getCachesByPathsSemaphore = new SemaphoreSlim(1, 1);

	private readonly object _fileWriteLock = new object();

	private readonly IpcManager _ipcManager;

	private readonly ILogger<FileCacheManager> _logger;

	public string CacheFolder => _configService.Current.CacheFolder;

	private string CsvBakPath => _csvPath + ".bak";

	public FileCacheManager(ILogger<FileCacheManager> logger, IpcManager ipcManager, MareConfigService configService, MareMediator mareMediator)
	{
		_logger = logger;
		_ipcManager = ipcManager;
		_configService = configService;
		_mareMediator = mareMediator;
		_csvPath = Path.Combine(configService.ConfigurationDirectory, "FileCache.csv");
	}

	public FileCacheEntity? CreateCacheEntry(string path)
	{
		FileInfo fi = new FileInfo(path);
		if (!fi.Exists)
		{
			return null;
		}
		_logger.LogTrace("Creating cache entry for {path}", path);
		string fullName = fi.FullName.ToLowerInvariant();
		if (!fullName.Contains(_configService.Current.CacheFolder.ToLowerInvariant(), StringComparison.Ordinal))
		{
			return null;
		}
		string prefixedPath = fullName.Replace(_configService.Current.CacheFolder.ToLowerInvariant(), "{cache}\\", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
		return CreateFileCacheEntity(fi, prefixedPath);
	}

	public FileCacheEntity? CreateFileEntry(string path)
	{
		FileInfo fi = new FileInfo(path);
		if (!fi.Exists)
		{
			return null;
		}
		_logger.LogTrace("Creating file entry for {path}", path);
		string fullName = fi.FullName.ToLowerInvariant();
		if (!fullName.Contains(_ipcManager.Penumbra.ModDirectory.ToLowerInvariant(), StringComparison.Ordinal))
		{
			return null;
		}
		string prefixedPath = fullName.Replace(_ipcManager.Penumbra.ModDirectory.ToLowerInvariant(), "{penumbra}\\", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
		return CreateFileCacheEntity(fi, prefixedPath);
	}

	public List<FileCacheEntity> GetAllFileCaches()
	{
		return _fileCaches.Values.SelectMany((List<FileCacheEntity> v) => v).ToList();
	}

	public List<FileCacheEntity> GetAllFileCachesByHash(string hash, bool ignoreCacheEntries = false, bool validate = true)
	{
		List<FileCacheEntity> output = new List<FileCacheEntity>();
		if (_fileCaches.TryGetValue(hash, out List<FileCacheEntity> fileCacheEntities))
		{
			foreach (FileCacheEntity fileCache in fileCacheEntities.Where((FileCacheEntity c) => !ignoreCacheEntries || !c.IsCacheEntry).ToList())
			{
				if (!validate)
				{
					output.Add(fileCache);
					continue;
				}
				FileCacheEntity validated = GetValidatedFileCache(fileCache);
				if (validated != null)
				{
					output.Add(validated);
				}
			}
		}
		return output;
	}

	public Task<List<FileCacheEntity>> ValidateLocalIntegrity(IProgress<(int, int, FileCacheEntity)> progress, CancellationToken cancellationToken)
	{
		_mareMediator.Publish(new HaltScanMessage("ValidateLocalIntegrity"));
		_logger.LogInformation("Validating local storage");
		List<FileCacheEntity> cacheEntries = (from v in _fileCaches.SelectMany<KeyValuePair<string, List<FileCacheEntity>>, FileCacheEntity>((KeyValuePair<string, List<FileCacheEntity>> v) => v.Value)
			where v.IsCacheEntry
			select v).ToList();
		List<FileCacheEntity> brokenEntities = new List<FileCacheEntity>();
		int i = 0;
		foreach (FileCacheEntity fileCache in cacheEntries)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				break;
			}
			_logger.LogInformation("Validating {file}", fileCache.ResolvedFilepath);
			progress.Report((i, cacheEntries.Count, fileCache));
			i++;
			if (!File.Exists(fileCache.ResolvedFilepath))
			{
				brokenEntities.Add(fileCache);
				continue;
			}
			try
			{
				string computedHash = fileCache.ResolvedFilepath.GetFileHash();
				if (!string.Equals(computedHash, fileCache.Hash, StringComparison.Ordinal))
				{
					_logger.LogInformation("Failed to validate {file}, got hash {hash}, expected hash {hash}", fileCache.ResolvedFilepath, computedHash, fileCache.Hash);
					brokenEntities.Add(fileCache);
				}
			}
			catch (Exception e)
			{
				_logger.LogWarning(e, "Error during validation of {file}", fileCache.ResolvedFilepath);
				brokenEntities.Add(fileCache);
			}
		}
		foreach (FileCacheEntity brokenEntity in brokenEntities)
		{
			RemoveHashedFile(brokenEntity.Hash, brokenEntity.PrefixedFilePath);
			try
			{
				File.Delete(brokenEntity.ResolvedFilepath);
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Could not delete {file}", brokenEntity.ResolvedFilepath);
			}
		}
		_mareMediator.Publish(new ResumeScanMessage("ValidateLocalIntegrity"));
		return Task.FromResult(brokenEntities);
	}

	public string GetCacheFilePath(string hash, string extension)
	{
		return Path.Combine(_configService.Current.CacheFolder, hash + "." + extension);
	}

	public async Task<(string, byte[])> GetCompressedFileData(string fileHash, CancellationToken uploadToken)
	{
		string fileCache = GetFileCacheByHash(fileHash).ResolvedFilepath;
		return (fileHash, LZ4Wrapper.WrapHC(await File.ReadAllBytesAsync(fileCache, uploadToken).ConfigureAwait(continueOnCapturedContext: false), 0, (int)new FileInfo(fileCache).Length));
	}

	public FileCacheEntity? GetFileCacheByHash(string hash)
	{
		if (_fileCaches.TryGetValue(hash, out List<FileCacheEntity> hashes))
		{
			FileCacheEntity item = hashes.OrderBy((FileCacheEntity p) => (!p.PrefixedFilePath.Contains("{penumbra}")) ? 1 : 0).FirstOrDefault();
			if (item != null)
			{
				return GetValidatedFileCache(item);
			}
		}
		return null;
	}

	private FileCacheEntity? GetFileCacheByPath(string path)
	{
		string cleanedPath = path.Replace("/", "\\", StringComparison.OrdinalIgnoreCase).ToLowerInvariant().Replace(_ipcManager.Penumbra.ModDirectory.ToLowerInvariant(), "", StringComparison.OrdinalIgnoreCase);
		FileCacheEntity entry = _fileCaches.SelectMany<KeyValuePair<string, List<FileCacheEntity>>, FileCacheEntity>((KeyValuePair<string, List<FileCacheEntity>> v) => v.Value).FirstOrDefault((FileCacheEntity f) => f.ResolvedFilepath.EndsWith(cleanedPath, StringComparison.OrdinalIgnoreCase));
		if (entry == null)
		{
			_logger.LogDebug("Found no entries for {path}", cleanedPath);
			return CreateFileEntry(path);
		}
		return GetValidatedFileCache(entry);
	}

	public Dictionary<string, FileCacheEntity?> GetFileCachesByPaths(string[] paths)
	{
		_getCachesByPathsSemaphore.Wait();
		try
		{
			Dictionary<string, string> dictionary = paths.Distinct<string>(StringComparer.OrdinalIgnoreCase).ToDictionary<string, string, string>((string p) => p, (string p) => p.Replace("/", "\\", StringComparison.OrdinalIgnoreCase).Replace(_ipcManager.Penumbra.ModDirectory, _ipcManager.Penumbra.ModDirectory.EndsWith('\\') ? "{penumbra}\\" : "{penumbra}", StringComparison.OrdinalIgnoreCase).Replace(_configService.Current.CacheFolder, _configService.Current.CacheFolder.EndsWith('\\') ? "{cache}\\" : "{cache}", StringComparison.OrdinalIgnoreCase)
				.Replace("\\\\", "\\", StringComparison.Ordinal), StringComparer.OrdinalIgnoreCase);
			Dictionary<string, FileCacheEntity> result = new Dictionary<string, FileCacheEntity>(StringComparer.OrdinalIgnoreCase);
			Dictionary<string, FileCacheEntity> dict = _fileCaches.SelectMany<KeyValuePair<string, List<FileCacheEntity>>, FileCacheEntity>((KeyValuePair<string, List<FileCacheEntity>> f) => f.Value).ToDictionary<FileCacheEntity, string, FileCacheEntity>((FileCacheEntity d) => d.PrefixedFilePath, (FileCacheEntity d) => d, StringComparer.OrdinalIgnoreCase);
			foreach (KeyValuePair<string, string> entry in dictionary)
			{
				if (dict.TryGetValue(entry.Value, out var entity))
				{
					FileCacheEntity validatedCache = GetValidatedFileCache(entity);
					result.Add(entry.Key, validatedCache);
				}
				else if (!entry.Value.Contains("{cache}", StringComparison.Ordinal))
				{
					result.Add(entry.Key, CreateFileEntry(entry.Key));
				}
				else
				{
					result.Add(entry.Key, CreateCacheEntry(entry.Key));
				}
			}
			return result;
		}
		finally
		{
			_getCachesByPathsSemaphore.Release();
		}
	}

	public void RemoveHashedFile(string hash, string prefixedFilePath)
	{
		if (_fileCaches.TryGetValue(hash, out List<FileCacheEntity> caches))
		{
			int? removedCount = caches?.RemoveAll((FileCacheEntity c) => string.Equals(c.PrefixedFilePath, prefixedFilePath, StringComparison.Ordinal));
			_logger.LogTrace("Removed from DB: {count} file(s) with hash {hash} and file cache {path}", removedCount, hash, prefixedFilePath);
			if (caches != null && caches.Count == 0)
			{
				_fileCaches.Remove<string, List<FileCacheEntity>>(hash, out var _);
			}
		}
	}

	public void UpdateHashedFile(FileCacheEntity fileCache, bool computeProperties = true)
	{
		_logger.LogTrace("Updating hash for {path}", fileCache.ResolvedFilepath);
		string oldHash = fileCache.Hash;
		string prefixedPath = fileCache.PrefixedFilePath;
		if (computeProperties)
		{
			FileInfo fi = new FileInfo(fileCache.ResolvedFilepath);
			fileCache.Size = fi.Length;
			fileCache.CompressedSize = null;
			fileCache.Hash = fileCache.ResolvedFilepath.GetFileHash();
			fileCache.LastModifiedDateTicks = fi.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
		}
		RemoveHashedFile(oldHash, prefixedPath);
		AddHashedFile(fileCache);
	}

	public (FileState State, FileCacheEntity FileCache) ValidateFileCacheEntity(FileCacheEntity fileCache)
	{
		fileCache = ReplacePathPrefixes(fileCache);
		FileInfo fi = new FileInfo(fileCache.ResolvedFilepath);
		if (!fi.Exists)
		{
			return (State: FileState.RequireDeletion, FileCache: fileCache);
		}
		if (!string.Equals(fi.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture), fileCache.LastModifiedDateTicks, StringComparison.Ordinal))
		{
			return (State: FileState.RequireUpdate, FileCache: fileCache);
		}
		return (State: FileState.Valid, FileCache: fileCache);
	}

	public void WriteOutFullCsv()
	{
		lock (_fileWriteLock)
		{
			StringBuilder sb = new StringBuilder();
			foreach (FileCacheEntity entry in _fileCaches.SelectMany<KeyValuePair<string, List<FileCacheEntity>>, FileCacheEntity>((KeyValuePair<string, List<FileCacheEntity>> k) => k.Value).OrderBy<FileCacheEntity, string>((FileCacheEntity f) => f.PrefixedFilePath, StringComparer.OrdinalIgnoreCase))
			{
				sb.AppendLine(entry.CsvEntry);
			}
			if (File.Exists(_csvPath))
			{
				File.Copy(_csvPath, CsvBakPath, overwrite: true);
			}
			try
			{
				File.WriteAllText(_csvPath, sb.ToString());
				File.Delete(CsvBakPath);
			}
			catch
			{
				File.WriteAllText(CsvBakPath, sb.ToString());
			}
		}
	}

	internal FileCacheEntity MigrateFileHashToExtension(FileCacheEntity fileCache, string ext)
	{
		try
		{
			RemoveHashedFile(fileCache.Hash, fileCache.PrefixedFilePath);
			string extensionPath = fileCache.ResolvedFilepath.ToUpper(CultureInfo.InvariantCulture) + "." + ext;
			File.Move(fileCache.ResolvedFilepath, extensionPath, overwrite: true);
			FileCacheEntity newHashedEntity = new FileCacheEntity(fileCache.Hash, fileCache.PrefixedFilePath + "." + ext, DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
			newHashedEntity.SetResolvedFilePath(extensionPath);
			AddHashedFile(newHashedEntity);
			_logger.LogTrace("Migrated from {oldPath} to {newPath}", fileCache.ResolvedFilepath, newHashedEntity.ResolvedFilepath);
			return newHashedEntity;
		}
		catch (Exception ex)
		{
			AddHashedFile(fileCache);
			_logger.LogWarning(ex, "Failed to migrate entity {entity}", fileCache.PrefixedFilePath);
			return fileCache;
		}
	}

	private void AddHashedFile(FileCacheEntity fileCache)
	{
		if (!_fileCaches.TryGetValue(fileCache.Hash, out List<FileCacheEntity> entries) || entries == null)
		{
			entries = (_fileCaches[fileCache.Hash] = new List<FileCacheEntity>());
		}
		if (!entries.Exists((FileCacheEntity u) => string.Equals(u.PrefixedFilePath, fileCache.PrefixedFilePath, StringComparison.OrdinalIgnoreCase)))
		{
			entries.Add(fileCache);
		}
	}

	private FileCacheEntity? CreateFileCacheEntity(FileInfo fileInfo, string prefixedPath, string? hash = null)
	{
		if (hash == null)
		{
			hash = fileInfo.FullName.GetFileHash();
		}
		FileCacheEntity entity = new FileCacheEntity(hash, prefixedPath, fileInfo.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture), fileInfo.Length);
		entity = ReplacePathPrefixes(entity);
		AddHashedFile(entity);
		lock (_fileWriteLock)
		{
			File.AppendAllLines(_csvPath, new string[1] { entity.CsvEntry });
		}
		FileCacheEntity result = GetFileCacheByPath(fileInfo.FullName);
		_logger.LogTrace("Creating cache entity for {name} success: {success}", fileInfo.FullName, result != null);
		return result;
	}

	private FileCacheEntity? GetValidatedFileCache(FileCacheEntity fileCache)
	{
		FileCacheEntity resultingFileCache = ReplacePathPrefixes(fileCache);
		return Validate(resultingFileCache);
	}

	private FileCacheEntity ReplacePathPrefixes(FileCacheEntity fileCache)
	{
		if (fileCache.PrefixedFilePath.StartsWith("{penumbra}", StringComparison.OrdinalIgnoreCase))
		{
			fileCache.SetResolvedFilePath(fileCache.PrefixedFilePath.Replace("{penumbra}", _ipcManager.Penumbra.ModDirectory, StringComparison.Ordinal));
		}
		else if (fileCache.PrefixedFilePath.StartsWith("{cache}", StringComparison.OrdinalIgnoreCase))
		{
			fileCache.SetResolvedFilePath(fileCache.PrefixedFilePath.Replace("{cache}", _configService.Current.CacheFolder, StringComparison.Ordinal));
		}
		return fileCache;
	}

	private FileCacheEntity? Validate(FileCacheEntity fileCache)
	{
		FileInfo file = new FileInfo(fileCache.ResolvedFilepath);
		if (!file.Exists)
		{
			RemoveHashedFile(fileCache.Hash, fileCache.PrefixedFilePath);
			return null;
		}
		if (!string.Equals(file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture), fileCache.LastModifiedDateTicks, StringComparison.Ordinal))
		{
			UpdateHashedFile(fileCache);
		}
		return fileCache;
	}

	public Task StartAsync(CancellationToken cancellationToken)
	{
		_logger.LogInformation("Starting FileCacheManager");
		lock (_fileWriteLock)
		{
			try
			{
				_logger.LogInformation("Checking for {bakPath}", CsvBakPath);
				if (File.Exists(CsvBakPath))
				{
					_logger.LogInformation("{bakPath} found, moving to {csvPath}", CsvBakPath, _csvPath);
					File.Move(CsvBakPath, _csvPath, overwrite: true);
				}
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Failed to move BAK to ORG, deleting BAK");
				try
				{
					if (File.Exists(CsvBakPath))
					{
						File.Delete(CsvBakPath);
					}
				}
				catch (Exception ex1)
				{
					_logger.LogWarning(ex1, "Could not delete bak file");
				}
			}
		}
		if (File.Exists(_csvPath))
		{
			if (!_ipcManager.Penumbra.APIAvailable || string.IsNullOrEmpty(_ipcManager.Penumbra.ModDirectory))
			{
				_mareMediator.Publish(new NotificationMessage("Penumbra not connected", "Could not load local file cache data. Penumbra is not connected or not properly set up. Please enable and/or configure Penumbra properly to use Mare. After, reload Mare in the Plugin installer.", NotificationType.Error));
			}
			_logger.LogInformation("{csvPath} found, parsing", _csvPath);
			bool success = false;
			string[] entries = Array.Empty<string>();
			int attempts = 0;
			while (!success && attempts < 10)
			{
				try
				{
					_logger.LogInformation("Attempting to read {csvPath}", _csvPath);
					entries = File.ReadAllLines(_csvPath);
					success = true;
				}
				catch (Exception ex)
				{
					attempts++;
					_logger.LogWarning(ex, "Could not open {file}, trying again", _csvPath);
					Thread.Sleep(100);
				}
			}
			if (!entries.Any())
			{
				_logger.LogWarning("Could not load entries from {path}, continuing with empty file cache", _csvPath);
			}
			_logger.LogInformation("Found {amount} files in {path}", entries.Length, _csvPath);
			Dictionary<string, bool> processedFiles = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
			string[] array = entries;
			foreach (string entry in array)
			{
				string[] splittedEntry = entry.Split("|");
				try
				{
					string hash = splittedEntry[0];
					if (hash.Length != 40)
					{
						throw new InvalidOperationException("Expected Hash length of 40, received " + hash.Length);
					}
					string path = splittedEntry[1];
					string time = splittedEntry[2];
					if (processedFiles.ContainsKey(path))
					{
						_logger.LogWarning("Already processed {file}, ignoring", path);
						continue;
					}
					processedFiles.Add(path, value: true);
					long size = -1L;
					long compressed = -1L;
					if (splittedEntry.Length > 3)
					{
						if (long.TryParse(splittedEntry[3], CultureInfo.InvariantCulture, out var result))
						{
							size = result;
						}
						if (long.TryParse(splittedEntry[4], CultureInfo.InvariantCulture, out var resultCompressed))
						{
							compressed = resultCompressed;
						}
					}
					AddHashedFile(ReplacePathPrefixes(new FileCacheEntity(hash, path, time, size, compressed)));
				}
				catch (Exception ex)
				{
					_logger.LogWarning(ex, "Failed to initialize entry {entry}, ignoring", entry);
				}
			}
			if (processedFiles.Count != entries.Length)
			{
				WriteOutFullCsv();
			}
		}
		_logger.LogInformation("Started FileCacheManager");
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		WriteOutFullCsv();
		return Task.CompletedTask;
	}
}
