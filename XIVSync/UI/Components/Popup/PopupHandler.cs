using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Microsoft.Extensions.Logging;
using XIVSync.Services;
using XIVSync.Services.Mediator;

namespace XIVSync.UI.Components.Popup;

public class PopupHandler : WindowMediatorSubscriberBase
{
	protected bool _openPopup;

	private readonly HashSet<IPopupHandler> _handlers;

	private readonly UiSharedService _uiSharedService;

	private IPopupHandler? _currentHandler;

	public PopupHandler(ILogger<PopupHandler> logger, MareMediator mediator, IEnumerable<IPopupHandler> popupHandlers, PerformanceCollectorService performanceCollectorService, UiSharedService uiSharedService)
		: base(logger, mediator, "MarePopupHandler", performanceCollectorService)
	{
		base.Flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoBringToFrontOnFocus;
		base.IsOpen = true;
		_handlers = popupHandlers.ToHashSet();
		base.Mediator.Subscribe(this, delegate(OpenBanUserPopupMessage msg)
		{
			_openPopup = true;
			_currentHandler = _handlers.OfType<BanUserPopupHandler>().Single();
			((BanUserPopupHandler)_currentHandler).Open(msg);
			base.IsOpen = true;
		});
		base.Mediator.Subscribe<OpenCensusPopupMessage>(this, delegate
		{
			_openPopup = true;
			_currentHandler = _handlers.OfType<CensusPopupHandler>().Single();
			base.IsOpen = true;
		});
		base.Mediator.Subscribe(this, delegate(OpenChangelogPopupMessage msg)
		{
			_openPopup = true;
			_currentHandler = _handlers.OfType<ChangelogPopupHandler>().Single();
			((ChangelogPopupHandler)_currentHandler).Open(msg.Version);
			base.IsOpen = true;
		});
		_uiSharedService = uiSharedService;
		base.DisableWindowSounds = true;
	}

	protected override void DrawInternal()
	{
		if (_currentHandler == null)
		{
			return;
		}
		if (_openPopup)
		{
			ImGui.OpenPopup(base.WindowName);
			_openPopup = false;
		}
		Vector2 size = ImGui.GetWindowViewport().Size;
		ImGui.SetNextWindowSize(_currentHandler.PopupSize * ImGuiHelpers.GlobalScale);
		ImGui.SetNextWindowPos(size / 2f, ImGuiCond.Always, new Vector2(0.5f));
		using ImRaii.IEndObject popup = ImRaii.Popup(base.WindowName, ImGuiWindowFlags.Modal);
		if (!popup)
		{
			return;
		}
		_currentHandler.DrawContent();
		if (_currentHandler.ShowClose)
		{
			ImGui.Separator();
			if (_uiSharedService.IconTextButton(FontAwesomeIcon.Times, "Close"))
			{
				ImGui.CloseCurrentPopup();
			}
		}
	}
}
