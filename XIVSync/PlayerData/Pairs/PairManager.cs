using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging;
using XIVSync.API.Data;
using XIVSync.API.Data.Comparer;
using XIVSync.API.Data.Enum;
using XIVSync.API.Data.Extensions;
using XIVSync.API.Dto.Group;
using XIVSync.API.Dto.User;
using XIVSync.MareConfiguration;
using XIVSync.MareConfiguration.Models;
using XIVSync.PlayerData.Factories;
using XIVSync.Services.Events;
using XIVSync.Services.Mediator;

namespace XIVSync.PlayerData.Pairs;

public sealed class PairManager : DisposableMediatorSubscriberBase
{
	private readonly ConcurrentDictionary<UserData, Pair> _allClientPairs = new ConcurrentDictionary<UserData, Pair>(UserDataComparer.Instance);

	private readonly ConcurrentDictionary<GroupData, GroupFullInfoDto> _allGroups = new ConcurrentDictionary<GroupData, GroupFullInfoDto>(GroupDataComparer.Instance);

	private readonly MareConfigService _configurationService;

	private readonly IContextMenu _dalamudContextMenu;

	private readonly PairFactory _pairFactory;

	private Lazy<List<Pair>> _directPairsInternal;

	private Lazy<Dictionary<GroupFullInfoDto, List<Pair>>> _groupPairsInternal;

	private Lazy<Dictionary<Pair, List<GroupFullInfoDto>>> _pairsWithGroupsInternal;

	public List<Pair> DirectPairs => _directPairsInternal.Value;

	public Dictionary<GroupFullInfoDto, List<Pair>> GroupPairs => _groupPairsInternal.Value;

	public Dictionary<GroupData, GroupFullInfoDto> Groups => _allGroups.ToDictionary<KeyValuePair<GroupData, GroupFullInfoDto>, GroupData, GroupFullInfoDto>((KeyValuePair<GroupData, GroupFullInfoDto> k) => k.Key, (KeyValuePair<GroupData, GroupFullInfoDto> k) => k.Value);

	public Pair? LastAddedUser { get; internal set; }

	public Dictionary<Pair, List<GroupFullInfoDto>> PairsWithGroups => _pairsWithGroupsInternal.Value;

	public PairManager(ILogger<PairManager> logger, PairFactory pairFactory, MareConfigService configurationService, MareMediator mediator, IContextMenu dalamudContextMenu)
		: base(logger, mediator)
	{
		_pairFactory = pairFactory;
		_configurationService = configurationService;
		_dalamudContextMenu = dalamudContextMenu;
		base.Mediator.Subscribe<DisconnectedMessage>(this, delegate
		{
			ClearPairs();
		});
		base.Mediator.Subscribe<CutsceneEndMessage>(this, delegate
		{
			ReapplyPairData();
		});
		_directPairsInternal = DirectPairsLazy();
		_groupPairsInternal = GroupPairsLazy();
		_pairsWithGroupsInternal = PairsWithGroupsLazy();
		_dalamudContextMenu.OnMenuOpened += DalamudContextMenuOnOnOpenGameObjectContextMenu;
	}

	public void AddGroup(GroupFullInfoDto dto)
	{
		_allGroups[dto.Group] = dto;
		RecreateLazy();
	}

	public void AddGroupPair(GroupPairFullInfoDto dto)
	{
		if (!_allClientPairs.ContainsKey(dto.User))
		{
			ConcurrentDictionary<UserData, Pair> allClientPairs = _allClientPairs;
			UserData user = dto.User;
			PairFactory pairFactory = _pairFactory;
			UserData user2 = dto.User;
			int num = 1;
			List<string> list = new List<string>(num);
			CollectionsMarshal.SetCount(list, num);
			Span<string> span = CollectionsMarshal.AsSpan(list);
			int index = 0;
			span[index] = dto.Group.GID;
			allClientPairs[user] = pairFactory.Create(new UserFullPairDto(user2, IndividualPairStatus.None, list, dto.SelfToOtherPermissions, dto.OtherToSelfPermissions));
		}
		else
		{
			_allClientPairs[dto.User].UserPair.Groups.Add(dto.GID);
		}
		RecreateLazy();
	}

	public Pair? GetPairByUID(string uid)
	{
		KeyValuePair<UserData, Pair> existingPair = _allClientPairs.FirstOrDefault<KeyValuePair<UserData, Pair>>((KeyValuePair<UserData, Pair> f) => f.Key.UID == uid);
		if (!object.Equals(existingPair, default(KeyValuePair<UserData, Pair>)))
		{
			return existingPair.Value;
		}
		return null;
	}

	public void AddUserPair(UserFullPairDto dto)
	{
		if (!_allClientPairs.ContainsKey(dto.User))
		{
			_allClientPairs[dto.User] = _pairFactory.Create(dto);
		}
		else
		{
			_allClientPairs[dto.User].UserPair.IndividualPairStatus = dto.IndividualPairStatus;
			_allClientPairs[dto.User].ApplyLastReceivedData();
		}
		RecreateLazy();
	}

	public void AddUserPair(UserPairDto dto, bool addToLastAddedUser = true)
	{
		if (!_allClientPairs.ContainsKey(dto.User))
		{
			_allClientPairs[dto.User] = _pairFactory.Create(dto);
		}
		else
		{
			addToLastAddedUser = false;
		}
		_allClientPairs[dto.User].UserPair.IndividualPairStatus = dto.IndividualPairStatus;
		_allClientPairs[dto.User].UserPair.OwnPermissions = dto.OwnPermissions;
		_allClientPairs[dto.User].UserPair.OtherPermissions = dto.OtherPermissions;
		if (addToLastAddedUser)
		{
			LastAddedUser = _allClientPairs[dto.User];
		}
		_allClientPairs[dto.User].ApplyLastReceivedData();
		RecreateLazy();
	}

	public void ClearPairs()
	{
		base.Logger.LogDebug("Clearing all Pairs");
		DisposePairs();
		_allClientPairs.Clear();
		_allGroups.Clear();
		RecreateLazy();
	}

	public List<Pair> GetOnlineUserPairs()
	{
		return (from p in _allClientPairs
			where !string.IsNullOrEmpty(p.Value.GetPlayerNameHash())
			select p.Value).ToList();
	}

	public int GetVisibleUserCount()
	{
		return _allClientPairs.Count<KeyValuePair<UserData, Pair>>((KeyValuePair<UserData, Pair> p) => p.Value.IsVisible);
	}

	public List<UserData> GetVisibleUsers()
	{
		return (from p in _allClientPairs
			where p.Value.IsVisible
			select p.Key).ToList();
	}

	public void MarkPairOffline(UserData user)
	{
		if (_allClientPairs.TryGetValue(user, out Pair pair))
		{
			base.Mediator.Publish(new ClearProfileDataMessage(pair.UserData));
			pair.MarkOffline();
		}
		RecreateLazy();
	}

	public void MarkPairOnline(OnlineUserIdentDto dto, bool sendNotif = true)
	{
		if (!_allClientPairs.TryGetValue(dto.User, out Pair pair))
		{
			UserPairDto minimal = new UserPairDto(dto.User, IndividualPairStatus.None, UserPermissions.NoneSet, UserPermissions.NoneSet);
			AddUserPair(minimal, addToLastAddedUser: false);
			pair = _allClientPairs[dto.User];
		}
		base.Mediator.Publish(new ClearProfileDataMessage(dto.User));
		if (!pair.HasCachedPlayer)
		{
			pair.CreateCachedPlayer(dto);
		}
		if (sendNotif && _configurationService.Current.ShowOnlineNotifications && ((_configurationService.Current.ShowOnlineNotificationsOnlyForIndividualPairs && pair.IsDirectlyPaired && !pair.IsOneSidedPair) || !_configurationService.Current.ShowOnlineNotificationsOnlyForIndividualPairs))
		{
			string msg = (string.IsNullOrEmpty(pair.UserData.Alias) ? (pair.UserData.UID + " is now online") : (pair.UserData.AliasOrUID + " is now online"));
			base.Mediator.Publish(new NotificationMessage("User online", msg, NotificationType.Info, TimeSpan.FromSeconds(5L)));
		}
		RecreateLazy();
	}

	public void ReceiveCharaData(OnlineUserCharaDataDto dto)
	{
		if (!_allClientPairs.TryGetValue(dto.User, out Pair pair))
		{
			throw new InvalidOperationException("No user found for " + dto.User);
		}
		base.Mediator.Publish(new EventMessage(new Event(pair.UserData, "PairManager", EventSeverity.Informational, "Received Character Data")));
		_allClientPairs[dto.User].ApplyData(dto);
	}

	public void RemoveGroup(GroupData data)
	{
		_allGroups.TryRemove(data, out GroupFullInfoDto _);
		foreach (KeyValuePair<UserData, Pair> item in _allClientPairs.ToList())
		{
			item.Value.UserPair.Groups.Remove(data.GID);
			if (!item.Value.HasAnyConnection())
			{
				item.Value.MarkOffline();
				_allClientPairs.TryRemove(item.Key, out Pair _);
			}
		}
		RecreateLazy();
	}

	public void RemoveGroupPair(GroupPairDto dto)
	{
		if (_allClientPairs.TryGetValue(dto.User, out Pair pair))
		{
			pair.UserPair.Groups.Remove(dto.Group.GID);
			if (!pair.HasAnyConnection())
			{
				pair.MarkOffline();
				_allClientPairs.TryRemove(dto.User, out Pair _);
			}
		}
		RecreateLazy();
	}

	public void RemoveUserPair(UserDto dto)
	{
		if (_allClientPairs.TryGetValue(dto.User, out Pair pair))
		{
			pair.UserPair.IndividualPairStatus = IndividualPairStatus.None;
			if (!pair.HasAnyConnection())
			{
				pair.MarkOffline();
				_allClientPairs.TryRemove(dto.User, out Pair _);
			}
		}
		RecreateLazy();
	}

	public void SetGroupInfo(GroupInfoDto dto)
	{
		_allGroups[dto.Group].Group = dto.Group;
		_allGroups[dto.Group].Owner = dto.Owner;
		_allGroups[dto.Group].GroupPermissions = dto.GroupPermissions;
		RecreateLazy();
	}

	public void UpdatePairPermissions(UserPermissionsDto dto)
	{
		if (!_allClientPairs.TryGetValue(dto.User, out Pair pair))
		{
			throw new InvalidOperationException("No such pair for " + dto);
		}
		if (pair.UserPair == null)
		{
			throw new InvalidOperationException("No direct pair for " + dto);
		}
		if (pair.UserPair.OtherPermissions.IsPaused() != dto.Permissions.IsPaused())
		{
			base.Mediator.Publish(new ClearProfileDataMessage(dto.User));
		}
		pair.UserPair.OtherPermissions = dto.Permissions;
		base.Logger.LogTrace("Paused: {paused}, Anims: {anims}, Sounds: {sounds}, VFX: {vfx}", pair.UserPair.OtherPermissions.IsPaused(), pair.UserPair.OtherPermissions.IsDisableAnimations(), pair.UserPair.OtherPermissions.IsDisableSounds(), pair.UserPair.OtherPermissions.IsDisableVFX());
		if (!pair.IsPaused)
		{
			pair.ApplyLastReceivedData();
		}
		RecreateLazy();
	}

	public void UpdateSelfPairPermissions(UserPermissionsDto dto)
	{
		if (!_allClientPairs.TryGetValue(dto.User, out Pair pair))
		{
			throw new InvalidOperationException("No such pair for " + dto);
		}
		if (pair.UserPair.OwnPermissions.IsPaused() != dto.Permissions.IsPaused())
		{
			base.Mediator.Publish(new ClearProfileDataMessage(dto.User));
		}
		pair.UserPair.OwnPermissions = dto.Permissions;
		base.Logger.LogTrace("Paused: {paused}, Anims: {anims}, Sounds: {sounds}, VFX: {vfx}", pair.UserPair.OwnPermissions.IsPaused(), pair.UserPair.OwnPermissions.IsDisableAnimations(), pair.UserPair.OwnPermissions.IsDisableSounds(), pair.UserPair.OwnPermissions.IsDisableVFX());
		if (!pair.IsPaused)
		{
			pair.ApplyLastReceivedData();
		}
		RecreateLazy();
	}

	internal void ReceiveUploadStatus(UserDto dto)
	{
		if (_allClientPairs.TryGetValue(dto.User, out Pair existingPair) && existingPair.IsVisible)
		{
			existingPair.SetIsUploading();
		}
	}

	internal void SetGroupPairStatusInfo(GroupPairUserInfoDto dto)
	{
		_allGroups[dto.Group].GroupPairUserInfos[dto.UID] = dto.GroupUserInfo;
		RecreateLazy();
	}

	internal void SetGroupPermissions(GroupPermissionDto dto)
	{
		_allGroups[dto.Group].GroupPermissions = dto.Permissions;
		RecreateLazy();
	}

	internal void SetGroupStatusInfo(GroupPairUserInfoDto dto)
	{
		_allGroups[dto.Group].GroupUserInfo = dto.GroupUserInfo;
		RecreateLazy();
	}

	internal void UpdateGroupPairPermissions(GroupPairUserPermissionDto dto)
	{
		_allGroups[dto.Group].GroupUserPermissions = dto.GroupPairPermissions;
		RecreateLazy();
	}

	internal void UpdateIndividualPairStatus(UserIndividualPairStatusDto dto)
	{
		if (_allClientPairs.TryGetValue(dto.User, out Pair pair))
		{
			pair.UserPair.IndividualPairStatus = dto.IndividualPairStatus;
			RecreateLazy();
		}
	}

	protected override void Dispose(bool disposing)
	{
		base.Dispose(disposing);
		_dalamudContextMenu.OnMenuOpened -= DalamudContextMenuOnOnOpenGameObjectContextMenu;
		DisposePairs();
	}

	private void DalamudContextMenuOnOnOpenGameObjectContextMenu(IMenuOpenedArgs args)
	{
		if (args.MenuType == ContextMenuType.Inventory || !_configurationService.Current.EnableRightClickMenus)
		{
			return;
		}
		foreach (KeyValuePair<UserData, Pair> item in _allClientPairs.Where<KeyValuePair<UserData, Pair>>((KeyValuePair<UserData, Pair> p) => p.Value.IsVisible))
		{
			item.Value.AddContextMenu(args);
		}
	}

	private Lazy<List<Pair>> DirectPairsLazy()
	{
		return new Lazy<List<Pair>>(() => (from k in _allClientPairs
			select k.Value into k
			where k.IndividualPairStatus != IndividualPairStatus.None
			select k).ToList());
	}

	private void DisposePairs()
	{
		base.Logger.LogDebug("Disposing all Pairs");
		Parallel.ForEach<KeyValuePair<UserData, Pair>>(_allClientPairs, delegate(KeyValuePair<UserData, Pair> item)
		{
			item.Value.MarkOffline(wait: false);
		});
		RecreateLazy();
	}

	private Lazy<Dictionary<GroupFullInfoDto, List<Pair>>> GroupPairsLazy()
	{
		return new Lazy<Dictionary<GroupFullInfoDto, List<Pair>>>(delegate
		{
			Dictionary<GroupFullInfoDto, List<Pair>> dictionary = new Dictionary<GroupFullInfoDto, List<Pair>>();
			foreach (KeyValuePair<GroupData, GroupFullInfoDto> group in _allGroups)
			{
				dictionary[group.Value] = (from p in _allClientPairs
					select p.Value into p
					where p.UserPair.Groups.Exists((string g) => GroupDataComparer.Instance.Equals(@group.Key, new GroupData(g)))
					select p).ToList();
			}
			return dictionary;
		});
	}

	private Lazy<Dictionary<Pair, List<GroupFullInfoDto>>> PairsWithGroupsLazy()
	{
		return new Lazy<Dictionary<Pair, List<GroupFullInfoDto>>>(delegate
		{
			Dictionary<Pair, List<GroupFullInfoDto>> dictionary = new Dictionary<Pair, List<GroupFullInfoDto>>();
			foreach (Pair pair in _allClientPairs.Select<KeyValuePair<UserData, Pair>, Pair>((KeyValuePair<UserData, Pair> k) => k.Value))
			{
				dictionary[pair] = (from k in _allGroups
					where pair.UserPair.Groups.Contains<string>(k.Key.GID, StringComparer.Ordinal)
					select k.Value).ToList();
			}
			return dictionary;
		});
	}

	private void ReapplyPairData()
	{
		foreach (Pair item in _allClientPairs.Select<KeyValuePair<UserData, Pair>, Pair>((KeyValuePair<UserData, Pair> k) => k.Value))
		{
			item.ApplyLastReceivedData(forced: true);
		}
	}

	private void RecreateLazy()
	{
		_directPairsInternal = DirectPairsLazy();
		_groupPairsInternal = GroupPairsLazy();
		_pairsWithGroupsInternal = PairsWithGroupsLazy();
		base.Mediator.Publish(new RefreshUiMessage());
	}
}
