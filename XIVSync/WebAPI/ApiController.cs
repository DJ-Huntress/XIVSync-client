using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Utility;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using XIVSync.API.Data;
using XIVSync.API.Data.Enum;
using XIVSync.API.Data.Extensions;
using XIVSync.API.Dto;
using XIVSync.API.Dto.CharaData;
using XIVSync.API.Dto.Group;
using XIVSync.API.Dto.User;
using XIVSync.API.SignalR;
using XIVSync.MareConfiguration;
using XIVSync.MareConfiguration.Models;
using XIVSync.PlayerData.Pairs;
using XIVSync.Services;
using XIVSync.Services.Events;
using XIVSync.Services.Mediator;
using XIVSync.Services.ServerConfiguration;
using XIVSync.WebAPI.SignalR;
using XIVSync.WebAPI.SignalR.Utils;

namespace XIVSync.WebAPI;

public sealed class ApiController : DisposableMediatorSubscriberBase, IMareHubClient, IMareHub
{
	public const string MainServer = "XIVSync Central Server";

	public const string MainServiceUri = "https://mare.xivsync.com";

	private readonly DalamudUtilService _dalamudUtil;

	private readonly HubFactory _hubFactory;

	private readonly PairManager _pairManager;

	private readonly ServerConfigurationManager _serverManager;

	private readonly TokenProvider _tokenProvider;

	private readonly MareConfigService _mareConfigService;

	private CancellationTokenSource _connectionCancellationTokenSource;

	private ConnectionDto? _connectionDto;

	private bool _doNotNotifyOnNextInfo;

	private CancellationTokenSource? _healthCheckTokenSource = new CancellationTokenSource();

	private bool _initialized;

	private string? _lastUsedToken;

	private HubConnection? _mareHub;

	private ServerState _serverState;

	private CensusUpdateMessage? _lastCensus;

	private HubConnection? _hub;

	private readonly object _usersLock = new object();

	private int _onlineUsers;

	private int _unauthorizedRetryCount;

	private const int MaxUnauthorizedRetries = 3;

	private bool _naggedAboutLod;

	public int OnlineUsers => Volatile.Read(ref _onlineUsers);

	public string AuthFailureMessage { get; private set; } = string.Empty;


	public Version CurrentClientVersion => _connectionDto?.CurrentClientVersion ?? new Version(0, 0, 0);

	public DefaultPermissionsDto? DefaultPermissions => _connectionDto?.DefaultPreferredPermissions ?? null;

	public string DisplayName => _connectionDto?.User.AliasOrUID ?? string.Empty;

	public bool IsConnected => ServerState == ServerState.Connected;

	public bool IsCurrentVersion => (Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0)) >= (_connectionDto?.CurrentClientVersion ?? new Version(0, 0, 0, 0));

	public bool ServerAlive
	{
		get
        {
            ServerState serverState = ServerState;
            if ((uint)(serverState - 4) <= 2u || serverState == ServerState.RateLimited)
			{
				return true;
			}
			return false;
		}
	}

	public ServerInfo ServerInfo => _connectionDto?.ServerInfo ?? new ServerInfo();

	public ServerState ServerState
	{
		get
		{
			return _serverState;
		}
		private set
		{
			base.Logger.LogDebug("New ServerState: {value}, prev ServerState: {_serverState}", value, _serverState);
			_serverState = value;
		}
	}

	public SystemInfoDto SystemInfoDto { get; private set; } = new SystemInfoDto();


	public string UID => _connectionDto?.User.UID ?? string.Empty;

	public event Action<int>? OnlineUsersChanged;

	public ApiController(ILogger<ApiController> logger, HubFactory hubFactory, DalamudUtilService dalamudUtil, PairManager pairManager, ServerConfigurationManager serverManager, MareMediator mediator, TokenProvider tokenProvider, MareConfigService mareConfigService)
		: base(logger, mediator)
	{
		_hubFactory = hubFactory;
		_dalamudUtil = dalamudUtil;
		_pairManager = pairManager;
		_serverManager = serverManager;
		_tokenProvider = tokenProvider;
		_mareConfigService = mareConfigService;
		_connectionCancellationTokenSource = new CancellationTokenSource();
		base.Mediator.Subscribe<DalamudLoginMessage>(this, delegate
		{
			DalamudUtilOnLogIn();
		});
		base.Mediator.Subscribe<DalamudLogoutMessage>(this, delegate
		{
			DalamudUtilOnLogOut();
		});
		base.Mediator.Subscribe(this, delegate(HubClosedMessage msg)
		{
			MareHubOnClosed(msg.Exception);
		});
		base.Mediator.Subscribe<HubReconnectedMessage>(this, delegate
		{
			MareHubOnReconnectedAsync();
		});
		base.Mediator.Subscribe(this, delegate(HubReconnectingMessage msg)
		{
			MareHubOnReconnecting(msg.Exception);
		});
		base.Mediator.Subscribe(this, delegate(CyclePauseMessage msg)
		{
			CyclePauseAsync(msg.UserData);
		});
		base.Mediator.Subscribe(this, delegate(CensusUpdateMessage msg)
		{
			_lastCensus = msg;
		});
		base.Mediator.Subscribe(this, delegate(PauseMessage msg)
		{
			PauseAsync(msg.UserData);
		});
		ServerState = ServerState.Offline;
		if (_dalamudUtil.IsLoggedIn)
		{
			DalamudUtilOnLogIn();
		}
	}

	public async Task<bool> CheckClientHealth()
	{
		return await _mareHub.InvokeAsync<bool>("CheckClientHealth").ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task CreateConnectionsAsync()
	{
		if (!_serverManager.ShownCensusPopup)
		{
			base.Mediator.Publish(new OpenCensusPopupMessage());
			while (!_serverManager.ShownCensusPopup)
			{
				await Task.Delay(500).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		base.Logger.LogDebug("CreateConnections called");
		if (_serverManager.CurrentServer?.FullPause ?? true)
		{
			base.Logger.LogInformation("Not recreating Connection, paused");
			_connectionDto = null;
			await StopConnectionAsync(ServerState.Disconnected).ConfigureAwait(continueOnCapturedContext: false);
			if (_connectionCancellationTokenSource != null)
			{
				await _connectionCancellationTokenSource.CancelAsync();
			}
			return;
		}
		if (!_serverManager.CurrentServer.UseOAuth2)
		{
			bool multi;
			string secretKey = _serverManager.GetSecretKey(out multi);
			if (multi)
			{
				base.Logger.LogWarning("Multiple secret keys for current character");
				_connectionDto = null;
				base.Mediator.Publish(new NotificationMessage("Multiple Identical Characters detected", "Your Service configuration has multiple characters with the same name and world set up. Delete the duplicates in the character management to be able to connect to Mare.", NotificationType.Error));
				await StopConnectionAsync(ServerState.MultiChara).ConfigureAwait(continueOnCapturedContext: false);
				if (_connectionCancellationTokenSource != null)
				{
					await _connectionCancellationTokenSource.CancelAsync();
				}
				return;
			}
			if (secretKey.IsNullOrEmpty())
			{
				base.Logger.LogWarning("No secret key set for current character");
				_connectionDto = null;
				await StopConnectionAsync(ServerState.NoSecretKey).ConfigureAwait(continueOnCapturedContext: false);
				if (_connectionCancellationTokenSource != null)
				{
					await _connectionCancellationTokenSource.CancelAsync();
				}
				return;
			}
		}
		else
		{
			bool multi;
			(string OAuthToken, string UID)? oauth2 = _serverManager.GetOAuth2(out multi);
			if (multi)
			{
				base.Logger.LogWarning("Multiple secret keys for current character");
				_connectionDto = null;
				base.Mediator.Publish(new NotificationMessage("Multiple Identical Characters detected", "Your Service configuration has multiple characters with the same name and world set up. Delete the duplicates in the character management to be able to connect to Mare.", NotificationType.Error));
				await StopConnectionAsync(ServerState.MultiChara).ConfigureAwait(continueOnCapturedContext: false);
				if (_connectionCancellationTokenSource != null)
				{
					await _connectionCancellationTokenSource.CancelAsync();
				}
				return;
			}
			if (!oauth2.HasValue)
			{
				base.Logger.LogWarning("No UID/OAuth set for current character");
				_connectionDto = null;
				await StopConnectionAsync(ServerState.OAuthMisconfigured).ConfigureAwait(continueOnCapturedContext: false);
				if (_connectionCancellationTokenSource != null)
				{
					await _connectionCancellationTokenSource.CancelAsync();
				}
				return;
			}
			if (!(await _tokenProvider.TryUpdateOAuth2LoginTokenAsync(_serverManager.CurrentServer).ConfigureAwait(continueOnCapturedContext: false)))
			{
				base.Logger.LogWarning("OAuth2 login token could not be updated");
				_connectionDto = null;
				await StopConnectionAsync(ServerState.OAuthLoginTokenStale).ConfigureAwait(continueOnCapturedContext: false);
				if (_connectionCancellationTokenSource != null)
				{
					await _connectionCancellationTokenSource.CancelAsync();
				}
				return;
			}
		}
		await StopConnectionAsync(ServerState.Disconnected).ConfigureAwait(continueOnCapturedContext: false);
		base.Logger.LogInformation("Recreating Connection");
		base.Mediator.Publish(new EventMessage(new Event("ApiController", EventSeverity.Informational, "Starting Connection to " + _serverManager.CurrentServer.ServerName)));
		if (_connectionCancellationTokenSource != null)
		{
			await _connectionCancellationTokenSource.CancelAsync();
		}
		_connectionCancellationTokenSource?.Dispose();
		_connectionCancellationTokenSource = new CancellationTokenSource();
		CancellationToken token = _connectionCancellationTokenSource.Token;
		while (ServerState != ServerState.Connected && !token.IsCancellationRequested)
		{
			AuthFailureMessage = string.Empty;
			await StopConnectionAsync(ServerState.Disconnected).ConfigureAwait(continueOnCapturedContext: false);
			ServerState = ServerState.Connecting;
			try
			{
				base.Logger.LogDebug("Building connection");
				try
				{
					_lastUsedToken = await _tokenProvider.GetOrUpdateToken(token).ConfigureAwait(continueOnCapturedContext: false);
				}
				catch (MareAuthFailureException ex)
				{
					AuthFailureMessage = ex.Reason;
					throw new HttpRequestException("Error during authentication", ex, HttpStatusCode.Unauthorized);
				}
				while (!(await _dalamudUtil.GetIsPlayerPresentAsync().ConfigureAwait(continueOnCapturedContext: false)) && !token.IsCancellationRequested)
				{
					base.Logger.LogDebug("Player not loaded in yet, waiting");
					await Task.Delay(TimeSpan.FromSeconds(1L), token).ConfigureAwait(continueOnCapturedContext: false);
				}
				if (token.IsCancellationRequested)
				{
					break;
				}
				_mareHub = _hubFactory.GetOrCreate(token);
				InitializeApiHooks();
				await _mareHub.StartAsync(token).ConfigureAwait(continueOnCapturedContext: false);
				_connectionDto = await GetConnectionDto().ConfigureAwait(continueOnCapturedContext: false);
				Volatile.Write(ref _onlineUsers, SystemInfoDto?.OnlineUsers ?? 0);
				this.OnlineUsersChanged?.Invoke(_onlineUsers);
				ServerState = ServerState.Connected;
				_unauthorizedRetryCount = 0;
				Version currentClientVer = Assembly.GetExecutingAssembly().GetName().Version;
				if (_connectionDto.ServerVersion != 33)
				{
					if (_connectionDto.CurrentClientVersion > currentClientVer)
					{
						base.Mediator.Publish(new NotificationMessage("Client incompatible", $"Your client is outdated ({currentClientVer.Major}.{currentClientVer.Minor}.{currentClientVer.Build}), current is: {_connectionDto.CurrentClientVersion.Major}.{_connectionDto.CurrentClientVersion.Minor}.{_connectionDto.CurrentClientVersion.Build}. This client version is incompatible and will not be able to connect. Please update your XIVSync client.", NotificationType.Error));
					}
					await StopConnectionAsync(ServerState.VersionMisMatch).ConfigureAwait(continueOnCapturedContext: false);
					break;
				}
				if (_connectionDto.CurrentClientVersion > currentClientVer)
				{
					base.Mediator.Publish(new NotificationMessage("Client outdated", $"Your client is outdated ({currentClientVer.Major}.{currentClientVer.Minor}.{currentClientVer.Build}), current is: {_connectionDto.CurrentClientVersion.Major}.{_connectionDto.CurrentClientVersion.Minor}.{_connectionDto.CurrentClientVersion.Build}. Please keep your XIVSync client up-to-date.", NotificationType.Warning));
				}
				if (_dalamudUtil.HasModifiedGameFiles)
				{
					base.Logger.LogError("Detected modified game files on connection");
					if (!_mareConfigService.Current.DebugStopWhining)
					{
						base.Mediator.Publish(new NotificationMessage("Modified Game Files detected", "Dalamud is reporting your FFXIV installation has modified game files. Any mods installed through TexTools will produce this message. XIVSync, Penumbra, and some other plugins assume your FFXIV installation is unmodified in order to work. Synchronization with pairs/shells can break because of this. Exit the game, open XIVLauncher, click the arrow next to Log In and select 'repair game files' to resolve this issue. Afterwards, do not install any mods with TexTools. Your plugin configurations will remain, as will mods enabled in Penumbra.", NotificationType.Error, TimeSpan.FromSeconds(15L)));
					}
				}
				if (_dalamudUtil.IsLodEnabled && !_naggedAboutLod)
				{
					_naggedAboutLod = true;
					base.Logger.LogWarning("Model LOD is enabled during connection");
					if (!_mareConfigService.Current.DebugStopWhining)
					{
						base.Mediator.Publish(new NotificationMessage("Model LOD is enabled", "You have \"Use low-detail models on distant objects (LOD)\" enabled. Having model LOD enabled is known to be a reason to cause random crashes when loading in or rendering modded pairs. Disabling LOD has a very low performance impact. Disable LOD while using Mare: Go to XIV Menu -> System Configuration -> Graphics Settings and disable the model LOD option.", NotificationType.Warning, TimeSpan.FromSeconds(15L)));
					}
				}
				if (_naggedAboutLod && !_dalamudUtil.IsLodEnabled)
				{
					_naggedAboutLod = false;
				}
				await LoadIninitialPairsAsync().ConfigureAwait(continueOnCapturedContext: false);
				await LoadOnlinePairsAsync().ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (OperationCanceledException)
			{
				base.Logger.LogWarning("Connection attempt cancelled");
				break;
			}
			catch (HttpRequestException ex)
			{
				base.Logger.LogWarning(ex, "HttpRequestException on Connection");
				if (ex.StatusCode.GetValueOrDefault() == HttpStatusCode.Unauthorized)
				{
					_unauthorizedRetryCount++;
					if (_unauthorizedRetryCount > 3)
					{
						base.Logger.LogError("Authentication failed after {count} attempts, stopping connection", _unauthorizedRetryCount);
						await StopConnectionAsync(ServerState.Unauthorized).ConfigureAwait(continueOnCapturedContext: false);
						break;
					}
					base.Logger.LogWarning("Authentication failed (attempt {count}/{max}), clearing token cache and retrying with fresh token", _unauthorizedRetryCount, 3);
					_tokenProvider.ClearTokenCache();
					ServerState = ServerState.Reconnecting;
					await Task.Delay(TimeSpan.FromSeconds(_unauthorizedRetryCount * 3), token).ConfigureAwait(continueOnCapturedContext: false);
				}
				else
				{
					ServerState = ServerState.Reconnecting;
					base.Logger.LogInformation("Failed to establish connection, retrying");
					await Task.Delay(TimeSpan.FromSeconds(new Random().Next(5, 20)), token).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
			catch (InvalidOperationException ex)
			{
				base.Logger.LogWarning(ex, "InvalidOperationException on connection");
				await StopConnectionAsync(ServerState.Disconnected).ConfigureAwait(continueOnCapturedContext: false);
				break;
			}
			catch (Exception ex)
			{
				base.Logger.LogWarning(ex, "Exception on Connection");
				base.Logger.LogInformation("Failed to establish connection, retrying");
				await Task.Delay(TimeSpan.FromSeconds(new Random().Next(5, 20)), token).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
	}

	public Task CyclePauseAsync(UserData userData)
	{
		CancellationTokenSource cts = new CancellationTokenSource();
		cts.CancelAfter(TimeSpan.FromSeconds(5L));
		Task.Run(async delegate
		{
			Pair pair = _pairManager.GetOnlineUserPairs().Single((Pair p) => p.UserPair != null && string.Equals(p.UserData.UID, userData.UID, StringComparison.Ordinal));
			UserPermissions perm = pair.UserPair.OwnPermissions;
			perm.SetPaused(paused: true);
			await UserSetPairPermissions(new UserPermissionsDto(userData, perm)).ConfigureAwait(continueOnCapturedContext: false);
			while (pair.UserPair.OwnPermissions != perm)
			{
				await Task.Delay(250, cts.Token).ConfigureAwait(continueOnCapturedContext: false);
				base.Logger.LogTrace("Waiting for permissions change for {data}", userData);
			}
			perm.SetPaused(paused: false);
			await UserSetPairPermissions(new UserPermissionsDto(userData, perm)).ConfigureAwait(continueOnCapturedContext: false);
		}, cts.Token).ContinueWith(delegate
		{
			cts.Dispose();
		});
		return Task.CompletedTask;
	}

	public async Task PauseAsync(UserData userData)
	{
		UserPermissions perm = _pairManager.GetOnlineUserPairs().Single((Pair p) => p.UserPair != null && string.Equals(p.UserData.UID, userData.UID, StringComparison.Ordinal)).UserPair.OwnPermissions;
		perm.SetPaused(paused: true);
		await UserSetPairPermissions(new UserPermissionsDto(userData, perm)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task RefreshOnlineUsersAsync()
	{
		if (_mareHub != null)
		{
			await HandleSystemInfoUpdate(await _mareHub.InvokeAsync<SystemInfoDto>("GetSystemInfo").ConfigureAwait(continueOnCapturedContext: false)).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	private Task HandleSystemInfoUpdate(SystemInfoDto dto)
	{
		if (dto == null)
		{
			return Task.CompletedTask;
		}
		int prev = Volatile.Read(ref _onlineUsers);
		SystemInfoDto = dto;
		Volatile.Write(ref _onlineUsers, dto.OnlineUsers);
		base.Logger.LogInformation("SystemInfo update: OnlineUsers changed {Prev} -> {Now}", prev, dto.OnlineUsers);
		if (dto.OnlineUsers != prev)
		{
			this.OnlineUsersChanged?.Invoke(dto.OnlineUsers);
			base.Mediator.Publish(new RefreshUiMessage());
		}
		return Task.CompletedTask;
	}

	public Task<ConnectionDto> GetConnectionDto()
	{
		return GetConnectionDtoAsync(publishConnected: true);
	}

	public async Task<ConnectionDto> GetConnectionDtoAsync(bool publishConnected)
	{
		ConnectionDto dto = await _mareHub.InvokeAsync<ConnectionDto>("GetConnectionDto").ConfigureAwait(continueOnCapturedContext: false);
		base.Logger.LogInformation("GetConnectionDto <= Shard={Shard} Created={Created} Joined={Joined} GroupUserCount={UserCount} FileServer={File}", dto.ServerInfo?.ShardName, dto.ServerInfo?.MaxGroupsCreatedByUser, dto.ServerInfo?.MaxGroupsJoinedByUser, dto.ServerInfo?.MaxGroupUserCount, dto.ServerInfo?.FileServerAddress);
		if (publishConnected)
		{
			base.Mediator.Publish(new ConnectedMessage(dto));
		}
		return dto;
	}

	protected override void Dispose(bool disposing)
	{
		base.Dispose(disposing);
		_healthCheckTokenSource?.Cancel();
		Task.Run(async delegate
		{
			await StopConnectionAsync(ServerState.Disconnected).ConfigureAwait(continueOnCapturedContext: false);
		});
		if (_connectionCancellationTokenSource != null)
		{
			Task.Run(() => _connectionCancellationTokenSource.CancelAsync());
		}
	}

	private async Task ClientHealthCheckAsync(CancellationToken ct)
	{
		while (!ct.IsCancellationRequested && _mareHub != null)
		{
			await Task.Delay(TimeSpan.FromSeconds(30L), ct).ConfigureAwait(continueOnCapturedContext: false);
			base.Logger.LogDebug("Checking Client Health State");
			if (await RefreshTokenAsync(ct).ConfigureAwait(continueOnCapturedContext: false))
			{
				break;
			}
			await CheckClientHealth().ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	private void DalamudUtilOnLogIn()
	{
		string charaName = _dalamudUtil.GetPlayerNameAsync().GetAwaiter().GetResult();
		uint worldId = _dalamudUtil.GetHomeWorldIdAsync().GetAwaiter().GetResult();
		Authentication? authentication = _serverManager.CurrentServer.Authentications.Find((Authentication f) => string.Equals(f.CharacterName, charaName, StringComparison.Ordinal) && f.WorldId == worldId);
		if ((object)authentication != null && authentication.AutoLogin)
		{
			base.Logger.LogInformation("Logging into {chara}", charaName);
			Task.Run((Func<Task?>)CreateConnectionsAsync);
			return;
		}
		base.Logger.LogInformation("Not logging into {chara}, auto login disabled", charaName);
		Task.Run(async delegate
		{
			await StopConnectionAsync(ServerState.NoAutoLogon).ConfigureAwait(continueOnCapturedContext: false);
		});
	}

	private void DalamudUtilOnLogOut()
	{
		Task.Run(async delegate
		{
			await StopConnectionAsync(ServerState.Disconnected).ConfigureAwait(continueOnCapturedContext: false);
		});
		ServerState = ServerState.Offline;
	}

	private void InitializeApiHooks()
	{
		if (_mareHub != null)
		{
			base.Logger.LogDebug("Initializing data");
			OnDownloadReady(delegate(Guid guid)
			{
				Client_DownloadReady(guid);
			});
			OnReceiveServerMessage(delegate(MessageSeverity sev, string msg)
			{
				Client_ReceiveServerMessage(sev, msg);
			});
			OnUpdateSystemInfo(delegate(SystemInfoDto dto)
			{
				Client_UpdateSystemInfo(dto);
			});
			OnUpdateSystemInfo(delegate(SystemInfoDto dto)
			{
				HandleSystemInfoUpdate(dto);
			});
			OnUserSendOffline(delegate(UserDto dto)
			{
				Client_UserSendOffline(dto);
			});
			OnUserAddClientPair(delegate(UserPairDto dto)
			{
				Client_UserAddClientPair(dto);
			});
			OnUserReceiveCharacterData(delegate(OnlineUserCharaDataDto dto)
			{
				Client_UserReceiveCharacterData(dto);
			});
			OnUserRemoveClientPair(delegate(UserDto dto)
			{
				Client_UserRemoveClientPair(dto);
			});
			OnUserSendOnline(delegate(OnlineUserIdentDto dto)
			{
				Client_UserSendOnline(dto);
			});
			OnUserUpdateOtherPairPermissions(delegate(UserPermissionsDto dto)
			{
				Client_UserUpdateOtherPairPermissions(dto);
			});
			OnUserUpdateSelfPairPermissions(delegate(UserPermissionsDto dto)
			{
				Client_UserUpdateSelfPairPermissions(dto);
			});
			OnUserReceiveUploadStatus(delegate(UserDto dto)
			{
				Client_UserReceiveUploadStatus(dto);
			});
			OnUserUpdateProfile(delegate(UserDto dto)
			{
				Client_UserUpdateProfile(dto);
			});
			OnUserDefaultPermissionUpdate(delegate(DefaultPermissionsDto dto)
			{
				Client_UserUpdateDefaultPermissions(dto);
			});
			OnUpdateUserIndividualPairStatusDto(delegate(UserIndividualPairStatusDto dto)
			{
				Client_UpdateUserIndividualPairStatusDto(dto);
			});
			OnGroupChangePermissions(delegate(GroupPermissionDto dto)
			{
				Client_GroupChangePermissions(dto);
			});
			OnGroupDelete(delegate(GroupDto dto)
			{
				Client_GroupDelete(dto);
			});
			OnGroupPairChangeUserInfo(delegate(GroupPairUserInfoDto dto)
			{
				Client_GroupPairChangeUserInfo(dto);
			});
			OnGroupPairJoined(delegate(GroupPairFullInfoDto dto)
			{
				Client_GroupPairJoined(dto);
			});
			OnGroupPairLeft(delegate(GroupPairDto dto)
			{
				Client_GroupPairLeft(dto);
			});
			OnGroupSendFullInfo(delegate(GroupFullInfoDto dto)
			{
				Client_GroupSendFullInfo(dto);
			});
			OnGroupSendInfo(delegate(GroupInfoDto dto)
			{
				Client_GroupSendInfo(dto);
			});
			OnGroupChangeUserPairPermissions(delegate(GroupPairUserPermissionDto dto)
			{
				Client_GroupChangeUserPairPermissions(dto);
			});
			OnGposeLobbyJoin(delegate(UserData dto)
			{
				Client_GposeLobbyJoin(dto);
			});
			OnGposeLobbyLeave(delegate(UserData dto)
			{
				Client_GposeLobbyLeave(dto);
			});
			OnGposeLobbyPushCharacterData(delegate(CharaDataDownloadDto dto)
			{
				Client_GposeLobbyPushCharacterData(dto);
			});
			OnGposeLobbyPushPoseData(delegate(UserData dto, PoseData data)
			{
				Client_GposeLobbyPushPoseData(dto, data);
			});
			OnGposeLobbyPushWorldData(delegate(UserData dto, WorldData data)
			{
				Client_GposeLobbyPushWorldData(dto, data);
			});
			_healthCheckTokenSource?.Cancel();
			_healthCheckTokenSource?.Dispose();
			_healthCheckTokenSource = new CancellationTokenSource();
			ClientHealthCheckAsync(_healthCheckTokenSource.Token);
			_initialized = true;
		}
	}

	private async Task LoadIninitialPairsAsync()
	{
		foreach (GroupFullInfoDto entry in await GroupsGetAll().ConfigureAwait(continueOnCapturedContext: false))
		{
			base.Logger.LogDebug("Group: {entry}", entry);
			_pairManager.AddGroup(entry);
		}
		foreach (UserFullPairDto userPair in await UserGetPairedClients().ConfigureAwait(continueOnCapturedContext: false))
		{
			base.Logger.LogDebug("Individual Pair: {userPair}", userPair);
			_pairManager.AddUserPair(userPair);
		}
	}

	private async Task LoadOnlinePairsAsync()
	{
		CensusDataDto dto = null;
		if (_serverManager.SendCensusData && _lastCensus != null)
		{
			dto = new CensusDataDto((ushort)(await _dalamudUtil.GetWorldIdAsync().ConfigureAwait(continueOnCapturedContext: false)), _lastCensus.RaceId, _lastCensus.TribeId, _lastCensus.Gender);
			base.Logger.LogDebug("Attaching Census Data: {data}", dto);
		}
		foreach (OnlineUserIdentDto entry in await UserGetOnlinePairs(dto).ConfigureAwait(continueOnCapturedContext: false))
		{
			base.Logger.LogDebug("Pair online: {pair}", entry);
			_pairManager.MarkPairOnline(entry, sendNotif: false);
		}
	}

	private void MareHubOnClosed(Exception? arg)
	{
		_healthCheckTokenSource?.Cancel();
		base.Mediator.Publish(new DisconnectedMessage());
		ServerState = ServerState.Offline;
		if (arg != null)
		{
			base.Logger.LogWarning(arg, "Connection closed");
		}
		else
		{
			base.Logger.LogInformation("Connection closed");
		}
	}

	private async Task MareHubOnReconnectedAsync()
	{
		ServerState = ServerState.Reconnecting;
		try
		{
			InitializeApiHooks();
			_connectionDto = await GetConnectionDtoAsync(publishConnected: false).ConfigureAwait(continueOnCapturedContext: false);
			if (_connectionDto.ServerVersion != 33)
			{
				await StopConnectionAsync(ServerState.VersionMisMatch).ConfigureAwait(continueOnCapturedContext: false);
				return;
			}
			ServerState = ServerState.Connected;
			await LoadIninitialPairsAsync().ConfigureAwait(continueOnCapturedContext: false);
			await LoadOnlinePairsAsync().ConfigureAwait(continueOnCapturedContext: false);
			base.Mediator.Publish(new ConnectedMessage(_connectionDto));
		}
		catch (Exception ex)
		{
			base.Logger.LogCritical(ex, "Failure to obtain data after reconnection");
			await StopConnectionAsync(ServerState.Disconnected).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	private void MareHubOnReconnecting(Exception? arg)
	{
		_doNotNotifyOnNextInfo = true;
		_healthCheckTokenSource?.Cancel();
		ServerState = ServerState.Reconnecting;
		base.Logger.LogWarning(arg, "Connection closed... Reconnecting");
		base.Mediator.Publish(new EventMessage(new Event("ApiController", EventSeverity.Warning, "Connection interrupted, reconnecting to " + _serverManager.CurrentServer.ServerName)));
	}

	private async Task<bool> RefreshTokenAsync(CancellationToken ct)
	{
		bool requireReconnect = false;
		try
		{
			if (!string.Equals(await _tokenProvider.GetOrUpdateToken(ct).ConfigureAwait(continueOnCapturedContext: false), _lastUsedToken, StringComparison.Ordinal))
			{
				base.Logger.LogDebug("Reconnecting due to updated token");
				_doNotNotifyOnNextInfo = true;
				await CreateConnectionsAsync().ConfigureAwait(continueOnCapturedContext: false);
				requireReconnect = true;
			}
		}
		catch (MareAuthFailureException ex)
		{
			AuthFailureMessage = ex.Reason;
			await StopConnectionAsync(ServerState.Unauthorized).ConfigureAwait(continueOnCapturedContext: false);
			requireReconnect = true;
		}
		catch (Exception ex)
		{
			base.Logger.LogWarning(ex, "Could not refresh token, forcing reconnect");
			_doNotNotifyOnNextInfo = true;
			await CreateConnectionsAsync().ConfigureAwait(continueOnCapturedContext: false);
			requireReconnect = true;
		}
		return requireReconnect;
	}

	private async Task StopConnectionAsync(ServerState state)
	{
		ServerState = ServerState.Disconnecting;
		base.Logger.LogInformation("Stopping existing connection");
		await _hubFactory.DisposeHubAsync().ConfigureAwait(continueOnCapturedContext: false);
		if (_mareHub != null)
		{
			base.Mediator.Publish(new EventMessage(new Event("ApiController", EventSeverity.Informational, "Stopping existing connection to " + _serverManager.CurrentServer.ServerName)));
			_initialized = false;
			_healthCheckTokenSource?.Cancel();
			base.Mediator.Publish(new DisconnectedMessage());
			_mareHub = null;
			_connectionDto = null;
		}
		ServerState = state;
	}

	private Task Client_UpdateUsersOnline(SystemInfoDto dto)
	{
		if (dto == null)
		{
			return Task.CompletedTask;
		}
		int prev = SystemInfoDto?.OnlineUsers ?? 0;
		SystemInfoDto = dto;
		if (dto.OnlineUsers != prev)
		{
			this.OnlineUsersChanged?.Invoke(dto.OnlineUsers);
			base.Mediator.Publish(new RefreshUiMessage());
		}
		return Task.CompletedTask;
	}

	public Task Client_DownloadReady(Guid requestId)
	{
		base.Logger.LogDebug("Server sent {requestId} ready", requestId);
		base.Mediator.Publish(new DownloadReadyMessage(requestId));
		return Task.CompletedTask;
	}

	public Task Client_GroupChangePermissions(GroupPermissionDto groupPermission)
	{
		base.Logger.LogTrace("Client_GroupChangePermissions: {perm}", groupPermission);
		ExecuteSafely(delegate
		{
			_pairManager.SetGroupPermissions(groupPermission);
		});
		return Task.CompletedTask;
	}

	public Task Client_GroupChangeUserPairPermissions(GroupPairUserPermissionDto dto)
	{
		base.Logger.LogDebug("Client_GroupChangeUserPairPermissions: {dto}", dto);
		ExecuteSafely(delegate
		{
			_pairManager.UpdateGroupPairPermissions(dto);
		});
		return Task.CompletedTask;
	}

	public Task Client_GroupDelete(GroupDto groupDto)
	{
		base.Logger.LogTrace("Client_GroupDelete: {dto}", groupDto);
		ExecuteSafely(delegate
		{
			_pairManager.RemoveGroup(groupDto.Group);
		});
		return Task.CompletedTask;
	}

	public Task Client_GroupPairChangeUserInfo(GroupPairUserInfoDto userInfo)
	{
		base.Logger.LogTrace("Client_GroupPairChangeUserInfo: {dto}", userInfo);
		ExecuteSafely(delegate
		{
			if (string.Equals(userInfo.UID, UID, StringComparison.Ordinal))
			{
				_pairManager.SetGroupStatusInfo(userInfo);
			}
			else
			{
				_pairManager.SetGroupPairStatusInfo(userInfo);
			}
		});
		return Task.CompletedTask;
	}

	public Task Client_GroupPairJoined(GroupPairFullInfoDto groupPairInfoDto)
	{
		base.Logger.LogTrace("Client_GroupPairJoined: {dto}", groupPairInfoDto);
		ExecuteSafely(delegate
		{
			_pairManager.AddGroupPair(groupPairInfoDto);
		});
		return Task.CompletedTask;
	}

	public Task Client_GroupPairLeft(GroupPairDto groupPairDto)
	{
		base.Logger.LogTrace("Client_GroupPairLeft: {dto}", groupPairDto);
		ExecuteSafely(delegate
		{
			_pairManager.RemoveGroupPair(groupPairDto);
		});
		return Task.CompletedTask;
	}

	public Task Client_GroupSendFullInfo(GroupFullInfoDto groupInfo)
	{
		base.Logger.LogTrace("Client_GroupSendFullInfo: {dto}", groupInfo);
		ExecuteSafely(delegate
		{
			_pairManager.AddGroup(groupInfo);
		});
		return Task.CompletedTask;
	}

	public Task Client_GroupSendInfo(GroupInfoDto groupInfo)
	{
		base.Logger.LogTrace("Client_GroupSendInfo: {dto}", groupInfo);
		ExecuteSafely(delegate
		{
			_pairManager.SetGroupInfo(groupInfo);
		});
		return Task.CompletedTask;
	}

	public Task Client_ReceiveServerMessage(MessageSeverity messageSeverity, string message)
	{
		switch (messageSeverity)
		{
		case MessageSeverity.Error:
			base.Mediator.Publish(new NotificationMessage("Warning from " + _serverManager.CurrentServer.ServerName, message, NotificationType.Error, TimeSpan.FromSeconds(7.5)));
			break;
		case MessageSeverity.Warning:
			base.Mediator.Publish(new NotificationMessage("Warning from " + _serverManager.CurrentServer.ServerName, message, NotificationType.Warning, TimeSpan.FromSeconds(7.5)));
			break;
		case MessageSeverity.Information:
			if (_doNotNotifyOnNextInfo)
			{
				_doNotNotifyOnNextInfo = false;
			}
			else
			{
				base.Mediator.Publish(new NotificationMessage("Info from " + _serverManager.CurrentServer.ServerName, message, NotificationType.Info, TimeSpan.FromSeconds(5L)));
			}
			break;
		}
		return Task.CompletedTask;
	}

	public Task Client_UpdateSystemInfo(SystemInfoDto systemInfo)
	{
		SystemInfoDto = systemInfo;
		return Task.CompletedTask;
	}

	public Task Client_UpdateUserIndividualPairStatusDto(UserIndividualPairStatusDto dto)
	{
		base.Logger.LogDebug("Client_UpdateUserIndividualPairStatusDto: {dto}", dto);
		ExecuteSafely(delegate
		{
			_pairManager.UpdateIndividualPairStatus(dto);
		});
		return Task.CompletedTask;
	}

	public Task Client_UserAddClientPair(UserPairDto dto)
	{
		base.Logger.LogDebug("Client_UserAddClientPair: {dto}", dto);
		ExecuteSafely(delegate
		{
			_pairManager.AddUserPair(dto);
		});
		return Task.CompletedTask;
	}

	public Task Client_UserReceiveCharacterData(OnlineUserCharaDataDto dataDto)
	{
		base.Logger.LogTrace("Client_UserReceiveCharacterData: {user}", dataDto.User);
		ExecuteSafely(delegate
		{
			_pairManager.ReceiveCharaData(dataDto);
		});
		return Task.CompletedTask;
	}

	public Task Client_UserReceiveUploadStatus(UserDto dto)
	{
		base.Logger.LogTrace("Client_UserReceiveUploadStatus: {dto}", dto);
		ExecuteSafely(delegate
		{
			_pairManager.ReceiveUploadStatus(dto);
		});
		return Task.CompletedTask;
	}

	public Task Client_UserRemoveClientPair(UserDto dto)
	{
		base.Logger.LogDebug("Client_UserRemoveClientPair: {dto}", dto);
		ExecuteSafely(delegate
		{
			_pairManager.RemoveUserPair(dto);
		});
		return Task.CompletedTask;
	}

	public Task Client_UserSendOffline(UserDto dto)
	{
		base.Logger.LogDebug("Client_UserSendOffline: {dto}", dto);
		ExecuteSafely(delegate
		{
			_pairManager.MarkPairOffline(dto.User);
		});
		return Task.CompletedTask;
	}

	public Task Client_UserSendOnline(OnlineUserIdentDto dto)
	{
		base.Logger.LogDebug("Client_UserSendOnline: {dto}", dto);
		ExecuteSafely(delegate
		{
			_pairManager.MarkPairOnline(dto);
		});
		return Task.CompletedTask;
	}

	public Task Client_UserUpdateDefaultPermissions(DefaultPermissionsDto dto)
	{
		base.Logger.LogDebug("Client_UserUpdateDefaultPermissions: {dto}", dto);
		_connectionDto.DefaultPreferredPermissions = dto;
		return Task.CompletedTask;
	}

	public Task Client_UserUpdateOtherPairPermissions(UserPermissionsDto dto)
	{
		base.Logger.LogDebug("Client_UserUpdateOtherPairPermissions: {dto}", dto);
		ExecuteSafely(delegate
		{
			_pairManager.UpdatePairPermissions(dto);
		});
		return Task.CompletedTask;
	}

	public Task Client_UserUpdateProfile(UserDto dto)
	{
		base.Logger.LogDebug("Client_UserUpdateProfile: {dto}", dto);
		ExecuteSafely(delegate
		{
			base.Mediator.Publish(new ClearProfileDataMessage(dto.User));
		});
		return Task.CompletedTask;
	}

	public Task Client_UserUpdateSelfPairPermissions(UserPermissionsDto dto)
	{
		base.Logger.LogDebug("Client_UserUpdateSelfPairPermissions: {dto}", dto);
		ExecuteSafely(delegate
		{
			_pairManager.UpdateSelfPairPermissions(dto);
		});
		return Task.CompletedTask;
	}

	public Task Client_GposeLobbyJoin(UserData userData)
	{
		base.Logger.LogDebug("Client_GposeLobbyJoin: {dto}", userData);
		ExecuteSafely(delegate
		{
			base.Mediator.Publish(new GposeLobbyUserJoin(userData));
		});
		return Task.CompletedTask;
	}

	public Task Client_GposeLobbyLeave(UserData userData)
	{
		base.Logger.LogDebug("Client_GposeLobbyLeave: {dto}", userData);
		ExecuteSafely(delegate
		{
			base.Mediator.Publish(new GPoseLobbyUserLeave(userData));
		});
		return Task.CompletedTask;
	}

	public Task Client_GposeLobbyPushCharacterData(CharaDataDownloadDto charaDownloadDto)
	{
		base.Logger.LogDebug("Client_GposeLobbyPushCharacterData: {dto}", charaDownloadDto.Uploader);
		ExecuteSafely(delegate
		{
			base.Mediator.Publish(new GPoseLobbyReceiveCharaData(charaDownloadDto));
		});
		return Task.CompletedTask;
	}

	public Task Client_GposeLobbyPushPoseData(UserData userData, PoseData poseData)
	{
		base.Logger.LogDebug("Client_GposeLobbyPushPoseData: {dto}", userData);
		ExecuteSafely(delegate
		{
			base.Mediator.Publish(new GPoseLobbyReceivePoseData(userData, poseData));
		});
		return Task.CompletedTask;
	}

	public Task Client_GposeLobbyPushWorldData(UserData userData, WorldData worldData)
	{
		ExecuteSafely(delegate
		{
			base.Mediator.Publish(new GPoseLobbyReceiveWorldData(userData, worldData));
		});
		return Task.CompletedTask;
	}

	public void OnDownloadReady(Action<Guid> act)
	{
		if (!_initialized)
		{
			_mareHub.On("Client_DownloadReady", act);
		}
	}

	public void OnGroupChangePermissions(Action<GroupPermissionDto> act)
	{
		if (!_initialized)
		{
			_mareHub.On("Client_GroupChangePermissions", act);
		}
	}

	public void OnGroupChangeUserPairPermissions(Action<GroupPairUserPermissionDto> act)
	{
		if (!_initialized)
		{
			_mareHub.On("Client_GroupChangeUserPairPermissions", act);
		}
	}

	public void OnGroupDelete(Action<GroupDto> act)
	{
		if (!_initialized)
		{
			_mareHub.On("Client_GroupDelete", act);
		}
	}

	public void OnGroupPairChangeUserInfo(Action<GroupPairUserInfoDto> act)
	{
		if (!_initialized)
		{
			_mareHub.On("Client_GroupPairChangeUserInfo", act);
		}
	}

	public void OnGroupPairJoined(Action<GroupPairFullInfoDto> act)
	{
		if (!_initialized)
		{
			_mareHub.On("Client_GroupPairJoined", act);
		}
	}

	public void OnGroupPairLeft(Action<GroupPairDto> act)
	{
		if (!_initialized)
		{
			_mareHub.On("Client_GroupPairLeft", act);
		}
	}

	public void OnGroupSendFullInfo(Action<GroupFullInfoDto> act)
	{
		if (!_initialized)
		{
			_mareHub.On("Client_GroupSendFullInfo", act);
		}
	}

	public void OnGroupSendInfo(Action<GroupInfoDto> act)
	{
		if (!_initialized)
		{
			_mareHub.On("Client_GroupSendInfo", act);
		}
	}

	public void OnReceiveServerMessage(Action<MessageSeverity, string> act)
	{
		if (!_initialized)
		{
			_mareHub.On("Client_ReceiveServerMessage", act);
		}
	}

	public void OnUpdateSystemInfo(Action<SystemInfoDto> act)
	{
		if (!_initialized)
		{
			_mareHub.On("Client_UpdateSystemInfo", act);
		}
	}

	public void OnUpdateUserIndividualPairStatusDto(Action<UserIndividualPairStatusDto> action)
	{
		if (!_initialized)
		{
			_mareHub.On("Client_UpdateUserIndividualPairStatusDto", action);
		}
	}

	public void OnUserAddClientPair(Action<UserPairDto> act)
	{
		if (!_initialized)
		{
			_mareHub.On("Client_UserAddClientPair", act);
		}
	}

	public void OnUserDefaultPermissionUpdate(Action<DefaultPermissionsDto> act)
	{
		if (!_initialized)
		{
			_mareHub.On("Client_UserUpdateDefaultPermissions", act);
		}
	}

	public void OnUserReceiveCharacterData(Action<OnlineUserCharaDataDto> act)
	{
		if (!_initialized)
		{
			_mareHub.On("Client_UserReceiveCharacterData", act);
		}
	}

	public void OnUserReceiveUploadStatus(Action<UserDto> act)
	{
		if (!_initialized)
		{
			_mareHub.On("Client_UserReceiveUploadStatus", act);
		}
	}

	public void OnUserRemoveClientPair(Action<UserDto> act)
	{
		if (!_initialized)
		{
			_mareHub.On("Client_UserRemoveClientPair", act);
		}
	}

	public void OnUserSendOffline(Action<UserDto> act)
	{
		if (!_initialized)
		{
			_mareHub.On("Client_UserSendOffline", act);
		}
	}

	public void OnUserSendOnline(Action<OnlineUserIdentDto> act)
	{
		if (!_initialized)
		{
			_mareHub.On("Client_UserSendOnline", act);
		}
	}

	public void OnUserUpdateOtherPairPermissions(Action<UserPermissionsDto> act)
	{
		if (!_initialized)
		{
			_mareHub.On("Client_UserUpdateOtherPairPermissions", act);
		}
	}

	public void OnUserUpdateProfile(Action<UserDto> act)
	{
		if (!_initialized)
		{
			_mareHub.On("Client_UserUpdateProfile", act);
		}
	}

	public void OnUserUpdateSelfPairPermissions(Action<UserPermissionsDto> act)
	{
		if (!_initialized)
		{
			_mareHub.On("Client_UserUpdateSelfPairPermissions", act);
		}
	}

	public void OnGposeLobbyJoin(Action<UserData> act)
	{
		if (!_initialized)
		{
			_mareHub.On("Client_GposeLobbyJoin", act);
		}
	}

	public void OnGposeLobbyLeave(Action<UserData> act)
	{
		if (!_initialized)
		{
			_mareHub.On("Client_GposeLobbyLeave", act);
		}
	}

	public void OnGposeLobbyPushCharacterData(Action<CharaDataDownloadDto> act)
	{
		if (!_initialized)
		{
			_mareHub.On("Client_GposeLobbyPushCharacterData", act);
		}
	}

	public void OnGposeLobbyPushPoseData(Action<UserData, PoseData> act)
	{
		if (!_initialized)
		{
			_mareHub.On("Client_GposeLobbyPushPoseData", act);
		}
	}

	public void OnGposeLobbyPushWorldData(Action<UserData, WorldData> act)
	{
		if (!_initialized)
		{
			_mareHub.On("Client_GposeLobbyPushWorldData", act);
		}
	}

	private void ExecuteSafely(Action act)
	{
		try
		{
			act();
		}
		catch (Exception ex)
		{
			base.Logger.LogCritical(ex, "Error on executing safely");
		}
	}

	public async Task<CharaDataFullDto?> CharaDataCreate()
	{
		if (!IsConnected)
		{
			return null;
		}
		try
		{
			base.Logger.LogDebug("Creating new Character Data");
			return await _mareHub.InvokeAsync<CharaDataFullDto>("CharaDataCreate").ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex)
		{
			base.Logger.LogWarning(ex, "Failed to create new character data");
			return null;
		}
	}

	public async Task<CharaDataFullDto?> CharaDataUpdate(CharaDataUpdateDto updateDto)
	{
		if (!IsConnected)
		{
			return null;
		}
		try
		{
			base.Logger.LogDebug("Updating chara data for {id}", updateDto.Id);
			return await _mareHub.InvokeAsync<CharaDataFullDto>("CharaDataUpdate", updateDto).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex)
		{
			base.Logger.LogWarning(ex, "Failed to update chara data for {id}", updateDto.Id);
			return null;
		}
	}

	public async Task<bool> CharaDataDelete(string id)
	{
		if (!IsConnected)
		{
			return false;
		}
		try
		{
			base.Logger.LogDebug("Deleting chara data for {id}", id);
			return await _mareHub.InvokeAsync<bool>("CharaDataDelete", id).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex)
		{
			base.Logger.LogWarning(ex, "Failed to delete chara data for {id}", id);
			return false;
		}
	}

	public async Task<CharaDataMetaInfoDto?> CharaDataGetMetainfo(string id)
	{
		if (!IsConnected)
		{
			return null;
		}
		try
		{
			base.Logger.LogDebug("Getting metainfo for chara data {id}", id);
			return await _mareHub.InvokeAsync<CharaDataMetaInfoDto>("CharaDataGetMetainfo", id).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex)
		{
			base.Logger.LogWarning(ex, "Failed to get meta info for chara data {id}", id);
			return null;
		}
	}

	public async Task<CharaDataFullDto?> CharaDataAttemptRestore(string id)
	{
		if (!IsConnected)
		{
			return null;
		}
		try
		{
			base.Logger.LogDebug("Attempting to restore chara data {id}", id);
			return await _mareHub.InvokeAsync<CharaDataFullDto>("CharaDataAttemptRestore", id).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex)
		{
			base.Logger.LogWarning(ex, "Failed to restore chara data for {id}", id);
			return null;
		}
	}

	public async Task<List<CharaDataFullDto>> CharaDataGetOwn()
	{
		if (!IsConnected)
		{
			return new List<CharaDataFullDto>();
		}
		try
		{
			base.Logger.LogDebug("Getting all own chara data");
			return await _mareHub.InvokeAsync<List<CharaDataFullDto>>("CharaDataGetOwn").ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex)
		{
			base.Logger.LogWarning(ex, "Failed to get own chara data");
			return new List<CharaDataFullDto>();
		}
	}

	public async Task<List<CharaDataMetaInfoDto>> CharaDataGetShared()
	{
		if (!IsConnected)
		{
			return new List<CharaDataMetaInfoDto>();
		}
		try
		{
			base.Logger.LogDebug("Getting all own chara data");
			return await _mareHub.InvokeAsync<List<CharaDataMetaInfoDto>>("CharaDataGetShared").ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex)
		{
			base.Logger.LogWarning(ex, "Failed to get shared chara data");
			return new List<CharaDataMetaInfoDto>();
		}
	}

	public async Task<CharaDataDownloadDto?> CharaDataDownload(string id)
	{
		if (!IsConnected)
		{
			return null;
		}
		try
		{
			base.Logger.LogDebug("Getting download chara data for {id}", id);
			return await _mareHub.InvokeAsync<CharaDataDownloadDto>("CharaDataDownload", id).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex)
		{
			base.Logger.LogWarning(ex, "Failed to get download chara data for {id}", id);
			return null;
		}
	}

	public async Task<string> GposeLobbyCreate()
	{
		if (!IsConnected)
		{
			return string.Empty;
		}
		try
		{
			base.Logger.LogDebug("Creating GPose Lobby");
			return await _mareHub.InvokeAsync<string>("GposeLobbyCreate").ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex)
		{
			base.Logger.LogWarning(ex, "Failed to create GPose lobby");
			return string.Empty;
		}
	}

	public async Task<bool> GposeLobbyLeave()
	{
		if (!IsConnected)
		{
			return true;
		}
		try
		{
			base.Logger.LogDebug("Leaving current GPose Lobby");
			return await _mareHub.InvokeAsync<bool>("GposeLobbyLeave").ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex)
		{
			base.Logger.LogWarning(ex, "Failed to leave GPose lobby");
			return false;
		}
	}

	public async Task<List<UserData>> GposeLobbyJoin(string lobbyId)
	{
		if (!IsConnected)
		{
			return new List<UserData>();
		}
		try
		{
			base.Logger.LogDebug("Joining GPose Lobby {id}", lobbyId);
			return await _mareHub.InvokeAsync<List<UserData>>("GposeLobbyJoin", lobbyId).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex)
		{
			base.Logger.LogWarning(ex, "Failed to join GPose lobby {id}", lobbyId);
			return new List<UserData>();
		}
	}

	public async Task GposeLobbyPushCharacterData(CharaDataDownloadDto charaDownloadDto)
	{
		if (!IsConnected)
		{
			return;
		}
		try
		{
			base.Logger.LogDebug("Sending Chara Data to GPose Lobby");
			await _mareHub.InvokeAsync("GposeLobbyPushCharacterData", charaDownloadDto).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex)
		{
			base.Logger.LogWarning(ex, "Failed to send Chara Data to GPose lobby");
		}
	}

	public async Task GposeLobbyPushPoseData(PoseData poseData)
	{
		if (!IsConnected)
		{
			return;
		}
		try
		{
			base.Logger.LogDebug("Sending Pose Data to GPose Lobby");
			await _mareHub.InvokeAsync("GposeLobbyPushPoseData", poseData).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex)
		{
			base.Logger.LogWarning(ex, "Failed to send Pose Data to GPose lobby");
		}
	}

	public async Task GposeLobbyPushWorldData(WorldData worldData)
	{
		if (!IsConnected)
		{
			return;
		}
		try
		{
			await _mareHub.InvokeAsync("GposeLobbyPushWorldData", worldData).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex)
		{
			base.Logger.LogWarning(ex, "Failed to send World Data to GPose lobby");
		}
	}

	public async Task GroupBanUser(GroupPairDto dto, string reason)
	{
		CheckConnection();
		await _mareHub.SendAsync("GroupBanUser", dto, reason).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task GroupChangeGroupPermissionState(GroupPermissionDto dto)
	{
		CheckConnection();
		await _mareHub.SendAsync("GroupChangeGroupPermissionState", dto).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task GroupChangeIndividualPermissionState(GroupPairUserPermissionDto dto)
	{
		CheckConnection();
		await SetBulkPermissions(new BulkPermissionsDto(new Dictionary<string, UserPermissions>(StringComparer.Ordinal), new Dictionary<string, GroupUserPreferredPermissions>(StringComparer.Ordinal) { 
		{
			dto.Group.GID,
			dto.GroupPairPermissions
		} })).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task GroupChangeOwnership(GroupPairDto groupPair)
	{
		CheckConnection();
		await _mareHub.SendAsync("GroupChangeOwnership", groupPair).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<bool> GroupChangePassword(GroupPasswordDto groupPassword)
	{
		CheckConnection();
		return await _mareHub.InvokeAsync<bool>("GroupChangePassword", groupPassword).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task GroupClear(GroupDto group)
	{
		CheckConnection();
		await _mareHub.SendAsync("GroupClear", group).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<GroupJoinDto> GroupCreate()
	{
		CheckConnection();
		return await _mareHub.InvokeAsync<GroupJoinDto>("GroupCreate").ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<List<string>> GroupCreateTempInvite(GroupDto group, int amount)
	{
		CheckConnection();
		return await _mareHub.InvokeAsync<List<string>>("GroupCreateTempInvite", group, amount).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task GroupDelete(GroupDto group)
	{
		CheckConnection();
		await _mareHub.SendAsync("GroupDelete", group).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<List<BannedGroupUserDto>> GroupGetBannedUsers(GroupDto group)
	{
		CheckConnection();
		return await _mareHub.InvokeAsync<List<BannedGroupUserDto>>("GroupGetBannedUsers", group).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<GroupJoinInfoDto> GroupJoin(GroupPasswordDto passwordedGroup)
	{
		CheckConnection();
		return await _mareHub.InvokeAsync<GroupJoinInfoDto>("GroupJoin", passwordedGroup).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<bool> GroupJoinFinalize(GroupJoinDto passwordedGroup)
	{
		CheckConnection();
		return await _mareHub.InvokeAsync<bool>("GroupJoinFinalize", passwordedGroup).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task GroupLeave(GroupDto group)
	{
		CheckConnection();
		await _mareHub.SendAsync("GroupLeave", group).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task GroupRemoveUser(GroupPairDto groupPair)
	{
		CheckConnection();
		await _mareHub.SendAsync("GroupRemoveUser", groupPair).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task GroupSetUserInfo(GroupPairUserInfoDto groupPair)
	{
		CheckConnection();
		await _mareHub.SendAsync("GroupSetUserInfo", groupPair).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<int> GroupPrune(GroupDto group, int days, bool execute)
	{
		CheckConnection();
		return await _mareHub.InvokeAsync<int>("GroupPrune", group, days, execute).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<List<GroupFullInfoDto>> GroupsGetAll()
	{
		CheckConnection();
		return await _mareHub.InvokeAsync<List<GroupFullInfoDto>>("GroupsGetAll").ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task GroupUnbanUser(GroupPairDto groupPair)
	{
		CheckConnection();
		await _mareHub.SendAsync("GroupUnbanUser", groupPair).ConfigureAwait(continueOnCapturedContext: false);
	}

	private void CheckConnection()
    {
        ServerState serverState = ServerState;
        if (((uint)(serverState - 1) > 1u && serverState != ServerState.Connected) || 1 == 0)
		{
			throw new InvalidDataException("Not connected");
		}
	}

	public async Task PushCharacterData(CharacterData data, List<UserData> visibleCharacters)
	{
		if (!IsConnected)
		{
			return;
		}
		try
		{
			await PushCharacterDataInternal(data, visibleCharacters.ToList()).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (OperationCanceledException)
		{
			base.Logger.LogDebug("Upload operation was cancelled");
		}
		catch (Exception ex)
		{
			base.Logger.LogWarning(ex, "Error during upload of files");
		}
	}

	public async Task UserAddPair(UserDto user)
	{
		if (IsConnected)
		{
			await _mareHub.SendAsync("UserAddPair", user).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	public async Task UserDelete()
	{
		CheckConnection();
		await _mareHub.SendAsync("UserDelete").ConfigureAwait(continueOnCapturedContext: false);
		await CreateConnectionsAsync().ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<List<OnlineUserIdentDto>> UserGetOnlinePairs(CensusDataDto? censusDataDto)
	{
		return await _mareHub.InvokeAsync<List<OnlineUserIdentDto>>("UserGetOnlinePairs", censusDataDto).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<List<UserFullPairDto>> UserGetPairedClients()
	{
		return await _mareHub.InvokeAsync<List<UserFullPairDto>>("UserGetPairedClients").ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<UserProfileDto> UserGetProfile(UserDto dto)
	{
		if (!IsConnected)
		{
			return new UserProfileDto(dto.User, Disabled: false, null, null, null);
		}
		return await _mareHub.InvokeAsync<UserProfileDto>("UserGetProfile", dto).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task UserPushData(UserCharaDataMessageDto dto)
	{
		try
		{
			await _mareHub.InvokeAsync("UserPushData", dto).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex)
		{
			base.Logger.LogWarning(ex, "Failed to Push character data");
		}
	}

	public async Task SetBulkPermissions(BulkPermissionsDto dto)
	{
		CheckConnection();
		try
		{
			await _mareHub.InvokeAsync("SetBulkPermissions", dto).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex)
		{
			base.Logger.LogWarning(ex, "Failed to set permissions");
		}
	}

	public async Task UserRemovePair(UserDto userDto)
	{
		if (IsConnected)
		{
			await _mareHub.SendAsync("UserRemovePair", userDto).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	public async Task UserSetPairPermissions(UserPermissionsDto userPermissions)
	{
		await SetBulkPermissions(new BulkPermissionsDto(new Dictionary<string, UserPermissions>(StringComparer.Ordinal) { 
		{
			userPermissions.User.UID,
			userPermissions.Permissions
		} }, new Dictionary<string, GroupUserPreferredPermissions>(StringComparer.Ordinal))).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task UserSetProfile(UserProfileDto userDescription)
	{
		if (IsConnected)
		{
			await _mareHub.InvokeAsync("UserSetProfile", userDescription).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	public async Task UserUpdateDefaultPermissions(DefaultPermissionsDto defaultPermissionsDto)
	{
		CheckConnection();
		await _mareHub.InvokeAsync("UserUpdateDefaultPermissions", defaultPermissionsDto).ConfigureAwait(continueOnCapturedContext: false);
	}

	private async Task PushCharacterDataInternal(CharacterData character, List<UserData> visibleCharacters)
	{
		base.Logger.LogInformation("Pushing character data for {hash} to {charas}", character.DataHash.Value, string.Join(", ", visibleCharacters.Select((UserData c) => c.AliasOrUID)));
		StringBuilder sb = new StringBuilder();
		foreach (KeyValuePair<ObjectKind, List<FileReplacementData>> kvp in character.FileReplacements.ToList())
		{
			StringBuilder stringBuilder = sb;
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(23, 2, stringBuilder);
			handler.AppendLiteral("FileReplacements for ");
			handler.AppendFormatted(kvp.Key);
			handler.AppendLiteral(": ");
			handler.AppendFormatted(kvp.Value.Count);
			stringBuilder2.AppendLine(ref handler);
		}
		foreach (KeyValuePair<ObjectKind, string> item in character.GlamourerData)
		{
			StringBuilder stringBuilder = sb;
			StringBuilder stringBuilder3 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(20, 2, stringBuilder);
			handler.AppendLiteral("GlamourerData for ");
			handler.AppendFormatted(item.Key);
			handler.AppendLiteral(": ");
			handler.AppendFormatted(!string.IsNullOrEmpty(item.Value));
			stringBuilder3.AppendLine(ref handler);
		}
		base.Logger.LogDebug("Chara data contained: {nl} {data}", Environment.NewLine, sb.ToString());
		CensusDataDto censusDto = null;
		if (_serverManager.SendCensusData && _lastCensus != null)
		{
			censusDto = new CensusDataDto((ushort)(await _dalamudUtil.GetWorldIdAsync().ConfigureAwait(continueOnCapturedContext: false)), _lastCensus.RaceId, _lastCensus.TribeId, _lastCensus.Gender);
			base.Logger.LogDebug("Attaching Census Data: {data}", censusDto);
		}
		await UserPushData(new UserCharaDataMessageDto(visibleCharacters, character, censusDto)).ConfigureAwait(continueOnCapturedContext: false);
	}
}
