using System.Collections.Generic;

namespace XIVSync.API.Dto.Files;

public class FilesSendDto
{
	public List<string> FileHashes { get; set; } = new List<string>();

	public List<string> UIDs { get; set; } = new List<string>();
}
