using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Microsoft.Extensions.Logging;

namespace XIVSync.Interop;

public class BlockedCharacterHandler
{
	private sealed record CharaData(ulong AccId, ulong ContentId);

	private readonly Dictionary<CharaData, bool> _blockedCharacterCache = new Dictionary<CharaData, bool>();

	private readonly ILogger<BlockedCharacterHandler> _logger;

	public BlockedCharacterHandler(ILogger<BlockedCharacterHandler> logger, IGameInteropProvider gameInteropProvider)
	{
		gameInteropProvider.InitializeFromAttributes(this);
		_logger = logger;
	}

	private unsafe static CharaData GetIdsFromPlayerPointer(nint ptr)
	{
		if (ptr == IntPtr.Zero)
		{
			return new CharaData(0uL, 0uL);
		}
		return new CharaData(((BattleChara*)ptr)->Character.AccountId, ((BattleChara*)ptr)->Character.ContentId);
	}

	public unsafe bool IsCharacterBlocked(nint ptr, out bool firstTime)
	{
		firstTime = false;
		CharaData combined = GetIdsFromPlayerPointer(ptr);
		if (_blockedCharacterCache.TryGetValue(combined, out var isBlocked))
		{
			return isBlocked;
		}
		firstTime = true;
		InfoProxyBlacklist.BlockResultType blockStatus = InfoProxyBlacklist.Instance()->GetBlockResultType(combined.AccId, combined.ContentId);
		_logger.LogTrace("CharaPtr {ptr} is BlockStatus: {status}", ptr, blockStatus);
		if (blockStatus == (InfoProxyBlacklist.BlockResultType)0)
		{
			return false;
		}
		return _blockedCharacterCache[combined] = blockStatus != InfoProxyBlacklist.BlockResultType.NotBlocked;
	}
}
