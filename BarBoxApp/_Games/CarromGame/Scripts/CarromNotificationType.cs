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
	public static (Color Color, bool Sticky, float Duration) For(NotificationType type) => type switch
	{
		NotificationType.TurnStart => (CarromPalette.NotificationTurnStart, true, 0f),
		NotificationType.PiecePocketed => (CarromPalette.NotificationGood, false, 2.0f),
		NotificationType.Foul => (CarromPalette.NotificationFoul, false, 3.0f),
		NotificationType.QueenPocketed => (CarromPalette.NotificationQueen, false, 3.0f),
		NotificationType.QueenCovered => (CarromPalette.NotificationQueen, false, 3.0f),
		NotificationType.QueenReturned => (CarromPalette.NotificationQueen, false, 2.0f),
		NotificationType.BreakingAttempt => (CarromPalette.NotificationBreaking, false, 2.0f),
		NotificationType.BreakingSuccess => (CarromPalette.NotificationBreaking, false, 2.0f),
		NotificationType.BreakingFailed => (CarromPalette.NotificationBreaking, false, 3.0f),
		_ => (Colors.White, false, 2.0f),
	};
}
