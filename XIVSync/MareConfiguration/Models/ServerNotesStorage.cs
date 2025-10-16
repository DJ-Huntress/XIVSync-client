using System;
using System.Collections.Generic;

namespace XIVSync.MareConfiguration.Models;

public class ServerNotesStorage
{
	public Dictionary<string, string> GidServerComments { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);


	public Dictionary<string, string> UidServerComments { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

}
