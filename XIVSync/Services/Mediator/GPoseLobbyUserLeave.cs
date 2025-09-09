using XIVSync.API.Data;

namespace XIVSync.Services.Mediator;

public record GPoseLobbyUserLeave(UserData UserData) : MessageBase();
