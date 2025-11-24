using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BarBox.Games.Carrom;

/// <summary>
/// Serializable snapshot of complete game state for save/load and debugging
/// Enables data-driven testing by loading arbitrary game states
/// </summary>
[Serializable]
public class SerializableGameState
{
	[JsonPropertyName("players")]
	public Dictionary<string, PlayerSnapshot> Players { get; set; } = new();

	[JsonPropertyName("pieces")]
	public List<PieceSnapshot> Pieces { get; set; } = new();

	[JsonPropertyName("current_turn")]
	public TurnSnapshot CurrentTurn { get; set; } = new();

	[JsonPropertyName("current_player_index")]
	public int CurrentPlayerIndex { get; set; }

	[JsonPropertyName("game_phase")]
	public string GamePhase { get; set; } = "Ready";

	[JsonPropertyName("is_breaking_turn")]
	public bool IsBreakingTurn { get; set; }

	[JsonPropertyName("breaking_attempts")]
	public int BreakingAttempts { get; set; }

	[JsonPropertyName("timestamp")]
	public string Timestamp { get; set; } = DateTime.UtcNow.ToString("O");

	[JsonPropertyName("game_mode")]
	public string GameMode { get; set; } = "Competitive";
}

/// <summary>
/// Serializable representation of piece state
/// </summary>
[Serializable]
public class PieceSnapshot
{
	[JsonPropertyName("type")]
	public string Type { get; set; } = "White";

	[JsonPropertyName("position_x")]
	public float PositionX { get; set; }

	[JsonPropertyName("position_y")]
	public float PositionY { get; set; }

	[JsonPropertyName("velocity_x")]
	public float VelocityX { get; set; }

	[JsonPropertyName("velocity_y")]
	public float VelocityY { get; set; }

	[JsonPropertyName("angular_velocity")]
	public float AngularVelocity { get; set; }

	[JsonPropertyName("visible")]
	public bool Visible { get; set; } = true;

	/// <summary>
	/// Create snapshot from CarromPiece
	/// </summary>
	public static PieceSnapshot FromPiece(CarromPiece piece)
	{
		if (piece == null || !GodotObject.IsInstanceValid(piece))
			return null;

		return new PieceSnapshot
		{
			Type = piece.Type.ToString(),
			PositionX = piece.GlobalPosition.X,
			PositionY = piece.GlobalPosition.Y,
			VelocityX = piece.LinearVelocity.X,
			VelocityY = piece.LinearVelocity.Y,
			AngularVelocity = piece.AngularVelocity,
			Visible = piece.Visible
		};
	}

	/// <summary>
	/// Get position as Vector2
	/// </summary>
	public Vector2 GetPosition() => new(PositionX, PositionY);

	/// <summary>
	/// Get velocity as Vector2
	/// </summary>
	public Vector2 GetVelocity() => new(VelocityX, VelocityY);

	/// <summary>
	/// Get piece type enum
	/// </summary>
	public PieceType GetPieceType()
	{
		return Enum.TryParse<PieceType>(Type, out var pieceType)
			? pieceType
			: PieceType.White;
	}
}

/// <summary>
/// Serializable representation of player state
/// </summary>
[Serializable]
public class PlayerSnapshot
{
	[JsonPropertyName("player_id")]
	public string PlayerId { get; set; }

	[JsonPropertyName("assigned_piece_type")]
	public string AssignedPieceType { get; set; } = "White";

	[JsonPropertyName("pieces_pocketed")]
	public int PiecesPocketed { get; set; }

	[JsonPropertyName("has_queen")]
	public bool HasQueen { get; set; }

	[JsonPropertyName("queen_covered")]
	public bool QueenCovered { get; set; }

	[JsonPropertyName("total_shots")]
	public int TotalShots { get; set; }

	[JsonPropertyName("valid_pockets")]
	public int ValidPockets { get; set; }

	[JsonPropertyName("fouls")]
	public int Fouls { get; set; }

	[JsonPropertyName("pocketed_pieces")]
	public List<string> PocketedPieces { get; set; } = new();

	/// <summary>
	/// Create snapshot from PlayerState
	/// </summary>
	public static PlayerSnapshot FromPlayerState(PlayerState playerState)
	{
		if (playerState == null)
			return null;

		return new PlayerSnapshot
		{
			PlayerId = playerState.PlayerId,
			AssignedPieceType = playerState.AssignedPieceType.ToString(),
			PiecesPocketed = playerState.PiecesPocketed,
			HasQueen = playerState.HasQueen,
			QueenCovered = playerState.QueenCovered,
			TotalShots = playerState.TotalShots,
			ValidPockets = playerState.ValidPockets,
			Fouls = playerState.Fouls,
			PocketedPieces = playerState.PocketedPieces.ConvertAll(p => p.ToString())
		};
	}

	/// <summary>
	/// Convert to PlayerState
	/// </summary>
	public PlayerState ToPlayerState()
	{
		var pieceType = Enum.TryParse<PieceType>(AssignedPieceType, out var type)
			? type
			: PieceType.White;

		var pocketedPiecesList = new List<PieceType>();
		foreach (var piece in PocketedPieces)
		{
			if (Enum.TryParse<PieceType>(piece, out var parsedType))
				pocketedPiecesList.Add(parsedType);
		}

		return new PlayerState
		{
			PlayerId = PlayerId,
			AssignedPieceType = pieceType,
			PiecesPocketed = PiecesPocketed,
			HasQueen = HasQueen,
			QueenCovered = QueenCovered,
			TotalShots = TotalShots,
			ValidPockets = ValidPockets,
			Fouls = Fouls,
			PocketedPieces = pocketedPiecesList
		};
	}
}

/// <summary>
/// Serializable representation of turn context
/// </summary>
[Serializable]
public class TurnSnapshot
{
	[JsonPropertyName("valid_pocket_this_stroke")]
	public bool ValidPocketThisStroke { get; set; }

	[JsonPropertyName("foul_this_stroke")]
	public bool FoulThisStroke { get; set; }

	[JsonPropertyName("is_breaking_turn")]
	public bool IsBreakingTurn { get; set; }

	[JsonPropertyName("breaking_attempts")]
	public int BreakingAttempts { get; set; }

	[JsonPropertyName("pieces_disturbed_in_breaking")]
	public bool PiecesDisturbedInBreaking { get; set; }

	[JsonPropertyName("pocketed_queen_this_turn")]
	public bool PocketedQueenThisTurn { get; set; }

	[JsonPropertyName("needs_queen_covering")]
	public bool NeedsQueenCovering { get; set; }

	[JsonPropertyName("pieces_this_turn")]
	public List<string> PiecesThisTurn { get; set; } = new();

	/// <summary>
	/// Create snapshot from TurnContext
	/// </summary>
	public static TurnSnapshot FromTurnContext(TurnContext turnContext)
	{
		if (turnContext == null)
			return new TurnSnapshot();

		return new TurnSnapshot
		{
			ValidPocketThisStroke = turnContext.ValidPocketThisStroke,
			FoulThisStroke = turnContext.FoulThisStroke,
			IsBreakingTurn = turnContext.IsBreakingTurn,
			BreakingAttempts = turnContext.BreakingAttempts,
			PiecesDisturbedInBreaking = turnContext.PiecesDisturbedInBreaking,
			PocketedQueenThisTurn = turnContext.PocketedQueenThisTurn,
			NeedsQueenCovering = turnContext.NeedsQueenCovering,
			PiecesThisTurn = turnContext.PiecesThisTurn.ConvertAll(p => p.ToString())
		};
	}

	/// <summary>
	/// Convert to TurnContext
	/// </summary>
	public TurnContext ToTurnContext()
	{
		var piecesThisTurnList = new List<PieceType>();
		foreach (var piece in PiecesThisTurn)
		{
			if (Enum.TryParse<PieceType>(piece, out var parsedType))
				piecesThisTurnList.Add(parsedType);
		}

		return new TurnContext
		{
			ValidPocketThisStroke = ValidPocketThisStroke,
			FoulThisStroke = FoulThisStroke,
			IsBreakingTurn = IsBreakingTurn,
			BreakingAttempts = BreakingAttempts,
			PiecesDisturbedInBreaking = PiecesDisturbedInBreaking,
			PocketedQueenThisTurn = PocketedQueenThisTurn,
			NeedsQueenCovering = NeedsQueenCovering,
			PiecesThisTurn = piecesThisTurnList
		};
	}
}
