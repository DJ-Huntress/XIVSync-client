using MessagePack;
using XIVSync.API.Data;

namespace XIVSync.API.Dto.Group;

[MessagePackObject(true)]
public record GroupDto(GroupData Group)
{
	public string GID => Group.GID;

	public string? GroupAlias => Group.Alias;

	public string GroupAliasOrGID => Group.AliasOrGID;
}
