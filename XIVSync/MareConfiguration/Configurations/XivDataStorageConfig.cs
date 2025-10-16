using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace XIVSync.MareConfiguration.Configurations;

public class XivDataStorageConfig : IMareConfiguration
{
	public ConcurrentDictionary<string, long> TriangleDictionary { get; set; } = new ConcurrentDictionary<string, long>(StringComparer.OrdinalIgnoreCase);


	public ConcurrentDictionary<string, Dictionary<string, List<ushort>>> BonesDictionary { get; set; } = new ConcurrentDictionary<string, Dictionary<string, List<ushort>>>(StringComparer.OrdinalIgnoreCase);


	public int Version { get; set; }
}
