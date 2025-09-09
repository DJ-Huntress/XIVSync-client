using MessagePack;

namespace XIVSync.API.Dto.CharaData;

[MessagePackObject(true)]
public record PoseEntry(long? Id)
{
	public string? Description { get; set; } = string.Empty;

	public string? PoseData { get; set; } = string.Empty;

	public WorldData? WorldData { get; set; }
}
