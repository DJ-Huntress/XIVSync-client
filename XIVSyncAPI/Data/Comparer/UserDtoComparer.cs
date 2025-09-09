using System;
using System.Collections.Generic;
using XIVSync.API.Dto.User;

namespace XIVSync.API.Data.Comparer;

public class UserDtoComparer : IEqualityComparer<UserDto>
{
	private static UserDtoComparer _instance = new UserDtoComparer();

	public static UserDtoComparer Instance => _instance;

	private UserDtoComparer()
	{
	}

	public bool Equals(UserDto? x, UserDto? y)
	{
		if (x == null || y == null)
		{
			return false;
		}
		return x.User.UID.Equals(y.User.UID, StringComparison.Ordinal);
	}

	public int GetHashCode(UserDto obj)
	{
		return obj.User.UID.GetHashCode();
	}
}
