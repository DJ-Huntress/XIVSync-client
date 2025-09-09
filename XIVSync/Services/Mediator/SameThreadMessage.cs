namespace XIVSync.Services.Mediator;

public record SameThreadMessage : MessageBase
{
	public override bool KeepThreadContext => true;
}
