using System;
using System.Collections.Generic;
using XIVSync.MareConfiguration.Models;

namespace XIVSync.MareConfiguration.Configurations;

[Serializable]
public class ServerConfig : IMareConfiguration
{
	public int CurrentServer { get; set; }

	public List<ServerStorage> ServerStorage { get; set; } = new List<ServerStorage>
	{
		new ServerStorage
		{
			ServerName = "XIVSync Central Server",
			ServerUri = "https://mare.xivsync.com",
			UseOAuth2 = true
		}
	};


	public bool SendCensusData { get; set; }

	public bool ShownCensusPopup { get; set; }

	public int Version { get; set; } = 2;

}
