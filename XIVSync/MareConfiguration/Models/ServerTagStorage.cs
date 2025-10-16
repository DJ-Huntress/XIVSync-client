using System;
using System.Collections.Generic;

namespace XIVSync.MareConfiguration.Models;

[Serializable]
public class ServerTagStorage
{
	public HashSet<string> OpenPairTags { get; set; } = new HashSet<string>(StringComparer.Ordinal);


	public HashSet<string> ServerAvailablePairTags { get; set; } = new HashSet<string>(StringComparer.Ordinal);


	public Dictionary<string, List<string>> UidServerPairedUserTags { get; set; } = new Dictionary<string, List<string>>(StringComparer.Ordinal);

}
