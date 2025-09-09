using XIVSync.Services.Events;

namespace XIVSync.Services.Mediator;

public record EventMessage(Event Event) : MessageBase();
