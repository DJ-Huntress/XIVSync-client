using MessagePack;
using XIVSync.API.Data;

namespace XIVSync.API.Dto.User;

[MessagePackObject(true)]
public record UserDto(UserData User);
