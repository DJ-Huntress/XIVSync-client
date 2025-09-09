using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace XIVSync.WebAPI.Files;

internal class ThrottledStream : Stream
{
	private sealed class Bandwidth
	{
		private long _count;

		private int _lastSecondCheckpoint;

		private long _lastTransferredBytesCount;

		private int _speedRetrieveTime;

		public double Speed { get; private set; }

		public double AverageSpeed { get; private set; }

		public long BandwidthLimit { get; set; }

		public Bandwidth()
		{
			BandwidthLimit = long.MaxValue;
			Reset();
		}

		public void CalculateSpeed(long receivedBytesCount)
		{
			int elapsedTime = Environment.TickCount - _lastSecondCheckpoint + 1;
			receivedBytesCount = Interlocked.Add(ref _lastTransferredBytesCount, receivedBytesCount);
			double momentSpeed = (double)(receivedBytesCount * 1000) / (double)elapsedTime;
			if (1000 < elapsedTime)
			{
				Speed = momentSpeed;
				AverageSpeed = (AverageSpeed * (double)_count + Speed) / (double)(_count + 1);
				_count++;
				SecondCheckpoint();
			}
			if (momentSpeed >= (double)BandwidthLimit)
			{
				long expectedTime = receivedBytesCount * 1000 / BandwidthLimit;
				Interlocked.Add(ref _speedRetrieveTime, (int)expectedTime - elapsedTime);
			}
		}

		public int PopSpeedRetrieveTime()
		{
			return Interlocked.Exchange(ref _speedRetrieveTime, 0);
		}

		public void Reset()
		{
			SecondCheckpoint();
			_count = 0L;
			Speed = 0.0;
			AverageSpeed = 0.0;
		}

		private void SecondCheckpoint()
		{
			Interlocked.Exchange(ref _lastSecondCheckpoint, Environment.TickCount);
			Interlocked.Exchange(ref _lastTransferredBytesCount, 0L);
		}
	}

	private readonly Stream _baseStream;

	private long _bandwidthLimit;

	private Bandwidth _bandwidth;

	private CancellationTokenSource _bandwidthChangeTokenSource = new CancellationTokenSource();

	public static long Infinite => long.MaxValue;

	public long BandwidthLimit
	{
		get
		{
			return _bandwidthLimit;
		}
		set
		{
			if (_bandwidthLimit != value)
			{
				_bandwidthLimit = ((value <= 0) ? Infinite : value);
				if (_bandwidth == null)
				{
					_bandwidth = new Bandwidth();
				}
				_bandwidth.BandwidthLimit = _bandwidthLimit;
				_bandwidthChangeTokenSource.Cancel();
				_bandwidthChangeTokenSource.Dispose();
				_bandwidthChangeTokenSource = new CancellationTokenSource();
			}
		}
	}

	public override bool CanRead => _baseStream.CanRead;

	public override bool CanSeek => _baseStream.CanSeek;

	public override bool CanWrite => _baseStream.CanWrite;

	public override long Length => _baseStream.Length;

	public override long Position
	{
		get
		{
			return _baseStream.Position;
		}
		set
		{
			_baseStream.Position = value;
		}
	}

	public ThrottledStream(Stream baseStream, long bandwidthLimit)
	{
		if (bandwidthLimit < 0)
		{
			throw new ArgumentOutOfRangeException("bandwidthLimit", bandwidthLimit, "The maximum number of bytes per second can't be negative.");
		}
		_baseStream = baseStream ?? throw new ArgumentNullException("baseStream");
		BandwidthLimit = bandwidthLimit;
	}

	public override void Flush()
	{
		_baseStream.Flush();
	}

	public override long Seek(long offset, SeekOrigin origin)
	{
		return _baseStream.Seek(offset, origin);
	}

	public override void SetLength(long value)
	{
		_baseStream.SetLength(value);
	}

	public override int Read(byte[] buffer, int offset, int count)
	{
		Throttle(count).Wait();
		return _baseStream.Read(buffer, offset, count);
	}

	public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
	{
		await Throttle(count, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		return await _baseStream.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
	}

	public override void Write(byte[] buffer, int offset, int count)
	{
		Throttle(count).Wait();
		_baseStream.Write(buffer, offset, count);
	}

	public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
	{
		await Throttle(count, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		await _baseStream.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
	}

	public override void Close()
	{
		_baseStream.Close();
		base.Close();
	}

	private async Task Throttle(int transmissionVolume, CancellationToken token = default(CancellationToken))
	{
		if (BandwidthLimit > 0 && transmissionVolume > 0)
		{
			_bandwidth.CalculateSpeed(transmissionVolume);
			await Sleep(_bandwidth.PopSpeedRetrieveTime(), token).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	private async Task Sleep(int time, CancellationToken token = default(CancellationToken))
	{
		try
		{
			if (time > 0)
			{
				CancellationToken bandWidthtoken = _bandwidthChangeTokenSource.Token;
				CancellationToken linked = CancellationTokenSource.CreateLinkedTokenSource(token, bandWidthtoken).Token;
				await Task.Delay(time, linked).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		catch (TaskCanceledException)
		{
		}
	}

	public override string ToString()
	{
		return _baseStream?.ToString() ?? string.Empty;
	}
}
