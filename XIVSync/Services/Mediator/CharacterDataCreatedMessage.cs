using XIVSync.API.Data;

namespace XIVSync.Services.Mediator;

public record CharacterDataCreatedMessage(CharacterData CharacterData) : SameThreadMessage();
