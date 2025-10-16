using System;

namespace XIVSync.MareConfiguration.Models;

[Serializable]
public record Authentication
{
	public string CharacterName { get; set; } = string.Empty;


	public uint WorldId { get; set; }

	public int SecretKeyIdx { get; set; } = -1;


	public string? UID { get; set; }

	public bool AutoLogin { get; set; } = true;


	public ulong? LastSeenCID { get; set; }
}
