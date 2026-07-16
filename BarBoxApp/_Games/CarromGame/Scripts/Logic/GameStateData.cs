using System.Collections.Generic;

namespace BarBox.Games.Carrom;

/// <summary>
/// Pure data class representing a player's state for rule validation
/// No Godot dependencies - fully unit testable
/// </summary>
public class PlayerState
{
	public string PlayerId { get; set; }

	public PieceType AssignedPieceType { get; set; }

	public int PiecesPocketed { get; set; }

	public bool HasQueen { get; set; }

	public bool QueenCovered { get; set; }

	public int TotalShots { get; set; }

	public int ValidPockets { get; set; }

	public int Fouls { get; set; }

	public List<PieceType> PocketedPieces { get; set; } = new();

	/// <summary>
	/// Create PlayerState from CarromPlayer instance
	/// </summary>
	public static PlayerState FromPlayer(CarromPlayer player)
	{
		if (player == null)
		{
			return null;
		}

		return new PlayerState
		{
			PlayerId = player.PlayerId,
			AssignedPieceType = player.AssignedPieceType,
			PiecesPocketed = player.PiecesPocketed,
			HasQueen = player.HasQueen,
			QueenCovered = player.QueenCovered,
			TotalShots = player.GetTotalShots(),
			ValidPockets = player.GetValidPockets(),
			Fouls = player.GetFouls(),
			PocketedPieces = [.. player.GetPocketedPieces()],
		};
	}

	/// <summary>
	/// Copy constructor for testing
	/// </summary>
	public PlayerState Clone()
	{
		return new PlayerState
		{
			PlayerId = PlayerId,
			AssignedPieceType = AssignedPieceType,
			PiecesPocketed = PiecesPocketed,
			HasQueen = HasQueen,
			QueenCovered = QueenCovered,
			TotalShots = TotalShots,
			ValidPockets = ValidPockets,
			Fouls = Fouls,
			PocketedPieces = [.. PocketedPieces],
		};
	}
}

/// <summary>
/// Context for current turn state
/// </summary>
public class TurnContext
{
	public bool ValidPocketThisStroke { get; set; }

	public bool FoulThisStroke { get; set; }

	public bool IsBreakingTurn { get; set; }

	public int BreakingAttempts { get; set; }

	public bool PiecesDisturbedInBreaking { get; set; }

	public bool PocketedQueenThisTurn { get; set; }

	public bool NeedsQueenCovering { get; set; }

	public bool OpponentPiecePocketedThisTurn { get; set; }

	public List<PieceType> PiecesThisTurn { get; set; } = new();

	public TurnContext Clone()
	{
		return new TurnContext
		{
			ValidPocketThisStroke = ValidPocketThisStroke,
			FoulThisStroke = FoulThisStroke,
			IsBreakingTurn = IsBreakingTurn,
			BreakingAttempts = BreakingAttempts,
			PiecesDisturbedInBreaking = PiecesDisturbedInBreaking,
			PocketedQueenThisTurn = PocketedQueenThisTurn,
			NeedsQueenCovering = NeedsQueenCovering,
			OpponentPiecePocketedThisTurn = OpponentPiecePocketedThisTurn,
			PiecesThisTurn = [.. PiecesThisTurn],
		};
	}
}

/// <summary>
/// Result of evaluating a pocketed piece
/// </summary>
public class PocketEvaluation
{
	public bool IsValid { get; set; }

	public bool IsFoul { get; set; }

	public FoulType FoulType { get; set; }

	public bool ShouldReturnPiece { get; set; }

	public bool UpdatesQueenState { get; set; }

	public bool CoversQueen { get; set; }

	public string Reason { get; set; }

	public static PocketEvaluation Valid(string reason = "")
	{
		return new PocketEvaluation
		{
			IsValid = true,
			IsFoul = false,
			Reason = reason,
		};
	}

	public static PocketEvaluation Foul(FoulType foulType, bool shouldReturnPiece, string reason = "")
	{
		return new PocketEvaluation
		{
			IsValid = false,
			IsFoul = true,
			FoulType = foulType,
			ShouldReturnPiece = shouldReturnPiece,
			Reason = reason,
		};
	}

	public static PocketEvaluation QueenPocket(bool canPocket, string reason = "")
	{
		return new PocketEvaluation
		{
			IsValid = canPocket,
			IsFoul = !canPocket,
			FoulType = canPocket ? FoulType.General : FoulType.ImproperQueenPocketing,
			UpdatesQueenState = canPocket,
			Reason = reason,
		};
	}

	public static PocketEvaluation QueenCovering(string reason = "")
	{
		return new PocketEvaluation
		{
			IsValid = true,
			IsFoul = false,
			CoversQueen = true,
			Reason = reason,
		};
	}
}

/// <summary>
/// Result of turn continuation decision
/// </summary>
public enum TurnDecision
{
	Continue,    // Player gets another turn
	Pass,        // Turn passes to next player
	BreakingContinue, // Breaking turn continues (< 3 attempts)
}

/// <summary>
/// Result of breaking turn evaluation
/// </summary>
public class BreakingResult
{
	public bool BreakingComplete { get; set; }

	public bool IsSuccess { get; set; }

	public TurnDecision TurnDecision { get; set; }

	public string Reason { get; set; }

	public static BreakingResult Success(TurnDecision decision, string reason = "")
	{
		return new BreakingResult
		{
			BreakingComplete = true,
			IsSuccess = true,
			TurnDecision = decision,
			Reason = reason,
		};
	}

	public static BreakingResult Failure(TurnDecision decision, string reason = "")
	{
		return new BreakingResult
		{
			BreakingComplete = true,
			IsSuccess = false,
			TurnDecision = decision,
			Reason = reason,
		};
	}

	public static BreakingResult Continue(int attemptsRemaining, string reason = "")
	{
		return new BreakingResult
		{
			BreakingComplete = false,
			IsSuccess = false,
			TurnDecision = TurnDecision.BreakingContinue,
			Reason = $"{reason} ({attemptsRemaining} attempts remaining)",
		};
	}
}

/// <summary>
/// Penalty action to apply
/// </summary>
public class PenaltyAction
{
	public int PiecesToReturn { get; set; }

	public PieceType? PreferredPieceType { get; set; }

	public string Reason { get; set; }

	public static PenaltyAction None()
	{
		return new PenaltyAction
		{
			PiecesToReturn = 0,
			Reason = "No penalty",
		};
	}

	public static PenaltyAction ReturnPieces(int count, PieceType? preferredType, string reason)
	{
		return new PenaltyAction
		{
			PiecesToReturn = count,
			PreferredPieceType = preferredType,
			Reason = reason,
		};
	}
}

/// <summary>
/// Win condition result
/// </summary>
public class WinCondition
{
	public bool HasWon { get; set; }

	public string Reason { get; set; }

	public static WinCondition Win(string reason = "")
	{
		return new WinCondition
		{
			HasWon = true,
			Reason = reason,
		};
	}

	public static WinCondition NotYet(string reason = "")
	{
		return new WinCondition
		{
			HasWon = false,
			Reason = reason,
		};
	}
}
