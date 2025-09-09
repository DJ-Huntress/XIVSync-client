using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using XIVSync.API.Data;
using XIVSync.API.Data.Enum;
using XIVSync.API.Dto.Files;
using XIVSync.API.Routes;
using XIVSync.FileCache;
using XIVSync.MareConfiguration;
using XIVSync.Services.Mediator;
using XIVSync.Services.ServerConfiguration;
using XIVSync.UI;
using XIVSync.WebAPI.Files.Models;

namespace XIVSync.WebAPI.Files;

public sealed class FileUploadManager : DisposableMediatorSubscriberBase
{
	private readonly FileCacheManager _fileDbManager;

	private readonly MareConfigService _mareConfigService;

	private readonly FileTransferOrchestrator _orchestrator;

	private readonly ServerConfigurationManager _serverManager;

	private readonly Dictionary<string, DateTime> _verifiedUploadedHashes = new Dictionary<string, DateTime>(StringComparer.Ordinal);

	private CancellationTokenSource? _uploadCancellationTokenSource = new CancellationTokenSource();

	public List<FileTransfer> CurrentUploads { get; } = new List<FileTransfer>();

	public bool IsUploading => CurrentUploads.Count > 0;

	public FileUploadManager(ILogger<FileUploadManager> logger, MareMediator mediator, MareConfigService mareConfigService, FileTransferOrchestrator orchestrator, FileCacheManager fileDbManager, ServerConfigurationManager serverManager)
		: base(logger, mediator)
	{
		_mareConfigService = mareConfigService;
		_orchestrator = orchestrator;
		_fileDbManager = fileDbManager;
		_serverManager = serverManager;
		base.Mediator.Subscribe<DisconnectedMessage>(this, delegate
		{
			Reset();
		});
	}

	public bool CancelUpload()
	{
		if (CurrentUploads.Any())
		{
			base.Logger.LogDebug("Cancelling current upload");
			_uploadCancellationTokenSource?.Cancel();
			_uploadCancellationTokenSource?.Dispose();
			_uploadCancellationTokenSource = null;
			CurrentUploads.Clear();
			return true;
		}
		return false;
	}

	public async Task DeleteAllFiles()
	{
		if (!_orchestrator.IsInitialized)
		{
			throw new InvalidOperationException("FileTransferManager is not initialized");
		}
		await _orchestrator.SendRequestAsync(HttpMethod.Post, MareFiles.ServerFilesDeleteAllFullPath(_orchestrator.FilesCdnUri)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<List<string>> UploadFiles(List<string> hashesToUpload, IProgress<string> progress, CancellationToken? ct = null)
	{
		base.Logger.LogDebug("Trying to upload files");
		HashSet<string> filesPresentLocally = hashesToUpload.Where((string h) => _fileDbManager.GetFileCacheByHash(h) != null).ToHashSet<string>(StringComparer.Ordinal);
		List<string> locallyMissingFiles = hashesToUpload.Except<string>(filesPresentLocally, StringComparer.Ordinal).ToList();
		if (locallyMissingFiles.Any())
		{
			return locallyMissingFiles;
		}
		progress.Report($"Starting upload for {filesPresentLocally.Count} files");
		List<UploadFileDto> filesToUpload = await FilesSend(filesPresentLocally.ToList(), new List<string>(), ct ?? CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
		if (filesToUpload.Exists((UploadFileDto f) => f.IsForbidden))
		{
			return (from f in filesToUpload
				where f.IsForbidden
				select f.Hash).ToList();
		}
		progress.Report($"Starting parallel upload of {filesToUpload.Count} files...");
		int completedFiles = 0;
		await Parallel.ForEachAsync(filesToUpload, new ParallelOptions
		{
			MaxDegreeOfParallelism = _mareConfigService.Current.ParallelUploads,
			CancellationToken = (ct ?? CancellationToken.None)
		}, async delegate(UploadFileDto file, CancellationToken token)
		{
			await _orchestrator.WaitForUploadSlotAsync(token).ConfigureAwait(continueOnCapturedContext: false);
			try
			{
				base.Logger.LogDebug("[{hash}] Compressing", file);
				(string, byte[]) data = await _fileDbManager.GetCompressedFileData(file.Hash, token).ConfigureAwait(continueOnCapturedContext: false);
				base.Logger.LogDebug("[{hash}] Starting upload for {filePath}", data.Item1, _fileDbManager.GetFileCacheByHash(data.Item1).ResolvedFilepath);
				await UploadFile(data.Item2, file.Hash, postProgress: false, token).ConfigureAwait(continueOnCapturedContext: false);
				int completed = Interlocked.Increment(ref completedFiles);
				progress.Report($"Uploaded {completed}/{filesToUpload.Count} files");
				token.ThrowIfCancellationRequested();
			}
			finally
			{
				_orchestrator.ReleaseUploadSlot();
			}
		}).ConfigureAwait(continueOnCapturedContext: false);
		return new List<string>();
	}

	public async Task<CharacterData> UploadFiles(CharacterData data, List<UserData> visiblePlayers)
	{
		CancelUpload();
		_uploadCancellationTokenSource = new CancellationTokenSource();
		CancellationToken uploadToken = _uploadCancellationTokenSource.Token;
		base.Logger.LogDebug("Sending Character data {hash} to service {url}", data.DataHash.Value, _serverManager.CurrentApiUrl);
		HashSet<string> unverifiedUploads = GetUnverifiedFiles(data);
		if (unverifiedUploads.Any())
		{
			await UploadUnverifiedFiles(unverifiedUploads, visiblePlayers, uploadToken).ConfigureAwait(continueOnCapturedContext: false);
			base.Logger.LogInformation("Upload complete for {hash}", data.DataHash.Value);
		}
		foreach (KeyValuePair<ObjectKind, List<FileReplacementData>> kvp in data.FileReplacements)
		{
			data.FileReplacements[kvp.Key].RemoveAll((FileReplacementData i) => _orchestrator.ForbiddenTransfers.Exists((FileTransfer f) => string.Equals(f.Hash, i.Hash, StringComparison.OrdinalIgnoreCase)));
		}
		return data;
	}

	protected override void Dispose(bool disposing)
	{
		base.Dispose(disposing);
		Reset();
	}

	private async Task<List<UploadFileDto>> FilesSend(List<string> hashes, List<string> uids, CancellationToken ct)
	{
		if (!_orchestrator.IsInitialized)
		{
			throw new InvalidOperationException("FileTransferManager is not initialized");
		}
		FilesSendDto filesSendDto = new FilesSendDto
		{
			FileHashes = hashes,
			UIDs = uids
		};
		return (await (await _orchestrator.SendRequestAsync(HttpMethod.Post, MareFiles.ServerFilesFilesSendFullPath(_orchestrator.FilesCdnUri), filesSendDto, ct).ConfigureAwait(continueOnCapturedContext: false)).Content.ReadFromJsonAsync<List<UploadFileDto>>(ct).ConfigureAwait(continueOnCapturedContext: false)) ?? new List<UploadFileDto>();
	}

	private HashSet<string> GetUnverifiedFiles(CharacterData data)
	{
		HashSet<string> unverifiedUploadHashes = new HashSet<string>(StringComparer.Ordinal);
		foreach (string item in data.FileReplacements.SelectMany<KeyValuePair<ObjectKind, List<FileReplacementData>>, string>((KeyValuePair<ObjectKind, List<FileReplacementData>> c) => (from v in c.Value
			where string.IsNullOrEmpty(v.FileSwapPath)
			select v.Hash).Distinct<string>(StringComparer.Ordinal)).Distinct<string>(StringComparer.Ordinal).ToList())
		{
			if (!_verifiedUploadedHashes.TryGetValue(item, out var verifiedTime))
			{
				verifiedTime = DateTime.MinValue;
			}
			if (verifiedTime < DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(10L)))
			{
				base.Logger.LogTrace("Verifying {item}, last verified: {date}", item, verifiedTime);
				unverifiedUploadHashes.Add(item);
			}
		}
		return unverifiedUploadHashes;
	}

	private void Reset()
	{
		_uploadCancellationTokenSource?.Cancel();
		_uploadCancellationTokenSource?.Dispose();
		_uploadCancellationTokenSource = null;
		CurrentUploads.Clear();
		_verifiedUploadedHashes.Clear();
	}

	private async Task UploadFile(byte[] compressedFile, string fileHash, bool postProgress, CancellationToken uploadToken)
	{
		if (!_orchestrator.IsInitialized)
		{
			throw new InvalidOperationException("FileTransferManager is not initialized");
		}
		base.Logger.LogInformation("[{hash}] Uploading {size}", fileHash, UiSharedService.ByteToString(compressedFile.Length));
		if (uploadToken.IsCancellationRequested)
		{
			return;
		}
		try
		{
			await UploadFileStream(compressedFile, fileHash, _mareConfigService.Current.UseAlternativeFileUpload, postProgress, uploadToken).ConfigureAwait(continueOnCapturedContext: false);
			_verifiedUploadedHashes[fileHash] = DateTime.UtcNow;
		}
		catch (Exception ex)
		{
			if (!_mareConfigService.Current.UseAlternativeFileUpload && !(ex is OperationCanceledException))
			{
				base.Logger.LogWarning(ex, "[{hash}] Error during file upload, trying alternative file upload", fileHash);
				await UploadFileStream(compressedFile, fileHash, munged: true, postProgress, uploadToken).ConfigureAwait(continueOnCapturedContext: false);
			}
			else
			{
				base.Logger.LogWarning(ex, "[{hash}] File upload cancelled", fileHash);
			}
		}
	}

	private async Task UploadFileStream(byte[] compressedFile, string fileHash, bool munged, bool postProgress, CancellationToken uploadToken)
	{
		if (munged)
		{
			FileDownloadManager.MungeBuffer(compressedFile.AsSpan());
		}
		using MemoryStream ms = new MemoryStream(compressedFile);
		Progress<UploadProgress> prog = ((!postProgress) ? null : new Progress<UploadProgress>(delegate(UploadProgress uploadProgress)
		{
			try
			{
				CurrentUploads.Single((FileTransfer f) => string.Equals(f.Hash, fileHash, StringComparison.Ordinal)).Transferred = uploadProgress.Uploaded;
			}
			catch (Exception exception)
			{
				base.Logger.LogWarning(exception, "[{hash}] Could not set upload progress", fileHash);
			}
		}));
		ProgressableStreamContent streamContent = new ProgressableStreamContent(ms, prog);
		streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
		HttpResponseMessage response = (munged ? (await _orchestrator.SendRequestStreamAsync(HttpMethod.Post, MareFiles.ServerFilesUploadMunged(_orchestrator.FilesCdnUri, fileHash), streamContent, uploadToken).ConfigureAwait(continueOnCapturedContext: false)) : (await _orchestrator.SendRequestStreamAsync(HttpMethod.Post, MareFiles.ServerFilesUploadFullPath(_orchestrator.FilesCdnUri, fileHash), streamContent, uploadToken).ConfigureAwait(continueOnCapturedContext: false)));
		base.Logger.LogDebug("[{hash}] Upload Status: {status}", fileHash, response.StatusCode);
	}

	private async Task UploadUnverifiedFiles(HashSet<string> unverifiedUploadHashes, List<UserData> visiblePlayers, CancellationToken uploadToken)
	{
		unverifiedUploadHashes = unverifiedUploadHashes.Where((string h) => _fileDbManager.GetFileCacheByHash(h) != null).ToHashSet<string>(StringComparer.Ordinal);
		base.Logger.LogDebug("Verifying {count} files", unverifiedUploadHashes.Count);
		List<UploadFileDto> filesToUpload = await FilesSend(unverifiedUploadHashes.ToList(), visiblePlayers.Select((UserData p) => p.UID).ToList(), uploadToken).ConfigureAwait(continueOnCapturedContext: false);
		foreach (UploadFileDto file in filesToUpload.Where((UploadFileDto f) => !f.IsForbidden).DistinctBy((UploadFileDto f) => f.Hash))
		{
			try
			{
				CurrentUploads.Add(new UploadFileTransfer(file)
				{
					Total = new FileInfo(_fileDbManager.GetFileCacheByHash(file.Hash).ResolvedFilepath).Length
				});
			}
			catch (Exception exception)
			{
				base.Logger.LogWarning(exception, "Tried to request file {hash} but file was not present", file.Hash);
			}
		}
		foreach (UploadFileDto file2 in filesToUpload.Where((UploadFileDto c) => c.IsForbidden))
		{
			if (_orchestrator.ForbiddenTransfers.TrueForAll((FileTransfer f) => !string.Equals(f.Hash, file2.Hash, StringComparison.Ordinal)))
			{
				_orchestrator.ForbiddenTransfers.Add(new UploadFileTransfer(file2)
				{
					LocalFile = (_fileDbManager.GetFileCacheByHash(file2.Hash)?.ResolvedFilepath ?? string.Empty)
				});
			}
			_verifiedUploadedHashes[file2.Hash] = DateTime.UtcNow;
		}
		long totalSize = CurrentUploads.Sum((FileTransfer c) => c.Total);
		base.Logger.LogDebug("Compressing and uploading files in parallel");
		List<FileTransfer> filesToUploadInternal = CurrentUploads.Where((FileTransfer f) => f.CanBeTransferred && !f.IsTransferred).ToList();
		if (filesToUploadInternal.Any())
		{
			await Parallel.ForEachAsync(filesToUploadInternal, new ParallelOptions
			{
				MaxDegreeOfParallelism = _mareConfigService.Current.ParallelUploads,
				CancellationToken = uploadToken
			}, async delegate(FileTransfer fileTransfer, CancellationToken token)
			{
				await _orchestrator.WaitForUploadSlotAsync(token).ConfigureAwait(continueOnCapturedContext: false);
				try
				{
					base.Logger.LogDebug("[{hash}] Compressing", fileTransfer);
					(string, byte[]) data = await _fileDbManager.GetCompressedFileData(fileTransfer.Hash, token).ConfigureAwait(continueOnCapturedContext: false);
					CurrentUploads.Single((FileTransfer e) => string.Equals(e.Hash, data.Item1, StringComparison.Ordinal)).Total = data.Item2.Length;
					base.Logger.LogDebug("[{hash}] Starting upload for {filePath}", data.Item1, _fileDbManager.GetFileCacheByHash(data.Item1).ResolvedFilepath);
					await UploadFile(data.Item2, fileTransfer.Hash, postProgress: true, token).ConfigureAwait(continueOnCapturedContext: false);
					token.ThrowIfCancellationRequested();
				}
				finally
				{
					_orchestrator.ReleaseUploadSlot();
				}
			}).ConfigureAwait(continueOnCapturedContext: false);
			long compressedSize = CurrentUploads.Sum((FileTransfer c) => c.Total);
			base.Logger.LogDebug("Upload complete, compressed {size} to {compressed}", UiSharedService.ByteToString(totalSize), UiSharedService.ByteToString(compressedSize));
		}
		foreach (string file3 in unverifiedUploadHashes.Where((string c) => !CurrentUploads.Exists((FileTransfer u) => string.Equals(u.Hash, c, StringComparison.Ordinal))))
		{
			_verifiedUploadedHashes[file3] = DateTime.UtcNow;
		}
		CurrentUploads.Clear();
	}
}
