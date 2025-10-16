using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XIVSync.MareConfiguration;
using XIVSync.Utils;

namespace XIVSync.Services.Mediator;

public sealed class MareMediator : IHostedService
{
	private sealed class SubscriberAction
	{
		public object Action { get; }

		public IMediatorSubscriber Subscriber { get; }

		public SubscriberAction(IMediatorSubscriber subscriber, object action)
		{
			Subscriber = subscriber;
			Action = action;
		}
	}

	private readonly object _addRemoveLock = new object();

	private readonly ConcurrentDictionary<object, DateTime> _lastErrorTime = new ConcurrentDictionary<object, DateTime>();

	private readonly ILogger<MareMediator> _logger;

	private readonly CancellationTokenSource _loopCts = new CancellationTokenSource();

	private readonly ConcurrentQueue<MessageBase> _messageQueue = new ConcurrentQueue<MessageBase>();

	private readonly PerformanceCollectorService _performanceCollector;

	private readonly MareConfigService _mareConfigService;

	private readonly ConcurrentDictionary<Type, HashSet<SubscriberAction>> _subscriberDict = new ConcurrentDictionary<Type, HashSet<SubscriberAction>>();

	private bool _processQueue;

	private readonly ConcurrentDictionary<Type, MethodInfo?> _genericExecuteMethods = new ConcurrentDictionary<Type, MethodInfo>();

	public MareMediator(ILogger<MareMediator> logger, PerformanceCollectorService performanceCollector, MareConfigService mareConfigService)
	{
		_logger = logger;
		_performanceCollector = performanceCollector;
		_mareConfigService = mareConfigService;
	}

	public void PrintSubscriberInfo()
	{
		foreach (IMediatorSubscriber subscriber in _subscriberDict.SelectMany<KeyValuePair<Type, HashSet<SubscriberAction>>, IMediatorSubscriber>((KeyValuePair<Type, HashSet<SubscriberAction>> c) => c.Value.Select((SubscriberAction v) => v.Subscriber)).DistinctBy((IMediatorSubscriber p) => p).OrderBy<IMediatorSubscriber, string>((IMediatorSubscriber p) => p.GetType().FullName, StringComparer.Ordinal)
			.ToList())
		{
			_logger.LogInformation("Subscriber {type}: {sub}", subscriber.GetType().Name, subscriber.ToString());
			StringBuilder sb = new StringBuilder();
			sb.Append("=> ");
			foreach (KeyValuePair<Type, HashSet<SubscriberAction>> item in _subscriberDict.Where<KeyValuePair<Type, HashSet<SubscriberAction>>>((KeyValuePair<Type, HashSet<SubscriberAction>> item) => item.Value.Any((SubscriberAction v) => v.Subscriber == subscriber)).ToList())
			{
				sb.Append(item.Key.Name).Append(", ");
			}
			if (!string.Equals(sb.ToString(), "=> ", StringComparison.Ordinal))
			{
				_logger.LogInformation("{sb}", sb.ToString());
			}
			_logger.LogInformation("---");
		}
	}

	public void Publish<T>(T message) where T : MessageBase
	{
		if (message.KeepThreadContext)
		{
			ExecuteMessage(message);
		}
		else
		{
			_messageQueue.Enqueue(message);
		}
	}

	public Task StartAsync(CancellationToken cancellationToken)
	{
		_logger.LogInformation("Starting MareMediator");
		Task.Run(async delegate
		{
			while (!_loopCts.Token.IsCancellationRequested)
			{
				while (!_processQueue)
				{
					await Task.Delay(100, _loopCts.Token).ConfigureAwait(continueOnCapturedContext: false);
				}
				await Task.Delay(100, _loopCts.Token).ConfigureAwait(continueOnCapturedContext: false);
				HashSet<MessageBase> processedMessages = new HashSet<MessageBase>();
				MessageBase message;
				while (_messageQueue.TryDequeue(out message))
				{
					if (!processedMessages.Contains(message))
					{
						processedMessages.Add(message);
						ExecuteMessage(message);
					}
				}
			}
		});
		_logger.LogInformation("Started MareMediator");
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		_messageQueue.Clear();
		_loopCts.Cancel();
		_loopCts.Dispose();
		return Task.CompletedTask;
	}

	public void Subscribe<T>(IMediatorSubscriber subscriber, Action<T> action) where T : MessageBase
	{
		lock (_addRemoveLock)
		{
			_subscriberDict.TryAdd(typeof(T), new HashSet<SubscriberAction>());
			if (!_subscriberDict[typeof(T)].Add(new SubscriberAction(subscriber, action)))
			{
				throw new InvalidOperationException("Already subscribed");
			}
		}
	}

	public void Unsubscribe<T>(IMediatorSubscriber subscriber) where T : MessageBase
	{
		lock (_addRemoveLock)
		{
			if (_subscriberDict.ContainsKey(typeof(T)))
			{
				_subscriberDict[typeof(T)].RemoveWhere((SubscriberAction p) => p.Subscriber == subscriber);
			}
		}
	}

	internal void UnsubscribeAll(IMediatorSubscriber subscriber)
	{
		lock (_addRemoveLock)
		{
			foreach (Type kvp in _subscriberDict.Select<KeyValuePair<Type, HashSet<SubscriberAction>>, Type>((KeyValuePair<Type, HashSet<SubscriberAction>> k) => k.Key))
			{
				if ((_subscriberDict[kvp]?.RemoveWhere((SubscriberAction p) => p.Subscriber == subscriber) ?? 0) > 0)
				{
					_logger.LogDebug("{sub} unsubscribed from {msg}", subscriber.GetType().Name, kvp.Name);
				}
			}
		}
	}

	private void ExecuteMessage(MessageBase message)
	{
		if (!_subscriberDict.TryGetValue(message.GetType(), out HashSet<SubscriberAction> subscribers) || subscribers == null || !subscribers.Any())
		{
			return;
		}
		List<SubscriberAction> subscribersCopy = new List<SubscriberAction>();
		lock (_addRemoveLock)
		{
			subscribersCopy = (from k in subscribers?.Where((SubscriberAction s) => s.Subscriber != null)
				orderby (!(k.Subscriber is IHighPriorityMediatorSubscriber)) ? 1 : 0
				select k).ToList() ?? new List<SubscriberAction>();
		}
		Type msgType = message.GetType();
		if (!_genericExecuteMethods.TryGetValue(msgType, out MethodInfo methodInfo))
		{
			methodInfo = (_genericExecuteMethods[msgType] = GetType().GetMethod("ExecuteReflected", BindingFlags.Instance | BindingFlags.NonPublic)?.MakeGenericMethod(msgType));
		}
		methodInfo.Invoke(this, new object[2] { subscribersCopy, message });
	}

	private void ExecuteReflected<T>(List<SubscriberAction> subscribers, T message) where T : MessageBase
	{
		foreach (SubscriberAction subscriber in subscribers)
		{
			try
			{
				if (_mareConfigService.Current.LogPerformance)
				{
					string isSameThread = (message.KeepThreadContext ? "$" : string.Empty);
					PerformanceCollectorService performanceCollector = _performanceCollector;
					MareInterpolatedStringHandler counterName = new MareInterpolatedStringHandler(10, 4);
					counterName.AppendFormatted(isSameThread);
					counterName.AppendLiteral("Execute>");
					counterName.AppendFormatted(message.GetType().Name);
					counterName.AppendLiteral("+");
					counterName.AppendFormatted(subscriber.Subscriber.GetType().Name);
					counterName.AppendLiteral(">");
					counterName.AppendFormatted(subscriber.Subscriber);
					performanceCollector.LogPerformance(this, counterName, delegate
					{
						((Action<T>)subscriber.Action)(message);
					});
				}
				else
				{
					((Action<T>)subscriber.Action)(message);
				}
			}
			catch (Exception ex)
			{
				if (!_lastErrorTime.TryGetValue(subscriber, out var lastErrorTime) || !(lastErrorTime.Add(TimeSpan.FromSeconds(10L)) > DateTime.UtcNow))
				{
					_logger.LogError(ex.InnerException ?? ex, "Error executing {type} for subscriber {subscriber}", message.GetType().Name, subscriber.Subscriber.GetType().Name);
					_lastErrorTime[subscriber] = DateTime.UtcNow;
				}
			}
		}
	}

	public void StartQueueProcessing()
	{
		_logger.LogInformation("Starting Message Queue Processing");
		_processQueue = true;
	}
}
