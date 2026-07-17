using Godot;

namespace BarBox.Core.Drawing;

/// <summary>
/// Control-side StyleBoxFlat generators for the OP-1 bordered-button chrome. Distinct from
/// VectorStyles.ButtonOutline, which is a Shape/vector stroke preset for the ShapeCanvas
/// pipeline (Node2D world) — this targets vanilla Godot Controls via theme overrides. The
/// width-2 coincidence between the two is not a shared contract. Font stays InterTheme.tres;
/// only sizes are standardized here.
/// </summary>
public static class UiTheme
{
	public const int FontSmall = 14;
	public const int FontBody = 16;
	public const int FontTitle = 24;
	public const int FontDigits = 32;

	private const int OutlineRadius = 10;
	private const int OutlineBorderWidth = 2;
	private const float HoverFillAlpha = 0.15f;
	private const float DisabledBorderAlpha = 0.35f;

	public readonly record struct OutlineButtonBoxes(StyleBoxFlat Normal, StyleBoxFlat Hover, StyleBoxFlat Pressed, StyleBoxFlat Disabled);

	public static OutlineButtonBoxes OutlineButton(Color accent)
	{
		var normal = BaseBox(accent);
		normal.BgColor = Colors.Transparent;

		var hover = BaseBox(accent);
		hover.BgColor = new Color(accent, HoverFillAlpha);

		var pressed = BaseBox(accent);
		pressed.BgColor = accent;

		var disabled = BaseBox(accent);
		disabled.BgColor = Colors.Transparent;
		disabled.BorderColor = new Color(accent, DisabledBorderAlpha);

		return new OutlineButtonBoxes(normal, hover, pressed, disabled);
	}

	/// <summary>Generic dark panel — flat background, hairline border, no accent.</summary>
	public static StyleBoxFlat PanelBox()
	{
		return new StyleBoxFlat
		{
			BgColor = Palette.Panel,
			BorderColor = Palette.Grid,
			BorderWidthLeft = 1,
			BorderWidthRight = 1,
			BorderWidthTop = 1,
			BorderWidthBottom = 1,
			CornerRadiusTopLeft = 8,
			CornerRadiusTopRight = 8,
			CornerRadiusBottomLeft = 8,
			CornerRadiusBottomRight = 8,
		};
	}

	/// <summary>Elevated modal surface. Border is mutable on the returned resource for callers that need an accent.</summary>
	public static StyleBoxFlat ModalBox()
	{
		return new StyleBoxFlat
		{
			BgColor = Palette.PanelRaised,
			BorderColor = Palette.Grid,
			BorderWidthLeft = 2,
			BorderWidthRight = 2,
			BorderWidthTop = 2,
			BorderWidthBottom = 2,
			CornerRadiusTopLeft = 12,
			CornerRadiusTopRight = 12,
			CornerRadiusBottomLeft = 12,
			CornerRadiusBottomRight = 12,
		};
	}

	public static void ApplyOutlineButton(Button button, Color accent)
	{
		var boxes = OutlineButton(accent);
		button.AddThemeStyleboxOverride("normal", boxes.Normal);
		button.AddThemeStyleboxOverride("hover", boxes.Hover);
		button.AddThemeStyleboxOverride("pressed", boxes.Pressed);
		button.AddThemeStyleboxOverride("disabled", boxes.Disabled);

		button.AddThemeColorOverride("font_color", accent);
		button.AddThemeColorOverride("font_hover_color", accent);
		button.AddThemeColorOverride("font_pressed_color", Palette.Ink);
		button.AddThemeColorOverride("font_disabled_color", Palette.EdgeGray);
	}

	private static StyleBoxFlat BaseBox(Color accent)
	{
		return new StyleBoxFlat
		{
			BorderColor = accent,
			BorderWidthLeft = OutlineBorderWidth,
			BorderWidthRight = OutlineBorderWidth,
			BorderWidthTop = OutlineBorderWidth,
			BorderWidthBottom = OutlineBorderWidth,
			CornerRadiusTopLeft = OutlineRadius,
			CornerRadiusTopRight = OutlineRadius,
			CornerRadiusBottomLeft = OutlineRadius,
			CornerRadiusBottomRight = OutlineRadius,
		};
	}
}
