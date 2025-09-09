using System;
using System.Collections.Generic;
using XIVSync.API.Dto.Group;

namespace XIVSync.API.Data.Comparer;

public class GroupPairDtoComparer : IEqualityComparer<GroupPairDto>
{
	private static GroupPairDtoComparer _instance = new GroupPairDtoComparer();

	public static GroupPairDtoComparer Instance => _instance;

	private GroupPairDtoComparer()
	{
	}

	public bool Equals(GroupPairDto? x, GroupPairDto? y)
	{
		if (x == null || y == null)
		{
			return false;
		}
		if (x.GID.Equals(y.GID, StringComparison.Ordinal))
		{
			return x.UID.Equals(y.UID, StringComparison.Ordinal);
		}
		return false;
	}

	public int GetHashCode(GroupPairDto obj)
	{
		return HashCode.Combine(obj.Group.GID.GetHashCode(), obj.User.UID.GetHashCode());
	}
}
