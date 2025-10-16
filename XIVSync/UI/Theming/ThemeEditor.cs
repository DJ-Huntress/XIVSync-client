using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Microsoft.Extensions.Logging;

namespace XIVSync.UI.Theming;

public class ThemeEditor
{
	private readonly ILogger _logger;

	private readonly UiSharedService _uiSharedService;

	private ThemePalette _workingTheme = new ThemePalette();

	private string _selectedPreset = "Blue";

	private readonly ThemeManager _themeManager;

	public Action? OnClosed { get; set; }

	public ThemeEditor(ILogger logger, UiSharedService uiSharedService, ThemeManager themeManager)
	{
		_logger = logger;
		_uiSharedService = uiSharedService;
		_themeManager = themeManager;
		_selectedPreset = _themeManager.CurrentThemeName;
		_workingTheme = _themeManager.Current.Clone();
	}

	public void Draw(ThemePalette currentTheme)
	{
		ImGui.AlignTextToFramePadding();
		Vector4 col = currentTheme.Accent;
		ImGui.TextColored(in col, "Theme");
		ImGui.SameLine();
		using (ImRaii.PushColor(ImGuiCol.Text, currentTheme.TextSecondary))
		{
			ImGui.TextUnformatted("(changes preview automatically)");
		}
		ImGui.Separator();
		ImGui.TextUnformatted("Preset");
		ImGui.SameLine();
		string displayName = (_themeManager.IsCustomTheme ? "Custom" : _selectedPreset);
		if (ImGui.BeginCombo("##theme-preset-inline", displayName))
		{
			foreach (KeyValuePair<string, ThemePalette> kv in _themeManager.PredefinedThemes)
			{
				bool selected = kv.Key == _selectedPreset;
				if (ImGui.Selectable(kv.Key, selected))
				{
					_selectedPreset = kv.Key;
					_workingTheme = kv.Value.Clone();
					_themeManager.SetTheme(_selectedPreset);
				}
				if (selected)
				{
					ImGui.SetItemDefaultFocus();
				}
			}
			ImGui.EndCombo();
		}
		ImGui.Separator();
		DrawColorRow("Panel Background", () => _workingTheme.PanelBg, delegate(Vector4 v)
		{
			_workingTheme.PanelBg = v;
		}, currentTheme);
		DrawColorRow("Panel Border", () => _workingTheme.PanelBorder, delegate(Vector4 v)
		{
			_workingTheme.PanelBorder = v;
		}, currentTheme);
		DrawColorRow("Header Background", () => _workingTheme.HeaderBg, delegate(Vector4 v)
		{
			_workingTheme.HeaderBg = v;
		}, currentTheme);
		DrawColorRow("Accent", () => _workingTheme.Accent, delegate(Vector4 v)
		{
			_workingTheme.Accent = v;
		}, currentTheme);
		DrawColorRow("Button", () => _workingTheme.Btn, delegate(Vector4 v)
		{
			_workingTheme.Btn = v;
		}, currentTheme);
		DrawColorRow("Button Hovered", () => _workingTheme.BtnHovered, delegate(Vector4 v)
		{
			_workingTheme.BtnHovered = v;
		}, currentTheme);
		DrawColorRow("Button Active", () => _workingTheme.BtnActive, delegate(Vector4 v)
		{
			_workingTheme.BtnActive = v;
		}, currentTheme);
		DrawColorRow("Button Text", () => _workingTheme.BtnText, delegate(Vector4 v)
		{
			_workingTheme.BtnText = v;
		}, currentTheme);
		DrawColorRow("Button Text Hovered", () => _workingTheme.BtnTextHovered, delegate(Vector4 v)
		{
			_workingTheme.BtnTextHovered = v;
		}, currentTheme);
		DrawColorRow("Button Text Active", () => _workingTheme.BtnTextActive, delegate(Vector4 v)
		{
			_workingTheme.BtnTextActive = v;
		}, currentTheme);
		DrawColorRow("Text Primary", () => _workingTheme.TextPrimary, delegate(Vector4 v)
		{
			_workingTheme.TextPrimary = v;
		}, currentTheme);
		DrawColorRow("Text Secondary", () => _workingTheme.TextSecondary, delegate(Vector4 v)
		{
			_workingTheme.TextSecondary = v;
		}, currentTheme);
		DrawColorRow("Text Disabled", () => _workingTheme.TextDisabled, delegate(Vector4 v)
		{
			_workingTheme.TextDisabled = v;
		}, currentTheme);
		DrawColorRow("Link", () => _workingTheme.Link, delegate(Vector4 v)
		{
			_workingTheme.Link = v;
		}, currentTheme);
		DrawColorRow("Link Hover", () => _workingTheme.LinkHover, delegate(Vector4 v)
		{
			_workingTheme.LinkHover = v;
		}, currentTheme);
		DrawColorRow("Tooltip Background", () => _workingTheme.TooltipBg, delegate(Vector4 v)
		{
			_workingTheme.TooltipBg = v;
		}, currentTheme);
		DrawColorRow("Tooltip Text", () => _workingTheme.TooltipText, delegate(Vector4 v)
		{
			_workingTheme.TooltipText = v;
		}, currentTheme);
		ImGui.Separator();
		ImGui.AlignTextToFramePadding();
		col = currentTheme.Accent;
		ImGui.TextColored(in col, "Folder Colors");
		DrawColorRow("Folder Visible", () => _workingTheme.FolderVisible, delegate(Vector4 v)
		{
			_workingTheme.FolderVisible = v;
		}, currentTheme);
		DrawColorRow("Folder Online", () => _workingTheme.FolderOnline, delegate(Vector4 v)
		{
			_workingTheme.FolderOnline = v;
		}, currentTheme);
		DrawColorRow("Folder Offline", () => _workingTheme.FolderOffline, delegate(Vector4 v)
		{
			_workingTheme.FolderOffline = v;
		}, currentTheme);
		DrawColorRow("Folder Offline Syncshell", () => _workingTheme.FolderOfflineSyncshell, delegate(Vector4 v)
		{
			_workingTheme.FolderOfflineSyncshell = v;
		}, currentTheme);
		DrawColorRow("Folder Unpaired", () => _workingTheme.FolderUnpaired, delegate(Vector4 v)
		{
			_workingTheme.FolderUnpaired = v;
		}, currentTheme);
		DrawColorRow("Folder All", () => _workingTheme.FolderAll, delegate(Vector4 v)
		{
			_workingTheme.FolderAll = v;
		}, currentTheme);
		DrawColorRow("Folder Custom", () => _workingTheme.FolderCustom, delegate(Vector4 v)
		{
			_workingTheme.FolderCustom = v;
		}, currentTheme);
		ImGui.Separator();
		using (ImRaii.PushColor(ImGuiCol.Text, currentTheme.BtnText))
		{
			if (ImGui.Button("Reset to Preset") && _themeManager.PredefinedThemes.TryGetValue(_selectedPreset, out ThemePalette preset))
			{
				_workingTheme = preset.Clone();
				_themeManager.SetTheme(_selectedPreset);
			}
			ImGui.SameLine();
			if (ImGui.Button("Cancel"))
			{
				_workingTheme = _themeManager.Current.Clone();
				OnClosed?.Invoke();
			}
			ImGui.SameLine();
			if (ImGui.Button("Save"))
			{
				try
				{
					_themeManager.SetCustomTheme(_workingTheme);
					_logger.LogInformation("Theme saved successfully");
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Failed to save theme");
				}
				OnClosed?.Invoke();
			}
		}
		ImGui.Spacing();
	}

	public void OnOpened()
	{
		_selectedPreset = _themeManager.CurrentThemeName;
		_workingTheme = _themeManager.Current.Clone();
	}

	private void DrawColorRow(string label, Func<Vector4> get, Action<Vector4> set, ThemePalette currentTheme)
	{
		float offsetFromStartX = 200f * ImGuiHelpers.GlobalScale;
		float swatchSize = 22f * ImGuiHelpers.GlobalScale;
		ImGui.AlignTextToFramePadding();
		ImGui.TextUnformatted(label);
		ImGui.SameLine(offsetFromStartX);
		ImGui.PushID(label);
		Vector4 color = get();
		ImGui.ColorButton("##swatch", in color, ImGuiColorEditFlags.AlphaPreviewHalf, new Vector2(swatchSize, swatchSize));
		if (ImGui.IsItemClicked())
		{
			ImGui.OpenPopup("picker");
		}
		if (ImGui.BeginPopup("picker"))
		{
			ImGuiColorEditFlags flags = ImGuiColorEditFlags.NoSidePreview | ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.PickerHueWheel;
			ImGui.ColorPicker4("##picker", ref color, flags);
			set(color);
			_themeManager.SetCustomTheme(_workingTheme);
			ImGui.EndPopup();
		}
		ImGui.PopID();
	}
}
