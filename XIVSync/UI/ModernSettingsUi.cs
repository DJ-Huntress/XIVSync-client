using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.Extensions.Logging;
using XIVSync.API.Dto;
using XIVSync.FileCache;
using XIVSync.Interop;
using XIVSync.Interop.Ipc;
using XIVSync.MareConfiguration;
using XIVSync.MareConfiguration.Models;
using XIVSync.PlayerData.Pairs;
using XIVSync.Services;
using XIVSync.Services.Mediator;
using XIVSync.Services.ServerConfiguration;
using XIVSync.WebAPI;
using XIVSync.WebAPI.Files;
using XIVSync.WebAPI.SignalR.Utils;

namespace XIVSync.UI;

public class ModernSettingsUi : WindowMediatorSubscriberBase
{
	private readonly MareConfigService _configService;

	private readonly NotesConfigService _notesConfigService;

	private readonly UiSharedService _uiSharedService;

	private readonly ApiController _apiController;

	private readonly IpcManager _ipcManager;

	private readonly PairManager _pairManager;

	private readonly PerformanceCollectorService _performanceCollector;

	private readonly PlayerPerformanceConfigService _playerPerformanceConfigService;

	private readonly FileCompactor _fileCompactor;

	private readonly CacheMonitor _cacheMonitor;

	private readonly FileTransferOrchestrator _fileTransferOrchestrator;

	private readonly DalamudUtilService _dalamudUtilService;

	private readonly ServerConfigurationManager _serverConfigurationManager;

	private readonly FileUploadManager _fileTransferManager;

	private readonly UILoggingProvider _uiLoggingProvider;

	private readonly HttpClient _httpClient;

	private int _selectedTab;

	private readonly string[] _tabNames = new string[7] { "General", "Performance", "Storage", "Transfers", "Service Settings", "Debug", "Logs" };

	private string _logFilterText = string.Empty;

	private bool _filterPlugin = true;

	private bool _filterAuth = true;

	private bool _filterServices = true;

	private bool _filterNetwork = true;

	private bool _filterFile = true;

	private bool _filterOther = true;

	private bool _penumbraExists;

	private bool _glamourerExists;

	private bool _heelsExists;

	private bool _customizePlusExists;

	private bool _honorificExists;

	private bool _moodlesExists;

	private bool _petNamesExists;

	private bool _brioExists;

	private bool _notesCollapsed;

	private bool _uiCollapsed;

	private string _uidToAddForIgnore = string.Empty;

	private int _selectedEntry = -1;

	private bool _deleteFilesPopupModalShown;

	private bool _deleteAccountPopupModalShown;

	private int _lastSelectedServerIndex = -1;

	public ModernSettingsUi(ILogger<ModernSettingsUi> logger, MareMediator mediator, MareConfigService configService, NotesConfigService notesConfigService, UiSharedService uiSharedService, ApiController apiController, IpcManager ipcManager, PairManager pairManager, PerformanceCollectorService performanceCollector, PlayerPerformanceConfigService playerPerformanceConfigService, FileCompactor fileCompactor, CacheMonitor cacheMonitor, FileTransferOrchestrator fileTransferOrchestrator, DalamudUtilService dalamudUtilService, ServerConfigurationManager serverConfigurationManager, FileUploadManager fileTransferManager, UILoggingProvider uiLoggingProvider, HttpClient httpClient)
		: base(logger, mediator, "XIV Sync Settings", performanceCollector)
	{
		_configService = configService;
		_notesConfigService = notesConfigService;
		_uiSharedService = uiSharedService;
		_apiController = apiController;
		_ipcManager = ipcManager;
		_pairManager = pairManager;
		_performanceCollector = performanceCollector;
		_playerPerformanceConfigService = playerPerformanceConfigService;
		_fileCompactor = fileCompactor;
		_cacheMonitor = cacheMonitor;
		_fileTransferOrchestrator = fileTransferOrchestrator;
		_dalamudUtilService = dalamudUtilService;
		_serverConfigurationManager = serverConfigurationManager;
		_fileTransferManager = fileTransferManager;
		_uiLoggingProvider = uiLoggingProvider;
		_httpClient = httpClient;
		base.SizeConstraints = new WindowSizeConstraints
		{
			MinimumSize = new Vector2(800f, 600f),
			MaximumSize = new Vector2(1200f, 1000f)
		};
		base.Flags = ImGuiWindowFlags.NoCollapse;
		base.AllowClickthrough = false;
		base.AllowPinning = false;
		base.Mediator.Subscribe<OpenModernSettingsUiMessage>(this, delegate
		{
			Toggle();
		});
		base.Mediator.Subscribe<SwitchToIntroUiMessage>(this, delegate
		{
			base.IsOpen = false;
		});
	}

	private void UpdatePluginStatus()
	{
		_penumbraExists = _ipcManager.Penumbra.APIAvailable;
		_glamourerExists = _ipcManager.Glamourer.APIAvailable;
		_customizePlusExists = _ipcManager.CustomizePlus.APIAvailable;
		_heelsExists = _ipcManager.Heels.APIAvailable;
		_honorificExists = _ipcManager.Honorific.APIAvailable;
		_moodlesExists = _ipcManager.Moodles.APIAvailable;
		_petNamesExists = _ipcManager.PetNames.APIAvailable;
		_brioExists = _ipcManager.Brio.APIAvailable;
	}

	public override void OnOpen()
	{
		UpdatePluginStatus();
		base.OnOpen();
	}

	protected override void DrawInternal()
	{
		UpdatePluginStatus();
		using ImRaii.IEndObject child = ImRaii.Child("MainContent", new Vector2(-1f, -1f), border: false, ImGuiWindowFlags.NoScrollbar);
		if (!child.Success)
		{
			return;
		}
		ImGui.GetWindowSize();
		float leftPanelWidth = 260f;
		float contentPadding = 12f;
		using (ImRaii.IEndObject leftPanel = ImRaii.Child("LeftPanel", new Vector2(leftPanelWidth, -1f), border: true, ImGuiWindowFlags.NoScrollbar))
		{
			if (leftPanel.Success)
			{
				DrawLeftSidebar();
			}
		}
		ImGui.SameLine();
		using ImRaii.IEndObject rightPanel = ImRaii.Child("RightPanel", new Vector2(-1f, -1f), border: false);
		if (rightPanel.Success)
		{
			ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(contentPadding, contentPadding));
			DrawContentArea();
			ImGui.PopStyleVar();
		}
	}

	private void DrawLeftSidebar()
	{
		ImGui.GetStyle();
		DrawPluginStatusSection();
		ImGui.Separator();
		DrawServiceStatusSection();
		ImGui.Separator();
		DrawTabNavigation();
		float footerHeight = 40f;
		float remainingHeight = ImGui.GetContentRegionAvail().Y - footerHeight;
		ImGui.Dummy(new Vector2(0f, Math.Max(0f, remainingHeight)));
		DrawDiscordButton();
	}

	private void DrawPluginStatusSection()
	{
		ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1f));
		ImGui.Text("MANDATORY PLUGINS");
		ImGui.PopStyleColor();
		ImGui.Spacing();
		DrawPluginStatus("Penumbra", _penumbraExists, mandatory: true);
		DrawPluginStatus("Glamourer", _glamourerExists, mandatory: true);
		ImGui.Spacing();
		ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1f));
		ImGui.Text("OPTIONAL PLUGINS");
		ImGui.PopStyleColor();
		ImGui.Spacing();
		DrawPluginStatus("SimpleHeels", _heelsExists, mandatory: false);
		DrawPluginStatus("Customize+", _customizePlusExists, mandatory: false);
		DrawPluginStatus("Honorific", _honorificExists, mandatory: false);
		DrawPluginStatus("Moodles", _moodlesExists, mandatory: false);
		DrawPluginStatus("PetNicknames", _petNamesExists, mandatory: false);
		DrawPluginStatus("Brio", _brioExists, mandatory: false);
	}

	private void DrawPluginStatus(string pluginName, bool available, bool mandatory)
	{
		ImGui.Text(pluginName);
		ImGui.SameLine();
		ImGui.CalcTextSize(pluginName);
		float availableWidth = ImGui.GetContentRegionAvail().X;
		float indicatorSize = 8f;
		ImGui.SetCursorPosX(ImGui.GetCursorPosX() + availableWidth - indicatorSize - 10f);
		ImDrawListPtr drawList = ImGui.GetWindowDrawList();
		Vector2 pos = ImGui.GetCursorScreenPos();
		drawList.AddCircleFilled(new Vector2(pos.X + indicatorSize / 2f, pos.Y + ImGui.GetTextLineHeight() / 2f), col: ImGui.ColorConvertFloat4ToU32((!available) ? (mandatory ? new Vector4(0.7f, 0.3f, 0.3f, 1f) : new Vector4(1f, 0.6f, 0f, 1f)) : new Vector4(0.3f, 0.7f, 0.3f, 1f)), radius: indicatorSize / 2f);
		ImGui.Dummy(new Vector2(indicatorSize, ImGui.GetTextLineHeight()));
		if (ImGui.IsItemHovered())
		{
			ImU8String tooltip = new ImU8String(5, 2);
			tooltip.AppendFormatted(pluginName);
			tooltip.AppendLiteral(" is ");
			tooltip.AppendFormatted(available ? "available and up to date" : "unavailable or not up to date");
			tooltip.AppendLiteral(".");
			ImGui.SetTooltip(tooltip);
		}
	}

	private void DrawServiceStatusSection()
	{
		ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1f));
		UiSharedService.TextWrapped(GetServerStatusText());
		ImGui.Spacing();
		int usersOnline = GetUsersOnlineCount();
		ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.3f, 0.7f, 0.3f, 1f));
		ImU8String text = new ImU8String(13, 1);
		text.AppendFormatted(usersOnline);
		text.AppendLiteral(" Users Online");
		ImGui.Text(text);
		ImGui.PopStyleColor();
		ImGui.PopStyleColor();
	}

	private void DrawTabNavigation()
	{
		for (int i = 0; i < _tabNames.Length; i++)
		{
			if (_selectedTab == i)
			{
				ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.29f, 0.42f, 0.97f, 1f));
				ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.35f, 0.48f, 0.97f, 1f));
				ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.25f, 0.38f, 0.93f, 1f));
				ImGui.PushStyleColor(ImGuiCol.Text, Vector4.One);
			}
			else
			{
				ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
				ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.23f, 0.23f, 0.23f, 1f));
				ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.2f, 0.2f, 0.2f, 1f));
				ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.8f, 0.8f, 1f));
			}
			if (ImGui.Button(_tabNames[i], new Vector2(-1f, 0f)))
			{
				_selectedTab = i;
			}
			ImGui.PopStyleColor(4);
			ImGui.Spacing();
		}
	}

	private void DrawDiscordButton()
	{
		ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.35f, 0.4f, 0.95f, 1f));
		ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.28f, 0.32f, 0.77f, 1f));
		ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.25f, 0.29f, 0.7f, 1f));
		ImGui.PushStyleColor(ImGuiCol.Text, Vector4.One);
		if (ImGui.Button("XIV Sync Discord", new Vector2(-1f, 0f)))
		{
			Util.OpenLink("https://discord.gg/kVva7DHV4r");
		}
		ImGui.PopStyleColor(4);
	}

	private void DrawContentArea()
	{
		switch (_selectedTab)
		{
		case 0:
			DrawGeneralTab();
			break;
		case 1:
			DrawPerformanceTab();
			break;
		case 2:
			DrawStorageTab();
			break;
		case 3:
			DrawTransfersTab();
			break;
		case 4:
			DrawServiceSettingsTab();
			break;
		case 5:
			DrawDebugTab();
			break;
		case 6:
			DrawLogsTab();
			break;
		}
	}

	private void DrawGeneralTab()
	{
		DrawCollapsibleSection("Notes", ref _notesCollapsed, delegate
		{
			if (ImGui.Button("Export all your user notes to clipboard"))
			{
				ExportNotes();
			}
			ImGui.Spacing();
			if (ImGui.Button("Import notes from clipboard"))
			{
				ImportNotes(overwrite: false);
			}
			ImGui.SameLine();
			if (ImGui.Button("Overwrite existing notes"))
			{
				ImportNotes(overwrite: true);
			}
			ImGui.Spacing();
			ImGui.Separator();
			ImGui.Spacing();
			bool v6 = _configService.Current.OpenPopupOnAdd;
			if (ImGui.Checkbox("Open Notes Popup on user addition", ref v6))
			{
				_configService.Current.OpenPopupOnAdd = v6;
				_configService.Save();
			}
			DrawHelpIcon("Shows a popup to add notes when adding a new user");
			bool v7 = _configService.Current.AutoPopulateEmptyNotesFromCharaName;
			if (ImGui.Checkbox("Automatically populate notes using player names", ref v7))
			{
				_configService.Current.AutoPopulateEmptyNotesFromCharaName = v7;
				_configService.Save();
			}
			DrawHelpIcon("Automatically fills in notes with character names when empty");
		});
		ImGui.Spacing();
		DrawCollapsibleSection("UI", ref _uiCollapsed, delegate
		{
			bool v = _configService.Current.EnableRightClickMenus;
			if (ImGui.Checkbox("Enable Game Right Click Menu Entries", ref v))
			{
				_configService.Current.EnableRightClickMenus = v;
				_configService.Save();
			}
			DrawHelpIcon("Adds Mare options to right-click context menus in game");
			bool v2 = _configService.Current.EnableDtrEntry;
			if (ImGui.Checkbox("Display status and visible pair count in Server Info Bar", ref v2))
			{
				_configService.Current.EnableDtrEntry = v2;
				_configService.Save();
			}
			DrawHelpIcon("Shows Mare status information in the game's server info bar");
			bool v3 = _configService.Current.ShowUidInDtrTooltip;
			if (ImGui.Checkbox("Show visible character's UID in tooltip", ref v3))
			{
				_configService.Current.ShowUidInDtrTooltip = v3;
				_configService.Save();
			}
			DrawHelpIcon("Shows user IDs when hovering over the server info bar");
			if (v3)
			{
				ImGui.Indent(20f);
				bool v4 = _configService.Current.PreferNoteInDtrTooltip;
				if (ImGui.Checkbox("Prefer notes over player names in tooltip", ref v4))
				{
					_configService.Current.PreferNoteInDtrTooltip = v4;
					_configService.Save();
				}
				ImGui.Unindent(20f);
			}
			bool v5 = _configService.Current.UseColorsInDtr;
			if (ImGui.Checkbox("Color-code the Server Info Bar entry according to status", ref v5))
			{
				_configService.Current.UseColorsInDtr = v5;
				_configService.Save();
			}
			DrawHelpIcon("Changes the color of the server info bar based on connection status");
			if (v5)
			{
				ImGui.Spacing();
				DrawColorSelection();
			}
			ImGui.Spacing();
			ImGui.Separator();
			ImGui.Spacing();
			DrawVisibilitySettings();
		});
	}

	private void DrawCollapsibleSection(string title, ref bool collapsed, Action drawContent)
	{
		_ = collapsed;
		string arrowChar = (collapsed ? "►" : "▼");
		ImGui.PushStyleColor(ImGuiCol.Text, Vector4.One);
		ImGui.PushFont(UiBuilder.DefaultFont);
		ImU8String label = new ImU8String(10, 3);
		label.AppendFormatted(arrowChar);
		label.AppendLiteral(" ");
		label.AppendFormatted(title);
		label.AppendLiteral("##header_");
		label.AppendFormatted(title);
		if (ImGui.Button(label, new Vector2(-1f, 0f)))
		{
			collapsed = !collapsed;
		}
		ImGui.PopFont();
		ImGui.PopStyleColor();
		if (!collapsed)
		{
			ImGui.Spacing();
			ImGui.Indent(16f);
			drawContent();
			ImGui.Unindent(16f);
		}
	}

	private void DrawHelpIcon(string tooltip)
	{
		ImGui.SameLine();
		ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1f));
		ImGui.Text("(?)");
		ImGui.PopStyleColor();
		if (ImGui.IsItemHovered())
		{
			ImGui.SetTooltip(tooltip);
		}
	}

	private void DrawColorSelection()
	{
		ImGui.Text("Color Options:");
		ImGui.Spacing();
		(string, DtrEntry.Colors)[] colors = new(string, DtrEntry.Colors)[3]
		{
			("Default", _configService.Current.DtrColorsDefault),
			("Not Connected", _configService.Current.DtrColorsNotConnected),
			("Pairs in Range", _configService.Current.DtrColorsPairsInRange)
		};
		for (int i = 0; i < colors.Length; i++)
		{
			(string, DtrEntry.Colors) tuple = colors[i];
			string name = tuple.Item1;
			DtrEntry.Colors colorConfig = tuple.Item2;
			Vector3 foregroundColor = ConvertColorToVector3(colorConfig.Foreground);
			Vector3 glowColor = ConvertColorToVector3(colorConfig.Glow);
			ImU8String text = new ImU8String(1, 1);
			text.AppendFormatted(name);
			text.AppendLiteral(":");
			ImGui.Text(text);
			ImGui.SameLine();
			text = new ImU8String(5, 1);
			text.AppendLiteral("##fg_");
			text.AppendFormatted(i);
			if (ImGui.ColorEdit3(text, ref foregroundColor, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel))
			{
				DtrEntry.Colors newColor = new DtrEntry.Colors(ConvertVector3ToColor(foregroundColor), colorConfig.Glow);
				SetDtrColor(i, newColor);
			}
			ImGui.SameLine();
			text = new ImU8String(7, 1);
			text.AppendLiteral("##glow_");
			text.AppendFormatted(i);
			if (ImGui.ColorEdit3(text, ref glowColor, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel))
			{
				DtrEntry.Colors newColor = new DtrEntry.Colors(colorConfig.Foreground, ConvertVector3ToColor(glowColor));
				SetDtrColor(i, newColor);
			}
			if (i < colors.Length - 1)
			{
				ImGui.Spacing();
			}
		}
	}

	private void DrawVisibilitySettings()
	{
		bool showVisibleSeparately = _configService.Current.ShowVisibleUsersSeparately;
		if (ImGui.Checkbox("Show separate Visible group", ref showVisibleSeparately))
		{
			_configService.Current.ShowVisibleUsersSeparately = showVisibleSeparately;
			_configService.Save();
		}
		DrawHelpIcon("Groups visible users in a separate section");
		if (showVisibleSeparately)
		{
			ImGui.Indent(20f);
			bool showSyncshellInVisible = _configService.Current.ShowSyncshellUsersInVisible;
			if (ImGui.Checkbox("Show Syncshell Users in Visible Group", ref showSyncshellInVisible))
			{
				_configService.Current.ShowSyncshellUsersInVisible = showSyncshellInVisible;
				_configService.Save();
			}
			ImGui.Unindent(20f);
		}
		bool showOfflineSeparately = _configService.Current.ShowOfflineUsersSeparately;
		if (ImGui.Checkbox("Show separate Offline group", ref showOfflineSeparately))
		{
			_configService.Current.ShowOfflineUsersSeparately = showOfflineSeparately;
			_configService.Save();
		}
		DrawHelpIcon("Groups offline users in a separate section");
		if (showOfflineSeparately)
		{
			ImGui.Indent(20f);
			bool showSyncshellOfflineSeparately = _configService.Current.ShowSyncshellOfflineUsersSeparately;
			if (ImGui.Checkbox("Show separate Offline group for Syncshell users", ref showSyncshellOfflineSeparately))
			{
				_configService.Current.ShowSyncshellOfflineUsersSeparately = showSyncshellOfflineSeparately;
				_configService.Save();
			}
			ImGui.Unindent(20f);
		}
		bool groupSyncshells = _configService.Current.GroupUpSyncshells;
		if (ImGui.Checkbox("Group up all syncshells in one folder", ref groupSyncshells))
		{
			_configService.Current.GroupUpSyncshells = groupSyncshells;
			_configService.Save();
		}
		DrawHelpIcon("Groups all syncshell users together in one folder");
		bool showPlayerNameForVisible = !_configService.Current.ShowCharacterNameInsteadOfNotesForVisible;
		if (ImGui.Checkbox("Show player name for visible players", ref showPlayerNameForVisible))
		{
			_configService.Current.ShowCharacterNameInsteadOfNotesForVisible = !showPlayerNameForVisible;
			_configService.Save();
		}
		DrawHelpIcon("Shows character names instead of notes for visible players");
		bool showProfileStatus = _configService.Current.ShowProfileStatusInPairList;
		if (ImGui.Checkbox("Show profile status in pair list", ref showProfileStatus))
		{
			_configService.Current.ShowProfileStatusInPairList = showProfileStatus;
			_configService.Save();
		}
		DrawHelpIcon("Shows the first line of users' Mare profiles as a status message in the pair listing");
		using (ImRaii.Disabled(!showProfileStatus))
		{
			ImGui.Indent(20f);
			int statusMaxLength = _configService.Current.ProfileStatusMaxLength;
			ImGui.SetNextItemWidth(150f);
			if (ImGui.InputInt("Max status length", ref statusMaxLength))
			{
				_configService.Current.ProfileStatusMaxLength = Math.Max(10, Math.Min(100, statusMaxLength));
				_configService.Save();
			}
			DrawHelpIcon("Maximum number of characters to show from profile status (10-100). NSFW profiles are automatically hidden.");
			ImGui.Unindent(20f);
		}
		bool allowNsfwProfiles = _configService.Current.ProfilesAllowNsfw;
		if (ImGui.Checkbox("Show NSFW profiles", ref allowNsfwProfiles))
		{
			_configService.Current.ProfilesAllowNsfw = allowNsfwProfiles;
			_configService.Save();
		}
		DrawHelpIcon("When enabled, allows viewing of profiles marked as NSFW. When disabled, NSFW profiles show a placeholder message instead.");
	}

	private void DrawPerformanceTab()
	{
		_uiSharedService.BigText("Performance Settings");
		UiSharedService.TextWrapped("The configuration options here are to give you more informed warnings and automation when it comes to other performance-intensive synced players.");
		ImGui.Dummy(new Vector2(10f));
		ImGui.Separator();
		ImGui.Dummy(new Vector2(10f));
		bool showPerformanceIndicator = _playerPerformanceConfigService.Current.ShowPerformanceIndicator;
		if (ImGui.Checkbox("Show performance indicator", ref showPerformanceIndicator))
		{
			_playerPerformanceConfigService.Current.ShowPerformanceIndicator = showPerformanceIndicator;
			_playerPerformanceConfigService.Save();
		}
		DrawHelpIcon("Will show a performance indicator when players exceed defined thresholds in Mare's UI.\nWill use warning thresholds.");
		bool warnOnExceedingThresholds = _playerPerformanceConfigService.Current.WarnOnExceedingThresholds;
		if (ImGui.Checkbox("Warn on loading in players exceeding performance thresholds", ref warnOnExceedingThresholds))
		{
			_playerPerformanceConfigService.Current.WarnOnExceedingThresholds = warnOnExceedingThresholds;
			_playerPerformanceConfigService.Save();
		}
		DrawHelpIcon("Mare will print a warning in chat once per session of meeting those people. Will not warn on players with preferred permissions.");
		using (ImRaii.Disabled(!warnOnExceedingThresholds && !showPerformanceIndicator))
		{
			ImGui.Indent(20f);
			bool warnOnPref = _playerPerformanceConfigService.Current.WarnOnPreferredPermissionsExceedingThresholds;
			if (ImGui.Checkbox("Warn/Indicate also on players with preferred permissions", ref warnOnPref))
			{
				_playerPerformanceConfigService.Current.WarnOnPreferredPermissionsExceedingThresholds = warnOnPref;
				_playerPerformanceConfigService.Save();
			}
			DrawHelpIcon("Mare will also print warnings and show performance indicator for players where you enabled preferred permissions. If warning in general is disabled, this will not produce any warnings.");
			ImGui.Unindent(20f);
		}
		using (ImRaii.Disabled(!showPerformanceIndicator && !warnOnExceedingThresholds))
		{
			int vram = _playerPerformanceConfigService.Current.VRAMSizeWarningThresholdMiB;
			int tris = _playerPerformanceConfigService.Current.TrisWarningThresholdThousands;
			ImGui.SetNextItemWidth(150f);
			if (ImGui.InputInt("Warning VRAM threshold", ref vram))
			{
				_playerPerformanceConfigService.Current.VRAMSizeWarningThresholdMiB = vram;
				_playerPerformanceConfigService.Save();
			}
			ImGui.SameLine();
			ImGui.Text("(MiB)");
			DrawHelpIcon("Limit in MiB of approximate VRAM usage to trigger warning or performance indicator on UI.\nDefault: 375 MiB");
			ImGui.SetNextItemWidth(150f);
			if (ImGui.InputInt("Warning Triangle threshold", ref tris))
			{
				_playerPerformanceConfigService.Current.TrisWarningThresholdThousands = tris;
				_playerPerformanceConfigService.Save();
			}
			ImGui.SameLine();
			ImGui.Text("(thousand triangles)");
			DrawHelpIcon("Limit in approximate used triangles from mods to trigger warning or performance indicator on UI.\nDefault: 165 thousand");
		}
		ImGui.Spacing();
		ImGui.Separator();
		ImGui.Spacing();
		_uiSharedService.BigText("Instance Settings");
		UiSharedService.TextWrapped("Configure automatic pausing during instanced content to avoid potential issues.");
		ImGui.Dummy(new Vector2(5f));
		bool pauseDuringInstances = _configService.Current.PauseSyncingDuringInstances;
		if (ImGui.Checkbox("Pause syncing during instances", ref pauseDuringInstances))
		{
			_configService.Current.PauseSyncingDuringInstances = pauseDuringInstances;
			_configService.Save();
		}
		DrawHelpIcon("Automatically pauses Mare syncing when entering duties, dungeons, trials, raids, PvP content, and other instanced areas.\nThis can help prevent potential issues or performance problems during challenging content.");
		ImGui.Spacing();
		ImGui.Separator();
		ImGui.Spacing();
		bool autoPause = _playerPerformanceConfigService.Current.AutoPausePlayersExceedingThresholds;
		if (ImGui.Checkbox("Automatically pause players exceeding thresholds", ref autoPause))
		{
			_playerPerformanceConfigService.Current.AutoPausePlayersExceedingThresholds = autoPause;
			_playerPerformanceConfigService.Save();
		}
		DrawHelpIcon("When enabled, it will automatically pause all players without preferred permissions that exceed the thresholds defined below.\nWill print a warning in chat when a player got paused automatically.\n\nWarning: this will not automatically unpause those people again, you will have to do this manually.");
		using (ImRaii.Disabled(!autoPause))
		{
			ImGui.Indent(20f);
			bool autoPauseEveryone = _playerPerformanceConfigService.Current.AutoPausePlayersWithPreferredPermissionsExceedingThresholds;
			if (ImGui.Checkbox("Automatically pause also players with preferred permissions", ref autoPauseEveryone))
			{
				_playerPerformanceConfigService.Current.AutoPausePlayersWithPreferredPermissionsExceedingThresholds = autoPauseEveryone;
				_playerPerformanceConfigService.Save();
			}
			DrawHelpIcon("When enabled, will automatically pause all players regardless of preferred permissions that exceed thresholds defined below.\nWarning: this will not automatically unpause those people again, you will have to do this manually.");
			int vramAuto = _playerPerformanceConfigService.Current.VRAMSizeAutoPauseThresholdMiB;
			int trisAuto = _playerPerformanceConfigService.Current.TrisAutoPauseThresholdThousands;
			ImGui.SetNextItemWidth(150f);
			if (ImGui.InputInt("Auto Pause VRAM threshold", ref vramAuto))
			{
				_playerPerformanceConfigService.Current.VRAMSizeAutoPauseThresholdMiB = vramAuto;
				_playerPerformanceConfigService.Save();
			}
			ImGui.SameLine();
			ImGui.Text("(MiB)");
			DrawHelpIcon("When a loading in player and their VRAM usage exceeds this amount, automatically pauses the synced player.\nDefault: 550 MiB");
			ImGui.SetNextItemWidth(150f);
			if (ImGui.InputInt("Auto Pause Triangle threshold", ref trisAuto))
			{
				_playerPerformanceConfigService.Current.TrisAutoPauseThresholdThousands = trisAuto;
				_playerPerformanceConfigService.Save();
			}
			ImGui.SameLine();
			ImGui.Text("(thousand triangles)");
			DrawHelpIcon("When a loading in player and their triangle count exceeds this amount, automatically pauses the synced player.\nDefault: 250 thousand");
			ImGui.Unindent(20f);
		}
		ImGui.Spacing();
		ImGui.Separator();
		ImGui.Spacing();
		_uiSharedService.BigText("Whitelisted UIDs");
		UiSharedService.TextWrapped("The entries in the list below will be ignored for all warnings and auto pause operations.");
		ImGui.Spacing();
		ImGui.SetNextItemWidth(200f);
		ImGui.InputText("##ignoreuid", ref _uidToAddForIgnore, 20);
		ImGui.SameLine();
		using (ImRaii.Disabled(string.IsNullOrEmpty(_uidToAddForIgnore)))
		{
			if (ImGui.Button("Add UID/Vanity ID to whitelist"))
			{
				if (!_playerPerformanceConfigService.Current.UIDsToIgnore.Contains<string>(_uidToAddForIgnore, StringComparer.Ordinal))
				{
					_playerPerformanceConfigService.Current.UIDsToIgnore.Add(_uidToAddForIgnore);
					_playerPerformanceConfigService.Save();
				}
				_uidToAddForIgnore = string.Empty;
			}
		}
		DrawHelpIcon("Hint: UIDs are case sensitive.");
		List<string> playerList = _playerPerformanceConfigService.Current.UIDsToIgnore;
		ImGui.SetNextItemWidth(200f);
		using (ImRaii.IEndObject lb = ImRaii.ListBox("UID whitelist"))
		{
			if (lb)
			{
				for (int i = 0; i < playerList.Count; i++)
				{
					bool shouldBeSelected = _selectedEntry == i;
					if (ImGui.Selectable(playerList[i] + "##" + i, shouldBeSelected))
					{
						_selectedEntry = i;
					}
				}
			}
		}
		using (ImRaii.Disabled(_selectedEntry == -1))
		{
			if (ImGui.Button("Delete selected UID"))
			{
				_playerPerformanceConfigService.Current.UIDsToIgnore.RemoveAt(_selectedEntry);
				_selectedEntry = -1;
				_playerPerformanceConfigService.Save();
			}
		}
	}

	private void DrawStorageTab()
	{
		_uiSharedService.BigText("Export MCDF");
		ImGui.Spacing();
		UiSharedService.ColorTextWrapped("Exporting MCDF has moved.", ImGuiColors.DalamudYellow);
		ImGui.Spacing();
		UiSharedService.TextWrapped("It is now found in the Main UI under \"Your User Menu\" (");
		ImGui.SameLine();
		using (ImRaii.PushFont(UiBuilder.IconFont))
		{
			ImGui.Text(FontAwesomeIcon.UserCog.ToIconString());
		}
		ImGui.SameLine();
		UiSharedService.TextWrapped(") -> \"Character Data Hub\".");
		if (ImGui.Button("Open Mare Character Data Hub"))
		{
			base.Mediator.Publish(new UiToggleMessage(typeof(CharaDataHubUi)));
		}
		UiSharedService.TextWrapped("Note: this entry will be removed in the near future. Please use the Main UI to open the Character Data Hub.");
		ImGui.Spacing();
		ImGui.Separator();
		ImGui.Spacing();
		_uiSharedService.BigText("Storage");
		UiSharedService.TextWrapped("Mare stores downloaded files from paired people permanently. This is to improve loading performance and requiring less downloads. The storage governs itself by clearing data beyond the set storage size. Please set the storage size accordingly. It is not necessary to manually clear the storage.");
		ImGui.Spacing();
		_uiSharedService.DrawFileScanState();
		ImGui.Text("Monitoring Penumbra Folder: " + (_cacheMonitor.PenumbraWatcher?.Path ?? "Not monitoring"));
		if (string.IsNullOrEmpty(_cacheMonitor.PenumbraWatcher?.Path))
		{
			ImGui.SameLine();
			if (ImGui.Button("Try to reinitialize Monitor##penumbra"))
			{
				_cacheMonitor.StartPenumbraWatcher(_ipcManager.Penumbra.ModDirectory);
			}
		}
		ImGui.Text("Monitoring Mare Storage Folder: " + (_cacheMonitor.MareWatcher?.Path ?? "Not monitoring"));
		if (string.IsNullOrEmpty(_cacheMonitor.MareWatcher?.Path))
		{
			ImGui.SameLine();
			if (ImGui.Button("Try to reinitialize Monitor##xivsync"))
			{
				_cacheMonitor.StartMareWatcher(_configService.Current.CacheFolder);
			}
		}
		if (_cacheMonitor.MareWatcher == null || _cacheMonitor.PenumbraWatcher == null)
		{
			if (ImGui.Button("Resume Monitoring"))
			{
				_cacheMonitor.StartMareWatcher(_configService.Current.CacheFolder);
				_cacheMonitor.StartPenumbraWatcher(_ipcManager.Penumbra.ModDirectory);
				_cacheMonitor.InvokeScan();
			}
			DrawHelpIcon("Attempts to resume monitoring for both Penumbra and Mare Storage.\nResuming the monitoring will also force a full scan to run.\nIf the button remains present after clicking it, consult /xllog for errors");
		}
		else
		{
			using (ImRaii.Disabled(!UiSharedService.CtrlPressed()))
			{
				if (ImGui.Button("Stop Monitoring"))
				{
					_cacheMonitor.StopMonitoring();
				}
			}
			DrawHelpIcon("Stops the monitoring for both Penumbra and Mare Storage.\nDo not stop the monitoring, unless you plan to move the Penumbra and Mare Storage folders, to ensure correct functionality of Mare.\nIf you stop the monitoring to move folders around, resume it after you are finished moving the files.\n\nHold CTRL to enable this button");
		}
		_uiSharedService.DrawCacheDirectorySetting();
		ImU8String text;
		if (_cacheMonitor.FileCacheSize >= 0)
		{
			text = new ImU8String(34, 1);
			text.AppendLiteral("Currently utilized local storage: ");
			text.AppendFormatted(UiSharedService.ByteToString(_cacheMonitor.FileCacheSize));
			ImGui.Text(text);
		}
		else
		{
			text = new ImU8String(48, 0);
			text.AppendLiteral("Currently utilized local storage: Calculating...");
			ImGui.Text(text);
		}
		text = new ImU8String(31, 1);
		text.AppendLiteral("Remaining space free on drive: ");
		text.AppendFormatted(UiSharedService.ByteToString(_cacheMonitor.FileCacheDriveFree));
		ImGui.Text(text);
		bool useFileCompactor = _configService.Current.UseCompactor;
		bool isLinux = _dalamudUtilService.IsWine;
		if (!useFileCompactor && !isLinux)
		{
			UiSharedService.ColorTextWrapped("Hint: To free up space when using Mare consider enabling the File Compactor", ImGuiColors.DalamudYellow);
		}
		using (ImRaii.Disabled(isLinux || !_cacheMonitor.StorageisNTFS))
		{
			if (ImGui.Checkbox("Use file compactor", ref useFileCompactor))
			{
				_configService.Current.UseCompactor = useFileCompactor;
				_configService.Save();
			}
		}
		DrawHelpIcon("The file compactor can massively reduce your saved files. It might incur a minor penalty on loading files on a slow CPU.\nIt is recommended to leave it enabled to save on space.");
		ImGui.SameLine();
		if (!_fileCompactor.MassCompactRunning)
		{
			if (ImGui.Button("Compact all files in storage"))
			{
				Task.Run(delegate
				{
					_fileCompactor.CompactStorage(compress: true);
					_cacheMonitor.RecalculateFileCacheSize(CancellationToken.None);
				});
			}
			DrawHelpIcon("This will run compression on all files in your current Mare Storage.\nYou do not need to run this manually if you keep the file compactor enabled.");
			ImGui.SameLine();
			if (ImGui.Button("Decompact all files in storage"))
			{
				Task.Run(delegate
				{
					_fileCompactor.CompactStorage(compress: false);
					_cacheMonitor.RecalculateFileCacheSize(CancellationToken.None);
				});
			}
			DrawHelpIcon("This will decompress all files in your current Mare Storage.\nUseful when switching to another compaction tool or moving to another OS.");
		}
		else
		{
			ImGui.Text("File compaction in progress...");
		}
		if (isLinux || !_cacheMonitor.StorageisNTFS)
		{
			ImGui.Spacing();
			if (isLinux)
			{
				UiSharedService.ColorTextWrapped("File compactor is not available on Linux/Wine.", ImGuiColors.DalamudRed);
			}
			else
			{
				UiSharedService.ColorTextWrapped("File compactor is only available on NTFS file systems.", ImGuiColors.DalamudRed);
			}
		}
		ImGui.Spacing();
		ImGui.Separator();
		ImGui.Spacing();
		double maxCacheInGiB = _configService.Current.MaxLocalCacheInGiB;
		ImGui.SetNextItemWidth(150f);
		if (ImGui.InputDouble("Max local cache size (GiB)", ref maxCacheInGiB, 0.1, 1.0, "%.1f"))
		{
			_configService.Current.MaxLocalCacheInGiB = Math.Max(0.1, maxCacheInGiB);
			_configService.Save();
		}
		DrawHelpIcon("Maximum size of the local file cache in GiB.\nWhen exceeded, older files will be automatically removed.");
		if (ImGui.Button("Clear entire file cache"))
		{
			ImGui.OpenPopup("Confirm Clear Cache");
		}
		DrawHelpIcon("This will delete all cached files and force re-download when needed.\nThis operation cannot be undone.");
		if (!ImGui.BeginPopupModal("Confirm Clear Cache"))
		{
			return;
		}
		ImGui.Text("Are you sure you want to clear the entire file cache?");
		ImGui.Text("This will delete all downloaded files and cannot be undone.");
		ImGui.Spacing();
		if (ImGui.Button("Yes, clear cache"))
		{
			Task.Run(delegate
			{
				string[] files = Directory.GetFiles(_configService.Current.CacheFolder);
				for (int i = 0; i < files.Length; i++)
				{
					File.Delete(files[i]);
				}
			});
			ImGui.CloseCurrentPopup();
		}
		ImGui.SameLine();
		if (ImGui.Button("Cancel"))
		{
			ImGui.CloseCurrentPopup();
		}
		ImGui.EndPopup();
	}

	private void DrawTransfersTab()
	{
		_uiSharedService.BigText("Transfer Settings");
		ImGui.Spacing();
		int downloadSpeedLimit = _configService.Current.DownloadSpeedLimitInBytes;
		ImGui.Text("Global Download Speed Limit");
		ImGui.SameLine();
		ImGui.SetNextItemWidth(150f);
		if (ImGui.InputInt("###speedlimit", ref downloadSpeedLimit))
		{
			_configService.Current.DownloadSpeedLimitInBytes = downloadSpeedLimit;
			_configService.Save();
			base.Mediator.Publish(new DownloadLimitChangedMessage());
		}
		ImGui.SameLine();
		ImGui.SetNextItemWidth(100f);
		_uiSharedService.DrawCombo("###speed", [DownloadSpeeds.Bps, DownloadSpeeds.KBps, DownloadSpeeds.MBps],
            (s) => s switch
		{
			DownloadSpeeds.Bps => "Byte/s", 
			DownloadSpeeds.KBps => "KB/s", 
			DownloadSpeeds.MBps => "MB/s", 
			_ => "Unknown", 
        }, (s) =>
		{
			_configService.Current.DownloadSpeedType = s;
			_configService.Save();
            Mediator.Publish(new DownloadLimitChangedMessage());
		}, _configService.Current.DownloadSpeedType);
		ImGui.SameLine();
		ImGui.Text("(0 = No limit)");
		int maxParallelDownloads = _configService.Current.ParallelDownloads;
		if (ImGui.SliderInt("Maximum Parallel Downloads", ref maxParallelDownloads, 1, 10))
		{
			_configService.Current.ParallelDownloads = maxParallelDownloads;
			_configService.Save();
		}
		int maxParallelUploads = _configService.Current.ParallelUploads;
		if (ImGui.SliderInt("Maximum Parallel Uploads", ref maxParallelUploads, 1, 6))
		{
			_configService.Current.ParallelUploads = maxParallelUploads;
			_configService.Save();
		}
		DrawHelpIcon("Number of files that can be uploaded simultaneously. Higher values may improve upload speed but use more resources.");
		bool useAlternativeUpload = _configService.Current.UseAlternativeFileUpload;
		if (ImGui.Checkbox("Use Alternative Upload Method", ref useAlternativeUpload))
		{
			_configService.Current.UseAlternativeFileUpload = useAlternativeUpload;
			_configService.Save();
		}
		DrawHelpIcon("This will attempt to upload files in one go instead of a stream. Typically not necessary to enable. Use if you have upload issues.");
		ImGui.Spacing();
		ImGui.Separator();
		ImGui.Spacing();
		_uiSharedService.BigText("Transfer UI");
		bool showTransferWindow = _configService.Current.ShowTransferWindow;
		if (ImGui.Checkbox("Show separate transfer window", ref showTransferWindow))
		{
			_configService.Current.ShowTransferWindow = showTransferWindow;
			_configService.Save();
		}
		DrawHelpIcon("The download window will show the current progress of outstanding downloads.\n\nWhat do W/Q/P/D stand for?\nW = Waiting for Slot (see Maximum Parallel Downloads)\nQ = Queued on Server, waiting for queue ready signal\nP = Processing download (aka downloading)\nD = Decompressing download");
		using (ImRaii.Disabled(!showTransferWindow))
		{
			ImGui.Indent(20f);
			bool editTransferWindowPosition = _uiSharedService.EditTrackerPosition;
			if (ImGui.Checkbox("Edit Transfer Window position", ref editTransferWindowPosition))
			{
				_uiSharedService.EditTrackerPosition = editTransferWindowPosition;
			}
			ImGui.Unindent(20f);
		}
		bool showTransferBars = _configService.Current.ShowTransferBars;
		if (ImGui.Checkbox("Show transfer bars", ref showTransferBars))
		{
			_configService.Current.ShowTransferBars = showTransferBars;
			_configService.Save();
		}
		DrawHelpIcon("Shows download progress bars in the main UI");
		using (ImRaii.Disabled(!showTransferBars))
		{
			ImGui.Indent(20f);
			int transferBarsWidth = _configService.Current.TransferBarsWidth;
			ImGui.SetNextItemWidth(150f);
			if (ImGui.InputInt("Transfer bars width", ref transferBarsWidth))
			{
				_configService.Current.TransferBarsWidth = Math.Max(100, transferBarsWidth);
				_configService.Save();
			}
			int transferBarsHeight = _configService.Current.TransferBarsHeight;
			ImGui.SetNextItemWidth(150f);
			if (ImGui.InputInt("Transfer bars height", ref transferBarsHeight))
			{
				_configService.Current.TransferBarsHeight = Math.Max(8, transferBarsHeight);
				_configService.Save();
			}
			bool showTransferBarsText = _configService.Current.TransferBarsShowText;
			if (ImGui.Checkbox("Show text on transfer bars", ref showTransferBarsText))
			{
				_configService.Current.TransferBarsShowText = showTransferBarsText;
				_configService.Save();
			}
			ImGui.Unindent(20f);
		}
	}

	private void DrawServiceSettingsTab()
	{
		if (_apiController.ServerAlive)
		{
			_uiSharedService.BigText("Service Actions");
			ImGui.Spacing();
			if (ImGui.Button("Delete all my files"))
			{
				_deleteFilesPopupModalShown = true;
				ImGui.OpenPopup("Delete all your files?");
			}
			DrawHelpIcon("Completely deletes all your uploaded files on the service.");
			if (ImGui.BeginPopupModal("Delete all your files?", ref _deleteFilesPopupModalShown, UiSharedService.PopupWindowFlags))
			{
				UiSharedService.TextWrapped("All your own uploaded files on the service will be deleted.\nThis operation cannot be undone.");
				ImGui.Text("Are you sure you want to continue?");
				ImGui.Separator();
				ImGui.Spacing();
				float buttonSize = (ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X - ImGui.GetStyle().ItemSpacing.X) / 2f;
				if (ImGui.Button("Delete everything", new Vector2(buttonSize, 0f)))
				{
					Task.Run((Func<Task?>)_fileTransferManager.DeleteAllFiles);
					_deleteFilesPopupModalShown = false;
				}
				ImGui.SameLine();
				if (ImGui.Button("Cancel##cancelDelete", new Vector2(buttonSize, 0f)))
				{
					_deleteFilesPopupModalShown = false;
				}
				UiSharedService.SetScaledWindowSize(325f);
				ImGui.EndPopup();
			}
			ImGui.SameLine();
			if (ImGui.Button("Delete account"))
			{
				_deleteAccountPopupModalShown = true;
				ImGui.OpenPopup("Delete your account?");
			}
			DrawHelpIcon("Completely deletes your account and all uploaded files to the service.");
			if (ImGui.BeginPopupModal("Delete your account?", ref _deleteAccountPopupModalShown, UiSharedService.PopupWindowFlags))
			{
				UiSharedService.TextWrapped("Your account and all associated files and data on the service will be deleted.");
				UiSharedService.TextWrapped("Your UID will be removed from all pairing lists.");
				ImGui.Text("Are you sure you want to continue?");
				ImGui.Separator();
				ImGui.Spacing();
				float buttonSize = (ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X - ImGui.GetStyle().ItemSpacing.X) / 2f;
				if (ImGui.Button("Delete account", new Vector2(buttonSize, 0f)))
				{
					Task.Run((Func<Task?>)_apiController.UserDelete);
					_deleteAccountPopupModalShown = false;
					base.Mediator.Publish(new SwitchToIntroUiMessage());
				}
				ImGui.SameLine();
				if (ImGui.Button("Cancel##cancelDelete", new Vector2(buttonSize, 0f)))
				{
					_deleteAccountPopupModalShown = false;
				}
				UiSharedService.SetScaledWindowSize(325f);
				ImGui.EndPopup();
			}
			ImGui.Separator();
			ImGui.Spacing();
		}
		_uiSharedService.BigText("Service & Character Settings");
		ImGui.Spacing();
		bool sendCensus = _serverConfigurationManager.SendCensusData;
		if (ImGui.Checkbox("Send Statistical Census Data", ref sendCensus))
		{
			_serverConfigurationManager.SendCensusData = sendCensus;
		}
		DrawHelpIcon("This will allow sending census data to the currently connected service.\n\nCensus data contains:\n- Current World\n- Current Gender\n- Current Race\n- Current Clan (this is not your Free Company, this is e.g. Keeper or Seeker for Miqo'te)\n\nThe census data is only saved temporarily and will be removed from the server on disconnect. It is stored temporarily associated with your UID while you are connected.\n\nIf you do not wish to participate in the statistical census, untick this box and reconnect to the server.");
		ImGui.Spacing();
		int idx = _uiSharedService.DrawServiceSelection();
		if (_lastSelectedServerIndex != idx)
		{
			_uiSharedService.ResetOAuthTasksState();
			_lastSelectedServerIndex = idx;
		}
		ImGui.Spacing();
		ServerStorage selectedServer = _serverConfigurationManager.GetServerByIndex(idx);
		if (selectedServer == _serverConfigurationManager.CurrentServer)
		{
			UiSharedService.ColorTextWrapped("For any changes to be applied to the current service you need to reconnect to the service.", ImGuiColors.DalamudYellow);
		}
		bool useOauth = selectedServer.UseOAuth2;
		if (ImGui.BeginTabBar("serverTabBar"))
		{
			if (ImGui.BeginTabItem("Character Management"))
			{
				DrawCharacterManagement(selectedServer, useOauth);
				ImGui.EndTabItem();
			}
			if (ImGui.BeginTabItem("Service Configuration"))
			{
				DrawServiceConfiguration(selectedServer, useOauth);
				ImGui.EndTabItem();
			}
			if (ImGui.BeginTabItem("Permission Settings"))
			{
				DrawPermissionSettings(selectedServer);
				ImGui.EndTabItem();
			}
			ImGui.EndTabBar();
		}
	}

	private void DrawDebugTab()
	{
		_uiSharedService.BigText("Debug");
		ImGui.Spacing();
		if (ImGui.Button("Copy Last created Character Data to clipboard"))
		{
			ImGui.SetClipboardText("Debug functionality coming soon...");
		}
		DrawHelpIcon("Use this when reporting mods being rejected from the server.");
		_uiSharedService.DrawCombo("Log Level", Enum.GetValues<LogLevel>(), (LogLevel l) => l.ToString(), delegate(LogLevel l)
		{
			_configService.Current.LogLevel = l;
			_configService.Save();
		}, _configService.Current.LogLevel);
		bool logPerformance = _configService.Current.LogPerformance;
		if (ImGui.Checkbox("Log Performance Counters", ref logPerformance))
		{
			_configService.Current.LogPerformance = logPerformance;
			_configService.Save();
		}
		DrawHelpIcon("Enabling this can incur a (slight) performance impact. Enabling this for extended periods of time is not recommended.");
		using (ImRaii.Disabled(!logPerformance))
		{
			if (ImGui.Button("Print Performance Stats to /xllog"))
			{
				_performanceCollector.PrintPerformanceStats();
			}
			ImGui.SameLine();
			if (ImGui.Button("Print Performance Stats (last 60s) to /xllog"))
			{
				_performanceCollector.PrintPerformanceStats(60);
			}
		}
		bool stopWhining = _configService.Current.DebugStopWhining;
		if (ImGui.Checkbox("Do not notify for modified game files or enabled LOD", ref stopWhining))
		{
			_configService.Current.DebugStopWhining = stopWhining;
			_configService.Save();
		}
		DrawHelpIcon("Having modified game files will still mark your logs with UNSUPPORTED and you will not receive support, message shown or not.\nKeeping LOD enabled can lead to more crashes. Use at your own risk.");
	}

	private void DrawLogsTab()
	{
		IEnumerable<LogEntry> logs = _uiLoggingProvider.GetRecentLogs();
		if (logs.Count() == 0)
		{
			_logger.LogInformation("ModernSettingsUi: Log viewer opened - testing if logs are captured");
		}
		ImGui.Text("Real-time Log Viewer");
		ImGui.SameLine();
		ImU8String text = new ImU8String(7, 1);
		text.AppendLiteral("(");
		text.AppendFormatted(logs.Count());
		text.AppendLiteral(" logs)");
		ImGui.Text(text);
		ImGui.Separator();
		ImGui.Text("Search:");
		ImGui.SameLine();
		ImGui.SetNextItemWidth(200f);
		ImGui.InputText("##LogSearch", ref _logFilterText, 100);
		ImGui.SameLine();
		if (ImGui.Button("Copy to Clipboard"))
		{
			IEnumerable<LogEntry> filteredLogs = GetFilteredLogs(logs);
			CopyLogsToClipboard(filteredLogs);
		}
		UiSharedService.AttachToolTip("Copy filtered logs to clipboard");
		ImGui.Separator();
		ImGui.Text("Filter by Level:");
		ImGui.SameLine();
		bool showTrace = _configService.Current.ShowTraceLogs;
		bool showDebug = _configService.Current.ShowDebugLogs;
		bool showInfo = _configService.Current.ShowInfoLogs;
		bool showWarning = _configService.Current.ShowWarningLogs;
		bool showError = _configService.Current.ShowErrorLogs;
		if (ImGui.Checkbox("Trace", ref showTrace))
		{
			_configService.Current.ShowTraceLogs = showTrace;
			_configService.Save();
		}
		ImGui.SameLine();
		if (ImGui.Checkbox("Debug", ref showDebug))
		{
			_configService.Current.ShowDebugLogs = showDebug;
			_configService.Save();
		}
		ImGui.SameLine();
		if (ImGui.Checkbox("Info", ref showInfo))
		{
			_configService.Current.ShowInfoLogs = showInfo;
			_configService.Save();
		}
		ImGui.SameLine();
		if (ImGui.Checkbox("Warning", ref showWarning))
		{
			_configService.Current.ShowWarningLogs = showWarning;
			_configService.Save();
		}
		ImGui.SameLine();
		if (ImGui.Checkbox("Error", ref showError))
		{
			_configService.Current.ShowErrorLogs = showError;
			_configService.Save();
		}
		ImGui.Separator();
		ImGui.Text("Filter by Category:");
		ImGui.Columns(3, "CategoryColumns", border: false);
		ImGui.Checkbox("Plugin", ref _filterPlugin);
		ImGui.Checkbox("Auth", ref _filterAuth);
		ImGui.Checkbox("Services", ref _filterServices);
		ImGui.NextColumn();
		ImGui.Checkbox("Network", ref _filterNetwork);
		ImGui.Checkbox("File", ref _filterFile);
		ImGui.Checkbox("Other", ref _filterOther);
		ImGui.Columns();
		ImGui.Separator();
		bool autoScroll = _configService.Current.LogViewerAutoScroll;
		if (ImGui.Checkbox("Auto-scroll to bottom", ref autoScroll))
		{
			_configService.Current.LogViewerAutoScroll = autoScroll;
			_configService.Save();
		}
		ImGui.SameLine();
		if (ImGui.Button("Clear Logs"))
		{
			logs = new List<LogEntry>();
		}
		ImGui.Separator();
		using ImRaii.IEndObject logChild = ImRaii.Child("LogDisplay", new Vector2(-1f, -1f), border: true);
		if (!logChild.Success)
		{
			return;
		}
		foreach (LogEntry log in GetFilteredLogs(logs))
		{
			Vector4 color = GetLogLevelColor(log.Level);
			string levelText = GetLogLevelText(log.Level);
			string timestamp = log.Timestamp.ToString("HH:mm:ss.fff");
			string category = TruncateCategory(log.Category);
			text = new ImU8String(8, 3);
			text.AppendLiteral("[");
			text.AppendFormatted(timestamp);
			text.AppendLiteral("] [");
			text.AppendFormatted(levelText);
			text.AppendLiteral("] [");
			text.AppendFormatted(category);
			text.AppendLiteral("]");
			ImGui.TextColored(in color, text);
			ImGui.SameLine();
			ImGui.Text(log.Message);
			if (log.Exception != null)
			{
				Vector4 col = ImGuiColors.DalamudRed;
				text = new ImU8String(15, 1);
				text.AppendLiteral("    Exception: ");
				text.AppendFormatted(log.Exception.Message);
				ImGui.TextColored(in col, text);
				if (!string.IsNullOrEmpty(log.Exception.StackTrace))
				{
					col = ImGuiColors.DalamudGrey3;
					text = new ImU8String(4, 1);
					text.AppendLiteral("    ");
					text.AppendFormatted(log.Exception.StackTrace);
					ImGui.TextColored(in col, text);
				}
			}
		}
		if (autoScroll)
		{
			ImGui.SetScrollHereY(1f);
		}
	}

	private static bool ShouldShowLogLevel(LogLevel level, bool showTrace, bool showDebug, bool showInfo, bool showWarning, bool showError)
	{
		return level switch
		{
			LogLevel.Trace => showTrace, 
			LogLevel.Debug => showDebug, 
			LogLevel.Information => showInfo, 
			LogLevel.Warning => showWarning, 
			LogLevel.Error => showError, 
			LogLevel.Critical => showError, 
			_ => true, 
		};
	}

	private static Vector4 GetLogLevelColor(LogLevel level)
	{
		return level switch
		{
			LogLevel.Trace => ImGuiColors.DalamudGrey3, 
			LogLevel.Debug => ImGuiColors.DalamudWhite, 
			LogLevel.Information => ImGuiColors.ParsedBlue, 
			LogLevel.Warning => ImGuiColors.DalamudYellow, 
			LogLevel.Error => ImGuiColors.DalamudRed, 
			LogLevel.Critical => ImGuiColors.DalamudRed, 
			_ => ImGuiColors.DalamudWhite, 
		};
	}

	private static string GetLogLevelText(LogLevel level)
	{
		return level switch
		{
			LogLevel.Trace => "TRC", 
			LogLevel.Debug => "DBG", 
			LogLevel.Information => "INF", 
			LogLevel.Warning => "WRN", 
			LogLevel.Error => "ERR", 
			LogLevel.Critical => "CRT", 
			_ => "UNK", 
		};
	}

	private static string TruncateCategory(string category)
	{
		if (category.Length <= 15)
		{
			return category;
		}
		return category.Substring(0, 12) + "...";
	}

	private static string GetLogCategory(string category)
	{
		string cat = category.ToLowerInvariant();
		if (cat.Contains("plugin") || cat.Contains("ui") || cat.Contains("window"))
		{
			return "Plugin";
		}
		cat = cat;
		if (cat.Contains("auth") || cat.Contains("login") || cat.Contains("token"))
		{
			return "Auth";
		}
		cat = cat;
		if (cat.Contains("service") || cat.Contains("manager") || cat.Contains("controller"))
		{
			return "Services";
		}
		cat = cat;
		if (cat.Contains("network") || cat.Contains("connection") || cat.Contains("signalr") || cat.Contains("api"))
		{
			return "Network";
		}
		cat = cat;
		if (cat.Contains("file") || cat.Contains("cache") || cat.Contains("download") || cat.Contains("upload"))
		{
			return "File";
		}
		return "Other";
	}

	private IEnumerable<LogEntry> GetFilteredLogs(IEnumerable<LogEntry> logs)
	{
		bool showTrace = _configService.Current.ShowTraceLogs;
		bool showDebug = _configService.Current.ShowDebugLogs;
		bool showInfo = _configService.Current.ShowInfoLogs;
		bool showWarning = _configService.Current.ShowWarningLogs;
		bool showError = _configService.Current.ShowErrorLogs;
		return logs.Where(delegate(LogEntry log)
		{
			if (!string.IsNullOrEmpty(_logFilterText) && !log.Message.Contains(_logFilterText, StringComparison.OrdinalIgnoreCase) && !log.Category.Contains(_logFilterText, StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
			return ShouldShowLogLevel(log.Level, showTrace, showDebug, showInfo, showWarning, showError) && GetLogCategory(log.Category) switch
			{
				"Plugin" => _filterPlugin, 
				"Auth" => _filterAuth, 
				"Services" => _filterServices, 
				"Network" => _filterNetwork, 
				"File" => _filterFile, 
				_ => _filterOther, 
			};
		});
	}

	private void CopyLogsToClipboard(IEnumerable<LogEntry> logs)
	{
		try
		{
			StringBuilder logText = new StringBuilder();
			foreach (LogEntry log in logs)
			{
				string timestamp = log.Timestamp.ToString("HH:mm:ss.fff");
				string levelText = GetLogLevelText(log.Level);
				string category = TruncateCategory(log.Category);
				StringBuilder stringBuilder = logText;
				StringBuilder stringBuilder2 = stringBuilder;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(9, 4, stringBuilder);
				handler.AppendLiteral("[");
				handler.AppendFormatted(timestamp);
				handler.AppendLiteral("] [");
				handler.AppendFormatted(levelText);
				handler.AppendLiteral("] [");
				handler.AppendFormatted(category);
				handler.AppendLiteral("] ");
				handler.AppendFormatted(log.Message);
				stringBuilder2.AppendLine(ref handler);
				if (log.Exception != null)
				{
					stringBuilder = logText;
					StringBuilder stringBuilder3 = stringBuilder;
					handler = new StringBuilder.AppendInterpolatedStringHandler(15, 1, stringBuilder);
					handler.AppendLiteral("    Exception: ");
					handler.AppendFormatted(log.Exception.Message);
					stringBuilder3.AppendLine(ref handler);
					if (!string.IsNullOrEmpty(log.Exception.StackTrace))
					{
						stringBuilder = logText;
						StringBuilder stringBuilder4 = stringBuilder;
						handler = new StringBuilder.AppendInterpolatedStringHandler(4, 1, stringBuilder);
						handler.AppendLiteral("    ");
						handler.AppendFormatted(log.Exception.StackTrace);
						stringBuilder4.AppendLine(ref handler);
					}
				}
			}
			if (logText.Length > 0)
			{
				ImGui.SetClipboardText(logText.ToString());
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to copy logs to clipboard");
		}
	}

	private void DrawCharacterManagement(ServerStorage selectedServer, bool useOauth)
	{
		if (selectedServer.SecretKeys.Any() || useOauth)
		{
			UiSharedService.ColorTextWrapped("Characters listed here will automatically connect to the selected Mare service with the settings as provided below. Make sure to enter the character names correctly or use the 'Add current character' button at the bottom.", ImGuiColors.DalamudYellow);
			int i = 0;
			_uiSharedService.DrawUpdateOAuthUIDsButton(selectedServer);
			if (selectedServer.UseOAuth2 && !string.IsNullOrEmpty(selectedServer.OAuthToken) && selectedServer.Authentications.Exists((Authentication u) => u.SecretKeyIdx != -1 && string.IsNullOrEmpty(u.UID)))
			{
				ImGuiHelpers.ScaledDummy(5f);
				UiSharedService.ColorTextWrapped("You have some characters set up for secret keys but not OAuth2. Press \"Convert Secret Keys to UIDs\" to convert them.", ImGuiColors.DalamudRed);
				ImGui.Button("Convert Secret Keys to UIDs");
				ImGuiHelpers.ScaledDummy(5f);
			}
			string youName = _dalamudUtilService.GetPlayerName() ?? string.Empty;
			uint youWorld = _dalamudUtilService.GetHomeWorldId();
			foreach (Authentication item in selectedServer.Authentications.ToList())
			{
				using (ImRaii.PushId("selectedChara" + i))
				{
					ushort worldIdx = (ushort)item.WorldId;
					Dictionary<ushort, string> data = _uiSharedService.WorldData.OrderBy<KeyValuePair<ushort, string>, string>((KeyValuePair<ushort, string> u) => u.Value, StringComparer.Ordinal).ToDictionary((KeyValuePair<ushort, string> k) => k.Key, (KeyValuePair<ushort, string> k) => k.Value);
					if (!data.TryGetValue(worldIdx, out var worldPreview))
					{
						worldPreview = data.First().Value;
					}
					Dictionary<int, SecretKey> keys = new Dictionary<int, SecretKey>();
					if (!useOauth)
					{
						int secretKeyIdx = item.SecretKeyIdx;
						keys = selectedServer.SecretKeys;
						if (!keys.TryGetValue(secretKeyIdx, out var secretKey))
						{
							secretKey = new SecretKey();
						}
					}
					bool thisIsYou = false;
					if (string.Equals(youName, item.CharacterName, StringComparison.OrdinalIgnoreCase) && youWorld == worldIdx)
					{
						thisIsYou = true;
					}
					bool misManaged = false;
					if (selectedServer.UseOAuth2 && !string.IsNullOrEmpty(selectedServer.OAuthToken) && string.IsNullOrEmpty(item.UID))
					{
						misManaged = true;
					}
					if (!selectedServer.UseOAuth2 && item.SecretKeyIdx == -1)
					{
						misManaged = true;
					}
					Vector4 color = ImGuiColors.ParsedGreen;
					string text = (thisIsYou ? "Your Current Character" : string.Empty);
					if (misManaged)
					{
						text = text + " [MISMANAGED (" + (selectedServer.UseOAuth2 ? "No UID Set" : "No Secret Key Set") + ")]";
						color = ImGuiColors.DalamudRed;
					}
					if (selectedServer.Authentications.Where((Authentication e) => e != item).Any((Authentication e) => string.Equals(e.CharacterName, item.CharacterName, StringComparison.Ordinal) && e.WorldId == item.WorldId))
					{
						text += " [DUPLICATE]";
						color = ImGuiColors.DalamudRed;
					}
					if (!string.IsNullOrEmpty(text))
					{
						text = text.Trim();
						_uiSharedService.BigText(text, color);
					}
					string charaName = item.CharacterName;
					if (ImGui.InputText("Character Name", ref charaName, 64))
					{
						item.CharacterName = charaName;
						_serverConfigurationManager.Save();
					}
					_uiSharedService.DrawCombo("World##" + item.CharacterName + i, data, (KeyValuePair<ushort, string> w) => w.Value, delegate(KeyValuePair<ushort, string> w)
					{
						if (item.WorldId != w.Key)
						{
							item.WorldId = w.Key;
							_serverConfigurationManager.Save();
						}
					}, EqualityComparer<KeyValuePair<ushort, string>>.Default.Equals(data.FirstOrDefault((KeyValuePair<ushort, string> f) => f.Key == worldIdx), default(KeyValuePair<ushort, string>)) ? data.First() : data.First((KeyValuePair<ushort, string> f) => f.Key == worldIdx));
					if (!useOauth)
					{
						_uiSharedService.DrawCombo("Secret Key##" + item.CharacterName + i, keys, (KeyValuePair<int, SecretKey> w) => w.Value.FriendlyName, delegate(KeyValuePair<int, SecretKey> w)
						{
							if (w.Key != item.SecretKeyIdx)
							{
								item.SecretKeyIdx = w.Key;
								_serverConfigurationManager.Save();
							}
						}, EqualityComparer<KeyValuePair<int, SecretKey>>.Default.Equals(keys.FirstOrDefault((KeyValuePair<int, SecretKey> f) => f.Key == item.SecretKeyIdx), default(KeyValuePair<int, SecretKey>)) ? keys.First() : keys.First((KeyValuePair<int, SecretKey> f) => f.Key == item.SecretKeyIdx));
					}
					else
					{
						_uiSharedService.DrawUIDComboForAuthentication(i, item, selectedServer.ServerUri, _logger);
					}
					bool isAutoLogin = item.AutoLogin;
					if (ImGui.Checkbox("Automatically login to Mare", ref isAutoLogin))
					{
						item.AutoLogin = isAutoLogin;
						_serverConfigurationManager.Save();
					}
					_uiSharedService.DrawHelpText("When enabled and logging into this character in XIV, Mare will automatically connect to the current service.");
					if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Delete Character") && UiSharedService.CtrlPressed())
					{
						_serverConfigurationManager.RemoveCharacterFromServer(_uiSharedService.DrawServiceSelection(), item);
					}
					UiSharedService.AttachToolTip("Hold CTRL to delete this entry.");
					i++;
					Authentication authentication = item;
					List<Authentication> list = selectedServer.Authentications.ToList();
					if (authentication != list[list.Count - 1])
					{
						ImGuiHelpers.ScaledDummy(5f);
						ImGui.Separator();
						ImGuiHelpers.ScaledDummy(5f);
					}
				}
			}
			if (selectedServer.Authentications.Any())
			{
				ImGui.Separator();
			}
			if (!selectedServer.Authentications.Exists((Authentication c) => string.Equals(c.CharacterName, youName, StringComparison.Ordinal) && c.WorldId == youWorld))
			{
				if (_uiSharedService.IconTextButton(FontAwesomeIcon.User, "Add current character"))
				{
					_serverConfigurationManager.AddCurrentCharacterToServer(_uiSharedService.DrawServiceSelection());
				}
				ImGui.SameLine();
			}
			if (_uiSharedService.IconTextButton(FontAwesomeIcon.Plus, "Add new character"))
			{
				_serverConfigurationManager.AddEmptyCharacterToServer(_uiSharedService.DrawServiceSelection());
			}
		}
		else
		{
			UiSharedService.ColorTextWrapped("You need to add a Secret Key first before adding Characters.", ImGuiColors.DalamudYellow);
		}
	}

	private void DrawSecretKeyManagement(ServerStorage selectedServer)
	{
		foreach (KeyValuePair<int, SecretKey> item in selectedServer.SecretKeys.ToList())
		{
			using (ImRaii.PushId("key" + item.Key))
			{
				string friendlyName = item.Value.FriendlyName;
				if (ImGui.InputText("Secret Key Display Name", ref friendlyName, 255))
				{
					item.Value.FriendlyName = friendlyName;
					_serverConfigurationManager.Save();
				}
				string key = item.Value.Key;
				if (ImGui.InputText("Secret Key", ref key, 64))
				{
					item.Value.Key = key;
					_serverConfigurationManager.Save();
				}
				if (!selectedServer.Authentications.Exists((Authentication p) => p.SecretKeyIdx == item.Key))
				{
					if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Delete Secret Key") && UiSharedService.CtrlPressed())
					{
						selectedServer.SecretKeys.Remove(item.Key);
						_serverConfigurationManager.Save();
					}
					UiSharedService.AttachToolTip("Hold CTRL to delete this secret key entry");
				}
				else
				{
					UiSharedService.ColorTextWrapped("This key is in use and cannot be deleted", ImGuiColors.DalamudYellow);
				}
				if (item.Key != selectedServer.SecretKeys.Keys.LastOrDefault())
				{
					ImGui.Separator();
				}
			}
		}
		ImGui.Separator();
		if (_uiSharedService.IconTextButton(FontAwesomeIcon.Plus, "Add new Secret Key"))
		{
			selectedServer.SecretKeys.Add(selectedServer.SecretKeys.Any() ? (selectedServer.SecretKeys.Max<KeyValuePair<int, SecretKey>>((KeyValuePair<int, SecretKey> p) => p.Key) + 1) : 0, new SecretKey
			{
				FriendlyName = "New Secret Key"
			});
			_serverConfigurationManager.Save();
		}
	}

	private void DrawServiceConfiguration(ServerStorage selectedServer, bool useOauth)
	{
		string serverName = selectedServer.ServerName;
		string serverUri = selectedServer.ServerUri;
		bool num = string.Equals(serverName, "XIVSync Central Server", StringComparison.OrdinalIgnoreCase);
		ImGuiInputTextFlags flags = (num ? ImGuiInputTextFlags.ReadOnly : ImGuiInputTextFlags.None);
		if (ImGui.InputText("Service URI", ref serverUri, 255, flags))
		{
			selectedServer.ServerUri = serverUri;
		}
		if (num)
		{
			_uiSharedService.DrawHelpText("You cannot edit the URI of the main service.");
		}
		if (ImGui.InputText("Service Name", ref serverName, 255, flags))
		{
			selectedServer.ServerName = serverName;
			_serverConfigurationManager.Save();
		}
		if (num)
		{
			_uiSharedService.DrawHelpText("You cannot edit the name of the main service.");
		}
		ImGui.SetNextItemWidth(200f);
		HttpTransportType serverTransport = _serverConfigurationManager.GetTransport();
		_uiSharedService.DrawCombo("Server Transport Type", from t in Enum.GetValues<HttpTransportType>()
			where t != HttpTransportType.None
			select t, (HttpTransportType v) => v.ToString(), delegate(HttpTransportType t)
		{
			_serverConfigurationManager.SetTransportType(t);
		}, serverTransport);
		_uiSharedService.DrawHelpText("You normally do not need to change this, if you don't know what this is or what it's for, keep it to WebSockets." + Environment.NewLine + "If you run into connection issues with e.g. VPNs, try ServerSentEvents first before trying out LongPolling.--SEP--Note: if the server does not support a specific Transport Type it will fall through to the next automatically: WebSockets > ServerSentEvents > LongPolling");
		if (_dalamudUtilService.IsWine)
		{
			bool forceWebSockets = selectedServer.ForceWebSockets;
			if (ImGui.Checkbox("[wine only] Force WebSockets", ref forceWebSockets))
			{
				selectedServer.ForceWebSockets = forceWebSockets;
				_serverConfigurationManager.Save();
			}
			_uiSharedService.DrawHelpText("On wine, Mare will automatically fall back to ServerSentEvents/LongPolling, even if WebSockets is selected. WebSockets are known to crash XIV entirely on wine 8.5 shipped with Dalamud. Only enable this if you are not running wine 8.5." + Environment.NewLine + "Note: If the issue gets resolved at some point this option will be removed.");
		}
		ImGuiHelpers.ScaledDummy(5f);
		if (ImGui.Checkbox("Use Discord OAuth2 Authentication", ref useOauth))
		{
			selectedServer.UseOAuth2 = useOauth;
			_serverConfigurationManager.Save();
		}
		_uiSharedService.DrawHelpText("Use Discord OAuth2 Authentication to identify with this server instead of secret keys");
		if (useOauth)
		{
			_uiSharedService.DrawOAuth(selectedServer);
			if (string.IsNullOrEmpty(_serverConfigurationManager.GetDiscordUserFromToken(selectedServer)))
			{
				ImGuiHelpers.ScaledDummy(10f);
				UiSharedService.ColorTextWrapped("You have enabled OAuth2 but it is not linked. Press the buttons Check, then Authenticate to link properly.", ImGuiColors.DalamudRed);
			}
			if (!string.IsNullOrEmpty(_serverConfigurationManager.GetDiscordUserFromToken(selectedServer)) && selectedServer.Authentications.TrueForAll((Authentication u) => string.IsNullOrEmpty(u.UID)))
			{
				ImGuiHelpers.ScaledDummy(10f);
				UiSharedService.ColorTextWrapped("You have enabled OAuth2 but no characters configured. Set the correct UIDs for your characters in \"Character Management\".", ImGuiColors.DalamudRed);
			}
		}
		else
		{
			ImGuiHelpers.ScaledDummy(10f);
			_uiSharedService.BigText("Secret Key Management");
			ImGui.Spacing();
			UiSharedService.ColorTextWrapped("Manage your secret keys for authentication with this server.", ImGuiColors.DalamudWhite);
			ImGui.Spacing();
			DrawSecretKeyManagement(selectedServer);
		}
		if (!num && selectedServer != _serverConfigurationManager.CurrentServer)
		{
			ImGui.Separator();
			if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Delete Service") && UiSharedService.CtrlPressed())
			{
				_serverConfigurationManager.DeleteServer(selectedServer);
			}
			_uiSharedService.DrawHelpText("Hold CTRL to delete this service");
		}
	}

	private void DrawPermissionSettings(ServerStorage selectedServer)
	{
		_uiSharedService.BigText("Default Permission Settings");
		if (selectedServer == _serverConfigurationManager.CurrentServer && _apiController.IsConnected)
		{
			UiSharedService.TextWrapped("Note: The default permissions settings here are not applied retroactively to existing pairs or joined Syncshells.");
			UiSharedService.TextWrapped("Note: The default permissions settings here are sent and stored on the connected service.");
			ImGuiHelpers.ScaledDummy(5f);
			DefaultPermissionsDto perms = _apiController.DefaultPermissions;
			bool individualIsSticky = perms.IndividualIsSticky;
			bool disableIndividualSounds = perms.DisableIndividualSounds;
			bool disableIndividualAnimations = perms.DisableIndividualAnimations;
			bool disableIndividualVFX = perms.DisableIndividualVFX;
			if (ImGui.Checkbox("Individually set permissions become preferred permissions", ref individualIsSticky))
			{
				perms.IndividualIsSticky = individualIsSticky;
				_apiController.UserUpdateDefaultPermissions(perms);
			}
			_uiSharedService.DrawHelpText("The preferred attribute means that the permissions to that user will never change through any of your permission changes to Syncshells unless you change them individually.");
			if (ImGui.Checkbox("Disable Individual pair sounds", ref disableIndividualSounds))
			{
				perms.DisableIndividualSounds = disableIndividualSounds;
				_apiController.UserUpdateDefaultPermissions(perms);
			}
			_uiSharedService.DrawHelpText("This setting will disable sound sync for all newly added individual pairs.");
			if (ImGui.Checkbox("Disable Individual pair animations", ref disableIndividualAnimations))
			{
				perms.DisableIndividualAnimations = disableIndividualAnimations;
				_apiController.UserUpdateDefaultPermissions(perms);
			}
			_uiSharedService.DrawHelpText("This setting will disable animation sync for all newly added individual pairs.");
			if (ImGui.Checkbox("Disable Individual pair VFX", ref disableIndividualVFX))
			{
				perms.DisableIndividualVFX = disableIndividualVFX;
				_apiController.UserUpdateDefaultPermissions(perms);
			}
			_uiSharedService.DrawHelpText("This setting will disable VFX sync for all newly added individual pairs.");
			ImGuiHelpers.ScaledDummy(5f);
			_uiSharedService.BigText("Default Syncshell Permission Settings");
			bool disableGroupSounds = perms.DisableGroupSounds;
			bool disableGroupAnimations = perms.DisableGroupAnimations;
			bool disableGroupVFX = perms.DisableGroupVFX;
			if (ImGui.Checkbox("Disable Syncshell pair sounds", ref disableGroupSounds))
			{
				perms.DisableGroupSounds = disableGroupSounds;
				_apiController.UserUpdateDefaultPermissions(perms);
			}
			_uiSharedService.DrawHelpText("This setting will disable sound sync for all non-sticky pairs in newly joined syncshells.");
			if (ImGui.Checkbox("Disable Syncshell pair animations", ref disableGroupAnimations))
			{
				perms.DisableGroupAnimations = disableGroupAnimations;
				_apiController.UserUpdateDefaultPermissions(perms);
			}
			_uiSharedService.DrawHelpText("This setting will disable animation sync for all non-sticky pairs in newly joined syncshells.");
			if (ImGui.Checkbox("Disable Syncshell pair VFX", ref disableGroupVFX))
			{
				perms.DisableGroupVFX = disableGroupVFX;
				_apiController.UserUpdateDefaultPermissions(perms);
			}
			_uiSharedService.DrawHelpText("This setting will disable VFX sync for all non-sticky pairs in newly joined syncshells.");
		}
		else
		{
			UiSharedService.ColorTextWrapped("Default Permission Settings unavailable for this service. You need to connect to this service to change the default permissions since they are stored on the service.", ImGuiColors.DalamudYellow);
		}
	}

	private string GetServerStatusText()
	{
		return _apiController.ServerState switch
		{
			ServerState.Connected => "Service XIVSync Central Server: Available", 
			ServerState.Connecting => "Service XIVSync Central Server: Connecting", 
			ServerState.Disconnected => "Service XIVSync Central Server: Disconnected", 
			ServerState.Offline => "Service XIVSync Central Server: Offline", 
			_ => "Service XIVSync Central Server: Unknown", 
		};
	}

	private int GetUsersOnlineCount()
	{
		if (_apiController.ServerState != ServerState.Connected)
		{
			return 0;
		}
		return _apiController.OnlineUsers;
	}

	private Vector3 ConvertColorToVector3(uint color)
	{
		return new Vector3((float)((color >> 16) & 0xFFu) / 255f, (float)((color >> 8) & 0xFFu) / 255f, (float)(color & 0xFFu) / 255f);
	}

	private uint ConvertVector3ToColor(Vector3 color)
	{
		return ((uint)(color.X * 255f) << 16) | ((uint)(color.Y * 255f) << 8) | (uint)(color.Z * 255f);
	}

	private void SetDtrColor(int index, DtrEntry.Colors colors)
	{
		switch (index)
		{
		case 0:
			_configService.Current.DtrColorsDefault = colors;
			break;
		case 1:
			_configService.Current.DtrColorsNotConnected = colors;
			break;
		case 2:
			_configService.Current.DtrColorsPairsInRange = colors;
			break;
		}
		_configService.Save();
	}

	private void ExportNotes()
	{
		try
		{
			ImGui.SetClipboardText("Export functionality coming soon...");
			_logger.LogInformation("Notes export requested - feature coming soon");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to export notes");
		}
	}

	private void ImportNotes(bool overwrite)
	{
		try
		{
			_logger.LogInformation("Notes import requested - feature coming soon");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to import notes");
		}
	}
}
