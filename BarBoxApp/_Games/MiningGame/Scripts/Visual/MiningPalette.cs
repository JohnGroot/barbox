using BarBox.Core.Drawing;
using Godot;

namespace BarBox.Games.MiningGame;

/// <summary>
/// Colors meaningful only to the mining game — see BarBox.Core.Drawing.Palette's doc comment for
/// the split. Built from Palette's base swatches where possible, rather than new literals.
/// </summary>
public static class MiningPalette
{
	/// <summary>The five gem hues, hand-picked for mutual distinctness rather than derived from Palette's swatches — GemTheme's Lightened/Darkened accents all key off these.</summary>
	public static readonly Color GemRuby = new(0.8f, 0.1f, 0.2f);

	public static readonly Color GemSapphire = new(0.1f, 0.3f, 0.8f);

	public static readonly Color GemEmerald = new(0.1f, 0.7f, 0.2f);

	public static readonly Color GemDiamond = new(0.9f, 0.9f, 0.95f);

	public static readonly Color GemAmethyst = new(0.6f, 0.2f, 0.8f);

	/// <summary>How much GemTheme.GetBackgroundColor dims a gem's hue to make the per-location screen backdrop.</summary>
	public const float GemBackgroundDimFactor = 0.15f;

	/// <summary>Alpha for the dimmed gem-backdrop color — near-opaque so the gem hue still tints the screen behind the UI.</summary>
	public const float GemBackgroundAlpha = 0.95f;

	/// <summary>Empty-track fill behind the mining progress bar.</summary>
	public static readonly Color ProgressTrackBg = new(0.2f, 0.2f, 0.25f);

	/// <summary>Whole-UI dim tint applied via Modulate while the player isn't logged in.</summary>
	public static readonly Color DisabledUiTint = new(0.6f, 0.6f, 0.6f, 0.8f);

	/// <summary>Per-button dim modulate for individually disabled buttons (affordability, max level, etc.), lighter than DisabledUiTint since it's not a full-screen state.</summary>
	public static readonly Color DisabledButtonModulate = new(0.7f, 0.7f, 0.7f);
}
