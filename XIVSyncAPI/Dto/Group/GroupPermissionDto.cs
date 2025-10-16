using System.Runtime.CompilerServices;
using MessagePack;
using XIVSync.API.Data;
using XIVSync.API.Data.Enum;

namespace XIVSync.API.Dto.Group;

[MessagePackObject(true)]
public record GroupPermissionDto : GroupDto
{
	public GroupPermissions Permissions { get; init; }

	public GroupPermissionDto(GroupData Group, GroupPermissions Permissions) : base(Group)
	{
		this.Permissions = Permissions;
	}

	[CompilerGenerated]
	public void Deconstruct(out GroupData Group, out GroupPermissions Permissions)
	{
		Group = base.Group;
		Permissions = this.Permissions;
	}
}
