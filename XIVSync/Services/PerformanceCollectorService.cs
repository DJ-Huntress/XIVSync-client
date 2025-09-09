using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XIVSync.MareConfiguration;
using XIVSync.Utils;

namespace XIVSync.Services;

public sealed class PerformanceCollectorService : IHostedService
{
	private const string _counterSplit = "=>";

	private readonly ILogger<PerformanceCollectorService> _logger;

	private readonly MareConfigService _mareConfigService;

	private readonly CancellationTokenSource _periodicLogPruneTaskCts = new CancellationTokenSource();

	public ConcurrentDictionary<string, RollingList<(TimeOnly, long)>> PerformanceCounters { get; } = new ConcurrentDictionary<string, RollingList<(TimeOnly, long)>>(StringComparer.Ordinal);

	public PerformanceCollectorService(ILogger<PerformanceCollectorService> logger, MareConfigService mareConfigService)
	{
		_logger = logger;
		_mareConfigService = mareConfigService;
	}

	public T LogPerformance<T>(object sender, MareInterpolatedStringHandler counterName, Func<T> func, int maxEntries = 10000)
	{
		if (!_mareConfigService.Current.LogPerformance)
		{
			return func();
		}
		string cn = sender.GetType().Name + "=>" + counterName.BuildMessage();
		if (!PerformanceCounters.TryGetValue(cn, out RollingList<(TimeOnly, long)> list))
		{
			RollingList<(TimeOnly, long)> rollingList = (PerformanceCounters[cn] = new RollingList<(TimeOnly, long)>(maxEntries));
			list = rollingList;
		}
		long dt = DateTime.UtcNow.Ticks;
		try
		{
			return func();
		}
		finally
		{
			long elapsed = DateTime.UtcNow.Ticks - dt;
			list.Add((TimeOnly.FromDateTime(DateTime.Now), elapsed));
		}
	}

	public void LogPerformance(object sender, MareInterpolatedStringHandler counterName, Action act, int maxEntries = 10000)
	{
		if (!_mareConfigService.Current.LogPerformance)
		{
			act();
			return;
		}
		string cn = sender.GetType().Name + "=>" + counterName.BuildMessage();
		if (!PerformanceCounters.TryGetValue(cn, out RollingList<(TimeOnly, long)> list))
		{
			RollingList<(TimeOnly, long)> rollingList = (PerformanceCounters[cn] = new RollingList<(TimeOnly, long)>(maxEntries));
			list = rollingList;
		}
		long dt = DateTime.UtcNow.Ticks;
		try
		{
			act();
		}
		finally
		{
			long elapsed = DateTime.UtcNow.Ticks - dt;
			list.Add((TimeOnly.FromDateTime(DateTime.Now), elapsed));
		}
	}

	public Task StartAsync(CancellationToken cancellationToken)
	{
		_logger.LogInformation("Starting PerformanceCollectorService");
		Task.Run((Func<Task?>)PeriodicLogPrune, _periodicLogPruneTaskCts.Token);
		_logger.LogInformation("Started PerformanceCollectorService");
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		_periodicLogPruneTaskCts.Cancel();
		_periodicLogPruneTaskCts.Dispose();
		return Task.CompletedTask;
	}

	internal void PrintPerformanceStats(int limitBySeconds = 0)
	{
		if (!_mareConfigService.Current.LogPerformance)
		{
			_logger.LogWarning("Performance counters are disabled");
		}
		StringBuilder sb = new StringBuilder();
		if (limitBySeconds > 0)
		{
			StringBuilder stringBuilder = sb;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(58, 1, stringBuilder);
			handler.AppendLiteral("Performance Metrics over the past ");
			handler.AppendFormatted(limitBySeconds);
			handler.AppendLiteral(" seconds of each counter");
			stringBuilder.AppendLine(ref handler);
		}
		else
		{
			sb.AppendLine("Performance metrics over total lifetime of each counter");
		}
		List<KeyValuePair<string, RollingList<(TimeOnly, long)>>> source = PerformanceCounters.ToList();
		int longestCounterName = source.OrderByDescending<KeyValuePair<string, RollingList<(TimeOnly, long)>>, int>((KeyValuePair<string, RollingList<(TimeOnly, long)>> d) => d.Key.Length).First().Key.Length + 2;
		sb.Append("-Last".PadRight(15, '-'));
		sb.Append('|');
		sb.Append("-Max".PadRight(15, '-'));
		sb.Append('|');
		sb.Append("-Average".PadRight(15, '-'));
		sb.Append('|');
		sb.Append("-Last Update".PadRight(15, '-'));
		sb.Append('|');
		sb.Append("-Entries".PadRight(10, '-'));
		sb.Append('|');
		sb.Append("-Counter Name".PadRight(longestCounterName, '-'));
		sb.AppendLine();
		List<KeyValuePair<string, RollingList<(TimeOnly, long)>>> list = source.OrderBy<KeyValuePair<string, RollingList<(TimeOnly, long)>>, string>((KeyValuePair<string, RollingList<(TimeOnly, long)>> k) => k.Key, StringComparer.OrdinalIgnoreCase).ToList();
		string previousCaller = list[0].Key.Split("=>", StringSplitOptions.RemoveEmptyEntries)[0];
		foreach (KeyValuePair<string, RollingList<(TimeOnly, long)>> entry in list)
		{
			string newCaller = entry.Key.Split("=>", StringSplitOptions.RemoveEmptyEntries)[0];
			if (!string.Equals(previousCaller, newCaller, StringComparison.Ordinal))
			{
				DrawSeparator(sb, longestCounterName);
			}
			List<(TimeOnly, long)> pastEntries = ((limitBySeconds > 0) ? entry.Value.Where(((TimeOnly, long) e) => e.Item1.AddMinutes((double)limitBySeconds / 60.0) >= TimeOnly.FromDateTime(DateTime.Now)).ToList() : entry.Value.ToList());
			if (pastEntries.Any())
			{
				(TimeOnly, long) tuple = pastEntries.LastOrDefault();
				sb.Append((" " + TimeSpan.FromTicks((tuple.Item1 == default(TimeOnly) && tuple.Item2 == 0L) ? 0 : pastEntries[pastEntries.Count - 1].Item2).TotalMilliseconds.ToString("0.00000", CultureInfo.InvariantCulture)).PadRight(15));
				sb.Append('|');
				sb.Append((" " + TimeSpan.FromTicks(pastEntries.Max(((TimeOnly, long) m) => m.Item2)).TotalMilliseconds.ToString("0.00000", CultureInfo.InvariantCulture)).PadRight(15));
				sb.Append('|');
				sb.Append((" " + TimeSpan.FromTicks((long)pastEntries.Average(((TimeOnly, long) m) => m.Item2)).TotalMilliseconds.ToString("0.00000", CultureInfo.InvariantCulture)).PadRight(15));
				sb.Append('|');
				tuple = pastEntries.LastOrDefault();
				sb.Append((" " + ((tuple.Item1 == default(TimeOnly) && tuple.Item2 == 0L) ? "-" : pastEntries[pastEntries.Count - 1].Item1.ToString("HH:mm:ss.ffff", CultureInfo.InvariantCulture))).PadRight(15, ' '));
				sb.Append('|');
				sb.Append((" " + pastEntries.Count).PadRight(10));
				sb.Append('|');
				sb.Append(' ').Append(entry.Key);
				sb.AppendLine();
			}
			previousCaller = newCaller;
		}
		DrawSeparator(sb, longestCounterName);
		_logger.LogInformation("{perf}", sb.ToString());
	}

	private static void DrawSeparator(StringBuilder sb, int longestCounterName)
	{
		sb.Append("".PadRight(15, '-'));
		sb.Append('+');
		sb.Append("".PadRight(15, '-'));
		sb.Append('+');
		sb.Append("".PadRight(15, '-'));
		sb.Append('+');
		sb.Append("".PadRight(15, '-'));
		sb.Append('+');
		sb.Append("".PadRight(10, '-'));
		sb.Append('+');
		sb.Append("".PadRight(longestCounterName, '-'));
		sb.AppendLine();
	}

	private async Task PeriodicLogPrune()
	{
		while (!_periodicLogPruneTaskCts.Token.IsCancellationRequested)
		{
			await Task.Delay(TimeSpan.FromMinutes(10L), _periodicLogPruneTaskCts.Token).ConfigureAwait(continueOnCapturedContext: false);
			foreach (KeyValuePair<string, RollingList<(TimeOnly, long)>> entries in PerformanceCounters.ToList())
			{
				try
				{
					if (entries.Value[entries.Value.Count - 1].Item1.AddMinutes(10.0) < TimeOnly.FromDateTime(DateTime.Now) && !PerformanceCounters.TryRemove(entries.Key, out RollingList<(TimeOnly, long)> _))
					{
						_logger.LogDebug("Could not remove performance counter {counter}", entries.Key);
					}
				}
				catch (Exception exception)
				{
					_logger.LogWarning(exception, "Error removing performance counter {counter}", entries.Key);
				}
			}
		}
	}
}
