using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace XIVSync.WebAPI.Files.Models;

public class ProgressableStreamContent : StreamContent
{
	private const int _defaultBufferSize = 4096;

	private readonly int _bufferSize;

	private readonly IProgress<UploadProgress>? _progress;

	private readonly Stream _streamToWrite;

	private bool _contentConsumed;

	public ProgressableStreamContent(Stream streamToWrite, IProgress<UploadProgress>? downloader)
		: this(streamToWrite, 4096, downloader)
	{
	}

	public ProgressableStreamContent(Stream streamToWrite, int bufferSize, IProgress<UploadProgress>? progress)
		: base(streamToWrite, bufferSize)
	{
		if (streamToWrite == null)
		{
			throw new ArgumentNullException("streamToWrite");
		}
		if (bufferSize <= 0)
		{
			throw new ArgumentOutOfRangeException("bufferSize");
		}
		_streamToWrite = streamToWrite;
		_bufferSize = bufferSize;
		_progress = progress;
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_streamToWrite.Dispose();
		}
		base.Dispose(disposing);
	}

	protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
	{
		PrepareContent();
		byte[] buffer = new byte[_bufferSize];
		long size = _streamToWrite.Length;
		int uploaded = 0;
		using (_streamToWrite)
		{
			while (true)
			{
				int length = await _streamToWrite.ReadAsync(buffer).ConfigureAwait(continueOnCapturedContext: false);
				if (length > 0)
				{
					uploaded += length;
					_progress?.Report(new UploadProgress(uploaded, size));
					await stream.WriteAsync(buffer.AsMemory(0, length)).ConfigureAwait(continueOnCapturedContext: false);
					continue;
				}
				break;
			}
		}
	}

	protected override bool TryComputeLength(out long length)
	{
		length = _streamToWrite.Length;
		return true;
	}

	private void PrepareContent()
	{
		if (_contentConsumed)
		{
			if (!_streamToWrite.CanSeek)
			{
				throw new InvalidOperationException("The stream has already been read.");
			}
			_streamToWrite.Position = 0L;
		}
		_contentConsumed = true;
	}
}
