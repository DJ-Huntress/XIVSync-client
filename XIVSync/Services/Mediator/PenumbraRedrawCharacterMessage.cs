using Dalamud.Game.ClientState.Objects.Types;

namespace XIVSync.Services.Mediator;

public record PenumbraRedrawCharacterMessage(ICharacter Character) : SameThreadMessage();
