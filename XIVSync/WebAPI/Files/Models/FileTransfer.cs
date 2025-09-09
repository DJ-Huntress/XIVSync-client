using XIVSync.API.Dto.Files;

namespace XIVSync.WebAPI.Files.Models;

public abstract class FileTransfer
{
	protected readonly ITransferFileDto TransferDto;

	public virtual bool CanBeTransferred
	{
		get
		{
			if (!TransferDto.IsForbidden)
			{
				if (TransferDto is DownloadFileDto dto)
				{
					return dto.FileExists;
				}
				return true;
			}
			return false;
		}
	}

	public string ForbiddenBy => TransferDto.ForbiddenBy;

	public string Hash => TransferDto.Hash;

	public bool IsForbidden => TransferDto.IsForbidden;

	public bool IsInTransfer
	{
		get
		{
			if (Transferred != Total)
			{
				return Transferred > 0;
			}
			return false;
		}
	}

	public bool IsTransferred => Transferred == Total;

	public abstract long Total { get; set; }

	public long Transferred { get; set; }

	protected FileTransfer(ITransferFileDto transferDto)
	{
		TransferDto = transferDto;
	}

	public override string ToString()
	{
		return Hash;
	}
}
