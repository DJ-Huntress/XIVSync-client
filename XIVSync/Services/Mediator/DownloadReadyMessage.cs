using System;

namespace XIVSync.Services.Mediator;

public record DownloadReadyMessage(Guid RequestId) : MessageBase();
