using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace BarBox.Games.Carrom;

/// <summary>
/// Manages competitive mode functionality for Carrom game
///
/// ARCHITECTURE NOTES:
/// - Striker positioning: Uses base class PositionStrikerAtBaseline() with player-specific indexing
/// - Phase transitions: Relies on base class ExecuteSettlement() - DO NOT manually transition phases
/// - Settlement flow: Focus on game logic (player switching) and let base class handle infrastructure
/// </summary>
[GlobalClass]
public partial class CarromCompetitiveModeManager : CarromModeManagerBase
{
	[Signal]
	public delegate void TurnChangedEventHandler(string playerId, int turnNumber);

	[Signal]
	public delegate void TurnReadyForPassEventHandler(string playerId, int turnNumber);

	[Signal]
	public delegate void PlayerWonEventHandler(string playerId);

	[Signal]
	public delegate void FoulCommittedEventHandler(string playerId);

	[Signal]
	public delegate void CompetitiveModeSetupCompleteEventHandler();

	[Signal]
	public delegate void NotificationRequestedEventHandler(int notificationType, string message);

	// Reference to GameStateManager for state delegation
	private GameStateManager _gameState;

	// Competitive mode state
	private List<CarromPiece> _competitivePieces = [];
	private List<CarromPlayer> _players = [];

	// Breaking turn state (OFFICIAL CARROM RULE: 3 attempts to break)
	private const int MAX_BREAKING_ATTEMPTS = 3;

	// Piece templates
	private PackedScene _whitePieceTemplate;
	private PackedScene _blackPieceTemplate;
	private PackedScene _redPieceTemplate;
	private PackedScene _strikerTemplate;

	// Penalty piece tween tracking
	private List<CarromPiece> _piecesNeedingTweenReturn = [];

	// Settings
	private int _competitiveCreditCost = 1;
	private int _playerCount = 2; // Can be 2 or 4 players

	// Global analytics tracking
	private SessionManager _sessionManager;

	// UI Dependencies
	private CarromScoreDisplay _scoreDisplay;

	/// <summary>
	/// Initialize with piece templates (competitive-specific version)
	/// Added GameStateManager parameter
	/// </summary>
	public void Initialize(GameStateManager gameState, CarromBoard board, CarromInputController inputController,
						   CarromPhysicsConfig physicsConfig, PackedScene whiteTemplate,
						   PackedScene blackTemplate, PackedScene redTemplate,
						   PackedScene strikerTemplate, int creditCost)
	{
		_gameState = gameState;  // Store reference to single source of truth
		Initialize(board, inputController, physicsConfig);
		_whitePieceTemplate = whiteTemplate;
		_blackPieceTemplate = blackTemplate;
		_redPieceTemplate = redTemplate;
		_strikerTemplate = strikerTemplate;
		_competitiveCreditCost = creditCost;

		// Get session manager for global data tracking
		_sessionManager = SessionManager.GetInstance();
	}

	/// <summary>
	/// Set the score display for direct floating text calls
	/// </summary>
	public void SetScoreDisplay(CarromScoreDisplay scoreDisplay)
	{
		_scoreDisplay = scoreDisplay;
	}

	// ================================================================
	// ABSTRACT METHOD IMPLEMENTATIONS
	// ================================================================
	protected override void CreateModeSpecificPieces()
	{
		_competitivePieces.Clear();

		CreateCompetitivePieces();

		SetupCompetitivePlayers();
	}

	protected override List<CarromPiece> GetManagedPieces()
	{
		return [.. _competitivePieces];
	}

	protected override void ClearModeSpecificPieces()
	{
		foreach (var piece in _competitivePieces)
		{
			if (GodotObject.IsInstanceValid(piece))
			{
				piece.QueueFree();
			}
		}

		_competitivePieces.Clear();
	}

	/// <summary>
	/// Handle competitive mode settlement - pure data processing only
	/// State machine controls all state transitions and striker restoration
	/// Delegates turn number to GameStateManager
	/// </summary>
	protected override void ExecuteModeSpecificSettlement()
	{
		var currentPlayer = GetCurrentPlayer();
		string currentPlayerId = currentPlayer?.PlayerId ?? "unknown";

		bool shouldContinue = ShouldContinueTurn(currentPlayerId);

		if (shouldContinue)
		{
			GD.Print($"[CarromCompetitive] {currentPlayerId} continues turn after successful pocketing");

			// CRITICAL FIX: Smoothly tween striker back to baseline when turn continues
			// Use tween for visual continuity instead of instant snap
			var carromGame = GetParent<CarromGame>();
			if (carromGame != null)
			{
				carromGame.TweenStrikerToBaseline(duration: 0.4f);
			}
			else
			{
				// Fallback to base class striker restoration
				EnsureStrikerRestored();
			}

			// State machine will automatically transition to Ready and re-enable input
			// No need for manual state control here
		}
		else
		{
			// Emit TurnReadyForPass signal for external UI handling
			int turnNumber = _gameState.GetCurrentTurnNumber();
			EmitSignal(SignalName.TurnReadyForPass, currentPlayerId, turnNumber);
		}

		// Stroke flags now managed by GameStateManager
		// No need to reset here - GameStateManager handles this in ProcessSettlement
	}

	/// <summary>
	/// Check if win condition is met for competitive mode
	/// Official Carrom Rules: Player/team wins by pocketing all assigned pieces and covering queen if pocketed
	/// Uses CarromRuleEngine for pure game logic evaluation
	/// </summary>
	public override bool CheckWinCondition(string playerId)
	{
		// Find the player by ID
		var player = _players.FirstOrDefault(p => p.PlayerId == playerId);
		if (player == null)
		{
			return false;
		}

		// Convert to rule engine data structure
		var playerState = PlayerState.FromPlayer(player);

		if (_playerCount == 2)
		{
			// 2-player mode: Use rule engine to check individual win condition
			var result = CarromRuleEngine.CheckWinCondition_Singles(playerState);
			return result.HasWon;
		}
		else if (_playerCount == 4)
		{
			// 4-player doubles mode: Check team win condition
			var playerPieceType = player.AssignedPieceType;
			var teamPlayers = _players.Where(p => p.AssignedPieceType == playerPieceType).ToList();

			if (teamPlayers.Count < 2)
			{
				// Incomplete team - cannot win
				return false;
			}

			// Convert both team members to rule engine data structures
			var player1State = PlayerState.FromPlayer(teamPlayers[0]);
			var player2State = PlayerState.FromPlayer(teamPlayers[1]);

			// Use rule engine to check team win condition
			var result = CarromRuleEngine.CheckWinCondition_Doubles(player1State, player2State);
			return result.HasWon;
		}

		return false;
	}

	/// <summary>
	/// Determine if current turn should continue based on competitive rules
	/// Handles both breaking turns (3 attempt rule) and normal turns
	/// </summary>
	public override bool ShouldContinueTurn(string playerId)
	{
		// Special handling for breaking turn (3 attempt rule)
		if (_gameState.IsBreakingTurn())
		{
			bool piecesDisturbed = _gameState.CurrentTurn.PiecesDisturbedInBreaking;
			int attempts = _gameState.CurrentTurn.BreakingAttempts;
			const int MAX_BREAKING_ATTEMPTS = 3;

			// If pieces disturbed OR max attempts reached, breaking complete (don't continue)
			if (piecesDisturbed || attempts >= MAX_BREAKING_ATTEMPTS)
			{
				return false;  // Breaking complete, end turn
			}

			// Breaking attempt failed but can try again
			return true;  // Continue turn for another breaking attempt
		}

		// Normal turn continuation (not breaking)
		bool validPocket = _gameState.GetValidPocketThisStroke();
		bool foul = _gameState.GetFoulThisStroke();
		bool opponentPiecePocketed = _gameState.CurrentTurn.OpponentPiecePocketedThisTurn;

		// Turn continues only if: valid pocket AND no foul AND no opponent piece pocketed
		// Per ICF Rules: Pocketing opponent piece ends your turn even though it's a valid pocket
		return validPocket && !foul && !opponentPiecePocketed;
	}

	/// <summary>
	/// DEBUG: Manually set current player index for testing
	/// Advances player turns until reaching the desired index
	/// </summary>
	public void DEBUG_SetCurrentPlayerIndex(int playerIndex)
	{
		if (playerIndex < 0 || playerIndex >= _players.Count)
		{
			GD.PrintErr($"[DEBUG] Invalid player index: {playerIndex} (total: {_players.Count})");
			return;
		}

		if (_gameState == null)
		{
			GD.PrintErr("[DEBUG] GameStateManager not available");
			return;
		}

		GD.Print($"[DEBUG] Setting current player index to: {playerIndex}");

		// Advance player turns until we reach the desired index
		int currentIndex = _gameState.GetCurrentPlayerIndex();
		int playerCount = _players.Count;

		while (currentIndex != playerIndex)
		{
			_gameState.AdvanceToNextPlayer();
			currentIndex = (currentIndex + 1) % playerCount;
		}
	}

	/// <summary>
	/// Position striker at baseline for current player
	/// Delegates player index to GameStateManager
	/// </summary>
	protected override void PositionStrikerAtBaseline()
	{
		if (_striker == null || !GodotObject.IsInstanceValid(_striker))
		{
			return;
		}

		if (_board == null)
		{
			return;
		}

		// Get current player index from GameStateManager
		int currentPlayerIndex = _gameState.GetCurrentPlayerIndex();
		Vector2 baselinePosition = _board.GetBaselinePosition(currentPlayerIndex);
		var globalPosition = _board.ToGlobal(baselinePosition);

		// Use synchronized Reset method instead of direct position assignment
		// This ensures physics engine state is immediately synchronized
		_striker.Reset(globalPosition);
	}

	/// <summary>
	/// Handle competitive-specific piece pocketing logic
	/// Implements official carrom rules for piece pocketing, fouls, and turn continuation
	/// Uses CarromRuleEngine for pure game logic evaluation
	/// Delegates stroke flags to GameStateManager
	/// </summary>
	protected override void HandlePiecePocketed(CarromPiece piece)
	{
		var currentPlayer = GetCurrentPlayer();
		if (currentPlayer == null)
		{
			GD.PrintErr($"[CarromCompetitive] No current player during piece pocket: {piece?.Type}. Cannot process.");
			return;
		}

		string playerId = currentPlayer.PlayerId;

		// Convert current game state to rule engine data structures
		var playerState = PlayerState.FromPlayer(currentPlayer);

		// Evaluate pocket using rule engine - passes GameStateManager.CurrentTurn directly (no shadow state)
		var evaluation = CarromRuleEngine.EvaluatePocketedPiece(
			piece.Type,
			playerState,
			_gameState.CurrentTurn);

		// Apply evaluation results
		if (evaluation.IsFoul)
		{
			// Record foul in GameStateManager
			_gameState.RecordFoul();
			currentPlayer.RecordFoul();
			EmitSignal(SignalName.FoulCommitted, playerId);

			// Return piece to center if required (but not striker - it has its own repositioning path)
			if (evaluation.ShouldReturnPiece && piece.Type != PieceType.Striker)
			{
				ReturnPieceToCenter(piece.Type, deferTweening: true);
			}

			// Apply penalty
			ApplyFoulPenalty(currentPlayer, evaluation.FoulType);
		}
		else if (evaluation.IsValid)
		{
			// Record valid pocket in GameStateManager
			_gameState.RecordValidPocket();

			// Check if this is an opponent's piece
			bool isOpponentPiece = piece.Type != currentPlayer.AssignedPieceType && piece.Type != PieceType.Red;

			if (isOpponentPiece)
			{
				// Mark that opponent piece was pocketed - this will end the turn
				_gameState.CurrentTurn.OpponentPiecePocketedThisTurn = true;

				// Credit opponent piece to the opponent
				var opponent = GetOpponentPlayer(currentPlayer);
				if (opponent != null)
				{
					opponent.RecordPocketedPiece(piece.Type);
					GD.Print($"[CarromCompetitive] {playerId} pocketed opponent piece - credited to {opponent.PlayerId}, turn will end");

					// Show feedback
					string pieceIcon = piece.Type == PieceType.White ? "⚪" : "⚫";
					_scoreDisplay?.ShowFloatingText(opponent.PlayerId, $"{pieceIcon} Opponent Gift!", Colors.Orange);

					// Turn will end at EndOfTurn processing via ShouldContinueTurn check
				}
				else
				{
					GD.PrintErr($"[CarromCompetitive] Could not find opponent to credit piece to");
				}
			}
			else
			{
				// Own piece or queen - record for current player
				// RecordPocketedPiece handles all state updates including queen covering
				// The CarromPlayer class automatically covers queen when pocketing assigned piece after queen
				currentPlayer.RecordPocketedPiece(piece.Type);
			}

			// Show appropriate UI feedback based on what was pocketed
			if (evaluation.UpdatesQueenState)
			{
				// Queen pocketed - update GameStateManager.CurrentTurn directly (no shadow state)
				CarromRuleEngine.ApplyQueenPocketing(playerState, _gameState.CurrentTurn);

				GD.Print($"[CarromCompetitive] {playerId} pocketed queen - can cover: {currentPlayer.CanCoverQueen()}");
				_scoreDisplay?.ShowFloatingText(playerId, "Queen Pocketed! 👑", Colors.Gold);
				EmitSignal(SignalName.NotificationRequested, (int)NotificationType.QueenPocketed, "👑 Queen Pocketed!");
			}
			else if (evaluation.CoversQueen)
			{
				// Assigned piece pocketed after queen - covers queen automatically via RecordPocketedPiece
				// Update GameStateManager.CurrentTurn directly to reflect covering (no shadow state)
				CarromRuleEngine.ApplyQueenCovering(playerState, _gameState.CurrentTurn);

				string pieceIcon = piece.Type == PieceType.White ? "⚪" : "⚫";
				_scoreDisplay?.ShowFloatingText(playerId, $"{pieceIcon} Queen Covered! 👑", Colors.Gold);
			}
			else
			{
				// Regular piece pocketed
				string pieceIcon = piece.Type == PieceType.White ? "⚪" : "⚫";
				_scoreDisplay?.ShowFloatingText(playerId, $"{pieceIcon} Piece Pocketed!", Colors.Green);
				EmitSignal(SignalName.NotificationRequested, (int)NotificationType.PiecePocketed, $"{pieceIcon} Piece Pocketed!");
			}

			// Check win condition after valid pocketing
			if (CheckWinCondition(playerId))
			{
				EmitSignal(SignalName.PlayerWon, playerId);
			}
		}

		// Note: Turn continuation is determined by ShouldContinueTurn() after all pieces settle
		// Valid pocketing (GameStateManager.ValidPocketThisStroke) allows turn to continue
		// Fouls (GameStateManager.FoulThisStroke) end the turn
	}

	// ================================================================
	// COMPETITIVE MODE SPECIFIC METHODS
	// ================================================================

	/// <summary>
	/// Start competitive mode with global credit tracking
	/// NEW PATTERN: Tracks global competitive games played instead of table credits
	/// </summary>
	public bool StartCompetitiveMode()
	{
		// Track global competitive games for new data pattern
		if (_competitiveCreditCost > 0)
		{
			// Reset user idle timer
			var sessionManager = SessionManager.GetInstance();
			if (sessionManager != null && IsInstanceValid(sessionManager))
			{
				sessionManager.ResetAllIdleTimers();
			}

			// NEW PATTERN: Track global competitive games played instead of spending table credits
			// This allows for unlimited competitive play while tracking usage analytics
			TrackGlobalCompetitiveGame();
		}

		// Setup competitive mode
		SetupCompetitiveMode();

		// Connect to TurnAdvanced signal for input controller synchronization
		// This ensures baseline updates happen BEFORE Ready phase
		if (_gameState != null && IsInstanceValid(_gameState))
		{
			_gameState.TurnAdvanced += OnTurnAdvanced;
		}

		return true;
	}

	/// <summary>
	/// Handle turn advancement signal - sync input controller BEFORE Ready phase
	/// </summary>
	private void OnTurnAdvanced(string newPlayerId)
	{
		int newPlayerIndex = _gameState.GetCurrentPlayerIndex();
		GD.Print($"[CarromCompetitive] OnTurnAdvanced - syncing input to player {newPlayerIndex}");

		// Sync input controller IMMEDIATELY (before Ready phase begins)
		_inputController?.SetCurrentPlayer(newPlayerIndex);
	}

	/// <summary>
	/// Setup competitive mode pieces (full carrom layout) - public interface
	/// </summary>
	public void SetupCompetitiveMode()
	{
		// Use base class template method
		SetupMode();
	}

	/// <summary>
	/// Finalize competitive mode setup - emit the expected signal for CarromGame
	/// </summary>
	protected override void FinalizeSetup()
	{
		// Emit the competitive-specific signal that CarromGame expects
		EmitSignal(SignalName.CompetitiveModeSetupComplete);
	}

	/// <summary>
	/// Handle piece being pocketed in competitive mode (with player ID) - additional method
	/// </summary>
	public void OnPiecePocketed(string playerId, CarromPiece piece)
	{
		// This is for external callers who already have player ID
		HandlePiecePocketed(piece);
	}

	/// <summary>
	/// Create competitive pieces in center formation
	/// </summary>
	private void CreateCompetitivePieces()
	{
		// Official carrom setup: 9 white, 9 black, 1 red queen
		// Following traditional carrom arrangement rules from the reference image
		Vector2 center = Vector2.Zero;
		float pieceSpacing = _board?.PieceRadius * 2.1f ?? 24.0f; // Pieces touching with slight gap

		// 1. Queen (red) in center
		CreateCompetitivePiece(PieceType.Red, center);

		// 2. First row: 6 pieces around Queen, alternating black and white starting with black
		Vector2[] firstRowPositions =
		[
			new Vector2(0, -pieceSpacing),                            // Top (0°)
			new Vector2(pieceSpacing * 0.866f, -pieceSpacing * 0.5f), // Top right (60°)
			new Vector2(pieceSpacing * 0.866f, pieceSpacing * 0.5f),  // Bottom right (120°)
			new Vector2(0, pieceSpacing),                             // Bottom (180°)
			new Vector2(-pieceSpacing * 0.866f, pieceSpacing * 0.5f), // Bottom left (240°)
			new Vector2(-pieceSpacing * 0.866f, -pieceSpacing * 0.5f) // Top left (300°)
		];

		// Alternate black and white in first row starting with black at top
		for (int i = 0; i < firstRowPositions.Length; i++)
		{
			PieceType pieceType = i % 2 == 0 ? PieceType.Black : PieceType.White;
			CreateCompetitivePiece(pieceType, firstRowPositions[i]);
		}

		// 3. Second row: 12 pieces with white Y-shape formation
		var secondRowRadius = pieceSpacing * 2.0f;

		// Create the Y-shape by placing 3 white pieces that form Y with the first row whites
		// From the reference image, the Y is formed by white pieces at specific angles
		// that align with the white pieces from first row (positions 1, 3, 5)
		int[] yShapeIndices = [2, 6, 10]; // These create the Y-shape extending from first row whites

		// Second row piece arrangement - fill all 12 positions
		for (int i = 0; i < 12; i++)
		{
			float angle = i * Mathf.Pi / 6; // 12 pieces = 30° each (π/6 radians)
			Vector2 pos = center + (new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * secondRowRadius);

			PieceType pieceType;
			if (yShapeIndices.Contains(i))
			{
				// White pieces forming the Y-shape
				pieceType = PieceType.White;
			}
			else
			{
				// Fill alternating pattern for remaining positions to balance colors
				// Ensure we get exactly 9 white and 9 black total
				// We have 3 whites from first row + 3 whites from Y-shape = 6 whites so far
				// We need 3 more whites in the remaining 9 positions
				if (i is 4 or 8 or 0 && CountWhitePiecesCreated() < 9)
				{
					pieceType = PieceType.White;
				}
				else
				{
					pieceType = PieceType.Black;
				}
			}

			CreateCompetitivePiece(pieceType, pos);
		}
	}

	/// <summary>
	/// Count white pieces created so far (for balancing during creation)
	/// </summary>
	private int CountWhitePiecesCreated()
	{
		return _competitivePieces.Count(p => p.Type == PieceType.White);
	}

	/// <summary>
	/// Create a competitive piece using the centralized piece factory
	/// </summary>
	private CarromPiece CreateCompetitivePiece(PieceType type, Vector2 position)
	{
		var piece = CreatePiece(type, position);

		// Track competitive pieces (factory handles board addition and physics setup)
		if (piece != null && type != PieceType.Striker)
		{
			_competitivePieces.Add(piece);
		}

		return piece;
	}

	/// <summary>
	/// Get piece template for type
	/// </summary>
	private PackedScene GetPieceTemplate(PieceType type)
	{
		return type switch
		{
			PieceType.White => _whitePieceTemplate,
			PieceType.Black => _blackPieceTemplate,
			PieceType.Red => _redPieceTemplate,
			PieceType.Striker => _strikerTemplate,
			_ => null,
		};
	}

	/// <summary>
	/// Set the number of players for competitive mode (2 or 4)
	/// </summary>
	public void SetPlayerCount(int playerCount)
	{
		if (playerCount is 2 or 4)
		{
			_playerCount = playerCount;
		}
	}

	/// <summary>
	/// Setup players for competitive mode with proper piece type assignments
	/// Supports both 2-player and 4-player (doubles) modes
	/// </summary>
	private void SetupCompetitivePlayers()
	{
		// Create players for competitive mode
		_players.Clear();

		if (_playerCount == 2)
		{
			// 2-player mode: One player gets white, other gets black
			var player1 = new CarromPlayer();
			player1.PlayerId = "player1";
			player1.AssignPieceType(PieceType.White);
			player1.ResetGameStats();
			player1.QueenCoveredSuccessfully += OnQueenCoveredSuccessfully;
			_players.Add(player1);

			var player2 = new CarromPlayer();
			player2.PlayerId = "player2";
			player2.AssignPieceType(PieceType.Black);
			player2.ResetGameStats();
			player2.QueenCoveredSuccessfully += OnQueenCoveredSuccessfully;
			_players.Add(player2);
		}
		else if (_playerCount == 4)
		{
			// 4-player doubles mode: Players sit opposite as partners
			// Team 1: Player 1 and Player 3 (white pieces)
			// Team 2: Player 2 and Player 4 (black pieces)
			var player1 = new CarromPlayer();
			player1.PlayerId = "player1";
			player1.AssignPieceType(PieceType.White);
			player1.ResetGameStats();
			player1.QueenCoveredSuccessfully += OnQueenCoveredSuccessfully;
			_players.Add(player1);

			var player2 = new CarromPlayer();
			player2.PlayerId = "player2";
			player2.AssignPieceType(PieceType.Black);
			player2.ResetGameStats();
			player2.QueenCoveredSuccessfully += OnQueenCoveredSuccessfully;
			_players.Add(player2);

			var player3 = new CarromPlayer();
			player3.PlayerId = "player3";
			player3.AssignPieceType(PieceType.White); // Partner with player1
			player3.ResetGameStats();
			player3.QueenCoveredSuccessfully += OnQueenCoveredSuccessfully;
			_players.Add(player3);

			var player4 = new CarromPlayer();
			player4.PlayerId = "player4";
			player4.AssignPieceType(PieceType.Black); // Partner with player2
			player4.ResetGameStats();
			player4.QueenCoveredSuccessfully += OnQueenCoveredSuccessfully;
			_players.Add(player4);
		}

		// Player index now managed by GameStateManager
		// Initial player setup happens in GameStateManager.SetupPlayers()
		_gameState.SetupPlayers(_players);

		// Sync input controller with initial player
		int initialPlayerIndex = _gameState.GetCurrentPlayerIndex();
		_inputController?.SetCurrentPlayer(initialPlayerIndex);

		EmitSignal(SignalName.TurnChanged, GetCurrentPlayer()?.PlayerId ?? "player1", 1);
	}

	/// <summary>
	/// Switch to next player - synchronous, linear operation
	/// OFFICIAL CARROM RULE: Handle queen covering validation at end of turn
	/// Delegates player advancement to GameStateManager
	/// </summary>
	public void SwitchToNextPlayer()
	{
		// Step 1: Handle end of current player's turn (queen covering validation)
		var currentPlayer = GetCurrentPlayer();
		if (currentPlayer != null)
		{
			bool queenNeedsReturning = currentPlayer.HandleEndOfTurn();
			if (queenNeedsReturning)
			{
				// Show floating text for queen returned
				_scoreDisplay?.ShowFloatingText(currentPlayer.PlayerId, "Queen Returned 👑", Colors.Orange);

				// Emit notification for queen returned
				EmitSignal(SignalName.NotificationRequested, (int)NotificationType.QueenReturned, "👑 Queen Returned");

				// Return uncovered queen to center
				ReturnPieceToCenter(PieceType.Red, deferTweening: true);
				GD.Print($"[CarromCompetitive] Returned uncovered queen to center for {currentPlayer.PlayerId}");
			}
		}

		// Step 2: DELEGATE player advancement to GameStateManager
		// OLD: _currentPlayerIndex = (_currentPlayerIndex + 1) % _players.Count;
		_gameState.AdvanceToNextPlayer();

		// Step 3: Start new turn for next player
		var nextPlayer = GetCurrentPlayer();
		nextPlayer?.StartNewTurn();

		// Step 4: Sync input controller with new player
		// Get player index from GameStateManager
		int newPlayerIndex = _gameState.GetCurrentPlayerIndex();
		_inputController?.SetCurrentPlayer(newPlayerIndex);

		// Step 5: Emit turn changed signal (striker positioning will happen after camera transition)
		// Note: GameStateManager also emits TurnChanged - this is for legacy compatibility
		EmitSignal(SignalName.TurnChanged, nextPlayer?.PlayerId ?? "unknown", newPlayerIndex + 1);
	}

	/// <summary>
	/// Count remaining pieces for player
	/// </summary>
	private int CountRemainingPiecesForPlayer(string playerId)
	{
		// Simplified logic - would need proper player piece assignment
		// TODO: Use playerId to filter pieces assigned to specific player
		return _competitivePieces.Count(p => p.Visible);
	}

	/// <summary>
	/// Get current player's CarromPlayer node
	/// Delegates to GameStateManager for state, returns corresponding player node
	/// </summary>
	public CarromPlayer GetCurrentPlayer()
	{
		// Validate GameStateManager has active game state
		if (_gameState == null)
		{
			GD.PrintErr("[CompetitiveModeManager] GameStateManager not initialized");
			return null;
		}

		// Delegate to single source of truth
		var playerState = _gameState.GetCurrentPlayer();
		if (playerState == null)
		{
			// This is normal during initialization or cleanup
			return null;
		}

		// Map PlayerState → CarromPlayer node
		var player = _players.Find(p => p != null && p.PlayerId == playerState.PlayerId);

		if (player == null)
		{
			GD.PrintErr($"[CompetitiveModeManager] Player node missing for {playerState.PlayerId} (have {_players.Count} players)");
		}

		return player;
	}

	/// <summary>
	/// Get opponent of the given player
	/// For 2-player carrom, returns the other player
	/// </summary>
	private CarromPlayer GetOpponentPlayer(CarromPlayer currentPlayer)
	{
		if (currentPlayer == null || _players.Count != 2)
		{
			return null;
		}

		return _players.Find(p => p != null && p.PlayerId != currentPlayer.PlayerId);
	}

	public List<CarromPiece> GetCompetitivePieces()
	{
		return [.. _competitivePieces];
	}

	protected override void ClearModeSpecificState()
	{
		_players.Clear();

		// Breaking turn state now managed by GameStateManager

		// Sync input controller with reset player
		int resetPlayerIndex = _gameState.GetCurrentPlayerIndex();
		_inputController?.SetCurrentPlayer(resetPlayerIndex);
	}

	/// <summary>
	/// Handle breaking turn continuation logic
	/// OFFICIAL RULE: Breaking player gets up to 3 attempts to disturb the central formation
	/// Uses CarromRuleEngine for pure game logic evaluation
	/// Delegates breaking state to GameStateManager
	/// </summary>
	private bool HandleBreakingTurnContinuation()
	{
		// Breaking attempts managed by GameStateManager
		// Increment happens there, we just query current value
		int currentAttempt = _gameState.GetBreakingAttempts();

		// Get stroke flags from GameStateManager
		bool validPocket = _gameState.GetValidPocketThisStroke();
		bool foul = _gameState.GetFoulThisStroke();

		// Query pieces disturbed state from GameStateManager (single source of truth)
		bool piecesDisturbed = _gameState.CurrentTurn.PiecesDisturbedInBreaking;

		// Use rule engine to evaluate breaking turn
		var result = CarromRuleEngine.EvaluateBreakingTurn(
			validPocket: validPocket,
			foul: foul,
			attemptNumber: currentAttempt,
			piecesDisturbed: piecesDisturbed);

		// Apply result based on turn decision
		bool shouldContinueTurn;

		switch (result.TurnDecision)
		{
			case TurnDecision.Continue:
				// Breaking successful with valid pocket - turn continues
				GD.Print($"[CarromCompetitive] Breaking successful on attempt {currentAttempt} - {result.Reason}");
				EmitSignal(SignalName.NotificationRequested, (int)NotificationType.BreakingSuccess, "✓ Breaking Successful!");
				shouldContinueTurn = true;
				break;

			case TurnDecision.Pass:
				// Breaking complete but turn ends (no valid pocket OR failed after 3 attempts)
				if (result.IsSuccess)
				{
					GD.Print($"[CarromCompetitive] Breaking successful on attempt {currentAttempt} - {result.Reason}");
					EmitSignal(SignalName.NotificationRequested, (int)NotificationType.BreakingSuccess, "✓ Breaking Successful!");
				}
				else
				{
					GD.Print($"[CarromCompetitive] Breaking failed after {MAX_BREAKING_ATTEMPTS} attempts - {result.Reason}");
					EmitSignal(SignalName.NotificationRequested, (int)NotificationType.BreakingFailed, "✗ Breaking Failed - Turn Passes");
				}

				shouldContinueTurn = false;
				break;

			case TurnDecision.BreakingContinue:
				// More breaking attempts available
				GD.Print($"[CarromCompetitive] Breaking attempt {currentAttempt}/{MAX_BREAKING_ATTEMPTS} - {result.Reason}");
				EmitSignal(SignalName.NotificationRequested, (int)NotificationType.BreakingAttempt, $"Breaking: Attempt {currentAttempt}/{MAX_BREAKING_ATTEMPTS}");
				shouldContinueTurn = true;
				break;

			default:
				// Unexpected state - pass turn
				GD.PrintErr($"[CarromCompetitive] Unexpected breaking turn decision: {result.TurnDecision}");
				shouldContinueTurn = false;
				break;
		}

		// Stroke flags now managed by GameStateManager
		// Reset happens in GameStateManager.ProcessSettlement()
		return shouldContinueTurn;
	}

	/// <summary>
	/// Check if the central piece formation was disturbed
	/// A simple heuristic: if any piece moved from its original position significantly
	/// </summary>
	private bool CheckIfPiecesWereDisturbed()
	{
		// Simple heuristic: if any managed piece is not at its starting position
		var managedPieces = GetManagedPieces();
		Vector2 centerPosition = _board?.GetCenterPosition() ?? Vector2.Zero;
		float disturbanceThreshold = (_board?.PieceRadius ?? 15.0f) * 3.0f; // 3 piece radii

		foreach (var piece in managedPieces)
		{
			if (!GodotObject.IsInstanceValid(piece) || !piece.Visible)
			{
				continue;
			}

			// If any piece is far from center, formation was disturbed
			float distanceFromCenter = piece.GlobalPosition.DistanceTo(centerPosition);
			if (distanceFromCenter > disturbanceThreshold)
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Get all players
	/// </summary>
	public List<CarromPlayer> GetPlayers()
	{
		return [.. _players];
	}

	// ================================================================
	// FOUL PENALTY SYSTEM
	// ================================================================

	/// <summary>
	/// Apply foul penalty according to official carrom rules
	/// OFFICIAL RULES (Per ICF Laws of Carrom):
	/// - Striker pocketing: Return one of your own pocketed pieces
	/// - Opponent piece pocketing: NO LONGER A FOUL - opponent piece stays pocketed, counts for opponent
	/// - Improper queen pocketing: Return queen + penalty piece
	/// Uses CarromRuleEngine for pure game logic evaluation
	/// </summary>
	private void ApplyFoulPenalty(CarromPlayer foulPlayer, FoulType foulType = FoulType.General)
	{
		if (foulPlayer == null)
		{
			return;
		}

		// Convert to rule engine data structure
		var playerState = PlayerState.FromPlayer(foulPlayer);

		// Use rule engine to calculate penalty
		var penalty = CarromRuleEngine.CalculateFoulPenalty(foulType, playerState);

		// Apply the penalty if pieces need to be returned
		if (penalty.PiecesToReturn > 0)
		{
			ApplyStandardPenalty(foulPlayer, penalty.Reason);
		}
		else
		{
			GD.Print($"[FOUL PENALTY] {foulPlayer.PlayerId} - {penalty.Reason}");
		}
	}

	/// <summary>
	/// Apply standard penalty - return one of the player's pocketed pieces to center
	/// </summary>
	private void ApplyStandardPenalty(CarromPlayer foulPlayer, string foulReason)
	{
		if (!foulPlayer.CanReturnPiece())
		{
			GD.Print($"[FOUL PENALTY] {foulPlayer.PlayerId} has no pieces to return for {foulReason}");
			return;
		}

		// Get a piece to return from the player's pocketed pieces
		var pieceTypeToReturn = foulPlayer.ReturnPocketedPiece();
		if (pieceTypeToReturn.HasValue)
		{
			// Create the piece at center of board with deferred tweening
			ReturnPieceToCenter(pieceTypeToReturn.Value, deferTweening: true);

			// Update score display to reflect penalty piece return
			_scoreDisplay?.UpdateAllPlayerScores(_players);

			GD.Print($"[FOUL PENALTY] {foulPlayer.PlayerId} returns {pieceTypeToReturn.Value} piece to center for {foulReason} (deferred for tween)");
		}
	}

	/// <summary>
	/// Create a piece at the center of the board (for foul penalties and queen returns)
	/// </summary>
	private void ReturnPieceToCenter(PieceType pieceType, bool deferTweening = false)
	{
		// GUARD: Striker should NEVER use this path - it has its own repositioning system
		if (pieceType == PieceType.Striker)
		{
			GD.PrintErr("[ARCHITECTURE ERROR] Striker repositioning should use TweenStrikerToBaseline(), not ReturnPieceToCenter()");
			GD.PrintErr($"[ARCHITECTURE ERROR] Call stack:\n{System.Environment.StackTrace}");
			return;
		}

		if (_board == null)
		{
			return;
		}

		// FIRST: Try to find and restore an existing hidden piece of this type
		var hiddenPiece = FindHiddenPieceOfType(pieceType);
		if (hiddenPiece != null)
		{
			// Find a safe position near center that doesn't overlap with existing pieces
			Vector2 centerPosition = _board.GetCenterPosition();
			Vector2 safePosition = FindSafePositionNearCenter(centerPosition, pieceType);

			if (deferTweening)
			{
				// Position piece off-screen and add to tween list
				PrepareHiddenPieceForTweening(hiddenPiece, safePosition);
			}
			else
			{
				// Restore the existing hidden piece immediately
				hiddenPiece.Reset(safePosition, validatePlacement: true);
			}

			return;
		}

		// FALLBACK: Only create new piece if no hidden piece found
		Vector2 fallbackPosition = FindSafePositionNearCenter(_board.GetCenterPosition(), pieceType);
		var returnedPiece = CreatePiece(pieceType, fallbackPosition);
		if (returnedPiece != null && pieceType != PieceType.Striker)
		{
			// Add to competitive pieces list so it's tracked
			_competitivePieces.Add(returnedPiece);

			if (deferTweening)
			{
				// Position new piece off-screen and add to tween list
				PrepareNewPieceForTweening(returnedPiece, fallbackPosition);
			}
		}
	}

	/// <summary>
	/// Find a hidden (pocketed) piece of the specified type that can be restored
	/// </summary>
	private CarromPiece FindHiddenPieceOfType(PieceType pieceType)
	{
		// Special case for striker - check base class _striker field
		// Strikers are excluded from _competitivePieces list
		if (pieceType == PieceType.Striker)
		{
			if (_striker != null && GodotObject.IsInstanceValid(_striker) &&
				!_striker.Visible && _striker.Freeze)
			{
				return _striker;
			}

			return null;
		}

		// For other pieces, search _competitivePieces as before
		return _competitivePieces.FirstOrDefault(p =>
			GodotObject.IsInstanceValid(p) &&
			p.Type == pieceType &&
			!p.Visible &&
			p.Freeze);
	}

	/// <summary>
	/// Find a safe position near center that doesn't overlap with existing pieces
	/// </summary>
	private Vector2 FindSafePositionNearCenter(Vector2 center, PieceType pieceType)
	{
		// Start at center and spiral outward until we find a free spot
		Vector2 testPosition = center;
		float pieceRadius = _board?.PieceRadius ?? 15.0f;
		float searchRadius = pieceRadius * 2.5f; // Initial search radius
		int maxAttempts = 20;

		for (int attempt = 0; attempt < maxAttempts; attempt++)
		{
			bool positionFree = true;

			// Check against all existing competitive pieces
			foreach (var existingPiece in _competitivePieces)
			{
				if (!IsInstanceValid(existingPiece) || !existingPiece.Visible)
				{
					continue;
				}

				float distance = testPosition.DistanceTo(existingPiece.GlobalPosition);

				// Pieces need to be separated
				if (distance < pieceRadius * 2.2f)
				{
					positionFree = false;
					break;
				}
			}

			if (positionFree)
			{
				return testPosition;
			}

			// Try a new position in a spiral pattern
			float angle = attempt * 0.5f; // Spiral pattern
			testPosition = center + new Vector2(
				Mathf.Cos(angle) * searchRadius,
				Mathf.Sin(angle) * searchRadius);

			// Increase search radius for next attempt
			searchRadius += pieceRadius * 0.5f;
		}

		// Fallback to center if no safe position found
		GD.PrintErr($"[WARNING] Could not find safe position for returned {pieceType} piece, using center");
		return center;
	}

	/// <summary>
	/// Prepare a hidden piece for tween animation by positioning it at a realistic pocket position
	/// </summary>
	private void PrepareHiddenPieceForTweening(CarromPiece piece, Vector2 finalPosition)
	{
		// GUARD: Validate piece before doing anything
		if (!GodotObject.IsInstanceValid(piece))
		{
			GD.PrintErr("[PENALTY TWEEN] Cannot prepare invalid piece for tweening");
			return;
		}

		// GUARD: Don't add duplicates
		if (_piecesNeedingTweenReturn.Contains(piece))
		{
			GD.PrintErr($"[PENALTY TWEEN] {piece.Type} piece already prepared for tween - skipping duplicate");
			return;
		}

		// Use piece's current position (should be at pocket center) as starting point
		Vector2 pocketPosition = piece.GlobalPosition;

		// If piece position seems invalid (too close to origin), find nearest pocket position
		if (pocketPosition.Length() < 10.0f)
		{
			pocketPosition = FindNearestPocketPosition(finalPosition);
		}

		// Reset piece physics state but keep it hidden until tween starts
		piece.Reset(pocketPosition, immediate: true, restoreVisualProperties: false, validatePlacement: false);

		// Keep piece hidden in pocket until tween animation begins
		piece.Visible = false;
		piece.Scale = Vector2.Zero;
		piece.Modulate = new Color(1.0f, 1.0f, 1.0f, 0.0f);

		// Store both the start and target positions as metadata
		piece.SetMeta("tween_start_position", pocketPosition);
		piece.SetMeta("tween_target_position", finalPosition);

		// Add to tween list
		_piecesNeedingTweenReturn.Add(piece);

		GD.Print($"[PENALTY TWEEN] Prepared hidden {piece.Type} piece for tween return from pocket at {pocketPosition} (kept hidden)");
	}

	/// <summary>
	/// Prepare a newly created piece for tween animation
	/// </summary>
	private void PrepareNewPieceForTweening(CarromPiece piece, Vector2 finalPosition)
	{
		// GUARD: Validate piece before doing anything
		if (!GodotObject.IsInstanceValid(piece))
		{
			GD.PrintErr("[PENALTY TWEEN] Cannot prepare invalid new piece for tweening");
			return;
		}

		// GUARD: Don't add duplicates
		if (_piecesNeedingTweenReturn.Contains(piece))
		{
			GD.PrintErr($"[PENALTY TWEEN] New {piece.Type} piece already prepared for tween - skipping duplicate");
			return;
		}

		// For new pieces, find a reasonable pocket position to start from
		Vector2 pocketPosition = FindNearestPocketPosition(finalPosition);

		// Move piece to pocket position
		piece.GlobalPosition = pocketPosition;

		// Start hidden like pocketed pieces, will be revealed when tween starts
		piece.Visible = false;
		piece.Freeze = false;
		piece.Scale = Vector2.Zero;
		piece.Modulate = new Color(1.0f, 1.0f, 1.0f, 0.0f);

		// Store both the start and target positions as metadata
		piece.SetMeta("tween_start_position", pocketPosition);
		piece.SetMeta("tween_target_position", finalPosition);

		// Add to tween list
		_piecesNeedingTweenReturn.Add(piece);

		GD.Print($"[PENALTY TWEEN] Prepared new {piece.Type} piece for tween return from pocket at {pocketPosition} (kept hidden)");
	}

	/// <summary>
	/// Find the nearest pocket position for piece tween animations
	/// </summary>
	private Vector2 FindNearestPocketPosition(Vector2 centerPosition)
	{
		if (_board == null)
		{
			return new Vector2(centerPosition.X, -200.0f); // Fallback to off-screen
		}

		// Get pocket positions from board
		var pockets = _board.GetPockets();
		if (pockets.Count == 0)
		{
			return new Vector2(centerPosition.X, -200.0f); // Fallback
		}

		// Find the pocket closest to the center position (reasonable visual choice)
		Vector2 nearestPocketPosition = pockets[0].GlobalPosition;
		float nearestDistance = centerPosition.DistanceTo(nearestPocketPosition);

		foreach (var pocket in pockets)
		{
			if (pocket != null && GodotObject.IsInstanceValid(pocket))
			{
				Vector2 pocketPos = pocket.GlobalPosition;
				float distance = centerPosition.DistanceTo(pocketPos);
				if (distance < nearestDistance)
				{
					nearestDistance = distance;
					nearestPocketPosition = pocketPos;
				}
			}
		}

		return nearestPocketPosition;
	}

	/// <summary>
	/// Get pieces that need tween return animation
	/// </summary>
	public List<CarromPiece> GetPiecesNeedingTweenReturn()
	{
		// Clean up any invalid pieces first
		_piecesNeedingTweenReturn.RemoveAll(p => !GodotObject.IsInstanceValid(p));

		// Return a copy to avoid modification during iteration
		return [.. _piecesNeedingTweenReturn];
	}

	public void ClearTweenReturnList()
	{
		_piecesNeedingTweenReturn.Clear();
	}

	private void OnQueenCoveredSuccessfully(string playerId)
	{
		// Show floating text for queen covered
		_scoreDisplay?.ShowFloatingText(playerId, "Queen Covered! 👑✓", Colors.Gold);

		// Emit notification for queen covered
		EmitSignal(SignalName.NotificationRequested, (int)NotificationType.QueenCovered, "👑✓ Queen Covered!");
	}

	/// <summary>
	/// Track global competitive game for analytics (NEW PATTERN)
	/// Replaces table credit spending with persistent global tracking
	/// </summary>
	private void TrackGlobalCompetitiveGame()
	{
		if (_sessionManager == null)
		{
			return;
		}

		var userSession = _sessionManager.GetPrimarySession();
		if (userSession != null)
		{
			// TODO: Event-sourced - carrom games tracked via backend events
			// Backend will aggregate wins/losses from carrom/round_finish events
			GD.Print($"[CarromCompetitive] Tracking competitive game for user {userSession.UserName}");
		}
		else
		{
			GD.Print("[CarromCompetitive] No global data available to track competitive game");
		}
	}

	/// <summary>
	/// Cleanup - disconnect signal handlers
	/// </summary>
	public override void _ExitTree()
	{
		// Disconnect TurnAdvanced signal
		if (_gameState != null && IsInstanceValid(_gameState))
		{
			_gameState.TurnAdvanced -= OnTurnAdvanced;
		}

		base._ExitTree();
	}
}
