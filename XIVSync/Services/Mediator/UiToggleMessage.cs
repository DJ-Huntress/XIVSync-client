using System;

namespace XIVSync.Services.Mediator;

public record UiToggleMessage(Type UiType) : MessageBase();
