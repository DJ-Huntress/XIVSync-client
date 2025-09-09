using XIVSync.API.Data;

namespace XIVSync.Services.Mediator;

public record GposeLobbyUserJoin(UserData UserData) : MessageBase();
