namespace BarBox.Games.Carrom;

/// <summary>
/// Pure C# rule engine for Carrom game logic
/// Zero Godot dependencies - 100% unit testable
/// All methods are pure functions with no side effects
/// </summary>
public static class CarromRuleEngine
{
	// ================================================================
	// PIECE POCKETING RULES
	// ================================================================

	/// <summary>
	/// Evaluate whether a pocketed piece is valid or a foul
	/// </summary>
	public static PocketEvaluation EvaluatePocketedPiece(
		PieceType pocketedType,
		PlayerState currentPlayer,
		TurnContext turnContext)
	{
		// Striker pocketed = foul
		if (pocketedType == PieceType.Striker)
		{
			return PocketEvaluation.Foul(
				FoulType.StrikerPocketed,
				shouldReturnPiece: true,
				reason: "Striker pocketed - foul");
		}

		// Queen pocketing rules
		if (pocketedType == PieceType.Red)
		{
			return EvaluateQueenPocketing(currentPlayer, turnContext);
		}

		// Opponent piece pocketed = valid for opponent (stays pocketed, counts for opponent)
		// Per ICF Rules 74-76: Opponent pieces remain pocketed regardless of stroke type
		if (pocketedType != currentPlayer.AssignedPieceType)
		{
			return PocketEvaluation.Valid(
				reason: "Opponent piece pocketed - counts for opponent, turn ends");
		}

		// Own piece pocketed = valid
		// Check if this covers the queen
		if (turnContext.NeedsQueenCovering)
		{
			return PocketEvaluation.QueenCovering(
				reason: "Own piece pocketed - covers queen");
		}

		return PocketEvaluation.Valid(
			reason: "Own piece pocketed - valid");
	}

	/// <summary>
	/// Evaluate queen pocketing eligibility and rules
	/// </summary>
	public static PocketEvaluation EvaluateQueenPocketing(
		PlayerState currentPlayer,
		TurnContext turnContext)
	{
		// Player already has queen = improper pocketing
		if (currentPlayer.HasQueen)
		{
			return PocketEvaluation.Foul(
				FoulType.ImproperQueenPocketing,
				shouldReturnPiece: false,
				reason: "Player already has queen - improper pocketing");
		}

		// Player must have at least 1 piece to pocket queen
		if (currentPlayer.PiecesPocketed < 1)
		{
			return PocketEvaluation.Foul(
				FoulType.ImproperQueenPocketing,
				shouldReturnPiece: false,
				reason: "Must pocket at least 1 piece before queen");
		}

		// Valid queen pocketing
		return PocketEvaluation.QueenPocket(
			canPocket: true,
			reason: "Queen pocketed - must cover in same turn");
	}

	// ================================================================
	// FOUL PENALTY RULES
	// ================================================================

	/// <summary>
	/// Calculate penalty for a foul
	/// </summary>
	public static PenaltyAction CalculateFoulPenalty(
		FoulType foulType,
		PlayerState player)
	{
		switch (foulType)
		{
			case FoulType.StrikerPocketed:
				// Return one piece if available
				if (player.PiecesPocketed > 0)
				{
					return PenaltyAction.ReturnPieces(
						count: 1,
						preferredType: player.AssignedPieceType,
						reason: "Striker foul - return one piece");
				}
				return PenaltyAction.None();

			case FoulType.OpponentPiecePocketed:
				// NOTE: This case should never occur now that opponent piece pocketing
				// is treated as valid. Keeping for backwards compatibility.
				return PenaltyAction.None();

			case FoulType.ImproperQueenPocketing:
				// No piece penalty, but queen may be returned
				return PenaltyAction.None();

			default:
				return PenaltyAction.None();
		}
	}

	// ================================================================
	// TURN CONTINUATION RULES
	// ================================================================

	// ================================================================
	// BREAKING TURN RULES
	// ================================================================

	/// <summary>
	/// Evaluate breaking turn outcome
	/// </summary>
	public static BreakingResult EvaluateBreakingTurn(
		bool validPocket,
		bool foul,
		int attemptNumber,
		bool piecesDisturbed)
	{
		// Success conditions: pieces disturbed OR valid pocket
		if (piecesDisturbed || validPocket)
		{
			// Breaking successful - check if turn continues
			if (validPocket && !foul)
			{
				return BreakingResult.Success(
					TurnDecision.Continue,
					reason: "Breaking successful with valid pocket - turn continues");
			}

			return BreakingResult.Success(
				TurnDecision.Pass,
				reason: "Breaking successful - turn passes");
		}

		// Failure after 3 attempts
		if (attemptNumber >= 3)
		{
			return BreakingResult.Failure(
				TurnDecision.Pass,
				reason: "Breaking failed after 3 attempts - turn passes");
		}

		// Continue breaking (< 3 attempts)
		return BreakingResult.Continue(
			attemptsRemaining: 3 - attemptNumber,
			reason: "Breaking incomplete");
	}

	/// <summary>
	/// Check if pieces have been sufficiently disturbed for breaking turn
	/// </summary>
	public static bool CheckPiecesDisturbed(float maxDistanceFromCenter, float pieceRadius)
	{
		// Pieces are considered disturbed if any piece moved > 3 piece radii from center
		float disturbanceThreshold = pieceRadius * 3.0f;
		return maxDistanceFromCenter > disturbanceThreshold;
	}

	// ================================================================
	// QUEEN COVERING RULES
	// ================================================================

	/// <summary>
	/// Validate queen covering at end of turn
	/// Returns true if queen needs to be returned to center
	/// </summary>
	public static bool ValidateQueenCovering(
		PlayerState player,
		TurnContext turnContext)
	{
		// If player pocketed queen this turn but didn't cover it
		if (turnContext.PocketedQueenThisTurn && turnContext.NeedsQueenCovering)
		{
			// Queen needs to be returned
			return true;
		}

		return false;
	}

	/// <summary>
	/// Update player queen state after pocketing queen
	/// </summary>
	public static void ApplyQueenPocketing(PlayerState player, TurnContext turnContext)
	{
		player.HasQueen = true;
		turnContext.PocketedQueenThisTurn = true;
		turnContext.NeedsQueenCovering = true;
	}

	/// <summary>
	/// Update player queen state after covering queen
	/// </summary>
	public static void ApplyQueenCovering(PlayerState player, TurnContext turnContext)
	{
		player.QueenCovered = true;
		turnContext.NeedsQueenCovering = false;
	}

	/// <summary>
	/// Return queen to board (failed covering)
	/// </summary>
	public static void ReturnQueen(PlayerState player)
	{
		player.HasQueen = false;
		player.QueenCovered = false;
	}

	// ================================================================
	// WIN CONDITION RULES
	// ================================================================

	/// <summary>
	/// Check if player has won (2-player mode)
	/// </summary>
	public static WinCondition CheckWinCondition_Singles(PlayerState player)
	{
		// Must pocket all 9 pieces
		if (player.PiecesPocketed < 9)
		{
			return WinCondition.NotYet(
				reason: $"Only {player.PiecesPocketed}/9 pieces pocketed");
		}

		// If player has queen, it must be covered
		if (player.HasQueen && !player.QueenCovered)
		{
			return WinCondition.NotYet(
				reason: "Queen not covered");
		}

		return WinCondition.Win(
			reason: "All 9 pieces pocketed + queen covered (if applicable)");
	}

	/// <summary>
	/// Check if team has won (4-player mode)
	/// </summary>
	public static WinCondition CheckWinCondition_Doubles(
		PlayerState player1,
		PlayerState player2)
	{
		// Combined pieces for team
		int teamPieces = player1.PiecesPocketed + player2.PiecesPocketed;

		// Must pocket all 9 team pieces
		if (teamPieces < 9)
		{
			return WinCondition.NotYet(
				reason: $"Team has {teamPieces}/9 pieces");
		}

		// If either teammate has queen, it must be covered
		bool teamHasQueen = player1.HasQueen || player2.HasQueen;
		bool teamQueenCovered = player1.QueenCovered || player2.QueenCovered;

		if (teamHasQueen && !teamQueenCovered)
		{
			return WinCondition.NotYet(
				reason: "Team queen not covered");
		}

		return WinCondition.Win(
			reason: "Team has all 9 pieces + queen covered (if applicable)");
	}

	// ================================================================
	// PIECE RETURN LOGIC
	// ================================================================

	/// <summary>
	/// Select which piece to return for penalty
	/// Returns most recently pocketed piece of preferred type
	/// </summary>
	public static PieceType? SelectPieceToReturn(
		PlayerState player,
		PieceType? preferredType)
	{
		if (player.PocketedPieces.Count == 0)
			return null;

		// Try to find preferred type (most recent)
		if (preferredType.HasValue)
		{
			for (int i = player.PocketedPieces.Count - 1; i >= 0; i--)
			{
				if (player.PocketedPieces[i] == preferredType.Value)
				{
					return player.PocketedPieces[i];
				}
			}
		}

		// Return most recent piece
		return player.PocketedPieces[player.PocketedPieces.Count - 1];
	}

	/// <summary>
	/// Apply piece return to player state
	/// </summary>
	public static void ApplyPieceReturn(PlayerState player, PieceType pieceType)
	{
		// Remove from pocketed pieces list (most recent occurrence)
		for (int i = player.PocketedPieces.Count - 1; i >= 0; i--)
		{
			if (player.PocketedPieces[i] == pieceType)
			{
				player.PocketedPieces.RemoveAt(i);
				player.PiecesPocketed--;
				break;
			}
		}
	}

	// ================================================================
	// STATISTICS HELPERS
	// ================================================================

	/// <summary>
	/// Calculate player accuracy (valid pockets / total shots)
	/// </summary>
	public static float CalculateAccuracy(int validPockets, int totalShots)
	{
		if (totalShots == 0)
			return 0.0f;

		return (float)validPockets / totalShots;
	}

	/// <summary>
	/// Calculate final score based on ICF rules
	/// </summary>
	public static int CalculateScore(PlayerState player)
	{
		int score = player.PiecesPocketed; // 1 point per piece

		// Queen worth 3 points if covered
		if (player.HasQueen && player.QueenCovered)
		{
			score += 3;
		}

		return score;
	}
}
