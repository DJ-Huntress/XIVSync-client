using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using XIVSync.API.Dto.CharaData;

namespace XIVSync.Services.CharaData.Models;

public sealed record CharaDataFullExtendedDto : CharaDataFullDto
{
	public string FullId { get; set; }

	public bool HasMissingFiles { get; init; }

	public IReadOnlyCollection<GamePathEntry> MissingFiles { get; init; }

	public CharaDataFullExtendedDto(CharaDataFullDto baseDto)
		: base(baseDto)
	{
		FullId = baseDto.Uploader.UID + ":" + baseDto.Id;
		MissingFiles = new ReadOnlyCollection<GamePathEntry>(baseDto.OriginalFiles.Except(baseDto.FileGamePaths).ToList());
		HasMissingFiles = MissingFiles.Any();
	}
}
