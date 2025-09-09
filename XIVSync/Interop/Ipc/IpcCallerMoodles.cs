using System;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Microsoft.Extensions.Logging;
using XIVSync.Services;
using XIVSync.Services.Mediator;

namespace XIVSync.Interop.Ipc;

public sealed class IpcCallerMoodles : IIpcCaller, IDisposable
{
	private readonly ICallGateSubscriber<int> _moodlesApiVersion;

	private readonly ICallGateSubscriber<IPlayerCharacter, object> _moodlesOnChange;

	private readonly ICallGateSubscriber<nint, string> _moodlesGetStatus;

	private readonly ICallGateSubscriber<nint, string, object> _moodlesSetStatus;

	private readonly ICallGateSubscriber<nint, object> _moodlesRevertStatus;

	private readonly ILogger<IpcCallerMoodles> _logger;

	private readonly DalamudUtilService _dalamudUtil;

	private readonly MareMediator _mareMediator;

	public bool APIAvailable { get; private set; }

	public IpcCallerMoodles(ILogger<IpcCallerMoodles> logger, IDalamudPluginInterface pi, DalamudUtilService dalamudUtil, MareMediator mareMediator)
	{
		_logger = logger;
		_dalamudUtil = dalamudUtil;
		_mareMediator = mareMediator;
		_moodlesApiVersion = pi.GetIpcSubscriber<int>("Moodles.Version");
		_moodlesOnChange = pi.GetIpcSubscriber<IPlayerCharacter, object>("Moodles.StatusManagerModified");
		_moodlesGetStatus = pi.GetIpcSubscriber<nint, string>("Moodles.GetStatusManagerByPtrV2");
		_moodlesSetStatus = pi.GetIpcSubscriber<nint, string, object>("Moodles.SetStatusManagerByPtrV2");
		_moodlesRevertStatus = pi.GetIpcSubscriber<nint, object>("Moodles.ClearStatusManagerByPtrV2");
		_moodlesOnChange.Subscribe(OnMoodlesChange);
		CheckAPI();
	}

	private void OnMoodlesChange(IPlayerCharacter character)
	{
		_mareMediator.Publish(new MoodlesMessage(character.Address));
	}

	public void CheckAPI()
	{
		int[] supportedVersions = new int[3] { 1, 2, 3 };
		try
		{
			int moodlesVersion = _moodlesApiVersion.InvokeFunc();
			APIAvailable = supportedVersions.Contains(moodlesVersion);
		}
		catch (Exception ex)
		{
			_logger.LogWarning("Failed to get Moodles API version: {Exception}", ex.Message);
			APIAvailable = false;
		}
	}

	public void Dispose()
	{
		_moodlesOnChange.Unsubscribe(OnMoodlesChange);
	}

	public async Task<string?> GetStatusAsync(nint address)
	{
		if (!APIAvailable)
		{
			return null;
		}
		try
		{
			return await _dalamudUtil.RunOnFrameworkThread(() => _moodlesGetStatus.InvokeFunc(address), "GetStatusAsync", "\\\\wsl.localhost\\Ubuntu\\home\\ddev\\xivsync\\sync_client2\\XIVSync\\Interop\\Ipc\\IpcCallerMoodles.cs", 74).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Could not Get Moodles Status");
			return null;
		}
	}

	public async Task SetStatusAsync(nint pointer, string status)
	{
		if (!APIAvailable)
		{
			return;
		}
		try
		{
			await _dalamudUtil.RunOnFrameworkThread(delegate
			{
				_moodlesSetStatus.InvokeAction(pointer, status);
			}, "SetStatusAsync", "\\\\wsl.localhost\\Ubuntu\\home\\ddev\\xivsync\\sync_client2\\XIVSync\\Interop\\Ipc\\IpcCallerMoodles.cs", 90).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Could not Set Moodles Status");
		}
	}

	public async Task RevertStatusAsync(nint pointer)
	{
		if (!APIAvailable)
		{
			return;
		}
		try
		{
			await _dalamudUtil.RunOnFrameworkThread(delegate
			{
				_moodlesRevertStatus.InvokeAction(pointer);
			}, "RevertStatusAsync", "\\\\wsl.localhost\\Ubuntu\\home\\ddev\\xivsync\\sync_client2\\XIVSync\\Interop\\Ipc\\IpcCallerMoodles.cs", 104).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Could not Revert Moodles Status");
		}
	}
}
