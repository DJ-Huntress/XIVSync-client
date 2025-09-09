using System.Runtime.CompilerServices;
using MessagePack;
using XIVSync.API.Data;

namespace XIVSync.API.Dto.Group;

[MessagePackObject(true)]
public record GroupPasswordDto : GroupDto
{
	public string Password { get; init; }

	public GroupPasswordDto(GroupData Group, string Password)
	{
		this.Password = Password;
		base._002Ector(Group);
	}

	[CompilerGenerated]
	public void Deconstruct(out GroupData Group, out string Password)
	{
		Group = base.Group;
		Password = this.Password;
	}
}
