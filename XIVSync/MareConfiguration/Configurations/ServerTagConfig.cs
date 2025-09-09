using System;
using System.Collections.Generic;
using XIVSync.MareConfiguration.Models;

namespace XIVSync.MareConfiguration.Configurations;

public class ServerTagConfig : IMareConfiguration
{
	public Dictionary<string, ServerTagStorage> ServerTagStorage { get; set; } = new Dictionary<string, ServerTagStorage>(StringComparer.OrdinalIgnoreCase);

	public int Version { get; set; }
}
