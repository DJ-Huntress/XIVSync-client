using System;
using XIVSync.API.Dto.Files;

namespace XIVSync.WebAPI.Files.Models;

public class DownloadFileTransfer : FileTransfer
{
	public override bool CanBeTransferred
	{
		get
		{
			if (Dto.FileExists && !Dto.IsForbidden)
			{
				return Dto.Size > 0;
			}
			return false;
		}
	}

	public Uri DownloadUri => new Uri(Dto.Url);

	public override long Total
	{
		get
		{
			return Dto.Size;
		}
		set
		{
		}
	}

	public long TotalRaw => Dto.RawSize;

	private DownloadFileDto Dto => (DownloadFileDto)TransferDto;

	public DownloadFileTransfer(DownloadFileDto dto)
		: base(dto)
	{
	}
}
