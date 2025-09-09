using System;
using System.Collections.Generic;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using Microsoft.Extensions.Logging;
using XIVSync.MareConfiguration;
using XIVSync.Services.Mediator;
using XIVSync.UI;
using XIVSync.UI.Components.Popup;

namespace XIVSync.Services;

public sealed class UiService : DisposableMediatorSubscriberBase
{
	private readonly List<WindowMediatorSubscriberBase> _createdWindows = new List<WindowMediatorSubscriberBase>();

	private readonly IUiBuilder _uiBuilder;

	private readonly FileDialogManager _fileDialogManager;

	private readonly ILogger<UiService> _logger;

	private readonly MareConfigService _mareConfigService;

	private readonly WindowSystem _windowSystem;

	private readonly UiFactory _uiFactory;

	public UiService(ILogger<UiService> logger, IUiBuilder uiBuilder, MareConfigService mareConfigService, WindowSystem windowSystem, IEnumerable<WindowMediatorSubscriberBase> windows, UiFactory uiFactory, FileDialogManager fileDialogManager, MareMediator mareMediator)
		: base(logger, mareMediator)
	{
		_logger = logger;
		_logger.LogTrace("Creating {type}", GetType().Name);
		_uiBuilder = uiBuilder;
		_mareConfigService = mareConfigService;
		_windowSystem = windowSystem;
		_uiFactory = uiFactory;
		_fileDialogManager = fileDialogManager;
		_uiBuilder.DisableGposeUiHide = true;
		_uiBuilder.Draw += Draw;
		_uiBuilder.OpenConfigUi += ToggleUi;
		_uiBuilder.OpenMainUi += ToggleMainUi;
		foreach (WindowMediatorSubscriberBase window in windows)
		{
			_windowSystem.AddWindow(window);
		}
		base.Mediator.Subscribe(this, delegate(ProfileOpenStandaloneMessage msg)
		{
			if (!_createdWindows.Exists((WindowMediatorSubscriberBase p) => p is StandaloneProfileUi standaloneProfileUi2 && string.Equals(standaloneProfileUi2.Pair.UserData.AliasOrUID, msg.Pair.UserData.AliasOrUID, StringComparison.Ordinal)))
			{
				StandaloneProfileUi standaloneProfileUi = _uiFactory.CreateStandaloneProfileUi(msg.Pair);
				_createdWindows.Add(standaloneProfileUi);
				_windowSystem.AddWindow(standaloneProfileUi);
			}
		});
		base.Mediator.Subscribe(this, delegate(OpenSyncshellAdminPanel msg)
		{
			if (!_createdWindows.Exists((WindowMediatorSubscriberBase p) => p is SyncshellAdminUI syncshellAdminUI2 && string.Equals(syncshellAdminUI2.GroupFullInfo.GID, msg.GroupInfo.GID, StringComparison.Ordinal)))
			{
				SyncshellAdminUI syncshellAdminUI = _uiFactory.CreateSyncshellAdminUi(msg.GroupInfo);
				_createdWindows.Add(syncshellAdminUI);
				_windowSystem.AddWindow(syncshellAdminUI);
			}
		});
		base.Mediator.Subscribe(this, delegate(OpenPermissionWindow msg)
		{
			if (!_createdWindows.Exists((WindowMediatorSubscriberBase p) => p is PermissionWindowUI permissionWindowUI2 && msg.Pair == permissionWindowUI2.Pair))
			{
				PermissionWindowUI permissionWindowUI = _uiFactory.CreatePermissionPopupUi(msg.Pair);
				_createdWindows.Add(permissionWindowUI);
				_windowSystem.AddWindow(permissionWindowUI);
			}
		});
		base.Mediator.Subscribe(this, delegate(RemoveWindowMessage msg)
		{
			_windowSystem.RemoveWindow(msg.Window);
			_createdWindows.Remove(msg.Window);
			msg.Window.Dispose();
		});
	}

	public void ToggleMainUi()
	{
		if (_mareConfigService.Current.HasValidSetup())
		{
			base.Mediator.Publish(new UiToggleMessage(typeof(CompactUi)));
		}
		else
		{
			base.Mediator.Publish(new UiToggleMessage(typeof(IntroUi)));
		}
	}

	public void ToggleUi()
	{
		if (_mareConfigService.Current.HasValidSetup())
		{
			base.Mediator.Publish(new UiToggleMessage(typeof(ModernSettingsUi)));
		}
		else
		{
			base.Mediator.Publish(new UiToggleMessage(typeof(IntroUi)));
		}
	}

	protected override void Dispose(bool disposing)
	{
		base.Dispose(disposing);
		_logger.LogTrace("Disposing {type}", GetType().Name);
		_windowSystem.RemoveAllWindows();
		foreach (WindowMediatorSubscriberBase createdWindow in _createdWindows)
		{
			createdWindow.Dispose();
		}
		_uiBuilder.Draw -= Draw;
		_uiBuilder.OpenConfigUi -= ToggleUi;
		_uiBuilder.OpenMainUi -= ToggleMainUi;
	}

	private void Draw()
	{
		_windowSystem.Draw();
		_fileDialogManager.Draw();
	}
}
