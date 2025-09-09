using XIVSync.PlayerData.Pairs;

namespace XIVSync.Services.Mediator;

public record TargetPairMessage(Pair Pair) : MessageBase();
