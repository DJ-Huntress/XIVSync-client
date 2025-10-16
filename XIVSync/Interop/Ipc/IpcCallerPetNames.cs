using System;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Microsoft.Extensions.Logging;
using XIVSync.Services;
using XIVSync.Services.Mediator;

namespace XIVSync.Interop.Ipc;

public sealed class IpcCallerPetNames : IIpcCaller, IDisposable
{
	private readonly ILogger<IpcCallerPetNames> _logger;

	private readonly DalamudUtilService _dalamudUtil;

	private readonly MareMediator _mareMediator;

	private readonly ICallGateSubscriber<object> _petnamesReady;

	private readonly ICallGateSubscriber<object> _petnamesDisposing;

	private readonly ICallGateSubscriber<(uint, uint)> _apiVersion;

	private readonly ICallGateSubscriber<bool> _enabled;

	private readonly ICallGateSubscriber<string, object> _playerDataChanged;

	private readonly ICallGateSubscriber<string> _getPlayerData;

	private readonly ICallGateSubscriber<string, object> _setPlayerData;

	private readonly ICallGateSubscriber<ushort, object> _clearPlayerData;

	public bool APIAvailable { get; private set; }

	public IpcCallerPetNames(ILogger<IpcCallerPetNames> logger, IDalamudPluginInterface pi, DalamudUtilService dalamudUtil, MareMediator mareMediator)
	{
		_logger = logger;
		_dalamudUtil = dalamudUtil;
		_mareMediator = mareMediator;
		_petnamesReady = pi.GetIpcSubscriber<object>("PetRenamer.OnReady");
		_petnamesDisposing = pi.GetIpcSubscriber<object>("PetRenamer.OnDisposing");
		_apiVersion = pi.GetIpcSubscriber<(uint, uint)>("PetRenamer.ApiVersion");
		_enabled = pi.GetIpcSubscriber<bool>("PetRenamer.IsEnabled");
		_playerDataChanged = pi.GetIpcSubscriber<string, object>("PetRenamer.OnPlayerDataChanged");
		_getPlayerData = pi.GetIpcSubscriber<string>("PetRenamer.GetPlayerData");
		_setPlayerData = pi.GetIpcSubscriber<string, object>("PetRenamer.SetPlayerData");
		_clearPlayerData = pi.GetIpcSubscriber<ushort, object>("PetRenamer.ClearPlayerData");
		_petnamesReady.Subscribe(OnPetNicknamesReady);
		_petnamesDisposing.Subscribe(OnPetNicknamesDispose);
		_playerDataChanged.Subscribe(OnLocalPetNicknamesDataChange);
		CheckAPI();
	}

	public void CheckAPI()
	{
		try
		{
			APIAvailable = _enabled?.InvokeFunc() ?? false;
			if (APIAvailable)
			{
				(uint, uint)? ver = _apiVersion?.InvokeFunc();
				APIAvailable = ver.HasValue && ver.GetValueOrDefault().Item1 == 4;
			}
		}
		catch
		{
			APIAvailable = false;
		}
	}

	private void OnPetNicknamesReady()
	{
		CheckAPI();
		_mareMediator.Publish(new PetNamesReadyMessage());
	}

	private void OnPetNicknamesDispose()
	{
		APIAvailable = false;
		_mareMediator.Publish(new PetNamesMessage(string.Empty));
	}

	public string GetLocalNames()
	{
		if (!APIAvailable)
		{
			return string.Empty;
		}
		try
		{
			string localNameData = _getPlayerData.InvokeFunc();
			return string.IsNullOrEmpty(localNameData) ? string.Empty : localNameData;
		}
		catch (Exception e)
		{
			_logger.LogWarning(e, "Could not obtain Pet Nicknames data");
		}
		return string.Empty;
	}

	public async Task SetPlayerData(nint character, string playerData)
	{
		if (!APIAvailable)
		{
			return;
		}
		_logger.LogTrace("Applying Pet Nicknames data to {chara}", ((IntPtr)character).ToString("X"));
		try
		{
			await _dalamudUtil.RunOnFrameworkThread(delegate
			{
				if (string.IsNullOrEmpty(playerData))
				{
					if (_dalamudUtil.CreateGameObject(character) is IPlayerCharacter playerCharacter)
					{
						_clearPlayerData.InvokeAction(playerCharacter.ObjectIndex);
					}
				}
				else
				{
					_setPlayerData.InvokeAction(playerData);
				}
			}, "SetPlayerData", "C:\\Users\\Owner\\sync_client2\\XIVSync\\Interop\\Ipc\\IpcCallerPetNames.cs", 107).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception e)
		{
			_logger.LogWarning(e, "Could not apply Pet Nicknames data");
		}
	}

	public async Task ClearPlayerData(nint characterPointer)
	{
		if (!APIAvailable)
		{
			return;
		}
		try
		{
			await _dalamudUtil.RunOnFrameworkThread(delegate
			{
				if (_dalamudUtil.CreateGameObject(characterPointer) is IPlayerCharacter playerCharacter)
				{
					_logger.LogTrace("Pet Nicknames removing for {addr}", ((IntPtr)playerCharacter.Address).ToString("X"));
					_clearPlayerData.InvokeAction(playerCharacter.ObjectIndex);
				}
			}, "ClearPlayerData", "C:\\Users\\Owner\\sync_client2\\XIVSync\\Interop\\Ipc\\IpcCallerPetNames.cs", 134).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception e)
		{
			_logger.LogWarning(e, "Could not clear Pet Nicknames data");
		}
	}

	private void OnLocalPetNicknamesDataChange(string data)
	{
		_mareMediator.Publish(new PetNamesMessage(data));
	}

	public void Dispose()
	{
		_petnamesReady.Unsubscribe(OnPetNicknamesReady);
		_petnamesDisposing.Unsubscribe(OnPetNicknamesDispose);
		_playerDataChanged.Unsubscribe(OnLocalPetNicknamesDataChange);
	}
}
