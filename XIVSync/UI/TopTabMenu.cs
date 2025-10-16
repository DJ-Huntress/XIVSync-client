using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using Microsoft.Extensions.Logging;
using XIVSync.API.Data;
using XIVSync.API.Data.Enum;
using XIVSync.API.Data.Extensions;
using XIVSync.API.Dto.Group;
using XIVSync.API.Dto.User;
using XIVSync.MareConfiguration;
using XIVSync.PlayerData.Pairs;
using XIVSync.Services.Mediator;
using XIVSync.UI.Theming;
using XIVSync.WebAPI;

namespace XIVSync.UI;

public class TopTabMenu
{
	private enum SelectedTab
	{
		None,
		Individual,
		Syncshell,
		Filter,
		UserConfig
	}

	private readonly ApiController _apiController;

	private readonly ILogger _logger;

	private readonly MareConfigService _mareConfigService;

	private readonly MareMediator _mareMediator;

	private readonly PairManager _pairManager;

	private readonly UiSharedService _uiSharedService;

	private readonly WindowMediatorSubscriberBase? _parentWindow;

	private string _filter = string.Empty;

	private int _globalControlCountdown;

	private string _pairToAdd = string.Empty;

	private SelectedTab _selectedTab;

	private float _rgbHue;

	private float _tabPulse;

	public string Filter
	{
		get
		{
			return _filter;
		}
		private set
		{
			if (!string.Equals(_filter, value, StringComparison.OrdinalIgnoreCase))
			{
				_mareMediator.Publish(new RefreshUiMessage());
			}
			_filter = value;
		}
	}

	private SelectedTab TabSelection
	{
		get
		{
			return _selectedTab;
		}
		set
		{
			if (_selectedTab == SelectedTab.Filter && value != SelectedTab.Filter)
			{
				Filter = string.Empty;
			}
			_selectedTab = value;
		}
	}

	public TopTabMenu(ILogger logger, MareMediator mareMediator, ApiController apiController, PairManager pairManager, UiSharedService uiSharedService, MareConfigService mareConfigService, WindowMediatorSubscriberBase? parentWindow = null)
	{
		_logger = logger;
		_mareMediator = mareMediator;
		_apiController = apiController;
		_pairManager = pairManager;
		_uiSharedService = uiSharedService;
		_mareConfigService = mareConfigService;
		_parentWindow = parentWindow;
		_logger.LogInformation("[Self-Mute] TopTabMenu initialized successfully");
	}

	private void AttachTooltip(string text, ThemePalette? theme)
	{
		if (theme != null)
		{
			UiSharedService.AttachThemedToolTip(text, theme);
		}
		else
		{
			UiSharedService.AttachToolTip(text);
		}
	}

	private void DrawCyberpunkTab(FontAwesomeIcon icon, Vector2 buttonSize, Vector2 spacing, SelectedTab tab, string tooltip, ImDrawListPtr drawList, ThemePalette? theme)
	{
		bool isSelected = TabSelection == tab;
		Vector2 cursorBefore = ImGui.GetCursorScreenPos();
		ThemePalette activeTheme = theme ?? new ThemePalette();
		if (isSelected)
		{
			ThemeEffects.DrawBackgroundGlow(cursorBefore, new Vector2(cursorBefore.X + buttonSize.X, cursorBefore.Y + buttonSize.Y), activeTheme, _tabPulse);
		}
		using (ImRaii.PushFont(UiBuilder.IconFont))
		{
			Vector2 x = ImGui.GetCursorScreenPos();
			if (ImGui.Button(icon.ToIconString(), buttonSize))
			{
				TabSelection = ((TabSelection != tab) ? tab : SelectedTab.None);
			}
			bool isHovered = ImGui.IsItemHovered();
			if (!isSelected && isHovered)
			{
				ThemeEffects.DrawHoverGlow(x, new Vector2(x.X + buttonSize.X, x.Y + buttonSize.Y), activeTheme, _rgbHue);
			}
			if (isSelected)
			{
				ThemeEffects.DrawPulsingBorder(x, new Vector2(x.X + buttonSize.X, x.Y + buttonSize.Y), activeTheme, _tabPulse);
				float lineY = x.Y + buttonSize.Y + spacing.Y;
				ThemeEffects.DrawAccentLine(new Vector2(x.X, lineY), new Vector2(x.X + buttonSize.X, lineY), activeTheme, _tabPulse);
			}
			else
			{
				ThemeEffects.DrawPulsingBorder(x, new Vector2(x.X + buttonSize.X, x.Y + buttonSize.Y), activeTheme, _tabPulse, 1.5f, 0.4f, 0.2f, 1.5f);
			}
			ImGui.SameLine();
		}
	}

	public void Draw(ThemePalette? theme = null)
	{
		float deltaTime = ImGui.GetIO().DeltaTime;
		_rgbHue = (_rgbHue + deltaTime * 0.3f) % 1f;
		_tabPulse += deltaTime * 2f;
		float availableWidth = ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;
		Vector2 spacing = ImGui.GetStyle().ItemSpacing;
		float buttonX = (availableWidth - spacing.X * 3f) / 4f;
		float buttonY = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Pause).Y + 8f;
		Vector2 buttonSize = new Vector2(buttonX, buttonY);
		ImDrawListPtr drawList = ImGui.GetWindowDrawList();
		ImGui.GetColorU32(ImGuiCol.Separator);
		int colorsPushed = 0;
		if (theme != null)
		{
			ImGui.PushStyleColor(ImGuiCol.Button, theme.Btn);
			ImGui.PushStyleColor(ImGuiCol.ButtonHovered, theme.BtnHovered);
			ImGui.PushStyleColor(ImGuiCol.ButtonActive, theme.BtnActive);
			ImGui.PushStyleColor(ImGuiCol.Text, theme.BtnText);
			colorsPushed = 4;
		}
		else
		{
			ImGui.PushStyleColor(ImGuiCol.Button, ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0f)));
			colorsPushed = 1;
		}
		ImGuiHelpers.ScaledDummy(spacing.Y / 2f);
		DrawCyberpunkTab(FontAwesomeIcon.User, buttonSize, spacing, SelectedTab.Individual, "Individual Pair Menu", drawList, theme);
		AttachTooltip("Individual Pair Menu", theme);
		DrawCyberpunkTab(FontAwesomeIcon.Users, buttonSize, spacing, SelectedTab.Syncshell, "Syncshell Menu", drawList, theme);
		AttachTooltip("Syncshell Menu", theme);
		ImGui.SameLine();
		DrawCyberpunkTab(FontAwesomeIcon.Filter, buttonSize, spacing, SelectedTab.Filter, "Filter", drawList, theme);
		AttachTooltip("Filter", theme);
		ImGui.SameLine();
		DrawCyberpunkTab(FontAwesomeIcon.UserCog, buttonSize, spacing, SelectedTab.UserConfig, "Your User Menu", drawList, theme);
		AttachTooltip("Your User Menu", theme);
		ImGui.NewLine();
		if (colorsPushed > 0)
		{
			ImGui.PopStyleColor(colorsPushed);
		}
		ImGuiHelpers.ScaledDummy(spacing);
		if (TabSelection == SelectedTab.Individual)
		{
			DrawAddPair(availableWidth, spacing.X, theme);
			DrawGlobalIndividualButtons(availableWidth, spacing.X, theme);
		}
		else if (TabSelection == SelectedTab.Syncshell)
		{
			DrawSyncshellMenu(availableWidth, spacing.X, theme);
			DrawGlobalSyncshellButtons(availableWidth, spacing.X, theme);
		}
		else if (TabSelection == SelectedTab.Filter)
		{
			DrawFilter(availableWidth, spacing.X, theme);
		}
		else if (TabSelection == SelectedTab.UserConfig)
		{
			DrawUserConfig(availableWidth, spacing.X, theme);
		}
		if (TabSelection != 0)
		{
			ImGuiHelpers.ScaledDummy(3f);
		}
	}

	private void DrawAddPair(float availableXWidth, float spacingX, ThemePalette? theme)
	{
		float buttonSize = _uiSharedService.GetIconTextButtonSize(FontAwesomeIcon.UserPlus, "Add");
		ImGui.SetNextItemWidth(availableXWidth - buttonSize - spacingX);
		ImGui.InputTextWithHint("##otheruid", "Other players UID/Alias", ref _pairToAdd, 20);
		ImGui.SameLine();
		using (ImRaii.Disabled(_pairManager.DirectPairs.Exists((Pair p) => string.Equals(p.UserData.UID, _pairToAdd, StringComparison.Ordinal) || string.Equals(p.UserData.Alias, _pairToAdd, StringComparison.Ordinal)) || string.IsNullOrEmpty(_pairToAdd)))
		{
			if (_uiSharedService.IconTextButton(FontAwesomeIcon.UserPlus, "Add"))
			{
				_apiController.UserAddPair(new UserDto(new UserData(_pairToAdd)));
				_pairToAdd = string.Empty;
			}
		}
		AttachTooltip("Pair with " + (_pairToAdd.IsNullOrEmpty() ? "other user" : _pairToAdd), theme);
	}

	private void DrawFilter(float availableWidth, float spacingX, ThemePalette? theme)
	{
		float buttonSize = _uiSharedService.GetIconTextButtonSize(FontAwesomeIcon.Ban, "Clear");
		ImGui.SetNextItemWidth(availableWidth - buttonSize - spacingX);
        string filter = Filter;
        if (ImGui.InputTextWithHint("##filter", "Filter for UID/notes", ref filter, 255))
		{
			Filter = filter;
		}
		ImGui.SameLine();
		using (ImRaii.Disabled(string.IsNullOrEmpty(Filter)))
		{
			if (_uiSharedService.IconTextButton(FontAwesomeIcon.Ban, "Clear"))
			{
				Filter = string.Empty;
			}
		}
	}

	private void DrawGlobalIndividualButtons(float availableXWidth, float spacingX, ThemePalette? theme)
	{
		ThemePalette activeTheme = theme ?? new ThemePalette();
		float buttonX = (availableXWidth - spacingX * 3f) / 4f;
		float buttonY = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Pause).Y;
		Vector2 buttonSize = new Vector2(buttonX, buttonY);
		using (ImRaii.PushFont(UiBuilder.IconFont))
		{
			using (ImRaii.Disabled(_globalControlCountdown > 0))
			{
				if (ThemeEffects.DrawIconButtonWithGlow(FontAwesomeIcon.Pause, buttonSize, activeTheme, _tabPulse))
				{
					ImGui.OpenPopup("Individual Pause");
				}
			}
		}
		AttachTooltip("Globally resume or pause all individual pairs.--SEP--" + ((_globalControlCountdown > 0) ? ("--SEP--Available again in " + _globalControlCountdown + " seconds.") : string.Empty), theme);
		ImGui.SameLine();
		using (ImRaii.PushFont(UiBuilder.IconFont))
		{
			using (ImRaii.Disabled(_globalControlCountdown > 0))
			{
				if (ThemeEffects.DrawIconButtonWithGlow(FontAwesomeIcon.VolumeUp, buttonSize, activeTheme, _tabPulse))
				{
					ImGui.OpenPopup("Individual Sounds");
				}
			}
		}
		AttachTooltip("Globally enable or disable sound sync with all individual pairs." + ((_globalControlCountdown > 0) ? ("--SEP--Available again in " + _globalControlCountdown + " seconds.") : string.Empty), theme);
		ImGui.SameLine();
		using (ImRaii.PushFont(UiBuilder.IconFont))
		{
			using (ImRaii.Disabled(_globalControlCountdown > 0))
			{
				if (ThemeEffects.DrawIconButtonWithGlow(FontAwesomeIcon.Running, buttonSize, activeTheme, _tabPulse))
				{
					ImGui.OpenPopup("Individual Animations");
				}
			}
		}
		AttachTooltip("Globally enable or disable animation sync with all individual pairs.--SEP--" + ((_globalControlCountdown > 0) ? ("--SEP--Available again in " + _globalControlCountdown + " seconds.") : string.Empty), theme);
		ImGui.SameLine();
		using (ImRaii.PushFont(UiBuilder.IconFont))
		{
			using (ImRaii.Disabled(_globalControlCountdown > 0))
			{
				if (ThemeEffects.DrawIconButtonWithGlow(FontAwesomeIcon.Sun, buttonSize, activeTheme, _tabPulse))
				{
					ImGui.OpenPopup("Individual VFX");
				}
			}
		}
		AttachTooltip("Globally enable or disable VFX sync with all individual pairs.--SEP--" + ((_globalControlCountdown > 0) ? ("--SEP--Available again in " + _globalControlCountdown + " seconds.") : string.Empty), theme);
		ImGui.SameLine();
		bool isMuted = _mareConfigService.Current.MuteOwnSounds;
		using (ImRaii.PushFont(UiBuilder.IconFont))
		{
			bool buttonResult = ImGui.Button((isMuted ? FontAwesomeIcon.UserSlash : FontAwesomeIcon.User).ToIconString(), buttonSize);
			bool isHoveredAfter = ImGui.IsItemHovered();
			bool isClicked = ImGui.IsItemClicked();
			if (!buttonResult && isClicked && isHoveredAfter)
			{
				buttonResult = true;
			}
			if (buttonResult)
			{
				_mareConfigService.Current.MuteOwnSounds = !_mareConfigService.Current.MuteOwnSounds;
				_mareConfigService.Save();
				_mareMediator.Publish(new SelfMuteSettingChangedMessage());
			}
		}
		AttachTooltip(isMuted ? "Unmute: Allow others to hear your sounds" : "Mute: Prevent others from hearing your sounds", theme);
		PopupIndividualSetting("Individual Pause", "Unpause all individuals", "Pause all individuals", FontAwesomeIcon.Play, FontAwesomeIcon.Pause, delegate(UserPermissions perm)
		{
			perm.SetPaused(paused: false);
			return perm;
		}, delegate(UserPermissions perm)
		{
			perm.SetPaused(paused: true);
			return perm;
		});
		PopupIndividualSetting("Individual Sounds", "Enable sounds for all individuals", "Disable sounds for all individuals", FontAwesomeIcon.VolumeUp, FontAwesomeIcon.VolumeMute, delegate(UserPermissions perm)
		{
			perm.SetDisableSounds(set: false);
			return perm;
		}, delegate(UserPermissions perm)
		{
			perm.SetDisableSounds(set: true);
			return perm;
		});
		PopupIndividualSetting("Individual Animations", "Enable animations for all individuals", "Disable animations for all individuals", FontAwesomeIcon.Running, FontAwesomeIcon.Stop, delegate(UserPermissions perm)
		{
			perm.SetDisableAnimations(set: false);
			return perm;
		}, delegate(UserPermissions perm)
		{
			perm.SetDisableAnimations(set: true);
			return perm;
		});
		PopupIndividualSetting("Individual VFX", "Enable VFX for all individuals", "Disable VFX for all individuals", FontAwesomeIcon.Sun, FontAwesomeIcon.Circle, delegate(UserPermissions perm)
		{
			perm.SetDisableVFX(set: false);
			return perm;
		}, delegate(UserPermissions perm)
		{
			perm.SetDisableVFX(set: true);
			return perm;
		});
	}

	private void DrawGlobalSyncshellButtons(float availableXWidth, float spacingX, ThemePalette? theme)
	{
		ThemePalette activeTheme = theme ?? new ThemePalette();
		float buttonX = (availableXWidth - spacingX * 4f) / 5f;
		float buttonY = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Pause).Y;
		Vector2 buttonSize = new Vector2(buttonX, buttonY);
		using (ImRaii.PushFont(UiBuilder.IconFont))
		{
			using (ImRaii.Disabled(_globalControlCountdown > 0))
			{
				if (ThemeEffects.DrawIconButtonWithGlow(FontAwesomeIcon.Pause, buttonSize, activeTheme, _tabPulse))
				{
					ImGui.OpenPopup("Syncshell Pause");
				}
			}
		}
		AttachTooltip("Globally resume or pause all syncshells.--SEP--Note: This will not affect users with preferred permissions in syncshells." + ((_globalControlCountdown > 0) ? ("--SEP--Available again in " + _globalControlCountdown + " seconds.") : string.Empty), theme);
		ImGui.SameLine();
		using (ImRaii.PushFont(UiBuilder.IconFont))
		{
			using (ImRaii.Disabled(_globalControlCountdown > 0))
			{
				if (ThemeEffects.DrawIconButtonWithGlow(FontAwesomeIcon.VolumeUp, buttonSize, activeTheme, _tabPulse))
				{
					ImGui.OpenPopup("Syncshell Sounds");
				}
			}
		}
		AttachTooltip("Globally enable or disable sound sync with all syncshells.--SEP--Note: This will not affect users with preferred permissions in syncshells." + ((_globalControlCountdown > 0) ? ("--SEP--Available again in " + _globalControlCountdown + " seconds.") : string.Empty), theme);
		ImGui.SameLine();
		using (ImRaii.PushFont(UiBuilder.IconFont))
		{
			using (ImRaii.Disabled(_globalControlCountdown > 0))
			{
				if (ThemeEffects.DrawIconButtonWithGlow(FontAwesomeIcon.Running, buttonSize, activeTheme, _tabPulse))
				{
					ImGui.OpenPopup("Syncshell Animations");
				}
			}
		}
		AttachTooltip("Globally enable or disable animation sync with all syncshells.--SEP--Note: This will not affect users with preferred permissions in syncshells." + ((_globalControlCountdown > 0) ? ("--SEP--Available again in " + _globalControlCountdown + " seconds.") : string.Empty), theme);
		ImGui.SameLine();
		using (ImRaii.PushFont(UiBuilder.IconFont))
		{
			using (ImRaii.Disabled(_globalControlCountdown > 0))
			{
				if (ThemeEffects.DrawIconButtonWithGlow(FontAwesomeIcon.Sun, buttonSize, activeTheme, _tabPulse))
				{
					ImGui.OpenPopup("Syncshell VFX");
				}
			}
		}
		AttachTooltip("Globally enable or disable VFX sync with all syncshells.--SEP--Note: This will not affect users with preferred permissions in syncshells." + ((_globalControlCountdown > 0) ? ("--SEP--Available again in " + _globalControlCountdown + " seconds.") : string.Empty), theme);
		PopupSyncshellSetting("Syncshell Pause", "Unpause all syncshells", "Pause all syncshells", FontAwesomeIcon.Play, FontAwesomeIcon.Pause, delegate(GroupUserPreferredPermissions perm)
		{
			perm.SetPaused(set: false);
			return perm;
		}, delegate(GroupUserPreferredPermissions perm)
		{
			perm.SetPaused(set: true);
			return perm;
		});
		PopupSyncshellSetting("Syncshell Sounds", "Enable sounds for all syncshells", "Disable sounds for all syncshells", FontAwesomeIcon.VolumeUp, FontAwesomeIcon.VolumeMute, delegate(GroupUserPreferredPermissions perm)
		{
			perm.SetDisableSounds(set: false);
			return perm;
		}, delegate(GroupUserPreferredPermissions perm)
		{
			perm.SetDisableSounds(set: true);
			return perm;
		});
		PopupSyncshellSetting("Syncshell Animations", "Enable animations for all syncshells", "Disable animations for all syncshells", FontAwesomeIcon.Running, FontAwesomeIcon.Stop, delegate(GroupUserPreferredPermissions perm)
		{
			perm.SetDisableAnimations(set: false);
			return perm;
		}, delegate(GroupUserPreferredPermissions perm)
		{
			perm.SetDisableAnimations(set: true);
			return perm;
		});
		PopupSyncshellSetting("Syncshell VFX", "Enable VFX for all syncshells", "Disable VFX for all syncshells", FontAwesomeIcon.Sun, FontAwesomeIcon.Circle, delegate(GroupUserPreferredPermissions perm)
		{
			perm.SetDisableVFX(set: false);
			return perm;
		}, delegate(GroupUserPreferredPermissions perm)
		{
			perm.SetDisableVFX(set: true);
			return perm;
		});
		ImGui.SameLine();
		using (ImRaii.PushFont(UiBuilder.IconFont))
		{
			using (ImRaii.Disabled(_globalControlCountdown > 0 || !UiSharedService.CtrlPressed()))
			{
				if (ThemeEffects.DrawIconButtonWithGlow(FontAwesomeIcon.Check, buttonSize, activeTheme, _tabPulse))
				{
					GlobalControlCountdown(10);
					Dictionary<string, GroupUserPreferredPermissions> bulkSyncshells = _pairManager.GroupPairs.Keys.OrderBy<GroupFullInfoDto, string>((GroupFullInfoDto g) => g.GroupAliasOrGID, StringComparer.OrdinalIgnoreCase).ToDictionary<GroupFullInfoDto, string, GroupUserPreferredPermissions>((GroupFullInfoDto g) => g.Group.GID, delegate(GroupFullInfoDto g)
					{
						GroupUserPreferredPermissions perm2 = g.GroupUserPermissions;
						perm2.SetDisableSounds(g.GroupPermissions.IsPreferDisableSounds());
						perm2.SetDisableAnimations(g.GroupPermissions.IsPreferDisableAnimations());
						perm2.SetDisableVFX(g.GroupPermissions.IsPreferDisableVFX());
						return perm2;
					}, StringComparer.Ordinal);
					_apiController.SetBulkPermissions(new BulkPermissionsDto(new Dictionary<string, UserPermissions>(StringComparer.Ordinal), bulkSyncshells)).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
		}
		AttachTooltip("Globally align syncshell permissions to suggested syncshell permissions.--SEP--Note: This will not affect users with preferred permissions in syncshells." + Environment.NewLine + "Note: If multiple users share one syncshell the permissions to that user will be set to " + Environment.NewLine + "the ones of the last applied syncshell in alphabetical order.--SEP--Hold CTRL to enable this button" + ((_globalControlCountdown > 0) ? ("--SEP--Available again in " + _globalControlCountdown + " seconds.") : string.Empty), theme);
	}

	private void DrawSyncshellMenu(float availableWidth, float spacingX, ThemePalette? theme)
	{
		float buttonX = (availableWidth - spacingX) / 2f;
		using (ImRaii.Disabled(_pairManager.GroupPairs.Select<KeyValuePair<GroupFullInfoDto, List<Pair>>, GroupFullInfoDto>((KeyValuePair<GroupFullInfoDto, List<Pair>> k) => k.Key).Distinct().Count((GroupFullInfoDto g) => string.Equals(g.OwnerUID, _apiController.UID, StringComparison.Ordinal)) >= _apiController.ServerInfo.MaxGroupsCreatedByUser))
		{
			if (_uiSharedService.IconTextButton(FontAwesomeIcon.Plus, "Create new Syncshell", buttonX))
			{
				_mareMediator.Publish(new UiToggleMessage(typeof(CreateSyncshellUI)));
			}
			ImGui.SameLine();
		}
		using (ImRaii.Disabled(_pairManager.GroupPairs.Select<KeyValuePair<GroupFullInfoDto, List<Pair>>, GroupFullInfoDto>((KeyValuePair<GroupFullInfoDto, List<Pair>> k) => k.Key).Distinct().Count() >= _apiController.ServerInfo.MaxGroupsJoinedByUser))
		{
			if (_uiSharedService.IconTextButton(FontAwesomeIcon.Users, "Join existing Syncshell", buttonX))
			{
				_mareMediator.Publish(new UiToggleMessage(typeof(JoinSyncshellUI)));
			}
		}
	}

	private void DrawUserConfig(float availableWidth, float spacingX, ThemePalette? theme)
	{
		float buttonX = (availableWidth - spacingX) / 2f;
		if (_uiSharedService.IconTextButton(FontAwesomeIcon.UserCircle, "Edit Mare Profile", buttonX))
		{
			_mareMediator.Publish(new UiToggleMessage(typeof(EditProfileUi)));
		}
		AttachTooltip("Edit your Mare Profile", theme);
		ImGui.SameLine();
		if (_uiSharedService.IconTextButton(FontAwesomeIcon.PersonCircleQuestion, "Chara Data Analysis", buttonX))
		{
			_mareMediator.Publish(new UiToggleMessage(typeof(DataAnalysisUi)));
		}
		AttachTooltip("View and analyze your generated character data", theme);
		if (_uiSharedService.IconTextButton(FontAwesomeIcon.Running, "Character Data Hub", availableWidth))
		{
			_mareMediator.Publish(new UiToggleMessage(typeof(CharaDataHubUi)));
		}
		if (_uiSharedService.IconTextButton(FontAwesomeIcon.Cog, "Settings", buttonX))
		{
			_mareMediator.Publish(new UiToggleMessage(typeof(ModernSettingsUi)));
		}
		AttachTooltip("Open Mare Settings", theme);
		ImGui.SameLine();
		if (_uiSharedService.IconTextButton(FontAwesomeIcon.Palette, "Customize Theme", buttonX) && _parentWindow is CompactUi compactUi)
		{
			compactUi.ToggleThemeInline();
		}
		AttachTooltip("Customize appearance and themes", theme);
		if (_parentWindow == null)
		{
			return;
		}
		ImGui.Separator();
		int checkboxColorsPushed = 0;
		if (theme != null)
		{
			ImGui.PushStyleColor(ImGuiCol.CheckMark, theme.BtnText);
			ImGui.PushStyleColor(ImGuiCol.FrameBg, theme.Btn);
			ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, theme.BtnHovered);
			ImGui.PushStyleColor(ImGuiCol.FrameBgActive, theme.BtnActive);
			ImGui.PushStyleColor(ImGuiCol.Text, theme.BtnText);
			checkboxColorsPushed = 5;
		}
		bool isPinned = _parentWindow.AllowPinning;
		if (ImGui.Checkbox("Pin Window", ref isPinned))
		{
			_parentWindow.AllowPinning = isPinned;
		}
		AttachTooltip("Prevent the window from being moved", theme);
		ImGui.SameLine();
		bool isClickThrough = _parentWindow.AllowClickthrough;
		if (ImGui.Checkbox("Click Through", ref isClickThrough))
		{
			_parentWindow.AllowClickthrough = isClickThrough;
			if (isClickThrough)
			{
				_parentWindow.AllowPinning = true;
				_selectedTab = SelectedTab.None;
				_filter = string.Empty;
			}
		}
		AttachTooltip("Allow clicks to pass through the window", theme);
		if (checkboxColorsPushed > 0)
		{
			ImGui.PopStyleColor(checkboxColorsPushed);
		}
	}

	public void ClearUserFilter()
	{
		_filter = string.Empty;
		_selectedTab = SelectedTab.None;
		_logger.LogInformation("Reset button clicked - cleared filter and user selection");
	}

	private async Task GlobalControlCountdown(int countdown)
	{
		for (_globalControlCountdown = countdown; _globalControlCountdown > 0; _globalControlCountdown--)
		{
			await Task.Delay(TimeSpan.FromSeconds(1L)).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	private void PopupIndividualSetting(string popupTitle, string enableText, string disableText, FontAwesomeIcon enableIcon, FontAwesomeIcon disableIcon, Func<UserPermissions, UserPermissions> actEnable, Func<UserPermissions, UserPermissions> actDisable)
	{
		if (!ImGui.BeginPopup(popupTitle))
		{
			return;
		}
		if (_uiSharedService.IconTextButton(enableIcon, enableText, null, isInPopup: true))
		{
			GlobalControlCountdown(10);
			Dictionary<string, UserPermissions> bulkIndividualPairs = _pairManager.PairsWithGroups.Keys.Where((Pair g) => g.IndividualPairStatus == IndividualPairStatus.Bidirectional).ToDictionary<Pair, string, UserPermissions>((Pair g) => g.UserPair.User.UID, (Pair g) => actEnable(g.UserPair.OwnPermissions), StringComparer.Ordinal);
			_apiController.SetBulkPermissions(new BulkPermissionsDto(bulkIndividualPairs, new Dictionary<string, GroupUserPreferredPermissions>(StringComparer.Ordinal))).ConfigureAwait(continueOnCapturedContext: false);
			ImGui.CloseCurrentPopup();
		}
		if (_uiSharedService.IconTextButton(disableIcon, disableText, null, isInPopup: true))
		{
			GlobalControlCountdown(10);
			Dictionary<string, UserPermissions> bulkIndividualPairs = _pairManager.PairsWithGroups.Keys.Where((Pair g) => g.IndividualPairStatus == IndividualPairStatus.Bidirectional).ToDictionary<Pair, string, UserPermissions>((Pair g) => g.UserPair.User.UID, (Pair g) => actDisable(g.UserPair.OwnPermissions), StringComparer.Ordinal);
			_apiController.SetBulkPermissions(new BulkPermissionsDto(bulkIndividualPairs, new Dictionary<string, GroupUserPreferredPermissions>(StringComparer.Ordinal))).ConfigureAwait(continueOnCapturedContext: false);
			ImGui.CloseCurrentPopup();
		}
		ImGui.EndPopup();
	}

	private void PopupSyncshellSetting(string popupTitle, string enableText, string disableText, FontAwesomeIcon enableIcon, FontAwesomeIcon disableIcon, Func<GroupUserPreferredPermissions, GroupUserPreferredPermissions> actEnable, Func<GroupUserPreferredPermissions, GroupUserPreferredPermissions> actDisable)
	{
		if (!ImGui.BeginPopup(popupTitle))
		{
			return;
		}
		if (_uiSharedService.IconTextButton(enableIcon, enableText, null, isInPopup: true))
		{
			GlobalControlCountdown(10);
			Dictionary<string, GroupUserPreferredPermissions> bulkSyncshells = _pairManager.GroupPairs.Keys.OrderBy<GroupFullInfoDto, string>((GroupFullInfoDto u) => u.GroupAliasOrGID, StringComparer.OrdinalIgnoreCase).ToDictionary<GroupFullInfoDto, string, GroupUserPreferredPermissions>((GroupFullInfoDto g) => g.Group.GID, (GroupFullInfoDto g) => actEnable(g.GroupUserPermissions), StringComparer.Ordinal);
			_apiController.SetBulkPermissions(new BulkPermissionsDto(new Dictionary<string, UserPermissions>(StringComparer.Ordinal), bulkSyncshells)).ConfigureAwait(continueOnCapturedContext: false);
			ImGui.CloseCurrentPopup();
		}
		if (_uiSharedService.IconTextButton(disableIcon, disableText, null, isInPopup: true))
		{
			GlobalControlCountdown(10);
			Dictionary<string, GroupUserPreferredPermissions> bulkSyncshells = _pairManager.GroupPairs.Keys.OrderBy<GroupFullInfoDto, string>((GroupFullInfoDto u) => u.GroupAliasOrGID, StringComparer.OrdinalIgnoreCase).ToDictionary<GroupFullInfoDto, string, GroupUserPreferredPermissions>((GroupFullInfoDto g) => g.Group.GID, (GroupFullInfoDto g) => actDisable(g.GroupUserPermissions), StringComparer.Ordinal);
			_apiController.SetBulkPermissions(new BulkPermissionsDto(new Dictionary<string, UserPermissions>(StringComparer.Ordinal), bulkSyncshells)).ConfigureAwait(continueOnCapturedContext: false);
			ImGui.CloseCurrentPopup();
		}
		ImGui.EndPopup();
	}
}
