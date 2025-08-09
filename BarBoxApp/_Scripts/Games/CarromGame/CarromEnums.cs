using Godot;

/// <summary>
/// Types of carrom pieces in the game
/// </summary>
public enum PieceType
{
	/// <summary>Player 1's pieces</summary>
	White,
	/// <summary>Player 2's pieces</summary>
	Black,
	/// <summary>Queen piece - special scoring rules apply</summary>
	Red,
	/// <summary>Striker piece used to hit other pieces</summary>
	Striker
}

/// <summary>
/// Available game modes for Carrom
/// </summary>
public enum CarromGameMode
{
	/// <summary>Single piece practice mode for learning</summary>
	Practice,
	/// <summary>Full competitive mode with complete rule set</summary>
	Competitive
}

/// <summary>
/// Types of fouls in carrom with specific penalty rules
/// OFFICIAL CARROM RULES: Different fouls have different penalties
/// </summary>
public enum FoulType
{
	/// <summary>General foul - standard penalty</summary>
	General,
	/// <summary>Striker pocketed - return one pocketed piece</summary>
	StrikerPocketed,
	/// <summary>Opponent's piece pocketed - return opponent's piece + additional penalty</summary>
	OpponentPiecePocketed,
	/// <summary>Improper queen pocketing - return queen + penalty piece</summary>
	ImproperQueenPocketing
}