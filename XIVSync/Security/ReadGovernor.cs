using System;
using System.Collections.Concurrent;
using System.Threading;

namespace XIVSync.Security;

internal sealed class ReadGovernor
{
	private readonly TimeSpan _window = TimeSpan.FromSeconds(60L);

	private readonly int _filesPerMinute;

	private readonly long _bytesPerMinute;

	private readonly int _distinctThreshold;

	private readonly ConcurrentQueue<(DateTime ts, string path, long bytes)> _events = new ConcurrentQueue<(DateTime, string, long)>();

	private long _bytes;

	public ReadGovernor(int filesPerMinute = 120, long bytesPerMinute = 1610612736L, int distinctThreshold = 200)
	{
		_filesPerMinute = filesPerMinute;
		_bytesPerMinute = bytesPerMinute;
		_distinctThreshold = distinctThreshold;
	}

	public void Note(string path, long bytes)
	{
		DateTime now = DateTime.UtcNow;
		_events.Enqueue((now, path, bytes));
		Interlocked.Add(ref _bytes, bytes);
		Trim(now);
	}

	public ThrottleState State()
	{
		DateTime now = DateTime.UtcNow;
		Trim(now);
		int count = _events.Count;
		long bytes = Interlocked.Read(ref _bytes);
		if (count > _filesPerMinute || bytes > _bytesPerMinute)
		{
			return ThrottleState.Slow;
		}
		int seen = 0;
		ConcurrentDictionary<string, byte> set = new ConcurrentDictionary<string, byte>();
		foreach (var @event in _events)
		{
			if (set.TryAdd(@event.path, 0) && ++seen > _distinctThreshold)
			{
				return ThrottleState.SustainedSweep;
			}
		}
		return ThrottleState.Off;
	}

	public static void Apply(ThrottleState s)
	{
		switch (s)
		{
		case ThrottleState.Slow:
			Thread.Sleep(30);
			break;
		case ThrottleState.SustainedSweep:
			Thread.Sleep(120);
			break;
		}
	}

	private void Trim(DateTime now)
	{
		(DateTime ts, string path, long bytes) e;
		while (_events.TryPeek(out e) && now - e.ts > _window)
		{
			if (_events.TryDequeue(out (DateTime ts, string path, long bytes) d))
			{
				Interlocked.Add(ref _bytes, -d.bytes);
			}
		}
	}
}
