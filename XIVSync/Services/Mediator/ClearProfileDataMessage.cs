using XIVSync.API.Data;

namespace XIVSync.Services.Mediator;

public record ClearProfileDataMessage(UserData? UserData = null) : MessageBase();
