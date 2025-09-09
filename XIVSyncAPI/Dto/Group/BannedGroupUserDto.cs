using System;
using MessagePack;
using XIVSync.API.Data;

namespace XIVSync.API.Dto.Group;

[MessagePackObject(true)]
public record BannedGroupUserDto : GroupPairDto
{
	public string Reason { get; set; }

	public DateTime BannedOn { get; set; }

	public string BannedBy { get; set; }

	public BannedGroupUserDto(GroupData group, UserData user, string reason, DateTime bannedOn, string bannedBy)
		: base(group, user)
	{
		Reason = reason;
		BannedOn = bannedOn;
		BannedBy = bannedBy;
	}
}
