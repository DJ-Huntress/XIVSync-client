using System;

namespace XIVSync.API.Data.Enum;

[Flags]
public enum UserPermissions
{
	NoneSet = 0,
	Paused = 1,
	DisableAnimations = 2,
	DisableSounds = 4,
	DisableVFX = 8,
	Sticky = 0x10
}
