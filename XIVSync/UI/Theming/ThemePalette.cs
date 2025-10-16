using System;
using System.Numerics;
using System.Text.Json.Serialization;

namespace XIVSync.UI.Theming;

public sealed class ThemePalette
{
	public static class SemanticHues
	{
		public const float Red = 0f;

		public const float Yellow = 0.12f;

		public const float Green = 0.33f;
	}

	[JsonConverter(typeof(Vector4JsonConverter))]
	public Vector4 PanelBg { get; set; } = new Vector4(0.07f, 0.08f, 0.12f, 0.8f);


	[JsonConverter(typeof(Vector4JsonConverter))]
	public Vector4 PanelBorder { get; set; } = new Vector4(0.25f, 0.45f, 0.95f, 1f);


	[JsonConverter(typeof(Vector4JsonConverter))]
	public Vector4 HeaderBg { get; set; } = new Vector4(0.12f, 0.2f, 0.36f, 1f);


	[JsonConverter(typeof(Vector4JsonConverter))]
	public Vector4 Accent { get; set; } = new Vector4(0.25f, 0.55f, 0.95f, 1f);


	[JsonConverter(typeof(Vector4JsonConverter))]
	public Vector4 TextPrimary { get; set; } = new Vector4(0.85f, 0.9f, 1f, 1f);


	[JsonConverter(typeof(Vector4JsonConverter))]
	public Vector4 TextSecondary { get; set; } = new Vector4(0.7f, 0.75f, 0.85f, 1f);


	[JsonConverter(typeof(Vector4JsonConverter))]
	public Vector4 TextDisabled { get; set; } = new Vector4(0.5f, 0.55f, 0.65f, 1f);


	[JsonConverter(typeof(Vector4JsonConverter))]
	public Vector4 Link { get; set; } = new Vector4(0.3f, 0.7f, 1f, 1f);


	[JsonConverter(typeof(Vector4JsonConverter))]
	public Vector4 LinkHover { get; set; } = new Vector4(0.45f, 0.82f, 1f, 1f);


	[JsonConverter(typeof(Vector4JsonConverter))]
	public Vector4 Btn { get; set; } = new Vector4(0.15f, 0.18f, 0.25f, 1f);


	[JsonConverter(typeof(Vector4JsonConverter))]
	public Vector4 BtnHovered { get; set; } = new Vector4(0.25f, 0.45f, 0.95f, 1f);


	[JsonConverter(typeof(Vector4JsonConverter))]
	public Vector4 BtnActive { get; set; } = new Vector4(0.2f, 0.35f, 0.75f, 1f);


	[JsonConverter(typeof(Vector4JsonConverter))]
	public Vector4 BtnText { get; set; } = new Vector4(0.85f, 0.9f, 1f, 1f);


	[JsonConverter(typeof(Vector4JsonConverter))]
	public Vector4 BtnTextHovered { get; set; } = new Vector4(1f, 1f, 1f, 1f);


	[JsonConverter(typeof(Vector4JsonConverter))]
	public Vector4 BtnTextActive { get; set; } = new Vector4(0.95f, 0.98f, 1f, 1f);


	[JsonConverter(typeof(Vector4JsonConverter))]
	public Vector4 TooltipBg { get; set; } = new Vector4(0.05f, 0.06f, 0.1f, 0.95f);


	[JsonConverter(typeof(Vector4JsonConverter))]
	public Vector4 TooltipText { get; set; } = new Vector4(0.9f, 0.95f, 1f, 1f);


	[JsonConverter(typeof(Vector4JsonConverter))]
	public Vector4 FolderVisible { get; set; } = new Vector4(0f, 1f, 0.5f, 1f);


	[JsonConverter(typeof(Vector4JsonConverter))]
	public Vector4 FolderOnline { get; set; } = new Vector4(0f, 0.9f, 1f, 1f);


	[JsonConverter(typeof(Vector4JsonConverter))]
	public Vector4 FolderOffline { get; set; } = new Vector4(0.9f, 0.2f, 0.2f, 1f);


	[JsonConverter(typeof(Vector4JsonConverter))]
	public Vector4 FolderOfflineSyncshell { get; set; } = new Vector4(0.9f, 0.2f, 0.2f, 1f);


	[JsonConverter(typeof(Vector4JsonConverter))]
	public Vector4 FolderUnpaired { get; set; } = new Vector4(1f, 0.5f, 0f, 1f);


	[JsonConverter(typeof(Vector4JsonConverter))]
	public Vector4 FolderAll { get; set; } = new Vector4(0.6f, 0.4f, 1f, 1f);


	[JsonConverter(typeof(Vector4JsonConverter))]
	public Vector4 FolderCustom { get; set; } = new Vector4(1f, 0f, 0.8f, 1f);


	public ThemePalette()
	{
	}

	public ThemePalette(ThemePalette source)
	{
		PanelBg = source.PanelBg;
		PanelBorder = source.PanelBorder;
		HeaderBg = source.HeaderBg;
		Accent = source.Accent;
		TextPrimary = source.TextPrimary;
		TextSecondary = source.TextSecondary;
		TextDisabled = source.TextDisabled;
		Link = source.Link;
		LinkHover = source.LinkHover;
		Btn = source.Btn;
		BtnHovered = source.BtnHovered;
		BtnActive = source.BtnActive;
		BtnText = source.BtnText;
		BtnTextHovered = source.BtnTextHovered;
		BtnTextActive = source.BtnTextActive;
		TooltipBg = source.TooltipBg;
		TooltipText = source.TooltipText;
		FolderVisible = source.FolderVisible;
		FolderOnline = source.FolderOnline;
		FolderOffline = source.FolderOffline;
		FolderOfflineSyncshell = source.FolderOfflineSyncshell;
		FolderUnpaired = source.FolderUnpaired;
		FolderAll = source.FolderAll;
		FolderCustom = source.FolderCustom;
	}

	public static Vector4 GetDarkerColor(Vector4 color, float factor = 0.7f)
	{
		return new Vector4(color.X * factor, color.Y * factor, color.Z * factor, color.W);
	}

	public static Vector4 GetLighterColor(Vector4 color, float factor = 1.3f)
	{
		return new Vector4(Math.Min(color.X * factor, 1f), Math.Min(color.Y * factor, 1f), Math.Min(color.Z * factor, 1f), color.W);
	}

	public static void RgbToHsv(float r, float g, float b, out float h, out float s, out float v)
	{
		float max = MathF.Max(r, MathF.Max(g, b));
		float min = MathF.Min(r, MathF.Min(g, b));
		v = max;
		float d = max - min;
		s = ((max <= 0f) ? 0f : (d / max));
		if (d <= 0f)
		{
			h = 0f;
			return;
		}
		if (max == r)
		{
			h = (g - b) / d % 6f;
		}
		else if (max == g)
		{
			h = (b - r) / d + 2f;
		}
		else
		{
			h = (r - g) / d + 4f;
		}
		h /= 6f;
		if (h < 0f)
		{
			h += 1f;
		}
	}

	public static void HsvToRgb(float h, float s, float v, out float r, out float g, out float b)
	{
		h = (h % 1f + 1f) % 1f;
		float c = v * s;
		float x = c * (1f - MathF.Abs(h * 6f % 2f - 1f));
		float m = v - c;
		float r1 = 0f;
		float g1 = 0f;
		float b1 = 0f;
		float seg = h * 6f;
		if (seg < 1f)
		{
			r1 = c;
			g1 = x;
			b1 = 0f;
		}
		else if (seg < 2f)
		{
			r1 = x;
			g1 = c;
			b1 = 0f;
		}
		else if (seg < 3f)
		{
			r1 = 0f;
			g1 = c;
			b1 = x;
		}
		else if (seg < 4f)
		{
			r1 = 0f;
			g1 = x;
			b1 = c;
		}
		else if (seg < 5f)
		{
			r1 = x;
			g1 = 0f;
			b1 = c;
		}
		else
		{
			r1 = c;
			g1 = 0f;
			b1 = x;
		}
		r = r1 + m;
		g = g1 + m;
		b = b1 + m;
	}

	public static Vector4 GetSemanticColorFromAccent(ThemePalette theme, float hue)
	{
		RgbToHsv(theme.Accent.X, theme.Accent.Y, theme.Accent.Z, out var _, out var s, out var v);
		s = MathF.Max(s, 0.5f);
		v = MathF.Max(v, 0.85f);
		HsvToRgb(hue, s, v, out var r, out var g, out var b);
		return new Vector4(r, g, b, theme.Accent.W);
	}

	public bool Equals(ThemePalette? other)
	{
		if (other == null)
		{
			return false;
		}
		if (PanelBg.Equals(other.PanelBg) && PanelBorder.Equals(other.PanelBorder) && HeaderBg.Equals(other.HeaderBg) && Accent.Equals(other.Accent) && TextPrimary.Equals(other.TextPrimary) && TextSecondary.Equals(other.TextSecondary) && TextDisabled.Equals(other.TextDisabled) && Link.Equals(other.Link) && LinkHover.Equals(other.LinkHover) && Btn.Equals(other.Btn) && BtnHovered.Equals(other.BtnHovered) && BtnActive.Equals(other.BtnActive) && BtnText.Equals(other.BtnText) && BtnTextHovered.Equals(other.BtnTextHovered) && BtnTextActive.Equals(other.BtnTextActive) && TooltipBg.Equals(other.TooltipBg) && TooltipText.Equals(other.TooltipText) && FolderVisible.Equals(other.FolderVisible) && FolderOnline.Equals(other.FolderOnline) && FolderOffline.Equals(other.FolderOffline) && FolderOfflineSyncshell.Equals(other.FolderOfflineSyncshell) && FolderUnpaired.Equals(other.FolderUnpaired) && FolderAll.Equals(other.FolderAll))
		{
			return FolderCustom.Equals(other.FolderCustom);
		}
		return false;
	}

	public ThemePalette Clone()
	{
		return new ThemePalette(this);
	}
}
