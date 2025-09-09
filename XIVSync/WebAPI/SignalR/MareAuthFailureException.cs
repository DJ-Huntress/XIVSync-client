using System;

namespace XIVSync.WebAPI.SignalR;

public class MareAuthFailureException : Exception
{
	public string Reason { get; }

	public MareAuthFailureException(string reason)
	{
		Reason = reason;
	}
}
