using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

namespace BarBox.Games.Carrom.Tests;

/// <summary>
/// Unit tests for CarromRuleEngine - pure C# game logic with zero Godot dependencies
/// Tests all official ICF carrom rules: pocketing, fouls, queen covering, breaking turns, win conditions
/// </summary>
public class CarromRuleEngineTests : TestClass
{
	public CarromRuleEngineTests(Node testScene) : base(testScene) { }

	// ================================================================
	// PIECE POCKETING TESTS
	// ================================================================

	[Test]
	public void EvaluatePocketedPiece_StrikerPocketed_ReturnsFoul()
	{
		// Arrange
		var playerState = CreateTestPlayer(PieceType.White, piecesPocketed: 2);
		var turnContext = new TurnContext();

		// Act
		var result = CarromRuleEngine.EvaluatePocketedPiece(
			PieceType.Striker,
			playerState,
			turnContext);

		// Assert
		result.IsFoul.ShouldBeTrue();
		result.IsValid.ShouldBeFalse();
		result.FoulType.ShouldBe(FoulType.StrikerPocketed);
		result.ShouldReturnPiece.ShouldBeTrue();
	}

	[Test]
	public void EvaluatePocketedPiece_OwnPiece_ReturnsValid()
	{
		// Arrange
		var playerState = CreateTestPlayer(PieceType.White, piecesPocketed: 2);
		var turnContext = new TurnContext();

		// Act
		var result = CarromRuleEngine.EvaluatePocketedPiece(
			PieceType.White,
			playerState,
			turnContext);

		// Assert
		result.IsValid.ShouldBeTrue();
		result.IsFoul.ShouldBeFalse();
	}

	[Test]
	public void EvaluatePocketedPiece_OpponentPiece_IsValidForOpponent()
	{
		// Arrange
		var playerState = CreateTestPlayer(PieceType.White, piecesPocketed: 2);
		var turnContext = new TurnContext();

		// Act
		var result = CarromRuleEngine.EvaluatePocketedPiece(
			PieceType.Black, // Opponent's piece
			playerState,
			turnContext);

		// Assert - Per ICF Rules 74-76: Opponent pieces remain pocketed and count for opponent
		result.IsValid.ShouldBeTrue();
		result.IsFoul.ShouldBeFalse();
	}

	// ================================================================
	// QUEEN POCKETING TESTS
	// ================================================================

	[Test]
	public void EvaluateQueenPocketing_WithOnePiecePocketed_AllowsQueenPocket()
	{
		// Arrange
		var playerState = CreateTestPlayer(PieceType.White, piecesPocketed: 1);
		var turnContext = new TurnContext();

		// Act
		var result = CarromRuleEngine.EvaluateQueenPocketing(playerState, turnContext);

		// Assert
		result.IsValid.ShouldBeTrue();
		result.IsFoul.ShouldBeFalse();
		result.UpdatesQueenState.ShouldBeTrue();
	}

	[Test]
	public void EvaluateQueenPocketing_WithNoPieces_ReturnsFoul()
	{
		// Arrange
		var playerState = CreateTestPlayer(PieceType.White, piecesPocketed: 0);
		var turnContext = new TurnContext();

		// Act
		var result = CarromRuleEngine.EvaluateQueenPocketing(playerState, turnContext);

		// Assert
		result.IsFoul.ShouldBeTrue();
		result.IsValid.ShouldBeFalse();
		result.FoulType.ShouldBe(FoulType.ImproperQueenPocketing);
	}

	[Test]
	public void EvaluateQueenPocketing_PlayerAlreadyHasQueen_ReturnsFoul()
	{
		// Arrange
		var playerState = CreateTestPlayer(PieceType.White, piecesPocketed: 3, hasQueen: true);
		var turnContext = new TurnContext();

		// Act
		var result = CarromRuleEngine.EvaluateQueenPocketing(playerState, turnContext);

		// Assert
		result.IsFoul.ShouldBeTrue();
		result.IsValid.ShouldBeFalse();
		result.FoulType.ShouldBe(FoulType.ImproperQueenPocketing);
	}

	[Test]
	public void EvaluatePocketedPiece_OwnPieceAfterQueen_CoversQueen()
	{
		// Arrange
		var playerState = CreateTestPlayer(PieceType.White, piecesPocketed: 2, hasQueen: true);
		var turnContext = new TurnContext
		{
			NeedsQueenCovering = true,
			PocketedQueenThisTurn = true
		};

		// Act
		var result = CarromRuleEngine.EvaluatePocketedPiece(
			PieceType.White,
			playerState,
			turnContext);

		// Assert
		result.IsValid.ShouldBeTrue();
		result.CoversQueen.ShouldBeTrue();
	}

	// ================================================================
	// FOUL PENALTY TESTS
	// ================================================================

	[Test]
	public void CalculateFoulPenalty_StrikerFoul_WithPieces_ReturnsPenalty()
	{
		// Arrange
		var playerState = CreateTestPlayer(PieceType.White, piecesPocketed: 3);

		// Act
		var penalty = CarromRuleEngine.CalculateFoulPenalty(
			FoulType.StrikerPocketed,
			playerState);

		// Assert
		penalty.PiecesToReturn.ShouldBe(1);
		penalty.PreferredPieceType.ShouldBe(PieceType.White);
	}

	[Test]
	public void CalculateFoulPenalty_StrikerFoul_NoPieces_ReturnsNoPenalty()
	{
		// Arrange
		var playerState = CreateTestPlayer(PieceType.White, piecesPocketed: 0);

		// Act
		var penalty = CarromRuleEngine.CalculateFoulPenalty(
			FoulType.StrikerPocketed,
			playerState);

		// Assert
		penalty.PiecesToReturn.ShouldBe(0);
	}

	[Test]
	public void CalculateFoulPenalty_OpponentPieceFoul_ReturnsNoPenalty()
	{
		// Arrange
		var playerState = CreateTestPlayer(PieceType.White, piecesPocketed: 2);

		// Act
		var penalty = CarromRuleEngine.CalculateFoulPenalty(
			FoulType.OpponentPiecePocketed,
			playerState);

		// Assert - Per ICF Rules: Opponent piece pocketing is valid, no penalty
		// This foul type should never occur in practice since opponent pieces stay pocketed
		penalty.PiecesToReturn.ShouldBe(0);
	}

	[Test]
	public void CalculateFoulPenalty_ImproperQueenPocketing_ReturnsNoPenalty()
	{
		// Arrange
		var playerState = CreateTestPlayer(PieceType.White, piecesPocketed: 2);

		// Act
		var penalty = CarromRuleEngine.CalculateFoulPenalty(
			FoulType.ImproperQueenPocketing,
			playerState);

		// Assert - Queen is returned but no additional penalty piece
		penalty.PiecesToReturn.ShouldBe(0);
	}

	// ================================================================
	// BREAKING TURN TESTS
	// ================================================================

	[Test]
	public void EvaluateBreakingTurn_PiecesDisturbed_ReturnsSuccess()
	{
		// Act
		var result = CarromRuleEngine.EvaluateBreakingTurn(
			validPocket: false,
			foul: false,
			attemptNumber: 1,
			piecesDisturbed: true);

		// Assert
		result.BreakingComplete.ShouldBeTrue();
		result.IsSuccess.ShouldBeTrue();
		result.TurnDecision.ShouldBe(TurnDecision.Pass);
	}

	[Test]
	public void EvaluateBreakingTurn_ValidPocketAndDisturbed_ReturnsContinue()
	{
		// Act
		var result = CarromRuleEngine.EvaluateBreakingTurn(
			validPocket: true,
			foul: false,
			attemptNumber: 1,
			piecesDisturbed: true);

		// Assert
		result.BreakingComplete.ShouldBeTrue();
		result.IsSuccess.ShouldBeTrue();
		result.TurnDecision.ShouldBe(TurnDecision.Continue);
	}

	[Test]
	public void EvaluateBreakingTurn_NoDisturbance_FirstAttempt_ReturnsContinue()
	{
		// Act
		var result = CarromRuleEngine.EvaluateBreakingTurn(
			validPocket: false,
			foul: false,
			attemptNumber: 1,
			piecesDisturbed: false);

		// Assert
		result.BreakingComplete.ShouldBeFalse();
		result.TurnDecision.ShouldBe(TurnDecision.BreakingContinue);
	}

	[Test]
	public void EvaluateBreakingTurn_NoDisturbance_ThirdAttempt_ReturnsFailure()
	{
		// Act
		var result = CarromRuleEngine.EvaluateBreakingTurn(
			validPocket: false,
			foul: false,
			attemptNumber: 3,
			piecesDisturbed: false);

		// Assert
		result.BreakingComplete.ShouldBeTrue();
		result.IsSuccess.ShouldBeFalse();
		result.TurnDecision.ShouldBe(TurnDecision.Pass);
	}

	[Test]
	public void CheckPiecesDisturbed_LargeDistance_ReturnsTrue()
	{
		// Arrange
		float pieceRadius = 10.0f;
		float maxDistance = 35.0f; // More than 3 * pieceRadius

		// Act
		var disturbed = CarromRuleEngine.CheckPiecesDisturbed(maxDistance, pieceRadius);

		// Assert
		disturbed.ShouldBeTrue();
	}

	[Test]
	public void CheckPiecesDisturbed_SmallDistance_ReturnsFalse()
	{
		// Arrange
		float pieceRadius = 10.0f;
		float maxDistance = 20.0f; // Less than 3 * pieceRadius

		// Act
		var disturbed = CarromRuleEngine.CheckPiecesDisturbed(maxDistance, pieceRadius);

		// Assert
		disturbed.ShouldBeFalse();
	}

	// ================================================================
	// QUEEN COVERING VALIDATION TESTS
	// ================================================================

	[Test]
	public void ValidateQueenCovering_QueenPocketedButNotCovered_ReturnsTrue()
	{
		// Arrange
		var playerState = CreateTestPlayer(PieceType.White, piecesPocketed: 2);
		var turnContext = new TurnContext
		{
			PocketedQueenThisTurn = true,
			NeedsQueenCovering = true
		};

		// Act
		var needsReturning = CarromRuleEngine.ValidateQueenCovering(playerState, turnContext);

		// Assert
		needsReturning.ShouldBeTrue();
	}

	[Test]
	public void ValidateQueenCovering_QueenCovered_ReturnsFalse()
	{
		// Arrange
		var playerState = CreateTestPlayer(PieceType.White, piecesPocketed: 3);
		var turnContext = new TurnContext
		{
			PocketedQueenThisTurn = true,
			NeedsQueenCovering = false // Covered
		};

		// Act
		var needsReturning = CarromRuleEngine.ValidateQueenCovering(playerState, turnContext);

		// Assert
		needsReturning.ShouldBeFalse();
	}

	[Test]
	public void ApplyQueenPocketing_UpdatesPlayerAndTurnState()
	{
		// Arrange
		var playerState = CreateTestPlayer(PieceType.White, piecesPocketed: 2);
		var turnContext = new TurnContext();

		// Act
		CarromRuleEngine.ApplyQueenPocketing(playerState, turnContext);

		// Assert
		playerState.HasQueen.ShouldBeTrue();
		turnContext.PocketedQueenThisTurn.ShouldBeTrue();
		turnContext.NeedsQueenCovering.ShouldBeTrue();
	}

	[Test]
	public void ApplyQueenCovering_UpdatesPlayerAndTurnState()
	{
		// Arrange
		var playerState = CreateTestPlayer(PieceType.White, piecesPocketed: 3, hasQueen: true);
		var turnContext = new TurnContext { NeedsQueenCovering = true };

		// Act
		CarromRuleEngine.ApplyQueenCovering(playerState, turnContext);

		// Assert
		playerState.QueenCovered.ShouldBeTrue();
		turnContext.NeedsQueenCovering.ShouldBeFalse();
	}

	[Test]
	public void ReturnQueen_ClearsQueenState()
	{
		// Arrange
		var playerState = CreateTestPlayer(PieceType.White, piecesPocketed: 3, hasQueen: true);

		// Act
		CarromRuleEngine.ReturnQueen(playerState);

		// Assert
		playerState.HasQueen.ShouldBeFalse();
		playerState.QueenCovered.ShouldBeFalse();
	}

	// ================================================================
	// WIN CONDITION TESTS - SINGLES
	// ================================================================

	[Test]
	public void CheckWinCondition_Singles_AllPiecesAndQueenCovered_ReturnsWin()
	{
		// Arrange
		var playerState = CreateTestPlayer(
			PieceType.White,
			piecesPocketed: 9,
			hasQueen: true,
			queenCovered: true);

		// Act
		var result = CarromRuleEngine.CheckWinCondition_Singles(playerState);

		// Assert
		result.HasWon.ShouldBeTrue();
	}

	[Test]
	public void CheckWinCondition_Singles_AllPiecesNoQueen_ReturnsWin()
	{
		// Arrange
		var playerState = CreateTestPlayer(PieceType.White, piecesPocketed: 9);

		// Act
		var result = CarromRuleEngine.CheckWinCondition_Singles(playerState);

		// Assert
		result.HasWon.ShouldBeTrue();
	}

	[Test]
	public void CheckWinCondition_Singles_AllPiecesQueenNotCovered_ReturnsNotWon()
	{
		// Arrange
		var playerState = CreateTestPlayer(
			PieceType.White,
			piecesPocketed: 9,
			hasQueen: true,
			queenCovered: false);

		// Act
		var result = CarromRuleEngine.CheckWinCondition_Singles(playerState);

		// Assert
		result.HasWon.ShouldBeFalse();
	}

	[Test]
	public void CheckWinCondition_Singles_IncompletePieces_ReturnsNotWon()
	{
		// Arrange
		var playerState = CreateTestPlayer(PieceType.White, piecesPocketed: 7);

		// Act
		var result = CarromRuleEngine.CheckWinCondition_Singles(playerState);

		// Assert
		result.HasWon.ShouldBeFalse();
	}

	// ================================================================
	// WIN CONDITION TESTS - DOUBLES
	// ================================================================

	[Test]
	public void CheckWinCondition_Doubles_CombinedPiecesAndQueen_ReturnsWin()
	{
		// Arrange
		var player1 = CreateTestPlayer(PieceType.White, piecesPocketed: 5);
		var player2 = CreateTestPlayer(
			PieceType.White,
			piecesPocketed: 4,
			hasQueen: true,
			queenCovered: true);

		// Act
		var result = CarromRuleEngine.CheckWinCondition_Doubles(player1, player2);

		// Assert
		result.HasWon.ShouldBeTrue();
	}

	[Test]
	public void CheckWinCondition_Doubles_IncompletePieces_ReturnsNotWon()
	{
		// Arrange
		var player1 = CreateTestPlayer(PieceType.White, piecesPocketed: 4);
		var player2 = CreateTestPlayer(PieceType.White, piecesPocketed: 3);

		// Act
		var result = CarromRuleEngine.CheckWinCondition_Doubles(player1, player2);

		// Assert
		result.HasWon.ShouldBeFalse();
	}

	[Test]
	public void CheckWinCondition_Doubles_TeamQueenNotCovered_ReturnsNotWon()
	{
		// Arrange
		var player1 = CreateTestPlayer(PieceType.White, piecesPocketed: 6, hasQueen: true);
		var player2 = CreateTestPlayer(PieceType.White, piecesPocketed: 3);

		// Act
		var result = CarromRuleEngine.CheckWinCondition_Doubles(player1, player2);

		// Assert - Has queen but not covered
		result.HasWon.ShouldBeFalse();
	}

	// ================================================================
	// PIECE RETURN TESTS
	// ================================================================

	[Test]
	public void SelectPieceToReturn_PreferredTypeAvailable_ReturnsPreferredType()
	{
		// Arrange
		var playerState = new PlayerState
		{
			PocketedPieces = { PieceType.White, PieceType.White, PieceType.White }
		};

		// Act
		var pieceType = CarromRuleEngine.SelectPieceToReturn(playerState, PieceType.White);

		// Assert
		pieceType.ShouldBe(PieceType.White);
	}

	[Test]
	public void SelectPieceToReturn_NoPiecesAvailable_ReturnsNull()
	{
		// Arrange
		var playerState = new PlayerState { PocketedPieces = { } };

		// Act
		var pieceType = CarromRuleEngine.SelectPieceToReturn(playerState, PieceType.White);

		// Assert
		pieceType.ShouldBeNull();
	}

	[Test]
	public void ApplyPieceReturn_RemovesPieceFromList()
	{
		// Arrange
		var playerState = new PlayerState
		{
			PiecesPocketed = 3,
			PocketedPieces = { PieceType.White, PieceType.White, PieceType.White }
		};

		// Act
		CarromRuleEngine.ApplyPieceReturn(playerState, PieceType.White);

		// Assert
		playerState.PiecesPocketed.ShouldBe(2);
		playerState.PocketedPieces.Count.ShouldBe(2);
	}

	// ================================================================
	// STATISTICS HELPER TESTS
	// ================================================================

	[Test]
	public void CalculateAccuracy_WithShots_ReturnsCorrectPercentage()
	{
		// Act
		var accuracy = CarromRuleEngine.CalculateAccuracy(validPockets: 7, totalShots: 10);

		// Assert
		accuracy.ShouldBe(0.7f, tolerance: 0.001f);
	}

	[Test]
	public void CalculateAccuracy_NoShots_ReturnsZero()
	{
		// Act
		var accuracy = CarromRuleEngine.CalculateAccuracy(validPockets: 0, totalShots: 0);

		// Assert
		accuracy.ShouldBe(0.0f);
	}

	[Test]
	public void CalculateScore_WithPiecesOnly_ReturnsCorrectScore()
	{
		// Arrange
		var playerState = CreateTestPlayer(PieceType.White, piecesPocketed: 5);

		// Act
		var score = CarromRuleEngine.CalculateScore(playerState);

		// Assert
		score.ShouldBe(5);
	}

	[Test]
	public void CalculateScore_WithCoveredQueen_AddsThreePoints()
	{
		// Arrange
		var playerState = CreateTestPlayer(
			PieceType.White,
			piecesPocketed: 5,
			hasQueen: true,
			queenCovered: true);

		// Act
		var score = CarromRuleEngine.CalculateScore(playerState);

		// Assert
		score.ShouldBe(8); // 5 pieces + 3 for queen
	}

	[Test]
	public void CalculateScore_WithUncoveredQueen_NoQueenPoints()
	{
		// Arrange
		var playerState = CreateTestPlayer(
			PieceType.White,
			piecesPocketed: 5,
			hasQueen: true,
			queenCovered: false);

		// Act
		var score = CarromRuleEngine.CalculateScore(playerState);

		// Assert
		score.ShouldBe(5); // Only pieces, no queen points
	}

	// ================================================================
	// HELPER METHODS
	// ================================================================

	private PlayerState CreateTestPlayer(
		PieceType assignedPieceType,
		int piecesPocketed = 0,
		bool hasQueen = false,
		bool queenCovered = false)
	{
		return new PlayerState
		{
			PlayerId = "test_player",
			AssignedPieceType = assignedPieceType,
			PiecesPocketed = piecesPocketed,
			HasQueen = hasQueen,
			QueenCovered = queenCovered,
			TotalShots = piecesPocketed > 0 ? piecesPocketed + 2 : 0,
			ValidPockets = piecesPocketed,
			Fouls = 0,
			PocketedPieces = new()
		};
	}
}
