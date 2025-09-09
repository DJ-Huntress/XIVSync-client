using System.Runtime.CompilerServices;
using MessagePack;
using XIVSync.API.Data;

namespace XIVSync.API.Dto.User;

[MessagePackObject(true)]
public record UserProfileDto : UserDto
{
	public bool Disabled { get; init; }

	public bool? IsNSFW { get; init; }

	public string? ProfilePictureBase64 { get; init; }

	public string? Description { get; init; }

	public UserProfileDto(UserData User, bool Disabled, bool? IsNSFW, string? ProfilePictureBase64, string? Description)
	{
		this.Disabled = Disabled;
		this.IsNSFW = IsNSFW;
		this.ProfilePictureBase64 = ProfilePictureBase64;
		this.Description = Description;
		base._002Ector(User);
	}

	[CompilerGenerated]
	public void Deconstruct(out UserData User, out bool Disabled, out bool? IsNSFW, out string? ProfilePictureBase64, out string? Description)
	{
		User = base.User;
		Disabled = this.Disabled;
		IsNSFW = this.IsNSFW;
		ProfilePictureBase64 = this.ProfilePictureBase64;
		Description = this.Description;
	}
}
