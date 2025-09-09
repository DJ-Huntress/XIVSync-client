using System;

namespace XIVSync.API.Data.Enum;

[Flags]
public enum GroupUserPreferredPermissions
{
	NoneSet = 0,
	Paused = 1,
	DisableAnimations = 2,
	DisableSounds = 4,
	DisableVFX = 8
}
