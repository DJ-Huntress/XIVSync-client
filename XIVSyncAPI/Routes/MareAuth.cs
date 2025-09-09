using System;

namespace XIVSync.API.Routes;

public class MareAuth
{
	public const string OAuth = "/oauth";

	public const string Auth = "/auth";

	public const string Auth_CreateIdent = "createWithIdent";

	public const string Auth_RenewToken = "renewToken";

	public const string OAuth_GetUIDsBasedOnSecretKeys = "getUIDsViaSecretKey";

	public const string OAuth_CreateOAuth = "createWithOAuth";

	public const string OAuth_RenewOAuthToken = "renewToken";

	public const string OAuth_GetDiscordOAuthEndpoint = "getDiscordOAuthEndpoint";

	public const string OAuth_GetUIDs = "getUIDs";

	public const string OAuth_GetDiscordOAuthToken = "getDiscordOAuthToken";

	public static Uri AuthFullPath(Uri baseUri)
	{
		return new Uri(baseUri, "/auth/createWithIdent");
	}

	public static Uri AuthWithOauthFullPath(Uri baseUri)
	{
		return new Uri(baseUri, "/oauth/createWithOAuth");
	}

	public static Uri RenewTokenFullPath(Uri baseUri)
	{
		return new Uri(baseUri, "/auth/renewToken");
	}

	public static Uri RenewOAuthTokenFullPath(Uri baseUri)
	{
		return new Uri(baseUri, "/oauth/renewToken");
	}

	public static Uri GetUIDsBasedOnSecretKeyFullPath(Uri baseUri)
	{
		return new Uri(baseUri, "/oauth/getUIDsViaSecretKey");
	}

	public static Uri GetDiscordOAuthEndpointFullPath(Uri baseUri)
	{
		return new Uri(baseUri, "/oauth/getDiscordOAuthEndpoint");
	}

	public static Uri GetDiscordOAuthTokenFullPath(Uri baseUri, string sessionId)
	{
		return new Uri(baseUri, "/oauth/getDiscordOAuthToken?sessionId=" + sessionId);
	}

	public static Uri GetUIDsFullPath(Uri baseUri)
	{
		return new Uri(baseUri, "/oauth/getUIDs");
	}
}
