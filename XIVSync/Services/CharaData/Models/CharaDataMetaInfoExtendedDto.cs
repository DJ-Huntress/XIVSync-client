using System.Collections.Generic;
using System.Threading.Tasks;
using XIVSync.API.Dto.CharaData;

namespace XIVSync.Services.CharaData.Models;

public sealed record CharaDataMetaInfoExtendedDto : CharaDataMetaInfoDto
{
	public List<PoseEntryExtended> PoseExtended { get; private set; } = new List<PoseEntryExtended>();

	public bool HasPoses => PoseExtended.Count != 0;

	public bool HasWorldData => PoseExtended.Exists((PoseEntryExtended p) => p.HasWorldData);

	public bool IsOwnData { get; private set; }

	public string FullId { get; private set; }

	private CharaDataMetaInfoExtendedDto(CharaDataMetaInfoDto baseMeta)
		: base(baseMeta)
	{
		FullId = baseMeta.Uploader.UID + ":" + baseMeta.Id;
	}

	public static async Task<CharaDataMetaInfoExtendedDto> Create(CharaDataMetaInfoDto baseMeta, DalamudUtilService dalamudUtilService, bool isOwnData = false)
	{
		CharaDataMetaInfoExtendedDto newDto = new CharaDataMetaInfoExtendedDto(baseMeta);
		foreach (PoseEntry poseDatum in newDto.PoseData)
		{
			List<PoseEntryExtended> poseExtended = newDto.PoseExtended;
			poseExtended.Add(await PoseEntryExtended.Create(poseDatum, newDto, dalamudUtilService).ConfigureAwait(continueOnCapturedContext: false));
		}
		newDto.IsOwnData = isOwnData;
		return newDto;
	}
}
