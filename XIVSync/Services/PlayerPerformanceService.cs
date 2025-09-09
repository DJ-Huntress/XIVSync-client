using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using XIVSync.API.Data;
using XIVSync.API.Data.Enum;
using XIVSync.FileCache;
using XIVSync.MareConfiguration;
using XIVSync.MareConfiguration.Configurations;
using XIVSync.MareConfiguration.Models;
using XIVSync.PlayerData.Handlers;
using XIVSync.PlayerData.Pairs;
using XIVSync.Services.Events;
using XIVSync.Services.Mediator;
using XIVSync.UI;
using XIVSync.WebAPI.Files.Models;

namespace XIVSync.Services;

public class PlayerPerformanceService
{
	private readonly FileCacheManager _fileCacheManager;

	private readonly XivDataAnalyzer _xivDataAnalyzer;

	private readonly ILogger<PlayerPerformanceService> _logger;

	private readonly MareMediator _mediator;

	private readonly PlayerPerformanceConfigService _playerPerformanceConfigService;

	private readonly Dictionary<string, bool> _warnedForPlayers = new Dictionary<string, bool>(StringComparer.Ordinal);

	public PlayerPerformanceService(ILogger<PlayerPerformanceService> logger, MareMediator mediator, PlayerPerformanceConfigService playerPerformanceConfigService, FileCacheManager fileCacheManager, XivDataAnalyzer xivDataAnalyzer)
	{
		_logger = logger;
		_mediator = mediator;
		_playerPerformanceConfigService = playerPerformanceConfigService;
		_fileCacheManager = fileCacheManager;
		_xivDataAnalyzer = xivDataAnalyzer;
	}

	public async Task<bool> CheckBothThresholds(PairHandler pairHandler, CharacterData charaData)
	{
		PlayerPerformanceConfig config = _playerPerformanceConfigService.Current;
		if (!ComputeAndAutoPauseOnVRAMUsageThresholds(pairHandler, charaData, new List<DownloadFileTransfer>()))
		{
			return false;
		}
		if (!(await CheckTriangleUsageThresholds(pairHandler, charaData).ConfigureAwait(continueOnCapturedContext: false)))
		{
			return false;
		}
		if (config.UIDsToIgnore.Exists((string uid) => string.Equals(uid, pairHandler.Pair.UserData.Alias, StringComparison.Ordinal) || string.Equals(uid, pairHandler.Pair.UserData.UID, StringComparison.Ordinal)))
		{
			return true;
		}
		long vramUsage = pairHandler.Pair.LastAppliedApproximateVRAMBytes;
		long triUsage = pairHandler.Pair.LastAppliedDataTris;
		bool isPrefPerm = pairHandler.Pair.UserPair.OwnPermissions.HasFlag(UserPermissions.Sticky);
		bool exceedsTris = CheckForThreshold(config.WarnOnExceedingThresholds, config.TrisWarningThresholdThousands * 1000, triUsage, config.WarnOnPreferredPermissionsExceedingThresholds, isPrefPerm);
		bool exceedsVram = CheckForThreshold(config.WarnOnExceedingThresholds, config.VRAMSizeWarningThresholdMiB * 1024 * 1024, vramUsage, config.WarnOnPreferredPermissionsExceedingThresholds, isPrefPerm);
		if (_warnedForPlayers.TryGetValue(pairHandler.Pair.UserData.UID, out var hadWarning) && hadWarning)
		{
			_warnedForPlayers[pairHandler.Pair.UserData.UID] = exceedsTris || exceedsVram;
			return true;
		}
		_warnedForPlayers[pairHandler.Pair.UserData.UID] = exceedsTris || exceedsVram;
		if (exceedsVram)
		{
			_mediator.Publish(new EventMessage(new Event(pairHandler.Pair.PlayerName, pairHandler.Pair.UserData, "PlayerPerformanceService", EventSeverity.Warning, $"Exceeds VRAM threshold: ({UiSharedService.ByteToString(vramUsage)}/{config.VRAMSizeWarningThresholdMiB} MiB)")));
		}
		if (exceedsTris)
		{
			_mediator.Publish(new EventMessage(new Event(pairHandler.Pair.PlayerName, pairHandler.Pair.UserData, "PlayerPerformanceService", EventSeverity.Warning, $"Exceeds triangle threshold: ({triUsage}/{config.TrisAutoPauseThresholdThousands * 1000} triangles)")));
		}
		if (exceedsTris || exceedsVram)
		{
			_ = string.Empty;
			string warningText = ((exceedsTris && !exceedsVram) ? $"Player {pairHandler.Pair.PlayerName} ({pairHandler.Pair.UserData.AliasOrUID}) exceeds your configured triangle warning threshold ({triUsage}/{config.TrisWarningThresholdThousands * 1000} triangles)." : (exceedsTris ? $"Player {pairHandler.Pair.PlayerName} ({pairHandler.Pair.UserData.AliasOrUID}) exceeds both VRAM warning threshold ({UiSharedService.ByteToString(vramUsage)}/{config.VRAMSizeWarningThresholdMiB} MiB) and triangle warning threshold ({triUsage}/{config.TrisWarningThresholdThousands * 1000} triangles)." : $"Player {pairHandler.Pair.PlayerName} ({pairHandler.Pair.UserData.AliasOrUID}) exceeds your configured VRAM warning threshold ({UiSharedService.ByteToString(vramUsage)}/{config.VRAMSizeWarningThresholdMiB} MiB)."));
			_mediator.Publish(new NotificationMessage(pairHandler.Pair.PlayerName + " (" + pairHandler.Pair.UserData.AliasOrUID + ") exceeds performance threshold(s)", warningText, NotificationType.Warning));
		}
		return true;
	}

	public async Task<bool> CheckTriangleUsageThresholds(PairHandler pairHandler, CharacterData charaData)
	{
		PlayerPerformanceConfig config = _playerPerformanceConfigService.Current;
		Pair pair = pairHandler.Pair;
		long triUsage = 0L;
		if (!charaData.FileReplacements.TryGetValue(ObjectKind.Player, out List<FileReplacementData> playerReplacements))
		{
			pair.LastAppliedDataTris = 0L;
			return true;
		}
		List<string> moddedModelHashes = (from p in playerReplacements
			where string.IsNullOrEmpty(p.FileSwapPath) && p.GamePaths.Any((string g) => g.EndsWith("mdl", StringComparison.OrdinalIgnoreCase))
			select p.Hash).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToList();
		foreach (string hash in moddedModelHashes)
		{
			long num = triUsage;
			triUsage = num + await _xivDataAnalyzer.GetTrianglesByHash(hash).ConfigureAwait(continueOnCapturedContext: false);
		}
		pair.LastAppliedDataTris = triUsage;
		_logger.LogDebug("Calculated VRAM usage for {p}", pairHandler);
		if (config.UIDsToIgnore.Exists((string uid) => string.Equals(uid, pair.UserData.Alias, StringComparison.Ordinal) || string.Equals(uid, pair.UserData.UID, StringComparison.Ordinal)))
		{
			return true;
		}
		bool isPrefPerm = pair.UserPair.OwnPermissions.HasFlag(UserPermissions.Sticky);
		if (CheckForThreshold(config.AutoPausePlayersExceedingThresholds, config.TrisAutoPauseThresholdThousands * 1000, triUsage, config.AutoPausePlayersWithPreferredPermissionsExceedingThresholds, isPrefPerm))
		{
			_mediator.Publish(new NotificationMessage(pair.PlayerName + " (" + pair.UserData.AliasOrUID + ") automatically paused", $"Player {pair.PlayerName} ({pair.UserData.AliasOrUID}) exceeded your configured triangle auto pause threshold ({triUsage}/{config.TrisAutoPauseThresholdThousands * 1000} triangles) and has been automatically paused.", NotificationType.Warning));
			_mediator.Publish(new EventMessage(new Event(pair.PlayerName, pair.UserData, "PlayerPerformanceService", EventSeverity.Warning, $"Exceeds triangle threshold: automatically paused ({triUsage}/{config.TrisAutoPauseThresholdThousands * 1000} triangles)")));
			_mediator.Publish(new PauseMessage(pair.UserData));
			return false;
		}
		return true;
	}

	public bool ComputeAndAutoPauseOnVRAMUsageThresholds(PairHandler pairHandler, CharacterData charaData, List<DownloadFileTransfer> toDownloadFiles)
	{
		PlayerPerformanceConfig config = _playerPerformanceConfigService.Current;
		Pair pair = pairHandler.Pair;
		long vramUsage = 0L;
		if (!charaData.FileReplacements.TryGetValue(ObjectKind.Player, out List<FileReplacementData> playerReplacements))
		{
			pair.LastAppliedApproximateVRAMBytes = 0L;
			return true;
		}
		foreach (string hash in (from p in playerReplacements
			where string.IsNullOrEmpty(p.FileSwapPath) && p.GamePaths.Any((string g) => g.EndsWith(".tex", StringComparison.OrdinalIgnoreCase))
			select p.Hash).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToList())
		{
			long fileSize = 0L;
			DownloadFileTransfer download = toDownloadFiles.Find((DownloadFileTransfer f) => string.Equals(hash, f.Hash, StringComparison.OrdinalIgnoreCase));
			if (download != null)
			{
				fileSize = download.TotalRaw;
			}
			else
			{
				FileCacheEntity fileEntry = _fileCacheManager.GetFileCacheByHash(hash);
				if (fileEntry == null)
				{
					continue;
				}
				if (!fileEntry.Size.HasValue)
				{
					fileEntry.Size = new FileInfo(fileEntry.ResolvedFilepath).Length;
					_fileCacheManager.UpdateHashedFile(fileEntry);
				}
				fileSize = fileEntry.Size.Value;
			}
			vramUsage += fileSize;
		}
		pair.LastAppliedApproximateVRAMBytes = vramUsage;
		_logger.LogDebug("Calculated VRAM usage for {p}", pairHandler);
		if (config.UIDsToIgnore.Exists((string uid) => string.Equals(uid, pair.UserData.Alias, StringComparison.Ordinal) || string.Equals(uid, pair.UserData.UID, StringComparison.Ordinal)))
		{
			return true;
		}
		bool isPrefPerm = pair.UserPair.OwnPermissions.HasFlag(UserPermissions.Sticky);
		if (CheckForThreshold(config.AutoPausePlayersExceedingThresholds, config.VRAMSizeAutoPauseThresholdMiB * 1024 * 1024, vramUsage, config.AutoPausePlayersWithPreferredPermissionsExceedingThresholds, isPrefPerm))
		{
			_mediator.Publish(new NotificationMessage(pair.PlayerName + " (" + pair.UserData.AliasOrUID + ") automatically paused", $"Player {pair.PlayerName} ({pair.UserData.AliasOrUID}) exceeded your configured VRAM auto pause threshold ({UiSharedService.ByteToString(vramUsage)}/{config.VRAMSizeAutoPauseThresholdMiB}MiB) and has been automatically paused.", NotificationType.Warning));
			_mediator.Publish(new PauseMessage(pair.UserData));
			_mediator.Publish(new EventMessage(new Event(pair.PlayerName, pair.UserData, "PlayerPerformanceService", EventSeverity.Warning, $"Exceeds VRAM threshold: automatically paused ({UiSharedService.ByteToString(vramUsage)}/{config.VRAMSizeAutoPauseThresholdMiB} MiB)")));
			return false;
		}
		return true;
	}

	private static bool CheckForThreshold(bool thresholdEnabled, long threshold, long value, bool checkForPrefPerm, bool isPrefPerm)
	{
		if (thresholdEnabled && threshold > 0 && threshold < value)
		{
			if (!(checkForPrefPerm && isPrefPerm))
			{
				return !isPrefPerm;
			}
			return true;
		}
		return false;
	}
}
