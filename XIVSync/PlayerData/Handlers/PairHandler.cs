using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XIVSync.API.Data;
using XIVSync.API.Data.Enum;
using XIVSync.FileCache;
using XIVSync.Interop.Ipc;
using XIVSync.PlayerData.Factories;
using XIVSync.PlayerData.Pairs;
using XIVSync.Services;
using XIVSync.Services.Events;
using XIVSync.Services.Mediator;
using XIVSync.Services.ServerConfiguration;
using XIVSync.Utils;
using XIVSync.WebAPI.Files;
using XIVSync.WebAPI.Files.Models;

namespace XIVSync.PlayerData.Handlers;

public sealed class PairHandler : DisposableMediatorSubscriberBase
{
	private sealed record CombatData(Guid ApplicationId, CharacterData CharacterData, bool Forced);

	private readonly DalamudUtilService _dalamudUtil;

	private readonly FileDownloadManager _downloadManager;

	private readonly FileCacheManager _fileDbManager;

	private readonly GameObjectHandlerFactory _gameObjectHandlerFactory;

	private readonly IpcManager _ipcManager;

	private readonly IHostApplicationLifetime _lifetime;

	private readonly PlayerPerformanceService _playerPerformanceService;

	private readonly ServerConfigurationManager _serverConfigManager;

	private readonly PluginWarningNotificationService _pluginWarningNotificationManager;

	private CancellationTokenSource? _applicationCancellationTokenSource = new CancellationTokenSource();

	private Guid _applicationId;

	private Task? _applicationTask;

	private CharacterData? _cachedData;

	private GameObjectHandler? _charaHandler;

	private readonly Dictionary<XIVSync.API.Data.Enum.ObjectKind, Guid?> _customizeIds = new Dictionary<XIVSync.API.Data.Enum.ObjectKind, Guid?>();

	private CombatData? _dataReceivedInDowntime;

	private CancellationTokenSource? _downloadCancellationTokenSource = new CancellationTokenSource();

	private bool _forceApplyMods;

	private bool _isVisible;

	private Guid _penumbraCollection;

	private bool _redrawOnNextApplication;

	private Task? _pairDownloadTask;

	public bool IsVisible
	{
		get
		{
			return _isVisible;
		}
		private set
		{
			if (_isVisible != value)
			{
				_isVisible = value;
				string text = "User Visibility Changed, now: " + (_isVisible ? "Is Visible" : "Is not Visible");
				base.Mediator.Publish(new EventMessage(new Event(PlayerName, Pair.UserData, "PairHandler", EventSeverity.Informational, text)));
				base.Mediator.Publish(new RefreshUiMessage());
			}
		}
	}

	public long LastAppliedDataBytes { get; private set; }

	public Pair Pair { get; private set; }

	public nint PlayerCharacter => _charaHandler?.Address ?? IntPtr.Zero;

	public unsafe uint PlayerCharacterId
	{
		get
		{
			if ((_charaHandler?.Address ?? IntPtr.Zero) != IntPtr.Zero)
			{
				return ((GameObject*)_charaHandler.Address)->EntityId;
			}
			return uint.MaxValue;
		}
	}

	public string? PlayerName { get; private set; }

	public string PlayerNameHash => Pair.Ident;

	public PairHandler(ILogger<PairHandler> logger, Pair pair, GameObjectHandlerFactory gameObjectHandlerFactory, IpcManager ipcManager, FileDownloadManager transferManager, PluginWarningNotificationService pluginWarningNotificationManager, DalamudUtilService dalamudUtil, IHostApplicationLifetime lifetime, FileCacheManager fileDbManager, MareMediator mediator, PlayerPerformanceService playerPerformanceService, ServerConfigurationManager serverConfigManager) : base(logger, mediator)
	{

		PairHandler pairHandler = this;
		Pair = pair;
		_gameObjectHandlerFactory = gameObjectHandlerFactory;
		_ipcManager = ipcManager;
		_downloadManager = transferManager;
		_pluginWarningNotificationManager = pluginWarningNotificationManager;
		_dalamudUtil = dalamudUtil;
		_lifetime = lifetime;
		_fileDbManager = fileDbManager;
		_playerPerformanceService = playerPerformanceService;
		_serverConfigManager = serverConfigManager;
		_penumbraCollection = _ipcManager.Penumbra.CreateTemporaryCollectionAsync(logger, Pair.UserData.UID).ConfigureAwait(continueOnCapturedContext: false).GetAwaiter()
			.GetResult();
		base.Mediator.Subscribe<FrameworkUpdateMessage>(this, delegate
		{
			pairHandler.FrameworkUpdate();
		});
		base.Mediator.Subscribe<ZoneSwitchStartMessage>(this, delegate
		{
			pairHandler._downloadCancellationTokenSource?.CancelDispose();
			pairHandler._charaHandler?.Invalidate();
			pairHandler.IsVisible = false;
		});
		base.Mediator.Subscribe<PenumbraInitializedMessage>(this, delegate
		{
			pairHandler._penumbraCollection = pairHandler._ipcManager.Penumbra.CreateTemporaryCollectionAsync(logger, pairHandler.Pair.UserData.UID).ConfigureAwait(continueOnCapturedContext: false).GetAwaiter()
				.GetResult();
			if (!pairHandler.IsVisible && pairHandler._charaHandler != null)
			{
				pairHandler.PlayerName = string.Empty;
				pairHandler._charaHandler.Dispose();
				pairHandler._charaHandler = null;
			}
		});
		base.Mediator.Subscribe(this, delegate(ClassJobChangedMessage msg)
		{
			if (msg.GameObjectHandler == pairHandler._charaHandler)
			{
				pairHandler._redrawOnNextApplication = true;
			}
		});
		base.Mediator.Subscribe<CombatOrPerformanceEndMessage>(this, delegate
		{
			if (pairHandler.IsVisible && pairHandler._dataReceivedInDowntime != null)
			{
				pairHandler.ApplyCharacterData(pairHandler._dataReceivedInDowntime.ApplicationId, pairHandler._dataReceivedInDowntime.CharacterData, pairHandler._dataReceivedInDowntime.Forced);
				pairHandler._dataReceivedInDowntime = null;
			}
		});
		base.Mediator.Subscribe<CombatOrPerformanceStartMessage>(this, delegate
		{
			pairHandler._dataReceivedInDowntime = null;
			pairHandler._downloadCancellationTokenSource = pairHandler._downloadCancellationTokenSource?.CancelRecreate();
			pairHandler._applicationCancellationTokenSource = pairHandler._applicationCancellationTokenSource?.CancelRecreate();
		});
		LastAppliedDataBytes = -1L;
	}

	public void ApplyCharacterData(Guid applicationBase, CharacterData characterData, bool forceApplyCustomization = false)
	{
		if (_dalamudUtil.IsInCombatOrPerforming)
		{
			base.Mediator.Publish(new EventMessage(new Event(PlayerName, Pair.UserData, "PairHandler", EventSeverity.Warning, "Cannot apply character data: you are in combat or performing music, deferring application")));
			base.Logger.LogDebug("[BASE-{appBase}] Received data but player is in combat or performing", applicationBase);
			_dataReceivedInDowntime = new CombatData(applicationBase, characterData, forceApplyCustomization);
			SetUploading(isUploading: false);
			return;
		}
		if (_charaHandler == null || PlayerCharacter == IntPtr.Zero)
		{
			base.Mediator.Publish(new EventMessage(new Event(PlayerName, Pair.UserData, "PairHandler", EventSeverity.Warning, "Cannot apply character data: Receiving Player is in an invalid state, deferring application")));
			base.Logger.LogDebug("[BASE-{appBase}] Received data but player was in invalid state, charaHandlerIsNull: {charaIsNull}, playerPointerIsNull: {ptrIsNull}", applicationBase, _charaHandler == null, PlayerCharacter == IntPtr.Zero);
			bool hasDiffMods = characterData.CheckUpdatedData(applicationBase, _cachedData, base.Logger, this, forceApplyCustomization, forceApplyMods: false).Any<KeyValuePair<XIVSync.API.Data.Enum.ObjectKind, HashSet<PlayerChanges>>>((KeyValuePair<XIVSync.API.Data.Enum.ObjectKind, HashSet<PlayerChanges>> p) => p.Value.Contains(PlayerChanges.ModManip) || p.Value.Contains(PlayerChanges.ModFiles));
			_forceApplyMods = hasDiffMods || _forceApplyMods || (PlayerCharacter == IntPtr.Zero && _cachedData == null);
			_cachedData = characterData;
			base.Logger.LogDebug("[BASE-{appBase}] Setting data: {hash}, forceApplyMods: {force}", applicationBase, _cachedData.DataHash.Value, _forceApplyMods);
			return;
		}
		SetUploading(isUploading: false);
		base.Logger.LogDebug("[BASE-{appbase}] Applying data for {player}, forceApplyCustomization: {forced}, forceApplyMods: {forceMods}", applicationBase, this, forceApplyCustomization, _forceApplyMods);
		base.Logger.LogDebug("[BASE-{appbase}] Hash for data is {newHash}, current cache hash is {oldHash}", applicationBase, characterData.DataHash.Value, _cachedData?.DataHash.Value ?? "NODATA");
		if (string.Equals(characterData.DataHash.Value, _cachedData?.DataHash.Value ?? string.Empty, StringComparison.Ordinal) && !forceApplyCustomization)
		{
			return;
		}
		if (_dalamudUtil.IsInCutscene || _dalamudUtil.IsInGpose || !_ipcManager.Penumbra.APIAvailable || !_ipcManager.Glamourer.APIAvailable)
		{
			base.Mediator.Publish(new EventMessage(new Event(PlayerName, Pair.UserData, "PairHandler", EventSeverity.Warning, "Cannot apply character data: you are in GPose, a Cutscene or Penumbra/Glamourer is not available")));
			base.Logger.LogInformation("[BASE-{appbase}] Application of data for {player} while in cutscene/gpose or Penumbra/Glamourer unavailable, returning", applicationBase, this);
			return;
		}
		base.Mediator.Publish(new EventMessage(new Event(PlayerName, Pair.UserData, "PairHandler", EventSeverity.Informational, "Applying Character Data")));
		_forceApplyMods |= forceApplyCustomization;
		Dictionary<XIVSync.API.Data.Enum.ObjectKind, HashSet<PlayerChanges>> charaDataToUpdate = characterData.CheckUpdatedData(applicationBase, _cachedData?.DeepClone() ?? new CharacterData(), base.Logger, this, forceApplyCustomization, _forceApplyMods);
		if (_charaHandler != null && _forceApplyMods)
		{
			_forceApplyMods = false;
		}
		if (_redrawOnNextApplication && charaDataToUpdate.TryGetValue(XIVSync.API.Data.Enum.ObjectKind.Player, out var player))
		{
			player.Add(PlayerChanges.ForcedRedraw);
			_redrawOnNextApplication = false;
		}
		if (charaDataToUpdate.TryGetValue(XIVSync.API.Data.Enum.ObjectKind.Player, out var playerChanges))
		{
			_pluginWarningNotificationManager.NotifyForMissingPlugins(Pair.UserData, PlayerName, playerChanges);
		}
		base.Logger.LogDebug("[BASE-{appbase}] Downloading and applying character for {name}", applicationBase, this);
		DownloadAndApplyCharacter(applicationBase, characterData.DeepClone(), charaDataToUpdate);
	}

	public override string ToString()
	{
		if (Pair != null)
		{
			return Pair.UserData.AliasOrUID + ":" + PlayerName + ":" + ((PlayerCharacter != IntPtr.Zero) ? "HasChar" : "NoChar");
		}
		return base.ToString() ?? string.Empty;
	}

	internal void SetUploading(bool isUploading = true)
	{
		base.Logger.LogTrace("Setting {this} uploading {uploading}", this, isUploading);
		if (_charaHandler != null)
		{
			base.Mediator.Publish(new PlayerUploadingMessage(_charaHandler, isUploading));
		}
	}

	protected override void Dispose(bool disposing)
	{
		base.Dispose(disposing);
		SetUploading(isUploading: false);
		string name = PlayerName;
		base.Logger.LogDebug("Disposing {name} ({user})", name, Pair);
		try
		{
			Guid applicationId = Guid.NewGuid();
			_applicationCancellationTokenSource?.CancelDispose();
			_applicationCancellationTokenSource = null;
			_downloadCancellationTokenSource?.CancelDispose();
			_downloadCancellationTokenSource = null;
			_downloadManager.Dispose();
			_charaHandler?.Dispose();
			_charaHandler = null;
			if (!string.IsNullOrEmpty(name))
			{
				base.Mediator.Publish(new EventMessage(new Event(name, Pair.UserData, "PairHandler", EventSeverity.Informational, "Disposing User")));
			}
			if (_lifetime.ApplicationStopping.IsCancellationRequested)
			{
				return;
			}
			DalamudUtilService dalamudUtil = _dalamudUtil;
			if (dalamudUtil == null || dalamudUtil.IsZoning || dalamudUtil.IsInCutscene || string.IsNullOrEmpty(name))
			{
				return;
			}
			base.Logger.LogTrace("[{applicationId}] Restoring state for {name} ({OnlineUser})", applicationId, name, Pair.UserPair);
			base.Logger.LogDebug("[{applicationId}] Removing Temp Collection for {name} ({user})", applicationId, name, Pair.UserPair);
			_ipcManager.Penumbra.RemoveTemporaryCollectionAsync(base.Logger, applicationId, _penumbraCollection).GetAwaiter().GetResult();
			if (!IsVisible)
			{
				base.Logger.LogDebug("[{applicationId}] Restoring Glamourer for {name} ({user})", applicationId, name, Pair.UserPair);
				_ipcManager.Glamourer.RevertByNameAsync(base.Logger, name, applicationId).GetAwaiter().GetResult();
				return;
			}
			using CancellationTokenSource cts = new CancellationTokenSource();
			cts.CancelAfter(TimeSpan.FromSeconds(60L));
			base.Logger.LogInformation("[{applicationId}] CachedData is null {isNull}, contains things: {contains}", applicationId, _cachedData == null, _cachedData?.FileReplacements.Any() ?? false);
			foreach (KeyValuePair<XIVSync.API.Data.Enum.ObjectKind, List<FileReplacementData>> item in _cachedData?.FileReplacements ?? new Dictionary<XIVSync.API.Data.Enum.ObjectKind, List<FileReplacementData>>())
			{
				try
				{
					RevertCustomizationDataAsync(item.Key, name, applicationId, cts.Token).GetAwaiter().GetResult();
				}
				catch (InvalidOperationException ex)
				{
					base.Logger.LogWarning(ex, "Failed disposing player (not present anymore?)");
					break;
				}
			}
		}
		catch (Exception ex)
		{
			base.Logger.LogWarning(ex, "Error on disposal of {name}", name);
		}
		finally
		{
			PlayerName = null;
			_cachedData = null;
			base.Logger.LogDebug("Disposing {name} complete", name);
		}
	}

	private async Task ApplyCustomizationDataAsync(Guid applicationId, KeyValuePair<XIVSync.API.Data.Enum.ObjectKind, HashSet<PlayerChanges>> changes, CharacterData charaData, CancellationToken token)
	{
		if (PlayerCharacter == IntPtr.Zero)
		{
			return;
		}
		nint ptr = PlayerCharacter;
		GameObjectHandler handler = changes.Key switch
		{
			XIVSync.API.Data.Enum.ObjectKind.Player => _charaHandler, 
			XIVSync.API.Data.Enum.ObjectKind.Companion => await _gameObjectHandlerFactory.Create(changes.Key, () => _dalamudUtil.GetCompanionPtr(ptr)).ConfigureAwait(continueOnCapturedContext: false), 
			XIVSync.API.Data.Enum.ObjectKind.MinionOrMount => await _gameObjectHandlerFactory.Create(changes.Key, () => _dalamudUtil.GetMinionOrMountPtr(ptr)).ConfigureAwait(continueOnCapturedContext: false), 
			XIVSync.API.Data.Enum.ObjectKind.Pet => await _gameObjectHandlerFactory.Create(changes.Key, () => _dalamudUtil.GetPetPtr(ptr)).ConfigureAwait(continueOnCapturedContext: false), 
			_ => throw new NotSupportedException("ObjectKind not supported: " + changes.Key), 
		};
		try
		{
			if (handler.Address == IntPtr.Zero)
			{
				return;
			}
			base.Logger.LogDebug("[{applicationId}] Applying Customization Data for {handler}", applicationId, handler);
			await _dalamudUtil.WaitWhileCharacterIsDrawing(base.Logger, handler, applicationId, 30000, token).ConfigureAwait(continueOnCapturedContext: false);
			token.ThrowIfCancellationRequested();
			foreach (PlayerChanges change in changes.Value.OrderBy((PlayerChanges p) => (int)p))
			{
				base.Logger.LogDebug("[{applicationId}] Processing {change} for {handler}", applicationId, change, handler);
				switch (change)
				{
				case PlayerChanges.Customize:
				{
					Guid? customizeId;
					if (charaData.CustomizePlusData.TryGetValue(changes.Key, out string customizePlusData))
					{
						Dictionary<XIVSync.API.Data.Enum.ObjectKind, Guid?> customizeIds = _customizeIds;
						XIVSync.API.Data.Enum.ObjectKind key = changes.Key;
						customizeIds[key] = await _ipcManager.CustomizePlus.SetBodyScaleAsync(handler.Address, customizePlusData).ConfigureAwait(continueOnCapturedContext: false);
					}
					else if (_customizeIds.TryGetValue(changes.Key, out customizeId))
					{
						await _ipcManager.CustomizePlus.RevertByIdAsync(customizeId).ConfigureAwait(continueOnCapturedContext: false);
						_customizeIds.Remove(changes.Key);
					}
					break;
				}
				case PlayerChanges.Heels:
					await _ipcManager.Heels.SetOffsetForPlayerAsync(handler.Address, charaData.HeelsData).ConfigureAwait(continueOnCapturedContext: false);
					break;
				case PlayerChanges.Honorific:
					await _ipcManager.Honorific.SetTitleAsync(handler.Address, charaData.HonorificData).ConfigureAwait(continueOnCapturedContext: false);
					break;
				case PlayerChanges.Glamourer:
				{
					if (charaData.GlamourerData.TryGetValue(changes.Key, out string glamourerData))
					{
						await _ipcManager.Glamourer.ApplyAllAsync(base.Logger, handler, glamourerData, applicationId, token).ConfigureAwait(continueOnCapturedContext: false);
					}
					break;
				}
				case PlayerChanges.Moodles:
					await _ipcManager.Moodles.SetStatusAsync(handler.Address, charaData.MoodlesData).ConfigureAwait(continueOnCapturedContext: false);
					break;
				case PlayerChanges.PetNames:
					await _ipcManager.PetNames.SetPlayerData(handler.Address, charaData.PetNamesData).ConfigureAwait(continueOnCapturedContext: false);
					break;
				case PlayerChanges.ForcedRedraw:
					await _ipcManager.Penumbra.RedrawAsync(base.Logger, handler, applicationId, token).ConfigureAwait(continueOnCapturedContext: false);
					break;
				}
				token.ThrowIfCancellationRequested();
			}
		}
		finally
		{
			if (handler != _charaHandler)
			{
				handler.Dispose();
			}
		}
	}

	private void DownloadAndApplyCharacter(Guid applicationBase, CharacterData charaData, Dictionary<XIVSync.API.Data.Enum.ObjectKind, HashSet<PlayerChanges>> updatedData)
	{
		if (!updatedData.Any())
		{
			base.Logger.LogDebug("[BASE-{appBase}] Nothing to update for {obj}", applicationBase, this);
			return;
		}
		bool updateModdedPaths = updatedData.Values.Any((HashSet<PlayerChanges> v) => v.Any((PlayerChanges p) => p == PlayerChanges.ModFiles));
		bool updateManip = updatedData.Values.Any((HashSet<PlayerChanges> v) => v.Any((PlayerChanges p) => p == PlayerChanges.ModManip));
		_downloadCancellationTokenSource = _downloadCancellationTokenSource?.CancelRecreate() ?? new CancellationTokenSource();
		CancellationToken downloadToken = _downloadCancellationTokenSource.Token;
		DownloadAndApplyCharacterAsync(applicationBase, charaData, updatedData, updateModdedPaths, updateManip, downloadToken).ConfigureAwait(continueOnCapturedContext: false);
	}

	private async Task DownloadAndApplyCharacterAsync(Guid applicationBase, CharacterData charaData, Dictionary<XIVSync.API.Data.Enum.ObjectKind, HashSet<PlayerChanges>> updatedData, bool updateModdedPaths, bool updateManip, CancellationToken downloadToken)
	{
		Dictionary<(string GamePath, string? Hash), string> moddedPaths = new Dictionary<(string, string), string>();
		if (updateModdedPaths)
		{
			int attempts = 0;
			List<FileReplacementData> toDownloadReplacements = TryCalculateModdedDictionary(applicationBase, charaData, out moddedPaths, downloadToken);
			while (toDownloadReplacements.Count > 0 && attempts++ <= 10 && !downloadToken.IsCancellationRequested)
			{
				if (_pairDownloadTask != null && !_pairDownloadTask.IsCompleted)
				{
					base.Logger.LogDebug("[BASE-{appBase}] Finishing prior running download task for player {name}, {kind}", applicationBase, PlayerName, updatedData);
					await _pairDownloadTask.ConfigureAwait(continueOnCapturedContext: false);
				}
				base.Logger.LogDebug("[BASE-{appBase}] Downloading missing files for player {name}, {kind}", applicationBase, PlayerName, updatedData);
				base.Mediator.Publish(new EventMessage(new Event(PlayerName, Pair.UserData, "PairHandler", EventSeverity.Informational, $"Starting download for {toDownloadReplacements.Count} files")));
				List<DownloadFileTransfer> toDownloadFiles = await _downloadManager.InitiateDownloadList(_charaHandler, toDownloadReplacements, downloadToken).ConfigureAwait(continueOnCapturedContext: false);
				if (!_playerPerformanceService.ComputeAndAutoPauseOnVRAMUsageThresholds(this, charaData, toDownloadFiles))
				{
					_downloadManager.ClearDownload();
					return;
				}
				_pairDownloadTask = Task.Run(async delegate
				{
					await _downloadManager.DownloadFiles(_charaHandler, toDownloadReplacements, downloadToken).ConfigureAwait(continueOnCapturedContext: false);
				});
				await _pairDownloadTask.ConfigureAwait(continueOnCapturedContext: false);
				if (downloadToken.IsCancellationRequested)
				{
					base.Logger.LogTrace("[BASE-{appBase}] Detected cancellation", applicationBase);
					return;
				}
				toDownloadReplacements = TryCalculateModdedDictionary(applicationBase, charaData, out moddedPaths, downloadToken);
				if (toDownloadReplacements.TrueForAll(delegate(FileReplacementData c)
				{
					return _downloadManager.ForbiddenTransfers.Exists((FileTransfer f) => string.Equals(f.Hash, c.Hash, StringComparison.Ordinal));
				}))
				{
					break;
				}
				await Task.Delay(TimeSpan.FromSeconds(2L), downloadToken).ConfigureAwait(continueOnCapturedContext: false);
			}
			if (!(await _playerPerformanceService.CheckBothThresholds(this, charaData).ConfigureAwait(continueOnCapturedContext: false)))
			{
				return;
			}
		}
		downloadToken.ThrowIfCancellationRequested();
		CancellationToken? appToken = _applicationCancellationTokenSource?.Token;
		while (true)
		{
			Task? applicationTask = _applicationTask;
			if (applicationTask == null || applicationTask.IsCompleted || downloadToken.IsCancellationRequested)
			{
				break;
			}
			if (!appToken.HasValue || appToken.GetValueOrDefault().IsCancellationRequested)
			{
				break;
			}
			base.Logger.LogDebug("[BASE-{appBase}] Waiting for current data application (Id: {id}) for player ({handler}) to finish", applicationBase, _applicationId, PlayerName);
			await Task.Delay(250).ConfigureAwait(continueOnCapturedContext: false);
		}
		if (!downloadToken.IsCancellationRequested && !(appToken?.IsCancellationRequested ?? false))
		{
			_applicationCancellationTokenSource = _applicationCancellationTokenSource.CancelRecreate() ?? new CancellationTokenSource();
			CancellationToken token = _applicationCancellationTokenSource.Token;
			_applicationTask = ApplyCharacterDataAsync(applicationBase, charaData, updatedData, updateModdedPaths, updateManip, moddedPaths, token);
		}
	}

	private async Task ApplyCharacterDataAsync(Guid applicationBase, CharacterData charaData, Dictionary<XIVSync.API.Data.Enum.ObjectKind, HashSet<PlayerChanges>> updatedData, bool updateModdedPaths, bool updateManip, Dictionary<(string GamePath, string? Hash), string> moddedPaths, CancellationToken token)
	{
		_ = 5;
		try
		{
			_applicationId = Guid.NewGuid();
			base.Logger.LogDebug("[BASE-{applicationId}] Starting application task for {this}: {appId}", applicationBase, this, _applicationId);
			base.Logger.LogDebug("[{applicationId}] Waiting for initial draw for for {handler}", _applicationId, _charaHandler);
			await _dalamudUtil.WaitWhileCharacterIsDrawing(base.Logger, _charaHandler, _applicationId, 30000, token).ConfigureAwait(continueOnCapturedContext: false);
			token.ThrowIfCancellationRequested();
			if (updateModdedPaths)
			{
				ushort objIndex = await _dalamudUtil.RunOnFrameworkThread(() => _charaHandler.GetGameObject().ObjectIndex, "ApplyCharacterDataAsync", "C:\\Users\\Owner\\sync_client2\\XIVSync\\PlayerData\\Handlers\\PairHandler.cs", 479).ConfigureAwait(continueOnCapturedContext: false);
				await _ipcManager.Penumbra.AssignTemporaryCollectionAsync(base.Logger, _penumbraCollection, objIndex).ConfigureAwait(continueOnCapturedContext: false);
				await _ipcManager.Penumbra.SetTemporaryModsAsync(base.Logger, _applicationId, _penumbraCollection, moddedPaths.ToDictionary<KeyValuePair<(string, string), string>, string, string>((KeyValuePair<(string GamePath, string Hash), string> k) => k.Key.GamePath, (KeyValuePair<(string GamePath, string Hash), string> k) => k.Value, StringComparer.Ordinal)).ConfigureAwait(continueOnCapturedContext: false);
				LastAppliedDataBytes = -1L;
				foreach (FileInfo path in from v in moddedPaths.Values.Distinct<string>(StringComparer.OrdinalIgnoreCase)
					select new FileInfo(v) into p
					where p.Exists
					select p)
				{
					if (LastAppliedDataBytes == -1)
					{
						LastAppliedDataBytes = 0L;
					}
					LastAppliedDataBytes += path.Length;
				}
			}
			if (updateManip)
			{
				await _ipcManager.Penumbra.SetManipulationDataAsync(base.Logger, _applicationId, _penumbraCollection, charaData.ManipulationData).ConfigureAwait(continueOnCapturedContext: false);
			}
			token.ThrowIfCancellationRequested();
			foreach (KeyValuePair<XIVSync.API.Data.Enum.ObjectKind, HashSet<PlayerChanges>> kind in updatedData)
			{
				await ApplyCustomizationDataAsync(_applicationId, kind, charaData, token).ConfigureAwait(continueOnCapturedContext: false);
				token.ThrowIfCancellationRequested();
			}
			_cachedData = charaData;
			base.Logger.LogDebug("[{applicationId}] Application finished", _applicationId);
		}
		catch (Exception ex)
		{
			if (ex is AggregateException aggr && aggr.InnerExceptions.Any((Exception e) => e is ArgumentNullException))
			{
				IsVisible = false;
				_forceApplyMods = true;
				_cachedData = charaData;
				base.Logger.LogDebug("[{applicationId}] Cancelled, player turned null during application", _applicationId);
			}
			else
			{
				base.Logger.LogWarning(ex, "[{applicationId}] Cancelled", _applicationId);
			}
		}
	}

	private void FrameworkUpdate()
	{
		if (string.IsNullOrEmpty(PlayerName))
		{
			(string Name, nint Address) pc = _dalamudUtil.FindPlayerByNameHash(Pair.Ident);
			(string, nint) tuple = pc;
			if (tuple.Item1 == null && tuple.Item2 == 0)
			{
				return;
			}
			base.Logger.LogDebug("One-Time Initializing {this}", this);
			Initialize(pc.Name);
			base.Logger.LogDebug("One-Time Initialized {this}", this);
			base.Mediator.Publish(new EventMessage(new Event(PlayerName, Pair.UserData, "PairHandler", EventSeverity.Informational, "Initializing User For Character " + pc.Name)));
		}
		if (_charaHandler?.Address != IntPtr.Zero && !IsVisible)
		{
			Guid appData = Guid.NewGuid();
			IsVisible = true;
			if (_cachedData != null)
			{
				base.Logger.LogTrace("[BASE-{appBase}] {this} visibility changed, now: {visi}, cached data exists", appData, this, IsVisible);
				Task.Run(delegate
				{
					ApplyCharacterData(appData, _cachedData, forceApplyCustomization: true);
				});
			}
			else
			{
				base.Logger.LogTrace("{this} visibility changed, now: {visi}, no cached data exists", this, IsVisible);
			}
		}
		else if (_charaHandler?.Address == IntPtr.Zero && IsVisible)
		{
			IsVisible = false;
			_charaHandler.Invalidate();
			_downloadCancellationTokenSource?.CancelDispose();
			_downloadCancellationTokenSource = null;
			base.Logger.LogTrace("{this} visibility changed, now: {visi}", this, IsVisible);
		}
	}

	private void Initialize(string name)
	{
		PlayerName = name;
		_charaHandler = _gameObjectHandlerFactory.Create(XIVSync.API.Data.Enum.ObjectKind.Player, () => _dalamudUtil.GetPlayerCharacterFromCachedTableByIdent(Pair.Ident)).GetAwaiter().GetResult();
		_serverConfigManager.AutoPopulateNoteForUid(Pair.UserData.UID, name);
		base.Mediator.Subscribe<HonorificReadyMessage>(this, async delegate
		{
			if (!string.IsNullOrEmpty(_cachedData?.HonorificData))
			{
				base.Logger.LogTrace("Reapplying Honorific data for {this}", this);
				await _ipcManager.Honorific.SetTitleAsync(PlayerCharacter, _cachedData.HonorificData).ConfigureAwait(continueOnCapturedContext: false);
			}
		});
		base.Mediator.Subscribe<PetNamesReadyMessage>(this, async delegate
		{
			if (!string.IsNullOrEmpty(_cachedData?.PetNamesData))
			{
				base.Logger.LogTrace("Reapplying Pet Names data for {this}", this);
				await _ipcManager.PetNames.SetPlayerData(PlayerCharacter, _cachedData.PetNamesData).ConfigureAwait(continueOnCapturedContext: false);
			}
		});
		_ipcManager.Penumbra.AssignTemporaryCollectionAsync(base.Logger, _penumbraCollection, _charaHandler.GetGameObject().ObjectIndex).GetAwaiter().GetResult();
	}

	private async Task RevertCustomizationDataAsync(XIVSync.API.Data.Enum.ObjectKind objectKind, string name, Guid applicationId, CancellationToken cancelToken)
	{
		nint address = _dalamudUtil.GetPlayerCharacterFromCachedTableByIdent(Pair.Ident);
		if (address == IntPtr.Zero)
		{
			return;
		}
		base.Logger.LogDebug("[{applicationId}] Reverting all Customization for {alias}/{name} {objectKind}", applicationId, Pair.UserData.AliasOrUID, name, objectKind);
		if (_customizeIds.TryGetValue(objectKind, out var customizeId))
		{
			_customizeIds.Remove(objectKind);
		}
		switch (objectKind)
		{
		case XIVSync.API.Data.Enum.ObjectKind.Player:
		{
			using (GameObjectHandler tempHandler = await _gameObjectHandlerFactory.Create(XIVSync.API.Data.Enum.ObjectKind.Player, () => address).ConfigureAwait(continueOnCapturedContext: false))
			{
				tempHandler.CompareNameAndThrow(name);
				base.Logger.LogDebug("[{applicationId}] Restoring Customization and Equipment for {alias}/{name}", applicationId, Pair.UserData.AliasOrUID, name);
				await _ipcManager.Glamourer.RevertAsync(base.Logger, tempHandler, applicationId, cancelToken).ConfigureAwait(continueOnCapturedContext: false);
				tempHandler.CompareNameAndThrow(name);
				base.Logger.LogDebug("[{applicationId}] Restoring Heels for {alias}/{name}", applicationId, Pair.UserData.AliasOrUID, name);
				await _ipcManager.Heels.RestoreOffsetForPlayerAsync(address).ConfigureAwait(continueOnCapturedContext: false);
				tempHandler.CompareNameAndThrow(name);
				base.Logger.LogDebug("[{applicationId}] Restoring C+ for {alias}/{name}", applicationId, Pair.UserData.AliasOrUID, name);
				await _ipcManager.CustomizePlus.RevertByIdAsync(customizeId).ConfigureAwait(continueOnCapturedContext: false);
				tempHandler.CompareNameAndThrow(name);
				base.Logger.LogDebug("[{applicationId}] Restoring Honorific for {alias}/{name}", applicationId, Pair.UserData.AliasOrUID, name);
				await _ipcManager.Honorific.ClearTitleAsync(address).ConfigureAwait(continueOnCapturedContext: false);
				base.Logger.LogDebug("[{applicationId}] Restoring Moodles for {alias}/{name}", applicationId, Pair.UserData.AliasOrUID, name);
				await _ipcManager.Moodles.RevertStatusAsync(address).ConfigureAwait(continueOnCapturedContext: false);
				base.Logger.LogDebug("[{applicationId}] Restoring Pet Nicknames for {alias}/{name}", applicationId, Pair.UserData.AliasOrUID, name);
				await _ipcManager.PetNames.ClearPlayerData(address).ConfigureAwait(continueOnCapturedContext: false);
			}
			break;
		}
		case XIVSync.API.Data.Enum.ObjectKind.MinionOrMount:
		{
			nint minionOrMount = await _dalamudUtil.GetMinionOrMountAsync(address).ConfigureAwait(continueOnCapturedContext: false);
			if (minionOrMount != IntPtr.Zero)
			{
				await _ipcManager.CustomizePlus.RevertByIdAsync(customizeId).ConfigureAwait(continueOnCapturedContext: false);
				using GameObjectHandler tempHandler = await _gameObjectHandlerFactory.Create(XIVSync.API.Data.Enum.ObjectKind.MinionOrMount, () => minionOrMount).ConfigureAwait(continueOnCapturedContext: false);
				await _ipcManager.Glamourer.RevertAsync(base.Logger, tempHandler, applicationId, cancelToken).ConfigureAwait(continueOnCapturedContext: false);
				await _ipcManager.Penumbra.RedrawAsync(base.Logger, tempHandler, applicationId, cancelToken).ConfigureAwait(continueOnCapturedContext: false);
			}
			break;
		}
		case XIVSync.API.Data.Enum.ObjectKind.Pet:
		{
			nint pet = await _dalamudUtil.GetPetAsync(address).ConfigureAwait(continueOnCapturedContext: false);
			if (pet != IntPtr.Zero)
			{
				await _ipcManager.CustomizePlus.RevertByIdAsync(customizeId).ConfigureAwait(continueOnCapturedContext: false);
				using GameObjectHandler tempHandler = await _gameObjectHandlerFactory.Create(XIVSync.API.Data.Enum.ObjectKind.Pet, () => pet).ConfigureAwait(continueOnCapturedContext: false);
				await _ipcManager.Glamourer.RevertAsync(base.Logger, tempHandler, applicationId, cancelToken).ConfigureAwait(continueOnCapturedContext: false);
				await _ipcManager.Penumbra.RedrawAsync(base.Logger, tempHandler, applicationId, cancelToken).ConfigureAwait(continueOnCapturedContext: false);
			}
			break;
		}
		case XIVSync.API.Data.Enum.ObjectKind.Companion:
		{
			nint companion = await _dalamudUtil.GetCompanionAsync(address).ConfigureAwait(continueOnCapturedContext: false);
			if (companion != IntPtr.Zero)
			{
				await _ipcManager.CustomizePlus.RevertByIdAsync(customizeId).ConfigureAwait(continueOnCapturedContext: false);
				using GameObjectHandler tempHandler = await _gameObjectHandlerFactory.Create(XIVSync.API.Data.Enum.ObjectKind.Pet, () => companion).ConfigureAwait(continueOnCapturedContext: false);
				await _ipcManager.Glamourer.RevertAsync(base.Logger, tempHandler, applicationId, cancelToken).ConfigureAwait(continueOnCapturedContext: false);
				await _ipcManager.Penumbra.RedrawAsync(base.Logger, tempHandler, applicationId, cancelToken).ConfigureAwait(continueOnCapturedContext: false);
			}
			break;
		}
		}
	}

	private List<FileReplacementData> TryCalculateModdedDictionary(Guid applicationBase, CharacterData charaData, out Dictionary<(string GamePath, string? Hash), string> moddedDictionary, CancellationToken token)
	{
		Stopwatch st = Stopwatch.StartNew();
		ConcurrentBag<FileReplacementData> missingFiles = new ConcurrentBag<FileReplacementData>();
		moddedDictionary = new Dictionary<(string, string), string>();
		ConcurrentDictionary<(string GamePath, string? Hash), string> outputDict = new ConcurrentDictionary<(string, string), string>();
		bool hasMigrationChanges = false;
		try
		{
			Parallel.ForEach(charaData.FileReplacements.SelectMany<KeyValuePair<XIVSync.API.Data.Enum.ObjectKind, List<FileReplacementData>>, FileReplacementData>((KeyValuePair<XIVSync.API.Data.Enum.ObjectKind, List<FileReplacementData>> k) => k.Value.Where((FileReplacementData v) => string.IsNullOrEmpty(v.FileSwapPath))).ToList(), new ParallelOptions
			{
				CancellationToken = token,
				MaxDegreeOfParallelism = 4
			}, delegate(FileReplacementData item)
			{
				token.ThrowIfCancellationRequested();
				FileCacheEntity fileCacheEntity = _fileDbManager.GetFileCacheByHash(item.Hash);
				if (fileCacheEntity != null)
				{
					if (string.IsNullOrEmpty(new FileInfo(fileCacheEntity.ResolvedFilepath).Extension))
					{
						hasMigrationChanges = true;
						fileCacheEntity = _fileDbManager.MigrateFileHashToExtension(fileCacheEntity, item.GamePaths[0].Split(".")[^1]);
					}
					string[] gamePaths2 = item.GamePaths;
					foreach (string item2 in gamePaths2)
					{
						outputDict[(item2, item.Hash)] = fileCacheEntity.ResolvedFilepath;
					}
				}
				else
				{
					base.Logger.LogTrace("Missing file: {hash}", item.Hash);
					missingFiles.Add(item);
				}
			});
			moddedDictionary = outputDict.ToDictionary<KeyValuePair<(string, string), string>, (string, string), string>((KeyValuePair<(string GamePath, string Hash), string> k) => k.Key, (KeyValuePair<(string GamePath, string Hash), string> k) => k.Value);
			foreach (FileReplacementData item in charaData.FileReplacements.SelectMany<KeyValuePair<XIVSync.API.Data.Enum.ObjectKind, List<FileReplacementData>>, FileReplacementData>((KeyValuePair<XIVSync.API.Data.Enum.ObjectKind, List<FileReplacementData>> k) => k.Value.Where((FileReplacementData v) => !string.IsNullOrEmpty(v.FileSwapPath))).ToList())
			{
				string[] gamePaths = item.GamePaths;
				foreach (string gamePath in gamePaths)
				{
					base.Logger.LogTrace("[BASE-{appBase}] Adding file swap for {path}: {fileSwap}", applicationBase, gamePath, item.FileSwapPath);
					moddedDictionary[(gamePath, null)] = item.FileSwapPath;
				}
			}
		}
		catch (Exception ex)
		{
			base.Logger.LogError(ex, "[BASE-{appBase}] Something went wrong during calculation replacements", applicationBase);
		}
		if (hasMigrationChanges)
		{
			_fileDbManager.WriteOutFullCsv();
		}
		st.Stop();
		base.Logger.LogDebug("[BASE-{appBase}] ModdedPaths calculated in {time}ms, missing files: {count}, total files: {total}", applicationBase, st.ElapsedMilliseconds, missingFiles.Count, moddedDictionary.Keys.Count);
		return missingFiles.ToList();
	}
}
