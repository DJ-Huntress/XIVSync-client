using MessagePack;

namespace XIVSync.API.Data;

[MessagePackObject(true)]
public record GroupData(string GID, string? Alias = null)
{
	[IgnoreMember]
	public string AliasOrGID
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(Alias))
			{
				return Alias;
			}
			return GID;
		}
	}
}
