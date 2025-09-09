using XIVSync.PlayerData.Handlers;

namespace XIVSync.Services.Mediator;

public record GameObjectHandlerCreatedMessage(GameObjectHandler GameObjectHandler, bool OwnedObject) : SameThreadMessage();
