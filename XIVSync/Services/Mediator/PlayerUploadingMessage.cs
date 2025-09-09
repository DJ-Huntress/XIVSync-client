using XIVSync.PlayerData.Handlers;

namespace XIVSync.Services.Mediator;

public record PlayerUploadingMessage(GameObjectHandler Handler, bool IsUploading) : MessageBase();
