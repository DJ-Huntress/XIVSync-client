using System;
using MessagePack;
using XIVSync.API.Data;

namespace XIVSync.API.Dto;

[MessagePackObject(true)]
public record ConnectionDto(UserData User)
{
	public Version CurrentClientVersion { get; set; } = new Version(0, 0, 0);

	public int ServerVersion { get; set; }

	public bool IsAdmin { get; set; }

	public bool IsModerator { get; set; }

	public ServerInfo ServerInfo { get; set; } = new ServerInfo();

	public DefaultPermissionsDto DefaultPreferredPermissions { get; set; } = new DefaultPermissionsDto();
}
