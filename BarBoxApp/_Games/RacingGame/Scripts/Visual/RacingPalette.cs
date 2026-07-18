using System;
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

	/// <summary>Countdown overlay scrim — the lightest of the three, since the countdown is brief and non-blocking.</summary>
	public static readonly Color ScrimSubtle = new(0f, 0f, 0f, 0.5f);

	/// <summary>Pause and tracks/leaderboard overlay scrim.</summary>
	public static readonly Color ScrimLight = new(0f, 0f, 0f, 0.7f);

	/// <summary>Race-complete overlay scrim — the heaviest, since it's a full-attention modal.</summary>
	public static readonly Color ScrimHeavy = new(0f, 0f, 0f, 0.8f);

	/// <summary>Digital timer label's outline, for contrast against the track behind it.</summary>
	public static readonly Color TextOutline = new(0f, 0f, 0f, 0.8f);

	/// <summary>Shared DataTableView styling for the race-complete and tracks/leaderboard tables.</summary>
	public static readonly Color TableHeaderBg = new(0.2f, 0.2f, 0.3f, 1.0f);

	public static readonly Color TableRowBg1 = new(0.1f, 0.1f, 0.15f, 0.8f);

	public static readonly Color TableRowBg2 = new(0.15f, 0.15f, 0.2f, 0.8f);

	public static readonly Color TableHighlight = new(0.9f, 0.7f, 0f, 0.6f);

	/// <summary>Track-list button accents: the currently loaded track vs. one with recorded scores.</summary>
	public static readonly Color CurrentTrackAccent = Palette.Yellow;

	public static readonly Color HasScoresAccent = Palette.Cyan;

	/// <summary>Race-complete overlay's decorative isometric wireframe, low-opacity behind the panel.</summary>
	public static readonly Color FlourishAccent = new(Palette.Purple, 0.35f);

	/// <summary>RacingZone's fallback tint before a factory method assigns a type-specific one.</summary>
	public static readonly Color ZoneDefault = new(1f, 1f, 0f, 0.3f);

	public static readonly Color ZoneSlowdown = new(1f, 0.2f, 0.2f, 0.3f);

	public static readonly Color ZoneBoost = new(0.2f, 0.4f, 1f, 0.3f);

	public static readonly Color ZoneFrictionless = new(0.2f, 1f, 1f, 0.3f);

	/// <summary>Unused today — RacingZone.CreateKerbZone() has no callers (kerbs render via StripedStroke instead), kept for parity with the other zone factories.</summary>
	public static readonly Color ZoneKerb = new(1f, 0.3f, 0.3f, 0.5f);

	/// <summary>
	/// A stable per-player color for leaderboard rows — usernames aren't tied to a car-color slot
	/// (unlike the 1-4 concurrent players PlayerColors otherwise indexes), so the same username
	/// always lands on the same color rather than drifting with rank position.
	/// </summary>
	public static Color ColorForUsername(string username)
	{
		if (string.IsNullOrEmpty(username))
		{
			return PlayerColors[0];
		}

		int index = Math.Abs(username.GetHashCode()) % PlayerColors.Length;
		return PlayerColors[index];
	}
}
