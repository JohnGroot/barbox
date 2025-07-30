using Godot;

/// <summary>
/// Shared enums for Carrom game
/// </summary>
public enum PieceType
{
	White,
	Black,
	Red,    // Queen piece
	Striker
}

public enum GamePhase
{
	Setup,
	Active,
	Processing
}

public enum CarromGameMode
{
	Practice,    // Single piece, free play
	Competitive  // Full carrom rules
}