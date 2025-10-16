using System.Runtime.CompilerServices;
using MessagePack;
using XIVSync.API.Data;
using XIVSync.API.Data.Enum;

namespace XIVSync.API.Dto.Group;

[MessagePackObject(true)]
public record GroupJoinDto : GroupPasswordDto
{
	public GroupUserPreferredPermissions GroupUserPreferredPermissions { get; init; }

	public GroupJoinDto(GroupData Group, string Password, GroupUserPreferredPermissions GroupUserPreferredPermissions) : base(Group, Password)
    {
		this.GroupUserPreferredPermissions = GroupUserPreferredPermissions;
	}

	[CompilerGenerated]
	public void Deconstruct(out GroupData Group, out string Password, out GroupUserPreferredPermissions GroupUserPreferredPermissions)
	{
		Group = base.Group;
		Password = base.Password;
		GroupUserPreferredPermissions = this.GroupUserPreferredPermissions;
	}
}
