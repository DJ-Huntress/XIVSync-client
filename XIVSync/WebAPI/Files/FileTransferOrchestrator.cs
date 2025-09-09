using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using XIVSync.MareConfiguration;
using XIVSync.MareConfiguration.Models;
using XIVSync.Services.Mediator;
using XIVSync.WebAPI.Files.Models;
using XIVSync.WebAPI.SignalR;

namespace XIVSync.WebAPI.Files;

public class FileTransferOrchestrator : DisposableMediatorSubscriberBase
{
	private readonly ConcurrentDictionary<Guid, bool> _downloadReady = new ConcurrentDictionary<Guid, bool>();

	private readonly HttpClient _httpClient;

	private readonly MareConfigService _mareConfig;

	private readonly object _semaphoreModificationLock = new object();

	private readonly TokenProvider _tokenProvider;

	private int _availableDownloadSlots;

	private int _availableUploadSlots;

	private SemaphoreSlim _downloadSemaphore;

	private SemaphoreSlim _uploadSemaphore;

	private int CurrentlyUsedDownloadSlots => _availableDownloadSlots - _downloadSemaphore.CurrentCount;

	private int CurrentlyUsedUploadSlots => _availableUploadSlots - _uploadSemaphore.CurrentCount;

	public Uri? FilesCdnUri { get; private set; }

	public List<FileTransfer> ForbiddenTransfers { get; } = new List<FileTransfer>();

	public bool IsInitialized => FilesCdnUri != null;

	public FileTransferOrchestrator(ILogger<FileTransferOrchestrator> logger, MareConfigService mareConfig, MareMediator mediator, TokenProvider tokenProvider, HttpClient httpClient)
		: base(logger, mediator)
	{
		_mareConfig = mareConfig;
		_tokenProvider = tokenProvider;
		_httpClient = httpClient;
		Version ver = Assembly.GetExecutingAssembly().GetName().Version;
		_httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MareSynchronos", ver.Major + "." + ver.Minor + "." + ver.Build));
		_availableDownloadSlots = mareConfig.Current.ParallelDownloads;
		_downloadSemaphore = new SemaphoreSlim(_availableDownloadSlots, _availableDownloadSlots);
		_availableUploadSlots = mareConfig.Current.ParallelUploads;
		_uploadSemaphore = new SemaphoreSlim(_availableUploadSlots, _availableUploadSlots);
		base.Mediator.Subscribe(this, delegate(ConnectedMessage msg)
		{
			FilesCdnUri = msg.Connection.ServerInfo.FileServerAddress;
		});
		base.Mediator.Subscribe<DisconnectedMessage>(this, delegate
		{
			FilesCdnUri = null;
		});
		base.Mediator.Subscribe(this, delegate(DownloadReadyMessage msg)
		{
			_downloadReady[msg.RequestId] = true;
		});
	}

	public void ClearDownloadRequest(Guid guid)
	{
		_downloadReady.Remove(guid, out var _);
	}

	public bool IsDownloadReady(Guid guid)
	{
		if (_downloadReady.TryGetValue(guid, out var isReady) && isReady)
		{
			return true;
		}
		return false;
	}

	public void ReleaseDownloadSlot()
	{
		try
		{
			_downloadSemaphore.Release();
			base.Mediator.Publish(new DownloadLimitChangedMessage());
		}
		catch (SemaphoreFullException)
		{
		}
	}

	public void ReleaseUploadSlot()
	{
		try
		{
			_uploadSemaphore.Release();
		}
		catch (SemaphoreFullException)
		{
		}
	}

	public async Task<HttpResponseMessage> SendRequestAsync(HttpMethod method, Uri uri, CancellationToken? ct = null, HttpCompletionOption httpCompletionOption = HttpCompletionOption.ResponseContentRead)
	{
		using HttpRequestMessage requestMessage = new HttpRequestMessage(method, uri);
		return await SendRequestInternalAsync(requestMessage, ct, httpCompletionOption).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<HttpResponseMessage> SendRequestAsync<T>(HttpMethod method, Uri uri, T content, CancellationToken ct) where T : class
	{
		using HttpRequestMessage requestMessage = new HttpRequestMessage(method, uri);
		if (!(content is ByteArrayContent))
		{
			requestMessage.Content = JsonContent.Create(content);
		}
		else
		{
			requestMessage.Content = content as ByteArrayContent;
		}
		return await SendRequestInternalAsync(requestMessage, ct).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<HttpResponseMessage> SendRequestStreamAsync(HttpMethod method, Uri uri, ProgressableStreamContent content, CancellationToken ct)
	{
		using HttpRequestMessage requestMessage = new HttpRequestMessage(method, uri);
		requestMessage.Content = content;
		return await SendRequestInternalAsync(requestMessage, ct).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task WaitForDownloadSlotAsync(CancellationToken token)
	{
		lock (_semaphoreModificationLock)
		{
			if (_availableDownloadSlots != _mareConfig.Current.ParallelDownloads && _availableDownloadSlots == _downloadSemaphore.CurrentCount)
			{
				_availableDownloadSlots = _mareConfig.Current.ParallelDownloads;
				_downloadSemaphore = new SemaphoreSlim(_availableDownloadSlots, _availableDownloadSlots);
			}
		}
		await _downloadSemaphore.WaitAsync(token).ConfigureAwait(continueOnCapturedContext: false);
		base.Mediator.Publish(new DownloadLimitChangedMessage());
	}

	public async Task WaitForUploadSlotAsync(CancellationToken token)
	{
		lock (_semaphoreModificationLock)
		{
			if (_availableUploadSlots != _mareConfig.Current.ParallelUploads && _availableUploadSlots == _uploadSemaphore.CurrentCount)
			{
				_availableUploadSlots = _mareConfig.Current.ParallelUploads;
				_uploadSemaphore = new SemaphoreSlim(_availableUploadSlots, _availableUploadSlots);
			}
		}
		await _uploadSemaphore.WaitAsync(token).ConfigureAwait(continueOnCapturedContext: false);
	}

	public long DownloadLimitPerSlot()
	{
		int limit = _mareConfig.Current.DownloadSpeedLimitInBytes;
		if (limit <= 0)
		{
			return 0L;
		}
		limit = _mareConfig.Current.DownloadSpeedType switch
		{
			DownloadSpeeds.Bps => limit, 
			DownloadSpeeds.KBps => limit * 1024, 
			DownloadSpeeds.MBps => limit * 1024 * 1024, 
			_ => limit, 
		};
		int currentUsedDlSlots = CurrentlyUsedDownloadSlots;
		int avaialble = _availableDownloadSlots;
		int currentCount = _downloadSemaphore.CurrentCount;
		int dividedLimit = limit / ((currentUsedDlSlots == 0) ? 1 : currentUsedDlSlots);
		if (dividedLimit < 0)
		{
			base.Logger.LogWarning("Calculated Bandwidth Limit is negative, returning Infinity: {value}, CurrentlyUsedDownloadSlots is {currentSlots}, DownloadSpeedLimit is {limit}, available slots: {avail}, current count: {count}", dividedLimit, currentUsedDlSlots, limit, avaialble, currentCount);
			return long.MaxValue;
		}
		return Math.Clamp(dividedLimit, 1L, long.MaxValue);
	}

	private async Task<HttpResponseMessage> SendRequestInternalAsync(HttpRequestMessage requestMessage, CancellationToken? ct = null, HttpCompletionOption httpCompletionOption = HttpCompletionOption.ResponseContentRead)
	{
		string token = await _tokenProvider.GetToken().ConfigureAwait(continueOnCapturedContext: false);
		requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		if (requestMessage.Content != null && !(requestMessage.Content is StreamContent) && !(requestMessage.Content is ByteArrayContent))
		{
			string content = await ((JsonContent)requestMessage.Content).ReadAsStringAsync().ConfigureAwait(continueOnCapturedContext: false);
			base.Logger.LogDebug("Sending {method} to {uri} (Content: {content})", requestMessage.Method, requestMessage.RequestUri, content);
		}
		else
		{
			base.Logger.LogDebug("Sending {method} to {uri}", requestMessage.Method, requestMessage.RequestUri);
		}
		try
		{
			if (ct.HasValue)
			{
				return await _httpClient.SendAsync(requestMessage, httpCompletionOption, ct.Value).ConfigureAwait(continueOnCapturedContext: false);
			}
			return await _httpClient.SendAsync(requestMessage, httpCompletionOption).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (TaskCanceledException)
		{
			throw;
		}
		catch (Exception exception)
		{
			base.Logger.LogWarning(exception, "Error during SendRequestInternal for {uri}", requestMessage.RequestUri);
			throw;
		}
	}
}
