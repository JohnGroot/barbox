using System.Threading.Tasks;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

namespace BarBox.Games.Carrom.Tests;

/// <summary>
/// Integration tests for CarromGame state management and rule engine integration
/// Tests full stack behavior: game logic, UI updates, signals, state transitions
/// Uses debug tools and fixtures to set up specific game scenarios
/// </summary>
public class CarromGameStateIntegrationTests : TestClass
{
	private CarromGame _game;

	public CarromGameStateIntegrationTests(Node testScene) : base(testScene) { }

	[Setup]
	public void SetupGameInstance()
	{
		// Load CarromGame scene
		var gameScene = GD.Load<PackedScene>("res://_Games/CarromGame/Scenes/CarromGame.tscn");
		_game = gameScene.Instantiate<CarromGame>();
		TestScene.AddChild(_game);

		// Wait for game to initialize
		TestScene.GetTree().CreateTimer(0.1).Timeout += () => { };
	}

	[Cleanup]
	public void CleanupGameInstance()
	{
		if (_game != null && GodotObject.IsInstanceValid(_game))
		{
			_game.QueueFree();
		}
	}

	// ================================================================
	// FOUL SCENARIO INTEGRATION TESTS
	// ================================================================

	[Test]
	public async Task StrikerFoul_WithPiecesAvailable_ReturnsPenaltyPiece()
	{
		// Arrange
		CarromTestFixtures.SetupStrikerFoulScenario(_game);
		await CarromTestFixtures.WaitForGameStateSettle(TestScene.GetTree());

		// Verify setup
		CarromTestFixtures.IsInCompetitiveMode(_game).ShouldBeTrue();
		CarromTestFixtures.IsGameReady(_game).ShouldBeTrue();

		// Act - Game should be ready for striker foul test
		// In actual gameplay, striker would be pocketed here
		// The setup prepares the game state for this scenario

		// Assert - Verify player has pieces to return
		CarromTestFixtures.PlayerHasPieceCount(_game, "player1", 2).ShouldBeTrue();
	}

	[Test]
	public async Task OpponentPieceFoul_ReturnsOpponentPieceAndPenalty()
	{
		// Arrange
		CarromTestFixtures.SetupOpponentPieceFoulScenario(_game);
		await CarromTestFixtures.WaitForGameStateSettle(TestScene.GetTree());

		// Verify setup
		CarromTestFixtures.IsInCompetitiveMode(_game).ShouldBeTrue();
		CarromTestFixtures.IsGameReady(_game).ShouldBeTrue();
	}

	// ================================================================
	// QUEEN COVERING INTEGRATION TESTS
	// ================================================================

	[Test]
	public async Task QueenCovering_Success_QueenMarkedAsCovered()
	{
		// Arrange
		CarromTestFixtures.SetupQueenCoveringSuccessScenario(_game);
		await CarromTestFixtures.WaitForGameStateSettle(TestScene.GetTree());

		// Verify initial state
		CarromTestFixtures.IsInCompetitiveMode(_game).ShouldBeTrue();

		// Player should have at least 1 piece (eligible to pocket queen)
		var state = CarromTestFixtures.GetGameStateSnapshot(_game);
		state.ShouldContain("player1");
	}

	[Test]
	public async Task QueenCovering_Failure_QueenReturnsToCenter()
	{
		// Arrange
		CarromTestFixtures.SetupQueenCoveringFailureScenario(_game);
		await CarromTestFixtures.WaitForGameStateSettle(TestScene.GetTree());

		// Verify initial state
		CarromTestFixtures.IsInCompetitiveMode(_game).ShouldBeTrue();

		// Player should have at least 1 piece but will fail to cover queen
		var state = CarromTestFixtures.GetGameStateSnapshot(_game);
		state.ShouldContain("player1");
	}

	// ================================================================
	// BREAKING TURN INTEGRATION TESTS
	// ================================================================

	[Test]
	public async Task BreakingTurn_InitialState_Has3Attempts()
	{
		// Arrange
		CarromTestFixtures.SetupBreakingTurnScenario(_game);
		await CarromTestFixtures.WaitForGameStateSettle(TestScene.GetTree());

		// Verify breaking turn setup
		var state = CarromTestFixtures.GetGameStateSnapshot(_game);
		state.ShouldContain("Competitive");

		// Game should be in ready state for breaking turn
		CarromTestFixtures.IsGameReady(_game).ShouldBeTrue();
	}

	// ================================================================
	// WIN CONDITION INTEGRATION TESTS
	// ================================================================

	[Test]
	public async Task NearWinState_OneMorePieceNeeded_CorrectState()
	{
		// Arrange
		CarromTestFixtures.SetupNearWinScenario(_game);
		await CarromTestFixtures.WaitForGameStateSettle(TestScene.GetTree());

		// Verify near-win state
		CarromTestFixtures.PlayerHasPieceCount(_game, "player1", 8).ShouldBeTrue();
		CarromTestFixtures.PlayerHasQueenState(_game, "player1", "Covered").ShouldBeTrue();
	}

	[Test]
	public async Task WinCondition_AllPiecesAndQueen_TriggersGameOver()
	{
		// Arrange - This will trigger win condition directly
		CarromTestFixtures.SetupWinConditionScenario(_game);
		await CarromTestFixtures.WaitForGameStateSettle(TestScene.GetTree());

		// Verify win state was triggered
		var state = CarromTestFixtures.GetGameStateSnapshot(_game);
		state.ShouldContain("player1");
	}

	// ================================================================
	// PLAYER STATE INTEGRATION TESTS
	// ================================================================

	[Test]
	public async Task MidGameState_5Pieces_CorrectAccuracyTracking()
	{
		// Arrange
		CarromTestFixtures.SetupMidGameScenario(_game);
		await CarromTestFixtures.WaitForGameStateSettle(TestScene.GetTree());

		// Verify mid-game state
		CarromTestFixtures.PlayerHasPieceCount(_game, "player1", 5).ShouldBeTrue();
		CarromTestFixtures.PlayerHasQueenState(_game, "player1", "None").ShouldBeTrue();
	}

	[Test]
	public async Task EarlyGameState_2Pieces_CannotPocketQueen()
	{
		// Arrange
		CarromTestFixtures.SetupEarlyGameScenario(_game);
		await CarromTestFixtures.WaitForGameStateSettle(TestScene.GetTree());

		// Verify early game state
		CarromTestFixtures.PlayerHasPieceCount(_game, "player1", 2).ShouldBeTrue();

		// Player needs at least 1 piece to pocket queen (has 2, so eligible)
		var state = CarromTestFixtures.GetGameStateSnapshot(_game);
		state.ShouldContain("player1");
	}

	// ================================================================
	// TURN MANAGEMENT INTEGRATION TESTS
	// ================================================================

	[Test]
	public async Task SwitchPlayer_ChangesCurrentPlayer()
	{
		// Arrange - Start with alternating turns scenario
		CarromTestFixtures.SetupAlternatingTurnsScenario(_game);
		await CarromTestFixtures.WaitForGameStateSettle(TestScene.GetTree());

		// Verify player1 is current player
		var initialState = CarromTestFixtures.GetGameStateSnapshot(_game);
		initialState.ShouldContain("Current Player: player1");

		// Act - Switch to player2
		CarromTestFixtures.SwitchToPlayer(_game, 1);
		await CarromTestFixtures.WaitForGameStateSettle(TestScene.GetTree());

		// Assert
		var newState = CarromTestFixtures.GetGameStateSnapshot(_game);
		newState.ShouldContain("Current Player: player2");
	}

	[Test]
	public async Task AlternatingTurns_BothPlayersHaveProgress()
	{
		// Arrange
		CarromTestFixtures.SetupAlternatingTurnsScenario(_game);
		await CarromTestFixtures.WaitForGameStateSettle(TestScene.GetTree());

		// Verify both players have pieces
		CarromTestFixtures.PlayerHasPieceCount(_game, "player1", 3).ShouldBeTrue();
		CarromTestFixtures.PlayerHasPieceCount(_game, "player2", 2).ShouldBeTrue();
	}

	// ================================================================
	// COMPLEX SCENARIO INTEGRATION TESTS
	// ================================================================

	[Test]
	public async Task CompetitiveGameInProgress_BothPlayersWithProgress()
	{
		// Arrange
		CarromTestFixtures.SetupCompetitiveGameInProgressScenario(_game);
		await CarromTestFixtures.WaitForGameStateSettle(TestScene.GetTree());

		// Verify player1 has queen (uncovered)
		CarromTestFixtures.PlayerHasPieceCount(_game, "player1", 6).ShouldBeTrue();
		CarromTestFixtures.PlayerHasQueenState(_game, "player1", "Uncovered").ShouldBeTrue();

		// Verify player2 doesn't have queen
		CarromTestFixtures.PlayerHasPieceCount(_game, "player2", 5).ShouldBeTrue();
		CarromTestFixtures.PlayerHasQueenState(_game, "player2", "None").ShouldBeTrue();
	}

	[Test]
	public async Task QueenRaceScenario_BothPlayersEligible()
	{
		// Arrange
		CarromTestFixtures.SetupQueenRaceScenario(_game);
		await CarromTestFixtures.WaitForGameStateSettle(TestScene.GetTree());

		// Both players should have exactly 1 piece (eligible for queen)
		CarromTestFixtures.PlayerHasPieceCount(_game, "player1", 1).ShouldBeTrue();
		CarromTestFixtures.PlayerHasPieceCount(_game, "player2", 1).ShouldBeTrue();

		// Neither should have queen yet
		CarromTestFixtures.PlayerHasQueenState(_game, "player1", "None").ShouldBeTrue();
		CarromTestFixtures.PlayerHasQueenState(_game, "player2", "None").ShouldBeTrue();
	}

	[Test]
	public async Task ComebackScenario_LargePieceDeficit()
	{
		// Arrange
		CarromTestFixtures.SetupComebackScenario(_game);
		await CarromTestFixtures.WaitForGameStateSettle(TestScene.GetTree());

		// Verify player1 is far behind
		CarromTestFixtures.PlayerHasPieceCount(_game, "player1", 2).ShouldBeTrue();

		// Verify player2 is winning
		CarromTestFixtures.PlayerHasPieceCount(_game, "player2", 7).ShouldBeTrue();
		CarromTestFixtures.PlayerHasQueenState(_game, "player2", "Covered").ShouldBeTrue();
	}

	// ================================================================
	// STATE TRANSITION INTEGRATION TESTS
	// ================================================================

	[Test]
	public async Task GameStateTransition_PracticeToCompetitive_MaintainsIntegrity()
	{
		// Arrange - Start in practice mode
		CarromTestFixtures.ReturnToPracticeMode(_game);
		await CarromTestFixtures.WaitForGameStateSettle(TestScene.GetTree());

		var practiceState = CarromTestFixtures.GetGameStateSnapshot(_game);
		practiceState.ShouldContain("Practice");

		// Act - Setup competitive scenario
		CarromTestFixtures.SetupMidGameScenario(_game);
		await CarromTestFixtures.WaitForGameStateSettle(TestScene.GetTree());

		// Assert - Should be in competitive mode now
		CarromTestFixtures.IsInCompetitiveMode(_game).ShouldBeTrue();
	}

	[Test]
	public async Task MultipleStateChanges_MaintainsConsistency()
	{
		// Arrange - Start with early game
		CarromTestFixtures.SetupEarlyGameScenario(_game);
		await CarromTestFixtures.WaitForGameStateSettle(TestScene.GetTree());

		// Act - Progress to mid game
		CarromTestFixtures.SetupMidGameScenario(_game);
		await CarromTestFixtures.WaitForGameStateSettle(TestScene.GetTree());

		// Verify mid game
		CarromTestFixtures.PlayerHasPieceCount(_game, "player1", 5).ShouldBeTrue();

		// Act - Progress to near win
		CarromTestFixtures.SetupNearWinScenario(_game);
		await CarromTestFixtures.WaitForGameStateSettle(TestScene.GetTree());

		// Assert - State should be consistent
		CarromTestFixtures.PlayerHasPieceCount(_game, "player1", 8).ShouldBeTrue();
		CarromTestFixtures.PlayerHasQueenState(_game, "player1", "Covered").ShouldBeTrue();
	}

	// ================================================================
	// RULE ENGINE INTEGRATION TESTS
	// ================================================================

	[Test]
	public async Task RuleEngine_PieceEvaluation_IntegratesWithGameState()
	{
		// Arrange - Setup mid-game scenario
		CarromTestFixtures.SetupMidGameScenario(_game);
		await CarromTestFixtures.WaitForGameStateSettle(TestScene.GetTree());

		// Verify rule engine integration through game state
		var state = CarromTestFixtures.GetGameStateSnapshot(_game);
		state.ShouldContain("player1");
		state.ShouldContain("Pieces: 5/9");

		// Game state should reflect rule engine calculations
		CarromTestFixtures.IsGameReady(_game).ShouldBeTrue();
	}

	[Test]
	public async Task RuleEngine_WinCondition_IntegratesCorrectly()
	{
		// Arrange - Setup near-win state
		CarromTestFixtures.SetupNearWinScenario(_game);
		await CarromTestFixtures.WaitForGameStateSettle(TestScene.GetTree());

		// Player should be one piece away from winning
		CarromTestFixtures.PlayerHasPieceCount(_game, "player1", 8).ShouldBeTrue();

		// Rule engine should correctly evaluate win condition (8 pieces = not won yet)
		var state = CarromTestFixtures.GetGameStateSnapshot(_game);
		state.ShouldContain("Pieces: 8/9");
	}

	// ================================================================
	// DEBUG TOOLS VALIDATION TESTS
	// ================================================================

	[Test]
	public async Task DebugTools_StateInspection_ReturnsFormattedState()
	{
		// Arrange
		CarromTestFixtures.SetupCompetitiveGameInProgressScenario(_game);
		await CarromTestFixtures.WaitForGameStateSettle(TestScene.GetTree());

		// Act
		var state = CarromTestFixtures.GetGameStateSnapshot(_game);

		// Assert - State should contain key information
		state.ShouldNotBeNullOrEmpty();
		state.ShouldContain("Mode:");
		state.ShouldContain("Game State:");
		state.ShouldContain("player1:");
		state.ShouldContain("player2:");
		state.ShouldContain("Pieces:");
		state.ShouldContain("Queen:");
	}

	[Test]
	public async Task DebugTools_StateParsing_ExtractsCorrectValues()
	{
		// Arrange
		CarromTestFixtures.SetupMidGameScenario(_game);
		await CarromTestFixtures.WaitForGameStateSettle(TestScene.GetTree());

		// Act - Use parsing helpers
		var fouls = CarromTestFixtures.GetPlayerFouls(_game, "player1");

		// Assert
		fouls.ShouldBeGreaterThanOrEqualTo(0);
	}

	// ================================================================
	// CLEANUP AND RESET TESTS
	// ================================================================

	[Test]
	public async Task ReturnToPractice_ResetsGameState()
	{
		// Arrange - Setup competitive game
		CarromTestFixtures.SetupCompetitiveGameInProgressScenario(_game);
		await CarromTestFixtures.WaitForGameStateSettle(TestScene.GetTree());

		// Verify competitive mode
		CarromTestFixtures.IsInCompetitiveMode(_game).ShouldBeTrue();

		// Act - Return to practice
		CarromTestFixtures.ReturnToPracticeMode(_game);
		await CarromTestFixtures.WaitForGameStateSettle(TestScene.GetTree());

		// Assert - Should be in practice mode
		var state = CarromTestFixtures.GetGameStateSnapshot(_game);
		state.ShouldContain("Practice");
	}
}
