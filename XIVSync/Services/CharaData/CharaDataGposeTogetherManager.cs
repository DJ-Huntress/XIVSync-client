using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Microsoft.Extensions.Logging;
using XIVSync.API.Data;
using XIVSync.API.Data.Enum;
using XIVSync.API.Dto.CharaData;
using XIVSync.Interop;
using XIVSync.Interop.Ipc;
using XIVSync.Services.CharaData.Models;
using XIVSync.Services.Mediator;
using XIVSync.WebAPI;

namespace XIVSync.Services.CharaData;

public class CharaDataGposeTogetherManager : DisposableMediatorSubscriberBase
{
	private readonly ApiController _apiController;

	private readonly IpcCallerBrio _brio;

	private readonly SemaphoreSlim _charaDataCreationSemaphore = new SemaphoreSlim(1, 1);

	private readonly CharaDataFileHandler _charaDataFileHandler;

	private readonly CharaDataManager _charaDataManager;

	private readonly DalamudUtilService _dalamudUtil;

	private readonly Dictionary<string, GposeLobbyUserData> _usersInLobby = new Dictionary<string, GposeLobbyUserData>();

	private readonly VfxSpawnManager _vfxSpawnManager;

	private (CharacterData ApiData, CharaDataDownloadDto Dto)? _lastCreatedCharaData;

	private PoseData? _lastDeltaPoseData;

	private PoseData? _lastFullPoseData;

	private WorldData? _lastWorldData;

	private CancellationTokenSource _lobbyCts = new CancellationTokenSource();

	private int _poseGenerationExecutions;

	private bool _forceResendFullPose;

	private bool _forceResendWorldData;

	private readonly SemaphoreSlim _charaDataSpawnSemaphore = new SemaphoreSlim(1, 1);

	public string? CurrentGPoseLobbyId { get; private set; }

	public string? LastGPoseLobbyId { get; private set; }

	public IEnumerable<GposeLobbyUserData> UsersInLobby => _usersInLobby.Values;

	public CharaDataGposeTogetherManager(ILogger<CharaDataGposeTogetherManager> logger, MareMediator mediator, ApiController apiController, IpcCallerBrio brio, DalamudUtilService dalamudUtil, VfxSpawnManager vfxSpawnManager, CharaDataFileHandler charaDataFileHandler, CharaDataManager charaDataManager)
		: base(logger, mediator)
	{
		base.Mediator.Subscribe(this, delegate(GposeLobbyUserJoin msg)
		{
			OnUserJoinLobby(msg.UserData);
		});
		base.Mediator.Subscribe(this, delegate(GPoseLobbyUserLeave msg)
		{
			OnUserLeaveLobby(msg.UserData);
		});
		base.Mediator.Subscribe(this, delegate(GPoseLobbyReceiveCharaData msg)
		{
			OnReceiveCharaData(msg.CharaDataDownloadDto);
		});
		base.Mediator.Subscribe(this, delegate(GPoseLobbyReceivePoseData msg)
		{
			OnReceivePoseData(msg.UserData, msg.PoseData);
		});
		base.Mediator.Subscribe(this, delegate(GPoseLobbyReceiveWorldData msg)
		{
			OnReceiveWorldData(msg.UserData, msg.WorldData);
		});
		base.Mediator.Subscribe<ConnectedMessage>(this, delegate
		{
			if (_usersInLobby.Count > 0 && !string.IsNullOrEmpty(CurrentGPoseLobbyId))
			{
				JoinGPoseLobby(CurrentGPoseLobbyId, isReconnecting: true);
			}
			else
			{
				LeaveGPoseLobby();
			}
		});
		base.Mediator.Subscribe<GposeStartMessage>(this, delegate
		{
			OnEnterGpose();
		});
		base.Mediator.Subscribe<GposeEndMessage>(this, delegate
		{
			OnExitGpose();
		});
		base.Mediator.Subscribe<FrameworkUpdateMessage>(this, delegate
		{
			OnFrameworkUpdate();
		});
		base.Mediator.Subscribe<CutsceneFrameworkUpdateMessage>(this, delegate
		{
			OnCutsceneFrameworkUpdate();
		});
		base.Mediator.Subscribe<DisconnectedMessage>(this, delegate
		{
			LeaveGPoseLobby();
		});
		_apiController = apiController;
		_brio = brio;
		_dalamudUtil = dalamudUtil;
		_vfxSpawnManager = vfxSpawnManager;
		_charaDataFileHandler = charaDataFileHandler;
		_charaDataManager = charaDataManager;
	}

	public (bool SameMap, bool SameServer, bool SameEverything) IsOnSameMapAndServer(GposeLobbyUserData data)
	{
		return (SameMap: data.Map.RowId == _lastWorldData?.LocationInfo.MapId, SameServer: data.WorldData?.LocationInfo.ServerId == _lastWorldData?.LocationInfo.ServerId, SameEverything: data.WorldData?.LocationInfo == _lastWorldData?.LocationInfo);
	}

	public async Task PushCharacterDownloadDto()
	{
		CharacterData playerData = await _charaDataFileHandler.CreatePlayerData().ConfigureAwait(continueOnCapturedContext: false);
		if (playerData == null)
		{
			return;
		}
		if (!string.Equals(playerData.DataHash.Value, _lastCreatedCharaData?.ApiData.DataHash.Value, StringComparison.Ordinal))
		{
			List<GamePathEntry> filegamePaths = playerData.FileReplacements[ObjectKind.Player].Where((FileReplacementData u) => string.IsNullOrEmpty(u.FileSwapPath)).SelectMany((FileReplacementData u) => u.GamePaths, (FileReplacementData file, string path) => new GamePathEntry(file.Hash, path)).ToList();
			List<GamePathEntry> fileSwapPaths = playerData.FileReplacements[ObjectKind.Player].Where((FileReplacementData u) => !string.IsNullOrEmpty(u.FileSwapPath)).SelectMany((FileReplacementData u) => u.GamePaths, (FileReplacementData file, string path) => new GamePathEntry(file.FileSwapPath, path)).ToList();
			await _charaDataManager.UploadFiles(playerData.FileReplacements[ObjectKind.Player].Where((FileReplacementData u) => string.IsNullOrEmpty(u.FileSwapPath)).SelectMany((FileReplacementData u) => u.GamePaths, (FileReplacementData file, string path) => new GamePathEntry(file.Hash, path)).ToList()).ConfigureAwait(continueOnCapturedContext: false);
			CharaDataDownloadDto charaDataDownloadDto = new CharaDataDownloadDto("GPOSELOBBY:" + CurrentGPoseLobbyId, new UserData(_apiController.UID))
			{
				UpdatedDate = DateTime.UtcNow,
				ManipulationData = playerData.ManipulationData,
				CustomizeData = playerData.CustomizePlusData[ObjectKind.Player],
				FileGamePaths = filegamePaths,
				FileSwaps = fileSwapPaths,
				GlamourerData = playerData.GlamourerData[ObjectKind.Player]
			};
			_lastCreatedCharaData = (playerData, charaDataDownloadDto);
		}
		ForceResendOwnData();
		if (_lastCreatedCharaData.HasValue)
		{
			await _apiController.GposeLobbyPushCharacterData(_lastCreatedCharaData.Value.Dto).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	internal void CreateNewLobby()
	{
		Task.Run(async delegate
		{
			ClearLobby();
			CurrentGPoseLobbyId = await _apiController.GposeLobbyCreate().ConfigureAwait(continueOnCapturedContext: false);
			if (!string.IsNullOrEmpty(CurrentGPoseLobbyId))
			{
				GposeWorldPositionBackgroundTask(_lobbyCts.Token);
				GposePoseDataBackgroundTask(_lobbyCts.Token);
			}
		});
	}

	internal void JoinGPoseLobby(string joinLobbyId, bool isReconnecting = false)
	{
		Task.Run(async delegate
		{
			List<UserData> otherUsers = await _apiController.GposeLobbyJoin(joinLobbyId).ConfigureAwait(continueOnCapturedContext: false);
			ClearLobby();
			if (otherUsers.Any())
			{
				LastGPoseLobbyId = string.Empty;
				foreach (UserData user in otherUsers)
				{
					OnUserJoinLobby(user);
				}
				CurrentGPoseLobbyId = joinLobbyId;
				GposeWorldPositionBackgroundTask(_lobbyCts.Token);
				GposePoseDataBackgroundTask(_lobbyCts.Token);
			}
			else
			{
				LeaveGPoseLobby();
				LastGPoseLobbyId = string.Empty;
			}
		});
	}

	internal void LeaveGPoseLobby()
	{
		Task.Run(async delegate
		{
			if (await _apiController.GposeLobbyLeave().ConfigureAwait(continueOnCapturedContext: false))
			{
				if (_usersInLobby.Count != 0)
				{
					LastGPoseLobbyId = CurrentGPoseLobbyId;
				}
				ClearLobby(revertCharas: true);
			}
		});
	}

	protected override void Dispose(bool disposing)
	{
		base.Dispose(disposing);
		if (disposing)
		{
			ClearLobby(revertCharas: true);
		}
	}

	private void ClearLobby(bool revertCharas = false)
	{
		_lobbyCts.Cancel();
		_lobbyCts.Dispose();
		_lobbyCts = new CancellationTokenSource();
		CurrentGPoseLobbyId = string.Empty;
		foreach (KeyValuePair<string, GposeLobbyUserData> user in _usersInLobby.ToDictionary())
		{
			if (revertCharas)
			{
				_charaDataManager.RevertChara(user.Value.HandledChara);
			}
			OnUserLeaveLobby(user.Value.UserData);
		}
		_usersInLobby.Clear();
	}

	private string CreateJsonFromPoseData(PoseData? poseData)
	{
		if (!poseData.HasValue)
		{
			return "{}";
		}
		JsonObject node = new JsonObject();
		node["Bones"] = new JsonObject();
		foreach (KeyValuePair<string, BoneData> bone in poseData.Value.Bones)
		{
			node["Bones"][bone.Key] = new JsonObject();
			node["Bones"][bone.Key]["Position"] = $"{bone.Value.PositionX.ToString(CultureInfo.InvariantCulture)}, {bone.Value.PositionY.ToString(CultureInfo.InvariantCulture)}, {bone.Value.PositionZ.ToString(CultureInfo.InvariantCulture)}";
			node["Bones"][bone.Key]["Scale"] = $"{bone.Value.ScaleX.ToString(CultureInfo.InvariantCulture)}, {bone.Value.ScaleY.ToString(CultureInfo.InvariantCulture)}, {bone.Value.ScaleZ.ToString(CultureInfo.InvariantCulture)}";
			node["Bones"][bone.Key]["Rotation"] = $"{bone.Value.RotationX.ToString(CultureInfo.InvariantCulture)}, {bone.Value.RotationY.ToString(CultureInfo.InvariantCulture)}, {bone.Value.RotationZ.ToString(CultureInfo.InvariantCulture)}, {bone.Value.RotationW.ToString(CultureInfo.InvariantCulture)}";
		}
		node["MainHand"] = new JsonObject();
		foreach (KeyValuePair<string, BoneData> bone2 in poseData.Value.MainHand)
		{
			node["MainHand"][bone2.Key] = new JsonObject();
			node["MainHand"][bone2.Key]["Position"] = $"{bone2.Value.PositionX.ToString(CultureInfo.InvariantCulture)}, {bone2.Value.PositionY.ToString(CultureInfo.InvariantCulture)}, {bone2.Value.PositionZ.ToString(CultureInfo.InvariantCulture)}";
			node["MainHand"][bone2.Key]["Scale"] = $"{bone2.Value.ScaleX.ToString(CultureInfo.InvariantCulture)}, {bone2.Value.ScaleY.ToString(CultureInfo.InvariantCulture)}, {bone2.Value.ScaleZ.ToString(CultureInfo.InvariantCulture)}";
			node["MainHand"][bone2.Key]["Rotation"] = $"{bone2.Value.RotationX.ToString(CultureInfo.InvariantCulture)}, {bone2.Value.RotationY.ToString(CultureInfo.InvariantCulture)}, {bone2.Value.RotationZ.ToString(CultureInfo.InvariantCulture)}, {bone2.Value.RotationW.ToString(CultureInfo.InvariantCulture)}";
		}
		node["OffHand"] = new JsonObject();
		foreach (KeyValuePair<string, BoneData> bone3 in poseData.Value.OffHand)
		{
			node["OffHand"][bone3.Key] = new JsonObject();
			node["OffHand"][bone3.Key]["Position"] = $"{bone3.Value.PositionX.ToString(CultureInfo.InvariantCulture)}, {bone3.Value.PositionY.ToString(CultureInfo.InvariantCulture)}, {bone3.Value.PositionZ.ToString(CultureInfo.InvariantCulture)}";
			node["OffHand"][bone3.Key]["Scale"] = $"{bone3.Value.ScaleX.ToString(CultureInfo.InvariantCulture)}, {bone3.Value.ScaleY.ToString(CultureInfo.InvariantCulture)}, {bone3.Value.ScaleZ.ToString(CultureInfo.InvariantCulture)}";
			node["OffHand"][bone3.Key]["Rotation"] = $"{bone3.Value.RotationX.ToString(CultureInfo.InvariantCulture)}, {bone3.Value.RotationY.ToString(CultureInfo.InvariantCulture)}, {bone3.Value.RotationZ.ToString(CultureInfo.InvariantCulture)}, {bone3.Value.RotationW.ToString(CultureInfo.InvariantCulture)}";
		}
		return node.ToJsonString();
	}

	private PoseData CreatePoseDataFromJson(string json, PoseData? fullPoseData = null)
	{
		PoseData output = new PoseData
		{
			Bones = new Dictionary<string, BoneData>(StringComparer.Ordinal),
			MainHand = new Dictionary<string, BoneData>(StringComparer.Ordinal),
			OffHand = new Dictionary<string, BoneData>(StringComparer.Ordinal)
		};
		JsonNode node = JsonNode.Parse(json);
		foreach (KeyValuePair<string, JsonNode> bone in node["Bones"].AsObject())
		{
			string name = bone.Key;
			BoneData outputBoneData = createBoneData(bone.Value.AsObject());
			if (fullPoseData.HasValue)
			{
				if (fullPoseData.Value.Bones.TryGetValue(name, out var prevBoneData) && prevBoneData != outputBoneData)
				{
					output.Bones[name] = outputBoneData;
				}
			}
			else
			{
				output.Bones[name] = outputBoneData;
			}
		}
		foreach (KeyValuePair<string, JsonNode> bone2 in node["MainHand"].AsObject())
		{
			string name2 = bone2.Key;
			BoneData outputBoneData2 = createBoneData(bone2.Value.AsObject());
			if (fullPoseData.HasValue)
			{
				if (fullPoseData.Value.MainHand.TryGetValue(name2, out var prevBoneData2) && prevBoneData2 != outputBoneData2)
				{
					output.MainHand[name2] = outputBoneData2;
				}
			}
			else
			{
				output.MainHand[name2] = outputBoneData2;
			}
		}
		foreach (KeyValuePair<string, JsonNode> bone3 in node["OffHand"].AsObject())
		{
			string name3 = bone3.Key;
			BoneData outputBoneData3 = createBoneData(bone3.Value.AsObject());
			if (fullPoseData.HasValue)
			{
				if (fullPoseData.Value.OffHand.TryGetValue(name3, out var prevBoneData3) && prevBoneData3 != outputBoneData3)
				{
					output.OffHand[name3] = outputBoneData3;
				}
			}
			else
			{
				output.OffHand[name3] = outputBoneData3;
			}
		}
		if (fullPoseData.HasValue)
		{
			output.IsDelta = true;
		}
		return output;
		static BoneData createBoneData(JsonNode boneJson)
		{
			BoneData outputBoneData4 = new BoneData
			{
				Exists = true
			};
			string[] pos = boneJson["Position"].ToString().Split(",", StringSplitOptions.TrimEntries);
			outputBoneData4.PositionX = getRounded(pos[0]);
			outputBoneData4.PositionY = getRounded(pos[1]);
			outputBoneData4.PositionZ = getRounded(pos[2]);
			string[] sca = boneJson["Scale"].ToString().Split(",", StringSplitOptions.TrimEntries);
			outputBoneData4.ScaleX = getRounded(sca[0]);
			outputBoneData4.ScaleY = getRounded(sca[1]);
			outputBoneData4.ScaleZ = getRounded(sca[2]);
			string[] rot = boneJson["Rotation"].ToString().Split(",", StringSplitOptions.TrimEntries);
			outputBoneData4.RotationX = getRounded(rot[0]);
			outputBoneData4.RotationY = getRounded(rot[1]);
			outputBoneData4.RotationZ = getRounded(rot[2]);
			outputBoneData4.RotationW = getRounded(rot[3]);
			return outputBoneData4;
		}
		static float getRounded(string number)
		{
			return float.Round(float.Parse(number, NumberStyles.Float, CultureInfo.InvariantCulture), 5);
		}
	}

	private async Task GposePoseDataBackgroundTask(CancellationToken ct)
	{
		_lastFullPoseData = null;
		_lastDeltaPoseData = null;
		_poseGenerationExecutions = 0;
		while (!ct.IsCancellationRequested)
		{
			await Task.Delay(TimeSpan.FromSeconds(10L), ct).ConfigureAwait(continueOnCapturedContext: false);
			if (!_dalamudUtil.IsInGpose || _usersInLobby.Count == 0)
			{
				continue;
			}
			try
			{
				IPlayerCharacter chara = await _dalamudUtil.GetPlayerCharacterAsync().ConfigureAwait(continueOnCapturedContext: false);
				if (_dalamudUtil.IsInGpose)
				{
					chara = (IPlayerCharacter)(await _dalamudUtil.GetGposeCharacterFromObjectTableByNameAsync(chara.Name.TextValue, _dalamudUtil.IsInGpose).ConfigureAwait(continueOnCapturedContext: false));
				}
				if (chara == null || chara.Address == IntPtr.Zero)
				{
					continue;
				}
				string poseJson = await _brio.GetPoseAsync(chara.Address).ConfigureAwait(continueOnCapturedContext: false);
				if (string.IsNullOrEmpty(poseJson))
				{
					continue;
				}
				PoseData? lastFullData = ((_poseGenerationExecutions++ >= 12) ? ((PoseData?)null) : _lastFullPoseData);
				lastFullData = (_forceResendFullPose ? _lastFullPoseData : lastFullData);
				PoseData poseData = CreatePoseDataFromJson(poseJson, lastFullData);
				if (!poseData.IsDelta)
				{
					_lastFullPoseData = poseData;
					_lastDeltaPoseData = null;
					_poseGenerationExecutions = 0;
				}
				bool deltaIsSame = _lastDeltaPoseData.HasValue && poseData.Bones.Keys.All((string k) => _lastDeltaPoseData.Value.Bones.ContainsKey(k) && poseData.Bones.Values.All((BoneData value) => _lastDeltaPoseData.Value.Bones.ContainsValue(value)));
				if (_forceResendFullPose || ((poseData.Bones.Any() || poseData.MainHand.Any() || poseData.OffHand.Any()) && (!poseData.IsDelta || (poseData.IsDelta && !deltaIsSame))))
				{
					_forceResendFullPose = false;
					await _apiController.GposeLobbyPushPoseData(poseData).ConfigureAwait(continueOnCapturedContext: false);
				}
				if (poseData.IsDelta)
				{
					_lastDeltaPoseData = poseData;
				}
			}
			catch (Exception exception)
			{
				base.Logger.LogWarning(exception, "Error during Pose Data Generation");
			}
		}
	}

	private async Task GposeWorldPositionBackgroundTask(CancellationToken ct)
	{
		while (!ct.IsCancellationRequested)
		{
			await Task.Delay(TimeSpan.FromSeconds((!_dalamudUtil.IsInGpose) ? 1 : 10), ct).ConfigureAwait(continueOnCapturedContext: false);
			if (_usersInLobby.Count == 0)
			{
				continue;
			}
			try
			{
				ICharacter player = await _dalamudUtil.GetPlayerCharacterAsync().ConfigureAwait(continueOnCapturedContext: false);
				WorldData worldData;
				if (player != null)
				{
					if (!_dalamudUtil.IsInGpose)
					{
						Quaternion rotQuaternion = Quaternion.CreateFromAxisAngle(new Vector3(0f, 1f, 0f), player.Rotation);
						worldData = new WorldData
						{
							PositionX = player.Position.X,
							PositionY = player.Position.Y,
							PositionZ = player.Position.Z,
							RotationW = rotQuaternion.W,
							RotationX = rotQuaternion.X,
							RotationY = rotQuaternion.Y,
							RotationZ = rotQuaternion.Z,
							ScaleX = 1f,
							ScaleY = 1f,
							ScaleZ = 1f
						};
						goto IL_032c;
					}
					player = await _dalamudUtil.GetGposeCharacterFromObjectTableByNameAsync(player.Name.TextValue, onlyGposeCharacters: true).ConfigureAwait(continueOnCapturedContext: false);
					if (player != null)
					{
						worldData = await _brio.GetTransformAsync(player.Address).ConfigureAwait(continueOnCapturedContext: false);
						goto IL_032c;
					}
				}
				goto end_IL_00d5;
				IL_049d:
				foreach (KeyValuePair<string, GposeLobbyUserData> entry in _usersInLobby)
				{
					if (!entry.Value.HasWorldDataUpdate || _dalamudUtil.IsInGpose || !entry.Value.WorldData.HasValue)
					{
						continue;
					}
					WorldData entryWorldData = entry.Value.WorldData.Value;
					if (worldData.LocationInfo.MapId == entryWorldData.LocationInfo.MapId && worldData.LocationInfo.DivisionId == entryWorldData.LocationInfo.DivisionId && (worldData.LocationInfo.HouseId != entryWorldData.LocationInfo.HouseId || worldData.LocationInfo.WardId != entryWorldData.LocationInfo.WardId || entryWorldData.LocationInfo.ServerId != worldData.LocationInfo.ServerId))
					{
						if (!entry.Value.SpawnedVfxId.HasValue)
						{
							entry.Value.LastWorldPosition = new Vector3(entryWorldData.PositionX, entryWorldData.PositionY, entryWorldData.PositionZ);
							GposeLobbyUserData value = entry.Value;
							value.SpawnedVfxId = await _dalamudUtil.RunOnFrameworkThread(() => _vfxSpawnManager.SpawnObject(entry.Value.LastWorldPosition.Value, Quaternion.Identity, Vector3.One, 0.5f, 0.1f, 0.5f, 0.9f), "GposeWorldPositionBackgroundTask", "\\\\wsl.localhost\\Ubuntu\\home\\ddev\\xivsync\\sync_client2\\XIVSync\\Services\\CharaData\\CharaDataGposeTogetherManager.cs", 479).ConfigureAwait(continueOnCapturedContext: false);
							continue;
						}
						Vector3 newPosition = new Vector3(entryWorldData.PositionX, entryWorldData.PositionY, entryWorldData.PositionZ);
						Vector3 value2 = newPosition;
						Vector3? lastWorldPosition = entry.Value.LastWorldPosition;
						if (value2 != lastWorldPosition)
						{
							entry.Value.UpdateStart = DateTime.UtcNow;
							entry.Value.TargetWorldPosition = newPosition;
						}
					}
					else
					{
						await _dalamudUtil.RunOnFrameworkThread(delegate
						{
							_vfxSpawnManager.DespawnObject(entry.Value.SpawnedVfxId);
						}, "GposeWorldPositionBackgroundTask", "\\\\wsl.localhost\\Ubuntu\\home\\ddev\\xivsync\\sync_client2\\XIVSync\\Services\\CharaData\\CharaDataGposeTogetherManager.cs", 495).ConfigureAwait(continueOnCapturedContext: false);
						entry.Value.SpawnedVfxId = null;
					}
				}
				goto end_IL_00d5;
				IL_032c:
				worldData.LocationInfo = await _dalamudUtil.GetMapDataAsync().ConfigureAwait(continueOnCapturedContext: false);
				if (!_forceResendWorldData)
				{
					WorldData value3 = worldData;
					WorldData? lastWorldData = _lastWorldData;
					if (!(value3 != lastWorldData))
					{
						goto IL_049d;
					}
				}
				_forceResendWorldData = false;
				await _apiController.GposeLobbyPushWorldData(worldData).ConfigureAwait(continueOnCapturedContext: false);
				_lastWorldData = worldData;
				base.Logger.LogTrace("WorldData (gpose: {gpose}): {data}", _dalamudUtil.IsInGpose, worldData);
				goto IL_049d;
				end_IL_00d5:;
			}
			catch (Exception exception)
			{
				base.Logger.LogWarning(exception, "Error during World Data Generation");
			}
		}
	}

	private void OnCutsceneFrameworkUpdate()
	{
		foreach (KeyValuePair<string, GposeLobbyUserData> kvp in _usersInLobby)
		{
			if (!string.IsNullOrWhiteSpace(kvp.Value.AssociatedCharaName))
			{
				kvp.Value.Address = _dalamudUtil.GetGposeCharacterFromObjectTableByName(kvp.Value.AssociatedCharaName, onlyGposeCharacters: true)?.Address ?? IntPtr.Zero;
				if (kvp.Value.Address == IntPtr.Zero)
				{
					kvp.Value.AssociatedCharaName = string.Empty;
				}
			}
			if (kvp.Value.Address == IntPtr.Zero || (!kvp.Value.HasWorldDataUpdate && !kvp.Value.HasPoseDataUpdate))
			{
				continue;
			}
			bool hadPoseDataUpdate = kvp.Value.HasPoseDataUpdate;
			bool hadWorldDataUpdate = kvp.Value.HasWorldDataUpdate;
			kvp.Value.HasPoseDataUpdate = false;
			kvp.Value.HasWorldDataUpdate = false;
			Task.Run(async delegate
			{
				if (hadPoseDataUpdate && kvp.Value.ApplicablePoseData.HasValue)
				{
					await _brio.SetPoseAsync(kvp.Value.Address, CreateJsonFromPoseData(kvp.Value.ApplicablePoseData)).ConfigureAwait(continueOnCapturedContext: false);
				}
				if (hadWorldDataUpdate && kvp.Value.WorldData.HasValue)
				{
					await _brio.ApplyTransformAsync(kvp.Value.Address, kvp.Value.WorldData.Value).ConfigureAwait(continueOnCapturedContext: false);
				}
			});
		}
	}

	private void OnEnterGpose()
	{
		ForceResendOwnData();
		ResetOwnData();
		foreach (GposeLobbyUserData data in _usersInLobby.Values)
		{
			_dalamudUtil.RunOnFrameworkThread(delegate
			{
				_vfxSpawnManager.DespawnObject(data.SpawnedVfxId);
			}, "OnEnterGpose", "\\\\wsl.localhost\\Ubuntu\\home\\ddev\\xivsync\\sync_client2\\XIVSync\\Services\\CharaData\\CharaDataGposeTogetherManager.cs", 548);
			data.Reset();
		}
	}

	private void OnExitGpose()
	{
		ForceResendOwnData();
		ResetOwnData();
		foreach (GposeLobbyUserData value in _usersInLobby.Values)
		{
			value.Reset();
		}
	}

	private void ForceResendOwnData()
	{
		_forceResendFullPose = true;
		_forceResendWorldData = true;
	}

	private void ResetOwnData()
	{
		_poseGenerationExecutions = 0;
		_lastCreatedCharaData = null;
	}

	private void OnFrameworkUpdate()
	{
		DateTime frameworkTime = DateTime.UtcNow;
		foreach (KeyValuePair<string, GposeLobbyUserData> kvp in _usersInLobby)
		{
			if (kvp.Value.SpawnedVfxId.HasValue && kvp.Value.UpdateStart.HasValue)
			{
				double secondsElasped = frameworkTime.Subtract(kvp.Value.UpdateStart.Value).TotalSeconds;
				if (secondsElasped >= 1.0)
				{
					kvp.Value.LastWorldPosition = kvp.Value.TargetWorldPosition;
					kvp.Value.TargetWorldPosition = null;
					kvp.Value.UpdateStart = null;
				}
				else
				{
					Vector3 lerp = Vector3.Lerp(kvp.Value.LastWorldPosition ?? Vector3.One, kvp.Value.TargetWorldPosition ?? Vector3.One, (float)secondsElasped);
					_vfxSpawnManager.MoveObject(kvp.Value.SpawnedVfxId.Value, lerp);
				}
			}
		}
	}

	private void OnReceiveCharaData(CharaDataDownloadDto charaDataDownloadDto)
	{
		if (_usersInLobby.TryGetValue(charaDataDownloadDto.Uploader.UID, out GposeLobbyUserData lobbyData))
		{
			lobbyData.CharaData = charaDataDownloadDto;
			if (lobbyData.Address != IntPtr.Zero && !string.IsNullOrEmpty(lobbyData.AssociatedCharaName))
			{
				ApplyCharaData(lobbyData);
			}
		}
	}

	public async Task ApplyCharaData(GposeLobbyUserData userData)
	{
		if (userData.CharaData == null || userData.Address == IntPtr.Zero || string.IsNullOrEmpty(userData.AssociatedCharaName))
		{
			return;
		}
		await _charaDataCreationSemaphore.WaitAsync(_lobbyCts.Token).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			await _charaDataManager.ApplyCharaData(userData.CharaData, userData.AssociatedCharaName).ConfigureAwait(continueOnCapturedContext: false);
			userData.LastAppliedCharaDataDate = userData.CharaData.UpdatedDate;
			userData.HasPoseDataUpdate = true;
			userData.HasWorldDataUpdate = true;
		}
		finally
		{
			_charaDataCreationSemaphore.Release();
		}
	}

	internal async Task SpawnAndApplyData(GposeLobbyUserData userData)
	{
		if (userData.CharaData == null)
		{
			return;
		}
		await _charaDataSpawnSemaphore.WaitAsync(_lobbyCts.Token).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			userData.HasPoseDataUpdate = false;
			userData.HasWorldDataUpdate = false;
			HandledCharaDataEntry chara = await _charaDataManager.SpawnAndApplyData(userData.CharaData).ConfigureAwait(continueOnCapturedContext: false);
			if (!(chara == null))
			{
				userData.HandledChara = chara;
				userData.AssociatedCharaName = chara.Name;
				userData.HasPoseDataUpdate = true;
				userData.HasWorldDataUpdate = true;
			}
		}
		finally
		{
			_charaDataSpawnSemaphore.Release();
		}
	}

	private void OnReceivePoseData(UserData userData, PoseData poseData)
	{
		if (_usersInLobby.TryGetValue(userData.UID, out GposeLobbyUserData lobbyData))
		{
			if (poseData.IsDelta)
			{
				lobbyData.DeltaPoseData = poseData;
			}
			else
			{
				lobbyData.FullPoseData = poseData;
			}
		}
	}

	private void OnReceiveWorldData(UserData userData, WorldData worldData)
	{
		_usersInLobby[userData.UID].WorldData = worldData;
		_usersInLobby[userData.UID].SetWorldDataDescriptor(_dalamudUtil);
	}

	private void OnUserJoinLobby(UserData userData)
	{
		if (_usersInLobby.ContainsKey(userData.UID))
		{
			OnUserLeaveLobby(userData);
		}
		_usersInLobby[userData.UID] = new GposeLobbyUserData(userData);
		PushCharacterDownloadDto();
	}

	private void OnUserLeaveLobby(UserData msg)
	{
		_usersInLobby.Remove(msg.UID, out GposeLobbyUserData existingData);
		if (existingData != null)
		{
			_dalamudUtil.RunOnFrameworkThread(delegate
			{
				_vfxSpawnManager.DespawnObject(existingData.SpawnedVfxId);
			}, "OnUserLeaveLobby", "\\\\wsl.localhost\\Ubuntu\\home\\ddev\\xivsync\\sync_client2\\XIVSync\\Services\\CharaData\\CharaDataGposeTogetherManager.cs", 693);
		}
	}
}
