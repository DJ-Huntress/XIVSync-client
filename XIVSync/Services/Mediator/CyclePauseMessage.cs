using XIVSync.API.Data;

namespace XIVSync.Services.Mediator;

public record CyclePauseMessage(UserData UserData) : MessageBase();
