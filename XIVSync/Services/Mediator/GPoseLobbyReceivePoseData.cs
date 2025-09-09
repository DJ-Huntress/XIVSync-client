using XIVSync.API.Data;
using XIVSync.API.Dto.CharaData;

namespace XIVSync.Services.Mediator;

public record GPoseLobbyReceivePoseData(UserData UserData, PoseData PoseData) : MessageBase();
