using System.Collections.Generic;
using System.Linq;
using Godot;

namespace BarBox.Games.Carrom;

public partial class CarromGame : GameController
{
	// ================================================================
	// DEBUG METHODS FOR EDITOR TESTING
	// ================================================================

	// ================================================================
	// HELPER METHODS FOR PHYSICAL PIECE MANIPULATION
	// ================================================================

	/// <summary>
	/// Get visible pieces of a specific type for debug manipulation
	/// </summary>
	private List<CarromPiece> GetVisiblePiecesOfType(PieceType type)
	{
		return _pieceFactory.GetPiecesOfType(type)
			.Where(p => GodotObject.IsInstanceValid(p) && p.Visible)
			.ToList();
	}

	/// <summary>
	/// DEBUG: Pocket pieces physically (hides from board, freezes physics)
	/// </summary>
	private void DEBUG_PocketPiecesPhysically(PieceType type, int count)
	{
		var pieces = GetVisiblePiecesOfType(type).Take(count);
		int pocketed = 0;

		foreach (var piece in pieces)
		{
			piece.PocketPiece();  // Hides piece, freezes physics
			pocketed++;
		}

		GD.Print($"[DEBUG] Pocketed {pocketed} {type} pieces physically");
	}

	/// <summary>
	/// DEBUG: Return piece to center (for fouls, queen returns)
	/// </summary>
	private void DEBUG_ReturnPieceToCenter(PieceType type)
	{
		var piece = _pieceFactory.GetPiecesOfType(type)
			.FirstOrDefault(p => GodotObject.IsInstanceValid(p) && !p.Visible);

		if (piece != null)
		{
			// Restore existing hidden piece
			float radius = _board.PieceRadius;
			Vector2 safePos = _board.FindValidPositionNearCenter(radius, piece);
			piece.Reset(safePos, immediate: true);
			piece.Visible = true;
			piece.Freeze = false;
			GD.Print($"[DEBUG] Returned {type} piece to center at {safePos}");
		}
		else
		{
			GD.Print($"[DEBUG] No hidden {type} piece found to return to center");
		}
	}

	/// <summary>
	/// DEBUG: Validation - compare physical vs recorded state
	/// </summary>
	public void DEBUG_ValidateBoardState()
	{
		if (_pieceFactory == null)
		{
			GD.Print("[DEBUG] Piece factory not available");
			return;
		}

		var allPieces = _pieceFactory.GetAllPieces();
		var visiblePieces = allPieces.Where(p => GodotObject.IsInstanceValid(p) && p.Visible).ToList();
		var pocketedPieces = allPieces.Where(p => GodotObject.IsInstanceValid(p) && !p.Visible).ToList();

		GD.Print($"=== DEBUG Board State ===");
		GD.Print($"Total pieces: {allPieces.Count}");
		GD.Print($"Visible: {visiblePieces.Count}");
		GD.Print($"Pocketed: {pocketedPieces.Count}");

		if (_carromGameMode == CarromGameMode.Competitive && _competitiveModeManager != null)
		{
			foreach (var player in _competitiveModeManager.GetPlayers())
			{
				var onBoard = visiblePieces.Count(p => p.Type == player.AssignedPieceType);
				GD.Print($"{player.PlayerId} ({player.AssignedPieceType}): {onBoard} visible, {player.PiecesPocketed} recorded");
			}
		}
	}

	/// <summary>
	/// DEBUG: Setup a foul scenario for testing foul detection and penalties
	/// </summary>
	public void DEBUG_SetupFoulScenario(FoulType foulType)
	{
		if (_carromGameMode != CarromGameMode.Competitive || _competitiveModeManager == null)
		{
			GD.PrintErr("[DEBUG] Cannot setup foul scenario - not in competitive mode");
			return;
		}

		GD.Print($"[DEBUG] Setting up foul scenario: {foulType}");

		var currentPlayer = _competitiveModeManager.GetCurrentPlayer();
		if (currentPlayer == null)
		{
			GD.PrintErr("[DEBUG] No current player found");
			return;
		}

		switch (foulType)
		{
			case FoulType.StrikerPocketed:
				// Physically pocket pieces to create inventory for penalty
				DEBUG_PocketPiecesPhysically(currentPlayer.AssignedPieceType, 2);
				currentPlayer.RecordPocketedPiece(currentPlayer.AssignedPieceType);
				currentPlayer.RecordPocketedPiece(currentPlayer.AssignedPieceType);
				GD.Print("[DEBUG] ✓ Striker foul scenario ready - pocket striker to test penalty");
				break;

			case FoulType.OpponentPiecePocketed:
				// Physically pocket an opponent piece to simulate the foul
				var opponentType = currentPlayer.AssignedPieceType == PieceType.White
					? PieceType.Black : PieceType.White;
				DEBUG_PocketPiecesPhysically(opponentType, 1);
				GD.Print("[DEBUG] ✓ Opponent piece foul scenario ready - opponent piece pocketed");
				break;

			case FoulType.ImproperQueenPocketing:
				// Physically pocket queen to simulate improper pocketing
				DEBUG_PocketPiecesPhysically(PieceType.Red, 1);
				currentPlayer.RecordPocketedPiece(PieceType.Red);
				GD.Print("[DEBUG] ✓ Improper queen pocketing scenario ready - queen already pocketed");
				break;
		}

		// Let GameStateManager handle state transitions automatically
		GD.Print($"[DEBUG] Foul scenario setup complete: {foulType}");
	}

	/// <summary>
	/// DEBUG: Setup queen covering test scenario
	/// </summary>
	public void DEBUG_SetupQueenCoveringTest(bool shouldCover)
	{
		if (_carromGameMode != CarromGameMode.Competitive || _competitiveModeManager == null)
		{
			GD.PrintErr("[DEBUG] Cannot setup queen covering test - not in competitive mode");
			return;
		}

		var currentPlayer = _competitiveModeManager.GetCurrentPlayer();
		if (currentPlayer == null)
		{
			GD.PrintErr("[DEBUG] No current player found");
			return;
		}

		GD.Print($"[DEBUG] Setting up queen covering test - Should Cover: {shouldCover}");

		// Ensure queen is visible on board
		var queens = _pieceFactory.GetPiecesOfType(PieceType.Red);
		var queen = queens.FirstOrDefault(p => GodotObject.IsInstanceValid(p));

		if (queen != null && !queen.Visible)
		{
			// Return hidden queen to center
			DEBUG_ReturnPieceToCenter(PieceType.Red);
		}

		// Give player some pocketed pieces for covering test (makes them eligible)
		DEBUG_PocketPiecesPhysically(currentPlayer.AssignedPieceType, 3);
		for (int i = 0; i < 3; i++)
		{
			currentPlayer.RecordPocketedPiece(currentPlayer.AssignedPieceType);
		}

		GD.Print("[DEBUG] ✓ Queen covering scenario ready");
		GD.Print("[DEBUG]   1. Pocket the red queen");
		if (shouldCover)
		{
			GD.Print("[DEBUG]   2. Immediately pocket your assigned piece to test successful covering");
		}
		else
		{
			GD.Print("[DEBUG]   2. Don't pocket your piece - test queen return on turn end");
		}
	}

	/// <summary>
	/// DEBUG: Force player state for testing specific scenarios
	/// </summary>
	public void DEBUG_ForcePlayerState(string playerId, int piecesPocketed, bool hasQueen, bool queenCovered)
	{
		if (_carromGameMode != CarromGameMode.Competitive || _competitiveModeManager == null)
		{
			GD.PrintErr("[DEBUG] Cannot force player state - not in competitive mode");
			return;
		}

		var players = _competitiveModeManager.GetPlayers();
		var player = players?.FirstOrDefault(p => p.PlayerId == playerId);

		if (player == null)
		{
			GD.PrintErr($"[DEBUG] Player {playerId} not found");
			return;
		}

		GD.Print($"[DEBUG] Forcing player state: {playerId} - Pieces: {piecesPocketed}, Queen: {hasQueen}, Covered: {queenCovered}");

		// Calculate how many pieces need to be physically removed
		int currentlyPocketed = player.PiecesPocketed;
		int needToPocket = piecesPocketed - currentlyPocketed;

		if (needToPocket > 0)
		{
			// Pocket additional pieces to match target state
			DEBUG_PocketPiecesPhysically(player.AssignedPieceType, needToPocket);

			// Update state to match
			for (int i = 0; i < needToPocket; i++)
			{
				player.RecordPocketedPiece(player.AssignedPieceType);
			}
		}
		else if (needToPocket < 0)
		{
			GD.PrintErr($"[DEBUG] Cannot reduce pocketed count (currently {currentlyPocketed}, target {piecesPocketed})");
		}

		// Update queen state
		player.HasQueen = hasQueen;
		player.QueenCovered = queenCovered;

		// Update UI
		if (_scoreDisplay != null)
		{
			_scoreDisplay.UpdateAllPlayerScores(players);
		}

		GD.Print($"[DEBUG] ✓ Player state updated: {piecesPocketed} pieces, Queen: {hasQueen}/{queenCovered}");
	}

	/// <summary>
	/// DEBUG: Set current player by index
	/// </summary>
	public void DEBUG_SetCurrentPlayer(int playerIndex)
	{
		if (_carromGameMode != CarromGameMode.Competitive || _competitiveModeManager == null)
		{
			GD.PrintErr("[DEBUG] Cannot set current player - not in competitive mode");
			return;
		}

		var players = _competitiveModeManager.GetPlayers();
		if (playerIndex < 0 || playerIndex >= players.Count)
		{
			GD.PrintErr($"[DEBUG] Invalid player index: {playerIndex} (total players: {players.Count})");
			return;
		}

		GD.Print($"[DEBUG] Setting current player to index: {playerIndex}");

		// Directly set the current player index in the competitive mode manager
		_competitiveModeManager.DEBUG_SetCurrentPlayerIndex(playerIndex);

		// Update input controller
		_inputController?.SetCurrentPlayer(playerIndex);

		// Update camera and striker
		_cameraController?.TransitionToPlayerWithZoom(playerIndex, 1.2f);
		TweenStrikerToBaseline(playerIndex);

		// Update UI
		if (_scoreDisplay != null)
		{
			_scoreDisplay.UpdateAllPlayerScores(players);
		}
	}

	/// <summary>
	/// DEBUG: Trigger win condition for testing game over flow
	/// </summary>
	public void DEBUG_TriggerWinCondition(string playerId)
	{
		if (_carromGameMode != CarromGameMode.Competitive || _competitiveModeManager == null)
		{
			GD.PrintErr("[DEBUG] Cannot trigger win condition - not in competitive mode");
			return;
		}

		var players = _competitiveModeManager.GetPlayers();
		var player = players?.FirstOrDefault(p => p.PlayerId == playerId);

		if (player == null)
		{
			GD.PrintErr($"[DEBUG] Player {playerId} not found");
			return;
		}

		GD.Print($"[DEBUG] Triggering win condition for: {playerId}");

		// Physically pocket all player's pieces
		DEBUG_PocketPiecesPhysically(player.AssignedPieceType, 9);

		// Physically pocket the queen
		DEBUG_PocketPiecesPhysically(PieceType.Red, 1);

		// Set player to winning state (all pieces + covered queen)
		player.PiecesPocketed = 9;
		player.HasQueen = true;
		player.QueenCovered = true;

		// Trigger win event
		OnPlayerWon(playerId);

		GD.Print($"[DEBUG] ✓ Win condition triggered - game over screen should appear");
	}

	/// <summary>
	/// DEBUG: Reset to breaking turn for testing breaking turn logic
	/// </summary>
	public void DEBUG_ResetToBreakingTurn()
	{
		if (_carromGameMode != CarromGameMode.Competitive || _competitiveModeManager == null)
		{
			GD.PrintErr("[DEBUG] Cannot reset to breaking turn - not in competitive mode");
			return;
		}

		GD.Print("[DEBUG] Resetting to breaking turn");

		// Clean up current competitive mode
		_competitiveModeManager.CleanupMode();

		// Re-setup competitive mode (this resets breaking turn state)
		_competitiveModeManager.SetupCompetitiveMode();

		// Setup state machine
		SetupStateMachineForCurrentMode();

		GD.Print("[DEBUG] Breaking turn setup complete - 3 attempts available to disturb pieces");
	}

	/// <summary>
	/// DEBUG: Get current game state for inspection
	/// </summary>
	public string DEBUG_GetGameState()
	{
		var state = new System.Text.StringBuilder();
		state.AppendLine("=== CARROM GAME STATE ===");
		state.AppendLine($"Mode: {_carromGameMode}");
		state.AppendLine($"Game State: {_gameStateManager?.CurrentState}");
		state.AppendLine($"Can Accept Input: {_gameStateManager?.CanAcceptInput}");

		if (_carromGameMode == CarromGameMode.Competitive && _competitiveModeManager != null)
		{
			var players = _competitiveModeManager.GetPlayers();
			var currentPlayer = _competitiveModeManager.GetCurrentPlayer();

			state.AppendLine($"\nCurrent Player: {currentPlayer?.PlayerId}");
			state.AppendLine("\nPLAYER STATES:");

			foreach (var player in players)
			{
				state.AppendLine($"  {player.PlayerId}:");
				state.AppendLine($"    Pieces: {player.PiecesPocketed}/9");
				state.AppendLine($"    Queen: {(player.HasQueen ? (player.QueenCovered ? "Covered" : "Uncovered") : "None")}");
				state.AppendLine($"    Accuracy: {player.GetAccuracy():P1}");
				state.AppendLine($"    Fouls: {player.GetFouls()}");
			}
		}

		state.AppendLine("========================");
		return state.ToString();
	}

	/// <summary>
	/// DEBUG: Return to practice mode without confirmation
	/// </summary>
	public void DEBUG_ReturnToPractice()
	{
		GD.Print("[DEBUG] Returning to practice mode (no confirmation)");

		if (IsRoundActive())
		{
			EndRound();
		}

		StartPracticeMode();
		RefreshUI();
	}

	
	/// <summary>
	/// DEBUG: Directly activate competitive mode for testing, bypassing UI menu
	/// </summary>
	public void DEBUG_ActivateCompetitiveMode(int playerCount = 2)
	{
		if (IsRoundActive())
		{
			EndRound();
		}

		// Clean up practice mode before switching
		_practiceModeManager?.CleanupMode();

		// Configure player count
		_competitiveModeManager?.SetPlayerCount(playerCount);

		// Set current mode manager to competitive
		_currentModeManager = _competitiveModeManager;
		_carromGameMode = CarromGameMode.Competitive;
		ResetGame();
		StartRound();

		// Delegate to competitive mode manager
		bool success = _competitiveModeManager.StartCompetitiveMode();
		if (!success)
		{
			GD.PrintErr("[DEBUG] Failed to activate competitive mode");
			return;
		}

		// Setup state machine for competitive mode
		SetupStateMachineForCurrentMode();

		// Show score display and notification system
		_scoreDisplay?.SetVisible(true);
		_notificationSystem?.Show();

		GD.Print($"[DEBUG] Competitive mode activated with {playerCount} players");
	}
	
	
	
	/// <summary>
	/// DEBUG METHOD: Force enable input for debugging stuck input states
	/// Call this method from debugger or add temporary UI button during development
	/// </summary>
	public void DEBUG_ForceEnableInput()
	{
		// DEBUG: Force enabling input for debugging
		
		// State machine handles transitions automatically
		
		// Force input controller to enabled state
		if (_inputController != null)
		{
			// This assumes SetInputState method exists - may need to adjust based on actual InputController API
			// DEBUG: Attempting to force input controller to enabled state
			
			// Re-establish all references
			_inputController.SetGameState(_gameStateManager);
			UpdateInputControllerStriker();
			
			// DEBUG: Re-established input controller references
		}
	}

	/// <summary>
	/// DEBUG METHOD: Test camera rotation manually
	/// </summary>
	public void DEBUG_TestCameraRotation(int playerIndex = 1, float duration = 1.0f)
	{
		_cameraController?.RotateToPlayer(playerIndex, duration);
	}

}