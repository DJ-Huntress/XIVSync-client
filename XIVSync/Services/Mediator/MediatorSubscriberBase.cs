using Microsoft.Extensions.Logging;

namespace XIVSync.Services.Mediator;

public abstract class MediatorSubscriberBase : IMediatorSubscriber
{
	public MareMediator Mediator { get; }

	protected ILogger Logger { get; }

	protected MediatorSubscriberBase(ILogger logger, MareMediator mediator)
	{
		Logger = logger;
		Logger.LogTrace("Creating {type} ({this})", GetType().Name, this);
		Mediator = mediator;
	}

	protected void UnsubscribeAll()
	{
		Logger.LogTrace("Unsubscribing from all for {type} ({this})", GetType().Name, this);
		Mediator.UnsubscribeAll(this);
	}
}
