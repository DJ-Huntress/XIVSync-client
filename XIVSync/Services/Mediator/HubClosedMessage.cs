using System;

namespace XIVSync.Services.Mediator;

public record HubClosedMessage(Exception? Exception) : SameThreadMessage();
