using XIVSync.API.Data;

namespace XIVSync.Services.Mediator;

public record PauseMessage(UserData UserData) : MessageBase();
