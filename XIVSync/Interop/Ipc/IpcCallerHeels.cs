using System;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Microsoft.Extensions.Logging;
using XIVSync.Services;
using XIVSync.Services.Mediator;

namespace XIVSync.Interop.Ipc;

public sealed class IpcCallerHeels : IIpcCaller, IDisposable
{
	private readonly ILogger<IpcCallerHeels> _logger;

	private readonly MareMediator _mareMediator;

	private readonly DalamudUtilService _dalamudUtil;

	private readonly ICallGateSubscriber<(int, int)> _heelsGetApiVersion;

	private readonly ICallGateSubscriber<string> _heelsGetOffset;

	private readonly ICallGateSubscriber<string, object?> _heelsOffsetUpdate;

	private readonly ICallGateSubscriber<int, string, object?> _heelsRegisterPlayer;

	private readonly ICallGateSubscriber<int, object?> _heelsUnregisterPlayer;

	public bool APIAvailable { get; private set; }

	public IpcCallerHeels(ILogger<IpcCallerHeels> logger, IDalamudPluginInterface pi, DalamudUtilService dalamudUtil, MareMediator mareMediator)
	{
		_logger = logger;
		_mareMediator = mareMediator;
		_dalamudUtil = dalamudUtil;
		_heelsGetApiVersion = pi.GetIpcSubscriber<(int, int)>("SimpleHeels.ApiVersion");
		_heelsGetOffset = pi.GetIpcSubscriber<string>("SimpleHeels.GetLocalPlayer");
		_heelsRegisterPlayer = pi.GetIpcSubscriber<int, string, object>("SimpleHeels.RegisterPlayer");
		_heelsUnregisterPlayer = pi.GetIpcSubscriber<int, object>("SimpleHeels.UnregisterPlayer");
		_heelsOffsetUpdate = pi.GetIpcSubscriber<string, object>("SimpleHeels.LocalChanged");
		_heelsOffsetUpdate.Subscribe(HeelsOffsetChange);
		CheckAPI();
	}

	private void HeelsOffsetChange(string offset)
	{
		_mareMediator.Publish(new HeelsOffsetMessage());
	}

	public async Task<string> GetOffsetAsync()
	{
		if (!APIAvailable)
		{
			return string.Empty;
		}
		return await _dalamudUtil.RunOnFrameworkThread((Func<string>)_heelsGetOffset.InvokeFunc, "GetOffsetAsync", "\\\\wsl.localhost\\Ubuntu\\home\\ddev\\xivsync\\sync_client2\\XIVSync\\Interop\\Ipc\\IpcCallerHeels.cs", 46).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task RestoreOffsetForPlayerAsync(nint character)
	{
		if (!APIAvailable)
		{
			return;
		}
		await _dalamudUtil.RunOnFrameworkThread(delegate
		{
			IGameObject gameObject = _dalamudUtil.CreateGameObject(character);
			if (gameObject != null)
			{
				_logger.LogTrace("Restoring Heels data to {chara}", ((IntPtr)character).ToString("X"));
				_heelsUnregisterPlayer.InvokeAction(gameObject.ObjectIndex);
			}
		}, "RestoreOffsetForPlayerAsync", "\\\\wsl.localhost\\Ubuntu\\home\\ddev\\xivsync\\sync_client2\\XIVSync\\Interop\\Ipc\\IpcCallerHeels.cs", 52).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task SetOffsetForPlayerAsync(nint character, string data)
	{
		if (!APIAvailable)
		{
			return;
		}
		await _dalamudUtil.RunOnFrameworkThread(delegate
		{
			IGameObject gameObject = _dalamudUtil.CreateGameObject(character);
			if (gameObject != null)
			{
				_logger.LogTrace("Applying Heels data to {chara}", ((IntPtr)character).ToString("X"));
				_heelsRegisterPlayer.InvokeAction(gameObject.ObjectIndex, data);
			}
		}, "SetOffsetForPlayerAsync", "\\\\wsl.localhost\\Ubuntu\\home\\ddev\\xivsync\\sync_client2\\XIVSync\\Interop\\Ipc\\IpcCallerHeels.cs", 66).ConfigureAwait(continueOnCapturedContext: false);
	}

	public void CheckAPI()
	{
		try
		{
			(int, int) tuple = _heelsGetApiVersion.InvokeFunc();
			APIAvailable = tuple.Item1 == 2 && tuple.Item2 >= 1;
		}
		catch
		{
			APIAvailable = false;
		}
	}

	public void Dispose()
	{
		_heelsOffsetUpdate.Unsubscribe(HeelsOffsetChange);
	}
}
