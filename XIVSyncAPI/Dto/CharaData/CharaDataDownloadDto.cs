using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MessagePack;
using XIVSync.API.Data;

namespace XIVSync.API.Dto.CharaData;

[MessagePackObject(true)]
public record CharaDataDownloadDto : CharaDataDto
{
	public string GlamourerData { get; init; } = string.Empty;

	public string CustomizeData { get; init; } = string.Empty;

	public string ManipulationData { get; set; } = string.Empty;

	public List<GamePathEntry> FileGamePaths { get; init; } = new List<GamePathEntry>();

	public List<GamePathEntry> FileSwaps { get; init; } = new List<GamePathEntry>();

	public CharaDataDownloadDto(string Id, UserData Uploader)
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
