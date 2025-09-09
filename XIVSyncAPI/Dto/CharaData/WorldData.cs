using MessagePack;

namespace XIVSync.API.Dto.CharaData;

[MessagePackObject(false)]
public record struct WorldData
{
	[Key(0)]
	public LocationInfo LocationInfo { get; set; }

	[Key(1)]
	public float PositionX { get; set; }

	[Key(2)]
	public float PositionY { get; set; }

	[Key(3)]
	public float PositionZ { get; set; }

	[Key(4)]
	public float RotationX { get; set; }

	[Key(5)]
	public float RotationY { get; set; }

	[Key(6)]
	public float RotationZ { get; set; }

	[Key(7)]
	public float RotationW { get; set; }

	[Key(8)]
	public float ScaleX { get; set; }

	[Key(9)]
	public float ScaleY { get; set; }

	[Key(10)]
	public float ScaleZ { get; set; }
}
