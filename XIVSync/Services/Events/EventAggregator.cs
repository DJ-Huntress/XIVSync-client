using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XIVSync.Services.Mediator;
using XIVSync.Utils;

namespace XIVSync.Services.Events;

public class EventAggregator : MediatorSubscriberBase, IHostedService
{
	private readonly RollingList<Event> _events = new RollingList<Event>(500);

	private readonly SemaphoreSlim _lock = new SemaphoreSlim(1);

	private readonly string _configDirectory;

	private readonly ILogger<EventAggregator> _logger;

	private DateTime _currentTime;

	public Lazy<List<Event>> EventList { get; private set; }

	public bool NewEventsAvailable => !EventList.IsValueCreated;

	public string EventLogFolder => Path.Combine(_configDirectory, "eventlog");

	private string CurrentLogName => $"{DateTime.Now:yyyy-MM-dd}-events.log";

	public EventAggregator(string configDirectory, ILogger<EventAggregator> logger, MareMediator mareMediator)
		: base(logger, mareMediator)
	{
		base.Mediator.Subscribe(this, delegate(EventMessage msg)
		{
			_lock.Wait();
			try
			{
				base.Logger.LogTrace("Received Event: {evt}", msg.Event.ToString());
				_events.Add(msg.Event);
				WriteToFile(msg.Event);
			}
			finally
			{
				_lock.Release();
			}
			RecreateLazy();
		});
		EventList = CreateEventLazy();
		_configDirectory = configDirectory;
		_logger = logger;
		_currentTime = DateTime.Now - TimeSpan.FromDays(1);
	}

	private void RecreateLazy()
	{
		if (EventList.IsValueCreated)
		{
			EventList = CreateEventLazy();
		}
	}

	private Lazy<List<Event>> CreateEventLazy()
	{
		return new Lazy<List<Event>>(delegate
		{
			_lock.Wait();
			try
			{
				return _events.ToList();
			}
			finally
			{
				_lock.Release();
			}
		});
	}

	private void WriteToFile(Event receivedEvent)
	{
		if (DateTime.Now.Day != _currentTime.Day)
		{
			try
			{
				_currentTime = DateTime.Now;
				IEnumerable<string> filesInDirectory = Directory.EnumerateFiles(EventLogFolder, "*.log");
				if (filesInDirectory.Skip(10).Any())
				{
					File.Delete(filesInDirectory.OrderBy((string f) => new FileInfo(f).LastWriteTimeUtc).First());
				}
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Could not delete last events");
			}
		}
		string eventLogFile = Path.Combine(EventLogFolder, CurrentLogName);
		try
		{
			if (!Directory.Exists(EventLogFolder))
			{
				Directory.CreateDirectory(EventLogFolder);
			}
            File.AppendAllLines(eventLogFile, [receivedEvent.ToString()]);
        }
        catch (Exception ex)
		{
			_logger.LogWarning(ex, "Could not write to event file " + eventLogFile);
		}
	}

	public Task StartAsync(CancellationToken cancellationToken)
	{
		base.Logger.LogInformation("Starting EventAggregatorService");
		base.Logger.LogInformation("Started EventAggregatorService");
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}
}
