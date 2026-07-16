using Godot;

namespace BarBox.Games.Carrom;

/// <summary>
/// Carrom's notification taxonomy for game events
/// </summary>
public enum NotificationType
{
	TurnStart,        // Sticky until striker hit
	PiecePocketed,    // Timed 2s
	Foul,             // Timed 3s
	QueenPocketed,    // Timed 3s
	QueenCovered,     // Timed 3s
	QueenReturned,    // Timed 2s
	BreakingAttempt,  // Timed 2s
	BreakingSuccess,  // Timed 2s
	BreakingFailed, // Timed 3s
}

/// <summary>
/// Maps Carrom's notification types onto NotificationService's generic
/// color/sticky/duration parameters - the type taxonomy is game-specific,
/// the stacking/fade/dismiss machinery it renders through is not.
/// </summary>
public static class CarromNotificationStyle
{
	private static readonly Color COLOR_TURN_START = new Color(0.2f, 0.9f, 0.8f, 1.0f); // Cyan
	private static readonly Color COLOR_GOOD = new Color(0.2f, 0.9f, 0.3f, 1.0f);        // Green
	private static readonly Color COLOR_FOUL = new Color(0.9f, 0.3f, 0.2f, 1.0f);        // Red
	private static readonly Color COLOR_QUEEN = new Color(1.0f, 0.84f, 0.0f, 1.0f);      // Gold
	private static readonly Color COLOR_BREAKING = new Color(0.6f, 0.4f, 0.9f, 1.0f);    // Purple

	public static (Color Color, bool Sticky, float Duration) For(NotificationType type) => type switch
	{
		NotificationType.TurnStart => (COLOR_TURN_START, true, 0f),
		NotificationType.PiecePocketed => (COLOR_GOOD, false, 2.0f),
		NotificationType.Foul => (COLOR_FOUL, false, 3.0f),
		NotificationType.QueenPocketed => (COLOR_QUEEN, false, 3.0f),
		NotificationType.QueenCovered => (COLOR_QUEEN, false, 3.0f),
		NotificationType.QueenReturned => (COLOR_QUEEN, false, 2.0f),
		NotificationType.BreakingAttempt => (COLOR_BREAKING, false, 2.0f),
		NotificationType.BreakingSuccess => (COLOR_BREAKING, false, 2.0f),
		NotificationType.BreakingFailed => (COLOR_BREAKING, false, 3.0f),
		_ => (Colors.White, false, 2.0f),
	};
}
