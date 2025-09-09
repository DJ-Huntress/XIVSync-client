using XIVSync.PlayerData.Handlers;

namespace XIVSync.Services.Mediator;

public record ClassJobChangedMessage(GameObjectHandler GameObjectHandler) : MessageBase();
