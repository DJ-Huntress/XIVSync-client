using System;
using System.Collections.Generic;
using System.Linq;
using XIVSync.API.Data;
using XIVSync.API.Data.Enum;

namespace XIVSync.PlayerData.Data;

public class CharacterData
{
	public Dictionary<ObjectKind, string> CustomizePlusScale { get; set; } = new Dictionary<ObjectKind, string>();

	public Dictionary<ObjectKind, HashSet<FileReplacement>> FileReplacements { get; set; } = new Dictionary<ObjectKind, HashSet<FileReplacement>>();

	public Dictionary<ObjectKind, string> GlamourerString { get; set; } = new Dictionary<ObjectKind, string>();

	public string HeelsData { get; set; } = string.Empty;

	public string HonorificData { get; set; } = string.Empty;

	public string ManipulationString { get; set; } = string.Empty;

	public string MoodlesData { get; set; } = string.Empty;

	public string PetNamesData { get; set; } = string.Empty;

	public void SetFragment(ObjectKind kind, CharacterDataFragment? fragment)
	{
		if (kind == ObjectKind.Player)
		{
			CharacterDataFragmentPlayer playerFragment = fragment as CharacterDataFragmentPlayer;
			HeelsData = playerFragment?.HeelsData ?? string.Empty;
			HonorificData = playerFragment?.HonorificData ?? string.Empty;
			ManipulationString = playerFragment?.ManipulationString ?? string.Empty;
			MoodlesData = playerFragment?.MoodlesData ?? string.Empty;
			PetNamesData = playerFragment?.PetNamesData ?? string.Empty;
		}
		if (fragment == null)
		{
			CustomizePlusScale.Remove(kind);
			FileReplacements.Remove(kind);
			GlamourerString.Remove(kind);
		}
		else
		{
			CustomizePlusScale[kind] = fragment.CustomizePlusScale;
			FileReplacements[kind] = fragment.FileReplacements;
			GlamourerString[kind] = fragment.GlamourerString;
		}
	}

	public XIVSync.API.Data.CharacterData ToAPI(bool muteOwnSounds = false)
	{
		Dictionary<ObjectKind, List<FileReplacementData>> fileReplacements = FileReplacements.ToDictionary<KeyValuePair<ObjectKind, HashSet<FileReplacement>>, ObjectKind, List<FileReplacementData>>((KeyValuePair<ObjectKind, HashSet<FileReplacement>> k) => k.Key, (KeyValuePair<ObjectKind, HashSet<FileReplacement>> k) => (from g in k.Value.Where((FileReplacement f) => f.HasFileReplacement && !f.IsFileSwap).GroupBy<FileReplacement, string>((FileReplacement f) => f.Hash, StringComparer.OrdinalIgnoreCase)
			select new FileReplacementData
			{
				GamePaths = g.SelectMany((FileReplacement f) => f.GamePaths).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToArray(),
				Hash = g.First().Hash
			}).ToList());
		foreach (KeyValuePair<ObjectKind, HashSet<FileReplacement>> item in FileReplacements)
		{
			IEnumerable<FileReplacementData> fileSwapsToAdd = from f in item.Value
				where f.IsFileSwap
				select f.ToFileReplacementDto();
			fileReplacements[item.Key].AddRange(fileSwapsToAdd);
		}
		return new XIVSync.API.Data.CharacterData
		{
			FileReplacements = fileReplacements,
			GlamourerData = GlamourerString.ToDictionary<KeyValuePair<ObjectKind, string>, ObjectKind, string>((KeyValuePair<ObjectKind, string> d) => d.Key, (KeyValuePair<ObjectKind, string> d) => d.Value),
			ManipulationData = ManipulationString,
			HeelsData = HeelsData,
			CustomizePlusData = CustomizePlusScale.ToDictionary<KeyValuePair<ObjectKind, string>, ObjectKind, string>((KeyValuePair<ObjectKind, string> d) => d.Key, (KeyValuePair<ObjectKind, string> d) => d.Value),
			HonorificData = HonorificData,
			MoodlesData = MoodlesData,
			PetNamesData = PetNamesData
		};
	}
}
