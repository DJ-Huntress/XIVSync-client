using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace XIVSync.Utils;

public static class Crypto
{
	private static readonly Dictionary<(string, ushort), string> _hashListPlayersSHA256 = new Dictionary<(string, ushort), string>();

	private static readonly Dictionary<string, string> _hashListSHA256 = new Dictionary<string, string>(StringComparer.Ordinal);

	private static readonly SHA256CryptoServiceProvider _sha256CryptoProvider = new SHA256CryptoServiceProvider();

	public static string GetFileHash(this string filePath)
	{
		using SHA1CryptoServiceProvider cryptoProvider = new SHA1CryptoServiceProvider();
		return BitConverter.ToString(cryptoProvider.ComputeHash(File.ReadAllBytes(filePath))).Replace("-", "", StringComparison.Ordinal);
	}

	public static string GetHash256(this (string, ushort) playerToHash)
	{
		if (_hashListPlayersSHA256.TryGetValue(playerToHash, out string hash))
		{
			return hash;
		}
		return _hashListPlayersSHA256[playerToHash] = BitConverter.ToString(_sha256CryptoProvider.ComputeHash(Encoding.UTF8.GetBytes(playerToHash.Item1 + playerToHash.Item2))).Replace("-", "", StringComparison.Ordinal);
	}

	public static string GetHash256(this string stringToHash)
	{
		return GetOrComputeHashSHA256(stringToHash);
	}

	private static string GetOrComputeHashSHA256(string stringToCompute)
	{
		if (_hashListSHA256.TryGetValue(stringToCompute, out string hash))
		{
			return hash;
		}
		return _hashListSHA256[stringToCompute] = BitConverter.ToString(_sha256CryptoProvider.ComputeHash(Encoding.UTF8.GetBytes(stringToCompute))).Replace("-", "", StringComparison.Ordinal);
	}
}
