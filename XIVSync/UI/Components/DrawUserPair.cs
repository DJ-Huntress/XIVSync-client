using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using XIVSync.API.Data.Enum;
using XIVSync.API.Data.Extensions;
using XIVSync.API.Dto.Group;
using XIVSync.API.Dto.User;
using XIVSync.MareConfiguration;
using XIVSync.PlayerData.Pairs;
using XIVSync.Services;
using XIVSync.Services.Mediator;
using XIVSync.Services.ServerConfiguration;
using XIVSync.UI.Handlers;
using XIVSync.UI.Theming;
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

	private float _hoverTransition;

	private float _pulsePhase;

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

	public void DrawPairedClient(Vector4? folderColor = null)
	{
		using (ImRaii.PushId(GetType()?.ToString() + _id))
		{
			float deltaTime = ImGui.GetIO().DeltaTime;
			float targetHover = (ImGui.IsItemHovered() ? 1f : 0f);
			_hoverTransition += (targetHover - _hoverTransition) * deltaTime * 8f;
			_hoverTransition = Math.Clamp(_hoverTransition, 0f, 1f);
			_pulsePhase += deltaTime * 2f;
			float height = GetRequiredHeight() + 8f;
			float windowWidth = UiSharedService.GetWindowContentRegionWidth() - ImGui.GetCursorPosX();
			DrawCyberpunkCard(height, windowWidth, folderColor);
			using (ImRaii.Child(GetType()?.ToString() + _id, new Vector2(windowWidth, height), border: false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoBackground))
			{
				ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 4f);
				DrawLeftSide(folderColor);
				ImGui.SameLine();
				float posX = ImGui.GetCursorPosX();
				float rightSide = GetWindowEndX();
				DrawName(posX, rightSide);
			}
			if ((_wasHovered = ImGui.IsItemHovered()) && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
			{
				ImGui.OpenPopup("User Context Menu");
			}
			using ImRaii.IEndObject popup = ImRaii.Popup("User Context Menu");
			if (popup)
			{
				DrawContextMenu();
			}
		}
	}

	private float GetWindowEndX()
	{
		return ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth();
	}

	private void DrawCyberpunkCard(float height, float width, Vector4? folderColor = null)
	{
		ImDrawListPtr drawList = ImGui.GetWindowDrawList();
		Vector2 cursorPos = ImGui.GetCursorScreenPos();
		Vector2 cardMin = cursorPos;
		Vector2 cardMax = new Vector2(cursorPos.X + width, cursorPos.Y + height);
		ThemePalette theme = GetCurrentTheme();
		Vector4 accentColor = folderColor ?? GetPairStatusColor();
		float bgAlpha = 0.15f + _hoverTransition * 0.1f;
		drawList.AddRectFilled(col: ImGui.ColorConvertFloat4ToU32(new Vector4(theme.PanelBg.X + accentColor.X * 0.05f, theme.PanelBg.Y + accentColor.Y * 0.05f, theme.PanelBg.Z + accentColor.Z * 0.05f, bgAlpha)), pMin: cardMin, pMax: cardMax, rounding: 3f);
		float pulseIntensity = 0.5f + MathF.Sin(_pulsePhase) * 0.2f;
		float borderAlpha = 0.3f + _hoverTransition * 0.5f * pulseIntensity;
		Vector4 borderColor = new Vector4(accentColor.X, accentColor.Y, accentColor.Z, borderAlpha);
		if (_hoverTransition > 0.01f)
		{
			drawList.AddRect(col: ImGui.ColorConvertFloat4ToU32(new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.2f * _hoverTransition)), pMin: new Vector2(cardMin.X - 1f, cardMin.Y - 1f), pMax: new Vector2(cardMax.X + 1f, cardMax.Y + 1f), rounding: 3f, flags: ImDrawFlags.None, thickness: 2f);
		}
		drawList.AddRect(cardMin, cardMax, ImGui.ColorConvertFloat4ToU32(borderColor), 3f, ImDrawFlags.None, 1.5f);
		float lineHeight = height * 0.6f;
		float lineY = cursorPos.Y + (height - lineHeight) * 0.5f;
		drawList.AddRectFilled(col: ImGui.ColorConvertFloat4ToU32(new Vector4(accentColor.X * pulseIntensity, accentColor.Y * pulseIntensity, accentColor.Z * pulseIntensity, 0.8f)), pMin: new Vector2(cardMin.X + 2f, lineY), pMax: new Vector2(cardMin.X + 4f, lineY + lineHeight));
	}

	private Vector4 GetPairStatusColor()
	{
		GetCurrentTheme();
		if (_pair.IsPaused)
		{
			return new Vector4(1f, 0.8f, 0f, 1f);
		}
		if (!_pair.IsOnline)
		{
			return new Vector4(0.9f, 0.2f, 0.2f, 1f);
		}
		if (_pair.IsVisible)
		{
			return new Vector4(0f, 1f, 0.5f, 1f);
		}
		return new Vector4(0f, 0.9f, 1f, 1f);
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
			UserPermissions permissions = _pair.UserPair.OwnPermissions;
			permissions.SetDisableSounds(!isDisableSounds);
			_apiController.UserSetPairPermissions(new UserPermissionsDto(_pair.UserData, permissions));
		}
		UiSharedService.AttachToolTip("Changes sound sync permissions with this user." + (individual ? individualText : string.Empty));
		bool isDisableAnims = _pair.UserPair.OwnPermissions.IsDisableAnimations();
		string disableAnimsText = (isDisableAnims ? "Enable animation sync" : "Disable animation sync");
		FontAwesomeIcon disableAnimsIcon = (isDisableAnims ? FontAwesomeIcon.Running : FontAwesomeIcon.Stop);
		if (_uiSharedService.IconTextButton(disableAnimsIcon, disableAnimsText, _menuWidth, isInPopup: true))
		{
			UserPermissions permissions = _pair.UserPair.OwnPermissions;
			permissions.SetDisableAnimations(!isDisableAnims);
			_apiController.UserSetPairPermissions(new UserPermissionsDto(_pair.UserData, permissions));
		}
		UiSharedService.AttachToolTip("Changes animation sync permissions with this user." + (individual ? individualText : string.Empty));
		bool isDisableVFX = _pair.UserPair.OwnPermissions.IsDisableVFX();
		string disableVFXText = (isDisableVFX ? "Enable VFX sync" : "Disable VFX sync");
		FontAwesomeIcon disableVFXIcon = (isDisableVFX ? FontAwesomeIcon.Sun : FontAwesomeIcon.Circle);
		if (_uiSharedService.IconTextButton(disableVFXIcon, disableVFXText, _menuWidth, isInPopup: true))
		{
			UserPermissions permissions = _pair.UserPair.OwnPermissions;
			permissions.SetDisableVFX(!isDisableVFX);
			_apiController.UserSetPairPermissions(new UserPermissionsDto(_pair.UserData, permissions));
		}
		UiSharedService.AttachToolTip("Changes VFX sync permissions with this user." + (individual ? individualText : string.Empty));
	}

	private void DrawIndividualMenu()
	{
		ImGui.TextUnformatted("Individual Pair Functions");
		string entryUID = _pair.UserData.AliasOrUID;
		if (_pair.IndividualPairStatus != 0)
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

	private void DrawLeftSide(Vector4? folderColor = null)
	{
		string userPairText = string.Empty;
		ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8f);
		ImGui.AlignTextToFramePadding();
		Vector2 iconPos = ImGui.GetCursorScreenPos();
		Vector2 iconSize = ImGui.CalcTextSize(FontAwesomeIcon.Eye.ToIconString());
		float circleRadius = iconSize.X * 0.8f;
		Vector2 circleCenter = new Vector2(iconPos.X + circleRadius, iconPos.Y + iconSize.Y * 0.5f);
		Vector4 statusColor = folderColor ?? GetPairStatusColor();
		ImDrawListPtr drawList = ImGui.GetWindowDrawList();
		float pulseIntensity = 0.7f + MathF.Sin(_pulsePhase * 1.5f) * 0.3f;
		Vector4 glowColor = new Vector4(statusColor.X * pulseIntensity, statusColor.Y * pulseIntensity, statusColor.Z * pulseIntensity, 0.2f * pulseIntensity);
		drawList.AddCircleFilled(circleCenter, circleRadius + 3f, ImGui.ColorConvertFloat4ToU32(glowColor), 16);
		Vector4 innerColor = new Vector4(statusColor.X * 0.3f, statusColor.Y * 0.3f, statusColor.Z * 0.3f, 0.4f);
		drawList.AddCircleFilled(circleCenter, circleRadius, ImGui.ColorConvertFloat4ToU32(innerColor), 16);
		if (_pair.IsPaused)
		{
			using (ImRaii.PushColor(ImGuiCol.Text, statusColor))
			{
				_uiSharedService.IconText(FontAwesomeIcon.PauseCircle);
				userPairText = _pair.UserData.AliasOrUID + " is paused";
			}
		}
		else if (!_pair.IsOnline)
		{
			using (ImRaii.PushColor(ImGuiCol.Text, statusColor))
			{
				_uiSharedService.IconText((_pair.IndividualPairStatus == IndividualPairStatus.OneSided) ? FontAwesomeIcon.ArrowsLeftRight : ((_pair.IndividualPairStatus == IndividualPairStatus.Bidirectional) ? FontAwesomeIcon.User : FontAwesomeIcon.Users));
				userPairText = _pair.UserData.AliasOrUID + " is offline";
			}
		}
		else if (_pair.IsVisible)
		{
			using (ImRaii.PushColor(ImGuiCol.Text, statusColor))
			{
				_uiSharedService.IconText(FontAwesomeIcon.Eye);
				userPairText = _pair.UserData.AliasOrUID + " is visible: " + _pair.PlayerName + Environment.NewLine + "Click to target this player";
				if (ImGui.IsItemClicked())
				{
					_mediator.Publish(new TargetPairMessage(_pair));
				}
			}
		}
		else
		{
			using (ImRaii.PushColor(ImGuiCol.Text, statusColor))
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

	private void DrawContextMenu()
	{
		FontAwesomeIcon pauseIcon = (_pair.UserPair.OwnPermissions.IsPaused() ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause);
		string pauseText = (_pair.UserPair.OwnPermissions.IsPaused() ? "Resume" : "Pause");
		if (_uiSharedService.IconTextButton(pauseIcon, pauseText, _menuWidth, isInPopup: true))
		{
			UserPermissions perm = _pair.UserPair.OwnPermissions;
			if (UiSharedService.CtrlPressed() && !perm.IsPaused())
			{
				perm.SetSticky(sticky: true);
			}
			perm.SetPaused(!perm.IsPaused());
			_apiController.UserSetPairPermissions(new UserPermissionsDto(_pair.UserData, perm));
			ImGui.CloseCurrentPopup();
		}
		ImGui.Separator();
		if (_pair.IsPaired)
		{
			UserFullPairDto userPair = _pair.UserPair;
			bool individualSoundsDisabled = ((object)userPair != null && userPair.OwnPermissions.IsDisableSounds()) || (_pair.UserPair?.OtherPermissions.IsDisableSounds() ?? false);
			UserFullPairDto userPair2 = _pair.UserPair;
			bool individualAnimDisabled = ((object)userPair2 != null && userPair2.OwnPermissions.IsDisableAnimations()) || (_pair.UserPair?.OtherPermissions.IsDisableAnimations() ?? false);
			UserFullPairDto userPair3 = _pair.UserPair;
			bool individualVFXDisabled = ((object)userPair3 != null && userPair3.OwnPermissions.IsDisableVFX()) || (_pair.UserPair?.OtherPermissions.IsDisableVFX() ?? false);
			bool individualIsSticky = _pair.UserPair.OwnPermissions.IsSticky();
			if (individualSoundsDisabled || individualAnimDisabled || individualVFXDisabled || individualIsSticky)
			{
				ImGui.TextUnformatted("Status:");
				if (individualSoundsDisabled)
				{
					ImGui.TextUnformatted("  • Sounds Disabled");
				}
				if (individualAnimDisabled)
				{
					ImGui.TextUnformatted("  • Animations Disabled");
				}
				if (individualVFXDisabled)
				{
					ImGui.TextUnformatted("  • VFX Disabled");
				}
				if (individualIsSticky)
				{
					ImGui.TextUnformatted("  • Preferred Permissions");
				}
				ImGui.Separator();
			}
		}
		DrawCommonClientMenu();
		ImGui.Separator();
		DrawIndividualMenu();
		if (_menuWidth <= 0f)
		{
			_menuWidth = ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;
		}
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
			if (!group.GroupPairUserInfos.TryGetValue(_pair.UserData.UID, out var userinfo))
			{
				userinfo = GroupPairUserInfo.IsModerator;
			}
			else
			{
				userinfo.SetModerator(!userinfo.IsModerator());
			}
			_apiController.GroupSetUserInfo(new GroupPairUserInfoDto(group.Group, _pair.UserData, userinfo));
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
