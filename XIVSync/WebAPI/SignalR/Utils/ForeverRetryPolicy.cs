using System;
using Microsoft.AspNetCore.SignalR.Client;
using XIVSync.MareConfiguration.Models;
using XIVSync.Services.Mediator;

namespace XIVSync.WebAPI.SignalR.Utils;

public class ForeverRetryPolicy : IRetryPolicy
{
	private readonly MareMediator _mediator;

	private bool _sentDisconnected;

	public ForeverRetryPolicy(MareMediator mediator)
	{
		_mediator = mediator;
	}

	public TimeSpan? NextRetryDelay(RetryContext retryContext)
	{
		TimeSpan timeToWait = TimeSpan.FromSeconds(new Random().Next(10, 20));
		if (retryContext.PreviousRetryCount == 0L)
		{
			_sentDisconnected = false;
			timeToWait = TimeSpan.FromSeconds(3L);
		}
		else if (retryContext.PreviousRetryCount == 1)
		{
			timeToWait = TimeSpan.FromSeconds(5L);
		}
		else if (retryContext.PreviousRetryCount == 2)
		{
			timeToWait = TimeSpan.FromSeconds(10L);
		}
		else
		{
			if (!_sentDisconnected)
			{
				_mediator.Publish(new NotificationMessage("Connection lost", "Connection lost to server", NotificationType.Warning, TimeSpan.FromSeconds(10L)));
				_mediator.Publish(new DisconnectedMessage());
			}
			_sentDisconnected = true;
		}
		return timeToWait;
	}
}
