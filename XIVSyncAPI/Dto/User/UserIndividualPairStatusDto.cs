using System.Runtime.CompilerServices;
using MessagePack;
using XIVSync.API.Data;
using XIVSync.API.Data.Enum;

namespace XIVSync.API.Dto.User;

[MessagePackObject(true)]
public record UserIndividualPairStatusDto : UserDto
{
	public IndividualPairStatus IndividualPairStatus { get; init; }

	public UserIndividualPairStatusDto(UserData User, IndividualPairStatus IndividualPairStatus)
	{
		this.IndividualPairStatus = IndividualPairStatus;
		base._002Ector(User);
	}

	[CompilerGenerated]
	public void Deconstruct(out UserData User, out IndividualPairStatus IndividualPairStatus)
	{
		User = base.User;
		IndividualPairStatus = this.IndividualPairStatus;
	}
}
