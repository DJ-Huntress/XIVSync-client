using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MessagePack;
using XIVSync.API.Data;
using XIVSync.API.Data.Enum;

namespace XIVSync.API.Dto.User;

[MessagePackObject(true)]
public record UserFullPairDto : UserDto
{
	public List<string> Groups { get; init; }

	public UserPermissions OwnPermissions { get; set; }

	public UserPermissions OtherPermissions { get; set; }

	public IndividualPairStatus IndividualPairStatus { get; set; }

	public UserFullPairDto(UserData User, IndividualPairStatus IndividualPairStatus, List<string> Groups, UserPermissions OwnPermissions, UserPermissions OtherPermissions) : base(User)
	{
		this.Groups = Groups;
		this.OwnPermissions = OwnPermissions;
		this.OtherPermissions = OtherPermissions;
		this.IndividualPairStatus = IndividualPairStatus;
	}

	[CompilerGenerated]
	public void Deconstruct(out UserData User, out IndividualPairStatus IndividualPairStatus, out List<string> Groups, out UserPermissions OwnPermissions, out UserPermissions OtherPermissions)
	{
		User = base.User;
		IndividualPairStatus = this.IndividualPairStatus;
		Groups = this.Groups;
		OwnPermissions = this.OwnPermissions;
		OtherPermissions = this.OtherPermissions;
	}
}
