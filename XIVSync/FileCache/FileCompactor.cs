using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using XIVSync.MareConfiguration;
using XIVSync.Services;

namespace XIVSync.FileCache;

public sealed class FileCompactor
{
	private enum CompressionAlgorithm
	{
		NO_COMPRESSION = -2,
		LZNT1,
		XPRESS4K,
		LZX,
		XPRESS8K,
		XPRESS16K
	}

	private struct WOF_FILE_COMPRESSION_INFO_V1
	{
		public CompressionAlgorithm Algorithm;

		public ulong Flags;
	}

	public const uint FSCTL_DELETE_EXTERNAL_BACKING = 590612u;

	public const ulong WOF_PROVIDER_FILE = 2uL;

	private readonly Dictionary<string, int> _clusterSizes;

	private readonly WOF_FILE_COMPRESSION_INFO_V1 _efInfo;

	private readonly ILogger<FileCompactor> _logger;

	private readonly MareConfigService _mareConfigService;

	private readonly DalamudUtilService _dalamudUtilService;

	public bool MassCompactRunning { get; private set; }

	public string Progress { get; private set; } = string.Empty;


	public FileCompactor(ILogger<FileCompactor> logger, MareConfigService mareConfigService, DalamudUtilService dalamudUtilService)
	{
		_clusterSizes = new Dictionary<string, int>(StringComparer.Ordinal);
		_logger = logger;
		_mareConfigService = mareConfigService;
		_dalamudUtilService = dalamudUtilService;
		_efInfo = new WOF_FILE_COMPRESSION_INFO_V1
		{
			Algorithm = CompressionAlgorithm.XPRESS8K,
			Flags = 0uL
		};
	}

	public void CompactStorage(bool compress)
	{
		MassCompactRunning = true;
		int currentFile = 1;
		List<string> list = Directory.EnumerateFiles(_mareConfigService.Current.CacheFolder).ToList();
		int allFilesCount = list.Count;
		foreach (string file in list)
		{
			Progress = $"{currentFile}/{allFilesCount}";
			if (compress)
			{
				CompactFile(file);
			}
			else
			{
				DecompressFile(file);
			}
			currentFile++;
		}
		MassCompactRunning = false;
	}

	public long GetFileSizeOnDisk(FileInfo fileInfo, bool? isNTFS = null)
	{
		bool ntfs = isNTFS ?? string.Equals(new DriveInfo(fileInfo.Directory.Root.FullName).DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase);
		if (_dalamudUtilService.IsWine || !ntfs)
		{
			return fileInfo.Length;
		}
		int clusterSize = GetClusterSize(fileInfo);
		if (clusterSize == -1)
		{
			return fileInfo.Length;
		}
		uint hosize;
		uint losize = GetCompressedFileSizeW(fileInfo.FullName, out hosize);
		return ((long)(((ulong)hosize << 32) | losize) + (long)clusterSize - 1) / clusterSize * clusterSize;
	}

	public async Task WriteAllBytesAsync(string filePath, byte[] decompressedFile, CancellationToken token)
	{
		await File.WriteAllBytesAsync(filePath, decompressedFile, token).ConfigureAwait(continueOnCapturedContext: false);
		if (!_dalamudUtilService.IsWine && _mareConfigService.Current.UseCompactor)
		{
			CompactFile(filePath);
		}
	}

	[DllImport("kernel32.dll")]
	private static extern int DeviceIoControl(nint hDevice, uint dwIoControlCode, nint lpInBuffer, uint nInBufferSize, nint lpOutBuffer, uint nOutBufferSize, out nint lpBytesReturned, out nint lpOverlapped);

	[DllImport("kernel32.dll")]
	private static extern uint GetCompressedFileSizeW([In][MarshalAs(UnmanagedType.LPWStr)] string lpFileName, [MarshalAs(UnmanagedType.U4)] out uint lpFileSizeHigh);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern int GetDiskFreeSpaceW([In][MarshalAs(UnmanagedType.LPWStr)] string lpRootPathName, out uint lpSectorsPerCluster, out uint lpBytesPerSector, out uint lpNumberOfFreeClusters, out uint lpTotalNumberOfClusters);

	[DllImport("WoFUtil.dll")]
	private static extern int WofIsExternalFile([MarshalAs(UnmanagedType.LPWStr)] string Filepath, out int IsExternalFile, out uint Provider, out WOF_FILE_COMPRESSION_INFO_V1 Info, ref uint BufferLength);

	[DllImport("WofUtil.dll")]
	private static extern int WofSetFileDataLocation(nint FileHandle, ulong Provider, nint ExternalFileInfo, ulong Length);

	private void CompactFile(string filePath)
	{
		if (!string.Equals(new DriveInfo(new FileInfo(filePath).Directory.Root.FullName).DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
		{
			_logger.LogWarning("Drive for file {file} is not NTFS", filePath);
			return;
		}
		FileInfo fi = new FileInfo(filePath);
		long oldSize = fi.Length;
		int clusterSize = GetClusterSize(fi);
		if (oldSize < Math.Max(clusterSize, 8192))
		{
			_logger.LogDebug("File {file} is smaller than cluster size ({size}), ignoring", filePath, clusterSize);
		}
		else if (!IsCompactedFile(filePath))
		{
			_logger.LogDebug("Compacting file to XPRESS8K: {file}", filePath);
			WOFCompressFile(filePath);
			long newSize = GetFileSizeOnDisk(fi);
			_logger.LogDebug("Compressed {file} from {orig}b to {comp}b", filePath, oldSize, newSize);
		}
		else
		{
			_logger.LogDebug("File {file} already compressed", filePath);
		}
	}

	private void DecompressFile(string path)
	{
		_logger.LogDebug("Removing compression from {file}", path);
		try
		{
			using FileStream fs = new FileStream(path, FileMode.Open);
			DeviceIoControl(fs.SafeFileHandle.DangerousGetHandle(), 590612u, IntPtr.Zero, 0u, IntPtr.Zero, 0u, out var _, out var _);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Error decompressing file {path}", path);
		}
	}

	private int GetClusterSize(FileInfo fi)
	{
		if (!fi.Exists)
		{
			return -1;
		}
		string root = fi.Directory?.Root.FullName.ToLower() ?? string.Empty;
		if (string.IsNullOrEmpty(root))
		{
			return -1;
		}
		if (_clusterSizes.TryGetValue(root, out var value))
		{
			return value;
		}
		_logger.LogDebug("Getting Cluster Size for {path}, root {root}", fi.FullName, root);
		if (GetDiskFreeSpaceW(root, out var sectorsPerCluster, out var bytesPerSector, out var _, out var _) == 0)
		{
			return -1;
		}
		_clusterSizes[root] = (int)(sectorsPerCluster * bytesPerSector);
		_logger.LogDebug("Determined Cluster Size for root {root}: {cluster}", root, _clusterSizes[root]);
		return _clusterSizes[root];
	}

	private static bool IsCompactedFile(string filePath)
	{
		uint buf = 8u;
		WofIsExternalFile(filePath, out var isExtFile, out var _, out var info, ref buf);
		if (isExtFile == 0)
		{
			return false;
		}
		return info.Algorithm == CompressionAlgorithm.XPRESS8K;
	}

	private void WOFCompressFile(string path)
	{
		nint efInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf(_efInfo));
		Marshal.StructureToPtr(_efInfo, efInfoPtr, fDeleteOld: true);
		ulong length = (ulong)Marshal.SizeOf(_efInfo);
		try
		{
			using FileStream fs = new FileStream(path, FileMode.Open);
			nint hFile = fs.SafeFileHandle.DangerousGetHandle();
			if (fs.SafeFileHandle.IsInvalid)
			{
				_logger.LogWarning("Invalid file handle to {file}", path);
				return;
			}
			int ret = WofSetFileDataLocation(hFile, 2uL, efInfoPtr, length);
			if (ret != 0 && ret != -2147024552)
			{
				_logger.LogWarning("Failed to compact {file}: {ret}", path, ret.ToString("X"));
			}
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Error compacting file {path}", path);
		}
		finally
		{
			Marshal.FreeHGlobal(efInfoPtr);
		}
	}
}
