using Godot;

namespace BarBox.Games.Nines;

/// <summary>
/// Unified configuration for Nines card game.
/// Combines game mechanics, credit settings, animation timing, and visual settings.
/// </summary>
[GlobalClass]
public partial class NinesGameConfig : Resource
{
	#region Game Mechanics

	[ExportCategory("Game Mechanics")]

	[Export]
	public int GridSize { get; set; } = 3;

	[Export]
	public int MaxPlayers { get; set; } = 8;

	[Export]
	public int InitialCardCount { get; set; } = 9;

	#endregion

	#region Credit/Jackpot Settings

	[ExportCategory("Credits")]

	/// <summary>
	/// Credits charged per player to play a game
	/// </summary>
	[Export(PropertyHint.Range, "1,1000")]
	public int EntryCost { get; set; } = 100;

	/// <summary>
	/// Base jackpot value after being won (starting value)
	/// Formula: Jackpot = BaseJackpotValue + (DaysSinceLastWin * DailyJackpotGrowth)
	/// </summary>
	[Export]
	public int BaseJackpotValue { get; set; } = 5000;

	/// <summary>
	/// Credits added to jackpot per day since last win
	/// </summary>
	[Export]
	public int DailyJackpotGrowth { get; set; } = 1000;

	#endregion

	#region Animation Timing

	[ExportCategory("Animation")]

	[Export(PropertyHint.Range, "0.1,1.0,0.05")]
	public float DealCardDuration { get; set; } = 0.3f;

	[Export(PropertyHint.Range, "0.1,1.0,0.05")]
	public float CardFlipDuration { get; set; } = 0.25f;

	[Export(PropertyHint.Range, "0.1,1.0,0.05")]
	public float CardPlaceDuration { get; set; } = 0.3f;

	[Export(PropertyHint.Range, "0.1,2.0,0.1")]
	public float RevealDelay { get; set; } = 0.5f;

	[Export(PropertyHint.Range, "0.05,0.5,0.05")]
	public float DealDelayBetweenCards { get; set; } = 0.15f;

	[Export(PropertyHint.Range, "0.5,3.0,0.1")]
	public float ResultFeedbackDuration { get; set; } = 1.5f;

	#endregion

	#region Visual Settings

	[ExportCategory("Visual")]

	[Export]
	public Vector2 CardSize { get; set; } = new(80, 112);

	[Export]
	public float GridSpacing { get; set; } = 20f;

	[Export]
	public float CardCornerRadius { get; set; } = 6f;

	[Export]
	public float CardBorderWidth { get; set; } = 2f;

	[Export]
	public Color CardFaceColor { get; set; } = Colors.White;

	[Export]
	public Color CardBackColor { get; set; } = new(0.2f, 0.3f, 0.6f);

	[Export]
	public Color CardBorderColor { get; set; } = new(0.1f, 0.1f, 0.1f);

	[Export]
	public Color RedSuitColor { get; set; } = new(0.8f, 0.1f, 0.1f);

	[Export]
	public Color BlackSuitColor { get; set; } = new(0.1f, 0.1f, 0.1f);

	[Export]
	public Color SelectedHighlightColor { get; set; } = new(1.0f, 0.8f, 0.0f, 0.8f);

	#endregion

	#region UI Theme

	[ExportCategory("UI Theme")]

	[Export]
	public Color BackgroundColor { get; set; } = new(0.15f, 0.4f, 0.25f);

	[Export]
	public Color PanelColor { get; set; } = new(0.1f, 0.1f, 0.15f, 0.9f);

	[Export]
	public Color ButtonNormalColor { get; set; } = new(0.2f, 0.5f, 0.3f);

	[Export]
	public Color ButtonHoverColor { get; set; } = new(0.3f, 0.6f, 0.4f);

	[Export]
	public Color ButtonDisabledColor { get; set; } = new(0.3f, 0.3f, 0.3f);

	[Export]
	public Color CorrectFeedbackColor { get; set; } = new(0.2f, 0.8f, 0.2f);

	[Export]
	public Color WrongFeedbackColor { get; set; } = new(0.8f, 0.2f, 0.2f);

	[Export]
	public Color JackpotDisplayColor { get; set; } = new(1.0f, 0.85f, 0.0f);

	#endregion

	#region Computed Properties

	public Vector2 GridTotalSize => new(
		(GridSize * CardSize.X) + ((GridSize - 1) * GridSpacing),
		(GridSize * CardSize.Y) + ((GridSize - 1) * GridSpacing));

	public int TotalCardsInDeck => 52;

	public int CardsRemainingAfterDeal => TotalCardsInDeck - InitialCardCount;

	#endregion

	#region UI Scaling

	private const float REFERENCE_HEIGHT = 1080f;

	public float GetScaleFactor(Vector2 viewportSize)
	{
		// Scale based on viewport height relative to 1080p reference
		float scale = viewportSize.Y / REFERENCE_HEIGHT;

		// Clamp to reasonable bounds
		return Mathf.Clamp(scale, 0.5f, 2.5f);
	}

	public Vector2 GetScaledCardSize(float scaleFactor) => CardSize * scaleFactor;

	public float GetScaledGridSpacing(float scaleFactor) => GridSpacing * scaleFactor;

	public int GetScaledFontSize(int baseSize, float scaleFactor) => (int)(baseSize * scaleFactor);

	public float GetScaledCornerRadius(float scaleFactor) => CardCornerRadius * scaleFactor;

	public float GetScaledBorderWidth(float scaleFactor) => CardBorderWidth * scaleFactor;

	#endregion
}
