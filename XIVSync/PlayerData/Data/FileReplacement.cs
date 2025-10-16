using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.RegularExpressions.Generated;
using XIVSync.API.Data;

namespace XIVSync.PlayerData.Data;

public class FileReplacement
{
	public HashSet<string> GamePaths { get; init; }

	public bool HasFileReplacement
	{
		get
		{
			if (GamePaths.Count >= 1)
			{
				return GamePaths.Any((string p) => !string.Equals(p, ResolvedPath, StringComparison.Ordinal));
			}
			return false;
		}
	}

	public string Hash { get; set; } = string.Empty;


	public bool IsFileSwap
	{
		get
		{
			if (!LocalPathRegex().IsMatch(ResolvedPath))
			{
				return GamePaths.All((string p) => !LocalPathRegex().IsMatch(p));
			}
			return false;
		}
	}

	public string ResolvedPath { get; init; }

	public FileReplacement(string[] gamePaths, string filePath)
	{
		GamePaths = gamePaths.Select((string g) => g.Replace('\\', '/').ToLowerInvariant()).ToHashSet<string>(StringComparer.Ordinal);
		ResolvedPath = filePath.Replace('\\', '/');
	}

	public FileReplacementData ToFileReplacementDto()
	{
		return new FileReplacementData
		{
			GamePaths = GamePaths.ToArray(),
			Hash = Hash,
			FileSwapPath = (IsFileSwap ? ResolvedPath : string.Empty)
		};
	}

	public override string ToString()
	{
		return $"HasReplacement:{HasFileReplacement},IsFileSwap:{IsFileSwap} - {string.Join(",", GamePaths)} => {ResolvedPath}";
	}

	[GeneratedRegex("^[a-zA-Z]:(/|\\\\)", RegexOptions.ECMAScript)]
	[GeneratedCode("System.Text.RegularExpressions.Generator", "9.0.12.6610")]
	private static Regex LocalPathRegex()
	{
		return _003CRegexGenerator_g_003EF7C2A3A4E3E23D9AAD8AD182C53AF1445029BB959A191641E99FAF6FAA9B714AC__LocalPathRegex_0.Instance;
	}
}
