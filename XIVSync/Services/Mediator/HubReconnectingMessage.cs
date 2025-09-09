using System;

namespace XIVSync.Services.Mediator;

public record HubReconnectingMessage(Exception? Exception) : SameThreadMessage();
