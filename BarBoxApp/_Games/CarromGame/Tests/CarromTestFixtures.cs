using System.Threading.Tasks;
using Godot;

namespace BarBox.Games.Carrom.Tests;

/// <summary>
/// Test fixtures for setting up CarromGame scenarios using debug methods
/// Provides reusable setup patterns for integration testing
/// </summary>
public static class CarromTestFixtures
{
	// ================================================================
	// FOUL SCENARIO FIXTURES
	// ================================================================

	/// <summary>
	/// Setup striker foul scenario - player has pieces to return as penalty
	/// </summary>
	public static void SetupStrikerFoulScenario(CarromGame game)
	{
		game.DEBUG_ActivateCompetitiveMode(playerCount: 2);
		game.DEBUG_SetupFoulScenario(FoulType.StrikerPocketed);
	}

	/// <summary>
	/// Setup opponent piece foul scenario
	/// </summary>
	public static void SetupOpponentPieceFoulScenario(CarromGame game)
	{
		game.DEBUG_ActivateCompetitiveMode(playerCount: 2);
		game.DEBUG_SetupFoulScenario(FoulType.OpponentPiecePocketed);
	}

	// ================================================================
	// QUEEN COVERING FIXTURES
	// ================================================================

	/// <summary>
	/// Setup queen covering success scenario
	/// Player has at least 1 piece, ready to pocket queen and cover it
	/// </summary>
	public static void SetupQueenCoveringSuccessScenario(CarromGame game)
	{
		game.DEBUG_ActivateCompetitiveMode(playerCount: 2);
		game.DEBUG_SetupQueenCoveringTest(shouldCover: true);
	}

	/// <summary>
	/// Setup queen covering failure scenario
	/// Player has at least 1 piece, will pocket queen but fail to cover
	/// </summary>
	public static void SetupQueenCoveringFailureScenario(CarromGame game)
	{
		game.DEBUG_ActivateCompetitiveMode(playerCount: 2);
		game.DEBUG_SetupQueenCoveringTest(shouldCover: false);
	}

	// ================================================================
	// BREAKING TURN FIXTURES
	// ================================================================

	/// <summary>
	/// Reset to breaking turn - start of competitive game
	/// </summary>
	public static void SetupBreakingTurnScenario(CarromGame game)
	{
		game.DEBUG_ActivateCompetitiveMode(playerCount: 2);
		game.DEBUG_ResetToBreakingTurn();
	}

	// ================================================================
	// PLAYER STATE FIXTURES
	// ================================================================

	/// <summary>
	/// Setup near-win scenario - player at 8 pieces + covered queen
	/// </summary>
	public static void SetupNearWinScenario(CarromGame game, string playerId = "player1")
	{
		game.DEBUG_ActivateCompetitiveMode(playerCount: 2);
		game.DEBUG_ForcePlayerState(playerId, piecesPocketed: 8, hasQueen: true, queenCovered: true);
	}

	/// <summary>
	/// Setup mid-game scenario - player at 5 pieces
	/// </summary>
	public static void SetupMidGameScenario(CarromGame game, string playerId = "player1")
	{
		game.DEBUG_ActivateCompetitiveMode(playerCount: 2);
		game.DEBUG_ForcePlayerState(playerId, piecesPocketed: 5, hasQueen: false, queenCovered: false);
	}

	/// <summary>
	/// Setup early game scenario - player at 2 pieces
	/// </summary>
	public static void SetupEarlyGameScenario(CarromGame game, string playerId = "player1")
	{
		game.DEBUG_ActivateCompetitiveMode(playerCount: 2);
		game.DEBUG_ForcePlayerState(playerId, piecesPocketed: 2, hasQueen: false, queenCovered: false);
	}

	/// <summary>
	/// Setup win condition scenario - player at 9 pieces + covered queen
	/// </summary>
	public static void SetupWinConditionScenario(CarromGame game, string playerId = "player1")
	{
		game.DEBUG_ActivateCompetitiveMode(playerCount: 2);
		game.DEBUG_TriggerWinCondition(playerId);
	}

	// ================================================================
	// TURN MANAGEMENT FIXTURES
	// ================================================================

	/// <summary>
	/// Switch to specified player
	/// </summary>
	public static void SwitchToPlayer(CarromGame game, int playerIndex)
	{
		game.DEBUG_SetCurrentPlayer(playerIndex);
	}

	/// <summary>
	/// Setup alternating turns scenario - player1 at 3 pieces, player2 at 2 pieces
	/// </summary>
	public static void SetupAlternatingTurnsScenario(CarromGame game)
	{
		game.DEBUG_ActivateCompetitiveMode(playerCount: 2);
		game.DEBUG_ForcePlayerState("player1", piecesPocketed: 3, hasQueen: false, queenCovered: false);
		game.DEBUG_SetCurrentPlayer(1); // Switch to player2
		game.DEBUG_ForcePlayerState("player2", piecesPocketed: 2, hasQueen: false, queenCovered: false);
		game.DEBUG_SetCurrentPlayer(0); // Switch back to player1
	}

	// ================================================================
	// COMPLEX SCENARIO FIXTURES
	// ================================================================

	/// <summary>
	/// Setup competitive game with both players having progress
	/// Player1: 6 pieces + uncovered queen
	/// Player2: 5 pieces
	/// </summary>
	public static void SetupCompetitiveGameInProgressScenario(CarromGame game)
	{
		game.DEBUG_ActivateCompetitiveMode(playerCount: 2);

		// Setup player1 with queen
		game.DEBUG_ForcePlayerState("player1", piecesPocketed: 6, hasQueen: true, queenCovered: false);

		// Switch to player2 and setup
		game.DEBUG_SetCurrentPlayer(1);
		game.DEBUG_ForcePlayerState("player2", piecesPocketed: 5, hasQueen: false, queenCovered: false);

		// Switch back to player1
		game.DEBUG_SetCurrentPlayer(0);
	}

	/// <summary>
	/// Setup queen covering race - both players near queen pocket threshold
	/// Player1: 1 piece (can pocket queen)
	/// Player2: 1 piece (can pocket queen)
	/// </summary>
	public static void SetupQueenRaceScenario(CarromGame game)
	{
		game.DEBUG_ActivateCompetitiveMode(playerCount: 2);
		game.DEBUG_ForcePlayerState("player1", piecesPocketed: 1, hasQueen: false, queenCovered: false);
		game.DEBUG_SetCurrentPlayer(1);
		game.DEBUG_ForcePlayerState("player2", piecesPocketed: 1, hasQueen: false, queenCovered: false);
		game.DEBUG_SetCurrentPlayer(0);
	}

	/// <summary>
	/// Setup comeback scenario - player1 far behind
	/// Player1: 2 pieces
	/// Player2: 7 pieces + covered queen
	/// </summary>
	public static void SetupComebackScenario(CarromGame game)
	{
		game.DEBUG_ActivateCompetitiveMode(playerCount: 2);
		game.DEBUG_ForcePlayerState("player1", piecesPocketed: 2, hasQueen: false, queenCovered: false);
		game.DEBUG_SetCurrentPlayer(1);
		game.DEBUG_ForcePlayerState("player2", piecesPocketed: 7, hasQueen: true, queenCovered: true);
		game.DEBUG_SetCurrentPlayer(0);
	}

	// ================================================================
	// VALIDATION HELPERS
	// ================================================================

	/// <summary>
	/// Wait for game state to settle (async operations complete)
	/// </summary>
	public static async Task WaitForGameStateSettle(SceneTree tree, double seconds = 0.5)
	{
		// Wait for specified duration
		await tree.CreateTimer(seconds).ToSignal(tree.CreateTimer(seconds), Timer.SignalName.Timeout);

		// Wait one additional frame for any deferred operations
		await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
	}

	/// <summary>
	/// Get current game state as formatted string for assertions
	/// </summary>
	public static string GetGameStateSnapshot(CarromGame game)
	{
		return game.DEBUG_GetGameState();
	}

	/// <summary>
	/// Return game to practice mode (cleanup between tests)
	/// </summary>
	public static void ReturnToPracticeMode(CarromGame game)
	{
		game.DEBUG_ReturnToPractice();
	}

	// ================================================================
	// SCENARIO VALIDATION HELPERS
	// ================================================================

	/// <summary>
	/// Verify game is in competitive mode with specified player count
	/// </summary>
	public static bool IsInCompetitiveMode(CarromGame game, int expectedPlayerCount = 2)
	{
		var state = game.DEBUG_GetGameState();
		return state.Contains("Mode: Competitive") && state.Contains($"player{expectedPlayerCount}");
	}

	/// <summary>
	/// Verify player has expected piece count
	/// </summary>
	public static bool PlayerHasPieceCount(CarromGame game, string playerId, int expectedCount)
	{
		var state = game.DEBUG_GetGameState();
		return state.Contains($"{playerId}:") && state.Contains($"Pieces: {expectedCount}/9");
	}

	/// <summary>
	/// Verify queen state for player
	/// </summary>
	public static bool PlayerHasQueenState(CarromGame game, string playerId, string expectedState)
	{
		// expectedState: "None", "Uncovered", "Covered"
		var state = game.DEBUG_GetGameState();
		return state.Contains($"{playerId}:") && state.Contains($"Queen: {expectedState}");
	}

	/// <summary>
	/// Verify game is in Ready state (accepting input)
	/// </summary>
	public static bool IsGameReady(CarromGame game)
	{
		var state = game.DEBUG_GetGameState();
		return state.Contains("Game State: Ready") && state.Contains("Can Accept Input: True");
	}

	/// <summary>
	/// Parse player accuracy from game state
	/// </summary>
	public static float GetPlayerAccuracy(CarromGame game, string playerId)
	{
		var state = game.DEBUG_GetGameState();
		var lines = state.Split('\n');

		foreach (var line in lines)
		{
			if (line.Contains($"{playerId}:"))
			{
				// Look for "Accuracy: XX.X%" in subsequent lines
				for (int i = 0; i < lines.Length; i++)
				{
					if (lines[i].Contains($"{playerId}:"))
					{
						// Check next few lines for accuracy
						for (int j = i + 1; j < i + 5 && j < lines.Length; j++)
						{
							if (lines[j].Contains("Accuracy:"))
							{
								var accuracyStr = lines[j].Split(':')[1].Trim().Replace("%", string.Empty);
								if (float.TryParse(accuracyStr, out float accuracy))
								{
									return accuracy;
								}
							}
						}
					}
				}
			}
		}

		return 0.0f;
	}

	/// <summary>
	/// Parse player foul count from game state
	/// </summary>
	public static int GetPlayerFouls(CarromGame game, string playerId)
	{
		var state = game.DEBUG_GetGameState();
		var lines = state.Split('\n');

		foreach (var line in lines)
		{
			if (line.Contains($"{playerId}:"))
			{
				// Look for "Fouls: X" in subsequent lines
				for (int i = 0; i < lines.Length; i++)
				{
					if (lines[i].Contains($"{playerId}:"))
					{
						for (int j = i + 1; j < i + 5 && j < lines.Length; j++)
						{
							if (lines[j].Contains("Fouls:"))
							{
								var foulsStr = lines[j].Split(':')[1].Trim();
								if (int.TryParse(foulsStr, out int fouls))
								{
									return fouls;
								}
							}
						}
					}
				}
			}
		}

		return 0;
	}
}
