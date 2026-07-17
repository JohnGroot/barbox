using Godot;

namespace BarBox.Core.Drawing;

/// <summary>
/// The single source of color truth for the OP-1 visual language. No new `new Color(...)`
/// literals belong outside this file.
///
/// The arrays below are static readonly *references* — the contents are still writable.
/// Treat them as immutable: styles hold the same array instance, so mutating one bypasses
/// every consumer's dirty tracking.
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

	/// <summary>The drivable track surface — rendered as a fat stroke, not a fill.</summary>
	public static readonly Color Asphalt = new("#17171b");

	public static readonly Color TrackEdge = Blue;
	public static readonly Color Danger = Red;
	public static readonly Color Success = Green;
	public static readonly Color HudNeedle = Orange;
	public static readonly Color Info = White;
	public static readonly Color Warning = Yellow;

	public static readonly Color[] PlayerColors = [Blue, Orange, Green, Purple];

	/// <summary>Speedometer arc ramp, interpolated in OKLab so the midrange stays saturated.</summary>
	public static readonly ColorStop[] SpeedGradient =
	[
		new(0f, Cyan),
		new(0.5f, Green),
		new(0.8f, Yellow),
		new(1f, Red),
	];
}
