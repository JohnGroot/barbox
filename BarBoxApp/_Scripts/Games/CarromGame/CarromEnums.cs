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
/// Game phase states for controlling input and game flow
/// </summary>
public enum GamePhase
{
	/// <summary>Initial setup - pieces being positioned</summary>
	Setup,
	/// <summary>Active play - accepting player input</summary>
	Active,
	/// <summary>Processing movement - pieces in motion</summary>
	Processing
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