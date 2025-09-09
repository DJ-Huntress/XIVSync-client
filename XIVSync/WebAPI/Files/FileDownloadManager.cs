using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Utility;
using K4os.Compression.LZ4.Legacy;
using Microsoft.Extensions.Logging;
using XIVSync.API.Data;
using XIVSync.API.Dto.Files;
using XIVSync.API.Routes;
using XIVSync.FileCache;
using XIVSync.PlayerData.Handlers;
using XIVSync.Services.Mediator;
using XIVSync.WebAPI.Files.Models;

namespace XIVSync.WebAPI.Files;

public class FileDownloadManager : DisposableMediatorSubscriberBase
{
	private readonly Dictionary<string, FileDownloadStatus> _downloadStatus;

	private readonly FileCompactor _fileCompactor;

	private readonly FileCacheManager _fileDbManager;

	private readonly FileTransferOrchestrator _orchestrator;

	private readonly List<ThrottledStream> _activeDownloadStreams;

	public List<DownloadFileTransfer> CurrentDownloads { get; private set; } = new List<DownloadFileTransfer>();

	public List<FileTransfer> ForbiddenTransfers => _orchestrator.ForbiddenTransfers;

	public bool IsDownloading => !CurrentDownloads.Any();

	public FileDownloadManager(ILogger<FileDownloadManager> logger, MareMediator mediator, FileTransferOrchestrator orchestrator, FileCacheManager fileCacheManager, FileCompactor fileCompactor)
		: base(logger, mediator)
	{
		_downloadStatus = new Dictionary<string, FileDownloadStatus>(StringComparer.Ordinal);
		_orchestrator = orchestrator;
		_fileDbManager = fileCacheManager;
		_fileCompactor = fileCompactor;
		_activeDownloadStreams = new List<ThrottledStream>();
		base.Mediator.Subscribe<DownloadLimitChangedMessage>(this, delegate
		{
			if (!_activeDownloadStreams.Any())
			{
				return;
			}
			long num = _orchestrator.DownloadLimitPerSlot();
			base.Logger.LogTrace("Setting new Download Speed Limit to {newLimit}", num);
			foreach (ThrottledStream activeDownloadStream in _activeDownloadStreams)
			{
				activeDownloadStream.BandwidthLimit = num;
			}
		});
	}

	public static void MungeBuffer(Span<byte> buffer)
	{
		for (int i = 0; i < buffer.Length; i++)
		{
			buffer[i] ^= 42;
		}
	}

	public void ClearDownload()
	{
		CurrentDownloads.Clear();
		_downloadStatus.Clear();
	}

	public async Task DownloadFiles(GameObjectHandler gameObject, List<FileReplacementData> fileReplacementDto, CancellationToken ct)
	{
		base.Mediator.Publish(new HaltScanMessage("DownloadFiles"));
		try
		{
			await DownloadFilesInternal(gameObject, fileReplacementDto, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch
		{
			ClearDownload();
		}
		finally
		{
			base.Mediator.Publish(new DownloadFinishedMessage(gameObject));
			base.Mediator.Publish(new ResumeScanMessage("DownloadFiles"));
		}
	}

	protected override void Dispose(bool disposing)
	{
		ClearDownload();
		foreach (ThrottledStream stream in _activeDownloadStreams.ToList())
		{
			try
			{
				stream.Dispose();
			}
			catch
			{
			}
		}
		base.Dispose(disposing);
	}

	private static byte MungeByte(int byteOrEof)
	{
		if (byteOrEof == -1)
		{
			throw new EndOfStreamException();
		}
		return (byte)(byteOrEof ^ 0x2A);
	}

	private static (string fileHash, long fileLengthBytes) ReadBlockFileHeader(FileStream fileBlockStream)
	{
		List<char> hashName = new List<char>();
		List<char> fileLength = new List<char>();
		if (MungeByte(fileBlockStream.ReadByte()) != 35)
		{
			throw new InvalidDataException("Data is invalid, first char is not #");
		}
		bool readHash = false;
		while (true)
		{
			int num = fileBlockStream.ReadByte();
			if (num == -1)
			{
				break;
			}
			char readChar = (char)MungeByte(num);
			switch (readChar)
			{
			case ':':
				readHash = true;
				continue;
			case '#':
				return (fileHash: string.Join("", hashName), fileLengthBytes: long.Parse(string.Join("", fileLength)));
			}
			if (!readHash)
			{
				hashName.Add(readChar);
			}
			else
			{
				fileLength.Add(readChar);
			}
		}
		throw new EndOfStreamException();
	}

	private async Task DownloadAndMungeFileHttpClient(string downloadGroup, Guid requestId, List<DownloadFileTransfer> fileTransfer, string tempPath, IProgress<long> progress, CancellationToken ct)
	{
		base.Logger.LogDebug("GUID {requestId} on server {uri} for files {files}", requestId, fileTransfer[0].DownloadUri, string.Join(", ", fileTransfer.Select((DownloadFileTransfer c) => c.Hash).ToList()));
		await WaitForDownloadReady(fileTransfer, requestId, ct).ConfigureAwait(continueOnCapturedContext: false);
		_downloadStatus[downloadGroup].DownloadStatus = DownloadStatus.Downloading;
		HttpResponseMessage response = null;
		Uri requestUrl = MareFiles.CacheGetFullPath(fileTransfer[0].DownloadUri, requestId);
		base.Logger.LogDebug("Downloading {requestUrl} for request {id}", requestUrl, requestId);
		try
		{
			response = await _orchestrator.SendRequestAsync(HttpMethod.Get, requestUrl, ct, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(continueOnCapturedContext: false);
			response.EnsureSuccessStatusCode();
		}
		catch (HttpRequestException ex)
		{
			base.Logger.LogWarning(ex, "Error during download of {requestUrl}, HttpStatusCode: {code}", requestUrl, ex.StatusCode);
			bool flag;
			switch (ex.StatusCode)
			{
			case HttpStatusCode.Unauthorized:
			case HttpStatusCode.NotFound:
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (flag)
			{
				throw new InvalidDataException($"Http error {ex.StatusCode} (cancelled: {ct.IsCancellationRequested}): {requestUrl}", ex);
			}
		}
		ThrottledStream stream = null;
		try
		{
			int num;
			_ = num - 2;
			_ = 3;
			try
			{
				FileStream fileStream = File.Create(tempPath);
				ConfiguredAsyncDisposable I_2 = fileStream.ConfigureAwait(continueOnCapturedContext: false);
				try
				{
					int bufferSize = ((response.Content.Headers.ContentLength > 1048576) ? 65536 : 8196);
					byte[] buffer = new byte[bufferSize];
					long limit = _orchestrator.DownloadLimitPerSlot();
					base.Logger.LogTrace("Starting Download of {id} with a speed limit of {limit} to {tempPath}", requestId, limit, tempPath);
					stream = new ThrottledStream(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(continueOnCapturedContext: false), limit);
					_activeDownloadStreams.Add(stream);
					while (true)
					{
						int num2;
						int bytesRead = (num2 = await stream.ReadAsync(buffer, ct).ConfigureAwait(continueOnCapturedContext: false));
						if (num2 <= 0)
						{
							break;
						}
						ct.ThrowIfCancellationRequested();
						MungeBuffer(buffer.AsSpan(0, bytesRead));
						await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(continueOnCapturedContext: false);
						progress.Report(bytesRead);
					}
					base.Logger.LogDebug("{requestUrl} downloaded to {tempPath}", requestUrl, tempPath);
				}
				finally
				{
					IAsyncDisposable asyncDisposable = I_2 as IAsyncDisposable;
					if (asyncDisposable != null)
					{
						await asyncDisposable.DisposeAsync();
					}
				}
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception)
			{
				try
				{
					if (!tempPath.IsNullOrEmpty())
					{
						File.Delete(tempPath);
					}
				}
				catch
				{
				}
				throw;
			}
		}
		finally
		{
			if (stream != null)
			{
				_activeDownloadStreams.Remove(stream);
				await stream.DisposeAsync().ConfigureAwait(continueOnCapturedContext: false);
			}
		}
	}

	public async Task<List<DownloadFileTransfer>> InitiateDownloadList(GameObjectHandler gameObjectHandler, List<FileReplacementData> fileReplacement, CancellationToken ct)
	{
		base.Logger.LogDebug("Download start: {id}", gameObjectHandler.Name);
		List<DownloadFileDto> downloadFileInfoFromService = (await FilesGetSizes(fileReplacement.Select((FileReplacementData f) => f.Hash).Distinct<string>(StringComparer.Ordinal).ToList(), ct).ConfigureAwait(continueOnCapturedContext: false)).ToList();
		base.Logger.LogDebug("Files with size 0 or less: {files}", string.Join(", ", from f in downloadFileInfoFromService
			where f.Size <= 0
			select f.Hash));
		foreach (DownloadFileDto dto in downloadFileInfoFromService.Where((DownloadFileDto c) => c.IsForbidden))
		{
			if (!_orchestrator.ForbiddenTransfers.Exists((FileTransfer f) => string.Equals(f.Hash, dto.Hash, StringComparison.Ordinal)))
			{
				_orchestrator.ForbiddenTransfers.Add(new DownloadFileTransfer(dto));
			}
		}
		CurrentDownloads = (from d in downloadFileInfoFromService.Distinct()
			select new DownloadFileTransfer(d) into d
			where d.CanBeTransferred
			select d).ToList();
		return CurrentDownloads;
	}

	private async Task DownloadFilesInternal(GameObjectHandler gameObjectHandler, List<FileReplacementData> fileReplacement, CancellationToken ct)
	{
		IEnumerable<IGrouping<string, DownloadFileTransfer>> downloadGroups = CurrentDownloads.GroupBy<DownloadFileTransfer, string>((DownloadFileTransfer f) => f.DownloadUri.Host + ":" + f.DownloadUri.Port, StringComparer.Ordinal);
		foreach (IGrouping<string, DownloadFileTransfer> downloadGroup in downloadGroups)
		{
			_downloadStatus[downloadGroup.Key] = new FileDownloadStatus
			{
				DownloadStatus = DownloadStatus.Initializing,
				TotalBytes = downloadGroup.Sum((DownloadFileTransfer c) => c.Total),
				TotalFiles = 1,
				TransferredBytes = 0L,
				TransferredFiles = 0
			};
		}
		base.Mediator.Publish(new DownloadStartedMessage(gameObjectHandler, _downloadStatus));
		await Parallel.ForEachAsync(downloadGroups, new ParallelOptions
		{
			MaxDegreeOfParallelism = downloadGroups.Count(),
			CancellationToken = ct
		}, async delegate(IGrouping<string, DownloadFileTransfer> fileGroup, CancellationToken token)
		{
			HttpResponseMessage requestIdResponse = await _orchestrator.SendRequestAsync(HttpMethod.Post, MareFiles.RequestEnqueueFullPath(fileGroup.First().DownloadUri), fileGroup.Select((DownloadFileTransfer c) => c.Hash), token).ConfigureAwait(continueOnCapturedContext: false);
			ILogger logger = base.Logger;
			object obj = fileGroup.Count();
			object downloadUri = fileGroup.First().DownloadUri;
			string text = await requestIdResponse.Content.ReadAsStringAsync(token).ConfigureAwait(continueOnCapturedContext: false);
			logger.LogDebug("Sent request for {n} files on server {uri} with result {result}", obj, downloadUri, text);
			Guid requestId = Guid.Parse((await requestIdResponse.Content.ReadAsStringAsync().ConfigureAwait(continueOnCapturedContext: false)).Trim('"'));
			base.Logger.LogDebug("GUID {requestId} for {n} files on server {uri}", requestId, fileGroup.Count(), fileGroup.First().DownloadUri);
			string blockFile = _fileDbManager.GetCacheFilePath(requestId.ToString("N"), "blk");
			FileInfo fi = new FileInfo(blockFile);
			try
			{
				_downloadStatus[fileGroup.Key].DownloadStatus = DownloadStatus.WaitingForSlot;
				await _orchestrator.WaitForDownloadSlotAsync(token).ConfigureAwait(continueOnCapturedContext: false);
				_downloadStatus[fileGroup.Key].DownloadStatus = DownloadStatus.WaitingForQueue;
				Progress<long> progress = new Progress<long>(delegate(long bytesDownloaded)
				{
					try
					{
						if (_downloadStatus.TryGetValue(fileGroup.Key, out FileDownloadStatus value))
						{
							value.TransferredBytes += bytesDownloaded;
						}
					}
					catch (Exception exception4)
					{
						base.Logger.LogWarning(exception4, "Could not set download progress");
					}
				});
				await DownloadAndMungeFileHttpClient(fileGroup.Key, requestId, fileGroup.ToList(), blockFile, progress, token).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (OperationCanceledException)
			{
				base.Logger.LogDebug("{dlName}: Detected cancellation of download, partially extracting files for {id}", fi.Name, gameObjectHandler);
			}
			catch (Exception exception)
			{
				_orchestrator.ReleaseDownloadSlot();
				File.Delete(blockFile);
				base.Logger.LogError(exception, "{dlName}: Error during download of {id}", fi.Name, requestId);
				ClearDownload();
				return;
			}
			FileStream fileBlockStream = null;
			try
			{
				int num;
				_ = num - 5;
				_ = 1;
				try
				{
					if (_downloadStatus.TryGetValue(fileGroup.Key, out FileDownloadStatus status))
					{
						status.TransferredFiles = 1;
						status.DownloadStatus = DownloadStatus.Decompressing;
					}
					fileBlockStream = File.OpenRead(blockFile);
					while (fileBlockStream.Position < fileBlockStream.Length)
					{
						var (fileHash, fileLengthBytes) = ReadBlockFileHeader(fileBlockStream);
						try
						{
							string fileExtension = fileReplacement.First((FileReplacementData f) => string.Equals(f.Hash, fileHash, StringComparison.OrdinalIgnoreCase)).GamePaths[0].Split(".")[^1];
							string filePath = _fileDbManager.GetCacheFilePath(fileHash, fileExtension);
							base.Logger.LogDebug("{dlName}: Decompressing {file}:{le} => {dest}", fi.Name, fileHash, fileLengthBytes, filePath);
							byte[] compressedFileContent = new byte[fileLengthBytes];
							if (await fileBlockStream.ReadAsync(compressedFileContent, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false) != fileLengthBytes)
							{
								throw new EndOfStreamException();
							}
							MungeBuffer(compressedFileContent);
							byte[] decompressedFile = LZ4Wrapper.Unwrap(compressedFileContent);
							await _fileCompactor.WriteAllBytesAsync(filePath, decompressedFile, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
							PersistFileToStorage(fileHash, filePath);
						}
						catch (EndOfStreamException)
						{
							base.Logger.LogWarning("{dlName}: Failure to extract file {fileHash}, stream ended prematurely", fi.Name, fileHash);
						}
						catch (Exception exception2)
						{
							base.Logger.LogWarning(exception2, "{dlName}: Error during decompression", fi.Name);
						}
					}
				}
				catch (EndOfStreamException)
				{
					base.Logger.LogDebug("{dlName}: Failure to extract file header data, stream ended", fi.Name);
				}
				catch (Exception exception3)
				{
					base.Logger.LogError(exception3, "{dlName}: Error during block file read", fi.Name);
				}
			}
			finally
			{
				_orchestrator.ReleaseDownloadSlot();
				if (fileBlockStream != null)
				{
					await fileBlockStream.DisposeAsync().ConfigureAwait(continueOnCapturedContext: false);
				}
				File.Delete(blockFile);
			}
		}).ConfigureAwait(continueOnCapturedContext: false);
		base.Logger.LogDebug("Download end: {id}", gameObjectHandler);
		ClearDownload();
	}

	private async Task<List<DownloadFileDto>> FilesGetSizes(List<string> hashes, CancellationToken ct)
	{
		if (!_orchestrator.IsInitialized)
		{
			throw new InvalidOperationException("FileTransferManager is not initialized");
		}
		return (await (await _orchestrator.SendRequestAsync(HttpMethod.Get, MareFiles.ServerFilesGetSizesFullPath(_orchestrator.FilesCdnUri), hashes, ct).ConfigureAwait(continueOnCapturedContext: false)).Content.ReadFromJsonAsync<List<DownloadFileDto>>(ct).ConfigureAwait(continueOnCapturedContext: false)) ?? new List<DownloadFileDto>();
	}

	private void PersistFileToStorage(string fileHash, string filePath)
	{
		new FileInfo(filePath)
		{
			CreationTime = RandomDayInThePast()(),
			LastAccessTime = DateTime.Today,
			LastWriteTime = RandomDayInThePast()()
		};
		try
		{
			FileCacheEntity entry = _fileDbManager.CreateCacheEntry(filePath);
			if (entry != null && !string.Equals(entry.Hash, fileHash, StringComparison.OrdinalIgnoreCase))
			{
				base.Logger.LogError("Hash mismatch after extracting, got {hash}, expected {expectedHash}, deleting file", entry.Hash, fileHash);
				File.Delete(filePath);
				_fileDbManager.RemoveHashedFile(entry.Hash, entry.PrefixedFilePath);
			}
		}
		catch (Exception exception)
		{
			base.Logger.LogWarning(exception, "Error creating cache entry");
		}
		static Func<DateTime> RandomDayInThePast()
		{
			DateTime start = new DateTime(1995, 1, 1, 1, 1, 1, DateTimeKind.Local);
			Random gen = new Random();
			int range = (DateTime.Today - start).Days;
			return () => start.AddDays(gen.Next(range));
		}
	}

	private async Task WaitForDownloadReady(List<DownloadFileTransfer> downloadFileTransfer, Guid requestId, CancellationToken downloadCt)
	{
		bool alreadyCancelled = false;
		try
		{
			CancellationTokenSource localTimeoutCts = new CancellationTokenSource();
			localTimeoutCts.CancelAfter(TimeSpan.FromSeconds(5L));
			CancellationTokenSource composite = CancellationTokenSource.CreateLinkedTokenSource(downloadCt, localTimeoutCts.Token);
			while (!_orchestrator.IsDownloadReady(requestId))
			{
				try
				{
					await Task.Delay(250, composite.Token).ConfigureAwait(continueOnCapturedContext: false);
				}
				catch (TaskCanceledException)
				{
					if (downloadCt.IsCancellationRequested)
					{
						throw;
					}
					(await _orchestrator.SendRequestAsync(HttpMethod.Get, MareFiles.RequestCheckQueueFullPath(downloadFileTransfer[0].DownloadUri, requestId), downloadFileTransfer.Select((DownloadFileTransfer c) => c.Hash).ToList(), downloadCt).ConfigureAwait(continueOnCapturedContext: false)).EnsureSuccessStatusCode();
					localTimeoutCts.Dispose();
					composite.Dispose();
					localTimeoutCts = new CancellationTokenSource();
					localTimeoutCts.CancelAfter(TimeSpan.FromSeconds(5L));
					composite = CancellationTokenSource.CreateLinkedTokenSource(downloadCt, localTimeoutCts.Token);
				}
			}
			localTimeoutCts.Dispose();
			composite.Dispose();
			base.Logger.LogDebug("Download {requestId} ready", requestId);
		}
		catch (TaskCanceledException)
		{
			try
			{
				await _orchestrator.SendRequestAsync(HttpMethod.Get, MareFiles.RequestCancelFullPath(downloadFileTransfer[0].DownloadUri, requestId)).ConfigureAwait(continueOnCapturedContext: false);
				alreadyCancelled = true;
			}
			catch
			{
			}
			throw;
		}
		finally
		{
			if (downloadCt.IsCancellationRequested && !alreadyCancelled)
			{
				try
				{
					await _orchestrator.SendRequestAsync(HttpMethod.Get, MareFiles.RequestCancelFullPath(downloadFileTransfer[0].DownloadUri, requestId)).ConfigureAwait(continueOnCapturedContext: false);
				}
				catch
				{
				}
			}
			_orchestrator.ClearDownloadRequest(requestId);
		}
	}
}
