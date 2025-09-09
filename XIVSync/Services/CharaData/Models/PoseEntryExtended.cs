using System.Globalization;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Utility;
using Lumina.Excel.Sheets;
using XIVSync.API.Dto.CharaData;

namespace XIVSync.Services.CharaData.Models;

public sealed record PoseEntryExtended : PoseEntry
{
	public CharaDataMetaInfoExtendedDto MetaInfo { get; }

	public bool HasPoseData { get; }

	public bool HasWorldData { get; }

	public Vector3 Position { get; }

	public Vector2 MapCoordinates { get; private set; }

	public Quaternion Rotation { get; }

	public Map Map { get; private set; }

	public string WorldDataDescriptor { get; private set; } = string.Empty;

	private PoseEntryExtended(PoseEntry basePose, CharaDataMetaInfoExtendedDto parent)
		: base(basePose)
	{
		HasPoseData = !string.IsNullOrEmpty(basePose.PoseData);
		HasWorldData = base.WorldData.GetValueOrDefault() != default(WorldData);
		if (HasWorldData)
		{
			Position = new Vector3(basePose.WorldData.Value.PositionX, basePose.WorldData.Value.PositionY, basePose.WorldData.Value.PositionZ);
			Rotation = new Quaternion(basePose.WorldData.Value.RotationX, basePose.WorldData.Value.RotationY, basePose.WorldData.Value.RotationZ, basePose.WorldData.Value.RotationW);
		}
		MetaInfo = parent;
	}

	public static async Task<PoseEntryExtended> Create(PoseEntry baseEntry, CharaDataMetaInfoExtendedDto parent, DalamudUtilService dalamudUtilService)
	{
		PoseEntryExtended newPose = new PoseEntryExtended(baseEntry, parent);
		if (newPose.HasWorldData)
		{
			WorldData worldData = newPose.WorldData.Value;
			PoseEntryExtended poseEntryExtended = newPose;
			poseEntryExtended.MapCoordinates = await dalamudUtilService.RunOnFrameworkThread(() => MapUtil.WorldToMap(new Vector2(worldData.PositionX, worldData.PositionY), dalamudUtilService.MapData.Value[worldData.LocationInfo.MapId].Map), "Create", "\\\\wsl.localhost\\Ubuntu\\home\\ddev\\xivsync\\sync_client2\\XIVSync\\Services\\CharaData\\Models\\PoseEntryExtended.cs", 40).ConfigureAwait(continueOnCapturedContext: false);
			newPose.Map = dalamudUtilService.MapData.Value[worldData.LocationInfo.MapId].Map;
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
			sb.AppendLine("Coordinates: X: " + newPose.MapCoordinates.X.ToString("0.0", CultureInfo.InvariantCulture) + ", Y: " + newPose.MapCoordinates.Y.ToString("0.0", CultureInfo.InvariantCulture));
			newPose.WorldDataDescriptor = sb.ToString();
		}
		return newPose;
	}
}
