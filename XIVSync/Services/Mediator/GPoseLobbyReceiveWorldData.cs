using XIVSync.API.Data;
using XIVSync.API.Dto.CharaData;

namespace XIVSync.Services.Mediator;

public record GPoseLobbyReceiveWorldData(UserData UserData, WorldData WorldData) : MessageBase();
