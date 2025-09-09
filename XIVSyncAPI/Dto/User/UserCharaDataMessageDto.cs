using System.Collections.Generic;
using MessagePack;
using XIVSync.API.Data;

namespace XIVSync.API.Dto.User;

[MessagePackObject(true)]
public record UserCharaDataMessageDto(List<UserData> Recipients, CharacterData CharaData, CensusDataDto? CensusDataDto);
