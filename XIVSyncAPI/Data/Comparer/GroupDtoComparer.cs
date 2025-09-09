using System;
using System.Collections.Generic;
using XIVSync.API.Dto.Group;

namespace XIVSync.API.Data.Comparer;

public class GroupDtoComparer : IEqualityComparer<GroupDto>
{
	private static GroupDtoComparer _instance = new GroupDtoComparer();

	public static GroupDtoComparer Instance => _instance;

	private GroupDtoComparer()
	{
	}

	public bool Equals(GroupDto? x, GroupDto? y)
	{
		if (x == null || y == null)
		{
			return false;
		}
		return x.GID.Equals(y.GID, StringComparison.Ordinal);
	}

	public int GetHashCode(GroupDto obj)
	{
		return obj.Group.GID.GetHashCode();
	}
}
