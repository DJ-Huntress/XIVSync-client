using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Config;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XIVSync.API.Dto.CharaData;
using XIVSync.Interop;
using XIVSync.MareConfiguration;
using XIVSync.PlayerData.Handlers;
using XIVSync.Services.Mediator;
using XIVSync.Utils;

namespace XIVSync.Services;

public class DalamudUtilService : IHostedService, IMediatorSubscriber
{
	private readonly List<uint> _classJobIdsIgnoredForPets;

	private readonly IClientState _clientState;

	private readonly ICondition _condition;

	private readonly IDataManager _gameData;

	private readonly IGameConfig _gameConfig;

	private readonly BlockedCharacterHandler _blockedCharacterHandler;

	private readonly IFramework _framework;

	private readonly IGameGui _gameGui;

	private readonly ILogger<DalamudUtilService> _logger;

	private readonly IObjectTable _objectTable;

	private readonly PerformanceCollectorService _performanceCollector;

	private readonly MareConfigService _configService;

	private uint? _classJobId;

	private DateTime _delayedFrameworkUpdateCheck;

	private string _lastGlobalBlockPlayer;

	private string _lastGlobalBlockReason;

	private ushort _lastZone;

	private readonly Dictionary<string, (string Name, nint Address)> _playerCharas;

	private readonly List<string> _notUpdatedCharas;

	private bool _sentBetweenAreas;

	private Lazy<ulong> _cid;

	public bool IsWine { get; init; }

	public unsafe GameObject* GposeTarget
	{
		get
		{
			return TargetSystem.Instance()->GPoseTarget;
		}
		set
		{
			TargetSystem.Instance()->GPoseTarget = value;
		}
	}

	private unsafe bool HasGposeTarget => GposeTarget != null;

	private unsafe int GPoseTargetIdx
	{
		get
		{
			if (HasGposeTarget)
			{
				return GposeTarget->ObjectIndex;
			}
			return -1;
		}
	}

	public bool IsAnythingDrawing { get; private set; }

	public bool IsInCutscene { get; private set; }

	public bool IsInGpose { get; private set; }

	public bool IsLoggedIn { get; private set; }

	public bool IsOnFrameworkThread => _framework.IsInFrameworkUpdateThread;

	public bool IsZoning
	{
		get
		{
			if (!_condition[ConditionFlag.BetweenAreas])
			{
				return _condition[ConditionFlag.BetweenAreas51];
			}
			return true;
		}
	}

	public bool IsInCombatOrPerforming { get; private set; }

	public bool IsInInstance { get; private set; }

	public bool HasModifiedGameFiles => _gameData.HasModifiedGameDataFiles;

	public uint ClassJobId => _classJobId.Value;

	public Lazy<Dictionary<uint, string>> JobData { get; private set; }

	public Lazy<Dictionary<ushort, string>> WorldData { get; private set; }

	public Lazy<Dictionary<uint, string>> TerritoryData { get; private set; }

	public Lazy<Dictionary<uint, (Map Map, string MapName)>> MapData { get; private set; }

	public bool IsLodEnabled { get; private set; }

	public MareMediator Mediator { get; }

	public DalamudUtilService(ILogger<DalamudUtilService> logger, IClientState clientState, IObjectTable objectTable, IFramework framework, IGameGui gameGui, ICondition condition, IDataManager gameData, ITargetManager targetManager, IGameConfig gameConfig, BlockedCharacterHandler blockedCharacterHandler, MareMediator mediator, PerformanceCollectorService performanceCollector, MareConfigService configService)
	{
		int num = 1;
		List<uint> list = new List<uint>(num);
		CollectionsMarshal.SetCount(list, num);
		CollectionsMarshal.AsSpan(list)[0] = 30u;
		_classJobIdsIgnoredForPets = list;
		_classJobId = 0u;
		_delayedFrameworkUpdateCheck = DateTime.UtcNow;
		_lastGlobalBlockPlayer = string.Empty;
		_lastGlobalBlockReason = string.Empty;
		_playerCharas = new Dictionary<string, (string, nint)>(StringComparer.Ordinal);
		_notUpdatedCharas = new List<string>();
		base._002Ector();
		DalamudUtilService dalamudUtilService = this;
		_logger = logger;
		_clientState = clientState;
		_objectTable = objectTable;
		_framework = framework;
		_gameGui = gameGui;
		_condition = condition;
		_gameData = gameData;
		_gameConfig = gameConfig;
		_blockedCharacterHandler = blockedCharacterHandler;
		Mediator = mediator;
		_performanceCollector = performanceCollector;
		_configService = configService;
		WorldData = new Lazy<Dictionary<ushort, string>>(() => (from w in gameData.GetExcelSheet<Lumina.Excel.Sheets.World>(ClientLanguage.English)
			where !w.Name.IsEmpty && w.DataCenter.RowId != 0 && (w.IsPublic || char.IsUpper(w.Name.ToString()[0]))
			select w).ToDictionary((Lumina.Excel.Sheets.World w) => (ushort)w.RowId, (Lumina.Excel.Sheets.World w) => w.Name.ToString()));
		JobData = new Lazy<Dictionary<uint, string>>(() => gameData.GetExcelSheet<ClassJob>(ClientLanguage.English).ToDictionary((ClassJob k) => k.RowId, (ClassJob k) => k.NameEnglish.ToString()));
		TerritoryData = new Lazy<Dictionary<uint, string>>(() => (from w in gameData.GetExcelSheet<TerritoryType>(ClientLanguage.English)
			where w.RowId != 0
			select w).ToDictionary((TerritoryType w) => w.RowId, delegate(TerritoryType w)
		{
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.Append(w.PlaceNameRegion.Value.Name);
			if (w.PlaceName.ValueNullable.HasValue)
			{
				stringBuilder.Append(" - ");
				stringBuilder.Append(w.PlaceName.Value.Name);
			}
			return stringBuilder.ToString();
		}));
		MapData = new Lazy<Dictionary<uint, (Map, string)>>(() => (from w in gameData.GetExcelSheet<Map>(ClientLanguage.English)
			where w.RowId != 0
			select w).ToDictionary((Map w) => w.RowId, delegate(Map w)
		{
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.Append(w.PlaceNameRegion.Value.Name);
			if (w.PlaceName.ValueNullable.HasValue)
			{
				stringBuilder.Append(" - ");
				stringBuilder.Append(w.PlaceName.Value.Name);
			}
			if (w.PlaceNameSub.ValueNullable.HasValue && !string.IsNullOrEmpty(w.PlaceNameSub.Value.Name.ToString()))
			{
				stringBuilder.Append(" - ");
				stringBuilder.Append(w.PlaceNameSub.Value.Name);
			}
			return (w: w, stringBuilder.ToString());
		}));
		mediator.Subscribe(this, delegate(TargetPairMessage msg)
		{
			if (!clientState.IsPvP)
			{
				string name = msg.Pair.PlayerName;
				if (!string.IsNullOrEmpty(name))
				{
					nint addr = dalamudUtilService._playerCharas.FirstOrDefault<KeyValuePair<string, (string, nint)>>((KeyValuePair<string, (string Name, nint Address)> f) => string.Equals(f.Value.Name, name, StringComparison.Ordinal)).Value.Item2;
					if (addr != IntPtr.Zero)
					{
						bool useFocusTarget = dalamudUtilService._configService.Current.UseFocusTarget;
						dalamudUtilService.RunOnFrameworkThread(delegate
						{
							if (useFocusTarget)
							{
								targetManager.FocusTarget = dalamudUtilService.CreateGameObject(addr);
							}
							else
							{
								targetManager.Target = dalamudUtilService.CreateGameObject(addr);
							}
						}, ".ctor", "\\\\wsl.localhost\\Ubuntu\\home\\ddev\\xivsync\\sync_client2\\XIVSync\\Services\\DalamudUtilService.cs", 126).ConfigureAwait(continueOnCapturedContext: false);
					}
				}
			}
		});
		IsWine = Util.IsWine();
		_cid = RebuildCID();
	}

	private Lazy<ulong> RebuildCID()
	{
		return new Lazy<ulong>(GetCID);
	}

	public async Task<IGameObject?> GetGposeTargetGameObjectAsync()
	{
		if (!HasGposeTarget)
		{
			return null;
		}
		return await _framework.RunOnFrameworkThread(() => _objectTable[GPoseTargetIdx]).ConfigureAwait(continueOnCapturedContext: true);
	}

	public IGameObject? CreateGameObject(nint reference)
	{
		EnsureIsOnFramework();
		return _objectTable.CreateObjectReference(reference);
	}

	public async Task<IGameObject?> CreateGameObjectAsync(nint reference)
	{
		return await RunOnFrameworkThread(() => _objectTable.CreateObjectReference(reference), "CreateGameObjectAsync", "\\\\wsl.localhost\\Ubuntu\\home\\ddev\\xivsync\\sync_client2\\XIVSync\\Services\\DalamudUtilService.cs", 183).ConfigureAwait(continueOnCapturedContext: false);
	}

	public void EnsureIsOnFramework()
	{
		if (!_framework.IsInFrameworkUpdateThread)
		{
			throw new InvalidOperationException("Can only be run on Framework");
		}
	}

	public ICharacter? GetCharacterFromObjectTableByIndex(int index)
	{
		EnsureIsOnFramework();
		IGameObject objTableObj = _objectTable[index];
		if (objTableObj.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player)
		{
			return null;
		}
		return (ICharacter)objTableObj;
	}

	public unsafe nint GetCompanionPtr(nint? playerPointer = null)
	{
		EnsureIsOnFramework();
		CharacterManager* mgr = CharacterManager.Instance();
		nint valueOrDefault = playerPointer.GetValueOrDefault();
		if (!playerPointer.HasValue)
		{
			valueOrDefault = GetPlayerPtr();
			playerPointer = valueOrDefault;
		}
		if (playerPointer == IntPtr.Zero || mgr == (CharacterManager*)IntPtr.Zero)
		{
			return IntPtr.Zero;
		}
		nint? num = playerPointer;
		return (nint)mgr->LookupBuddyByOwnerObject((BattleChara*)(num.HasValue ? ((void*)num.GetValueOrDefault()) : null));
	}

	public async Task<nint> GetCompanionAsync(nint? playerPointer = null)
	{
		return await RunOnFrameworkThread(() => GetCompanionPtr(playerPointer), "GetCompanionAsync", "\\\\wsl.localhost\\Ubuntu\\home\\ddev\\xivsync\\sync_client2\\XIVSync\\Services\\DalamudUtilService.cs", 210).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<ICharacter?> GetGposeCharacterFromObjectTableByNameAsync(string name, bool onlyGposeCharacters = false)
	{
		return await RunOnFrameworkThread(() => GetGposeCharacterFromObjectTableByName(name, onlyGposeCharacters), "GetGposeCharacterFromObjectTableByNameAsync", "\\\\wsl.localhost\\Ubuntu\\home\\ddev\\xivsync\\sync_client2\\XIVSync\\Services\\DalamudUtilService.cs", 215).ConfigureAwait(continueOnCapturedContext: false);
	}

	public ICharacter? GetGposeCharacterFromObjectTableByName(string name, bool onlyGposeCharacters = false)
	{
		EnsureIsOnFramework();
		return (ICharacter)_objectTable.FirstOrDefault((IGameObject i) => (!onlyGposeCharacters || i.ObjectIndex >= 200) && string.Equals(i.Name.ToString(), name, StringComparison.Ordinal));
	}

	public IEnumerable<ICharacter?> GetGposeCharactersFromObjectTable()
	{
		return _objectTable.Where((IGameObject o) => o.ObjectIndex > 200 && o.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player).Cast<ICharacter>();
	}

	public bool GetIsPlayerPresent()
	{
		EnsureIsOnFramework();
		if (_clientState.LocalPlayer != null)
		{
			return _clientState.LocalPlayer.IsValid();
		}
		return false;
	}

	public async Task<bool> GetIsPlayerPresentAsync()
	{
		return await RunOnFrameworkThread((Func<bool>)GetIsPlayerPresent, "GetIsPlayerPresentAsync", "\\\\wsl.localhost\\Ubuntu\\home\\ddev\\xivsync\\sync_client2\\XIVSync\\Services\\DalamudUtilService.cs", 238).ConfigureAwait(continueOnCapturedContext: false);
	}

	public unsafe nint GetMinionOrMountPtr(nint? playerPointer = null)
	{
		EnsureIsOnFramework();
		nint valueOrDefault = playerPointer.GetValueOrDefault();
		if (!playerPointer.HasValue)
		{
			valueOrDefault = GetPlayerPtr();
			playerPointer = valueOrDefault;
		}
		if (playerPointer == IntPtr.Zero)
		{
			return IntPtr.Zero;
		}
		IObjectTable objectTable = _objectTable;
		nint? num = playerPointer;
		return objectTable.GetObjectAddress(((GameObject*)(num.HasValue ? ((void*)num.GetValueOrDefault()) : null))->ObjectIndex + 1);
	}

	public async Task<nint> GetMinionOrMountAsync(nint? playerPointer = null)
	{
		return await RunOnFrameworkThread(() => GetMinionOrMountPtr(playerPointer), "GetMinionOrMountAsync", "\\\\wsl.localhost\\Ubuntu\\home\\ddev\\xivsync\\sync_client2\\XIVSync\\Services\\DalamudUtilService.cs", 251).ConfigureAwait(continueOnCapturedContext: false);
	}

	public unsafe nint GetPetPtr(nint? playerPointer = null)
	{
		EnsureIsOnFramework();
		if (_classJobIdsIgnoredForPets.Contains(_classJobId.GetValueOrDefault()))
		{
			return IntPtr.Zero;
		}
		CharacterManager* mgr = CharacterManager.Instance();
		nint valueOrDefault = playerPointer.GetValueOrDefault();
		if (!playerPointer.HasValue)
		{
			valueOrDefault = GetPlayerPtr();
			playerPointer = valueOrDefault;
		}
		if (playerPointer == IntPtr.Zero || mgr == (CharacterManager*)IntPtr.Zero)
		{
			return IntPtr.Zero;
		}
		nint? num = playerPointer;
		return (nint)mgr->LookupPetByOwnerObject((BattleChara*)(num.HasValue ? ((void*)num.GetValueOrDefault()) : null));
	}

	public async Task<nint> GetPetAsync(nint? playerPointer = null)
	{
		return await RunOnFrameworkThread(() => GetPetPtr(playerPointer), "GetPetAsync", "\\\\wsl.localhost\\Ubuntu\\home\\ddev\\xivsync\\sync_client2\\XIVSync\\Services\\DalamudUtilService.cs", 266).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<IPlayerCharacter> GetPlayerCharacterAsync()
	{
		return await RunOnFrameworkThread((Func<IPlayerCharacter>)GetPlayerCharacter, "GetPlayerCharacterAsync", "\\\\wsl.localhost\\Ubuntu\\home\\ddev\\xivsync\\sync_client2\\XIVSync\\Services\\DalamudUtilService.cs", 271).ConfigureAwait(continueOnCapturedContext: false);
	}

	public IPlayerCharacter GetPlayerCharacter()
	{
		EnsureIsOnFramework();
		return _clientState.LocalPlayer;
	}

	public nint GetPlayerCharacterFromCachedTableByIdent(string characterName)
	{
		if (_playerCharas.TryGetValue(characterName, out (string, nint) pchar))
		{
			return pchar.Item2;
		}
		return IntPtr.Zero;
	}

	public string GetPlayerName()
	{
		EnsureIsOnFramework();
		return _clientState.LocalPlayer?.Name.ToString() ?? "--";
	}

	public async Task<string> GetPlayerNameAsync()
	{
		return await RunOnFrameworkThread((Func<string>)GetPlayerName, "GetPlayerNameAsync", "\\\\wsl.localhost\\Ubuntu\\home\\ddev\\xivsync\\sync_client2\\XIVSync\\Services\\DalamudUtilService.cs", 294).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<ulong> GetCIDAsync()
	{
		return await RunOnFrameworkThread((Func<ulong>)GetCID, "GetCIDAsync", "\\\\wsl.localhost\\Ubuntu\\home\\ddev\\xivsync\\sync_client2\\XIVSync\\Services\\DalamudUtilService.cs", 299).ConfigureAwait(continueOnCapturedContext: false);
	}

	public unsafe ulong GetCID()
	{
		EnsureIsOnFramework();
		return ((BattleChara*)GetPlayerCharacter().Address)->Character.ContentId;
	}

	public async Task<string> GetPlayerNameHashedAsync()
	{
		return await RunOnFrameworkThread(() => _cid.Value.ToString().GetHash256(), "GetPlayerNameHashedAsync", "\\\\wsl.localhost\\Ubuntu\\home\\ddev\\xivsync\\sync_client2\\XIVSync\\Services\\DalamudUtilService.cs", 311).ConfigureAwait(continueOnCapturedContext: false);
	}

	private unsafe static string GetHashedCIDFromPlayerPointer(nint ptr)
	{
		return ((BattleChara*)ptr)->Character.ContentId.ToString().GetHash256();
	}

	public nint GetPlayerPtr()
	{
		EnsureIsOnFramework();
		return _clientState.LocalPlayer?.Address ?? IntPtr.Zero;
	}

	public async Task<nint> GetPlayerPointerAsync()
	{
		return await RunOnFrameworkThread((Func<nint>)GetPlayerPtr, "GetPlayerPointerAsync", "\\\\wsl.localhost\\Ubuntu\\home\\ddev\\xivsync\\sync_client2\\XIVSync\\Services\\DalamudUtilService.cs", 327).ConfigureAwait(continueOnCapturedContext: false);
	}

	public uint GetHomeWorldId()
	{
		EnsureIsOnFramework();
		return _clientState.LocalPlayer?.HomeWorld.RowId ?? 0;
	}

	public uint GetWorldId()
	{
		EnsureIsOnFramework();
		return _clientState.LocalPlayer.CurrentWorld.RowId;
	}

	public unsafe LocationInfo GetMapData()
	{
		EnsureIsOnFramework();
		AgentMap* agentMap = AgentMap.Instance();
		HousingManager* houseMan = HousingManager.Instance();
		uint serverId = 0u;
		serverId = ((_clientState.LocalPlayer != null) ? _clientState.LocalPlayer.CurrentWorld.RowId : 0u);
		uint mapId = ((agentMap != null) ? agentMap->CurrentMapId : 0u);
		uint territoryId = ((agentMap != null) ? agentMap->CurrentTerritoryId : 0u);
		uint divisionId = (uint)((houseMan != null) ? houseMan->GetCurrentDivision() : 0);
		uint wardId = ((houseMan != null) ? ((uint)(houseMan->GetCurrentWard() + 1)) : 0u);
		uint houseId = 0u;
		int tempHouseId = ((houseMan != null) ? houseMan->GetCurrentPlot() : 0);
		if (!houseMan->IsInside())
		{
			tempHouseId = 0;
		}
		if (tempHouseId < -1)
		{
			divisionId = ((tempHouseId != -127) ? 1u : 2u);
			tempHouseId = 100;
		}
		if (tempHouseId == -1)
		{
			tempHouseId = 0;
		}
		houseId = (uint)tempHouseId;
		if (houseId != 0)
		{
			territoryId = HousingManager.GetOriginalHouseTerritoryTypeId();
		}
		uint roomId = (uint)((houseMan != null) ? houseMan->GetCurrentRoom() : 0);
		return new LocationInfo
		{
			ServerId = serverId,
			MapId = mapId,
			TerritoryId = territoryId,
			DivisionId = divisionId,
			WardId = wardId,
			HouseId = houseId,
			RoomId = roomId
		};
	}

	public unsafe void SetMarkerAndOpenMap(Vector3 position, Map map)
	{
		EnsureIsOnFramework();
		AgentMap* agentMap = AgentMap.Instance();
		if (agentMap != null)
		{
			agentMap->OpenMapByMapId(map.RowId);
			agentMap->SetFlagMapMarker(map.TerritoryType.RowId, map.RowId, position);
		}
	}

	public async Task<LocationInfo> GetMapDataAsync()
	{
		return await RunOnFrameworkThread((Func<LocationInfo>)GetMapData, "GetMapDataAsync", "\\\\wsl.localhost\\Ubuntu\\home\\ddev\\xivsync\\sync_client2\\XIVSync\\Services\\DalamudUtilService.cs", 393).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<uint> GetWorldIdAsync()
	{
		return await RunOnFrameworkThread((Func<uint>)GetWorldId, "GetWorldIdAsync", "\\\\wsl.localhost\\Ubuntu\\home\\ddev\\xivsync\\sync_client2\\XIVSync\\Services\\DalamudUtilService.cs", 398).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<uint> GetHomeWorldIdAsync()
	{
		return await RunOnFrameworkThread((Func<uint>)GetHomeWorldId, "GetHomeWorldIdAsync", "\\\\wsl.localhost\\Ubuntu\\home\\ddev\\xivsync\\sync_client2\\XIVSync\\Services\\DalamudUtilService.cs", 403).ConfigureAwait(continueOnCapturedContext: false);
	}

	public bool IsGameObjectPresent(nint key)
	{
		return _objectTable.Any((IGameObject f) => f.Address == key);
	}

	public bool IsObjectPresent(IGameObject? obj)
	{
		EnsureIsOnFramework();
		return obj?.IsValid() ?? false;
	}

	public async Task<bool> IsObjectPresentAsync(IGameObject? obj)
	{
		return await RunOnFrameworkThread(() => IsObjectPresent(obj), "IsObjectPresentAsync", "\\\\wsl.localhost\\Ubuntu\\home\\ddev\\xivsync\\sync_client2\\XIVSync\\Services\\DalamudUtilService.cs", 419).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task RunOnFrameworkThread(System.Action act, [CallerMemberName] string callerMember = "", [CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0)
	{
		string fileName = Path.GetFileNameWithoutExtension(callerFilePath);
		PerformanceCollectorService performanceCollector = _performanceCollector;
		DalamudUtilService sender = this;
		MareInterpolatedStringHandler counterName = new MareInterpolatedStringHandler(21, 3);
		counterName.AppendLiteral("RunOnFramework:Act/");
		counterName.AppendFormatted(fileName);
		counterName.AppendLiteral(">");
		counterName.AppendFormatted(callerMember);
		counterName.AppendLiteral(":");
		counterName.AppendFormatted(callerLineNumber);
		await performanceCollector.LogPerformance((object)sender, counterName, (Func<Task>)async delegate
		{
			if (!_framework.IsInFrameworkUpdateThread)
			{
				await _framework.RunOnFrameworkThread(act).ContinueWith((Task _) => Task.CompletedTask).ConfigureAwait(continueOnCapturedContext: false);
				while (_framework.IsInFrameworkUpdateThread)
				{
					_logger.LogTrace("Still on framework");
					await Task.Delay(1).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
			else
			{
				act();
			}
		}, 10000).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<T> RunOnFrameworkThread<T>(Func<T> func, [CallerMemberName] string callerMember = "", [CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0)
	{
		string fileName = Path.GetFileNameWithoutExtension(callerFilePath);
		PerformanceCollectorService performanceCollector = _performanceCollector;
		DalamudUtilService sender = this;
		MareInterpolatedStringHandler counterName = new MareInterpolatedStringHandler(24, 4);
		counterName.AppendLiteral("RunOnFramework:Func<");
		counterName.AppendFormatted(typeof(T));
		counterName.AppendLiteral(">/");
		counterName.AppendFormatted(fileName);
		counterName.AppendLiteral(">");
		counterName.AppendFormatted(callerMember);
		counterName.AppendLiteral(":");
		counterName.AppendFormatted(callerLineNumber);
		return await performanceCollector.LogPerformance(sender, counterName, async delegate
		{
			if (!_framework.IsInFrameworkUpdateThread)
			{
				T result = await _framework.RunOnFrameworkThread(func).ContinueWith((Task<T> task) => task.Result).ConfigureAwait(continueOnCapturedContext: false);
				while (_framework.IsInFrameworkUpdateThread)
				{
					_logger.LogTrace("Still on framework");
					await Task.Delay(1).ConfigureAwait(continueOnCapturedContext: false);
				}
				return result;
			}
			return func();
		}).ConfigureAwait(continueOnCapturedContext: false);
	}

	public Task StartAsync(CancellationToken cancellationToken)
	{
		_logger.LogInformation("Starting DalamudUtilService");
		_framework.Update += FrameworkOnUpdate;
		if (IsLoggedIn)
		{
			_classJobId = _clientState.LocalPlayer.ClassJob.RowId;
		}
		_logger.LogInformation("Started DalamudUtilService");
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		_logger.LogTrace("Stopping {type}", GetType());
		Mediator.UnsubscribeAll(this);
		_framework.Update -= FrameworkOnUpdate;
		return Task.CompletedTask;
	}

	public async Task WaitWhileCharacterIsDrawing(ILogger logger, GameObjectHandler handler, Guid redrawId, int timeOut = 5000, CancellationToken? ct = null)
	{
		if (!_clientState.IsLoggedIn)
		{
			return;
		}
		if (!ct.HasValue)
		{
			ct = CancellationToken.None;
		}
		int curWaitTime = 0;
		try
		{
			logger.LogTrace("[{redrawId}] Starting wait for {handler} to draw", redrawId, handler);
			await Task.Delay(250, ct.Value).ConfigureAwait(continueOnCapturedContext: true);
			curWaitTime += 250;
			while (true)
			{
				bool flag = !ct.Value.IsCancellationRequested && curWaitTime < timeOut;
				if (flag)
				{
					flag = await handler.IsBeingDrawnRunOnFrameworkAsync().ConfigureAwait(continueOnCapturedContext: false);
				}
				if (!flag)
				{
					break;
				}
				logger.LogTrace("[{redrawId}] Waiting for {handler} to finish drawing", redrawId, handler);
				curWaitTime += 250;
				await Task.Delay(250).ConfigureAwait(continueOnCapturedContext: true);
			}
			logger.LogTrace("[{redrawId}] Finished drawing after {curWaitTime}ms", redrawId, curWaitTime);
		}
		catch (NullReferenceException exception)
		{
			logger.LogWarning(exception, "Error accessing {handler}, object does not exist anymore?", handler);
		}
		catch (AccessViolationException exception2)
		{
			logger.LogWarning(exception2, "Error accessing {handler}, object does not exist anymore?", handler);
		}
	}

	public unsafe void WaitWhileGposeCharacterIsDrawing(nint characterAddress, int timeOut = 5000)
	{
		Thread.Sleep(500);
		int curWaitTime = 0;
		_logger.LogTrace("RenderFlags: {flags}", ((GameObject*)characterAddress)->RenderFlags.ToString("X"));
		while (((GameObject*)characterAddress)->RenderFlags != 0 && curWaitTime < timeOut)
		{
			_logger.LogTrace("Waiting for gpose actor to finish drawing");
			curWaitTime += 250;
			Thread.Sleep(250);
		}
		Thread.Sleep(500);
	}

	public Vector2 WorldToScreen(IGameObject? obj)
	{
		if (obj == null)
		{
			return Vector2.Zero;
		}
		if (!_gameGui.WorldToScreen(obj.Position, out var screenPos))
		{
			return Vector2.Zero;
		}
		return screenPos;
	}

	internal (string Name, nint Address) FindPlayerByNameHash(string ident)
	{
		_playerCharas.TryGetValue(ident, out (string, nint) result);
		return result;
	}

	private unsafe void CheckCharacterForDrawing(nint address, string characterName)
	{
		DrawObject* drawObj = ((GameObject*)address)->DrawObject;
		bool isDrawing = false;
		bool isDrawingChanged = false;
		if (drawObj != (DrawObject*)IntPtr.Zero)
		{
			isDrawing = ((GameObject*)address)->RenderFlags == 2048;
			if (!isDrawing)
			{
				isDrawing = ((CharacterBase*)drawObj)->HasModelInSlotLoaded != 0;
				if (!isDrawing)
				{
					isDrawing = ((CharacterBase*)drawObj)->HasModelFilesInSlotLoaded != 0;
					if (isDrawing && !string.Equals(_lastGlobalBlockPlayer, characterName, StringComparison.Ordinal) && !string.Equals(_lastGlobalBlockReason, "HasModelFilesInSlotLoaded", StringComparison.Ordinal))
					{
						_lastGlobalBlockPlayer = characterName;
						_lastGlobalBlockReason = "HasModelFilesInSlotLoaded";
						isDrawingChanged = true;
					}
				}
				else if (!string.Equals(_lastGlobalBlockPlayer, characterName, StringComparison.Ordinal) && !string.Equals(_lastGlobalBlockReason, "HasModelInSlotLoaded", StringComparison.Ordinal))
				{
					_lastGlobalBlockPlayer = characterName;
					_lastGlobalBlockReason = "HasModelInSlotLoaded";
					isDrawingChanged = true;
				}
			}
			else if (!string.Equals(_lastGlobalBlockPlayer, characterName, StringComparison.Ordinal) && !string.Equals(_lastGlobalBlockReason, "RenderFlags", StringComparison.Ordinal))
			{
				_lastGlobalBlockPlayer = characterName;
				_lastGlobalBlockReason = "RenderFlags";
				isDrawingChanged = true;
			}
		}
		if (isDrawingChanged)
		{
			_logger.LogTrace("Global draw block: START => {name} ({reason})", characterName, _lastGlobalBlockReason);
		}
		IsAnythingDrawing |= isDrawing;
	}

	private void FrameworkOnUpdate(IFramework framework)
	{
		PerformanceCollectorService performanceCollector = _performanceCollector;
		MareInterpolatedStringHandler counterName = new MareInterpolatedStringHandler(17, 0);
		counterName.AppendLiteral("FrameworkOnUpdate");
		performanceCollector.LogPerformance(this, counterName, FrameworkOnUpdateInternal);
	}

	private unsafe void FrameworkOnUpdateInternal()
	{
		IPlayerCharacter? localPlayer = _clientState.LocalPlayer;
		if (localPlayer != null && localPlayer.IsDead && _condition[ConditionFlag.BoundByDuty])
		{
			return;
		}
		bool isNormalFrameworkUpdate = DateTime.UtcNow < _delayedFrameworkUpdateCheck.AddSeconds(1.0);
		PerformanceCollectorService performanceCollector = _performanceCollector;
		MareInterpolatedStringHandler counterName = new MareInterpolatedStringHandler(26, 1);
		counterName.AppendLiteral("FrameworkOnUpdateInternal+");
		counterName.AppendFormatted(isNormalFrameworkUpdate ? "Regular" : "Delayed");
		performanceCollector.LogPerformance(this, counterName, delegate
		{
			IsAnythingDrawing = false;
			PerformanceCollectorService performanceCollector2 = _performanceCollector;
			DalamudUtilService sender = this;
			MareInterpolatedStringHandler counterName2 = new MareInterpolatedStringHandler(16, 0);
			counterName2.AppendLiteral("ObjTableToCharas");
			performanceCollector2.LogPerformance(sender, counterName2, delegate
			{
				_notUpdatedCharas.AddRange(_playerCharas.Keys);
				for (int i = 0; i < 200; i += 2)
				{
					IGameObject gameObject = _objectTable[i];
					if (gameObject != null && gameObject.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player)
					{
						if (_blockedCharacterHandler.IsCharacterBlocked(gameObject.Address, out var firstTime) && firstTime)
						{
							_logger.LogTrace("Skipping character {addr}, blocked/muted", ((IntPtr)gameObject.Address).ToString("X"));
						}
						else
						{
							string nameString = ((GameObject*)gameObject.Address)->NameString;
							string hashedCIDFromPlayerPointer = GetHashedCIDFromPlayerPointer(gameObject.Address);
							if (!IsAnythingDrawing)
							{
								CheckCharacterForDrawing(gameObject.Address, nameString);
							}
							_notUpdatedCharas.Remove(hashedCIDFromPlayerPointer);
							_playerCharas[hashedCIDFromPlayerPointer] = (nameString, gameObject.Address);
						}
					}
				}
				foreach (string current in _notUpdatedCharas)
				{
					_playerCharas.Remove(current);
				}
				_notUpdatedCharas.Clear();
			});
			if (!IsAnythingDrawing && !string.IsNullOrEmpty(_lastGlobalBlockPlayer))
			{
				_logger.LogTrace("Global draw block: END => {name}", _lastGlobalBlockPlayer);
				_lastGlobalBlockPlayer = string.Empty;
				_lastGlobalBlockReason = string.Empty;
			}
			if (_clientState.IsGPosing && !IsInGpose)
			{
				_logger.LogDebug("Gpose start");
				IsInGpose = true;
				Mediator.Publish(new GposeStartMessage());
			}
			else if (!_clientState.IsGPosing && IsInGpose)
			{
				_logger.LogDebug("Gpose end");
				IsInGpose = false;
				Mediator.Publish(new GposeEndMessage());
			}
			if ((_condition[ConditionFlag.Performing] || _condition[ConditionFlag.InCombat]) && !IsInCombatOrPerforming)
			{
				_logger.LogDebug("Combat/Performance start");
				IsInCombatOrPerforming = true;
				Mediator.Publish(new CombatOrPerformanceStartMessage());
				Mediator.Publish(new HaltScanMessage("IsInCombatOrPerforming"));
			}
			else if (!_condition[ConditionFlag.Performing] && !_condition[ConditionFlag.InCombat] && IsInCombatOrPerforming)
			{
				_logger.LogDebug("Combat/Performance end");
				IsInCombatOrPerforming = false;
				Mediator.Publish(new CombatOrPerformanceEndMessage());
				Mediator.Publish(new ResumeScanMessage("IsInCombatOrPerforming"));
			}
			if (_condition[ConditionFlag.BoundByDuty] && !IsInInstance)
			{
				_logger.LogDebug("Instance start");
				IsInInstance = true;
				Mediator.Publish(new InstanceStartMessage());
				if (_configService.Current.PauseSyncingDuringInstances)
				{
					Mediator.Publish(new HaltScanMessage("IsInInstance"));
				}
			}
			else if (!_condition[ConditionFlag.BoundByDuty] && IsInInstance)
			{
				_logger.LogDebug("Instance end");
				IsInInstance = false;
				Mediator.Publish(new InstanceEndMessage());
				if (_configService.Current.PauseSyncingDuringInstances)
				{
					Mediator.Publish(new ResumeScanMessage("IsInInstance"));
				}
			}
			if (_condition[ConditionFlag.WatchingCutscene] && !IsInCutscene)
			{
				_logger.LogDebug("Cutscene start");
				IsInCutscene = true;
				Mediator.Publish(new CutsceneStartMessage());
				Mediator.Publish(new HaltScanMessage("IsInCutscene"));
			}
			else if (!_condition[ConditionFlag.WatchingCutscene] && IsInCutscene)
			{
				_logger.LogDebug("Cutscene end");
				IsInCutscene = false;
				Mediator.Publish(new CutsceneEndMessage());
				Mediator.Publish(new ResumeScanMessage("IsInCutscene"));
			}
			if (IsInCutscene)
			{
				Mediator.Publish(new CutsceneFrameworkUpdateMessage());
			}
			else if (_condition[ConditionFlag.BetweenAreas] || _condition[ConditionFlag.BetweenAreas51])
			{
				ushort territoryType = _clientState.TerritoryType;
				if (_lastZone != territoryType)
				{
					_lastZone = territoryType;
					if (!_sentBetweenAreas)
					{
						_logger.LogDebug("Zone switch start");
						_sentBetweenAreas = true;
						Mediator.Publish(new ZoneSwitchStartMessage());
						Mediator.Publish(new HaltScanMessage("BetweenAreas"));
					}
				}
			}
			else
			{
				if (_sentBetweenAreas)
				{
					_logger.LogDebug("Zone switch end");
					_sentBetweenAreas = false;
					Mediator.Publish(new ZoneSwitchEndMessage());
					Mediator.Publish(new ResumeScanMessage("BetweenAreas"));
				}
				IPlayerCharacter localPlayer2 = _clientState.LocalPlayer;
				if (localPlayer2 != null)
				{
					_classJobId = localPlayer2.ClassJob.RowId;
				}
				if (!IsInCombatOrPerforming)
				{
					Mediator.Publish(new FrameworkUpdateMessage());
				}
				Mediator.Publish(new PriorityFrameworkUpdateMessage());
				if (!isNormalFrameworkUpdate)
				{
					if (localPlayer2 != null && !IsLoggedIn)
					{
						_logger.LogDebug("Logged in");
						IsLoggedIn = true;
						_lastZone = _clientState.TerritoryType;
						_cid = RebuildCID();
						Mediator.Publish(new DalamudLoginMessage());
					}
					else if (localPlayer2 == null && IsLoggedIn)
					{
						_logger.LogDebug("Logged out");
						IsLoggedIn = false;
						Mediator.Publish(new DalamudLogoutMessage());
					}
					if (_gameConfig != null && _gameConfig.TryGet(SystemConfigOption.LodType_DX11, out bool value))
					{
						IsLodEnabled = value;
					}
					if (IsInCombatOrPerforming)
					{
						Mediator.Publish(new FrameworkUpdateMessage());
					}
					Mediator.Publish(new DelayedFrameworkUpdateMessage());
					_delayedFrameworkUpdateCheck = DateTime.UtcNow;
				}
			}
		});
	}
}
