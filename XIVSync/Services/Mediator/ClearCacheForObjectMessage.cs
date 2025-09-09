using XIVSync.PlayerData.Handlers;

namespace XIVSync.Services.Mediator;

public record ClearCacheForObjectMessage(GameObjectHandler ObjectToCreateFor) : SameThreadMessage();
