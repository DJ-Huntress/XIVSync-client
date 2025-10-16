using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Lumina.Data.Files;
using Microsoft.Extensions.Logging;
using XIVSync.API.Data;
using XIVSync.API.Data.Enum;
using XIVSync.FileCache;
using XIVSync.Services.Mediator;
using XIVSync.UI;
using XIVSync.Utils;

namespace XIVSync.Services;

public sealed class CharacterAnalyzer : MediatorSubscriberBase, IDisposable
{
	internal sealed record FileDataEntry
	{
		public string Hash { get; init; }

		public string FileType { get; init; }

		public List<string> GamePaths { get; init; }

		public List<string> FilePaths { get; init; }

		public bool IsComputed
		{
			get
			{
				if (OriginalSize > 0)
				{
					return CompressedSize > 0;
				}
				return false;
			}
		}

		public long OriginalSize { get; private set; }

		public long CompressedSize { get; private set; }

		public long Triangles { get; private set; }

		public Lazy<string> Format;

		public FileDataEntry(string Hash, string FileType, List<string> GamePaths, List<string> FilePaths, long OriginalSize, long CompressedSize, long Triangles) : base()
		{
			this.Hash = Hash;
			this.FileType = FileType;
			this.GamePaths = GamePaths;
			this.FilePaths = FilePaths;
			this.OriginalSize = OriginalSize;
			this.CompressedSize = CompressedSize;
			this.Triangles = Triangles;
			Format = new Lazy<string>(delegate
			{
				if (FileType == "tex")
				{
					try
					{
						using FileStream input = new FileStream(FilePaths[0], FileMode.Open, FileAccess.Read, FileShare.Read);
						using BinaryReader binaryReader = new BinaryReader(input);
						binaryReader.BaseStream.Position = 4L;
						return ((TexFile.TextureFormat)binaryReader.ReadInt32()).ToString();
					}
					catch
					{
						return "Unknown";
					}
				}
				return string.Empty;
			});
	
		}

		public async Task ComputeSizes(FileCacheManager fileCacheManager, CancellationToken token)
		{
			(string, byte[]) compressedsize = await fileCacheManager.GetCompressedFileData(Hash, token).ConfigureAwait(continueOnCapturedContext: false);
			long normalSize = new FileInfo(FilePaths[0]).Length;
			foreach (FileCacheEntity item in fileCacheManager.GetAllFileCachesByHash(Hash, ignoreCacheEntries: true, validate: false))
			{
				item.Size = normalSize;
				item.CompressedSize = compressedsize.Item2.LongLength;
			}
			OriginalSize = normalSize;
			CompressedSize = compressedsize.Item2.LongLength;
		}

		[CompilerGenerated]
		public void Deconstruct(out string Hash, out string FileType, out List<string> GamePaths, out List<string> FilePaths, out long OriginalSize, out long CompressedSize, out long Triangles)
		{
			Hash = this.Hash;
			FileType = this.FileType;
			GamePaths = this.GamePaths;
			FilePaths = this.FilePaths;
			OriginalSize = this.OriginalSize;
			CompressedSize = this.CompressedSize;
			Triangles = this.Triangles;
		}
	}

	private readonly FileCacheManager _fileCacheManager;

	private readonly XivDataAnalyzer _xivDataAnalyzer;

	private CancellationTokenSource? _analysisCts;

	private CancellationTokenSource _baseAnalysisCts = new CancellationTokenSource();

	private string _lastDataHash = string.Empty;

	public int CurrentFile { get; internal set; }

	public bool IsAnalysisRunning => _analysisCts != null;

	public int TotalFiles { get; internal set; }

	internal Dictionary<ObjectKind, Dictionary<string, FileDataEntry>> LastAnalysis { get; } = new Dictionary<ObjectKind, Dictionary<string, FileDataEntry>>();


	public CharacterAnalyzer(ILogger<CharacterAnalyzer> logger, MareMediator mediator, FileCacheManager fileCacheManager, XivDataAnalyzer modelAnalyzer)
		: base(logger, mediator)
	{
		base.Mediator.Subscribe(this, delegate(CharacterDataCreatedMessage msg)
		{
			_baseAnalysisCts = _baseAnalysisCts.CancelRecreate();
			CancellationToken token = _baseAnalysisCts.Token;
			BaseAnalysis(msg.CharacterData, token);
		});
		_fileCacheManager = fileCacheManager;
		_xivDataAnalyzer = modelAnalyzer;
	}

	public void CancelAnalyze()
	{
		_analysisCts?.CancelDispose();
		_analysisCts = null;
	}

	public async Task ComputeAnalysis(bool print = true, bool recalculate = false)
	{
		base.Logger.LogDebug("=== Calculating Character Analysis ===");
		_analysisCts = _analysisCts?.CancelRecreate() ?? new CancellationTokenSource();
		CancellationToken cancelToken = _analysisCts.Token;
		List<FileDataEntry> allFiles = LastAnalysis.SelectMany<KeyValuePair<ObjectKind, Dictionary<string, FileDataEntry>>, FileDataEntry>((KeyValuePair<ObjectKind, Dictionary<string, FileDataEntry>> v) => v.Value.Select((KeyValuePair<string, FileDataEntry> d) => d.Value)).ToList();
		if (allFiles.Exists((FileDataEntry c) => !c.IsComputed || recalculate))
		{
			List<FileDataEntry> remaining = allFiles.Where((FileDataEntry c) => !c.IsComputed || recalculate).ToList();
			TotalFiles = remaining.Count;
			CurrentFile = 1;
			base.Logger.LogDebug("=== Computing {amount} remaining files ===", remaining.Count);
			base.Mediator.Publish(new HaltScanMessage("CharacterAnalyzer"));
			try
			{
				foreach (FileDataEntry file in remaining)
				{
					base.Logger.LogDebug("Computing file {file}", file.FilePaths[0]);
					await file.ComputeSizes(_fileCacheManager, cancelToken).ConfigureAwait(continueOnCapturedContext: false);
					CurrentFile++;
				}
				_fileCacheManager.WriteOutFullCsv();
			}
			catch (Exception ex)
			{
				base.Logger.LogWarning(ex, "Failed to analyze files");
			}
			finally
			{
				base.Mediator.Publish(new ResumeScanMessage("CharacterAnalyzer"));
			}
		}
		base.Mediator.Publish(new CharacterDataAnalyzedMessage());
		_analysisCts.CancelDispose();
		_analysisCts = null;
		if (print)
		{
			PrintAnalysis();
		}
	}

	public void Dispose()
	{
		_analysisCts?.CancelDispose();
		_baseAnalysisCts?.CancelDispose();
	}

	private async Task BaseAnalysis(CharacterData charaData, CancellationToken token)
	{
		if (string.Equals(charaData.DataHash.Value, _lastDataHash, StringComparison.Ordinal))
		{
			return;
		}
		LastAnalysis.Clear();
		foreach (KeyValuePair<ObjectKind, List<FileReplacementData>> obj in charaData.FileReplacements)
		{
			Dictionary<string, FileDataEntry> data = new Dictionary<string, FileDataEntry>(StringComparer.OrdinalIgnoreCase);
			foreach (FileReplacementData fileEntry in obj.Value)
			{
				token.ThrowIfCancellationRequested();
				List<FileCacheEntity> fileCacheEntries = _fileCacheManager.GetAllFileCachesByHash(fileEntry.Hash, ignoreCacheEntries: true, validate: false).ToList();
				if (fileCacheEntries.Count == 0)
				{
					continue;
				}
				string filePath = fileCacheEntries[0].ResolvedFilepath;
				FileInfo fi = new FileInfo(filePath);
				string ext = "unk?";
				try
				{
					string extension = fi.Extension;
					ext = extension.Substring(1, extension.Length - 1);
				}
				catch (Exception ex)
				{
					base.Logger.LogWarning(ex, "Could not identify extension for {path}", filePath);
				}
				long tris = await _xivDataAnalyzer.GetTrianglesByHash(fileEntry.Hash).ConfigureAwait(continueOnCapturedContext: false);
				foreach (FileCacheEntity entry in fileCacheEntries)
				{
					data[fileEntry.Hash] = new FileDataEntry(fileEntry.Hash, ext, fileEntry.GamePaths.ToList(), fileCacheEntries.Select((FileCacheEntity c) => c.ResolvedFilepath).Distinct().ToList(), (entry.Size > 0) ? entry.Size.Value : 0, (entry.CompressedSize > 0) ? entry.CompressedSize.Value : 0, tris);
				}
			}
			LastAnalysis[obj.Key] = data;
		}
		base.Mediator.Publish(new CharacterDataAnalyzedMessage());
		_lastDataHash = charaData.DataHash.Value;
	}

	private void PrintAnalysis()
	{
		if (LastAnalysis.Count == 0)
		{
			return;
		}
		foreach (KeyValuePair<ObjectKind, Dictionary<string, FileDataEntry>> kvp in LastAnalysis)
		{
			int fileCounter = 1;
			int totalFiles = kvp.Value.Count;
			base.Logger.LogInformation("=== Analysis for {obj} ===", kvp.Key);
			foreach (KeyValuePair<string, FileDataEntry> entry in kvp.Value.OrderBy<KeyValuePair<string, FileDataEntry>, string>((KeyValuePair<string, FileDataEntry> b) => b.Value.GamePaths.OrderBy<string, string>((string p) => p, StringComparer.Ordinal).First(), StringComparer.Ordinal))
			{
				base.Logger.LogInformation("File {x}/{y}: {hash}", fileCounter++, totalFiles, entry.Key);
				foreach (string path in entry.Value.GamePaths)
				{
					base.Logger.LogInformation("  Game Path: {path}", path);
				}
				if (entry.Value.FilePaths.Count > 1)
				{
					base.Logger.LogInformation("  Multiple fitting files detected for {key}", entry.Key);
				}
				foreach (string filePath in entry.Value.FilePaths)
				{
					base.Logger.LogInformation("  File Path: {path}", filePath);
				}
				base.Logger.LogInformation("  Size: {size}, Compressed: {compressed}", UiSharedService.ByteToString(entry.Value.OriginalSize), UiSharedService.ByteToString(entry.Value.CompressedSize));
			}
		}
		foreach (KeyValuePair<ObjectKind, Dictionary<string, FileDataEntry>> kvp in LastAnalysis)
		{
			base.Logger.LogInformation("=== Detailed summary by file type for {obj} ===", kvp.Key);
			foreach (IGrouping<string, FileDataEntry> entry in kvp.Value.Select((KeyValuePair<string, FileDataEntry> v) => v.Value).GroupBy<FileDataEntry, string>((FileDataEntry v) => v.FileType, StringComparer.Ordinal))
			{
				base.Logger.LogInformation("{ext} files: {count}, size extracted: {size}, size compressed: {sizeComp}", entry.Key, entry.Count(), UiSharedService.ByteToString(entry.Sum((FileDataEntry v) => v.OriginalSize)), UiSharedService.ByteToString(entry.Sum((FileDataEntry v) => v.CompressedSize)));
			}
			base.Logger.LogInformation("=== Total summary for {obj} ===", kvp.Key);
			base.Logger.LogInformation("Total files: {count}, size extracted: {size}, size compressed: {sizeComp}", kvp.Value.Count, UiSharedService.ByteToString(kvp.Value.Sum((KeyValuePair<string, FileDataEntry> v) => v.Value.OriginalSize)), UiSharedService.ByteToString(kvp.Value.Sum((KeyValuePair<string, FileDataEntry> v) => v.Value.CompressedSize)));
		}
		base.Logger.LogInformation("=== Total summary for all currently present objects ===");
		base.Logger.LogInformation("Total files: {count}, size extracted: {size}, size compressed: {sizeComp}", LastAnalysis.Values.Sum((Dictionary<string, FileDataEntry> v) => v.Values.Count), UiSharedService.ByteToString(LastAnalysis.Values.Sum((Dictionary<string, FileDataEntry> c) => c.Values.Sum((FileDataEntry v) => v.OriginalSize))), UiSharedService.ByteToString(LastAnalysis.Values.Sum((Dictionary<string, FileDataEntry> c) => c.Values.Sum((FileDataEntry v) => v.CompressedSize))));
		base.Logger.LogInformation("IMPORTANT NOTES:\n\r- For Mare up- and downloads only the compressed size is relevant.\n\r- An unusually high total files count beyond 200 and up will also increase your download time to others significantly.");
	}
}
