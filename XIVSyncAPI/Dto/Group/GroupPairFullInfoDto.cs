using System.Runtime.CompilerServices;
using MessagePack;
using XIVSync.API.Data;
using XIVSync.API.Data.Enum;

namespace XIVSync.API.Dto.Group;

[MessagePackObject(true)]
public record GroupPairFullInfoDto : GroupPairDto
{
	public UserPermissions SelfToOtherPermissions { get; init; }

	public UserPermissions OtherToSelfPermissions { get; init; }

	public GroupPairFullInfoDto(GroupData Group, UserData User, UserPermissions SelfToOtherPermissions, UserPermissions OtherToSelfPermissions) : base(Group, User)
	{
		this.SelfToOtherPermissions = SelfToOtherPermissions;
		this.OtherToSelfPermissions = OtherToSelfPermissions;
	}

	[CompilerGenerated]
	public void Deconstruct(out GroupData Group, out UserData User, out UserPermissions SelfToOtherPermissions, out UserPermissions OtherToSelfPermissions)
	{
		Group = base.Group;
		User = base.User;
		SelfToOtherPermissions = this.SelfToOtherPermissions;
		OtherToSelfPermissions = this.OtherToSelfPermissions;
	}
}
