using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using XIVSync.MareConfiguration;
using XIVSync.MareConfiguration.Configurations;

namespace XIVSync.UI.Theming;

public class ThemeManager
{
	private class ThemeScope : IDisposable
	{
		private readonly int _colorCount;

		private readonly int _styleVarCount;

		public ThemeScope(int colorCount, int styleVarCount = 0)
		{
			_colorCount = colorCount;
			_styleVarCount = styleVarCount;
		}

		public void Dispose()
		{
			if (_styleVarCount > 0)
			{
				ImGui.PopStyleVar(_styleVarCount);
			}
			ImGui.PopStyleColor(_colorCount);
		}
	}

	private readonly MareConfigService _configService;

	private readonly Dictionary<string, ThemePalette> _predefinedThemes;

	private ThemePalette _currentTheme;

	private string _currentThemeName = "Blue";

	private bool _isCustomTheme;

	private ThemePalette? _savedCustomTheme;

	public static ThemeManager? Instance { get; private set; }

	public ThemePalette Current => _currentTheme;

	public string CurrentThemeName => _currentThemeName;

	public bool IsCustomTheme => _isCustomTheme;

	public IReadOnlyDictionary<string, ThemePalette> PredefinedThemes => _predefinedThemes;

	public ThemeManager(MareConfigService configService)
	{
		_configService = configService;
		_predefinedThemes = CreatePredefinedThemes();
		_currentTheme = new ThemePalette();
		LoadSavedTheme();
		Instance = this;
	}

	public void SetTheme(string themeName)
	{
		if (_predefinedThemes.TryGetValue(themeName, out ThemePalette theme))
		{
			if (_isCustomTheme)
			{
				_savedCustomTheme = _currentTheme;
			}
			_currentTheme = theme.Clone();
			_currentThemeName = themeName;
			_isCustomTheme = false;
			SaveThemeSettings();
		}
	}

	public void SetCustomTheme(ThemePalette customTheme)
	{
		_currentTheme = customTheme.Clone();
		_currentThemeName = "Custom";
		_isCustomTheme = true;
		_savedCustomTheme = customTheme.Clone();
		SaveThemeSettings();
	}

	public bool RestoreSavedCustomTheme()
	{
		if (_savedCustomTheme != null)
		{
			_currentTheme = _savedCustomTheme.Clone();
			_currentThemeName = "Custom";
			_isCustomTheme = true;
			SaveThemeSettings();
			return true;
		}
		return false;
	}

	public IDisposable PushTheme()
	{
		ImGui.PushStyleColor(ImGuiCol.WindowBg, _currentTheme.PanelBg);
		ImGui.PushStyleColor(ImGuiCol.ChildBg, _currentTheme.PanelBg);
		ImGui.PushStyleColor(ImGuiCol.Border, _currentTheme.PanelBorder);
		ImGui.PushStyleColor(ImGuiCol.TitleBg, _currentTheme.HeaderBg);
		ImGui.PushStyleColor(ImGuiCol.TitleBgActive, _currentTheme.HeaderBg);
		ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, _currentTheme.HeaderBg);
		ImGui.PushStyleColor(ImGuiCol.MenuBarBg, _currentTheme.HeaderBg);
		ImGui.PushStyleColor(ImGuiCol.Header, _currentTheme.HeaderBg);
		ImGui.PushStyleColor(ImGuiCol.HeaderHovered, _currentTheme.BtnHovered);
		ImGui.PushStyleColor(ImGuiCol.HeaderActive, _currentTheme.BtnActive);
		ImGui.PushStyleColor(ImGuiCol.Button, _currentTheme.Btn);
		ImGui.PushStyleColor(ImGuiCol.ButtonHovered, _currentTheme.BtnHovered);
		ImGui.PushStyleColor(ImGuiCol.ButtonActive, _currentTheme.BtnActive);
		ImGui.PushStyleColor(ImGuiCol.Text, _currentTheme.TextPrimary);
		ImGui.PushStyleColor(ImGuiCol.TextDisabled, _currentTheme.TextDisabled);
		ImGui.PushStyleColor(ImGuiCol.Tab, _currentTheme.Btn);
		ImGui.PushStyleColor(ImGuiCol.TabHovered, _currentTheme.BtnHovered);
		ImGui.PushStyleColor(ImGuiCol.TabActive, _currentTheme.BtnActive);
		ImGui.PushStyleColor(ImGuiCol.TabUnfocused, _currentTheme.Btn);
		ImGui.PushStyleColor(ImGuiCol.TabUnfocusedActive, _currentTheme.BtnActive);
		ImGui.PushStyleColor(ImGuiCol.FrameBg, _currentTheme.Btn);
		ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, _currentTheme.BtnHovered);
		ImGui.PushStyleColor(ImGuiCol.FrameBgActive, _currentTheme.BtnActive);
		ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, new Vector4(0f, 0f, 0f, 0f));
		ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, _currentTheme.Btn);
		ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, _currentTheme.BtnHovered);
		ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, _currentTheme.BtnActive);
		ImGui.PushStyleColor(ImGuiCol.CheckMark, _currentTheme.Accent);
		ImGui.PushStyleColor(ImGuiCol.SliderGrab, _currentTheme.Accent);
		ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, _currentTheme.BtnActive);
		ImGui.PushStyleColor(ImGuiCol.Separator, _currentTheme.PanelBorder);
		ImGui.PushStyleColor(ImGuiCol.SeparatorHovered, _currentTheme.BtnHovered);
		ImGui.PushStyleColor(ImGuiCol.SeparatorActive, _currentTheme.BtnActive);
		ImGui.PushStyleColor(ImGuiCol.ResizeGrip, _currentTheme.Accent);
		ImGui.PushStyleColor(ImGuiCol.ResizeGripHovered, _currentTheme.BtnHovered);
		ImGui.PushStyleColor(ImGuiCol.ResizeGripActive, _currentTheme.BtnActive);
		ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, _currentTheme.HeaderBg);
		ImGui.PushStyleColor(ImGuiCol.TableBorderStrong, _currentTheme.PanelBorder);
		ImGui.PushStyleColor(ImGuiCol.TableBorderLight, _currentTheme.PanelBorder);
		ImGui.PushStyleColor(ImGuiCol.TableRowBg, new Vector4(0f, 0f, 0f, 0f));
		ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, new Vector4(_currentTheme.HeaderBg.X, _currentTheme.HeaderBg.Y, _currentTheme.HeaderBg.Z, 0.6f));
		ImGui.PushStyleColor(ImGuiCol.PopupBg, _currentTheme.PanelBg);
		ImGui.PushStyleColor(ImGuiCol.ModalWindowDimBg, new Vector4(_currentTheme.PanelBg.X, _currentTheme.PanelBg.Y, _currentTheme.PanelBg.Z, 0.5f));
		ImGui.PushStyleColor(ImGuiCol.TextSelectedBg, _currentTheme.BtnActive);
		ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 12f * ImGuiHelpers.GlobalScale);
		ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 10f * ImGuiHelpers.GlobalScale);
		ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6f * ImGuiHelpers.GlobalScale);
		ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 8f * ImGuiHelpers.GlobalScale);
		ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 6f * ImGuiHelpers.GlobalScale);
		ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 6f * ImGuiHelpers.GlobalScale);
		ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, 6f * ImGuiHelpers.GlobalScale);
		return new ThemeScope(44, 7);
	}

	private void LoadSavedTheme()
	{
		MareConfig config = _configService.Current;
		if (config?.Theme != null)
		{
			bool foundPreset = false;
			foreach (KeyValuePair<string, ThemePalette> preset in _predefinedThemes)
			{
				if (preset.Value.Equals(config.Theme))
				{
					_currentTheme = preset.Value.Clone();
					_currentThemeName = preset.Key;
					_isCustomTheme = false;
					foundPreset = true;
					break;
				}
			}
			if (!foundPreset)
			{
				_currentTheme = config.Theme.Clone();
				_currentThemeName = "Custom";
				_isCustomTheme = true;
				_savedCustomTheme = config.Theme.Clone();
			}
		}
		else
		{
			if (_predefinedThemes.TryGetValue("Blue", out ThemePalette defaultTheme))
			{
				_currentTheme = defaultTheme.Clone();
				_currentThemeName = "Blue";
			}
			else
			{
				_currentTheme = new ThemePalette();
				_currentThemeName = "Default";
			}
			_isCustomTheme = false;
		}
	}

	private void SaveThemeSettings()
	{
		_configService.Current.Theme = _currentTheme.Clone();
		_configService.Save();
	}

	private static Dictionary<string, ThemePalette> CreatePredefinedThemes()
	{
		Dictionary<string, ThemePalette> themes = new Dictionary<string, ThemePalette>();
		foreach (KeyValuePair<string, ThemePalette> preset in ThemePresets.Presets)
		{
			themes[preset.Key] = preset.Value.Clone();
		}
		return themes;
	}
}
