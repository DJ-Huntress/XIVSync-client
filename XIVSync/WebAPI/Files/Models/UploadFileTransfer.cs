using XIVSync.API.Dto.Files;

namespace XIVSync.WebAPI.Files.Models;

public class UploadFileTransfer : FileTransfer
{
	public string LocalFile { get; set; } = string.Empty;


	public override long Total { get; set; }

	public UploadFileTransfer(UploadFileDto dto)
		: base(dto)
	{
	}
}
