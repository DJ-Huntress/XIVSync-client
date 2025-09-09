using XIVSync.API.Data;

namespace XIVSync.Services.Mediator;

public record OpenCharaDataHubWithFilterMessage(UserData UserData) : MessageBase();
