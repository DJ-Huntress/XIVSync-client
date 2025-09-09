using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Utility;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.Extensions.Logging;
using XIVSync.API.Routes;
using XIVSync.MareConfiguration;
using XIVSync.MareConfiguration.Models;
using XIVSync.Services.Mediator;

namespace XIVSync.Services.ServerConfiguration;

public class ServerConfigurationManager
{
	private readonly ServerConfigService _configService;

	private readonly DalamudUtilService _dalamudUtil;

	private readonly MareConfigService _mareConfigService;

	private readonly HttpClient _httpClient;

	private readonly ILogger<ServerConfigurationManager> _logger;

	private readonly MareMediator _mareMediator;

	private readonly NotesConfigService _notesConfig;

	private readonly ServerTagConfigService _serverTagConfig;

	public string CurrentApiUrl => CurrentServer.ServerUri;

	public ServerStorage CurrentServer => _configService.Current.ServerStorage[CurrentServerIndex];

	public bool SendCensusData
	{
		get
		{
			return _configService.Current.SendCensusData;
		}
		set
		{
			_configService.Current.SendCensusData = value;
			_configService.Save();
		}
	}

	public bool ShownCensusPopup
	{
		get
		{
			return _configService.Current.ShownCensusPopup;
		}
		set
		{
			_configService.Current.ShownCensusPopup = value;
			_configService.Save();
		}
	}

	public int CurrentServerIndex
	{
		get
		{
			if (_configService.Current.CurrentServer < 0)
			{
				_configService.Current.CurrentServer = 0;
				_configService.Save();
			}
			return _configService.Current.CurrentServer;
		}
		set
		{
			_configService.Current.CurrentServer = value;
			_configService.Save();
		}
	}

	public ServerConfigurationManager(ILogger<ServerConfigurationManager> logger, ServerConfigService configService, ServerTagConfigService serverTagConfig, NotesConfigService notesConfig, DalamudUtilService dalamudUtil, MareConfigService mareConfigService, HttpClient httpClient, MareMediator mareMediator)
	{
		_logger = logger;
		_configService = configService;
		_serverTagConfig = serverTagConfig;
		_notesConfig = notesConfig;
		_dalamudUtil = dalamudUtil;
		_mareConfigService = mareConfigService;
		_httpClient = httpClient;
		_mareMediator = mareMediator;
		EnsureMainExists();
	}

	public (string OAuthToken, string UID)? GetOAuth2(out bool hasMulti, int serverIdx = -1)
	{
		ServerStorage currentServer = ((serverIdx == -1) ? CurrentServer : GetServerByIndex(serverIdx));
		if (currentServer == null)
		{
			currentServer = new ServerStorage();
			Save();
		}
		hasMulti = false;
		string charaName;
		ushort worldId;
		ulong cid;
		try
		{
			TimeSpan timeout = TimeSpan.FromSeconds(2L);
			charaName = (_dalamudUtil.GetPlayerNameAsync().Wait(timeout) ? _dalamudUtil.GetPlayerNameAsync().GetAwaiter().GetResult() : "--");
			worldId = (ushort)(_dalamudUtil.GetHomeWorldIdAsync().Wait(timeout) ? ((ushort)_dalamudUtil.GetHomeWorldIdAsync().GetAwaiter().GetResult()) : 0);
			cid = (_dalamudUtil.GetCIDAsync().Wait(timeout) ? _dalamudUtil.GetCIDAsync().GetAwaiter().GetResult() : 0);
		}
		catch (Exception)
		{
			charaName = "--";
			worldId = 0;
			cid = 0uL;
		}
		List<Authentication> auth = currentServer.Authentications.FindAll((Authentication f) => string.Equals(f.CharacterName, charaName) && f.WorldId == worldId);
		if (auth.Count >= 2)
		{
			_logger.LogTrace("GetOAuth2 accessed, returning null because multiple ({count}) identical characters.", auth.Count);
			hasMulti = true;
			return null;
		}
		if (auth.Count == 0)
		{
			_logger.LogTrace("GetOAuth2 accessed, returning null because no set up characters for {chara} on {world}", charaName, worldId);
			return null;
		}
		if (auth.Single().LastSeenCID != cid)
		{
			auth.Single().LastSeenCID = cid;
			_logger.LogTrace("GetOAuth2 accessed, updating CID for {chara} on {world} to {cid}", charaName, worldId, cid);
			Save();
		}
		if (!string.IsNullOrEmpty(auth.Single().UID) && !string.IsNullOrEmpty(currentServer.OAuthToken))
		{
			_logger.LogTrace("GetOAuth2 accessed, returning {key} ({keyValue}) for {chara} on {world}", auth.Single().UID, string.Join("", currentServer.OAuthToken.Take(10)), charaName, worldId);
			return (currentServer.OAuthToken, auth.Single().UID);
		}
		_logger.LogTrace("GetOAuth2 accessed, returning null because no UID found for {chara} on {world} or OAuthToken is not configured.", charaName, worldId);
		return null;
	}

	public string? GetSecretKey(out bool hasMulti, int serverIdx = -1)
	{
		ServerStorage currentServer = ((serverIdx == -1) ? CurrentServer : GetServerByIndex(serverIdx));
		if (currentServer == null)
		{
			currentServer = new ServerStorage();
			Save();
		}
		hasMulti = false;
		string charaName;
		ushort worldId;
		ulong cid;
		try
		{
			TimeSpan timeout = TimeSpan.FromSeconds(2L);
			charaName = (_dalamudUtil.GetPlayerNameAsync().Wait(timeout) ? _dalamudUtil.GetPlayerNameAsync().GetAwaiter().GetResult() : "--");
			worldId = (ushort)(_dalamudUtil.GetHomeWorldIdAsync().Wait(timeout) ? ((ushort)_dalamudUtil.GetHomeWorldIdAsync().GetAwaiter().GetResult()) : 0);
			cid = (_dalamudUtil.GetCIDAsync().Wait(timeout) ? _dalamudUtil.GetCIDAsync().GetAwaiter().GetResult() : 0);
		}
		catch (Exception)
		{
			charaName = "--";
			worldId = 0;
			cid = 0uL;
		}
		if (!currentServer.Authentications.Any() && currentServer.SecretKeys.Any())
		{
			currentServer.Authentications.Add(new Authentication
			{
				CharacterName = charaName,
				WorldId = worldId,
				LastSeenCID = cid,
				SecretKeyIdx = currentServer.SecretKeys.Last().Key
			});
			Save();
		}
		List<Authentication> auth = currentServer.Authentications.FindAll((Authentication f) => string.Equals(f.CharacterName, charaName, StringComparison.Ordinal) && f.WorldId == worldId);
		if (auth.Count >= 2)
		{
			_logger.LogTrace("GetSecretKey accessed, returning null because multiple ({count}) identical characters.", auth.Count);
			hasMulti = true;
			return null;
		}
		if (auth.Count == 0)
		{
			_logger.LogTrace("GetSecretKey accessed, returning null because no set up characters for {chara} on {world}", charaName, worldId);
			return null;
		}
		if (auth.Single().LastSeenCID != cid)
		{
			auth.Single().LastSeenCID = cid;
			_logger.LogTrace("GetSecretKey accessed, updating CID for {chara} on {world} to {cid}", charaName, worldId, cid);
			Save();
		}
		if (currentServer.SecretKeys.TryGetValue(auth.Single().SecretKeyIdx, out SecretKey secretKey))
		{
			_logger.LogTrace("GetSecretKey accessed, returning {key} ({keyValue}) for {chara} on {world}", secretKey.FriendlyName, string.Join("", secretKey.Key.Take(10)), charaName, worldId);
			return secretKey.Key;
		}
		_logger.LogTrace("GetSecretKey accessed, returning null because no fitting key found for {chara} on {world} for idx {idx}.", charaName, worldId, auth.Single().SecretKeyIdx);
		return null;
	}

	public string[] GetServerApiUrls()
	{
		return _configService.Current.ServerStorage.Select((ServerStorage v) => v.ServerUri).ToArray();
	}

	public ServerStorage GetServerByIndex(int idx)
	{
		try
		{
			return _configService.Current.ServerStorage[idx];
		}
		catch
		{
			_configService.Current.CurrentServer = 0;
			EnsureMainExists();
			return CurrentServer;
		}
	}

	public string GetDiscordUserFromToken(ServerStorage server)
	{
		JwtSecurityTokenHandler handler = new JwtSecurityTokenHandler();
		if (string.IsNullOrEmpty(server.OAuthToken))
		{
			return string.Empty;
		}
		try
		{
			return handler.ReadJwtToken(server.OAuthToken).Claims.First((Claim f) => string.Equals(f.Type, "discord_user", StringComparison.Ordinal)).Value;
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Could not read jwt, resetting it");
			server.OAuthToken = null;
			Save();
			return string.Empty;
		}
	}

	public string[] GetServerNames()
	{
		return _configService.Current.ServerStorage.Select((ServerStorage v) => v.ServerName).ToArray();
	}

	public bool HasValidConfig()
	{
		if (CurrentServer != null)
		{
			return CurrentServer.Authentications.Count > 0;
		}
		return false;
	}

	public void Save()
	{
		string caller = new StackTrace().GetFrame(1)?.GetMethod()?.ReflectedType?.Name ?? "Unknown";
		_logger.LogDebug("{caller} Calling config save", caller);
		_configService.Save();
	}

	public void SelectServer(int idx)
	{
		_configService.Current.CurrentServer = idx;
		CurrentServer.FullPause = false;
		Save();
	}

	internal void AddCurrentCharacterToServer(int serverSelectionIndex = -1)
	{
		if (serverSelectionIndex == -1)
		{
			serverSelectionIndex = CurrentServerIndex;
		}
		ServerStorage server = GetServerByIndex(serverSelectionIndex);
		if (!server.Authentications.Any((Authentication c) => string.Equals(c.CharacterName, _dalamudUtil.GetPlayerNameAsync().GetAwaiter().GetResult(), StringComparison.Ordinal) && c.WorldId == _dalamudUtil.GetHomeWorldIdAsync().GetAwaiter().GetResult()))
		{
			server.Authentications.Add(new Authentication
			{
				CharacterName = _dalamudUtil.GetPlayerNameAsync().GetAwaiter().GetResult(),
				WorldId = _dalamudUtil.GetHomeWorldIdAsync().GetAwaiter().GetResult(),
				SecretKeyIdx = ((!server.UseOAuth2) ? server.SecretKeys.Last().Key : (-1)),
				LastSeenCID = _dalamudUtil.GetCIDAsync().GetAwaiter().GetResult()
			});
			Save();
		}
	}

	internal void AddEmptyCharacterToServer(int serverSelectionIndex)
	{
		ServerStorage server = GetServerByIndex(serverSelectionIndex);
		server.Authentications.Add(new Authentication
		{
			SecretKeyIdx = (server.SecretKeys.Any() ? server.SecretKeys.First().Key : (-1))
		});
		Save();
	}

	internal void AddOpenPairTag(string tag)
	{
		CurrentServerTagStorage().OpenPairTags.Add(tag);
		_serverTagConfig.Save();
	}

	internal void AddServer(ServerStorage serverStorage)
	{
		_configService.Current.ServerStorage.Add(serverStorage);
		Save();
	}

	internal void AddTag(string tag)
	{
		CurrentServerTagStorage().ServerAvailablePairTags.Add(tag);
		_serverTagConfig.Save();
		_mareMediator.Publish(new RefreshUiMessage());
	}

	internal void AddTagForUid(string uid, string tagName)
	{
		if (CurrentServerTagStorage().UidServerPairedUserTags.TryGetValue(uid, out List<string> tags))
		{
			tags.Add(tagName);
			_mareMediator.Publish(new RefreshUiMessage());
		}
		else
		{
			Dictionary<string, List<string>> uidServerPairedUserTags = CurrentServerTagStorage().UidServerPairedUserTags;
			int num = 1;
			List<string> list = new List<string>(num);
			CollectionsMarshal.SetCount(list, num);
			Span<string> span = CollectionsMarshal.AsSpan(list);
			int index = 0;
			span[index] = tagName;
			uidServerPairedUserTags[uid] = list;
		}
		_serverTagConfig.Save();
	}

	internal bool ContainsOpenPairTag(string tag)
	{
		return CurrentServerTagStorage().OpenPairTags.Contains(tag);
	}

	internal bool ContainsTag(string uid, string tag)
	{
		if (CurrentServerTagStorage().UidServerPairedUserTags.TryGetValue(uid, out List<string> tags))
		{
			return tags.Contains<string>(tag, StringComparer.Ordinal);
		}
		return false;
	}

	internal void DeleteServer(ServerStorage selectedServer)
	{
		if (Array.IndexOf(_configService.Current.ServerStorage.ToArray(), selectedServer) < _configService.Current.CurrentServer)
		{
			_configService.Current.CurrentServer--;
		}
		_configService.Current.ServerStorage.Remove(selectedServer);
		Save();
	}

	internal string? GetNoteForGid(string gID)
	{
		if (CurrentNotesStorage().GidServerComments.TryGetValue(gID, out string note))
		{
			if (string.IsNullOrEmpty(note))
			{
				return null;
			}
			return note;
		}
		return null;
	}

	internal string? GetNoteForUid(string uid)
	{
		if (CurrentNotesStorage().UidServerComments.TryGetValue(uid, out string note))
		{
			if (string.IsNullOrEmpty(note))
			{
				return null;
			}
			return note;
		}
		return null;
	}

	internal HashSet<string> GetServerAvailablePairTags()
	{
		return CurrentServerTagStorage().ServerAvailablePairTags;
	}

	internal Dictionary<string, List<string>> GetUidServerPairedUserTags()
	{
		return CurrentServerTagStorage().UidServerPairedUserTags;
	}

	internal HashSet<string> GetUidsForTag(string tag)
	{
		return (from p in CurrentServerTagStorage().UidServerPairedUserTags
			where p.Value.Contains<string>(tag, StringComparer.Ordinal)
			select p.Key).ToHashSet<string>(StringComparer.Ordinal);
	}

	internal bool HasTags(string uid)
	{
		if (CurrentServerTagStorage().UidServerPairedUserTags.TryGetValue(uid, out List<string> tags))
		{
			return tags.Any();
		}
		return false;
	}

	internal void RemoveCharacterFromServer(int serverSelectionIndex, Authentication item)
	{
		GetServerByIndex(serverSelectionIndex).Authentications.Remove(item);
		Save();
	}

	internal void RemoveOpenPairTag(string tag)
	{
		CurrentServerTagStorage().OpenPairTags.Remove(tag);
		_serverTagConfig.Save();
	}

	internal void RemoveTag(string tag)
	{
		CurrentServerTagStorage().ServerAvailablePairTags.Remove(tag);
		foreach (string uid in GetUidsForTag(tag))
		{
			RemoveTagForUid(uid, tag, save: false);
		}
		_serverTagConfig.Save();
		_mareMediator.Publish(new RefreshUiMessage());
	}

	internal void RemoveTagForUid(string uid, string tagName, bool save = true)
	{
		if (CurrentServerTagStorage().UidServerPairedUserTags.TryGetValue(uid, out List<string> tags))
		{
			tags.Remove(tagName);
			if (save)
			{
				_serverTagConfig.Save();
				_mareMediator.Publish(new RefreshUiMessage());
			}
		}
	}

	internal void RenameTag(string oldName, string newName)
	{
		CurrentServerTagStorage().ServerAvailablePairTags.Remove(oldName);
		CurrentServerTagStorage().ServerAvailablePairTags.Add(newName);
		foreach (List<string> existingTags in CurrentServerTagStorage().UidServerPairedUserTags.Select<KeyValuePair<string, List<string>>, List<string>>((KeyValuePair<string, List<string>> k) => k.Value))
		{
			if (existingTags.Remove(oldName))
			{
				existingTags.Add(newName);
			}
		}
	}

	internal void SaveNotes()
	{
		_notesConfig.Save();
	}

	internal void SetNoteForGid(string gid, string note, bool save = true)
	{
		if (!string.IsNullOrEmpty(gid))
		{
			CurrentNotesStorage().GidServerComments[gid] = note;
			if (save)
			{
				_notesConfig.Save();
			}
		}
	}

	internal void SetNoteForUid(string uid, string note, bool save = true)
	{
		if (!string.IsNullOrEmpty(uid))
		{
			CurrentNotesStorage().UidServerComments[uid] = note;
			if (save)
			{
				_notesConfig.Save();
			}
		}
	}

	internal void AutoPopulateNoteForUid(string uid, string note)
	{
		if (_mareConfigService.Current.AutoPopulateEmptyNotesFromCharaName && GetNoteForUid(uid) == null)
		{
			SetNoteForUid(uid, note);
		}
	}

	private ServerNotesStorage CurrentNotesStorage()
	{
		TryCreateCurrentNotesStorage();
		return _notesConfig.Current.ServerNotes[CurrentApiUrl];
	}

	private ServerTagStorage CurrentServerTagStorage()
	{
		TryCreateCurrentServerTagStorage();
		return _serverTagConfig.Current.ServerTagStorage[CurrentApiUrl];
	}

	private void EnsureMainExists()
	{
		if (_configService.Current.ServerStorage.Count == 0 || !string.Equals(_configService.Current.ServerStorage[0].ServerUri, "https://mare.xivsync.com", StringComparison.OrdinalIgnoreCase))
		{
			_configService.Current.ServerStorage.Insert(0, new ServerStorage
			{
				ServerUri = "https://mare.xivsync.com",
				ServerName = "XIVSync Central Server",
				UseOAuth2 = true
			});
		}
		Save();
	}

	private void TryCreateCurrentNotesStorage()
	{
		if (!_notesConfig.Current.ServerNotes.ContainsKey(CurrentApiUrl))
		{
			_notesConfig.Current.ServerNotes[CurrentApiUrl] = new ServerNotesStorage();
		}
	}

	private void TryCreateCurrentServerTagStorage()
	{
		if (!_serverTagConfig.Current.ServerTagStorage.ContainsKey(CurrentApiUrl))
		{
			_serverTagConfig.Current.ServerTagStorage[CurrentApiUrl] = new ServerTagStorage();
		}
	}

	public async Task<Dictionary<string, string>> GetUIDsWithDiscordToken(string serverUri, string token)
	{
		_ = 2;
		try
		{
			Uri oauthCheckUri = MareAuth.GetUIDsFullPath(new Uri(serverUri.Replace("wss://", "https://").Replace("ws://", "http://")));
			_httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
			return (await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(await (await _httpClient.GetAsync(oauthCheckUri).ConfigureAwait(continueOnCapturedContext: false)).Content.ReadAsStreamAsync().ConfigureAwait(continueOnCapturedContext: false)).ConfigureAwait(continueOnCapturedContext: false)) ?? new Dictionary<string, string>();
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Failure getting UIDs");
			return new Dictionary<string, string>();
		}
	}

	public async Task<Uri?> CheckDiscordOAuth(string serverUri)
	{
		try
		{
			Uri oauthCheckUri = MareAuth.GetDiscordOAuthEndpointFullPath(new Uri(serverUri.Replace("wss://", "https://").Replace("ws://", "http://")));
			return await _httpClient.GetFromJsonAsync<Uri>(oauthCheckUri).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Failure checking for Discord Auth");
			return null;
		}
	}

	public async Task<string?> GetDiscordOAuthToken(Uri discordAuthUri, string serverUri, CancellationToken token)
	{
		string sessionId = BitConverter.ToString(RandomNumberGenerator.GetBytes(64)).Replace("-", "").ToLower();
		Util.OpenLink(discordAuthUri.ToString() + "?sessionId=" + sessionId);
		using CancellationTokenSource timeOutCts = new CancellationTokenSource();
		timeOutCts.CancelAfter(TimeSpan.FromSeconds(60L));
		using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeOutCts.Token, token);
		_ = 1;
		string discordToken;
		try
		{
			Uri oauthCheckUri = MareAuth.GetDiscordOAuthTokenFullPath(new Uri(serverUri.Replace("wss://", "https://").Replace("ws://", "http://")), sessionId);
			discordToken = await (await _httpClient.GetAsync(oauthCheckUri, linkedCts.Token).ConfigureAwait(continueOnCapturedContext: false)).Content.ReadAsStringAsync().ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Failure getting Discord Token");
			return null;
		}
		if (discordToken == null)
		{
			return null;
		}
		return discordToken;
	}

	public HttpTransportType GetTransport()
	{
		return CurrentServer.HttpTransportType;
	}

	public void SetTransportType(HttpTransportType httpTransportType)
	{
		CurrentServer.HttpTransportType = httpTransportType;
		Save();
	}
}
