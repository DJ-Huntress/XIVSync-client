using System.Collections.Generic;
using System.Numerics;

namespace XIVSync.UI.Theming;

public class ModernTheme
{
	public static readonly Dictionary<string, ModernTheme> Themes = new Dictionary<string, ModernTheme>
	{
		["default"] = new ModernTheme
		{
			Name = "default",
			DisplayName = "Default Blue",
			Accent = new Vector4(0.29f, 0.424f, 0.969f, 1f),
			Accent2 = new Vector4(0.608f, 0.694f, 1f, 1f),
			BackgroundOpacity = 0.4f
		},
		["green"] = new ModernTheme
		{
			Name = "green",
			DisplayName = "Forest Green",
			Accent = new Vector4(0.212f, 0.773f, 0.416f, 1f),
			Accent2 = new Vector4(0.416f, 0.886f, 0.608f, 1f),
			BackgroundOpacity = 0.4f
		},
		["red"] = new ModernTheme
		{
			Name = "red",
			DisplayName = "Crimson Red",
			Accent = new Vector4(1f, 0.322f, 0.322f, 1f),
			Accent2 = new Vector4(1f, 0.608f, 0.608f, 1f),
			BackgroundOpacity = 0.4f
		},
		["purple"] = new ModernTheme
		{
			Name = "purple",
			DisplayName = "Royal Purple",
			Accent = new Vector4(0.69f, 0.322f, 1f, 1f),
			Accent2 = new Vector4(0.835f, 0.608f, 1f, 1f),
			BackgroundOpacity = 0.4f
		},
		["orange"] = new ModernTheme
		{
			Name = "orange",
			DisplayName = "Sunset Orange",
			Accent = new Vector4(1f, 0.42f, 0.208f, 1f),
			Accent2 = new Vector4(1f, 0.69f, 0.125f, 1f),
			BackgroundOpacity = 0.4f
		},
		["light"] = new ModernTheme
		{
			Name = "light",
			DisplayName = "Light Theme",
			Accent = new Vector4(0.239f, 0.694f, 1f, 1f),
			Accent2 = new Vector4(0.608f, 0.835f, 1f, 1f),
			TextPrimary = new Vector4(0.125f, 0.125f, 0.125f, 1f),
			TextMuted = new Vector4(0.42f, 0.42f, 0.42f, 1f),
			TextMuted2 = new Vector4(0.604f, 0.604f, 0.604f, 1f),
			BackgroundOpacity = 0.85f
		}
	};

	public float BackgroundOpacity { get; set; } = 0.4f;


	public Vector4 Surface0 { get; set; }

	public Vector4 Surface1 { get; set; }

	public Vector4 Surface2 { get; set; }

	public Vector4 Surface3 { get; set; }

	public Vector4 Border => new Vector4(1f, 1f, 1f, 0.08f);

	public Vector4 Separator => new Vector4(1f, 1f, 1f, 0.07f);

	public Vector4 TextPrimary { get; set; } = new Vector4(0.914f, 0.929f, 0.945f, 1f);


	public Vector4 TextMuted { get; set; } = new Vector4(0.604f, 0.639f, 0.678f, 1f);


	public Vector4 TextMuted2 { get; set; } = new Vector4(0.42f, 0.459f, 0.502f, 1f);


	public Vector4 Accent { get; set; } = new Vector4(0.29f, 0.424f, 0.969f, 1f);


	public Vector4 Accent2 { get; set; } = new Vector4(0.608f, 0.694f, 1f, 1f);


	public Vector4 StatusOk => new Vector4(0.212f, 0.773f, 0.416f, 1f);

	public Vector4 StatusWarn => new Vector4(1f, 0.69f, 0.125f, 1f);

	public Vector4 StatusError => new Vector4(1f, 0.322f, 0.322f, 1f);

	public Vector4 StatusPaused => new Vector4(1f, 0.42f, 0.208f, 1f);

	public Vector4 StatusInfo => new Vector4(0.239f, 0.694f, 1f, 1f);

	public float RadiusSmall => 8f;

	public float RadiusMedium => 14f;

	public float RadiusLarge => 20f;

	public float SpacingXS => 6f;

	public float SpacingS => 10f;

	public float SpacingM => 14f;

	public float SpacingL => 18f;

	public string Name { get; set; } = "Default";


	public string DisplayName { get; set; } = "Default Blue";


	public void UpdateSurfaceOpacity()
	{
		Surface0 = new Vector4(0.035f, 0.039f, 0.055f, BackgroundOpacity);
		Surface1 = new Vector4(0.071f, 0.078f, 0.11f, BackgroundOpacity);
		Surface2 = new Vector4(0.11f, 0.118f, 0.165f, BackgroundOpacity);
		Surface3 = new Vector4(0.141f, 0.149f, 0.22f, BackgroundOpacity);
	}

	public ModernTheme()
	{
		UpdateSurfaceOpacity();
	}

	public static void ApplyThemeColors(ModernTheme theme)
	{
		switch (theme.Name)
		{
		case "green":
			theme.Surface0 = new Vector4(0.012f, 0.02f, 0.024f, theme.BackgroundOpacity);
			theme.Surface1 = new Vector4(0.024f, 0.039f, 0.031f, theme.BackgroundOpacity);
			theme.Surface2 = new Vector4(0.031f, 0.063f, 0.047f, theme.BackgroundOpacity);
			theme.Surface3 = new Vector4(0.047f, 0.086f, 0.063f, theme.BackgroundOpacity);
			break;
		case "red":
			theme.Surface0 = new Vector4(0.024f, 0f, 0f, theme.BackgroundOpacity);
			theme.Surface1 = new Vector4(0.063f, 0.008f, 0.008f, theme.BackgroundOpacity);
			theme.Surface2 = new Vector4(0.094f, 0.024f, 0.024f, theme.BackgroundOpacity);
			theme.Surface3 = new Vector4(0.141f, 0.039f, 0.039f, theme.BackgroundOpacity);
			break;
		case "light":
			theme.Surface0 = new Vector4(0.984f, 0.988f, 1f, theme.BackgroundOpacity);
			theme.Surface1 = new Vector4(0.961f, 0.969f, 0.988f, theme.BackgroundOpacity);
			theme.Surface2 = new Vector4(0.933f, 0.945f, 0.965f, theme.BackgroundOpacity);
			theme.Surface3 = new Vector4(0.902f, 0.922f, 0.953f, theme.BackgroundOpacity);
			break;
		default:
			theme.UpdateSurfaceOpacity();
			break;
		}
	}
}
