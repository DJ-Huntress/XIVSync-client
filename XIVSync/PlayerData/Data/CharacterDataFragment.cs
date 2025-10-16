using System.Collections.Generic;

namespace XIVSync.PlayerData.Data;

public class CharacterDataFragment
{
	public string CustomizePlusScale { get; set; } = string.Empty;


	public HashSet<FileReplacement> FileReplacements { get; set; } = new HashSet<FileReplacement>();


	public string GlamourerString { get; set; } = string.Empty;

}
