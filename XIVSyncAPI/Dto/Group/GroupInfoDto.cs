using System.Runtime.CompilerServices;
using MessagePack;
using XIVSync.API.Data;
using XIVSync.API.Data.Enum;

namespace XIVSync.API.Dto.Group;

[MessagePackObject(true)]
public record GroupInfoDto : GroupDto
{
	public GroupPermissions GroupPermissions { get; set; }

	public UserData Owner { get; set; }

	public string OwnerUID => Owner.UID;

	public string? OwnerAlias => Owner.Alias;

	public string OwnerAliasOrUID => Owner.AliasOrUID;

	public GroupInfoDto(GroupData Group, UserData Owner, GroupPermissions GroupPermissions) : base(Group)
	{
		this.GroupPermissions = GroupPermissions;
		this.Owner = Owner;
	}

	[CompilerGenerated]
	public void Deconstruct(out GroupData Group, out UserData Owner, out GroupPermissions GroupPermissions)
	{
		Group = base.Group;
		Owner = this.Owner;
		GroupPermissions = this.GroupPermissions;
	}
}
