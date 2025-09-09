namespace XIVSync.Services.Mediator;

public record PenumbraRedrawMessage(nint Address, int ObjTblIdx, bool WasRequested) : SameThreadMessage();
