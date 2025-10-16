using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Glamourer.Api.Enums;
using Glamourer.Api.Helpers;
using Glamourer.Api.IpcSubscribers;
using Microsoft.Extensions.Logging;
using XIVSync.MareConfiguration.Models;
using XIVSync.PlayerData.Handlers;
using XIVSync.Services;
using XIVSync.Services.Mediator;

namespace XIVSync.Interop.Ipc;

public sealed class IpcCallerGlamourer : DisposableMediatorSubscriberBase, IIpcCaller, IDisposable
{
	private readonly ILogger<IpcCallerGlamourer> _logger;

	private readonly IDalamudPluginInterface _pi;

	private readonly DalamudUtilService _dalamudUtil;

	private readonly MareMediator _mareMediator;

	private readonly RedrawManager _redrawManager;

	private readonly ApiVersion _glamourerApiVersions;

	private readonly ApplyState? _glamourerApplyAll;

	private readonly GetStateBase64? _glamourerGetAllCustomization;

	private readonly RevertState _glamourerRevert;

	private readonly RevertStateName _glamourerRevertByName;

	private readonly UnlockState _glamourerUnlock;

	private readonly UnlockStateName _glamourerUnlockByName;

	private readonly EventSubscriber<nint>? _glamourerStateChanged;

	private bool _shownGlamourerUnavailable;

	private readonly uint LockCode = 1835102821u;

	public bool APIAvailable { get; private set; }

	public IpcCallerGlamourer(ILogger<IpcCallerGlamourer> logger, IDalamudPluginInterface pi, DalamudUtilService dalamudUtil, MareMediator mareMediator, RedrawManager redrawManager)
		: base(logger, mareMediator)
	{
		_glamourerApiVersions = new ApiVersion(pi);
		_glamourerGetAllCustomization = new GetStateBase64(pi);
		_glamourerApplyAll = new ApplyState(pi);
		_glamourerRevert = new RevertState(pi);
		_glamourerRevertByName = new RevertStateName(pi);
		_glamourerUnlock = new UnlockState(pi);
		_glamourerUnlockByName = new UnlockStateName(pi);
		_logger = logger;
		_pi = pi;
		_dalamudUtil = dalamudUtil;
		_mareMediator = mareMediator;
		_redrawManager = redrawManager;
		CheckAPI();
		_glamourerStateChanged = StateChanged.Subscriber(pi, GlamourerChanged);
		_glamourerStateChanged.Enable();
		base.Mediator.Subscribe<DalamudLoginMessage>(this, delegate
		{
			_shownGlamourerUnavailable = false;
		});
	}

	protected override void Dispose(bool disposing)
	{
		base.Dispose(disposing);
		_redrawManager.Cancel();
		_glamourerStateChanged?.Dispose();
	}

	public void CheckAPI()
	{
		bool apiAvailable = false;
		try
		{
			bool versionValid = (_pi.InstalledPlugins.FirstOrDefault((IExposedPlugin p) => string.Equals(p.InternalName, "Glamourer", StringComparison.OrdinalIgnoreCase))?.Version ?? new Version(0, 0, 0, 0)) >= new Version(1, 3, 0, 10);
			try
			{
				(int Major, int Minor) version = _glamourerApiVersions.Invoke();
				if (version.Major == 1 && version.Minor >= 1 && versionValid)
				{
					apiAvailable = true;
				}
			}
			catch
			{
			}
			_shownGlamourerUnavailable = _shownGlamourerUnavailable && !apiAvailable;
			APIAvailable = apiAvailable;
		}
		catch
		{
			APIAvailable = apiAvailable;
		}
		finally
		{
			if (!apiAvailable && !_shownGlamourerUnavailable)
			{
				_shownGlamourerUnavailable = true;
				_mareMediator.Publish(new NotificationMessage("Glamourer inactive", "Your Glamourer installation is not active or out of date. Update Glamourer to continue to use Mare. If you just updated Glamourer, ignore this message.", NotificationType.Error));
			}
		}
	}

	public async Task ApplyAllAsync(ILogger logger, GameObjectHandler handler, string? customization, Guid applicationId, CancellationToken token, bool fireAndForget = false)
	{
		if (!APIAvailable || string.IsNullOrEmpty(customization) || _dalamudUtil.IsZoning)
		{
			return;
		}
		await _redrawManager.RedrawSemaphore.WaitAsync(token).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			await _redrawManager.PenumbraRedrawInternalAsync(logger, handler, applicationId, delegate(ICharacter chara)
			{
				try
				{
					logger.LogDebug("[{appid}] Calling on IPC: GlamourerApplyAll", applicationId);
					_glamourerApplyAll.Invoke(customization, chara.ObjectIndex, LockCode, ApplyFlag.Equipment | ApplyFlag.Customization | ApplyFlag.Lock);
				}
				catch (Exception exception)
				{
					logger.LogWarning(exception, "[{appid}] Failed to apply Glamourer data", applicationId);
				}
			}, token).ConfigureAwait(continueOnCapturedContext: false);
		}
		finally
		{
			_redrawManager.RedrawSemaphore.Release();
		}
	}

	public async Task<string> GetCharacterCustomizationAsync(nint character)
	{
		if (!APIAvailable)
		{
			return string.Empty;
		}
		try
		{
			return await _dalamudUtil.RunOnFrameworkThread(() => (_dalamudUtil.CreateGameObject(character) is ICharacter character2) ? (_glamourerGetAllCustomization.Invoke(character2.ObjectIndex).Item2 ?? string.Empty) : string.Empty, "GetCharacterCustomizationAsync", "C:\\Users\\Owner\\sync_client2\\XIVSync\\Interop\\Ipc\\IpcCallerGlamourer.cs", 139).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch
		{
			return string.Empty;
		}
	}

	public async Task RevertAsync(ILogger logger, GameObjectHandler handler, Guid applicationId, CancellationToken token)
	{
		if (!APIAvailable || _dalamudUtil.IsZoning)
		{
			return;
		}
		try
		{
			await _redrawManager.RedrawSemaphore.WaitAsync(token).ConfigureAwait(continueOnCapturedContext: false);
			await _redrawManager.PenumbraRedrawInternalAsync(logger, handler, applicationId, delegate(ICharacter chara)
			{
				try
				{
					logger.LogDebug("[{appid}] Calling On IPC: GlamourerUnlockName", applicationId);
					_glamourerUnlock.Invoke(chara.ObjectIndex, LockCode);
					logger.LogDebug("[{appid}] Calling On IPC: GlamourerRevert", applicationId);
					_glamourerRevert.Invoke(chara.ObjectIndex, LockCode, ApplyFlag.Equipment | ApplyFlag.Customization);
					logger.LogDebug("[{appid}] Calling On IPC: PenumbraRedraw", applicationId);
					_mareMediator.Publish(new PenumbraRedrawCharacterMessage(chara));
				}
				catch (Exception exception)
				{
					logger.LogWarning(exception, "[{appid}] Error during GlamourerRevert", applicationId);
				}
			}, token).ConfigureAwait(continueOnCapturedContext: false);
		}
		finally
		{
			_redrawManager.RedrawSemaphore.Release();
		}
	}

	public async Task RevertByNameAsync(ILogger logger, string name, Guid applicationId)
	{
		if (APIAvailable && !_dalamudUtil.IsZoning)
		{
			await _dalamudUtil.RunOnFrameworkThread(delegate
			{
				RevertByName(logger, name, applicationId);
			}, "RevertByNameAsync", "C:\\Users\\Owner\\sync_client2\\XIVSync\\Interop\\Ipc\\IpcCallerGlamourer.cs", 189).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	public void RevertByName(ILogger logger, string name, Guid applicationId)
	{
		if (!APIAvailable || _dalamudUtil.IsZoning)
		{
			return;
		}
		try
		{
			logger.LogDebug("[{appid}] Calling On IPC: GlamourerRevertByName", applicationId);
			_glamourerRevertByName.Invoke(name, LockCode, ApplyFlag.Equipment | ApplyFlag.Customization);
			logger.LogDebug("[{appid}] Calling On IPC: GlamourerUnlockName", applicationId);
			_glamourerUnlockByName.Invoke(name, LockCode);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Error during Glamourer RevertByName");
		}
	}

	private void GlamourerChanged(nint address)
	{
		_mareMediator.Publish(new GlamourerChangedMessage(address));
	}
}
