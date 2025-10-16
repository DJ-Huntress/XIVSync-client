using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using XIVSync.API.Data;
using XIVSync.API.Data.Enum;
using XIVSync.FileCache;

namespace XIVSync.Services.CharaData.Models;

public record MareCharaFileData
{
	public record FileSwap(IEnumerable<string> GamePaths, string FileSwapPath);

	public record FileData(IEnumerable<string> GamePaths, int Length, string Hash);

	public string Description { get; set; } = string.Empty;


	public string GlamourerData { get; set; } = string.Empty;


	public string CustomizePlusData { get; set; } = string.Empty;


	public string ManipulationData { get; set; } = string.Empty;


	public List<FileData> Files { get; set; } = new List<FileData>();


	public List<FileSwap> FileSwaps { get; set; } = new List<FileSwap>();


	public MareCharaFileData()
	{
	}

	public MareCharaFileData(FileCacheManager manager, string description, CharacterData dto)
	{
		Description = description;
		if (dto.GlamourerData.TryGetValue(ObjectKind.Player, out string glamourerData))
		{
			GlamourerData = glamourerData;
		}
		dto.CustomizePlusData.TryGetValue(ObjectKind.Player, out string customizePlusData);
		CustomizePlusData = customizePlusData ?? string.Empty;
		ManipulationData = dto.ManipulationData;
		if (!dto.FileReplacements.TryGetValue(ObjectKind.Player, out List<FileReplacementData> fileReplacements))
		{
			return;
		}
		foreach (IGrouping<string, FileReplacementData> file in fileReplacements.GroupBy<FileReplacementData, string>((FileReplacementData f) => f.Hash, StringComparer.OrdinalIgnoreCase))
		{
			if (string.IsNullOrEmpty(file.Key))
			{
				foreach (FileReplacementData item in file)
				{
					FileSwaps.Add(new FileSwap(item.GamePaths, item.FileSwapPath));
				}
				continue;
			}
			string filePath = manager.GetFileCacheByHash(file.First().Hash)?.ResolvedFilepath;
			if (filePath != null)
			{
				Files.Add(new FileData(file.SelectMany((FileReplacementData f) => f.GamePaths), (int)new FileInfo(filePath).Length, file.First().Hash));
			}
		}
	}

	public byte[] ToByteArray()
	{
		return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this));
	}

	public static MareCharaFileData FromByteArray(byte[] data)
	{
		return JsonSerializer.Deserialize<MareCharaFileData>(Encoding.UTF8.GetString(data));
	}
}
