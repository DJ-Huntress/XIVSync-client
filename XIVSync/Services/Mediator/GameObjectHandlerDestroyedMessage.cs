using XIVSync.PlayerData.Handlers;

namespace XIVSync.Services.Mediator;

public record GameObjectHandlerDestroyedMessage(GameObjectHandler GameObjectHandler, bool OwnedObject) : SameThreadMessage();
