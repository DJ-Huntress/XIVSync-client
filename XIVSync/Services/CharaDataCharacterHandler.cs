using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using XIVSync.API.Data.Enum;
using XIVSync.Interop.Ipc;
using XIVSync.PlayerData.Factories;
using XIVSync.PlayerData.Handlers;
using XIVSync.Services.CharaData.Models;
using XIVSync.Services.Mediator;

namespace XIVSync.Services;

public sealed class CharaDataCharacterHandler : DisposableMediatorSubscriberBase
{
	private readonly GameObjectHandlerFactory _gameObjectHandlerFactory;

	private readonly DalamudUtilService _dalamudUtilService;

	private readonly IpcManager _ipcManager;

	private readonly HashSet<HandledCharaDataEntry> _handledCharaData = new HashSet<HandledCharaDataEntry>();

	public IEnumerable<HandledCharaDataEntry> HandledCharaData => _handledCharaData;

	public CharaDataCharacterHandler(ILogger<CharaDataCharacterHandler> logger, MareMediator mediator, GameObjectHandlerFactory gameObjectHandlerFactory, DalamudUtilService dalamudUtilService, IpcManager ipcManager)
		: base(logger, mediator)
	{
		_gameObjectHandlerFactory = gameObjectHandlerFactory;
		_dalamudUtilService = dalamudUtilService;
		_ipcManager = ipcManager;
		mediator.Subscribe<GposeEndMessage>(this, delegate
		{
			foreach (HandledCharaDataEntry chara in _handledCharaData)
			{
				Task.Run(() => RevertHandledChara(chara));
			}
		});
		mediator.Subscribe<CutsceneFrameworkUpdateMessage>(this, delegate
		{
			HandleCutsceneFrameworkUpdate();
		});
	}

	private void HandleCutsceneFrameworkUpdate()
	{
		if (!_dalamudUtilService.IsInGpose)
		{
			return;
		}
		foreach (HandledCharaDataEntry entry in _handledCharaData.ToList())
		{
			if (_dalamudUtilService.GetGposeCharacterFromObjectTableByName(entry.Name, onlyGposeCharacters: true) == null)
			{
				RevertChara(entry.Name, entry.CustomizePlus).GetAwaiter().GetResult();
				_handledCharaData.Remove(entry);
			}
		}
	}

	protected override void Dispose(bool disposing)
	{
		base.Dispose(disposing);
		foreach (HandledCharaDataEntry chara in _handledCharaData)
		{
			Task.Run(() => RevertHandledChara(chara));
		}
	}

	public async Task RevertChara(string name, Guid? cPlusId)
	{
		Guid applicationId = Guid.NewGuid();
		await _ipcManager.Glamourer.RevertByNameAsync(base.Logger, name, applicationId).ConfigureAwait(continueOnCapturedContext: false);
		if (cPlusId.HasValue)
		{
			await _ipcManager.CustomizePlus.RevertByIdAsync(cPlusId).ConfigureAwait(continueOnCapturedContext: false);
		}
		using GameObjectHandler handler = await _gameObjectHandlerFactory.Create(ObjectKind.Player, () => _dalamudUtilService.GetGposeCharacterFromObjectTableByName(name, _dalamudUtilService.IsInGpose)?.Address ?? IntPtr.Zero).ConfigureAwait(continueOnCapturedContext: false);
		if (handler.Address != IntPtr.Zero)
		{
			await _ipcManager.Penumbra.RedrawAsync(base.Logger, handler, applicationId, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	public async Task<bool> RevertHandledChara(string name)
	{
		HandledCharaDataEntry handled = _handledCharaData.FirstOrDefault((HandledCharaDataEntry f) => string.Equals(f.Name, name, StringComparison.Ordinal));
		if (handled == null)
		{
			return false;
		}
		_handledCharaData.Remove(handled);
		await _dalamudUtilService.RunOnFrameworkThread(() => RevertChara(handled.Name, handled.CustomizePlus), "RevertHandledChara", "\\\\wsl.localhost\\Ubuntu\\home\\ddev\\xivsync\\sync_client2\\XIVSync\\Services\\CharaData\\CharaDataCharacterHandler.cs", 83).ConfigureAwait(continueOnCapturedContext: false);
		return true;
	}

	public Task RevertHandledChara(HandledCharaDataEntry? handled)
	{
		if (handled == null)
		{
			return Task.CompletedTask;
		}
		_handledCharaData.Remove(handled);
		return _dalamudUtilService.RunOnFrameworkThread(() => RevertChara(handled.Name, handled.CustomizePlus), "RevertHandledChara", "\\\\wsl.localhost\\Ubuntu\\home\\ddev\\xivsync\\sync_client2\\XIVSync\\Services\\CharaData\\CharaDataCharacterHandler.cs", 91);
	}

	internal void AddHandledChara(HandledCharaDataEntry handledCharaDataEntry)
	{
		_handledCharaData.Add(handledCharaDataEntry);
	}

	public void UpdateHandledData(Dictionary<string, CharaDataMetaInfoExtendedDto?> newData)
	{
		foreach (HandledCharaDataEntry handledData in _handledCharaData)
		{
			if (newData.TryGetValue(handledData.MetaInfo.FullId, out CharaDataMetaInfoExtendedDto metaInfo) && metaInfo != null)
			{
				handledData.MetaInfo = metaInfo;
			}
		}
	}

	public async Task<GameObjectHandler?> TryCreateGameObjectHandler(string name, bool gPoseOnly = false)
	{
		GameObjectHandler handler = await _gameObjectHandlerFactory.Create(ObjectKind.Player, () => _dalamudUtilService.GetGposeCharacterFromObjectTableByName(name, gPoseOnly && _dalamudUtilService.IsInGpose)?.Address ?? IntPtr.Zero).ConfigureAwait(continueOnCapturedContext: false);
		if (handler.Address == IntPtr.Zero)
		{
			return null;
		}
		return handler;
	}

	public async Task<GameObjectHandler?> TryCreateGameObjectHandler(int index)
	{
		GameObjectHandler handler = await _gameObjectHandlerFactory.Create(ObjectKind.Player, () => _dalamudUtilService.GetCharacterFromObjectTableByIndex(index)?.Address ?? IntPtr.Zero).ConfigureAwait(continueOnCapturedContext: false);
		if (handler.Address == IntPtr.Zero)
		{
			return null;
		}
		return handler;
	}
}
