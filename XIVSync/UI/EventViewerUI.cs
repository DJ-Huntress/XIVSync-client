using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Microsoft.Extensions.Logging;
using XIVSync.Services;
using XIVSync.Services.Events;
using XIVSync.Services.Mediator;

namespace XIVSync.UI;

internal class EventViewerUI : WindowMediatorSubscriberBase
{
	private readonly EventAggregator _eventAggregator;

	private readonly UiSharedService _uiSharedService;

	private List<Event> _currentEvents = new List<Event>();

	private Lazy<List<Event>> _filteredEvents;

	private string _filterFreeText = string.Empty;

	private string _filterCharacter = string.Empty;

	private string _filterUid = string.Empty;

	private string _filterSource = string.Empty;

	private string _filterEvent = string.Empty;

	private bool _filterPlugin = true;

	private bool _filterAuth = true;

	private bool _filterServices = true;

	private bool _filterNetwork = true;

	private bool _filterFile = true;

	private bool _filterOther = true;

	private bool _filterInfo = true;

	private bool _filterWarning = true;

	private bool _filterError = true;

	private List<Event> CurrentEvents
	{
		get
		{
			return _currentEvents;
		}
		set
		{
			_currentEvents = value;
			_filteredEvents = RecreateFilter();
		}
	}

	public EventViewerUI(ILogger<EventViewerUI> logger, MareMediator mediator, EventAggregator eventAggregator, UiSharedService uiSharedService, PerformanceCollectorService performanceCollectorService)
		: base(logger, mediator, "Event Viewer", performanceCollectorService)
	{
		_eventAggregator = eventAggregator;
		_uiSharedService = uiSharedService;
		base.SizeConstraints = new WindowSizeConstraints
		{
			MinimumSize = new Vector2(600f, 500f),
			MaximumSize = new Vector2(1000f, 2000f)
		};
		_filteredEvents = RecreateFilter();
	}

	private Lazy<List<Event>> RecreateFilter()
	{
		return new Lazy<List<Event>>(() => CurrentEvents.Where(delegate(Event f)
		{
			bool flag = (string.IsNullOrEmpty(_filterFreeText) || f.EventSource.Contains(_filterFreeText, StringComparison.OrdinalIgnoreCase) || f.Character.Contains(_filterFreeText, StringComparison.OrdinalIgnoreCase) || f.UID.Contains(_filterFreeText, StringComparison.OrdinalIgnoreCase) || f.Message.Contains(_filterFreeText, StringComparison.OrdinalIgnoreCase)) && (string.IsNullOrEmpty(_filterUid) || f.UID.Contains(_filterUid, StringComparison.OrdinalIgnoreCase)) && (string.IsNullOrEmpty(_filterSource) || f.EventSource.Contains(_filterSource, StringComparison.OrdinalIgnoreCase)) && (string.IsNullOrEmpty(_filterCharacter) || f.Character.Contains(_filterCharacter, StringComparison.OrdinalIgnoreCase)) && (string.IsNullOrEmpty(_filterEvent) || f.Message.Contains(_filterEvent, StringComparison.OrdinalIgnoreCase));
			if (flag)
			{
				flag = GetEventCategory(f.EventSource) switch
				{
					"Plugin" => _filterPlugin, 
					"Auth" => _filterAuth, 
					"Services" => _filterServices, 
					"Network" => _filterNetwork, 
					"File" => _filterFile, 
					_ => _filterOther, 
				};
			}
			bool flag2 = flag;
			if (flag2)
			{
				flag2 = f.EventSeverity switch
				{
					EventSeverity.Informational => _filterInfo, 
					EventSeverity.Warning => _filterWarning, 
					EventSeverity.Error => _filterError, 
					_ => true, 
				};
			}
			return flag2;
		}).ToList());
	}

	private static string GetEventCategory(string eventSource)
	{
		string source = eventSource.ToLowerInvariant();
		if (source.Contains("plugin") || source.Contains("ui") || source.Contains("window"))
		{
			return "Plugin";
		}
		string source2 = source;
		if (source2.Contains("auth") || source2.Contains("login") || source2.Contains("token"))
		{
			return "Auth";
		}
		string source3 = source;
		if (source3.Contains("service") || source3.Contains("manager") || source3.Contains("controller"))
		{
			return "Services";
		}
		string source4 = source;
		if (source4.Contains("network") || source4.Contains("connection") || source4.Contains("signalr") || source4.Contains("api"))
		{
			return "Network";
		}
		string source5 = source;
		if (source5.Contains("file") || source5.Contains("cache") || source5.Contains("download") || source5.Contains("upload"))
		{
			return "File";
		}
		return "Other";
	}

	private void ClearFilters()
	{
		_filterFreeText = string.Empty;
		_filterCharacter = string.Empty;
		_filterUid = string.Empty;
		_filterSource = string.Empty;
		_filterEvent = string.Empty;
		_filterPlugin = true;
		_filterAuth = true;
		_filterServices = true;
		_filterNetwork = true;
		_filterFile = true;
		_filterOther = true;
		_filterInfo = true;
		_filterWarning = true;
		_filterError = true;
		_filteredEvents = RecreateFilter();
	}

	private void CopyLogsToClipboard()
	{
		try
		{
			StringBuilder logText = new StringBuilder();
			foreach (Event ev in _filteredEvents.Value)
			{
				logText.AppendLine(ev.ToString());
			}
			if (logText.Length > 0)
			{
				ImGui.SetClipboardText(logText.ToString());
			}
		}
		catch (Exception)
		{
		}
	}

	public override void OnOpen()
	{
		CurrentEvents = _eventAggregator.EventList.Value.OrderByDescending((Event f) => f.EventTime).ToList();
		ClearFilters();
	}

	protected override void DrawInternal()
	{
		using (ImRaii.Disabled(!_eventAggregator.NewEventsAvailable))
		{
			if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowsToCircle, "Refresh events"))
			{
				CurrentEvents = _eventAggregator.EventList.Value.OrderByDescending((Event f) => f.EventTime).ToList();
			}
		}
		if (_eventAggregator.NewEventsAvailable)
		{
			ImGui.SameLine();
			ImGui.AlignTextToFramePadding();
			UiSharedService.ColorTextWrapped("New events are available, press refresh to update", ImGuiColors.DalamudYellow);
		}
		float iconTextButtonSize = _uiSharedService.GetIconTextButtonSize(FontAwesomeIcon.Copy, "Copy to Clipboard");
		float folderButtonSize = _uiSharedService.GetIconTextButtonSize(FontAwesomeIcon.FolderOpen, "Open EventLog Folder");
		float totalButtonWidth = iconTextButtonSize + folderButtonSize + ImGui.GetStyle().ItemSpacing.X;
		ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - totalButtonWidth);
		if (_uiSharedService.IconTextButton(FontAwesomeIcon.Copy, "Copy to Clipboard"))
		{
			CopyLogsToClipboard();
		}
		UiSharedService.AttachToolTip("Copy filtered logs to clipboard");
		ImGui.SameLine();
		if (_uiSharedService.IconTextButton(FontAwesomeIcon.FolderOpen, "Open EventLog folder"))
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = _eventAggregator.EventLogFolder,
				UseShellExecute = true,
				WindowStyle = ProcessWindowStyle.Normal
			});
		}
		_uiSharedService.BigText("Last Events");
		ImRaii.IEndObject endObject = ImRaii.TreeNode("Filter");
		if (endObject)
		{
			if (_uiSharedService.IconTextButton(FontAwesomeIcon.Ban, "Clear Filters"))
			{
				ClearFilters();
			}
			ImGui.Text("Text Filters:");
			ImGui.SetNextItemWidth(200f);
			int num = 0 | (ImGui.InputText("Search all columns", ref _filterFreeText, 50) ? 1 : 0);
			ImGui.SetNextItemWidth(200f);
			int num2 = num | (ImGui.InputText("Filter by Source", ref _filterSource, 50) ? 1 : 0);
			ImGui.SetNextItemWidth(200f);
			int num3 = num2 | (ImGui.InputText("Filter by UID", ref _filterUid, 50) ? 1 : 0);
			ImGui.SetNextItemWidth(200f);
			int num4 = num3 | (ImGui.InputText("Filter by Character", ref _filterCharacter, 50) ? 1 : 0);
			ImGui.SetNextItemWidth(200f);
			int num5 = num4 | (ImGui.InputText("Filter by Event", ref _filterEvent, 50) ? 1 : 0);
			ImGui.Separator();
			ImGui.Text("Categories:");
			ImGui.Columns(3, "CategoryColumns", border: false);
			int num6 = (int)((uint)num5 | (ImGui.Checkbox("Plugin", ref _filterPlugin) ? 1u : 0u) | (ImGui.Checkbox("Auth", ref _filterAuth) ? 1u : 0u)) | (ImGui.Checkbox("Services", ref _filterServices) ? 1 : 0);
			ImGui.NextColumn();
			int num7 = (int)((uint)num6 | (ImGui.Checkbox("Network", ref _filterNetwork) ? 1u : 0u) | (ImGui.Checkbox("File", ref _filterFile) ? 1u : 0u)) | (ImGui.Checkbox("Other", ref _filterOther) ? 1 : 0);
			ImGui.Columns();
			ImGui.Separator();
			ImGui.Text("Severity:");
			ImGui.Columns(3, "SeverityColumns", border: false);
			int num8 = (int)((uint)num7 | (ImGui.Checkbox("Info", ref _filterInfo) ? 1u : 0u) | (ImGui.Checkbox("Warning", ref _filterWarning) ? 1u : 0u)) | (ImGui.Checkbox("Error", ref _filterError) ? 1 : 0);
			ImGui.Columns();
			if (num8 != 0)
			{
				_filteredEvents = RecreateFilter();
			}
		}
		endObject.Dispose();
		float cursorPos = ImGui.GetCursorPosY();
		Vector2 windowContentRegionMax = ImGui.GetWindowContentRegionMax();
		Vector2 min = ImGui.GetWindowContentRegionMin();
		float width = windowContentRegionMax.X - min.X;
		float height = windowContentRegionMax.Y - cursorPos;
		using ImRaii.IEndObject table = ImRaii.Table("eventTable", 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.ScrollY, new Vector2(width, height));
		if (!ImRaii.IEndObject.op_True(table))
		{
			return;
		}
		ImGui.TableSetupScrollFreeze(0, 1);
		ImGui.TableSetupColumn(string.Empty, ImGuiTableColumnFlags.NoSort);
		ImGui.TableSetupColumn("Time");
		ImGui.TableSetupColumn("Source");
		ImGui.TableSetupColumn("UID");
		ImGui.TableSetupColumn("Character");
		ImGui.TableSetupColumn("Event");
		ImGui.TableHeadersRow();
		foreach (Event ev in _filteredEvents.Value)
		{
			FontAwesomeIcon icon = ev.EventSeverity switch
			{
				EventSeverity.Informational => FontAwesomeIcon.InfoCircle, 
				EventSeverity.Warning => FontAwesomeIcon.ExclamationTriangle, 
				EventSeverity.Error => FontAwesomeIcon.Cross, 
				_ => FontAwesomeIcon.QuestionCircle, 
			};
			Vector4 iconColor = ev.EventSeverity switch
			{
				EventSeverity.Informational => default(Vector4), 
				EventSeverity.Warning => ImGuiColors.DalamudYellow, 
				EventSeverity.Error => ImGuiColors.DalamudRed, 
				_ => default(Vector4), 
			};
			ImGui.TableNextColumn();
			_uiSharedService.IconText(icon, (iconColor == default(Vector4)) ? ((Vector4?)null) : new Vector4?(iconColor));
			UiSharedService.AttachToolTip(ev.EventSeverity.ToString());
			ImGui.TableNextColumn();
			ImGui.AlignTextToFramePadding();
			ImGui.TextUnformatted(ev.EventTime.ToString("G", CultureInfo.CurrentCulture));
			ImGui.TableNextColumn();
			ImGui.AlignTextToFramePadding();
			ImGui.TextUnformatted(ev.EventSource);
			ImGui.TableNextColumn();
			ImGui.AlignTextToFramePadding();
			ImGui.TextUnformatted(string.IsNullOrEmpty(ev.UID) ? "--" : ev.UID);
			ImGui.TableNextColumn();
			ImGui.AlignTextToFramePadding();
			ImGui.TextUnformatted(string.IsNullOrEmpty(ev.Character) ? "--" : ev.Character);
			ImGui.TableNextColumn();
			ImGui.AlignTextToFramePadding();
			float posX = ImGui.GetCursorPosX();
			float maxTextLength = ImGui.GetWindowContentRegionMax().X - posX;
			float textSize = ImGui.CalcTextSize(ev.Message).X;
			string msg = ev.Message;
			while (textSize > maxTextLength)
			{
				string text = msg;
				msg = text.Substring(0, text.Length - 5) + "...";
				textSize = ImGui.CalcTextSize(msg).X;
			}
			ImGui.TextUnformatted(msg);
			if (!string.Equals(msg, ev.Message, StringComparison.Ordinal))
			{
				UiSharedService.AttachToolTip(ev.Message);
			}
		}
	}
}
