using System.Numerics;
using System.Text.Json.Serialization;

namespace XIVSync.UI;

public sealed class ThemePalette
{
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
}
