using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MessagePack;
using XIVSync.API.Data;

namespace XIVSync.API.Dto.CharaData;

[MessagePackObject(true)]
public record CharaDataMetaInfoDto : CharaDataDto
{
	public bool CanBeDownloaded { get; init; }

	public List<PoseEntry> PoseData { get; set; } = new List<PoseEntry>();

	public CharaDataMetaInfoDto(string Id, UserData Uploader)
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
