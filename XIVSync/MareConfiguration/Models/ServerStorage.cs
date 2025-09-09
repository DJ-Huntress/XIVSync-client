using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http.Connections;

namespace XIVSync.MareConfiguration.Models;

[Serializable]
public class ServerStorage
{
	public List<Authentication> Authentications { get; set; } = new List<Authentication>();

	public bool FullPause { get; set; }

	public Dictionary<int, SecretKey> SecretKeys { get; set; } = new Dictionary<int, SecretKey>();

	public string ServerName { get; set; } = string.Empty;

	public string ServerUri { get; set; } = string.Empty;

	public bool UseOAuth2 { get; set; }

	public string? OAuthToken { get; set; }

	public HttpTransportType HttpTransportType { get; set; } = HttpTransportType.WebSockets;

	public bool ForceWebSockets { get; set; }
}
