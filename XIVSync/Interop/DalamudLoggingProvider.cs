using System;
using System.Collections.Concurrent;
using System.Linq;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging;
using XIVSync.MareConfiguration;

namespace XIVSync.Interop;

[ProviderAlias("Dalamud")]
public sealed class DalamudLoggingProvider : ILoggerProvider, IDisposable
{
	private readonly ConcurrentDictionary<string, DalamudLogger> _loggers = new ConcurrentDictionary<string, DalamudLogger>(StringComparer.OrdinalIgnoreCase);

	private readonly MareConfigService _mareConfigService;

	private readonly IPluginLog _pluginLog;

	private readonly bool _hasModifiedGameFiles;

	public DalamudLoggingProvider(MareConfigService mareConfigService, IPluginLog pluginLog, bool hasModifiedGameFiles)
	{
		_mareConfigService = mareConfigService;
		_pluginLog = pluginLog;
		_hasModifiedGameFiles = hasModifiedGameFiles;
	}

	public ILogger CreateLogger(string categoryName)
	{
		string catName = categoryName.Split(".", StringSplitOptions.RemoveEmptyEntries)[^1];
		catName = ((catName.Length <= 15) ? (string.Join("", from _ in Enumerable.Range(0, 15 - catName.Length)
			select " ") + catName) : (string.Join("", catName.Take(6)) + "..." + string.Join("", catName.TakeLast(6))));
		return _loggers.GetOrAdd(catName, (string name) => new DalamudLogger(name, _mareConfigService, _pluginLog, _hasModifiedGameFiles));
	}

	public void Dispose()
	{
		_loggers.Clear();
		GC.SuppressFinalize(this);
	}
}
