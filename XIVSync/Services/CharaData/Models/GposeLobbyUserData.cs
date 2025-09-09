using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Utility;
using Lumina.Excel.Sheets;
using XIVSync.API.Data;
using XIVSync.API.Dto.CharaData;
using XIVSync.Utils;

namespace XIVSync.Services.CharaData.Models;

public sealed record GposeLobbyUserData(UserData UserData)
{
	public WorldData? WorldData
	{
		get
		{
			return _worldData;
		}
		set
		{
			_worldData = value;
			HasWorldDataUpdate = true;
		}
	}

	public bool HasWorldDataUpdate { get; set; }

	public PoseData? FullPoseData
	{
		get
		{
			return _fullPoseData;
		}
		set
		{
			_fullPoseData = value;
			ApplicablePoseData = CombinePoseData();
			HasPoseDataUpdate = true;
		}
	}

	public PoseData? DeltaPoseData
	{
		get
		{
			return _deltaPoseData;
		}
		set
		{
			_deltaPoseData = value;
			ApplicablePoseData = CombinePoseData();
			HasPoseDataUpdate = true;
		}
	}

	public PoseData? ApplicablePoseData { get; private set; }

	public bool HasPoseDataUpdate { get; set; }

	public Guid? SpawnedVfxId { get; set; }

	public Vector3? LastWorldPosition { get; set; }

	public Vector3? TargetWorldPosition { get; set; }

	public DateTime? UpdateStart { get; set; }

	public CharaDataDownloadDto? CharaData
	{
		get
		{
			return _charaData;
		}
		set
		{
			_charaData = value;
			LastUpdatedCharaData = _charaData?.UpdatedDate ?? DateTime.MaxValue;
		}
	}

	public DateTime LastUpdatedCharaData { get; private set; } = DateTime.MaxValue;

	public DateTime LastAppliedCharaDataDate { get; set; } = DateTime.MinValue;

	public nint Address { get; set; }

	public string AssociatedCharaName { get; set; } = string.Empty;

	public string WorldDataDescriptor { get; private set; } = string.Empty;

	public Vector2 MapCoordinates { get; private set; }

	public Map Map { get; private set; }

	public HandledCharaDataEntry? HandledChara { get; set; }

	private WorldData? _worldData;

	private PoseData? _fullPoseData;

	private PoseData? _deltaPoseData;

	private CharaDataDownloadDto? _charaData;

	public void Reset()
	{
		HasWorldDataUpdate = WorldData.HasValue;
		HasPoseDataUpdate = ApplicablePoseData.HasValue;
		SpawnedVfxId = null;
		LastAppliedCharaDataDate = DateTime.MinValue;
	}

	private PoseData? CombinePoseData()
	{
		if (!DeltaPoseData.HasValue && FullPoseData.HasValue)
		{
			return FullPoseData;
		}
		if (!FullPoseData.HasValue)
		{
			return null;
		}
		PoseData output = FullPoseData.Value.DeepClone();
		PoseData delta = DeltaPoseData.Value;
		foreach (KeyValuePair<string, BoneData> bone in FullPoseData.Value.Bones)
		{
			if (delta.Bones.TryGetValue(bone.Key, out var data))
			{
				if (!data.Exists)
				{
					output.Bones.Remove(bone.Key);
				}
				else
				{
					output.Bones[bone.Key] = data;
				}
			}
		}
		foreach (KeyValuePair<string, BoneData> bone2 in FullPoseData.Value.MainHand)
		{
			if (delta.MainHand.TryGetValue(bone2.Key, out var data2))
			{
				if (!data2.Exists)
				{
					output.MainHand.Remove(bone2.Key);
				}
				else
				{
					output.MainHand[bone2.Key] = data2;
				}
			}
		}
		foreach (KeyValuePair<string, BoneData> bone3 in FullPoseData.Value.OffHand)
		{
			if (delta.OffHand.TryGetValue(bone3.Key, out var data3))
			{
				if (!data3.Exists)
				{
					output.OffHand.Remove(bone3.Key);
				}
				else
				{
					output.OffHand[bone3.Key] = data3;
				}
			}
		}
		return output;
	}

	public async Task SetWorldDataDescriptor(DalamudUtilService dalamudUtilService)
	{
		if (!WorldData.HasValue)
		{
			WorldDataDescriptor = "No World Data found";
		}
		WorldData worldData = WorldData.Value;
		MapCoordinates = await dalamudUtilService.RunOnFrameworkThread(() => MapUtil.WorldToMap(new Vector2(worldData.PositionX, worldData.PositionY), dalamudUtilService.MapData.Value[worldData.LocationInfo.MapId].Map), "SetWorldDataDescriptor", "\\\\wsl.localhost\\Ubuntu\\home\\ddev\\xivsync\\sync_client2\\XIVSync\\Services\\CharaData\\Models\\GposeLobbyUserData.cs", 142).ConfigureAwait(continueOnCapturedContext: false);
		Map = dalamudUtilService.MapData.Value[worldData.LocationInfo.MapId].Map;
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("Server: " + dalamudUtilService.WorldData.Value[(ushort)worldData.LocationInfo.ServerId]);
		sb.AppendLine("Territory: " + dalamudUtilService.TerritoryData.Value[worldData.LocationInfo.TerritoryId]);
		sb.AppendLine("Map: " + dalamudUtilService.MapData.Value[worldData.LocationInfo.MapId].MapName);
		if (worldData.LocationInfo.WardId != 0)
		{
			sb.AppendLine("Ward #: " + worldData.LocationInfo.WardId);
		}
		if (worldData.LocationInfo.DivisionId != 0)
		{
			StringBuilder stringBuilder = sb;
			stringBuilder.AppendLine("Subdivision: " + worldData.LocationInfo.DivisionId switch
			{
				1u => "No", 
				2u => "Yes", 
				_ => "-", 
			});
		}
		if (worldData.LocationInfo.HouseId != 0)
		{
			sb.AppendLine("House #: " + ((worldData.LocationInfo.HouseId == 100) ? "Apartments" : worldData.LocationInfo.HouseId.ToString()));
		}
		if (worldData.LocationInfo.RoomId != 0)
		{
			sb.AppendLine("Apartment #: " + worldData.LocationInfo.RoomId);
		}
		sb.AppendLine("Coordinates: X: " + MapCoordinates.X.ToString("0.0", CultureInfo.InvariantCulture) + ", Y: " + MapCoordinates.Y.ToString("0.0", CultureInfo.InvariantCulture));
		WorldDataDescriptor = sb.ToString();
	}

	[CompilerGenerated]
	private bool PrintMembers(StringBuilder builder)
	{
		RuntimeHelpers.EnsureSufficientExecutionStack();
		builder.Append("UserData = ");
		builder.Append(UserData);
		builder.Append(", WorldData = ");
		builder.Append(WorldData.ToString());
		builder.Append(", HasWorldDataUpdate = ");
		builder.Append(HasWorldDataUpdate.ToString());
		builder.Append(", FullPoseData = ");
		builder.Append(FullPoseData.ToString());
		builder.Append(", DeltaPoseData = ");
		builder.Append(DeltaPoseData.ToString());
		builder.Append(", ApplicablePoseData = ");
		builder.Append(ApplicablePoseData.ToString());
		builder.Append(", HasPoseDataUpdate = ");
		builder.Append(HasPoseDataUpdate.ToString());
		builder.Append(", SpawnedVfxId = ");
		builder.Append(SpawnedVfxId.ToString());
		builder.Append(", LastWorldPosition = ");
		builder.Append(LastWorldPosition.ToString());
		builder.Append(", TargetWorldPosition = ");
		builder.Append(TargetWorldPosition.ToString());
		builder.Append(", UpdateStart = ");
		builder.Append(UpdateStart.ToString());
		builder.Append(", CharaData = ");
		builder.Append(CharaData);
		builder.Append(", LastUpdatedCharaData = ");
		builder.Append(LastUpdatedCharaData.ToString());
		builder.Append(", LastAppliedCharaDataDate = ");
		builder.Append(LastAppliedCharaDataDate.ToString());
		builder.Append(", Address = ");
		builder.Append(((object)Address/*cast due to .constrained prefix*/).ToString());
		builder.Append(", AssociatedCharaName = ");
		builder.Append((object?)AssociatedCharaName);
		builder.Append(", WorldDataDescriptor = ");
		builder.Append((object?)WorldDataDescriptor);
		builder.Append(", MapCoordinates = ");
		builder.Append(MapCoordinates.ToString());
		builder.Append(", Map = ");
		builder.Append(Map.ToString());
		builder.Append(", HandledChara = ");
		builder.Append(HandledChara);
		return true;
	}

	[CompilerGenerated]
	public override int GetHashCode()
	{
		return (((((((((((((((((((EqualityComparer<Type>.Default.GetHashCode(EqualityContract) * -1521134295 + EqualityComparer<XIVSync.API.Data.UserData>.Default.GetHashCode(UserData)) * -1521134295 + EqualityComparer<XIVSync.API.Dto.CharaData.WorldData?>.Default.GetHashCode(_worldData)) * -1521134295 + EqualityComparer<bool>.Default.GetHashCode(HasWorldDataUpdate)) * -1521134295 + EqualityComparer<PoseData?>.Default.GetHashCode(_fullPoseData)) * -1521134295 + EqualityComparer<PoseData?>.Default.GetHashCode(_deltaPoseData)) * -1521134295 + EqualityComparer<PoseData?>.Default.GetHashCode(ApplicablePoseData)) * -1521134295 + EqualityComparer<bool>.Default.GetHashCode(HasPoseDataUpdate)) * -1521134295 + EqualityComparer<Guid?>.Default.GetHashCode(SpawnedVfxId)) * -1521134295 + EqualityComparer<Vector3?>.Default.GetHashCode(LastWorldPosition)) * -1521134295 + EqualityComparer<Vector3?>.Default.GetHashCode(TargetWorldPosition)) * -1521134295 + EqualityComparer<DateTime?>.Default.GetHashCode(UpdateStart)) * -1521134295 + EqualityComparer<CharaDataDownloadDto>.Default.GetHashCode(_charaData)) * -1521134295 + EqualityComparer<DateTime>.Default.GetHashCode(LastUpdatedCharaData)) * -1521134295 + EqualityComparer<DateTime>.Default.GetHashCode(LastAppliedCharaDataDate)) * -1521134295 + EqualityComparer<nint>.Default.GetHashCode(Address)) * -1521134295 + EqualityComparer<string>.Default.GetHashCode(AssociatedCharaName)) * -1521134295 + EqualityComparer<string>.Default.GetHashCode(WorldDataDescriptor)) * -1521134295 + EqualityComparer<Vector2>.Default.GetHashCode(MapCoordinates)) * -1521134295 + EqualityComparer<Map>.Default.GetHashCode(Map)) * -1521134295 + EqualityComparer<HandledCharaDataEntry>.Default.GetHashCode(HandledChara);
	}

	[CompilerGenerated]
	public bool Equals(GposeLobbyUserData? other)
	{
		if ((object)this != other)
		{
			if ((object)other != null && EqualityContract == other.EqualityContract && EqualityComparer<XIVSync.API.Data.UserData>.Default.Equals(UserData, other.UserData) && EqualityComparer<XIVSync.API.Dto.CharaData.WorldData?>.Default.Equals(_worldData, other._worldData) && EqualityComparer<bool>.Default.Equals(HasWorldDataUpdate, other.HasWorldDataUpdate) && EqualityComparer<PoseData?>.Default.Equals(_fullPoseData, other._fullPoseData) && EqualityComparer<PoseData?>.Default.Equals(_deltaPoseData, other._deltaPoseData) && EqualityComparer<PoseData?>.Default.Equals(ApplicablePoseData, other.ApplicablePoseData) && EqualityComparer<bool>.Default.Equals(HasPoseDataUpdate, other.HasPoseDataUpdate) && EqualityComparer<Guid?>.Default.Equals(SpawnedVfxId, other.SpawnedVfxId) && EqualityComparer<Vector3?>.Default.Equals(LastWorldPosition, other.LastWorldPosition) && EqualityComparer<Vector3?>.Default.Equals(TargetWorldPosition, other.TargetWorldPosition) && EqualityComparer<DateTime?>.Default.Equals(UpdateStart, other.UpdateStart) && EqualityComparer<CharaDataDownloadDto>.Default.Equals(_charaData, other._charaData) && EqualityComparer<DateTime>.Default.Equals(LastUpdatedCharaData, other.LastUpdatedCharaData) && EqualityComparer<DateTime>.Default.Equals(LastAppliedCharaDataDate, other.LastAppliedCharaDataDate) && EqualityComparer<nint>.Default.Equals(Address, other.Address) && EqualityComparer<string>.Default.Equals(AssociatedCharaName, other.AssociatedCharaName) && EqualityComparer<string>.Default.Equals(WorldDataDescriptor, other.WorldDataDescriptor) && EqualityComparer<Vector2>.Default.Equals(MapCoordinates, other.MapCoordinates) && EqualityComparer<Map>.Default.Equals(Map, other.Map))
			{
				return EqualityComparer<HandledCharaDataEntry>.Default.Equals(HandledChara, other.HandledChara);
			}
			return false;
		}
		return true;
	}

	[CompilerGenerated]
	private GposeLobbyUserData(GposeLobbyUserData original)
	{
		UserData = original.UserData;
		_worldData = original._worldData;
		HasWorldDataUpdate = original.HasWorldDataUpdate;
		_fullPoseData = original._fullPoseData;
		_deltaPoseData = original._deltaPoseData;
		ApplicablePoseData = original.ApplicablePoseData;
		HasPoseDataUpdate = original.HasPoseDataUpdate;
		SpawnedVfxId = original.SpawnedVfxId;
		LastWorldPosition = original.LastWorldPosition;
		TargetWorldPosition = original.TargetWorldPosition;
		UpdateStart = original.UpdateStart;
		_charaData = original._charaData;
		LastUpdatedCharaData = original.LastUpdatedCharaData;
		LastAppliedCharaDataDate = original.LastAppliedCharaDataDate;
		Address = original.Address;
		AssociatedCharaName = original.AssociatedCharaName;
		WorldDataDescriptor = original.WorldDataDescriptor;
		MapCoordinates = original.MapCoordinates;
		Map = original.Map;
		HandledChara = original.HandledChara;
	}
}
