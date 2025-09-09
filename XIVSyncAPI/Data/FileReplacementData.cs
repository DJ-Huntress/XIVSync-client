using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MessagePack;

namespace XIVSync.API.Data;

[MessagePackObject(true)]
public class FileReplacementData
{
	[JsonIgnore]
	public Lazy<string> DataHash { get; }

	public string FileSwapPath { get; set; } = string.Empty;

	public string[] GamePaths { get; set; } = Array.Empty<string>();

	public string Hash { get; set; } = string.Empty;

	public FileReplacementData()
	{
		DataHash = new Lazy<string>(delegate
		{
			string s = JsonSerializer.Serialize(this);
			using SHA256CryptoServiceProvider sHA256CryptoServiceProvider = new SHA256CryptoServiceProvider();
			return BitConverter.ToString(sHA256CryptoServiceProvider.ComputeHash(Encoding.UTF8.GetBytes(s))).Replace("-", "", StringComparison.Ordinal);
		});
	}
}
