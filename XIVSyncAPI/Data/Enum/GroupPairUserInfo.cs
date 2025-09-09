using System;

namespace XIVSync.API.Data.Enum;

[Flags]
public enum GroupPairUserInfo
{
	None = 0,
	IsModerator = 2,
	IsPinned = 4
}
