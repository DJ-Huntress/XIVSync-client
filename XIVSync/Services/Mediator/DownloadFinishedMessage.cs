using XIVSync.PlayerData.Handlers;

namespace XIVSync.Services.Mediator;

public record DownloadFinishedMessage(GameObjectHandler DownloadId) : MessageBase();
