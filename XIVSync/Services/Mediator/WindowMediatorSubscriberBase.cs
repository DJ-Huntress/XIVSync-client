using System;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Interface.Windowing;
using Microsoft.Extensions.Logging;
using XIVSync.Utils;

namespace XIVSync.Services.Mediator;

public abstract class WindowMediatorSubscriberBase : Window, IMediatorSubscriber, IDisposable
{
	protected readonly ILogger _logger;

	private readonly PerformanceCollectorService _performanceCollectorService;

	public MareMediator Mediator { get; }

	protected WindowMediatorSubscriberBase(ILogger logger, MareMediator mediator, string name, PerformanceCollectorService performanceCollectorService)
		: base(name)
	{
		_logger = logger;
		Mediator = mediator;
		_performanceCollectorService = performanceCollectorService;
		_logger.LogTrace("Creating {type}", GetType());
		Mediator.Subscribe(this, delegate(UiToggleMessage msg)
		{
			if (msg.UiType == GetType())
			{
				Toggle();
			}
		});
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	public override void Draw()
	{
		try
		{
			PerformanceCollectorService performanceCollectorService = _performanceCollectorService;
			MareInterpolatedStringHandler counterName = new MareInterpolatedStringHandler(4, 0);
			counterName.AppendLiteral("Draw");
			performanceCollectorService.LogPerformance(this, counterName, DrawInternal);
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Error in {ClassName}.Draw() - {WindowName}", GetType().Name, base.WindowName);
			throw;
		}
	}

	protected abstract void DrawInternal();

	public virtual Task StopAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}

	protected virtual void Dispose(bool disposing)
	{
		_logger.LogTrace("Disposing {type}", GetType());
		Mediator.UnsubscribeAll(this);
	}
}
