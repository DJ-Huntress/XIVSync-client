using System.Runtime.CompilerServices;
using MessagePack;
using XIVSync.API.Data;
using XIVSync.API.Data.Enum;

namespace XIVSync.API.Dto.Group;

[MessagePackObject(true)]
public record GroupPairUserPermissionDto : GroupPairDto
{
	public GroupUserPreferredPermissions GroupPairPermissions { get; init; }

	public GroupPairUserPermissionDto(GroupData Group, UserData User, GroupUserPreferredPermissions GroupPairPermissions)
	{
		this.GroupPairPermissions = GroupPairPermissions;
		base._002Ector(Group, User);
	}

	[CompilerGenerated]
	public void Deconstruct(out GroupData Group, out UserData User, out GroupUserPreferredPermissions GroupPairPermissions)
	{
		Group = base.Group;
		User = base.User;
		GroupPairPermissions = this.GroupPairPermissions;
	}
}
