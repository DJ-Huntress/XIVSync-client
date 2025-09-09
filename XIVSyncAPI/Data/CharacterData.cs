using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MessagePack;
using XIVSync.API.Data.Enum;

namespace XIVSync.API.Data;

[MessagePackObject(true)]
public class CharacterData
{
	public Dictionary<ObjectKind, string> CustomizePlusData { get; set; } = new Dictionary<ObjectKind, string>();

	[JsonIgnore]
	public Lazy<string> DataHash { get; }

	public Dictionary<ObjectKind, List<FileReplacementData>> FileReplacements { get; set; } = new Dictionary<ObjectKind, List<FileReplacementData>>();

	public Dictionary<ObjectKind, string> GlamourerData { get; set; } = new Dictionary<ObjectKind, string>();

	public string HeelsData { get; set; } = string.Empty;

	public string HonorificData { get; set; } = string.Empty;

	public string ManipulationData { get; set; } = string.Empty;

	public string MoodlesData { get; set; } = string.Empty;

	public string PetNamesData { get; set; } = string.Empty;

	public CharacterData()
	{
		DataHash = new Lazy<string>(delegate
		{
			string s = JsonSerializer.Serialize(this);
			using SHA256CryptoServiceProvider sHA256CryptoServiceProvider = new SHA256CryptoServiceProvider();
			return BitConverter.ToString(sHA256CryptoServiceProvider.ComputeHash(Encoding.UTF8.GetBytes(s))).Replace("-", "", StringComparison.Ordinal);
		});
	}
}
