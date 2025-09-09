using System.Runtime.CompilerServices;
using MessagePack;
using XIVSync.API.Data;
using XIVSync.API.Data.Enum;

namespace XIVSync.API.Dto.Group;

[MessagePackObject(true)]
public record GroupPairUserInfoDto : GroupPairDto
{
	public GroupPairUserInfo GroupUserInfo { get; init; }

	public GroupPairUserInfoDto(GroupData Group, UserData User, GroupPairUserInfo GroupUserInfo)
	{
		this.GroupUserInfo = GroupUserInfo;
		base._002Ector(Group, User);
	}

	[CompilerGenerated]
	public void Deconstruct(out GroupData Group, out UserData User, out GroupPairUserInfo GroupUserInfo)
	{
		Group = base.Group;
		User = base.User;
		GroupUserInfo = this.GroupUserInfo;
	}
}
