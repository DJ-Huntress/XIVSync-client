using System.Runtime.CompilerServices;
using MessagePack;
using XIVSync.API.Data;
using XIVSync.API.Data.Enum;

namespace XIVSync.API.Dto.User;

[MessagePackObject(true)]
public record UserPermissionsDto : UserDto
{
	public UserPermissions Permissions { get; init; }

	public UserPermissionsDto(UserData User, UserPermissions Permissions) : base(User)
	{
		this.Permissions = Permissions;
	}

	[CompilerGenerated]
	public void Deconstruct(out UserData User, out UserPermissions Permissions)
	{
		User = base.User;
		Permissions = this.Permissions;
	}
}
