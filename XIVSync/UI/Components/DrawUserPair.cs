using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using XIVSync.API.Data.Enum;
using XIVSync.API.Data.Extensions;
using XIVSync.API.Dto.Group;
using XIVSync.API.Dto.User;
using XIVSync.MareConfiguration;
using XIVSync.PlayerData.Pairs;
using XIVSync.Services;
using XIVSync.Services.CharaData.Models;
using XIVSync.Services.Mediator;
using XIVSync.Services.ServerConfiguration;
using XIVSync.UI.Handlers;
using XIVSync.WebAPI;

namespace XIVSync.UI.Components;

public class DrawUserPair
{
	protected readonly ApiController _apiController;

	protected readonly IdDisplayHandler _displayHandler;

	protected readonly MareMediator _mediator;

	protected readonly List<GroupFullInfoDto> _syncedGroups;

	private readonly GroupFullInfoDto? _currentGroup;

	protected Pair _pair;

	private readonly string _id;

	private readonly SelectTagForPairUi _selectTagForPairUi;

	private readonly ServerConfigurationManager _serverConfigurationManager;

	private readonly UiSharedService _uiSharedService;

	private readonly PlayerPerformanceConfigService _performanceConfigService;

	private readonly CharaDataManager _charaDataManager;

	private readonly MareConfigService _configService;

	private readonly MareProfileManager _profileManager;

	private float _menuWidth = -1f;

	private bool _wasHovered;

	public Pair Pair => _pair;

	public UserFullPairDto UserPair => _pair.UserPair;

	public DrawUserPair(string id, Pair entry, List<GroupFullInfoDto> syncedGroups, GroupFullInfoDto? currentGroup, ApiController apiController, IdDisplayHandler uIDDisplayHandler, MareMediator mareMediator, SelectTagForPairUi selectTagForPairUi, ServerConfigurationManager serverConfigurationManager, UiSharedService uiSharedService, PlayerPerformanceConfigService performanceConfigService, CharaDataManager charaDataManager, MareConfigService configService, MareProfileManager profileManager)
	{
		_id = id;
		_pair = entry;
		_syncedGroups = syncedGroups;
		_currentGroup = currentGroup;
		_apiController = apiController;
		_displayHandler = uIDDisplayHandler;
		_mediator = mareMediator;
		_selectTagForPairUi = selectTagForPairUi;
		_serverConfigurationManager = serverConfigurationManager;
		_uiSharedService = uiSharedService;
		_performanceConfigService = performanceConfigService;
		_charaDataManager = charaDataManager;
		_configService = configService;
		_profileManager = profileManager;
	}

	private ThemePalette GetCurrentTheme()
	{
		return _uiSharedService.GetCurrentTheme() ?? new ThemePalette();
	}

	private float GetRequiredHeight()
	{
		float baseHeight = ImGui.GetFrameHeight();
		if (_configService.Current.ShowProfileStatusInPairList && HasStatusMessage())
		{
			return baseHeight + ImGui.GetTextLineHeight() + ImGui.GetStyle().ItemSpacing.Y;
		}
		return baseHeight;
	}

	private bool HasStatusMessage()
	{
		if (!_configService.Current.ShowProfileStatusInPairList)
		{
			return false;
		}
		MareProfileData profile = _profileManager.GetMareProfile(_pair.UserData);
		if (profile.Description == "Loading Data from server..." || profile.Description == "-- User has no description set --")
		{
			return false;
		}
		string[] lines = profile.Description.Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
		if (lines.Length == 0)
		{
			return false;
		}
		string firstLine = lines[0].Trim();
		if (string.IsNullOrEmpty(firstLine))
		{
			return false;
		}
		if (firstLine.StartsWith("STATUS:"))
		{
			return !string.IsNullOrEmpty(firstLine.Substring(7).Trim());
		}
		return false;
	}

	public void DrawPairedClient()
	{
		using (ImRaii.PushId(GetType()?.ToString() + _id))
		{
			float height = GetRequiredHeight();
			using (ImRaii.Child(GetType()?.ToString() + _id, new Vector2(UiSharedService.GetWindowContentRegionWidth() - ImGui.GetCursorPosX(), height)))
			{
				DrawLeftSide();
				ImGui.SameLine();
				float posX = ImGui.GetCursorPosX();
				float rightSide = DrawRightSide();
				DrawName(posX, rightSide);
			}
			_wasHovered = ImGui.IsItemHovered();
		}
	}

	private void DrawCommonClientMenu()
	{
		if (!_pair.IsPaused)
		{
			if (_uiSharedService.IconTextButton(FontAwesomeIcon.User, "Open Profile", _menuWidth, isInPopup: true))
			{
				_displayHandler.OpenProfile(_pair);
				ImGui.CloseCurrentPopup();
			}
			UiSharedService.AttachToolTip("Opens the profile for this user in a new window");
		}
		if (_pair.IsVisible)
		{
			if (_uiSharedService.IconTextButton(FontAwesomeIcon.Sync, "Reload last data", _menuWidth, isInPopup: true))
			{
				_pair.ApplyLastReceivedData(forced: true);
				ImGui.CloseCurrentPopup();
			}
			UiSharedService.AttachToolTip("This reapplies the last received character data to this character");
		}
		if (_uiSharedService.IconTextButton(FontAwesomeIcon.PlayCircle, "Cycle pause state", _menuWidth, isInPopup: true))
		{
			_apiController.CyclePauseAsync(_pair.UserData);
			ImGui.CloseCurrentPopup();
		}
		ImGui.Separator();
		ImGui.TextUnformatted("Pair Permission Functions");
		if (_uiSharedService.IconTextButton(FontAwesomeIcon.WindowMaximize, "Open Permissions Window", _menuWidth, isInPopup: true))
		{
			_mediator.Publish(new OpenPermissionWindow(_pair));
			ImGui.CloseCurrentPopup();
		}
		UiSharedService.AttachToolTip("Opens the Permissions Window which allows you to manage multiple permissions at once.");
		bool isSticky = _pair.UserPair.OwnPermissions.IsSticky();
		string stickyText = (isSticky ? "Disable Preferred Permissions" : "Enable Preferred Permissions");
		FontAwesomeIcon stickyIcon = (isSticky ? FontAwesomeIcon.ArrowCircleDown : FontAwesomeIcon.ArrowCircleUp);
		if (_uiSharedService.IconTextButton(stickyIcon, stickyText, _menuWidth, isInPopup: true))
		{
			UserPermissions permissions = _pair.UserPair.OwnPermissions;
			permissions.SetSticky(!isSticky);
			_apiController.UserSetPairPermissions(new UserPermissionsDto(_pair.UserData, permissions));
		}
		UiSharedService.AttachToolTip("Preferred permissions means that this pair will not" + Environment.NewLine + " be affected by any syncshell permission changes through you.");
		string individualText = Environment.NewLine + Environment.NewLine + "Note: changing this permission will turn the permissions for this" + Environment.NewLine + "user to preferred permissions. You can change this behavior" + Environment.NewLine + "in the permission settings.";
		bool individual = !_pair.IsDirectlyPaired && _apiController.DefaultPermissions.IndividualIsSticky;
		bool isDisableSounds = _pair.UserPair.OwnPermissions.IsDisableSounds();
		string disableSoundsText = (isDisableSounds ? "Enable sound sync" : "Disable sound sync");
		FontAwesomeIcon disableSoundsIcon = (isDisableSounds ? FontAwesomeIcon.VolumeUp : FontAwesomeIcon.VolumeMute);
		if (_uiSharedService.IconTextButton(disableSoundsIcon, disableSoundsText, _menuWidth, isInPopup: true))
		{
			UserPermissions permissions2 = _pair.UserPair.OwnPermissions;
			permissions2.SetDisableSounds(!isDisableSounds);
			_apiController.UserSetPairPermissions(new UserPermissionsDto(_pair.UserData, permissions2));
		}
		UiSharedService.AttachToolTip("Changes sound sync permissions with this user." + (individual ? individualText : string.Empty));
		bool isDisableAnims = _pair.UserPair.OwnPermissions.IsDisableAnimations();
		string disableAnimsText = (isDisableAnims ? "Enable animation sync" : "Disable animation sync");
		FontAwesomeIcon disableAnimsIcon = (isDisableAnims ? FontAwesomeIcon.Running : FontAwesomeIcon.Stop);
		if (_uiSharedService.IconTextButton(disableAnimsIcon, disableAnimsText, _menuWidth, isInPopup: true))
		{
			UserPermissions permissions3 = _pair.UserPair.OwnPermissions;
			permissions3.SetDisableAnimations(!isDisableAnims);
			_apiController.UserSetPairPermissions(new UserPermissionsDto(_pair.UserData, permissions3));
		}
		UiSharedService.AttachToolTip("Changes animation sync permissions with this user." + (individual ? individualText : string.Empty));
		bool isDisableVFX = _pair.UserPair.OwnPermissions.IsDisableVFX();
		string disableVFXText = (isDisableVFX ? "Enable VFX sync" : "Disable VFX sync");
		FontAwesomeIcon disableVFXIcon = (isDisableVFX ? FontAwesomeIcon.Sun : FontAwesomeIcon.Circle);
		if (_uiSharedService.IconTextButton(disableVFXIcon, disableVFXText, _menuWidth, isInPopup: true))
		{
			UserPermissions permissions4 = _pair.UserPair.OwnPermissions;
			permissions4.SetDisableVFX(!isDisableVFX);
			_apiController.UserSetPairPermissions(new UserPermissionsDto(_pair.UserData, permissions4));
		}
		UiSharedService.AttachToolTip("Changes VFX sync permissions with this user." + (individual ? individualText : string.Empty));
	}

	private void DrawIndividualMenu()
	{
		ImGui.TextUnformatted("Individual Pair Functions");
		string entryUID = _pair.UserData.AliasOrUID;
		if (_pair.IndividualPairStatus != IndividualPairStatus.None)
		{
			if (_uiSharedService.IconTextButton(FontAwesomeIcon.Folder, "Pair Groups", _menuWidth, isInPopup: true))
			{
				_selectTagForPairUi.Open(_pair);
			}
			UiSharedService.AttachToolTip("Choose pair groups for " + entryUID);
			if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Unpair Permanently", _menuWidth, isInPopup: true) && UiSharedService.CtrlPressed())
			{
				_apiController.UserRemovePair(new UserDto(_pair.UserData));
			}
			UiSharedService.AttachToolTip("Hold CTRL and click to unpair permanently from " + entryUID);
		}
		else
		{
			if (_uiSharedService.IconTextButton(FontAwesomeIcon.Plus, "Pair individually", _menuWidth, isInPopup: true))
			{
				_apiController.UserAddPair(new UserDto(_pair.UserData));
			}
			UiSharedService.AttachToolTip("Pair individually with " + entryUID);
		}
	}

	private void DrawLeftSide()
	{
		string userPairText = string.Empty;
		ImGui.AlignTextToFramePadding();
		if (_pair.IsPaused)
		{
			using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudYellow))
			{
				_uiSharedService.IconText(FontAwesomeIcon.PauseCircle);
				userPairText = _pair.UserData.AliasOrUID + " is paused";
			}
		}
		else if (!_pair.IsOnline)
		{
			using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudRed))
			{
				_uiSharedService.IconText((_pair.IndividualPairStatus == IndividualPairStatus.OneSided) ? FontAwesomeIcon.ArrowsLeftRight : ((_pair.IndividualPairStatus == IndividualPairStatus.Bidirectional) ? FontAwesomeIcon.User : FontAwesomeIcon.Users));
				userPairText = _pair.UserData.AliasOrUID + " is offline";
			}
		}
		else if (_pair.IsVisible)
		{
			_uiSharedService.IconText(FontAwesomeIcon.Eye, ImGuiColors.ParsedGreen);
			userPairText = _pair.UserData.AliasOrUID + " is visible: " + _pair.PlayerName + Environment.NewLine + "Click to target this player";
			if (ImGui.IsItemClicked())
			{
				_mediator.Publish(new TargetPairMessage(_pair));
			}
		}
		else
		{
			using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.HealerGreen))
			{
				_uiSharedService.IconText((_pair.IndividualPairStatus == IndividualPairStatus.Bidirectional) ? FontAwesomeIcon.User : FontAwesomeIcon.Users);
				userPairText = _pair.UserData.AliasOrUID + " is online";
			}
		}
		if (_pair.IndividualPairStatus == IndividualPairStatus.OneSided)
		{
			userPairText += "--SEP--User has not added you back";
		}
		else if (_pair.IndividualPairStatus == IndividualPairStatus.Bidirectional)
		{
			userPairText += "--SEP--You are directly Paired";
		}
		if (_pair.LastAppliedDataBytes >= 0)
		{
			userPairText += "--SEP--";
			userPairText = userPairText + ((!_pair.IsPaired) ? "(Last) " : string.Empty) + "Mods Info" + Environment.NewLine;
			userPairText = userPairText + "Files Size: " + UiSharedService.ByteToString(_pair.LastAppliedDataBytes);
			if (_pair.LastAppliedApproximateVRAMBytes >= 0)
			{
				userPairText = userPairText + Environment.NewLine + "Approx. VRAM Usage: " + UiSharedService.ByteToString(_pair.LastAppliedApproximateVRAMBytes);
			}
			if (_pair.LastAppliedDataTris >= 0)
			{
				userPairText = userPairText + Environment.NewLine + "Approx. Triangle Count (excl. Vanilla): " + ((_pair.LastAppliedDataTris > 1000) ? ((double)_pair.LastAppliedDataTris / 1000.0).ToString("0.0'k'") : ((object)_pair.LastAppliedDataTris));
			}
		}
		if (_syncedGroups.Any())
		{
			userPairText = userPairText + "--SEP--" + string.Join(Environment.NewLine, _syncedGroups.Select(delegate(GroupFullInfoDto g)
			{
				string noteForGid = _serverConfigurationManager.GetNoteForGid(g.GID);
				string text = (string.IsNullOrEmpty(noteForGid) ? g.GroupAliasOrGID : (noteForGid + " (" + g.GroupAliasOrGID + ")"));
				return "Paired through " + text;
			}));
		}
		UiSharedService.AttachToolTip(userPairText);
		if (_performanceConfigService.Current.ShowPerformanceIndicator && !_performanceConfigService.Current.UIDsToIgnore.Exists((string uid) => string.Equals(uid, UserPair.User.Alias, StringComparison.Ordinal) || string.Equals(uid, UserPair.User.UID, StringComparison.Ordinal)) && ((_performanceConfigService.Current.VRAMSizeWarningThresholdMiB > 0 && _performanceConfigService.Current.VRAMSizeWarningThresholdMiB * 1024 * 1024 < _pair.LastAppliedApproximateVRAMBytes) || (_performanceConfigService.Current.TrisWarningThresholdThousands > 0 && _performanceConfigService.Current.TrisWarningThresholdThousands * 1000 < _pair.LastAppliedDataTris)) && (!_pair.UserPair.OwnPermissions.IsSticky() || _performanceConfigService.Current.WarnOnPreferredPermissionsExceedingThresholds))
		{
			ImGui.SameLine();
			_uiSharedService.IconText(FontAwesomeIcon.ExclamationTriangle, ImGuiColors.DalamudYellow);
			string userWarningText = "WARNING: This user exceeds one or more of your defined thresholds:--SEP--";
			bool shownVram = false;
			if (_performanceConfigService.Current.VRAMSizeWarningThresholdMiB > 0 && _performanceConfigService.Current.VRAMSizeWarningThresholdMiB * 1024 * 1024 < _pair.LastAppliedApproximateVRAMBytes)
			{
				shownVram = true;
				userWarningText += $"Approx. VRAM Usage: Used: {UiSharedService.ByteToString(_pair.LastAppliedApproximateVRAMBytes)}, Threshold: {_performanceConfigService.Current.VRAMSizeWarningThresholdMiB} MiB";
			}
			if (_performanceConfigService.Current.TrisWarningThresholdThousands > 0 && _performanceConfigService.Current.TrisWarningThresholdThousands * 1024 < _pair.LastAppliedDataTris)
			{
				if (shownVram)
				{
					userWarningText += Environment.NewLine;
				}
				userWarningText += $"Approx. Triangle count: Used: {_pair.LastAppliedDataTris}, Threshold: {_performanceConfigService.Current.TrisWarningThresholdThousands * 1000}";
			}
			UiSharedService.AttachToolTip(userWarningText);
		}
		ImGui.SameLine();
	}

	private void DrawName(float leftSide, float rightSide)
	{
		_displayHandler.DrawPairText(_id, _pair, leftSide, () => rightSide - leftSide);
	}

	private void DrawPairedClientMenu()
	{
		DrawIndividualMenu();
		if (_syncedGroups.Any())
		{
			ImGui.Separator();
		}
		foreach (GroupFullInfoDto entry in _syncedGroups)
		{
			bool selfIsOwner = string.Equals(_apiController.UID, entry.Owner.UID, StringComparison.Ordinal);
			bool selfIsModerator = entry.GroupUserInfo.IsModerator();
			GroupPairUserInfo modinfo;
			bool userIsModerator = entry.GroupPairUserInfos.TryGetValue(_pair.UserData.UID, out modinfo) && modinfo.IsModerator();
			GroupPairUserInfo info;
			bool userIsPinned = entry.GroupPairUserInfos.TryGetValue(_pair.UserData.UID, out info) && info.IsPinned();
			if (selfIsOwner || selfIsModerator)
			{
				string groupNote = _serverConfigurationManager.GetNoteForGid(entry.GID);
				if (ImGui.BeginMenu((string.IsNullOrEmpty(groupNote) ? entry.GroupAliasOrGID : (groupNote + " (" + entry.GroupAliasOrGID + ")")) + " Moderation Functions"))
				{
					DrawSyncshellMenu(entry, selfIsOwner, selfIsModerator, userIsPinned, userIsModerator);
					ImGui.EndMenu();
				}
			}
		}
	}

	private float DrawRightSide()
	{
		FontAwesomeIcon pauseIcon = (_pair.UserPair.OwnPermissions.IsPaused() ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause);
		Vector2 pauseButtonSize = _uiSharedService.GetIconButtonSize(pauseIcon);
		Vector2 barButtonSize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.EllipsisV);
		float spacingX = ImGui.GetStyle().ItemSpacing.X;
		float currentRightSide = ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth() - barButtonSize.X;
		ImGui.SameLine(currentRightSide);
		ImGui.AlignTextToFramePadding();
		if (_uiSharedService.IconButton(FontAwesomeIcon.EllipsisV))
		{
			ImGui.OpenPopup("User Flyout Menu");
		}
		currentRightSide -= pauseButtonSize.X + spacingX;
		ImGui.SameLine(currentRightSide);
		if (_uiSharedService.IconButton(pauseIcon))
		{
			UserPermissions perm = _pair.UserPair.OwnPermissions;
			if (UiSharedService.CtrlPressed() && !perm.IsPaused())
			{
				perm.SetSticky(sticky: true);
			}
			perm.SetPaused(!perm.IsPaused());
			_apiController.UserSetPairPermissions(new UserPermissionsDto(_pair.UserData, perm));
		}
		UiSharedService.AttachToolTip((!_pair.UserPair.OwnPermissions.IsPaused()) ? ("Pause pairing with " + _pair.UserData.AliasOrUID + (_pair.UserPair.OwnPermissions.IsSticky() ? string.Empty : ("--SEP--Hold CTRL to enable preferred permissions while pausing." + Environment.NewLine + "This will leave this pair paused even if unpausing syncshells including this pair."))) : ("Resume pairing with " + _pair.UserData.AliasOrUID));
		if (_pair.IsPaired)
		{
			UserFullPairDto userPair = _pair.UserPair;
			bool individualSoundsDisabled = ((object)userPair != null && userPair.OwnPermissions.IsDisableSounds()) || (_pair.UserPair?.OtherPermissions.IsDisableSounds() ?? false);
			UserFullPairDto userPair2 = _pair.UserPair;
			bool individualAnimDisabled = ((object)userPair2 != null && userPair2.OwnPermissions.IsDisableAnimations()) || (_pair.UserPair?.OtherPermissions.IsDisableAnimations() ?? false);
			UserFullPairDto userPair3 = _pair.UserPair;
			bool individualVFXDisabled = ((object)userPair3 != null && userPair3.OwnPermissions.IsDisableVFX()) || (_pair.UserPair?.OtherPermissions.IsDisableVFX() ?? false);
			bool individualIsSticky = _pair.UserPair.OwnPermissions.IsSticky();
			FontAwesomeIcon individualIcon = (individualIsSticky ? FontAwesomeIcon.ArrowCircleUp : FontAwesomeIcon.InfoCircle);
			if (individualAnimDisabled || individualSoundsDisabled || individualVFXDisabled || individualIsSticky)
			{
				currentRightSide -= _uiSharedService.GetIconSize(individualIcon).X + spacingX;
				ImGui.SameLine(currentRightSide);
				using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudYellow, individualAnimDisabled || individualSoundsDisabled || individualVFXDisabled))
				{
					_uiSharedService.IconText(individualIcon);
				}
				if (ImGui.IsItemHovered())
				{
					ImGui.BeginTooltip();
					ImGui.TextUnformatted("Individual User permissions");
					ImGui.Separator();
					if (individualIsSticky)
					{
						_uiSharedService.IconText(individualIcon);
						ImGui.SameLine(40f * ImGuiHelpers.GlobalScale);
						ImGui.AlignTextToFramePadding();
						ImGui.TextUnformatted("Preferred permissions enabled");
						if (individualAnimDisabled || individualSoundsDisabled || individualVFXDisabled)
						{
							ImGui.Separator();
						}
					}
					if (individualSoundsDisabled)
					{
						_uiSharedService.IconText(FontAwesomeIcon.VolumeOff);
						ImGui.SameLine(40f * ImGuiHelpers.GlobalScale);
						ImGui.AlignTextToFramePadding();
						ImGui.TextUnformatted("Sound sync");
						ImGui.NewLine();
						ImGui.SameLine(40f * ImGuiHelpers.GlobalScale);
						ImGui.AlignTextToFramePadding();
						ImGui.TextUnformatted("You");
						_uiSharedService.BooleanToColoredIcon(!_pair.UserPair.OwnPermissions.IsDisableSounds());
						ImGui.SameLine();
						ImGui.AlignTextToFramePadding();
						ImGui.TextUnformatted("They");
						_uiSharedService.BooleanToColoredIcon(!_pair.UserPair.OtherPermissions.IsDisableSounds());
					}
					if (individualAnimDisabled)
					{
						_uiSharedService.IconText(FontAwesomeIcon.Stop);
						ImGui.SameLine(40f * ImGuiHelpers.GlobalScale);
						ImGui.AlignTextToFramePadding();
						ImGui.TextUnformatted("Animation sync");
						ImGui.NewLine();
						ImGui.SameLine(40f * ImGuiHelpers.GlobalScale);
						ImGui.AlignTextToFramePadding();
						ImGui.TextUnformatted("You");
						_uiSharedService.BooleanToColoredIcon(!_pair.UserPair.OwnPermissions.IsDisableAnimations());
						ImGui.SameLine();
						ImGui.AlignTextToFramePadding();
						ImGui.TextUnformatted("They");
						_uiSharedService.BooleanToColoredIcon(!_pair.UserPair.OtherPermissions.IsDisableAnimations());
					}
					if (individualVFXDisabled)
					{
						_uiSharedService.IconText(FontAwesomeIcon.Circle);
						ImGui.SameLine(40f * ImGuiHelpers.GlobalScale);
						ImGui.AlignTextToFramePadding();
						ImGui.TextUnformatted("VFX sync");
						ImGui.NewLine();
						ImGui.SameLine(40f * ImGuiHelpers.GlobalScale);
						ImGui.AlignTextToFramePadding();
						ImGui.TextUnformatted("You");
						_uiSharedService.BooleanToColoredIcon(!_pair.UserPair.OwnPermissions.IsDisableVFX());
						ImGui.SameLine();
						ImGui.AlignTextToFramePadding();
						ImGui.TextUnformatted("They");
						_uiSharedService.BooleanToColoredIcon(!_pair.UserPair.OtherPermissions.IsDisableVFX());
					}
					ImGui.EndTooltip();
				}
			}
		}
		if (_charaDataManager.SharedWithYouData.TryGetValue(_pair.UserData, out List<CharaDataMetaInfoExtendedDto> sharedData))
		{
			currentRightSide -= _uiSharedService.GetIconSize(FontAwesomeIcon.Running).X + spacingX / 2f;
			ImGui.SameLine(currentRightSide);
			_uiSharedService.IconText(FontAwesomeIcon.Running);
			UiSharedService.AttachToolTip($"This user has shared {sharedData.Count} Character Data Sets with you." + "--SEP--Click to open the Character Data Hub and show the entries.");
			if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
			{
				_mediator.Publish(new OpenCharaDataHubWithFilterMessage(_pair.UserData));
			}
		}
		if (_currentGroup != null)
		{
			FontAwesomeIcon icon = FontAwesomeIcon.None;
			string text = string.Empty;
			GroupPairUserInfo userinfo;
			if (string.Equals(_currentGroup.OwnerUID, _pair.UserData.UID, StringComparison.Ordinal))
			{
				icon = FontAwesomeIcon.Crown;
				text = "User is owner of this syncshell";
			}
			else if (_currentGroup.GroupPairUserInfos.TryGetValue(_pair.UserData.UID, out userinfo))
			{
				if (userinfo.IsModerator())
				{
					icon = FontAwesomeIcon.UserShield;
					text = "User is moderator in this syncshell";
				}
				else if (userinfo.IsPinned())
				{
					icon = FontAwesomeIcon.Thumbtack;
					text = "User is pinned in this syncshell";
				}
			}
			if (!string.IsNullOrEmpty(text))
			{
				currentRightSide -= _uiSharedService.GetIconSize(icon).X + spacingX;
				ImGui.SameLine(currentRightSide);
				_uiSharedService.IconText(icon);
				UiSharedService.AttachToolTip(text);
			}
		}
		if (ImGui.BeginPopup("User Flyout Menu"))
		{
			ThemePalette theme = GetCurrentTheme();
			using (ImRaii.PushColor(ImGuiCol.PopupBg, theme.TooltipBg))
			{
				using (ImRaii.PushColor(ImGuiCol.Text, theme.TooltipText))
				{
					ImU8String id = new ImU8String(8, 1);
					id.AppendLiteral("buttons-");
					id.AppendFormatted(_pair.UserData.UID);
					using (ImRaii.PushId(id))
					{
						ImGui.TextUnformatted("Common Pair Functions");
						DrawCommonClientMenu();
						ImGui.Separator();
						DrawPairedClientMenu();
						if (_menuWidth <= 0f)
						{
							_menuWidth = ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;
						}
					}
					ImGui.EndPopup();
				}
			}
		}
		return currentRightSide - spacingX;
	}

	private void DrawSyncshellMenu(GroupFullInfoDto group, bool selfIsOwner, bool selfIsModerator, bool userIsPinned, bool userIsModerator)
	{
		if (selfIsOwner || (selfIsModerator && !userIsModerator))
		{
			ImGui.TextUnformatted("Syncshell Moderator Functions");
			string pinText = (userIsPinned ? "Unpin user" : "Pin user");
			if (_uiSharedService.IconTextButton(FontAwesomeIcon.Thumbtack, pinText, _menuWidth, isInPopup: true))
			{
				ImGui.CloseCurrentPopup();
				if (!group.GroupPairUserInfos.TryGetValue(_pair.UserData.UID, out var userinfo))
				{
					userinfo = GroupPairUserInfo.IsPinned;
				}
				else
				{
					userinfo.SetPinned(!userinfo.IsPinned());
				}
				_apiController.GroupSetUserInfo(new GroupPairUserInfoDto(group.Group, _pair.UserData, userinfo));
			}
			UiSharedService.AttachToolTip("Pin this user to the Syncshell. Pinned users will not be deleted in case of a manually initiated Syncshell clean");
			if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Remove user", _menuWidth, isInPopup: true) && UiSharedService.CtrlPressed())
			{
				ImGui.CloseCurrentPopup();
				_apiController.GroupRemoveUser(new GroupPairDto(group.Group, _pair.UserData));
			}
			UiSharedService.AttachToolTip("Hold CTRL and click to remove user " + _pair.UserData.AliasOrUID + " from Syncshell");
			if (_uiSharedService.IconTextButton(FontAwesomeIcon.UserSlash, "Ban User", _menuWidth, isInPopup: true))
			{
				_mediator.Publish(new OpenBanUserPopupMessage(_pair, group));
				ImGui.CloseCurrentPopup();
			}
			UiSharedService.AttachToolTip("Ban user from this Syncshell");
			ImGui.Separator();
		}
		if (!selfIsOwner)
		{
			return;
		}
		ImGui.TextUnformatted("Syncshell Owner Functions");
		string modText = (userIsModerator ? "Demod user" : "Mod user");
		if (_uiSharedService.IconTextButton(FontAwesomeIcon.UserShield, modText, _menuWidth, isInPopup: true) && UiSharedService.CtrlPressed())
		{
			ImGui.CloseCurrentPopup();
			if (!group.GroupPairUserInfos.TryGetValue(_pair.UserData.UID, out var userinfo2))
			{
				userinfo2 = GroupPairUserInfo.IsModerator;
			}
			else
			{
				userinfo2.SetModerator(!userinfo2.IsModerator());
			}
			_apiController.GroupSetUserInfo(new GroupPairUserInfoDto(group.Group, _pair.UserData, userinfo2));
		}
		UiSharedService.AttachToolTip("Hold CTRL to change the moderator status for " + _pair.UserData.AliasOrUID + Environment.NewLine + "Moderators can kick, ban/unban, pin/unpin users and clear the Syncshell.");
		if (_uiSharedService.IconTextButton(FontAwesomeIcon.Crown, "Transfer Ownership", _menuWidth, isInPopup: true) && UiSharedService.CtrlPressed() && UiSharedService.ShiftPressed())
		{
			ImGui.CloseCurrentPopup();
			_apiController.GroupChangeOwnership(new GroupPairDto(group.Group, _pair.UserData));
		}
		UiSharedService.AttachToolTip("Hold CTRL and SHIFT and click to transfer ownership of this Syncshell to " + _pair.UserData.AliasOrUID + Environment.NewLine + "WARNING: This action is irreversible.");
	}
}
