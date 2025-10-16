using System;
using Microsoft.Extensions.Logging;
using XIVSync.MareConfiguration.Models;
using XIVSync.UI;
using XIVSync.UI.Theming;

namespace XIVSync.MareConfiguration.Configurations;

[Serializable]
public class MareConfig : IMareConfiguration
{
	public bool AcceptedAgreement { get; set; }

	public string CacheFolder { get; set; } = string.Empty;


	public bool DisableOptionalPluginWarnings { get; set; }

	public bool EnableDtrEntry { get; set; }

	public bool ShowUidInDtrTooltip { get; set; } = true;


	public bool PreferNoteInDtrTooltip { get; set; }

	public bool UseColorsInDtr { get; set; } = true;


	public DtrEntry.Colors DtrColorsDefault { get; set; }

	public DtrEntry.Colors DtrColorsNotConnected { get; set; } = new DtrEntry.Colors(0u, 272639u);


	public DtrEntry.Colors DtrColorsPairsInRange { get; set; } = new DtrEntry.Colors(0u, 16759367u);


	public bool EnableRightClickMenus { get; set; } = true;


	public NotificationLocation ErrorNotification { get; set; } = NotificationLocation.Both;


	public string ExportFolder { get; set; } = string.Empty;


	public bool FileScanPaused { get; set; }

	public NotificationLocation InfoNotification { get; set; } = NotificationLocation.Toast;


	public bool InitialScanComplete { get; set; }

	public LogLevel LogLevel { get; set; } = LogLevel.Information;


	public bool LogPerformance { get; set; }

	public double MaxLocalCacheInGiB { get; set; } = 20.0;


	public bool OpenGposeImportOnGposeStart { get; set; }

	public bool OpenPopupOnAdd { get; set; } = true;


	public int ParallelDownloads { get; set; } = 10;


	public int ParallelUploads { get; set; } = 3;


	public int DownloadSpeedLimitInBytes { get; set; }

	public DownloadSpeeds DownloadSpeedType { get; set; } = DownloadSpeeds.MBps;


	public bool PreferNotesOverNamesForVisible { get; set; }

	public float ProfileDelay { get; set; } = 1.5f;


	public bool ProfilePopoutRight { get; set; }

	public bool ProfilesAllowNsfw { get; set; }

	public bool ProfilesShow { get; set; } = true;


	public bool ShowSyncshellUsersInVisible { get; set; } = true;


	public bool ShowCharacterNameInsteadOfNotesForVisible { get; set; }

	public bool ShowOfflineUsersSeparately { get; set; } = true;


	public bool ShowSyncshellOfflineUsersSeparately { get; set; } = true;


	public bool GroupUpSyncshells { get; set; } = true;


	public bool ShowOnlineNotifications { get; set; }

	public bool ShowOnlineNotificationsOnlyForIndividualPairs { get; set; } = true;


	public bool ShowOnlineNotificationsOnlyForNamedPairs { get; set; }

	public bool ShowTransferBars { get; set; } = true;


	public bool ShowTransferWindow { get; set; }

	public bool ShowUploading { get; set; } = true;


	public bool ShowUploadingBigText { get; set; } = true;


	public bool ShowVisibleUsersSeparately { get; set; } = true;


	public int TimeSpanBetweenScansInSeconds { get; set; } = 30;


	public int TransferBarsHeight { get; set; } = 12;


	public bool TransferBarsShowText { get; set; } = true;


	public int TransferBarsWidth { get; set; } = 250;


	public bool UseAlternativeFileUpload { get; set; }

	public bool UseCompactor { get; set; }

	public bool DebugStopWhining { get; set; }

	public bool AutoPopulateEmptyNotesFromCharaName { get; set; }

	public int Version { get; set; } = 1;


	public NotificationLocation WarningNotification { get; set; } = NotificationLocation.Both;


	public bool UseFocusTarget { get; set; }

	public ThemePalette? Theme { get; set; }

	public bool ShowTraceLogs { get; set; } = true;


	public bool ShowDebugLogs { get; set; } = true;


	public bool ShowInfoLogs { get; set; } = true;


	public bool ShowWarningLogs { get; set; } = true;


	public bool ShowErrorLogs { get; set; } = true;


	public bool LogViewerAutoScroll { get; set; } = true;


	public bool MuteOwnSounds { get; set; }

	public bool MuteOwnSoundsLocally { get; set; }

	public string ModernThemeName { get; set; } = "default";


	public float ModernThemeOpacity { get; set; } = 0.4f;


	public string ModernBackgroundPreset { get; set; } = "aether";


	public string ModernBackgroundImagePath { get; set; } = string.Empty;


	public bool PauseSyncingDuringInstances { get; set; }

	public bool ShowProfileStatusInPairList { get; set; } = true;


	public int ProfileStatusMaxLength { get; set; } = 50;


	public string LastSeenVersion { get; set; } = string.Empty;

}
