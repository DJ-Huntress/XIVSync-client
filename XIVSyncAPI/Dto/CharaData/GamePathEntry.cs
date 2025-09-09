using MessagePack;

namespace XIVSync.API.Dto.CharaData;

[MessagePackObject(true)]
public record GamePathEntry(string HashOrFileSwap, string GamePath);
