using Godot;

namespace BarBox.Core.Drawing;

/// <summary>
/// The base OP-1 swatches shared across the whole app — example colors every game or UI surface
/// draws from.
///
/// A color meaningful to only one game (a HUD tint, a track surface, a gradient) does not belong
/// here — it belongs in that game's own `<Game>Palette.cs` (e.g. `RacingPalette` in
/// `_Games/RacingGame/Scripts/Visual/`), built from these base swatches where possible rather than
/// new literals. See BarBoxApp/CLAUDE.md's Colors rule for the full boundary.
/// </summary>
public static class Palette
{
	public static readonly Color Ink = new("#0b0b0d");
	public static readonly Color Panel = new("#151519");
	public static readonly Color PanelRaised = new("#1f1f25");

	public static readonly Color Grid = new("#2e2e36");
	public static readonly Color EdgeGray = new("#5a5a66");
	public static readonly Color White = new("#f2f2f2");

	public static readonly Color Blue = new("#3b82f6");
	public static readonly Color Orange = new("#f97316");
	public static readonly Color Green = new("#22c55e");
	public static readonly Color Red = new("#ef4444");
	public static readonly Color Yellow = new("#eab308");
	public static readonly Color Cyan = new("#06b6d4");
	public static readonly Color Purple = new("#a855f7");

	public static readonly Color Danger = Red;
	public static readonly Color Success = Green;
	public static readonly Color HudNeedle = Orange;
	public static readonly Color Info = White;
	public static readonly Color Warning = Yellow;

	/// <summary>TopMenuBar.ApplyStandardButtonStyle's grays — the legacy StyleBoxFlat button look kept alive for BuyCreditsModal/CarromPlayerSetupMenu until M12 retires the helper.</summary>
	public static readonly Color ButtonBgNormal = new(0.3f, 0.3f, 0.3f, 1f);
	public static readonly Color ButtonBorderNormal = new(0.6f, 0.6f, 0.6f, 1f);
	public static readonly Color ButtonBgHover = new(0.4f, 0.4f, 0.4f, 1f);
	public static readonly Color ButtonBorderHover = new(0.8f, 0.8f, 0.8f, 1f);
	public static readonly Color ButtonBgPressed = new(0.2f, 0.2f, 0.2f, 1f);
	public static readonly Color ButtonBorderPressed = ButtonBorderHover;
}
