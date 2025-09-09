using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using XIVSync.API.Data;
using XIVSync.API.Dto.CharaData;

namespace XIVSync.Services.CharaData.Models;

public sealed record CharaDataExtendedUpdateDto : CharaDataUpdateDto
{
	public CharaDataUpdateDto BaseDto => new CharaDataUpdateDto(base.Id)
	{
		AllowedUsers = base.AllowedUsers,
		AllowedGroups = base.AllowedGroups,
		AccessType = base.AccessType,
		CustomizeData = base.CustomizeData,
		Description = base.Description,
		ExpiryDate = base.ExpiryDate,
		FileGamePaths = base.FileGamePaths,
		FileSwaps = base.FileSwaps,
		GlamourerData = base.GlamourerData,
		ShareType = base.ShareType,
		ManipulationData = base.ManipulationData,
		Poses = base.Poses
	};

	public new string ManipulationData
	{
		get
		{
			return base.ManipulationData ?? _charaDataFullDto.ManipulationData;
		}
		set
		{
			base.ManipulationData = value;
			if (string.Equals(base.ManipulationData, _charaDataFullDto.ManipulationData, StringComparison.Ordinal))
			{
				base.ManipulationData = null;
			}
		}
	}

	public new string Description
	{
		get
		{
			return base.Description ?? _charaDataFullDto.Description;
		}
		set
		{
			base.Description = value;
			if (string.Equals(base.Description, _charaDataFullDto.Description, StringComparison.Ordinal))
			{
				base.Description = null;
			}
		}
	}

	public new DateTime ExpiryDate
	{
		get
		{
			return base.ExpiryDate ?? _charaDataFullDto.ExpiryDate;
		}
		private set
		{
			base.ExpiryDate = value;
			if (object.Equals(base.ExpiryDate, _charaDataFullDto.ExpiryDate))
			{
				base.ExpiryDate = null;
			}
		}
	}

	public new AccessTypeDto AccessType
	{
		get
		{
			return base.AccessType ?? _charaDataFullDto.AccessType;
		}
		set
		{
			base.AccessType = value;
			if (object.Equals(base.AccessType, _charaDataFullDto.AccessType))
			{
				base.AccessType = null;
			}
		}
	}

	public new ShareTypeDto ShareType
	{
		get
		{
			return base.ShareType ?? _charaDataFullDto.ShareType;
		}
		set
		{
			base.ShareType = value;
			if (object.Equals(base.ShareType, _charaDataFullDto.ShareType))
			{
				base.ShareType = null;
			}
		}
	}

	public new List<GamePathEntry>? FileGamePaths
	{
		get
		{
			return base.FileGamePaths ?? _charaDataFullDto.FileGamePaths;
		}
		set
		{
			base.FileGamePaths = value;
			if (!(base.FileGamePaths ?? new List<GamePathEntry>()).Except(_charaDataFullDto.FileGamePaths).Any() && !_charaDataFullDto.FileGamePaths.Except(base.FileGamePaths ?? new List<GamePathEntry>()).Any())
			{
				base.FileGamePaths = null;
			}
		}
	}

	public new List<GamePathEntry>? FileSwaps
	{
		get
		{
			return base.FileSwaps ?? _charaDataFullDto.FileSwaps;
		}
		set
		{
			base.FileSwaps = value;
			if (!(base.FileSwaps ?? new List<GamePathEntry>()).Except(_charaDataFullDto.FileSwaps).Any() && !_charaDataFullDto.FileSwaps.Except(base.FileSwaps ?? new List<GamePathEntry>()).Any())
			{
				base.FileSwaps = null;
			}
		}
	}

	public new string? GlamourerData
	{
		get
		{
			return base.GlamourerData ?? _charaDataFullDto.GlamourerData;
		}
		set
		{
			base.GlamourerData = value;
			if (string.Equals(base.GlamourerData, _charaDataFullDto.GlamourerData, StringComparison.Ordinal))
			{
				base.GlamourerData = null;
			}
		}
	}

	public new string? CustomizeData
	{
		get
		{
			return base.CustomizeData ?? _charaDataFullDto.CustomizeData;
		}
		set
		{
			base.CustomizeData = value;
			if (string.Equals(base.CustomizeData, _charaDataFullDto.CustomizeData, StringComparison.Ordinal))
			{
				base.CustomizeData = null;
			}
		}
	}

	public IEnumerable<UserData> UserList => _userList;

	public IEnumerable<GroupData> GroupList => _groupList;

	public IEnumerable<PoseEntry> PoseList => _poseList;

	public bool HasChanges
	{
		get
		{
			if (base.Description == null && !base.ExpiryDate.HasValue && !base.AccessType.HasValue && !base.ShareType.HasValue && base.AllowedUsers == null && base.AllowedGroups == null && base.GlamourerData == null && base.FileSwaps == null && base.FileGamePaths == null && base.CustomizeData == null && base.ManipulationData == null)
			{
				return base.Poses != null;
			}
			return true;
		}
	}

	public bool IsAppearanceEqual
	{
		get
		{
			if (string.Equals(GlamourerData, _charaDataFullDto.GlamourerData, StringComparison.Ordinal) && string.Equals(CustomizeData, _charaDataFullDto.CustomizeData, StringComparison.Ordinal) && FileGamePaths == _charaDataFullDto.FileGamePaths && FileSwaps == _charaDataFullDto.FileSwaps)
			{
				return string.Equals(ManipulationData, _charaDataFullDto.ManipulationData, StringComparison.Ordinal);
			}
			return false;
		}
	}

	private readonly CharaDataFullDto _charaDataFullDto;

	private readonly List<UserData> _userList;

	private readonly List<GroupData> _groupList;

	private readonly List<PoseEntry> _poseList;

	public CharaDataExtendedUpdateDto(CharaDataUpdateDto dto, CharaDataFullDto charaDataFullDto)
		: base(dto)
	{
		_charaDataFullDto = charaDataFullDto;
		_userList = charaDataFullDto.AllowedUsers.ToList();
		_groupList = charaDataFullDto.AllowedGroups.ToList();
		_poseList = charaDataFullDto.PoseData.Select((PoseEntry k) => new PoseEntry(k.Id)
		{
			Description = k.Description,
			PoseData = k.PoseData,
			WorldData = k.WorldData
		}).ToList();
	}

	public void AddUserToList(string user)
	{
		_userList.Add(new UserData(user));
		UpdateAllowedUsers();
	}

	public void AddGroupToList(string group)
	{
		_groupList.Add(new GroupData(group));
		UpdateAllowedGroups();
	}

	private void UpdateAllowedUsers()
	{
		base.AllowedUsers = _userList.Select((UserData u) => u.UID).ToList();
		if (!base.AllowedUsers.Except<string>(_charaDataFullDto.AllowedUsers.Select((UserData u) => u.UID), StringComparer.Ordinal).Any() && !_charaDataFullDto.AllowedUsers.Select((UserData u) => u.UID).Except<string>(base.AllowedUsers, StringComparer.Ordinal).Any())
		{
			base.AllowedUsers = null;
		}
	}

	private void UpdateAllowedGroups()
	{
		base.AllowedGroups = _groupList.Select((GroupData u) => u.GID).ToList();
		if (!base.AllowedGroups.Except<string>(_charaDataFullDto.AllowedGroups.Select((GroupData u) => u.GID), StringComparer.Ordinal).Any() && !_charaDataFullDto.AllowedGroups.Select((GroupData u) => u.GID).Except<string>(base.AllowedGroups, StringComparer.Ordinal).Any())
		{
			base.AllowedGroups = null;
		}
	}

	public void RemoveUserFromList(string user)
	{
		_userList.RemoveAll((UserData u) => string.Equals(u.UID, user, StringComparison.Ordinal));
		UpdateAllowedUsers();
	}

	public void RemoveGroupFromList(string group)
	{
		_groupList.RemoveAll((GroupData u) => string.Equals(u.GID, group, StringComparison.Ordinal));
		UpdateAllowedGroups();
	}

	public void AddPose()
	{
		_poseList.Add(new PoseEntry(null));
		UpdatePoseList();
	}

	public void RemovePose(PoseEntry entry)
	{
		if (entry.Id.HasValue)
		{
			entry.Description = null;
			entry.WorldData = null;
			entry.PoseData = null;
		}
		else
		{
			_poseList.Remove(entry);
		}
		UpdatePoseList();
	}

	public void UpdatePoseList()
	{
		base.Poses = _poseList.ToList();
		if (!base.Poses.Except(_charaDataFullDto.PoseData).Any() && !_charaDataFullDto.PoseData.Except(base.Poses).Any())
		{
			base.Poses = null;
		}
	}

	public void SetExpiry(bool expiring)
	{
		if (expiring)
		{
			DateTime date = DateTime.UtcNow.AddDays(7.0);
			SetExpiry(date.Year, date.Month, date.Day);
		}
		else
		{
			ExpiryDate = DateTime.MaxValue;
		}
	}

	public void SetExpiry(int year, int month, int day)
	{
		int daysInMonth = DateTime.DaysInMonth(year, month);
		if (day > daysInMonth)
		{
			day = 1;
		}
		ExpiryDate = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
	}

	internal void UndoChanges()
	{
		base.Description = null;
		base.AccessType = null;
		base.ShareType = null;
		base.GlamourerData = null;
		base.FileSwaps = null;
		base.FileGamePaths = null;
		base.CustomizeData = null;
		base.ManipulationData = null;
		base.AllowedUsers = null;
		base.AllowedGroups = null;
		base.Poses = null;
		_poseList.Clear();
		_poseList.AddRange(_charaDataFullDto.PoseData.Select((PoseEntry k) => new PoseEntry(k.Id)
		{
			Description = k.Description,
			PoseData = k.PoseData,
			WorldData = k.WorldData
		}));
	}

	internal void RevertDeletion(PoseEntry pose)
	{
		if (pose.Id.HasValue)
		{
			PoseEntry oldPose = _charaDataFullDto.PoseData.Find((PoseEntry p) => p.Id == pose.Id);
			if (!(oldPose == null))
			{
				pose.Description = oldPose.Description;
				pose.PoseData = oldPose.PoseData;
				pose.WorldData = oldPose.WorldData;
				UpdatePoseList();
			}
		}
	}

	internal bool PoseHasChanges(PoseEntry pose)
	{
		if (!pose.Id.HasValue)
		{
			return false;
		}
		PoseEntry oldPose = _charaDataFullDto.PoseData.Find((PoseEntry p) => p.Id == pose.Id);
		if (oldPose == null)
		{
			return false;
		}
		if (string.Equals(pose.Description, oldPose.Description, StringComparison.Ordinal) && string.Equals(pose.PoseData, oldPose.PoseData, StringComparison.Ordinal))
		{
			WorldData? worldData = pose.WorldData;
			WorldData? worldData2 = oldPose.WorldData;
			if (worldData.HasValue != worldData2.HasValue)
			{
				return true;
			}
			if (!worldData.HasValue)
			{
				return false;
			}
			return worldData.GetValueOrDefault() != worldData2.GetValueOrDefault();
		}
		return true;
	}

	[CompilerGenerated]
	protected override bool PrintMembers(StringBuilder builder)
	{
		if (base.PrintMembers(builder))
		{
			builder.Append(", ");
		}
		builder.Append("BaseDto = ");
		builder.Append(BaseDto);
		builder.Append(", ManipulationData = ");
		builder.Append((object?)ManipulationData);
		builder.Append(", Description = ");
		builder.Append((object?)Description);
		builder.Append(", ExpiryDate = ");
		builder.Append(ExpiryDate.ToString());
		builder.Append(", AccessType = ");
		builder.Append(AccessType.ToString());
		builder.Append(", ShareType = ");
		builder.Append(ShareType.ToString());
		builder.Append(", FileGamePaths = ");
		builder.Append(FileGamePaths);
		builder.Append(", FileSwaps = ");
		builder.Append(FileSwaps);
		builder.Append(", GlamourerData = ");
		builder.Append((object?)GlamourerData);
		builder.Append(", CustomizeData = ");
		builder.Append((object?)CustomizeData);
		builder.Append(", UserList = ");
		builder.Append(UserList);
		builder.Append(", GroupList = ");
		builder.Append(GroupList);
		builder.Append(", PoseList = ");
		builder.Append(PoseList);
		builder.Append(", HasChanges = ");
		builder.Append(HasChanges.ToString());
		builder.Append(", IsAppearanceEqual = ");
		builder.Append(IsAppearanceEqual.ToString());
		return true;
	}
}
