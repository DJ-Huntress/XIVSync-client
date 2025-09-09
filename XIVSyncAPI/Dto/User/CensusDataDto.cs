using MessagePack;

namespace XIVSync.API.Dto.User;

[MessagePackObject(true)]
public record CensusDataDto(ushort WorldId, short RaceId, short TribeId, short Gender);
