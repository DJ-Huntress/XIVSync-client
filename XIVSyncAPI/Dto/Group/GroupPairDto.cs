using System.Runtime.CompilerServices;
using MessagePack;
using XIVSync.API.Data;

namespace XIVSync.API.Dto.Group;

[MessagePackObject(true)]
public record GroupPairDto : GroupDto
{
	public UserData User { get; init; }

	public string UID => User.UID;

	public string? UserAlias => User.Alias;

	public string UserAliasOrUID => User.AliasOrUID;

	public GroupPairDto(GroupData Group, UserData User) : base(Group)
	{
		this.User = User;
	}

	[CompilerGenerated]
	public void Deconstruct(out GroupData Group, out UserData User)
	{
		Group = base.Group;
		User = this.User;
	}
}
