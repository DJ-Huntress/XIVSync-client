using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.Extensions.Logging;
using XIVSync.MareConfiguration;
using XIVSync.MareConfiguration.Configurations;

namespace XIVSync.UI.Theming;

public class ModernThemeService
{
	private readonly ILogger<ModernThemeService> _logger;

	private readonly MareConfigService _configService;

	private ModernTheme _currentTheme;

	public ModernTheme CurrentTheme => _currentTheme;

	public event Action<ModernTheme>? ThemeChanged;

	public ModernThemeService(ILogger<ModernThemeService> logger, MareConfigService configService)
	{
		_logger = logger;
		_configService = configService;
		_currentTheme = ModernTheme.Themes["default"];
		LoadThemeSettings();
	}

	public IEnumerable<string> GetAvailableThemes()
	{
		return ModernTheme.Themes.Keys;
	}

	public Dictionary<string, string> GetThemeDisplayNames()
	{
		return GetAllAvailableThemes().ToDictionary<KeyValuePair<string, ModernTheme>, string, string>((KeyValuePair<string, ModernTheme> kvp) => kvp.Key, (KeyValuePair<string, ModernTheme> kvp) => kvp.Value.DisplayName);
	}

	public Dictionary<string, ModernTheme> GetAllAvailableThemes()
	{
		Dictionary<string, ModernTheme> allThemes = new Dictionary<string, ModernTheme>(ModernTheme.Themes);
		foreach (KeyValuePair<string, ThemePalette> legacyTheme in ThemePresets.Presets)
		{
			if (!allThemes.ContainsKey(legacyTheme.Key.ToLowerInvariant()))
			{
				ModernTheme convertedTheme = ConvertLegacyTheme(legacyTheme.Key, legacyTheme.Value);
				allThemes[legacyTheme.Key.ToLowerInvariant()] = convertedTheme;
			}
		}
		return allThemes;
	}

	private ModernTheme ConvertLegacyTheme(string name, ThemePalette legacy)
	{
		ModernTheme modern = new ModernTheme
		{
			Name = name.ToLowerInvariant(),
			DisplayName = name,
			BackgroundOpacity = legacy.PanelBg.W,
			TextPrimary = legacy.TextPrimary,
			TextMuted = legacy.TextSecondary,
			TextMuted2 = legacy.TextDisabled,
			Accent = legacy.Accent,
			Accent2 = legacy.Link
		};
		modern.Surface0 = new Vector4(legacy.PanelBg.X * 0.8f, legacy.PanelBg.Y * 0.8f, legacy.PanelBg.Z * 0.8f, modern.BackgroundOpacity);
		modern.Surface1 = new Vector4(legacy.PanelBg.X, legacy.PanelBg.Y, legacy.PanelBg.Z, modern.BackgroundOpacity);
		modern.Surface2 = new Vector4(legacy.HeaderBg.X, legacy.HeaderBg.Y, legacy.HeaderBg.Z, modern.BackgroundOpacity);
		modern.Surface3 = new Vector4(legacy.Btn.X, legacy.Btn.Y, legacy.Btn.Z, modern.BackgroundOpacity);
		return modern;
	}

	public void SetTheme(string themeName)
	{
		Dictionary<string, ModernTheme> allAvailableThemes = GetAllAvailableThemes();
		string themeKey = themeName.ToLowerInvariant();
		if (!allAvailableThemes.TryGetValue(themeKey, out ModernTheme theme))
		{
			_logger.LogWarning("Theme '{ThemeName}' not found, using default", themeName);
			theme = ModernTheme.Themes["default"];
		}
		_currentTheme = theme;
		SaveThemeSettings();
		this.ThemeChanged?.Invoke(_currentTheme);
		_logger.LogInformation("Switched to theme: {ThemeName}", theme.DisplayName);
	}

	public void SetBackgroundOpacity(float opacity)
	{
		_currentTheme.BackgroundOpacity = Math.Clamp(opacity, 0f, 1f);
		_currentTheme.UpdateSurfaceOpacity();
		SaveThemeSettings();
		this.ThemeChanged?.Invoke(_currentTheme);
	}

	private void LoadThemeSettings()
	{
		try
		{
			MareConfig config = _configService.Current;
			if (!string.IsNullOrEmpty(config.ModernThemeName) && GetAllAvailableThemes().TryGetValue(config.ModernThemeName.ToLowerInvariant(), out ModernTheme theme))
			{
				_currentTheme = theme;
			}
			if (config.ModernThemeOpacity > 0f)
			{
				_currentTheme.BackgroundOpacity = config.ModernThemeOpacity;
			}
			_logger.LogInformation("Loaded theme settings: {ThemeName}, opacity: {Opacity}", _currentTheme.DisplayName, _currentTheme.BackgroundOpacity);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to load theme settings, using defaults");
		}
	}

	private void SaveThemeSettings()
	{
		try
		{
			MareConfig current = _configService.Current;
			current.ModernThemeName = _currentTheme.Name;
			current.ModernThemeOpacity = _currentTheme.BackgroundOpacity;
			_configService.Save();
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to save theme settings");
		}
	}
}
