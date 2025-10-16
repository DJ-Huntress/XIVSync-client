using System.Runtime.CompilerServices;
using MessagePack;
using XIVSync.API.Data;
using XIVSync.API.Data.Enum;

namespace XIVSync.API.Dto.User;

[MessagePackObject(true)]
public record UserPairDto : UserDto
{
	public UserPermissions OwnPermissions { get; set; }

	public UserPermissions OtherPermissions { get; set; }

	public IndividualPairStatus IndividualPairStatus { get; set; }

	public UserPairDto(UserData User, IndividualPairStatus IndividualPairStatus, UserPermissions OwnPermissions, UserPermissions OtherPermissions) : base(User)
	{
		this.OwnPermissions = OwnPermissions;
		this.OtherPermissions = OtherPermissions;
		this.IndividualPairStatus = IndividualPairStatus;
	}

	[CompilerGenerated]
	public void Deconstruct(out UserData User, out IndividualPairStatus IndividualPairStatus, out UserPermissions OwnPermissions, out UserPermissions OtherPermissions)
	{
		User = base.User;
		IndividualPairStatus = this.IndividualPairStatus;
		OwnPermissions = this.OwnPermissions;
		OtherPermissions = this.OtherPermissions;
	}
}
