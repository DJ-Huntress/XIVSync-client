using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MessagePack;
using XIVSync.API.Data;

namespace XIVSync.API.Dto.CharaData;

[MessagePackObject(true)]
public record CharaDataFullDto : CharaDataDto
{
	public DateTime CreatedDate { get; init; }

	public DateTime ExpiryDate { get; set; }

	public string GlamourerData { get; set; } = string.Empty;

	public string CustomizeData { get; set; } = string.Empty;

	public string ManipulationData { get; set; } = string.Empty;

	public int DownloadCount { get; set; }

	public List<UserData> AllowedUsers { get; set; } = new List<UserData>();

	public List<GroupData> AllowedGroups { get; set; } = new List<GroupData>();

	public List<GamePathEntry> FileGamePaths { get; set; } = new List<GamePathEntry>();

	public List<GamePathEntry> FileSwaps { get; set; } = new List<GamePathEntry>();

	public List<GamePathEntry> OriginalFiles { get; set; } = new List<GamePathEntry>();

	public AccessTypeDto AccessType { get; set; }

	public ShareTypeDto ShareType { get; set; }

	public List<PoseEntry> PoseData { get; set; } = new List<PoseEntry>();

	public CharaDataFullDto(string Id, UserData Uploader)
		: base(Id, Uploader)
	{
	}

	[CompilerGenerated]
	public new void Deconstruct(out string Id, out UserData Uploader)
	{
		Id = base.Id;
		Uploader = base.Uploader;
	}
}
