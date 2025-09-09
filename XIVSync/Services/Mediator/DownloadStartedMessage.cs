using System.Collections.Generic;
using XIVSync.PlayerData.Handlers;
using XIVSync.WebAPI.Files.Models;

namespace XIVSync.Services.Mediator;

public record DownloadStartedMessage(GameObjectHandler DownloadId, Dictionary<string, FileDownloadStatus> DownloadStatus) : MessageBase();
