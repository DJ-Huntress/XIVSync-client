using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace XIVSync.Interop;

public class UILoggingProvider : ILoggerProvider, IDisposable
{
	private class UILogger : ILogger
	{
		private readonly string _categoryName;

		private readonly ConcurrentQueue<LogEntry> _logEntries;

		private readonly int _maxEntries;

		public UILogger(string categoryName, ConcurrentQueue<LogEntry> logEntries, int maxEntries)
		{
			_categoryName = categoryName;
			_logEntries = logEntries;
			_maxEntries = maxEntries;
		}

		public IDisposable? BeginScope<TState>(TState state) where TState : notnull
		{
			return null;
		}

		public bool IsEnabled(LogLevel logLevel)
		{
			return logLevel != LogLevel.None;
		}

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
		{
			if (IsEnabled(logLevel))
			{
				string message = formatter(state, exception);
				LogEntry logEntry = new LogEntry
				{
					Timestamp = DateTime.UtcNow,
					LogLevel = logLevel,
					Category = _categoryName,
					Message = message,
					Exception = exception
				};
				_logEntries.Enqueue(logEntry);
				while (_logEntries.Count > _maxEntries)
				{
					_logEntries.TryDequeue(out LogEntry _);
				}
			}
		}
	}

	private readonly ConcurrentDictionary<string, UILogger> _loggers = new ConcurrentDictionary<string, UILogger>();

	private readonly ConcurrentQueue<LogEntry> _logEntries = new ConcurrentQueue<LogEntry>();

	private readonly int _maxEntries = 10000;

	public ILogger CreateLogger(string categoryName)
	{
		return _loggers.GetOrAdd(categoryName, (string name) => new UILogger(name, _logEntries, _maxEntries));
	}

	public IEnumerable<LogEntry> GetLogEntries()
	{
		return _logEntries.ToArray();
	}

	public IEnumerable<LogEntry> GetRecentLogs(int count = 1000)
	{
		return _logEntries.TakeLast(count).ToArray();
	}

	public void ClearLogs()
	{
		_logEntries.Clear();
	}

	public void Dispose()
	{
		_loggers.Clear();
		_logEntries.Clear();
	}
}
