using System.Runtime.CompilerServices;
using MessagePack;
using XIVSync.API.Data;

namespace XIVSync.API.Dto.User;

[MessagePackObject(true)]
public record OnlineUserIdentDto : UserDto
{
	public string Ident { get; init; }

	public OnlineUserIdentDto(UserData User, string Ident) : base(User)
	{
		this.Ident = Ident;
	}

	[CompilerGenerated]
	public void Deconstruct(out UserData User, out string Ident)
	{
		User = base.User;
		Ident = this.Ident;
	}
}
