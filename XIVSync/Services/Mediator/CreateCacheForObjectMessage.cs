using XIVSync.PlayerData.Handlers;

namespace XIVSync.Services.Mediator;

public record CreateCacheForObjectMessage(GameObjectHandler ObjectToCreateFor) : SameThreadMessage();
