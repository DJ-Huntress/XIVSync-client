using MessagePack;

namespace XIVSync.API.Dto.CharaData;

[MessagePackObject(false)]
public record struct LocationInfo
{
	[Key(0)]
	public uint ServerId { get; set; }

	[Key(1)]
	public uint MapId { get; set; }

	[Key(2)]
	public uint TerritoryId { get; set; }

	[Key(3)]
	public uint DivisionId { get; set; }

	[Key(4)]
	public uint WardId { get; set; }

	[Key(5)]
	public uint HouseId { get; set; }

	[Key(6)]
	public uint RoomId { get; set; }
}
