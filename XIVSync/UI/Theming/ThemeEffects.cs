using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;

namespace XIVSync.UI.Theming;

public static class ThemeEffects
{
	public static Vector4 GetAnimatedAccentColor(ThemePalette theme, float animationPhase, float pulseIntensity = 0.15f)
	{
		Vector4 baseColor = theme.Accent;
		float pulse = 0.85f + MathF.Sin(animationPhase * (float)Math.PI * 2f) * pulseIntensity;
		return new Vector4(baseColor.X * pulse, baseColor.Y * pulse, baseColor.Z * pulse, baseColor.W);
	}

	public static bool DrawIconButtonWithGlow(FontAwesomeIcon icon, Vector2 buttonSize, ThemePalette theme, float animationPhase, float glowIntensityMultiplier = 0.7f, float glowSizeScale = 4f)
	{
		Vector2 cursorPos = ImGui.GetCursorScreenPos();
		bool result = ImGui.Button(icon.ToIconString(), buttonSize);
		if (ImGui.IsItemHovered())
		{
			Vector4 rgbColor = GetAnimatedAccentColor(theme, animationPhase);
			float glowPulse = glowIntensityMultiplier + MathF.Sin(animationPhase * 0.5f) * 0.3f;
			Vector4 glowColor = new Vector4(rgbColor.X * glowPulse, rgbColor.Y * glowPulse, rgbColor.Z * glowPulse, 0.25f);
			ImDrawListPtr drawList = ImGui.GetWindowDrawList();
			float glowSize = glowSizeScale * ImGuiHelpers.GlobalScale;
			drawList.AddRectFilled(new Vector2(cursorPos.X - glowSize, cursorPos.Y - glowSize), new Vector2(cursorPos.X + buttonSize.X + glowSize, cursorPos.Y + buttonSize.Y + glowSize), ImGui.ColorConvertFloat4ToU32(glowColor), 6f * ImGuiHelpers.GlobalScale);
		}
		return result;
	}

	public static void DrawHoverGlow(Vector2 elementMin, Vector2 elementMax, ThemePalette theme, float animationPhase, float glowAlpha = 0.2f, float cornerRadius = 4f)
	{
		Vector4 rgbColor = GetAnimatedAccentColor(theme, animationPhase, 0.2f);
		Vector4 hoverGlowColor = new Vector4(rgbColor.X * 0.8f, rgbColor.Y * 0.8f, rgbColor.Z * 0.8f, glowAlpha);
		ImGui.GetWindowDrawList().AddRectFilled(new Vector2(elementMin.X - 2f, elementMin.Y - 2f), new Vector2(elementMax.X + 2f, elementMax.Y + 2f), ImGui.ColorConvertFloat4ToU32(hoverGlowColor), cornerRadius);
	}

	public static void DrawPulsingBorder(Vector2 elementMin, Vector2 elementMax, ThemePalette theme, float animationPhase, float pulseSpeed = 1.5f, float pulseIntensity = 0.7f, float pulseVariation = 0.3f, float borderThickness = 2.5f, float cornerRadius = 3f)
	{
		Vector4 rgbColor = GetAnimatedAccentColor(theme, animationPhase);
		float pulse = pulseIntensity + MathF.Sin(animationPhase * pulseSpeed) * pulseVariation;
		Vector4 borderColor = new Vector4(rgbColor.X * pulse, rgbColor.Y * pulse, rgbColor.Z * pulse, 0.9f);
		ImGui.GetWindowDrawList().AddRect(elementMin, elementMax, ImGui.ColorConvertFloat4ToU32(borderColor), cornerRadius, ImDrawFlags.None, borderThickness);
	}

	public static void DrawBackgroundGlow(Vector2 elementMin, Vector2 elementMax, ThemePalette theme, float animationPhase, float innerAlpha = 0.4f, float outerAlpha = 0.3f, float cornerRadius = 4f)
	{
		Vector4 rgbColor = GetAnimatedAccentColor(theme, animationPhase);
		float pulseIntensity = 0.6f + MathF.Sin(animationPhase * 1.5f) * 0.3f;
		ImDrawListPtr drawList = ImGui.GetWindowDrawList();
		drawList.AddRectFilled(col: ImGui.ColorConvertFloat4ToU32(new Vector4(rgbColor.X * pulseIntensity, rgbColor.Y * pulseIntensity, rgbColor.Z * pulseIntensity, outerAlpha * pulseIntensity)), pMin: new Vector2(elementMin.X - 2f, elementMin.Y - 2f), pMax: new Vector2(elementMax.X + 2f, elementMax.Y + 2f), rounding: cornerRadius);
		drawList.AddRectFilled(col: ImGui.ColorConvertFloat4ToU32(new Vector4(rgbColor.X * 0.3f, rgbColor.Y * 0.3f, rgbColor.Z * 0.3f, innerAlpha)), pMin: elementMin, pMax: elementMax, rounding: cornerRadius - 1f);
	}

	public static void DrawAccentLine(Vector2 start, Vector2 end, ThemePalette theme, float animationPhase, float thickness = 3f)
	{
		Vector4 rgbColor = GetAnimatedAccentColor(theme, animationPhase);
		float pulseIntensity = 0.7f + MathF.Sin(animationPhase * 1.5f) * 0.3f;
		Vector4 lineColor = new Vector4(rgbColor.X * pulseIntensity, rgbColor.Y * pulseIntensity, rgbColor.Z * pulseIntensity, 0.9f);
		ImGui.GetWindowDrawList().AddLine(start, end, ImGui.ColorConvertFloat4ToU32(lineColor), thickness);
	}
}
