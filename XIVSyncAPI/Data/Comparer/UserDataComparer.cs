using System;
using System.Collections.Generic;

namespace XIVSync.API.Data.Comparer;

public class UserDataComparer : IEqualityComparer<UserData>
{
	private static UserDataComparer _instance = new UserDataComparer();

	public static UserDataComparer Instance => _instance;

	private UserDataComparer()
	{
	}

	public bool Equals(UserData? x, UserData? y)
	{
		if (x == null || y == null)
		{
			return false;
		}
		return x.UID.Equals(y.UID, StringComparison.Ordinal);
	}

	public int GetHashCode(UserData obj)
	{
		return obj.UID.GetHashCode();
	}
}
