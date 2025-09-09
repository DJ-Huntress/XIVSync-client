using System;

namespace XIVSync.API.Data.Enum;

[Flags]
public enum GroupPermissions
{
	NoneSet = 0,
	PreferDisableAnimations = 1,
	PreferDisableSounds = 2,
	DisableInvites = 4,
	PreferDisableVFX = 8
}
