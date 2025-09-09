namespace XIVSync.Services.Mediator;

public record PenumbraResourceLoadMessage(nint GameObject, string GamePath, string FilePath) : SameThreadMessage();
