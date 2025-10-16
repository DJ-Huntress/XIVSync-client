using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MessagePack;
using XIVSync.API.Data;
using XIVSync.API.Data.Enum;

namespace XIVSync.API.Dto.Group;

[MessagePackObject(true)]
public record GroupFullInfoDto : GroupInfoDto
{
	public Dictionary<string, GroupPairUserInfo> GroupPairUserInfos { get; init; }

	public GroupUserPreferredPermissions GroupUserPermissions { get; set; }

	public GroupPairUserInfo GroupUserInfo { get; set; }

	public GroupFullInfoDto(GroupData Group, UserData Owner, GroupPermissions GroupPermissions, GroupUserPreferredPermissions GroupUserPermissions, GroupPairUserInfo GroupUserInfo, Dictionary<string, GroupPairUserInfo> GroupPairUserInfos) : base(Group, Owner, GroupPermissions)
    {
		this.GroupPairUserInfos = GroupPairUserInfos;
		this.GroupUserPermissions = GroupUserPermissions;
		this.GroupUserInfo = GroupUserInfo;
	}

	[CompilerGenerated]
	public void Deconstruct(out GroupData Group, out UserData Owner, out GroupPermissions GroupPermissions, out GroupUserPreferredPermissions GroupUserPermissions, out GroupPairUserInfo GroupUserInfo, out Dictionary<string, GroupPairUserInfo> GroupPairUserInfos)
	{
		Group = base.Group;
		Owner = base.Owner;
		GroupPermissions = base.GroupPermissions;
		GroupUserPermissions = this.GroupUserPermissions;
		GroupUserInfo = this.GroupUserInfo;
		GroupPairUserInfos = this.GroupPairUserInfos;
	}
}
