using System.Runtime.CompilerServices;
using XIVSync.API.Data;
using XIVSync.API.Data.Enum;

namespace XIVSync.API.Dto.Group;

public record GroupJoinInfoDto : GroupInfoDto
{
	public bool Success { get; init; }

	public GroupJoinInfoDto(GroupData Group, UserData Owner, GroupPermissions GroupPermissions, bool Success)
	{
		this.Success = Success;
		base._002Ector(Group, Owner, GroupPermissions);
	}

	[CompilerGenerated]
	public void Deconstruct(out GroupData Group, out UserData Owner, out GroupPermissions GroupPermissions, out bool Success)
	{
		Group = base.Group;
		Owner = base.Owner;
		GroupPermissions = base.GroupPermissions;
		Success = this.Success;
	}
}
