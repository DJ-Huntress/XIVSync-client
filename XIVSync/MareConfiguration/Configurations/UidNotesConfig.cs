using System;
using System.Collections.Generic;
using XIVSync.MareConfiguration.Models;

namespace XIVSync.MareConfiguration.Configurations;

public class UidNotesConfig : IMareConfiguration
{
	public Dictionary<string, ServerNotesStorage> ServerNotes { get; set; } = new Dictionary<string, ServerNotesStorage>(StringComparer.Ordinal);

	public int Version { get; set; }
}
