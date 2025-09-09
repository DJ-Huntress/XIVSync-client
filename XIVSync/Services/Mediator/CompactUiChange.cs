using System.Numerics;

namespace XIVSync.Services.Mediator;

public record CompactUiChange(Vector2 Size, Vector2 Position) : MessageBase();
