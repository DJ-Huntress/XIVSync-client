using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using XIVSync.API.Data;
using XIVSync.Services;
using XIVSync.Services.Mediator;
using XIVSync.Utils;
using XIVSync.WebAPI;
using XIVSync.WebAPI.Files;

namespace XIVSync.PlayerData.Pairs;

public class VisibleUserDataDistributor : DisposableMediatorSubscriberBase
{
	private readonly ApiController _apiController;

	private readonly DalamudUtilService _dalamudUtil;

	private readonly FileUploadManager _fileTransferManager;

	private readonly PairManager _pairManager;

	private CharacterData? _lastCreatedData;

	private CharacterData? _uploadingCharacterData;

	private readonly List<UserData> _previouslyVisiblePlayers = new List<UserData>();

	private Task<CharacterData>? _fileUploadTask;

	private readonly HashSet<UserData> _usersToPushDataTo = new HashSet<UserData>();

	private readonly SemaphoreSlim _pushDataSemaphore = new SemaphoreSlim(1, 1);

	private readonly CancellationTokenSource _runtimeCts = new CancellationTokenSource();

	public VisibleUserDataDistributor(ILogger<VisibleUserDataDistributor> logger, ApiController apiController, DalamudUtilService dalamudUtil, PairManager pairManager, MareMediator mediator, FileUploadManager fileTransferManager)
		: base(logger, mediator)
	{
		_apiController = apiController;
		_dalamudUtil = dalamudUtil;
		_pairManager = pairManager;
		_fileTransferManager = fileTransferManager;
		base.Mediator.Subscribe<DelayedFrameworkUpdateMessage>(this, delegate
		{
			FrameworkOnUpdate();
		});
		base.Mediator.Subscribe(this, delegate(CharacterDataCreatedMessage msg)
		{
			CharacterData characterData = msg.CharacterData;
			if (_lastCreatedData == null || !string.Equals(characterData.DataHash.Value, _lastCreatedData.DataHash.Value, StringComparison.Ordinal))
			{
				_lastCreatedData = characterData;
				base.Logger.LogTrace("Storing new data hash {hash}", characterData.DataHash.Value);
				bool forced = _uploadingCharacterData == null || !_fileTransferManager.IsUploading;
				PushToAllVisibleUsers(forced);
			}
			else
			{
				base.Logger.LogTrace("Data hash {hash} equal to stored data", characterData.DataHash.Value);
			}
		});
		base.Mediator.Subscribe<ConnectedMessage>(this, delegate
		{
			PushToAllVisibleUsers();
		});
		base.Mediator.Subscribe<DisconnectedMessage>(this, delegate
		{
			_previouslyVisiblePlayers.Clear();
		});
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_runtimeCts.Cancel();
			_runtimeCts.Dispose();
		}
		base.Dispose(disposing);
	}

	private void PushToAllVisibleUsers(bool forced = false)
	{
		foreach (UserData user in _pairManager.GetVisibleUsers())
		{
			_usersToPushDataTo.Add(user);
		}
		if (_usersToPushDataTo.Count > 0)
		{
			base.Logger.LogDebug("Pushing data {hash} for {count} visible players", _lastCreatedData?.DataHash.Value ?? "UNKNOWN", _usersToPushDataTo.Count);
			PushCharacterData(forced);
		}
	}

	private void FrameworkOnUpdate()
	{
		if (!_dalamudUtil.GetIsPlayerPresent() || !_apiController.IsConnected)
		{
			return;
		}
		List<UserData> allVisibleUsers = _pairManager.GetVisibleUsers();
		List<UserData> newVisibleUsers = allVisibleUsers.Except(_previouslyVisiblePlayers).ToList();
		_previouslyVisiblePlayers.Clear();
		_previouslyVisiblePlayers.AddRange(allVisibleUsers);
		if (newVisibleUsers.Count == 0)
		{
			return;
		}
		base.Logger.LogDebug("Scheduling character data push of {data} to {users}", _lastCreatedData?.DataHash.Value ?? string.Empty, string.Join(", ", newVisibleUsers.Select((UserData k) => k.AliasOrUID)));
		foreach (UserData user in newVisibleUsers)
		{
			_usersToPushDataTo.Add(user);
		}
		PushCharacterData();
	}

	private void PushCharacterData(bool forced = false)
	{
		if (_lastCreatedData == null || _usersToPushDataTo.Count == 0)
		{
			return;
		}
		Task.Run(async delegate
		{
			forced |= _uploadingCharacterData?.DataHash != _lastCreatedData.DataHash;
			if (_fileUploadTask == null || (_fileUploadTask?.IsCompleted ?? false) || forced)
			{
				_uploadingCharacterData = _lastCreatedData.DeepClone();
				base.Logger.LogDebug("Starting UploadTask for {hash}, Reason: TaskIsNull: {task}, TaskIsCompleted: {taskCpl}, Forced: {frc}", _lastCreatedData.DataHash, _fileUploadTask == null, _fileUploadTask?.IsCompleted ?? false, forced);
				_fileUploadTask = _fileTransferManager.UploadFiles(_uploadingCharacterData, _usersToPushDataTo.ToList());
			}
			if (_fileUploadTask != null)
			{
				CharacterData dataToSend = await _fileUploadTask.ConfigureAwait(continueOnCapturedContext: false);
				await _pushDataSemaphore.WaitAsync(_runtimeCts.Token).ConfigureAwait(continueOnCapturedContext: false);
				try
				{
					if (_usersToPushDataTo.Count == 0)
					{
						return;
					}
					base.Logger.LogDebug("Pushing {data} to {users}", dataToSend.DataHash, string.Join(", ", _usersToPushDataTo.Select((UserData k) => k.AliasOrUID)));
					await _apiController.PushCharacterData(dataToSend, _usersToPushDataTo.ToList()).ConfigureAwait(continueOnCapturedContext: false);
					_usersToPushDataTo.Clear();
				}
				finally
				{
					_pushDataSemaphore.Release();
				}
			}
		});
	}
}
