using BarBox.Core.Drawing;
using Godot;

namespace BarBox.Games.Racing;

/// <summary>
/// Colors meaningful only to the racing game — see BarBox.Core.Drawing.Palette's doc comment for
/// the split. Built from Palette's base swatches where possible, rather than new literals.
///
/// The arrays below are static readonly *references* — the contents are still writable. Treat
/// them as immutable: styles hold the same array instance, so mutating one bypasses every
/// consumer's dirty tracking.
/// </summary>
public static class RacingPalette
{
	/// <summary>The drivable track surface — rendered as a fat stroke, not a fill.</summary>
	public static readonly Color Asphalt = new("#17171b");

	public static readonly Color[] PlayerColors = [Palette.Blue, Palette.Orange, Palette.Green, Palette.Purple];

	/// <summary>Speedometer arc ramp, interpolated in OKLab so the midrange stays saturated.</summary>
	public static readonly ColorStop[] SpeedGradient =
	[
		new(0f, Palette.Cyan),
		new(0.5f, Palette.Green),
		new(0.8f, Palette.Yellow),
		new(1f, Palette.Red),
	];

	/// <summary>HUD gauge background ring — the dark, low-alpha track every arc gauge sits on.</summary>
	public static readonly Color HudTrack = new("#33334d");

	/// <summary>Countdown ring's outer pulsing glow. Alpha is pre-baked since every use wants it translucent, never opaque.</summary>
	public static readonly Color CountdownGlow = new(0f, 0.8f, 1f, 0.5f);

	/// <summary>Headlight cone fill. Alpha is pre-baked since the cone always reads as a translucent glow, never opaque.</summary>
	public static readonly Color HeadlightGlow = new(1f, 1f, 0.8f, 0.27f);
}
