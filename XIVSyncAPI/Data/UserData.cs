using MessagePack;

namespace XIVSync.API.Data;

[MessagePackObject(true)]
public record UserData(string UID, string? Alias = null)
{
	[IgnoreMember]
	public string AliasOrUID
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(Alias))
			{
				return Alias;
			}
			return UID;
		}
	}
}
