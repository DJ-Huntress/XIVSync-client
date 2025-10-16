using System;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Microsoft.Extensions.Logging;
using XIVSync.Services;
using XIVSync.Services.Mediator;

namespace XIVSync.Interop.Ipc;

public sealed class IpcCallerHonorific : IIpcCaller, IDisposable
{
	private readonly ICallGateSubscriber<(uint major, uint minor)> _honorificApiVersion;

	private readonly ICallGateSubscriber<int, object> _honorificClearCharacterTitle;

	private readonly ICallGateSubscriber<object> _honorificDisposing;

	private readonly ICallGateSubscriber<string> _honorificGetLocalCharacterTitle;

	private readonly ICallGateSubscriber<string, object> _honorificLocalCharacterTitleChanged;

	private readonly ICallGateSubscriber<object> _honorificReady;

	private readonly ICallGateSubscriber<int, string, object> _honorificSetCharacterTitle;

	private readonly ILogger<IpcCallerHonorific> _logger;

	private readonly MareMediator _mareMediator;

	private readonly DalamudUtilService _dalamudUtil;

	public bool APIAvailable { get; private set; }

	public IpcCallerHonorific(ILogger<IpcCallerHonorific> logger, IDalamudPluginInterface pi, DalamudUtilService dalamudUtil, MareMediator mareMediator)
	{
		_logger = logger;
		_mareMediator = mareMediator;
		_dalamudUtil = dalamudUtil;
		_honorificApiVersion = pi.GetIpcSubscriber<(uint, uint)>("Honorific.ApiVersion");
		_honorificGetLocalCharacterTitle = pi.GetIpcSubscriber<string>("Honorific.GetLocalCharacterTitle");
		_honorificClearCharacterTitle = pi.GetIpcSubscriber<int, object>("Honorific.ClearCharacterTitle");
		_honorificSetCharacterTitle = pi.GetIpcSubscriber<int, string, object>("Honorific.SetCharacterTitle");
		_honorificLocalCharacterTitleChanged = pi.GetIpcSubscriber<string, object>("Honorific.LocalCharacterTitleChanged");
		_honorificDisposing = pi.GetIpcSubscriber<object>("Honorific.Disposing");
		_honorificReady = pi.GetIpcSubscriber<object>("Honorific.Ready");
		_honorificLocalCharacterTitleChanged.Subscribe(OnHonorificLocalCharacterTitleChanged);
		_honorificDisposing.Subscribe(OnHonorificDisposing);
		_honorificReady.Subscribe(OnHonorificReady);
		CheckAPI();
	}

	public void CheckAPI()
	{
		try
		{
			(uint, uint) tuple = _honorificApiVersion.InvokeFunc();
			APIAvailable = tuple.Item1 == 3 && tuple.Item2 >= 1;
		}
		catch
		{
			APIAvailable = false;
		}
	}

	public void Dispose()
	{
		_honorificLocalCharacterTitleChanged.Unsubscribe(OnHonorificLocalCharacterTitleChanged);
		_honorificDisposing.Unsubscribe(OnHonorificDisposing);
		_honorificReady.Unsubscribe(OnHonorificReady);
	}

	public async Task ClearTitleAsync(nint character)
	{
		if (!APIAvailable)
		{
			return;
		}
		await _dalamudUtil.RunOnFrameworkThread(delegate
		{
			if (_dalamudUtil.CreateGameObject(character) is IPlayerCharacter playerCharacter)
			{
				_logger.LogTrace("Honorific removing for {addr}", ((IntPtr)playerCharacter.Address).ToString("X"));
				_honorificClearCharacterTitle.InvokeAction(playerCharacter.ObjectIndex);
			}
		}, "ClearTitleAsync", "C:\\Users\\Owner\\sync_client2\\XIVSync\\Interop\\Ipc\\IpcCallerHonorific.cs", 69).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<string> GetTitle()
	{
		if (!APIAvailable)
		{
			return string.Empty;
		}
		string title = await _dalamudUtil.RunOnFrameworkThread(() => _honorificGetLocalCharacterTitle.InvokeFunc(), "GetTitle", "C:\\Users\\Owner\\sync_client2\\XIVSync\\Interop\\Ipc\\IpcCallerHonorific.cs", 83).ConfigureAwait(continueOnCapturedContext: false);
		return string.IsNullOrEmpty(title) ? string.Empty : Convert.ToBase64String(Encoding.UTF8.GetBytes(title));
	}

	public async Task SetTitleAsync(nint character, string honorificDataB64)
	{
		if (!APIAvailable)
		{
			return;
		}
		_logger.LogTrace("Applying Honorific data to {chara}", ((IntPtr)character).ToString("X"));
		try
		{
			await _dalamudUtil.RunOnFrameworkThread(delegate
			{
				if (_dalamudUtil.CreateGameObject(character) is IPlayerCharacter playerCharacter)
				{
					string text = (string.IsNullOrEmpty(honorificDataB64) ? string.Empty : Encoding.UTF8.GetString(Convert.FromBase64String(honorificDataB64)));
					if (string.IsNullOrEmpty(text))
					{
						_honorificClearCharacterTitle.InvokeAction(playerCharacter.ObjectIndex);
					}
					else
					{
						_honorificSetCharacterTitle.InvokeAction(playerCharacter.ObjectIndex, text);
					}
				}
			}, "SetTitleAsync", "C:\\Users\\Owner\\sync_client2\\XIVSync\\Interop\\Ipc\\IpcCallerHonorific.cs", 93).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception e)
		{
			_logger.LogWarning(e, "Could not apply Honorific data");
		}
	}

	private void OnHonorificDisposing()
	{
		_mareMediator.Publish(new HonorificMessage(string.Empty));
	}

	private void OnHonorificLocalCharacterTitleChanged(string titleJson)
	{
		string titleData = (string.IsNullOrEmpty(titleJson) ? string.Empty : Convert.ToBase64String(Encoding.UTF8.GetBytes(titleJson)));
		_mareMediator.Publish(new HonorificMessage(titleData));
	}

	private void OnHonorificReady()
	{
		CheckAPI();
		_mareMediator.Publish(new HonorificReadyMessage());
	}
}
