using System;
using Microsoft.Extensions.Logging;

namespace XIVSync.Interop;

public class LogEntry
{
	public DateTime Timestamp { get; set; }

	public LogLevel LogLevel { get; set; }

	public LogLevel Level => LogLevel;

	public string Category { get; set; } = string.Empty;


	public string Message { get; set; } = string.Empty;


	public Exception? Exception { get; set; }
}
