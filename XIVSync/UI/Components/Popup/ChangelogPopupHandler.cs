using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using XIVSync.MareConfiguration;

namespace XIVSync.UI.Components.Popup;

public class ChangelogPopupHandler : IPopupHandler
{
	private readonly MareConfigService _configService;

	private readonly UiSharedService _uiSharedService;

	private string _currentVersion = string.Empty;

	public Vector2 PopupSize => new Vector2(600f, 500f);

	public bool ShowClose => false;

	public ChangelogPopupHandler(MareConfigService configService, UiSharedService uiSharedService)
	{
		_configService = configService;
		_uiSharedService = uiSharedService;
	}

	public void Open(string version)
	{
		_currentVersion = version;
	}

	public void DrawContent()
	{
		ThemePalette theme = _uiSharedService.GetCurrentTheme() ?? new ThemePalette();
		using (ImRaii.PushColor(ImGuiCol.Text, theme.Accent))
		{
			_uiSharedService.BigText("XIVSync " + _currentVersion + " - What's New!");
		}
		ImGui.Separator();
		ImGui.Spacing();
		using (ImRaii.IEndObject child = ImRaii.Child("ChangelogContent", new Vector2(-1f, -50f), border: true))
		{
			if (child)
			{
				DrawChangelogContent();
			}
		}
		ImGui.Spacing();
		ImGui.Separator();
		float buttonWidth = 120f * ImGuiHelpers.GlobalScale;
		ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - buttonWidth) / 2f);
		if (_uiSharedService.IconTextButton(FontAwesomeIcon.Check, "Got it!"))
		{
			_configService.Current.LastSeenVersion = _currentVersion;
			_configService.Save();
			ImGui.CloseCurrentPopup();
		}
	}

	private void DrawChangelogContent()
	{
		if (_uiSharedService.GetCurrentTheme() == null)
		{
			new ThemePalette();
		}
		DrawVersionChangelog("1.35.0", new string[18]
		{
			"UI Experience Improvements:", "• Moved Settings, Customize, Collapse, and Disconnect buttons out of hamburger menu for quick access", "• Added Close button next to Disconnect for easier window management", "• Removed hamburger menu and cleaned up toolbar layout", "• Made all backgrounds 25% more transparent for better game visibility", "", "Upload Performance Fixes:", "• Fixed large file upload cancellation issues that prevented 40MB+ files from completing", "• Smarter upload queue management - won't cancel uploads for minor character changes", "• Increased HTTP timeout to 10 minutes for large texture files",
			"• Large files now upload successfully instead of getting stuck in retry loops", "", "Interface Polish:", "• Username/UID display now scales font size to fit available space", "• Simplified name display - shows vanity name OR UID, not both", "• Added bottom padding to pairs list to prevent items getting cut off", "• Moved toolbar 2 pixels left and status message down 3 pixels for better positioning", "• Made pairs list backgrounds fully transparent for cleaner appearance"
		});
		ImGui.Spacing();
		ImGui.Separator();
		ImGui.Spacing();
		DrawVersionChangelog("1.30.0", new string[8] { "\ud83c\udf89 New Features:", "• Added NSFW profile viewing toggle in Settings > General", "• UI no longer auto-opens on game start - only appears when using '/xiv' command", "• Improved privacy controls for adult content viewing", "", "\ud83d\udd27 Improvements:", "• Better user control over when the XIVSync UI appears", "• Enhanced settings organization for profile-related options" });
		ImGui.Spacing();
		ImGui.Separator();
		ImGui.Spacing();
		DrawVersionChangelog("1.29.2", new string[5] { "✨ Status Message System:", "• Added editable status message bar below the top tab menu", "• Click status bar to edit your message in-place with save/cancel option", "• Profile content and status messages are now visually separated", "• Status messages appear in pair listings for users who have set them" });
		ImGui.Spacing();
		ImGui.Separator();
		ImGui.Spacing();
		DrawVersionChangelog("1.29.1", new string[8] { "\ud83d\udc1b Bug Fixes:", "• Fixed hamburger menu positioning in both collapsed and expanded modes", "• Resolved bottom border display issues in the main UI", "• Improved UI layout consistency across different window states", "", "\ud83d\udd27 Improvements:", "• Better spacing and alignment for single-button toolbar layouts", "• Enhanced visual consistency in the compact interface" });
		ImGui.Spacing();
		ImGui.Separator();
		ImGui.Spacing();
		DrawVersionChangelog("1.29.0", new string[28]
		{
			"\ud83d\ude80 Major Discord Bot Simplification:", "• Eliminated complex Lodestone verification - instant account creation with just Discord ID", "• Removed relink functionality - strict 1 Discord = 1 Mare account policy", "• Eliminated captcha/embed verification - direct access to main interface", "• Removed support/donation system for cleaner interface", "", "\ud83c\udfa8 Complete Rebranding & Localization:", "• 'Mare Synchronos' → 'XIV Sync' across all user-facing strings", "• Full English localization and interface cleanup", "• Cleaned interface - removed MareToken/WorryCoin management",
			"• Streamlined menu options and descriptions", "", "\ud83d\udcf1 Dramatically Improved User Experience:", "• Registration now 90% faster and simpler", "• Before: Complex multi-step verification process", "• After: Start → Register → ✅ Instant Account + Optional Vanity ID", "", "\ud83d\udd27 Technical Improvements:", "• Removed ~2000 lines of complex verification code", "• Eliminated external dependencies (lodestone API, payment platform)",
			"• Better error handling with fewer failure points", "• Improved security with clearer ownership model", "", "✅ Preserved Core Features:", "• Account recovery functionality (regenerate lost secret keys)", "• All existing account management features", "• OAuth2 authentication compatibility unchanged", "• Legacy secret keys still work - no impact on game connectivity"
		});
		ImGui.Spacing();
		ImGui.Separator();
		ImGui.Spacing();
		DrawVersionChangelog("1.28.0", new string[13]
		{
			"\ud83c\udf89 New Features:", "• Profile Status Messages - See the first line of users' Mare profiles in the pair list", "• Instance Auto-Pause - Automatically pause syncing during dungeons, trials, and PvP", "• Hamburger Menu - Consolidated all actions into a clean hamburger menu", "", "\ud83d\udd27 Improvements:", "• Better UI organization and cleaner interface", "• Improved performance during instance detection", "• Enhanced settings organization", "",
			"\ud83d\udc1b Bug Fixes:", "• Fixed various UI layout issues", "• Improved stability during game state changes"
		});
		ImGui.Spacing();
		ImGui.Separator();
		ImGui.Spacing();
		DrawVersionChangelog("1.27.0", new string[14]
		{
			"\ud83c\udfa8 Major UI Overhaul:", "• Complete redesign with modern theming system", "• New compact interface with better space utilization", "• Customizable themes and backgrounds", "", "⚡ Performance Improvements:", "• Faster pair synchronization", "• Reduced memory usage", "• Better handling of large friend lists", "",
			"\ud83d\udd27 New Features:", "• Enhanced profile system", "• Improved group management", "• Better error handling and notifications"
		});
	}

	private void DrawVersionChangelog(string version, string[] changes)
	{
		ThemePalette theme = _uiSharedService.GetCurrentTheme() ?? new ThemePalette();
		using (ImRaii.PushColor(ImGuiCol.Text, theme.Accent))
		{
			ImU8String text = new ImU8String(8, 1);
			text.AppendLiteral("Version ");
			text.AppendFormatted(version);
			ImGui.TextUnformatted(text);
		}
		ImGui.Spacing();
		foreach (string change in changes)
		{
			if (string.IsNullOrEmpty(change))
			{
				ImGui.Spacing();
			}
			else if (change.StartsWith("\ud83c\udf89") || change.StartsWith("\ud83c\udfa8") || change.StartsWith("⚡") || change.StartsWith("\ud83d\udd27") || change.StartsWith("\ud83d\udc1b"))
			{
				using (ImRaii.PushColor(ImGuiCol.Text, theme.Accent))
				{
					ImGui.TextUnformatted(change);
				}
			}
			else if (change.StartsWith("•"))
			{
				using (ImRaii.PushColor(ImGuiCol.Text, theme.TextSecondary))
				{
					ImU8String text = new ImU8String(2, 1);
					text.AppendLiteral("  ");
					text.AppendFormatted(change);
					ImGui.TextUnformatted(text);
				}
			}
			else
			{
				using (ImRaii.PushColor(ImGuiCol.Text, theme.TextPrimary))
				{
					ImGui.TextUnformatted(change);
				}
			}
		}
	}
}
