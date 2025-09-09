using XIVSync.API.Dto;

namespace XIVSync.Services.Mediator;

public record ConnectedMessage(ConnectionDto Connection) : MessageBase();
