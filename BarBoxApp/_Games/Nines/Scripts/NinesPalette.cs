using BarBox.Core.Drawing;
using Godot;

namespace BarBox.Games.Nines;

/// <summary>
/// Colors meaningful only to the Nines card game — see BarBox.Core.Drawing.Palette's doc comment
/// for the split. Built from Palette's base swatches where possible, rather than new literals.
/// </summary>
public static class NinesPalette
{
	/// <summary>Main menu's "Entry: N credits per player" label text.</summary>
	public static readonly Color EntryCostText = new(0.8f, 0.8f, 0.8f);

	/// <summary>Prediction popup's HIGHER button.</summary>
	public static readonly Color PredictionHigher = new(0.2f, 0.6f, 0.2f);

	/// <summary>Prediction popup's LOWER button.</summary>
	public static readonly Color PredictionLower = new(0.6f, 0.2f, 0.2f);

	/// <summary>Prediction popup's SAME button.</summary>
	public static readonly Color PredictionSame = new(0.5f, 0.4f, 0.1f);

	/// <summary>Prediction popup's cancel/dismiss button — same value as Palette.ButtonBgHover.</summary>
	public static readonly Color PredictionCancel = Palette.ButtonBgHover;

	/// <summary>Modal overlay scrim shared by the results and confirmation popups.</summary>
	public static readonly Color Scrim = new(0f, 0f, 0f, 0.7f);

	/// <summary>Player list's "No players joined" label, dimmed via Modulate.</summary>
	public static readonly Color NoPlayersModulate = new(1f, 1f, 1f, 0.5f);
}
