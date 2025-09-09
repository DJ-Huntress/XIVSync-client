using XIVSync.PlayerData.Pairs;

namespace XIVSync.Services.Mediator;

public record ProfileOpenStandaloneMessage(Pair Pair) : MessageBase();
