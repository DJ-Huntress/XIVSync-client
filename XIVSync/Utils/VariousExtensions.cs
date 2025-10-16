using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Microsoft.Extensions.Logging;
using XIVSync.API.Data;
using XIVSync.API.Data.Enum;
using XIVSync.PlayerData.Data;
using XIVSync.PlayerData.Handlers;
using XIVSync.PlayerData.Pairs;

namespace XIVSync.Utils;

public static class VariousExtensions
{
	public static string ToByteString(this int bytes, bool addSuffix = true)
	{
		string[] suffix = new string[5] { "B", "KiB", "MiB", "GiB", "TiB" };
		double dblSByte = bytes;
		int i = 0;
		while (i < suffix.Length && bytes >= 1024)
		{
			dblSByte = (double)bytes / 1024.0;
			i++;
			bytes /= 1024;
		}
		if (addSuffix)
		{
			return $"{dblSByte:0.00} {suffix[i]}";
		}
		return $"{dblSByte:0.00}";
	}

	public static string ToByteString(this long bytes, bool addSuffix = true)
	{
		string[] suffix = new string[5] { "B", "KiB", "MiB", "GiB", "TiB" };
		double dblSByte = bytes;
		int i = 0;
		while (i < suffix.Length && bytes >= 1024)
		{
			dblSByte = (double)bytes / 1024.0;
			i++;
			bytes /= 1024;
		}
		if (addSuffix)
		{
			return $"{dblSByte:0.00} {suffix[i]}";
		}
		return $"{dblSByte:0.00}";
	}

	public static void CancelDispose(this CancellationTokenSource? cts)
	{
		try
		{
			cts?.Cancel();
			cts?.Dispose();
		}
		catch (ObjectDisposedException)
		{
		}
	}

	public static CancellationTokenSource CancelRecreate(this CancellationTokenSource? cts)
	{
		cts?.CancelDispose();
		return new CancellationTokenSource();
	}

	public static Dictionary<XIVSync.API.Data.Enum.ObjectKind, HashSet<PlayerChanges>> CheckUpdatedData(this XIVSync.API.Data.CharacterData newData, Guid applicationBase, XIVSync.API.Data.CharacterData? oldData, ILogger logger, PairHandler cachedPlayer, bool forceApplyCustomization, bool forceApplyMods)
	{
		if (oldData == null)
		{
			oldData = new XIVSync.API.Data.CharacterData();
		}
		Dictionary<XIVSync.API.Data.Enum.ObjectKind, HashSet<PlayerChanges>> charaDataToUpdate = new Dictionary<XIVSync.API.Data.Enum.ObjectKind, HashSet<PlayerChanges>>();
		XIVSync.API.Data.Enum.ObjectKind[] values = Enum.GetValues<XIVSync.API.Data.Enum.ObjectKind>();
		foreach (XIVSync.API.Data.Enum.ObjectKind objectKind in values)
		{
			charaDataToUpdate[objectKind] = new HashSet<PlayerChanges>();
			oldData.FileReplacements.TryGetValue(objectKind, out List<FileReplacementData> existingFileReplacements);
			newData.FileReplacements.TryGetValue(objectKind, out List<FileReplacementData> newFileReplacements);
			oldData.GlamourerData.TryGetValue(objectKind, out string existingGlamourerData);
			newData.GlamourerData.TryGetValue(objectKind, out string newGlamourerData);
			bool hasNewButNotOldFileReplacements = newFileReplacements != null && existingFileReplacements == null;
			bool hasOldButNotNewFileReplacements = existingFileReplacements != null && newFileReplacements == null;
			bool hasNewButNotOldGlamourerData = newGlamourerData != null && existingGlamourerData == null;
			bool hasOldButNotNewGlamourerData = existingGlamourerData != null && newGlamourerData == null;
			bool hasNewAndOldFileReplacements = newFileReplacements != null && existingFileReplacements != null;
			bool hasNewAndOldGlamourerData = newGlamourerData != null && existingGlamourerData != null;
			if (hasNewButNotOldFileReplacements || hasOldButNotNewFileReplacements || hasNewButNotOldGlamourerData || hasOldButNotNewGlamourerData)
			{
				logger.LogDebug("[BASE-{appBase}] Updating {object}/{kind} (Some new data arrived: NewButNotOldFiles:{hasNewButNotOldFileReplacements}, OldButNotNewFiles:{hasOldButNotNewFileReplacements}, NewButNotOldGlam:{hasNewButNotOldGlamourerData}, OldButNotNewGlam:{hasOldButNotNewGlamourerData}) => {change}, {change2}", applicationBase, cachedPlayer, objectKind, hasNewButNotOldFileReplacements, hasOldButNotNewFileReplacements, hasNewButNotOldGlamourerData, hasOldButNotNewGlamourerData, PlayerChanges.ModFiles, PlayerChanges.Glamourer);
				charaDataToUpdate[objectKind].Add(PlayerChanges.ModFiles);
				charaDataToUpdate[objectKind].Add(PlayerChanges.Glamourer);
				charaDataToUpdate[objectKind].Add(PlayerChanges.ForcedRedraw);
			}
			else
			{
				if (hasNewAndOldFileReplacements && (!oldData.FileReplacements[objectKind].SequenceEqual(newData.FileReplacements[objectKind], FileReplacementDataComparer.Instance) || forceApplyMods))
				{
					logger.LogDebug("[BASE-{appBase}] Updating {object}/{kind} (FileReplacements not equal) => {change}", applicationBase, cachedPlayer, objectKind, PlayerChanges.ModFiles);
					charaDataToUpdate[objectKind].Add(PlayerChanges.ModFiles);
					if (forceApplyMods || objectKind != 0)
					{
						charaDataToUpdate[objectKind].Add(PlayerChanges.ForcedRedraw);
					}
					else
					{
						List<FileReplacementData> existingFace = existingFileReplacements.Where((FileReplacementData g) => g.GamePaths.Any((string p) => p.Contains("/face/", StringComparison.OrdinalIgnoreCase))).OrderBy<FileReplacementData, string>((FileReplacementData g) => (!string.IsNullOrEmpty(g.Hash)) ? g.Hash : g.FileSwapPath, StringComparer.OrdinalIgnoreCase).ToList();
						List<FileReplacementData> existingHair = existingFileReplacements.Where((FileReplacementData g) => g.GamePaths.Any((string p) => p.Contains("/hair/", StringComparison.OrdinalIgnoreCase))).OrderBy<FileReplacementData, string>((FileReplacementData g) => (!string.IsNullOrEmpty(g.Hash)) ? g.Hash : g.FileSwapPath, StringComparer.OrdinalIgnoreCase).ToList();
						List<FileReplacementData> existingTail = existingFileReplacements.Where((FileReplacementData g) => g.GamePaths.Any((string p) => p.Contains("/tail/", StringComparison.OrdinalIgnoreCase))).OrderBy<FileReplacementData, string>((FileReplacementData g) => (!string.IsNullOrEmpty(g.Hash)) ? g.Hash : g.FileSwapPath, StringComparer.OrdinalIgnoreCase).ToList();
						List<FileReplacementData> newFace = newFileReplacements.Where((FileReplacementData g) => g.GamePaths.Any((string p) => p.Contains("/face/", StringComparison.OrdinalIgnoreCase))).OrderBy<FileReplacementData, string>((FileReplacementData g) => (!string.IsNullOrEmpty(g.Hash)) ? g.Hash : g.FileSwapPath, StringComparer.OrdinalIgnoreCase).ToList();
						List<FileReplacementData> newHair = newFileReplacements.Where((FileReplacementData g) => g.GamePaths.Any((string p) => p.Contains("/hair/", StringComparison.OrdinalIgnoreCase))).OrderBy<FileReplacementData, string>((FileReplacementData g) => (!string.IsNullOrEmpty(g.Hash)) ? g.Hash : g.FileSwapPath, StringComparer.OrdinalIgnoreCase).ToList();
						List<FileReplacementData> newTail = newFileReplacements.Where((FileReplacementData g) => g.GamePaths.Any((string p) => p.Contains("/tail/", StringComparison.OrdinalIgnoreCase))).OrderBy<FileReplacementData, string>((FileReplacementData g) => (!string.IsNullOrEmpty(g.Hash)) ? g.Hash : g.FileSwapPath, StringComparer.OrdinalIgnoreCase).ToList();
						List<FileReplacementData> existingTransients = existingFileReplacements.Where((FileReplacementData g) => g.GamePaths.Any((string g) => !g.EndsWith("mdl") && !g.EndsWith("tex") && !g.EndsWith("mtrl"))).OrderBy<FileReplacementData, string>((FileReplacementData g) => (!string.IsNullOrEmpty(g.Hash)) ? g.Hash : g.FileSwapPath, StringComparer.OrdinalIgnoreCase).ToList();
						List<FileReplacementData> newTransients = newFileReplacements.Where((FileReplacementData g) => g.GamePaths.Any((string g) => !g.EndsWith("mdl") && !g.EndsWith("tex") && !g.EndsWith("mtrl"))).OrderBy<FileReplacementData, string>((FileReplacementData g) => (!string.IsNullOrEmpty(g.Hash)) ? g.Hash : g.FileSwapPath, StringComparer.OrdinalIgnoreCase).ToList();
						logger.LogTrace("[BASE-{appbase}] ExistingFace: {of}, NewFace: {fc}; ExistingHair: {eh}, NewHair: {nh}; ExistingTail: {et}, NewTail: {nt}; ExistingTransient: {etr}, NewTransient: {ntr}", applicationBase, existingFace.Count, newFace.Count, existingHair.Count, newHair.Count, existingTail.Count, newTail.Count, existingTransients.Count, newTransients.Count);
						bool differentFace = !existingFace.SequenceEqual(newFace, FileReplacementDataComparer.Instance);
						bool differentHair = !existingHair.SequenceEqual(newHair, FileReplacementDataComparer.Instance);
						bool differentTail = !existingTail.SequenceEqual(newTail, FileReplacementDataComparer.Instance);
						bool differenTransients = !existingTransients.SequenceEqual(newTransients, FileReplacementDataComparer.Instance);
						if (differentFace || differentHair || differentTail || differenTransients)
						{
							logger.LogDebug("[BASE-{appbase}] Different Subparts: Face: {face}, Hair: {hair}, Tail: {tail}, Transients: {transients} => {change}", applicationBase, differentFace, differentHair, differentTail, differenTransients, PlayerChanges.ForcedRedraw);
							charaDataToUpdate[objectKind].Add(PlayerChanges.ForcedRedraw);
						}
					}
				}
				if (hasNewAndOldGlamourerData && (!string.Equals(oldData.GlamourerData[objectKind], newData.GlamourerData[objectKind], StringComparison.Ordinal) || forceApplyCustomization))
				{
					logger.LogDebug("[BASE-{appBase}] Updating {object}/{kind} (Glamourer different) => {change}", applicationBase, cachedPlayer, objectKind, PlayerChanges.Glamourer);
					charaDataToUpdate[objectKind].Add(PlayerChanges.Glamourer);
				}
			}
			oldData.CustomizePlusData.TryGetValue(objectKind, out string oldCustomizePlusData);
			newData.CustomizePlusData.TryGetValue(objectKind, out string newCustomizePlusData);
			if (oldCustomizePlusData == null)
			{
				oldCustomizePlusData = string.Empty;
			}
			if (newCustomizePlusData == null)
			{
				newCustomizePlusData = string.Empty;
			}
			if (!string.Equals(oldCustomizePlusData, newCustomizePlusData, StringComparison.Ordinal) || (forceApplyCustomization && !string.IsNullOrEmpty(newCustomizePlusData)))
			{
				logger.LogDebug("[BASE-{appBase}] Updating {object}/{kind} (Diff customize data) => {change}", applicationBase, cachedPlayer, objectKind, PlayerChanges.Customize);
				charaDataToUpdate[objectKind].Add(PlayerChanges.Customize);
			}
			if (objectKind == XIVSync.API.Data.Enum.ObjectKind.Player)
			{
				if (!string.Equals(oldData.ManipulationData, newData.ManipulationData, StringComparison.Ordinal) || forceApplyMods)
				{
					logger.LogDebug("[BASE-{appBase}] Updating {object}/{kind} (Diff manip data) => {change}", applicationBase, cachedPlayer, objectKind, PlayerChanges.ModManip);
					charaDataToUpdate[objectKind].Add(PlayerChanges.ModManip);
					charaDataToUpdate[objectKind].Add(PlayerChanges.ForcedRedraw);
				}
				if (!string.Equals(oldData.HeelsData, newData.HeelsData, StringComparison.Ordinal) || (forceApplyCustomization && !string.IsNullOrEmpty(newData.HeelsData)))
				{
					logger.LogDebug("[BASE-{appBase}] Updating {object}/{kind} (Diff heels data) => {change}", applicationBase, cachedPlayer, objectKind, PlayerChanges.Heels);
					charaDataToUpdate[objectKind].Add(PlayerChanges.Heels);
				}
				if (!string.Equals(oldData.HonorificData, newData.HonorificData, StringComparison.Ordinal) || (forceApplyCustomization && !string.IsNullOrEmpty(newData.HonorificData)))
				{
					logger.LogDebug("[BASE-{appBase}] Updating {object}/{kind} (Diff honorific data) => {change}", applicationBase, cachedPlayer, objectKind, PlayerChanges.Honorific);
					charaDataToUpdate[objectKind].Add(PlayerChanges.Honorific);
				}
				if (!string.Equals(oldData.MoodlesData, newData.MoodlesData, StringComparison.Ordinal) || (forceApplyCustomization && !string.IsNullOrEmpty(newData.MoodlesData)))
				{
					logger.LogDebug("[BASE-{appBase}] Updating {object}/{kind} (Diff moodles data) => {change}", applicationBase, cachedPlayer, objectKind, PlayerChanges.Moodles);
					charaDataToUpdate[objectKind].Add(PlayerChanges.Moodles);
				}
				if (!string.Equals(oldData.PetNamesData, newData.PetNamesData, StringComparison.Ordinal) || (forceApplyCustomization && !string.IsNullOrEmpty(newData.PetNamesData)))
				{
					logger.LogDebug("[BASE-{appBase}] Updating {object}/{kind} (Diff petnames data) => {change}", applicationBase, cachedPlayer, objectKind, PlayerChanges.PetNames);
					charaDataToUpdate[objectKind].Add(PlayerChanges.PetNames);
				}
			}
		}
		foreach (KeyValuePair<XIVSync.API.Data.Enum.ObjectKind, HashSet<PlayerChanges>> data in charaDataToUpdate.ToList())
		{
			if (!data.Value.Any())
			{
				charaDataToUpdate.Remove(data.Key);
				continue;
			}
			Dictionary<XIVSync.API.Data.Enum.ObjectKind, HashSet<PlayerChanges>> dictionary = charaDataToUpdate;
			XIVSync.API.Data.Enum.ObjectKind key = data.Key;
			HashSet<PlayerChanges> hashSet = new HashSet<PlayerChanges>();
			foreach (PlayerChanges item in data.Value.OrderByDescending((PlayerChanges p) => (int)p))
			{
				hashSet.Add(item);
			}
			dictionary[key] = hashSet;
		}
		return charaDataToUpdate;
	}

	public static T DeepClone<T>(this T obj)
	{
		return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(obj));
	}

	public unsafe static int? ObjectTableIndex(this IGameObject? gameObject)
	{
		if (gameObject == null || gameObject.Address == IntPtr.Zero)
		{
			return null;
		}
		return ((GameObject*)gameObject.Address)->ObjectIndex;
	}
}
