using System.Runtime.CompilerServices;
using MessagePack;
using XIVSync.API.Data;

namespace XIVSync.API.Dto.User;

[MessagePackObject(true)]
public record OnlineUserCharaDataDto : UserDto
{
	public CharacterData CharaData { get; init; }

	public OnlineUserCharaDataDto(UserData User, CharacterData CharaData) : base(User)
	{
		this.CharaData = CharaData;
	}

	[CompilerGenerated]
	public void Deconstruct(out UserData User, out CharacterData CharaData)
	{
		User = base.User;
		CharaData = this.CharaData;
	}
}
