using System.Collections.Generic;
using MessagePack;

namespace XIVSync.API.Dto.CharaData;

[MessagePackObject(false)]
public record struct PoseData
{
	[Key(0)]
	public bool IsDelta { get; set; }

	[Key(1)]
	public Dictionary<string, BoneData> Bones { get; set; }

	[Key(2)]
	public Dictionary<string, BoneData> MainHand { get; set; }

	[Key(3)]
	public Dictionary<string, BoneData> OffHand { get; set; }

	[Key(4)]
	public BoneData ModelDifference { get; set; }
}
