namespace XIVSync.Services.Mediator;

public abstract record MessageBase
{
	public virtual bool KeepThreadContext => false;
}
