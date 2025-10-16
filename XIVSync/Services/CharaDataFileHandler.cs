using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.SubKinds;
using K4os.Compression.LZ4.Legacy;
using Microsoft.Extensions.Logging;
using XIVSync.API.Data;
using XIVSync.API.Data.Enum;
using XIVSync.API.Dto.CharaData;
using XIVSync.FileCache;
using XIVSync.PlayerData.Data;
using XIVSync.PlayerData.Factories;
using XIVSync.PlayerData.Handlers;
using XIVSync.Services.CharaData;
using XIVSync.Services.CharaData.Models;
using XIVSync.Utils;
using XIVSync.WebAPI.Files;

namespace XIVSync.Services;

public sealed class CharaDataFileHandler : IDisposable
{
	private readonly DalamudUtilService _dalamudUtilService;

	private readonly FileCacheManager _fileCacheManager;

	private readonly FileDownloadManager _fileDownloadManager;

	private readonly FileUploadManager _fileUploadManager;

	private readonly GameObjectHandlerFactory _gameObjectHandlerFactory;

	private readonly ILogger<CharaDataFileHandler> _logger;

	private readonly MareCharaFileDataFactory _mareCharaFileDataFactory;

	private readonly PlayerDataFactory _playerDataFactory;

	private int _globalFileCounter;

	public CharaDataFileHandler(ILogger<CharaDataFileHandler> logger, FileDownloadManagerFactory fileDownloadManagerFactory, FileUploadManager fileUploadManager, FileCacheManager fileCacheManager, DalamudUtilService dalamudUtilService, GameObjectHandlerFactory gameObjectHandlerFactory, PlayerDataFactory playerDataFactory)
	{
		_fileDownloadManager = fileDownloadManagerFactory.Create();
		_logger = logger;
		_fileUploadManager = fileUploadManager;
		_fileCacheManager = fileCacheManager;
		_dalamudUtilService = dalamudUtilService;
		_gameObjectHandlerFactory = gameObjectHandlerFactory;
		_playerDataFactory = playerDataFactory;
		_mareCharaFileDataFactory = new MareCharaFileDataFactory(fileCacheManager);
	}

	public void ComputeMissingFiles(CharaDataDownloadDto charaDataDownloadDto, out Dictionary<string, string> modPaths, out List<FileReplacementData> missingFiles)
	{
		modPaths = new Dictionary<string, string>();
		missingFiles = new List<FileReplacementData>();
		foreach (GamePathEntry file in charaDataDownloadDto.FileGamePaths)
		{
			FileCacheEntity localCacheFile = _fileCacheManager.GetFileCacheByHash(file.HashOrFileSwap);
			if (localCacheFile == null)
			{
				FileReplacementData existingFile = missingFiles.Find((FileReplacementData f) => string.Equals(f.Hash, file.HashOrFileSwap, StringComparison.Ordinal));
				if (existingFile == null)
				{
					missingFiles.Add(new FileReplacementData
					{
						Hash = file.HashOrFileSwap,
						GamePaths = new string[1] { file.GamePath }
					});
				}
				else
				{
                    existingFile.GamePaths = existingFile.GamePaths.Concat([file.GamePath]).ToArray();
                }
            }
			else
			{
				modPaths[file.GamePath] = localCacheFile.ResolvedFilepath;
			}
		}
		foreach (GamePathEntry swap in charaDataDownloadDto.FileSwaps)
		{
			modPaths[swap.GamePath] = swap.HashOrFileSwap;
		}
	}

	public async Task<XIVSync.API.Data.CharacterData?> CreatePlayerData()
	{
		IPlayerCharacter chara = await _dalamudUtilService.GetPlayerCharacterAsync().ConfigureAwait(continueOnCapturedContext: false);
		if (_dalamudUtilService.IsInGpose)
		{
			chara = (IPlayerCharacter)(await _dalamudUtilService.GetGposeCharacterFromObjectTableByNameAsync(chara.Name.TextValue, _dalamudUtilService.IsInGpose).ConfigureAwait(continueOnCapturedContext: false));
		}
		if (chara == null)
		{
			return null;
		}
		using GameObjectHandler tempHandler = await _gameObjectHandlerFactory.Create(ObjectKind.Player, () => _dalamudUtilService.GetCharacterFromObjectTableByIndex(chara.ObjectIndex)?.Address ?? IntPtr.Zero).ConfigureAwait(continueOnCapturedContext: false);
		XIVSync.PlayerData.Data.CharacterData newCdata = new XIVSync.PlayerData.Data.CharacterData();
		newCdata.SetFragment(ObjectKind.Player, await _playerDataFactory.BuildCharacterData(tempHandler, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false));
		if (newCdata.FileReplacements.TryGetValue(ObjectKind.Player, out HashSet<FileReplacement> playerData) && playerData != null)
		{
			foreach (HashSet<string> item in playerData.Select((FileReplacement g) => g.GamePaths))
			{
				item.RemoveWhere((string g) => g.EndsWith(".pap", StringComparison.OrdinalIgnoreCase) || g.EndsWith(".tmb", StringComparison.OrdinalIgnoreCase) || g.EndsWith(".scd", StringComparison.OrdinalIgnoreCase) || (g.EndsWith(".avfx", StringComparison.OrdinalIgnoreCase) && !g.Contains("/weapon/", StringComparison.OrdinalIgnoreCase) && !g.Contains("/equipment/", StringComparison.OrdinalIgnoreCase)) || (g.EndsWith(".atex", StringComparison.OrdinalIgnoreCase) && !g.Contains("/weapon/", StringComparison.OrdinalIgnoreCase) && !g.Contains("/equipment/", StringComparison.OrdinalIgnoreCase)));
			}
			playerData.RemoveWhere((FileReplacement g) => g.GamePaths.Count == 0);
		}
		return newCdata.ToAPI();
	}

	public void Dispose()
	{
		_fileDownloadManager.Dispose();
	}

	public async Task DownloadFilesAsync(GameObjectHandler tempHandler, List<FileReplacementData> missingFiles, Dictionary<string, string> modPaths, CancellationToken token)
	{
		await _fileDownloadManager.InitiateDownloadList(tempHandler, missingFiles, token).ConfigureAwait(continueOnCapturedContext: false);
		await _fileDownloadManager.DownloadFiles(tempHandler, missingFiles, token).ConfigureAwait(continueOnCapturedContext: false);
		token.ThrowIfCancellationRequested();
		foreach (var file in missingFiles.SelectMany((FileReplacementData m) => m.GamePaths, (FileReplacementData FileEntry, string GamePath) => (Hash: FileEntry.Hash, GamePath: GamePath)))
		{
			string localFile = _fileCacheManager.GetFileCacheByHash(file.Hash)?.ResolvedFilepath;
			if (localFile == null)
			{
				throw new FileNotFoundException("File not found locally.");
			}
			modPaths[file.GamePath] = localFile;
		}
	}

	public Task<(MareCharaFileHeader loadedCharaFile, long expectedLength)> LoadCharaFileHeader(string filePath)
	{
		try
		{
			using FileStream unwrapped = File.OpenRead(filePath);
			using LZ4Stream lz4Stream = new LZ4Stream(unwrapped, LZ4StreamMode.Decompress, LZ4StreamFlags.HighCompression);
			using BinaryReader reader = new BinaryReader(lz4Stream);
			MareCharaFileHeader loadedCharaFile = MareCharaFileHeader.FromBinaryReader(filePath, reader);
			_logger.LogInformation("Read Mare Chara File");
			_logger.LogInformation("Version: {ver}", ((int?)loadedCharaFile?.Version) ?? (-1));
			long expectedLength = 0L;
			if (loadedCharaFile != null)
			{
				_logger.LogTrace("Data");
				foreach (MareCharaFileData.FileSwap item in loadedCharaFile.CharaFileData.FileSwaps)
				{
					foreach (string gamePath in item.GamePaths)
					{
						_logger.LogTrace("Swap: {gamePath} => {fileSwapPath}", gamePath, item.FileSwapPath);
					}
				}
				int itemNr = 0;
				foreach (MareCharaFileData.FileData item in loadedCharaFile.CharaFileData.Files)
				{
					itemNr++;
					expectedLength += item.Length;
					foreach (string gamePath in item.GamePaths)
					{
						_logger.LogTrace("File {itemNr}: {gamePath} = {len}", itemNr, gamePath, item.Length.ToByteString());
					}
				}
				_logger.LogInformation("Expected length: {expected}", expectedLength.ToByteString());
				return Task.FromResult((loadedCharaFile, expectedLength));
			}
			throw new InvalidOperationException("MCDF Header was null");
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Could not parse MCDF header of file {file}", filePath);
			throw;
		}
	}

	public Dictionary<string, string> McdfExtractFiles(MareCharaFileHeader? charaFileHeader, long expectedLength, List<string> extractedFiles)
	{
		if (charaFileHeader == null)
		{
			return new Dictionary<string, string>();
		}
		using LZ4Stream lz4Stream = new LZ4Stream(File.OpenRead(charaFileHeader.FilePath), LZ4StreamMode.Decompress, LZ4StreamFlags.HighCompression);
		using BinaryReader reader = new BinaryReader(lz4Stream);
		MareCharaFileHeader.AdvanceReaderToData(reader);
		long totalRead = 0L;
		Dictionary<string, string> gamePathToFilePath = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (MareCharaFileData.FileData fileData in charaFileHeader.CharaFileData.Files)
		{
			string fileName = Path.Combine(_fileCacheManager.CacheFolder, "mare_" + _globalFileCounter++ + ".tmp");
			extractedFiles.Add(fileName);
			int length = fileData.Length;
			int bufferSize = length;
			using FileStream fs = File.OpenWrite(fileName);
			using BinaryWriter wr = new BinaryWriter(fs);
			_logger.LogTrace("Reading {length} of {fileName}", length.ToByteString(), fileName);
			byte[] buffer = reader.ReadBytes(bufferSize);
			wr.Write(buffer);
			wr.Flush();
			wr.Close();
			if (buffer.Length == 0)
			{
				throw new EndOfStreamException("Unexpected EOF");
			}
			foreach (string path in fileData.GamePaths)
			{
				gamePathToFilePath[path] = fileName;
				_logger.LogTrace("{path} => {fileName} [{hash}]", path, fileName, fileData.Hash);
			}
			totalRead += length;
			_logger.LogTrace("Read {read}/{expected} bytes", totalRead.ToByteString(), expectedLength.ToByteString());
		}
		return gamePathToFilePath;
	}

	public async Task UpdateCharaDataAsync(CharaDataExtendedUpdateDto updateDto)
	{
		XIVSync.API.Data.CharacterData data = await CreatePlayerData().ConfigureAwait(continueOnCapturedContext: false);
		if (data == null)
		{
			return;
		}
		if (!data.GlamourerData.TryGetValue(ObjectKind.Player, out string playerDataString))
		{
			updateDto.GlamourerData = null;
		}
		else
		{
			updateDto.GlamourerData = playerDataString;
		}
		if (!data.CustomizePlusData.TryGetValue(ObjectKind.Player, out string customizeDataString))
		{
			updateDto.CustomizeData = null;
		}
		else
		{
			updateDto.CustomizeData = customizeDataString;
		}
		updateDto.ManipulationData = data.ManipulationData;
		if (!data.FileReplacements.TryGetValue(ObjectKind.Player, out List<FileReplacementData> fileReplacements))
		{
			updateDto.FileGamePaths = new List<GamePathEntry>();
			updateDto.FileSwaps = new List<GamePathEntry>();
			return;
		}
		updateDto.FileGamePaths = fileReplacements.Where((FileReplacementData u) => string.IsNullOrEmpty(u.FileSwapPath)).SelectMany((FileReplacementData u) => u.GamePaths, (FileReplacementData file, string path) => new GamePathEntry(file.Hash, path)).ToList();
		updateDto.FileSwaps = fileReplacements.Where((FileReplacementData u) => !string.IsNullOrEmpty(u.FileSwapPath)).SelectMany((FileReplacementData u) => u.GamePaths, (FileReplacementData file, string path) => new GamePathEntry(file.FileSwapPath, path)).ToList();
	}

	internal async Task SaveCharaFileAsync(string description, string filePath)
	{
		string tempFilePath = filePath + ".tmp";
		try
		{
			XIVSync.API.Data.CharacterData data = await CreatePlayerData().ConfigureAwait(continueOnCapturedContext: false);
			if (data == null)
			{
				return;
			}
			MareCharaFileData mareCharaFileData = _mareCharaFileDataFactory.Create(description, data);
			MareCharaFileHeader output = new MareCharaFileHeader(MareCharaFileHeader.CurrentVersion, mareCharaFileData);
			using FileStream fs = new FileStream(tempFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
			using LZ4Stream lz4 = new LZ4Stream(fs, LZ4StreamMode.Compress, LZ4StreamFlags.HighCompression);
			using BinaryWriter writer = new BinaryWriter(lz4);
			output.WriteToStream(writer);
			foreach (MareCharaFileData.FileData item in output.CharaFileData.Files)
			{
				FileCacheEntity file = _fileCacheManager.GetFileCacheByHash(item.Hash);
				_logger.LogDebug("Saving to MCDF: {hash}:{file}", item.Hash, file.ResolvedFilepath);
				_logger.LogDebug("\tAssociated GamePaths:");
				foreach (string path in item.GamePaths)
				{
					_logger.LogDebug("\t{path}", path);
				}
				FileStream fsRead = File.OpenRead(file.ResolvedFilepath);
                await using (fsRead.ConfigureAwait(false))
                {
                    using var br = new BinaryReader(fsRead);
                    byte[] buffer = new byte[item.Length];
                    br.Read(buffer, 0, item.Length);
                    writer.Write(buffer);
                }
            }
			writer.Flush();
			await lz4.FlushAsync().ConfigureAwait(continueOnCapturedContext: false);
			await fs.FlushAsync().ConfigureAwait(continueOnCapturedContext: false);
			fs.Close();
			File.Move(tempFilePath, filePath, overwrite: true);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failure Saving Mare Chara File, deleting output");
			File.Delete(tempFilePath);
		}
	}

	internal async Task<List<string>> UploadFiles(List<string> fileList, ValueProgress<string> uploadProgress, CancellationToken token)
	{
		return await _fileUploadManager.UploadFiles(fileList, uploadProgress, token).ConfigureAwait(continueOnCapturedContext: false);
	}
}
