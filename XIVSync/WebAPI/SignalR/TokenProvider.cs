using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using XIVSync.API.Routes;
using XIVSync.MareConfiguration.Models;
using XIVSync.Services;
using XIVSync.Services.Mediator;
using XIVSync.Services.ServerConfiguration;
using XIVSync.Utils;

namespace XIVSync.WebAPI.SignalR;

public sealed class TokenProvider : IDisposable, IMediatorSubscriber
{
	private readonly DalamudUtilService _dalamudUtil;

	private readonly HttpClient _httpClient;

	private readonly ILogger<TokenProvider> _logger;

	private readonly ServerConfigurationManager _serverManager;

	private readonly ConcurrentDictionary<JwtIdentifier, string> _tokenCache = new ConcurrentDictionary<JwtIdentifier, string>();

	private JwtIdentifier? _lastJwtIdentifier;

	public MareMediator Mediator { get; }

	public TokenProvider(ILogger<TokenProvider> logger, ServerConfigurationManager serverManager, DalamudUtilService dalamudUtil, MareMediator mareMediator, HttpClient httpClient)
	{
		_logger = logger;
		_serverManager = serverManager;
		_dalamudUtil = dalamudUtil;
		_ = Assembly.GetExecutingAssembly().GetName().Version;
		Mediator = mareMediator;
		_httpClient = httpClient;
		Mediator.Subscribe<DalamudLogoutMessage>(this, delegate
		{
			_lastJwtIdentifier = null;
			_tokenCache.Clear();
		});
		Mediator.Subscribe<DalamudLoginMessage>(this, delegate
		{
			_lastJwtIdentifier = null;
			_tokenCache.Clear();
		});
	}

	public void Dispose()
	{
		Mediator.UnsubscribeAll(this);
	}

	public async Task<string> GetNewToken(bool isRenewal, JwtIdentifier identifier, CancellationToken ct)
	{
		string response = string.Empty;
		string value;
		try
		{
			HttpResponseMessage result;
			if (!isRenewal)
			{
				_logger.LogDebug("GetNewToken: Requesting");
				if (!_serverManager.CurrentServer.UseOAuth2)
				{
					Uri tokenUri = MareAuth.AuthFullPath(new Uri(_serverManager.CurrentApiUrl.Replace("wss://", "https://", StringComparison.OrdinalIgnoreCase).Replace("ws://", "http://", StringComparison.OrdinalIgnoreCase)));
					bool hasMulti;
					string auth = _serverManager.GetSecretKey(out hasMulti).GetHash256();
					_logger.LogInformation("Sending SecretKey Request to server with auth {auth}", string.Join("", identifier.SecretKeyOrOAuth.Take(10)));
					HttpClient httpClient = _httpClient;
					Uri requestUri = tokenUri;
					KeyValuePair<string, string> keyValuePair = new KeyValuePair<string, string>("auth", auth);
					value = await _dalamudUtil.GetPlayerNameHashedAsync().ConfigureAwait(continueOnCapturedContext: false);
					result = await httpClient.PostAsync(requestUri, new FormUrlEncodedContent([
                            new KeyValuePair<string, string>("auth", auth),
                            new KeyValuePair<string, string>("charaIdent", await _dalamudUtil.GetPlayerNameHashedAsync().ConfigureAwait(false)),
                    ]), ct).ConfigureAwait(continueOnCapturedContext: false);
				}
				else
				{
					Uri tokenUri = MareAuth.AuthWithOauthFullPath(new Uri(_serverManager.CurrentApiUrl.Replace("wss://", "https://", StringComparison.OrdinalIgnoreCase).Replace("ws://", "http://", StringComparison.OrdinalIgnoreCase)));
					HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, tokenUri.ToString());
					request.Content = new FormUrlEncodedContent([
                        new KeyValuePair<string, string>("uid", identifier.UID),
                        new KeyValuePair<string, string>("charaIdent", identifier.CharaHash)
                        ]);
					request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", identifier.SecretKeyOrOAuth);
					_logger.LogInformation("Sending OAuth Request to server with auth {auth}", string.Join("", identifier.SecretKeyOrOAuth.Take(10)));
					result = await _httpClient.SendAsync(request, ct).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
			else
			{
				_logger.LogDebug("GetNewToken: Renewal");
				Uri tokenUri = MareAuth.RenewTokenFullPath(new Uri(_serverManager.CurrentApiUrl.Replace("wss://", "https://", StringComparison.OrdinalIgnoreCase).Replace("ws://", "http://", StringComparison.OrdinalIgnoreCase)));
				HttpRequestMessage request2 = new HttpRequestMessage(HttpMethod.Get, tokenUri.ToString());
				request2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokenCache[identifier]);
				result = await _httpClient.SendAsync(request2, ct).ConfigureAwait(continueOnCapturedContext: false);
			}
			value = await result.Content.ReadAsStringAsync().ConfigureAwait(continueOnCapturedContext: false);
			response = value;
			result.EnsureSuccessStatusCode();
			_tokenCache[identifier] = response;
		}
		catch (HttpRequestException ex)
		{
			_tokenCache.TryRemove(identifier, out value);
			_logger.LogError(ex, "GetNewToken: Failure to get token");
			if (ex.StatusCode == HttpStatusCode.Unauthorized)
			{
				if (isRenewal)
				{
					Mediator.Publish(new NotificationMessage("Error refreshing token", "Your authentication token could not be renewed. Try reconnecting to Mare manually.", NotificationType.Error));
				}
				else
				{
					Mediator.Publish(new NotificationMessage("Error generating token", "Your authentication token could not be generated. Check Mares Main UI (/mare in chat) to see the error message.", NotificationType.Error));
				}
				Mediator.Publish(new DisconnectedMessage());
				throw new MareAuthFailureException(response);
			}
			throw;
		}
		JwtSecurityToken jwtToken = new JwtSecurityTokenHandler().ReadJwtToken(response);
		_logger.LogTrace("GetNewToken: JWT {token}", response);
		_logger.LogDebug("GetNewToken: Valid until {date}, ValidClaim until {date}", jwtToken.ValidTo, new DateTime(long.Parse(jwtToken.Claims.Single((Claim c) => string.Equals(c.Type, "expiration_date", StringComparison.Ordinal)).Value), DateTimeKind.Utc));
		DateTime dateTimeMinus10 = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(10L));
		DateTime dateTimePlus10 = DateTime.UtcNow.Add(TimeSpan.FromMinutes(10L));
		DateTime tokenTime = jwtToken.ValidTo.Subtract(TimeSpan.FromHours(6));
		if (tokenTime <= dateTimeMinus10 || tokenTime >= dateTimePlus10)
		{
			_tokenCache.TryRemove(identifier, out value);
			Mediator.Publish(new NotificationMessage("Invalid system clock", "The clock of your computer is invalid. Mare will not function properly if the time zone is not set correctly. Please set your computers time zone correctly and keep your clock synchronized with the internet.", NotificationType.Error));
			throw new InvalidOperationException($"JwtToken is behind DateTime.UtcNow, DateTime.UtcNow is possibly wrong. DateTime.UtcNow is {DateTime.UtcNow}, JwtToken.ValidTo is {jwtToken.ValidTo}");
		}
		return response;
	}

	private async Task<JwtIdentifier?> GetIdentifier()
	{
		JwtIdentifier jwtIdentifier;
		try
		{
			string playerIdentifier = await _dalamudUtil.GetPlayerNameHashedAsync().ConfigureAwait(continueOnCapturedContext: false);
			if (string.IsNullOrEmpty(playerIdentifier))
			{
				_logger.LogTrace("GetIdentifier: PlayerIdentifier was null, returning last identifier {identifier}", _lastJwtIdentifier);
				return _lastJwtIdentifier;
			}
			bool hasMulti;
			if (_serverManager.CurrentServer.UseOAuth2)
			{
				(string OAuthToken, string UID) obj = _serverManager.GetOAuth2(out hasMulti) ?? throw new InvalidOperationException("Requested OAuth2 but received null");
				string OAuthToken = obj.OAuthToken;
				string UID = obj.UID;
				jwtIdentifier = new JwtIdentifier(_serverManager.CurrentApiUrl, playerIdentifier, UID, OAuthToken);
			}
			else
			{
				string secretKey = _serverManager.GetSecretKey(out hasMulti) ?? throw new InvalidOperationException("Requested SecretKey but received null");
				jwtIdentifier = new JwtIdentifier(_serverManager.CurrentApiUrl, playerIdentifier, string.Empty, secretKey);
			}
			_lastJwtIdentifier = jwtIdentifier;
		}
		catch (Exception exception)
		{
			if (_lastJwtIdentifier == null)
			{
				_logger.LogError("GetIdentifier: No last identifier found, aborting");
				return null;
			}
			_logger.LogWarning(exception, "GetIdentifier: Could not get JwtIdentifier for some reason or another, reusing last identifier {identifier}", _lastJwtIdentifier);
			jwtIdentifier = _lastJwtIdentifier;
		}
		_logger.LogDebug("GetIdentifier: Using identifier {identifier}", jwtIdentifier);
		return jwtIdentifier;
	}

	public async Task<string?> GetToken()
	{
		JwtIdentifier jwtIdentifier = await GetIdentifier().ConfigureAwait(continueOnCapturedContext: false);
		if (jwtIdentifier == null)
		{
			return null;
		}
		if (_tokenCache.TryGetValue(jwtIdentifier, out string token))
		{
			return token;
		}
		throw new InvalidOperationException("No token present");
	}

	public async Task<string?> GetOrUpdateToken(CancellationToken ct)
	{
		JwtIdentifier jwtIdentifier = await GetIdentifier().ConfigureAwait(continueOnCapturedContext: false);
		if (jwtIdentifier == null)
		{
			return null;
		}
		bool renewal = false;
		if (_tokenCache.TryGetValue(jwtIdentifier, out string token))
		{
			JwtSecurityToken jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
			if (jwt.ValidTo == DateTime.MinValue || jwt.ValidTo.Subtract(TimeSpan.FromMinutes(5L)) > DateTime.UtcNow)
			{
				return token;
			}
			_logger.LogDebug("GetOrUpdate: Cached token requires renewal, token valid to: {valid}, UtcTime is {utcTime}", jwt.ValidTo, DateTime.UtcNow);
			renewal = true;
		}
		else
		{
			_logger.LogDebug("GetOrUpdate: Did not find token in cache, requesting a new one");
		}
		_logger.LogTrace("GetOrUpdate: Getting new token");
		return await GetNewToken(renewal, jwtIdentifier, ct).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<bool> TryUpdateOAuth2LoginTokenAsync(ServerStorage currentServer, bool forced = false)
	{
		bool hasMulti;
		(string, string)? oauth2 = _serverManager.GetOAuth2(out hasMulti);
		if (!oauth2.HasValue)
		{
			return false;
		}
		JwtSecurityToken jwt = new JwtSecurityTokenHandler().ReadJwtToken(oauth2.Value.Item1);
		if (!forced)
		{
			if (jwt.ValidTo == DateTime.MinValue || jwt.ValidTo.Subtract(TimeSpan.FromDays(7)) > DateTime.Now)
			{
				return true;
			}
			if (jwt.ValidTo < DateTime.UtcNow)
			{
				return false;
			}
		}
		Uri tokenUri = MareAuth.RenewOAuthTokenFullPath(new Uri(currentServer.ServerUri.Replace("wss://", "https://", StringComparison.OrdinalIgnoreCase).Replace("ws://", "http://", StringComparison.OrdinalIgnoreCase)));
		HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, tokenUri.ToString());
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", oauth2.Value.Item1);
		_logger.LogInformation("Sending Request to server with auth {auth}", string.Join("", oauth2.Value.Item1.Take(10)));
		HttpResponseMessage result = await _httpClient.SendAsync(request).ConfigureAwait(continueOnCapturedContext: false);
		if (!result.IsSuccessStatusCode)
		{
			_logger.LogWarning("Could not renew OAuth2 Login token, error code {error}", result.StatusCode);
			currentServer.OAuthToken = null;
			_serverManager.Save();
			return false;
		}
		currentServer.OAuthToken = await result.Content.ReadAsStringAsync().ConfigureAwait(continueOnCapturedContext: false);
		_serverManager.Save();
		return true;
	}
}
