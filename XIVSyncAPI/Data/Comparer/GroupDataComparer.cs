using System;
using System.Collections.Generic;

namespace XIVSync.API.Data.Comparer;

public class GroupDataComparer : IEqualityComparer<GroupData>
{
	private static GroupDataComparer _instance = new GroupDataComparer();

	public static GroupDataComparer Instance => _instance;

	private GroupDataComparer()
	{
	}

	public bool Equals(GroupData? x, GroupData? y)
	{
		if (x == null || y == null)
		{
			return false;
		}
		return x.GID.Equals(y.GID, StringComparison.Ordinal);
	}

	public int GetHashCode(GroupData obj)
	{
		return obj.GID.GetHashCode();
	}
}
