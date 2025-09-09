using System;
using XIVSync.API.Data;

namespace XIVSync.Services.Events;

public record Event
{
	public DateTime EventTime { get; }

	public string UID { get; }

	public string Character { get; }

	public string EventSource { get; }

	public EventSeverity EventSeverity { get; }

	public string Message { get; }

	public Event(string? Character, UserData UserData, string EventSource, EventSeverity EventSeverity, string Message)
	{
		EventTime = DateTime.Now;
		UID = UserData.AliasOrUID;
		this.Character = Character ?? string.Empty;
		this.EventSource = EventSource;
		this.EventSeverity = EventSeverity;
		this.Message = Message;
	}

	public Event(UserData UserData, string EventSource, EventSeverity EventSeverity, string Message)
		: this(null, UserData, EventSource, EventSeverity, Message)
	{
	}

	public Event(string EventSource, EventSeverity EventSeverity, string Message)
		: this(new UserData(string.Empty), EventSource, EventSeverity, Message)
	{
	}

	public override string ToString()
	{
		if (!string.IsNullOrEmpty(UID))
		{
			if (!string.IsNullOrEmpty(Character))
			{
				return $"{EventTime:HH:mm:ss.fff}\t[{EventSource}]{{{EventSeverity}}}\t<{UID}\\{Character}> {Message}";
			}
			return $"{EventTime:HH:mm:ss.fff}\t[{EventSource}]{{{EventSeverity}}}\t<{UID}> {Message}";
		}
		return $"{EventTime:HH:mm:ss.fff}\t[{EventSource}]{{{EventSeverity}}}\t{Message}";
	}
}
