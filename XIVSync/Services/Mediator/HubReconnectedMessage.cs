namespace XIVSync.Services.Mediator;

public record HubReconnectedMessage(string? Arg) : SameThreadMessage();
