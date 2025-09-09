using MessagePack;

namespace XIVSync.API.Dto.Files;

[MessagePackObject(true)]
public record DownloadFileDto : ITransferFileDto
{
	public bool FileExists { get; set; } = true;

	public string Hash { get; set; } = string.Empty;

	public string Url { get; set; } = string.Empty;

	public long Size { get; set; }

	public bool IsForbidden { get; set; }

	public string ForbiddenBy { get; set; } = string.Empty;

	public long RawSize { get; set; }
}
