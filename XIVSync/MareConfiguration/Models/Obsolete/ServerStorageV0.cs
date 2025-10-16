using System;
using System.Collections.Generic;
using System.Linq;

namespace XIVSync.MareConfiguration.Models.Obsolete;

[Serializable]
[Obsolete("Deprecated, use ServerStorage")]
public class ServerStorageV0
{
	public List<Authentication> Authentications { get; set; } = new List<Authentication>();


	public bool FullPause { get; set; }

	public Dictionary<string, string> GidServerComments { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);


	public HashSet<string> OpenPairTags { get; set; } = new HashSet<string>(StringComparer.Ordinal);


	public Dictionary<int, SecretKey> SecretKeys { get; set; } = new Dictionary<int, SecretKey>();


	public HashSet<string> ServerAvailablePairTags { get; set; } = new HashSet<string>(StringComparer.Ordinal);


	public string ServerName { get; set; } = string.Empty;


	public string ServerUri { get; set; } = string.Empty;


	public Dictionary<string, string> UidServerComments { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);


	public Dictionary<string, List<string>> UidServerPairedUserTags { get; set; } = new Dictionary<string, List<string>>(StringComparer.Ordinal);


	public ServerStorage ToV1()
	{
		return new ServerStorage
		{
			ServerUri = ServerUri,
			ServerName = ServerName,
			Authentications = Authentications.ToList(),
			FullPause = FullPause,
			SecretKeys = SecretKeys.ToDictionary<KeyValuePair<int, SecretKey>, int, SecretKey>((KeyValuePair<int, SecretKey> p) => p.Key, (KeyValuePair<int, SecretKey> p) => p.Value)
		};
	}
}
