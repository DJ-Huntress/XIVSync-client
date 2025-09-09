namespace XIVSync.Services.Mediator;

public record CensusUpdateMessage(byte Gender, byte RaceId, byte TribeId) : MessageBase();
