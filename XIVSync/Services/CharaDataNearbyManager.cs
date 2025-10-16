using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.SubKinds;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Microsoft.Extensions.Logging;
using XIVSync.API.Data;
using XIVSync.API.Dto.CharaData;
using XIVSync.Interop;
using XIVSync.MareConfiguration;
using XIVSync.Services.CharaData.Models;
using XIVSync.Services.Mediator;
using XIVSync.Services.ServerConfiguration;

namespace XIVSync.Services;

public sealed class CharaDataNearbyManager : DisposableMediatorSubscriberBase
{
	public record NearbyCharaDataEntry
	{
		public float Direction { get; init; }

		public float Distance { get; init; }
	}

	private readonly DalamudUtilService _dalamudUtilService;

	private readonly Dictionary<PoseEntryExtended, NearbyCharaDataEntry> _nearbyData = new Dictionary<PoseEntryExtended, NearbyCharaDataEntry>();

	private readonly Dictionary<PoseEntryExtended, Guid> _poseVfx = new Dictionary<PoseEntryExtended, Guid>();

	private readonly ServerConfigurationManager _serverConfigurationManager;

	private readonly CharaDataConfigService _charaDataConfigService;

	private readonly Dictionary<UserData, List<CharaDataMetaInfoExtendedDto>> _metaInfoCache = new Dictionary<UserData, List<CharaDataMetaInfoExtendedDto>>();

	private readonly VfxSpawnManager _vfxSpawnManager;

	private Task? _filterEntriesRunningTask;

	private (Guid VfxId, PoseEntryExtended Pose)? _hoveredVfx;

	private DateTime _lastExecutionTime = DateTime.UtcNow;

	private SemaphoreSlim _sharedDataUpdateSemaphore = new SemaphoreSlim(1, 1);

	public bool ComputeNearbyData { get; set; }

	public IDictionary<PoseEntryExtended, NearbyCharaDataEntry> NearbyData => _nearbyData;

	public string UserNoteFilter { get; set; } = string.Empty;


	public CharaDataNearbyManager(ILogger<CharaDataNearbyManager> logger, MareMediator mediator, DalamudUtilService dalamudUtilService, VfxSpawnManager vfxSpawnManager, ServerConfigurationManager serverConfigurationManager, CharaDataConfigService charaDataConfigService)
		: base(logger, mediator)
	{
		mediator.Subscribe<FrameworkUpdateMessage>(this, delegate
		{
			HandleFrameworkUpdate();
		});
		mediator.Subscribe<CutsceneFrameworkUpdateMessage>(this, delegate
		{
			HandleFrameworkUpdate();
		});
		_dalamudUtilService = dalamudUtilService;
		_vfxSpawnManager = vfxSpawnManager;
		_serverConfigurationManager = serverConfigurationManager;
		_charaDataConfigService = charaDataConfigService;
		mediator.Subscribe<GposeStartMessage>(this, delegate
		{
			ClearAllVfx();
		});
	}

	public void UpdateSharedData(Dictionary<string, CharaDataMetaInfoExtendedDto?> newData)
	{
		_sharedDataUpdateSemaphore.Wait();
		try
		{
			_metaInfoCache.Clear();
			foreach (KeyValuePair<string, CharaDataMetaInfoExtendedDto> kvp in newData)
			{
				if (!(kvp.Value == null))
				{
					if (!_metaInfoCache.TryGetValue(kvp.Value.Uploader, out List<CharaDataMetaInfoExtendedDto> list))
					{
						list = (_metaInfoCache[kvp.Value.Uploader] = new List<CharaDataMetaInfoExtendedDto>());
					}
					list.Add(kvp.Value);
				}
			}
		}
		finally
		{
			_sharedDataUpdateSemaphore.Release();
		}
	}

	internal void SetHoveredVfx(PoseEntryExtended? hoveredPose)
	{
		if (hoveredPose == null && !_hoveredVfx.HasValue)
		{
			return;
		}
		if (hoveredPose == null)
		{
			_vfxSpawnManager.DespawnObject(_hoveredVfx.Value.VfxId);
			_hoveredVfx = null;
		}
		else if (!_hoveredVfx.HasValue)
		{
			Guid? vfxGuid = _vfxSpawnManager.SpawnObject(hoveredPose.Position, hoveredPose.Rotation, Vector3.One * 4f, 1f, 0.2f, 0.2f, 1f);
			if (vfxGuid.HasValue)
			{
				_hoveredVfx = (vfxGuid.Value, hoveredPose);
			}
		}
		else if (hoveredPose != _hoveredVfx.Value.Pose)
		{
			_vfxSpawnManager.DespawnObject(_hoveredVfx.Value.VfxId);
			Guid? vfxGuid = _vfxSpawnManager.SpawnObject(hoveredPose.Position, hoveredPose.Rotation, Vector3.One * 4f, 1f, 0.2f, 0.2f, 1f);
			if (vfxGuid.HasValue)
			{
				_hoveredVfx = (vfxGuid.Value, hoveredPose);
			}
		}
	}

	protected override void Dispose(bool disposing)
	{
		base.Dispose(disposing);
		ClearAllVfx();
	}

	private static float CalculateYawDegrees(Vector3 directionXZ)
	{
		float yawDegrees = (float)Math.Atan2(0f - directionXZ.X, directionXZ.Z) * (180f / (float)Math.PI);
		if (yawDegrees < 0f)
		{
			yawDegrees += 360f;
		}
		return yawDegrees;
	}

	private static float GetAngleToTarget(Vector3 cameraPosition, float cameraYawDegrees, Vector3 targetPosition)
	{
		Vector3 directionToTarget = targetPosition - cameraPosition;
		Vector3 directionToTargetXZ = new Vector3(directionToTarget.X, 0f, directionToTarget.Z);
		if (directionToTargetXZ.LengthSquared() < 1E-10f)
		{
			return 0f;
		}
		directionToTargetXZ = Vector3.Normalize(directionToTargetXZ);
		float relativeAngle = CalculateYawDegrees(directionToTargetXZ) - cameraYawDegrees;
		if (relativeAngle < 0f)
		{
			relativeAngle += 360f;
		}
		return relativeAngle;
	}

	private static float GetCameraYaw(Vector3 cameraPosition, Vector3 lookAtVector)
	{
		Vector3 directionFacing = lookAtVector - cameraPosition;
		Vector3 directionFacingXZ = new Vector3(directionFacing.X, 0f, directionFacing.Z);
		directionFacingXZ = ((!(directionFacingXZ.LengthSquared() < 1E-10f)) ? Vector3.Normalize(directionFacingXZ) : new Vector3(0f, 0f, 1f));
		return CalculateYawDegrees(directionFacingXZ);
	}

	private void ClearAllVfx()
	{
		foreach (KeyValuePair<PoseEntryExtended, Guid> vfx in _poseVfx)
		{
			_vfxSpawnManager.DespawnObject(vfx.Value);
		}
		_poseVfx.Clear();
	}

	private async Task FilterEntriesAsync(Vector3 cameraPos, Vector3 cameraLookAt)
	{
		List<PoseEntryExtended> previousPoses = _nearbyData.Keys.ToList();
		_nearbyData.Clear();
		LocationInfo ownLocation = await _dalamudUtilService.RunOnFrameworkThread(() => _dalamudUtilService.GetMapData(), "FilterEntriesAsync", "C:\\Users\\Owner\\sync_client2\\XIVSync\\Services\\CharaData\\CharaDataNearbyManager.cs", 189).ConfigureAwait(continueOnCapturedContext: false);
		IPlayerCharacter obj = await _dalamudUtilService.RunOnFrameworkThread(() => _dalamudUtilService.GetPlayerCharacter(), "FilterEntriesAsync", "C:\\Users\\Owner\\sync_client2\\XIVSync\\Services\\CharaData\\CharaDataNearbyManager.cs", 190).ConfigureAwait(continueOnCapturedContext: false);
		RowRef<Lumina.Excel.Sheets.World> currentServer = obj.CurrentWorld;
		Vector3 playerPos = obj.Position;
		float cameraYaw = GetCameraYaw(cameraPos, cameraLookAt);
		bool ignoreHousingLimits = _charaDataConfigService.Current.NearbyIgnoreHousingLimitations;
		bool onlyCurrentServer = _charaDataConfigService.Current.NearbyOwnServerOnly;
		bool showOwnData = _charaDataConfigService.Current.NearbyShowOwnData;
		foreach (KeyValuePair<UserData, List<CharaDataMetaInfoExtendedDto>> item in _metaInfoCache.Where<KeyValuePair<UserData, List<CharaDataMetaInfoExtendedDto>>>((KeyValuePair<UserData, List<CharaDataMetaInfoExtendedDto>> d) => string.IsNullOrWhiteSpace(UserNoteFilter) || (d.Key.Alias ?? string.Empty).Contains(UserNoteFilter, StringComparison.OrdinalIgnoreCase) || d.Key.UID.Contains(UserNoteFilter, StringComparison.OrdinalIgnoreCase) || (_serverConfigurationManager.GetNoteForUid(UserNoteFilter) ?? string.Empty).Contains(UserNoteFilter, StringComparison.OrdinalIgnoreCase)).ToDictionary((KeyValuePair<UserData, List<CharaDataMetaInfoExtendedDto>> k) => k.Key, (KeyValuePair<UserData, List<CharaDataMetaInfoExtendedDto>> k) => k.Value))
		{
			foreach (PoseEntryExtended pose in (from p in item.Value.Where((CharaDataMetaInfoExtendedDto v) => v.HasPoses && v.HasWorldData && (showOwnData || !v.IsOwnData)).SelectMany((CharaDataMetaInfoExtendedDto k) => k.PoseExtended)
				where p.HasPoseData && p.HasWorldData && p.WorldData.Value.LocationInfo.TerritoryId == ownLocation.TerritoryId
				select p).ToList())
			{
				LocationInfo poseLocation = pose.WorldData.Value.LocationInfo;
				bool isInHousing = poseLocation.WardId != 0;
				float distance = Vector3.Distance(playerPos, pose.Position);
				if (!(distance > (float)_charaDataConfigService.Current.NearbyDistanceFilter) && ((!isInHousing && poseLocation.MapId == ownLocation.MapId && (!onlyCurrentServer || poseLocation.ServerId == currentServer.RowId)) || (isInHousing && ((ignoreHousingLimits && !onlyCurrentServer) || (ignoreHousingLimits && onlyCurrentServer && poseLocation.ServerId == currentServer.RowId) || poseLocation.ServerId == currentServer.RowId) && ((poseLocation.HouseId == 0 && poseLocation.DivisionId == ownLocation.DivisionId && (ignoreHousingLimits || poseLocation.WardId == ownLocation.WardId)) || (poseLocation.HouseId != 0 && (ignoreHousingLimits || (poseLocation.HouseId == ownLocation.HouseId && poseLocation.WardId == ownLocation.WardId && poseLocation.DivisionId == ownLocation.DivisionId && poseLocation.RoomId == ownLocation.RoomId)))))))
				{
					_nearbyData[pose] = new NearbyCharaDataEntry
					{
						Direction = GetAngleToTarget(cameraPos, cameraYaw, pose.Position),
						Distance = distance
					};
				}
			}
		}
		if (_charaDataConfigService.Current.NearbyDrawWisps && !_dalamudUtilService.IsInGpose && !_dalamudUtilService.IsInCombatOrPerforming)
		{
			await _dalamudUtilService.RunOnFrameworkThread(delegate
			{
				ManageWispsNearby(previousPoses);
			}, "FilterEntriesAsync", "C:\\Users\\Owner\\sync_client2\\XIVSync\\Services\\CharaData\\CharaDataNearbyManager.cs", 240).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	private unsafe void HandleFrameworkUpdate()
	{
		if (_lastExecutionTime.AddSeconds(0.5) > DateTime.UtcNow)
		{
			return;
		}
		_lastExecutionTime = DateTime.UtcNow;
		if (!ComputeNearbyData && !_charaDataConfigService.Current.NearbyShowAlways)
		{
			if (_nearbyData.Any())
			{
				_nearbyData.Clear();
			}
			if (_poseVfx.Any())
			{
				ClearAllVfx();
			}
			return;
		}
		if (!_charaDataConfigService.Current.NearbyDrawWisps || _dalamudUtilService.IsInGpose || _dalamudUtilService.IsInCombatOrPerforming)
		{
			ClearAllVfx();
		}
		Camera* camera = CameraManager.Instance()->CurrentCamera;
		Vector3 cameraPos = new Vector3(camera->Position.X, camera->Position.Y, camera->Position.Z);
		Vector3 lookAt = new Vector3(camera->LookAtVector.X, camera->LookAtVector.Y, camera->LookAtVector.Z);
		if (_filterEntriesRunningTask?.IsCompleted ?? _dalamudUtilService.IsLoggedIn)
		{
			_filterEntriesRunningTask = FilterEntriesAsync(cameraPos, lookAt);
		}
	}

	private void ManageWispsNearby(List<PoseEntryExtended> previousPoses)
	{
		foreach (PoseEntryExtended data in _nearbyData.Keys)
		{
			if (!_poseVfx.TryGetValue(data, out var _))
			{
				Guid? vfxGuid = ((!data.MetaInfo.IsOwnData) ? _vfxSpawnManager.SpawnObject(data.Position, data.Rotation, Vector3.One * 2f) : _vfxSpawnManager.SpawnObject(data.Position, data.Rotation, Vector3.One * 2f, 0.8f, 0.5f, 0f, 0.7f));
				if (vfxGuid.HasValue)
				{
					_poseVfx[data] = vfxGuid.Value;
				}
			}
		}
		foreach (PoseEntryExtended data in previousPoses.Except(_nearbyData.Keys))
		{
			if (_poseVfx.Remove(data, out var guid))
			{
				_vfxSpawnManager.DespawnObject(guid);
			}
		}
	}
}
