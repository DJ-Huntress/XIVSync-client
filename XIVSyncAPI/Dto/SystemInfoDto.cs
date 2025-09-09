using MessagePack;

namespace XIVSync.API.Dto;

[MessagePackObject(true)]
public record SystemInfoDto
{
	public int OnlineUsers { get; set; }
}
