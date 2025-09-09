using System;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Utility;
using Microsoft.Extensions.Logging;
using XIVSync.Services;
using XIVSync.Services.Mediator;

namespace XIVSync.Interop.Ipc;

public sealed class IpcCallerCustomize : IIpcCaller, IDisposable
{
	private readonly ICallGateSubscriber<(int, int)> _customizePlusApiVersion;

	private readonly ICallGateSubscriber<ushort, (int, Guid?)> _customizePlusGetActiveProfile;

	private readonly ICallGateSubscriber<Guid, (int, string?)> _customizePlusGetProfileById;

	private readonly ICallGateSubscriber<ushort, Guid, object> _customizePlusOnScaleUpdate;

	private readonly ICallGateSubscriber<ushort, int> _customizePlusRevertCharacter;

	private readonly ICallGateSubscriber<ushort, string, (int, Guid?)> _customizePlusSetBodyScaleToCharacter;

	private readonly ICallGateSubscriber<Guid, int> _customizePlusDeleteByUniqueId;

	private readonly ILogger<IpcCallerCustomize> _logger;

	private readonly DalamudUtilService _dalamudUtil;

	private readonly MareMediator _mareMediator;

	public bool APIAvailable { get; private set; }

	public IpcCallerCustomize(ILogger<IpcCallerCustomize> logger, IDalamudPluginInterface dalamudPluginInterface, DalamudUtilService dalamudUtil, MareMediator mareMediator)
	{
		_customizePlusApiVersion = dalamudPluginInterface.GetIpcSubscriber<(int, int)>("CustomizePlus.General.GetApiVersion");
		_customizePlusGetActiveProfile = dalamudPluginInterface.GetIpcSubscriber<ushort, (int, Guid?)>("CustomizePlus.Profile.GetActiveProfileIdOnCharacter");
		_customizePlusGetProfileById = dalamudPluginInterface.GetIpcSubscriber<Guid, (int, string)>("CustomizePlus.Profile.GetByUniqueId");
		_customizePlusRevertCharacter = dalamudPluginInterface.GetIpcSubscriber<ushort, int>("CustomizePlus.Profile.DeleteTemporaryProfileOnCharacter");
		_customizePlusSetBodyScaleToCharacter = dalamudPluginInterface.GetIpcSubscriber<ushort, string, (int, Guid?)>("CustomizePlus.Profile.SetTemporaryProfileOnCharacter");
		_customizePlusOnScaleUpdate = dalamudPluginInterface.GetIpcSubscriber<ushort, Guid, object>("CustomizePlus.Profile.OnUpdate");
		_customizePlusDeleteByUniqueId = dalamudPluginInterface.GetIpcSubscriber<Guid, int>("CustomizePlus.Profile.DeleteTemporaryProfileByUniqueId");
		_customizePlusOnScaleUpdate.Subscribe(OnCustomizePlusScaleChange);
		_logger = logger;
		_dalamudUtil = dalamudUtil;
		_mareMediator = mareMediator;
		CheckAPI();
	}

	public async Task RevertAsync(nint character)
	{
		if (!APIAvailable)
		{
			return;
		}
		await _dalamudUtil.RunOnFrameworkThread(delegate
		{
			if (_dalamudUtil.CreateGameObject(character) is ICharacter character2)
			{
				_logger.LogTrace("CustomizePlus reverting for {chara}", ((IntPtr)character2.Address).ToString("X"));
				_customizePlusRevertCharacter.InvokeFunc(character2.ObjectIndex);
			}
		}, "RevertAsync", "\\\\wsl.localhost\\Ubuntu\\home\\ddev\\xivsync\\sync_client2\\XIVSync\\Interop\\Ipc\\IpcCallerCustomize.cs", 49).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Guid?> SetBodyScaleAsync(nint character, string scale)
	{
		if (!APIAvailable)
		{
			return null;
		}
		return await _dalamudUtil.RunOnFrameworkThread(delegate
		{
			if (_dalamudUtil.CreateGameObject(character) is ICharacter character2)
			{
				string arg = Encoding.UTF8.GetString(Convert.FromBase64String(scale));
				_logger.LogTrace("CustomizePlus applying for {chara}", ((IntPtr)character2.Address).ToString("X"));
				if (scale.IsNullOrEmpty())
				{
					_customizePlusRevertCharacter.InvokeFunc(character2.ObjectIndex);
					return (Guid?)null;
				}
				return _customizePlusSetBodyScaleToCharacter.InvokeFunc(character2.ObjectIndex, arg).Item2;
			}
			return (Guid?)null;
		}, "SetBodyScaleAsync", "\\\\wsl.localhost\\Ubuntu\\home\\ddev\\xivsync\\sync_client2\\XIVSync\\Interop\\Ipc\\IpcCallerCustomize.cs", 63).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task RevertByIdAsync(Guid? profileId)
	{
		if (APIAvailable && profileId.HasValue)
		{
			await _dalamudUtil.RunOnFrameworkThread(delegate
			{
				_customizePlusDeleteByUniqueId.InvokeFunc(profileId.Value);
			}, "RevertByIdAsync", "\\\\wsl.localhost\\Ubuntu\\home\\ddev\\xivsync\\sync_client2\\XIVSync\\Interop\\Ipc\\IpcCallerCustomize.cs", 90).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	public async Task<string?> GetScaleAsync(nint character)
	{
		if (!APIAvailable)
		{
			return null;
		}
		string scale = await _dalamudUtil.RunOnFrameworkThread(delegate
		{
			if (_dalamudUtil.CreateGameObject(character) is ICharacter character2)
			{
				(int, Guid?) tuple = _customizePlusGetActiveProfile.InvokeFunc(character2.ObjectIndex);
				_logger.LogTrace("CustomizePlus GetActiveProfile returned {err}", tuple.Item1);
				if (tuple.Item1 != 0 || !tuple.Item2.HasValue)
				{
					return string.Empty;
				}
				return _customizePlusGetProfileById.InvokeFunc(tuple.Item2.Value).Item2;
			}
			return string.Empty;
		}, "GetScaleAsync", "\\\\wsl.localhost\\Ubuntu\\home\\ddev\\xivsync\\sync_client2\\XIVSync\\Interop\\Ipc\\IpcCallerCustomize.cs", 99).ConfigureAwait(continueOnCapturedContext: false);
		if (string.IsNullOrEmpty(scale))
		{
			return string.Empty;
		}
		return Convert.ToBase64String(Encoding.UTF8.GetBytes(scale));
	}

	public void CheckAPI()
	{
		try
		{
			(int, int) version = _customizePlusApiVersion.InvokeFunc();
			APIAvailable = version.Item1 == 6 && version.Item2 >= 0;
		}
		catch
		{
			APIAvailable = false;
		}
	}

	private void OnCustomizePlusScaleChange(ushort c, Guid g)
	{
		ICharacter obj = _dalamudUtil.GetCharacterFromObjectTableByIndex(c);
		_mareMediator.Publish(new CustomizePlusMessage(obj?.Address));
	}

	public void Dispose()
	{
		_customizePlusOnScaleUpdate.Unsubscribe(OnCustomizePlusScaleChange);
	}
}
