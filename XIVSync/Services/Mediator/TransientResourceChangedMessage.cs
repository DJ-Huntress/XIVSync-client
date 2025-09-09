namespace XIVSync.Services.Mediator;

public record TransientResourceChangedMessage(nint Address) : MessageBase();
