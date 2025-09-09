using System;
using System.Text;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging;
using XIVSync.MareConfiguration;

namespace XIVSync.Interop;

internal sealed class DalamudLogger : ILogger
{
	private readonly MareConfigService _mareConfigService;

	private readonly string _name;

	private readonly IPluginLog _pluginLog;

	private readonly bool _hasModifiedGameFiles;

	public DalamudLogger(string name, MareConfigService mareConfigService, IPluginLog pluginLog, bool hasModifiedGameFiles)
	{
		_name = name;
		_mareConfigService = mareConfigService;
		_pluginLog = pluginLog;
		_hasModifiedGameFiles = hasModifiedGameFiles;
	}

	public IDisposable BeginScope<TState>(TState state)
	{
		return null;
	}

	public bool IsEnabled(LogLevel logLevel)
	{
		return _mareConfigService.Current.LogLevel <= logLevel;
	}

	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
	{
		if (!IsEnabled(logLevel))
		{
			return;
		}
		string unsupported = (_hasModifiedGameFiles ? "[UNSUPPORTED]" : string.Empty);
		if (logLevel <= LogLevel.Information)
		{
			_pluginLog.Information($"{unsupported}[{_name}]{{{logLevel}}} {state}{(_hasModifiedGameFiles ? "." : string.Empty)}");
			return;
		}
		StringBuilder sb = new StringBuilder();
		StringBuilder stringBuilder = sb;
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(6, 6, stringBuilder);
		handler.AppendFormatted(unsupported);
		handler.AppendLiteral("[");
		handler.AppendFormatted(_name);
		handler.AppendLiteral("]{");
		handler.AppendFormatted((int)logLevel);
		handler.AppendLiteral("} ");
		handler.AppendFormatted(state);
		handler.AppendFormatted(_hasModifiedGameFiles ? "." : string.Empty);
		handler.AppendLiteral(" ");
		handler.AppendFormatted(exception?.Message);
		stringBuilder2.Append(ref handler);
		if (!string.IsNullOrWhiteSpace(exception?.StackTrace))
		{
			sb.AppendLine(exception?.StackTrace);
		}
		for (Exception innerException = exception?.InnerException; innerException != null; innerException = innerException.InnerException)
		{
			stringBuilder = sb;
			StringBuilder stringBuilder3 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(17, 2, stringBuilder);
			handler.AppendLiteral("InnerException ");
			handler.AppendFormatted(innerException);
			handler.AppendLiteral(": ");
			handler.AppendFormatted(innerException.Message);
			stringBuilder3.AppendLine(ref handler);
			sb.AppendLine(innerException.StackTrace);
		}
		switch (logLevel)
		{
		case LogLevel.Warning:
			_pluginLog.Warning(sb.ToString());
			break;
		case LogLevel.Error:
			_pluginLog.Error(sb.ToString());
			break;
		default:
			_pluginLog.Fatal(sb.ToString());
			break;
		}
	}
}
