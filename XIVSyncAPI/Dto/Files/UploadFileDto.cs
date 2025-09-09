using MessagePack;

namespace XIVSync.API.Dto.Files;

[MessagePackObject(true)]
public record UploadFileDto : ITransferFileDto
{
	public string Hash { get; set; } = string.Empty;

	public bool IsForbidden { get; set; }

	public string ForbiddenBy { get; set; } = string.Empty;
}
