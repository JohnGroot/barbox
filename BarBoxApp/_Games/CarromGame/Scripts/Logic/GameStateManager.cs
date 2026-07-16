using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace BarBox.Games.Carrom;

/// <summary>
/// Centralized state manager for CarromGame - single source of truth
/// Centralized state manager - single source of truth for all CarromGame state
/// Owns ALL game state: players, pieces, turn context, phase
/// </summary>
public partial class GameStateManager : Node, ICarromGameState
{
	// ================================================================
	// SIGNALS
	// ================================================================
	[Signal]
	public delegate void PhaseChangedEventHandler(string oldPhase, string newPhase);

	[Signal]
	public delegate void TurnChangedEventHandler(string playerId, int turnNumber);

	[Signal]
	public delegate void TurnReadyForPassEventHandler(string playerId, int turnNumber);

	[Signal]
	public delegate void TurnAdvancedEventHandler(string newPlayerId);

	[Signal]
	public delegate void ContinueTurnRequestedEventHandler(string playerId);

	[Signal]
	public delegate void PlayerWonEventHandler(string playerId);

	[Signal]
	public delegate void FoulCommittedEventHandler(string playerId);

	[Signal]
	public delegate void SettlementCompletedEventHandler();

	// ================================================================
	// ENUMS
	// ================================================================
	public enum GamePhase
	{
		Initializing,
		Ready,
		Strike,
		Physics,
		Settlement,
	}

	// ================================================================
	// STATE PROPERTIES (SINGLE SOURCE OF TRUTH)
	// ================================================================

	// Phase state
	public GamePhase CurrentPhase { get; private set; } = GamePhase.Initializing;

	public bool CanAcceptInput => CurrentPhase == GamePhase.Ready;

	// Player state
	private Dictionary<string, PlayerState> _players = new();
	private List<string> _playerOrder = new(); // Order of player IDs for turn rotation
	private int _currentPlayerIndex = 0;

	// Turn state
	public TurnContext CurrentTurn { get; private set; } = new();

	// Breaking turn state
	private bool _isBreakingTurn = true;
	private int _breakingAttempts = 0;
	private const int MAX_BREAKING_ATTEMPTS = 3;

	// Piece state (positions tracked separately from Godot nodes)
	private Dictionary<Guid, PieceState> _pieces = new();

	// Stroke flags
	private bool _validPocketThisStroke = false;
	private bool _foulThisStroke = false;

	// Game mode
	private CarromGameMode _gameMode = CarromGameMode.Competitive;

	// ================================================================
	// PRIVATE FIELDS
	// ================================================================
	private int _currentTurnNumber = 1;

	// Dependencies
	private CarromBoard _board;
	private CarromInputController _inputController;
	private CarromPieceFactory _pieceFactory;
	private CarromPhysicsMonitor _physicsMonitor;

	// ================================================================
	// INITIALIZATION
	// ================================================================
	public void Initialize(CarromBoard board, CarromInputController inputController, CarromGameMode gameMode, CarromPieceFactory pieceFactory)
	{
		_board = board;
		_inputController = inputController;
		_gameMode = gameMode;
		_pieceFactory = pieceFactory;

		// Create and initialize physics monitor
		_physicsMonitor = new CarromPhysicsMonitor();
		AddChild(_physicsMonitor);
		_physicsMonitor.Initialize(_pieceFactory);
		_physicsMonitor.AllPiecesStopped += OnAllPiecesStopped;

		GD.Print($"[GameStateManager] Initialized for {gameMode} mode");
	}

	// Track actual CarromPlayer nodes for compatibility
	private List<CarromPlayer> _playerNodes = new();

	public void SetupPlayers(List<CarromPlayer> players)
	{
		_players.Clear();
		_playerOrder.Clear();
		_playerNodes = players ?? new();

		foreach (var player in players)
		{
			if (player == null || string.IsNullOrEmpty(player.PlayerId))
			{
				continue;
			}

			var playerState = PlayerState.FromPlayer(player);
			_players[player.PlayerId] = playerState;
			_playerOrder.Add(player.PlayerId);
		}

		_currentPlayerIndex = 0;
		GD.Print($"[GameStateManager] Setup {_players.Count} players");
	}

	public void SetGameMode(CarromGameMode gameMode)
	{
		_gameMode = gameMode;
		GD.Print($"[GameStateManager] Game mode set to: {gameMode}");
	}

	// ================================================================
	// PHASE TRANSITIONS
	// ================================================================
	public void TransitionToPhase(GamePhase newPhase)
	{
		if (CurrentPhase == newPhase)
		{
			return;
		}

		var oldPhase = CurrentPhase;
		ExitPhase(oldPhase);
		CurrentPhase = newPhase;
		EnterPhase(newPhase);

		EmitSignal(SignalName.PhaseChanged, oldPhase.ToString(), newPhase.ToString());
		GD.Print($"[GameStateManager] {oldPhase} → {newPhase}");
	}

	private void ExitPhase(GamePhase phase)
	{
		switch (phase)
		{
			case GamePhase.Physics:
				_physicsMonitor?.StopMonitoring();
				break;
		}
	}

	private void EnterPhase(GamePhase phase)
	{
		switch (phase)
		{
			case GamePhase.Ready:
				// Reset stroke flags for new stroke
				_validPocketThisStroke = false;
				_foulThisStroke = false;
				break;

			case GamePhase.Strike:
				// Strike executed, transition immediately to Physics monitoring
				TransitionToPhase(GamePhase.Physics);
				break;

			case GamePhase.Physics:
				_physicsMonitor?.StartMonitoring();
				break;

			case GamePhase.Settlement:
				// Wait one physics frame to ensure all velocities are truly zero
				CallDeferred(MethodName.ProcessSettlementWithSync);
				break;
		}
	}

	public void OnStrikeExecuted()
	{
		if (CurrentPhase == GamePhase.Ready)
		{
			TransitionToPhase(GamePhase.Strike);
		}
	}

	// ================================================================
	// PHYSICS MONITORING (delegated to CarromPhysicsMonitor)
	// ================================================================
	private void OnAllPiecesStopped()
	{
		// Physics monitor detected all pieces have stopped
		if (CurrentPhase == GamePhase.Physics)
		{
			TransitionToPhase(GamePhase.Settlement);
		}
	}

	// ================================================================
	// SETTLEMENT PROCESSING
	// ================================================================
	private async void ProcessSettlementWithSync()
	{
		// Wait for one physics frame to ensure all piece velocities are truly zero
		await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		ProcessSettlement();
	}

	private void ProcessSettlement()
	{
		try
		{
			var currentPlayer = GetCurrentPlayer();
			string currentPlayerId = currentPlayer?.PlayerId ?? "unknown";

			GD.Print($"[ProcessSettlement] GameMode: {_gameMode}, Breaking: {_isBreakingTurn}, Phase: {CurrentPhase}, Player: {currentPlayerId}");

			// CRITICAL FIX: Update PiecesDisturbedInBreaking BEFORE checking ShouldContinueTurn
			// This ensures the flag is set even when no pieces are pocketed
			if (_isBreakingTurn && CurrentTurn != null)
			{
				bool piecesDisturbed = CheckIfPiecesWereDisturbed();
				CurrentTurn.PiecesDisturbedInBreaking = piecesDisturbed;
				GD.Print($"[GameStateManager] Breaking turn settlement - Pieces disturbed: {piecesDisturbed}");
			}

			bool shouldContinue = ShouldContinueTurn(currentPlayerId);

			// Competitive mode: Wait for user to pass turn manually
			if (!shouldContinue && _gameMode == CarromGameMode.Competitive)
			{
				GD.Print($"[GameStateManager] Turn ready for pass - Phase: {CurrentPhase}, Player: {currentPlayerId}, Turn: {_currentTurnNumber}");
				GD.Print($"[GameStateManager] Emitting TurnReadyForPass signal");
				EmitSignal(SignalName.TurnReadyForPass, currentPlayerId, _currentTurnNumber);

				// Stay in Settlement phase - don't transition yet
				return;
			}

			// Practice mode or continued turn: proceed immediately
			if (shouldContinue)
			{
				GD.Print($"[GameStateManager] {currentPlayerId} continues turn");

				// Request striker restoration for continued turn
				EmitSignal(SignalName.ContinueTurnRequested, currentPlayerId);
			}
			else
			{
				// Practice mode: auto-advance to next player
				AdvanceToNextPlayer();
			}

			// Stroke flags reset in EnterPhase(GamePhase.Ready) - single authoritative location
			EmitSignal(SignalName.SettlementCompleted);
			TransitionToPhase(GamePhase.Ready);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[GameStateManager] Settlement failed: {ex.Message}");
			EmitSignal(SignalName.SettlementCompleted);
			TransitionToPhase(GamePhase.Ready);
		}
	}

	/// <summary>
	/// Check if pieces have been disturbed from center formation
	/// Used for breaking turn validation
	/// </summary>
	private bool CheckIfPiecesWereDisturbed()
	{
		if (_pieceFactory == null)
		{
			GD.PrintErr("[GameStateManager] PieceFactory is null - cannot check piece disturbance");
			return false;
		}

		// Get all non-striker pieces
		var allPieces = _pieceFactory.GetAllPieces();
		if (allPieces == null || allPieces.Count == 0)
		{
			GD.Print("[GameStateManager] No pieces found - cannot determine disturbance");
			return false;
		}

		var pieces = allPieces.Where(p => p != null && GodotObject.IsInstanceValid(p) && p.Type != PieceType.Striker).ToList();

		if (pieces.Count == 0)
		{
			GD.Print("[GameStateManager] No non-striker pieces found");
			return false;
		}

		// Get center position
		Vector2 centerPosition = GetCenterPosition();

		// Check if any piece is far from center (threshold: 3 piece radii)
		// Piece radius is typically ~20px, so 3 radii = 60px
		const float disturbanceThreshold = 60.0f;

		foreach (var piece in pieces)
		{
			float distance = piece.GlobalPosition.DistanceTo(centerPosition);
			if (distance > disturbanceThreshold)
			{
				GD.Print($"[GameStateManager] Piece {piece.Type} disturbed - Distance from center: {distance:F1}px (threshold: {disturbanceThreshold}px)");
				return true;
			}
		}

		GD.Print($"[GameStateManager] No pieces disturbed - all within {disturbanceThreshold}px of center");
		return false;
	}

	/// <summary>
	/// Get center position of board
	/// </summary>
	private Vector2 GetCenterPosition()
	{
		if (_board != null && GodotObject.IsInstanceValid(_board))
		{
			return _board.GetCenterPosition();
		}

		GD.PrintErr("[GameStateManager] Cannot get center position - board not available");
		return Vector2.Zero;
	}

	// ================================================================
	// TURN MANAGEMENT
	// ================================================================
	private bool ShouldContinueTurn(string playerId)
	{
		GD.Print($"[GameStateManager] ShouldContinueTurn called - Player: {playerId}, Breaking: {_isBreakingTurn}");

		var currentPlayer = GetCurrentPlayer();
		if (currentPlayer == null || currentPlayer.PlayerId != playerId)
		{
			GD.Print($"[GameStateManager] Invalid player - returning false");
			return false;
		}

		// Handle breaking turn logic
		if (_isBreakingTurn)
		{
			GD.Print($"[GameStateManager] Breaking turn - calling HandleBreakingTurnContinuation");
			GD.Print($"[GameStateManager]   PiecesDisturbedInBreaking: {CurrentTurn?.PiecesDisturbedInBreaking}");
			GD.Print($"[GameStateManager]   ValidPocket: {_validPocketThisStroke}, Foul: {_foulThisStroke}");
			GD.Print($"[GameStateManager]   Attempts: {_breakingAttempts}/{MAX_BREAKING_ATTEMPTS}");

			bool result = HandleBreakingTurnContinuation();
			GD.Print($"[GameStateManager]   HandleBreakingTurnContinuation returned: {result}");
			return result;
		}

		// Check if opponent piece was pocketed - this ends the turn even if valid
		bool opponentPiecePocketed = CurrentTurn?.OpponentPiecePocketedThisTurn ?? false;
		bool normalResult = _validPocketThisStroke && !_foulThisStroke && !opponentPiecePocketed;
		GD.Print($"[GameStateManager] Normal turn - ValidPocket: {_validPocketThisStroke}, Foul: {_foulThisStroke}, OpponentPiece: {opponentPiecePocketed}, returning: {normalResult}");
		return normalResult;
	}

	private bool HandleBreakingTurnContinuation()
	{
		// Check if opponent piece was pocketed - always ends turn even during break
		// Per ICF Rules: Pocketing opponent piece ends your turn regardless of breaking status
		bool opponentPiecePocketed = CurrentTurn?.OpponentPiecePocketedThisTurn ?? false;
		if (opponentPiecePocketed)
		{
			_isBreakingTurn = false;
			GD.Print("[GameStateManager] Breaking turn - opponent piece pocketed, turn ends");
			return false;  // End turn
		}

		if (CurrentTurn.PiecesDisturbedInBreaking)
		{
			// Breaking successful - end breaking turn
			_isBreakingTurn = false;
			GD.Print("[GameStateManager] Breaking completed - pieces disturbed");

			var breakingResult = CarromRuleEngine.EvaluateBreakingTurn(
				_validPocketThisStroke,
				_foulThisStroke,
				_breakingAttempts,
				CurrentTurn.PiecesDisturbedInBreaking);
			return breakingResult.TurnDecision == TurnDecision.Continue;
		}
		else
		{
			// Breaking attempt failed - increment attempt counter
			_breakingAttempts++;
			CurrentTurn.BreakingAttempts = _breakingAttempts;

			if (_breakingAttempts >= MAX_BREAKING_ATTEMPTS)
			{
				// Max attempts reached - end breaking turn
				_isBreakingTurn = false;
				GD.Print($"[GameStateManager] Breaking failed after {MAX_BREAKING_ATTEMPTS} attempts");
				return false;
			}

			GD.Print($"[GameStateManager] Breaking attempt {_breakingAttempts}/{MAX_BREAKING_ATTEMPTS} - turn continues");
			return true;
		}
	}

	/// <summary>
	/// Advance to next player in turn order
	/// Internal visibility - external code should use ExecutePassTurn() or let settlement handle advancement
	/// </summary>
	internal void AdvanceToNextPlayer()
	{
		if (_playerOrder.Count == 0)
		{
			return;
		}

		// Handle end of turn for current player
		var currentPlayer = GetCurrentPlayer();
		if (currentPlayer != null)
		{
			// Check if queen needs returning (pocketed but not covered)
			// This is handled in CarromPlayer currently - will migrate later
		}

		_currentPlayerIndex = (_currentPlayerIndex + 1) % _playerOrder.Count;
		_currentTurnNumber++;

		var nextPlayer = GetCurrentPlayer();
		if (nextPlayer != null)
		{
			GD.Print($"[GameStateManager] Turn advanced to {nextPlayer.PlayerId}");
			EmitSignal(SignalName.TurnChanged, nextPlayer.PlayerId, _currentTurnNumber);
		}
	}

	/// <summary>
	/// Execute pass turn action - called when user clicks pass turn button
	/// This completes the turn transition that was waiting in Settlement phase
	/// </summary>
	public void ExecutePassTurn()
	{
		if (CurrentPhase != GamePhase.Settlement)
		{
			GD.PrintErr("[GameStateManager] ExecutePassTurn called but not in Settlement phase");
			return;
		}

		GD.Print("[GameStateManager] Executing pass turn");

		// Advance to next player
		AdvanceToNextPlayer();

		// CRITICAL: Signal mode manager to sync input controller with new player
		// This must happen BEFORE transitioning to Ready phase so baseline is correct
		var currentPlayer = GetCurrentPlayer();
		string newPlayerId = currentPlayer?.PlayerId ?? "unknown";
		EmitSignal(SignalName.TurnAdvanced, newPlayerId);

		// Stroke flags reset in EnterPhase(GamePhase.Ready) - single authoritative location

		// Complete settlement and transition to Ready
		EmitSignal(SignalName.SettlementCompleted);
		TransitionToPhase(GamePhase.Ready);
	}

	// ================================================================
	// PIECE POCKET HANDLING
	// ================================================================
	public void OnPiecePocketed(CarromPiece piece)
	{
		if (piece == null || !GodotObject.IsInstanceValid(piece))
		{
			return;
		}

		var currentPlayer = GetCurrentPlayer();
		if (currentPlayer == null)
		{
			GD.PrintErr("[GameStateManager] No current player during piece pocket");
			return;
		}

		var pieceType = piece.Type;
		GD.Print($"[GameStateManager] {pieceType} piece pocketed by {currentPlayer.PlayerId}");

		// Use rule engine to evaluate pocket
		var evaluation = CarromRuleEngine.EvaluatePocketedPiece(
			pieceType,
			currentPlayer,
			CurrentTurn);

		ApplyPocketEvaluation(evaluation, pieceType);
	}

	private void ApplyPocketEvaluation(PocketEvaluation evaluation, PieceType pocketedType)
	{
		var currentPlayer = GetCurrentPlayer();
		if (currentPlayer == null)
		{
			return;
		}

		if (evaluation.IsValid)
		{
			_validPocketThisStroke = true;

			// Record piece pocketed
			currentPlayer.PocketedPieces.Add(pocketedType);

			if (pocketedType == PieceType.Red)
			{
				currentPlayer.HasQueen = true;
				CurrentTurn.PocketedQueenThisTurn = true;
				CurrentTurn.NeedsQueenCovering = true;
			}
			else if (pocketedType == currentPlayer.AssignedPieceType)
			{
				currentPlayer.PiecesPocketed++;
				currentPlayer.ValidPockets++;

				// Check if this covers the queen
				if (CurrentTurn.NeedsQueenCovering && evaluation.CoversQueen)
				{
					currentPlayer.QueenCovered = true;
					CurrentTurn.NeedsQueenCovering = false;
				}
			}

			CurrentTurn.PiecesThisTurn.Add(pocketedType);
		}

		if (evaluation.IsFoul)
		{
			_foulThisStroke = true;
			currentPlayer.Fouls++;
			EmitSignal(SignalName.FoulCommitted, currentPlayer.PlayerId);

			// Apply penalty
			var penalty = CarromRuleEngine.CalculateFoulPenalty(evaluation.FoulType, currentPlayer);
			ApplyPenalty(penalty);
		}

		// Check win condition
		CheckAndHandleWinCondition(currentPlayer.PlayerId);
	}

	private void ApplyPenalty(PenaltyAction penalty)
	{
		if (penalty.PiecesToReturn <= 0)
		{
			return;
		}

		var currentPlayer = GetCurrentPlayer();
		if (currentPlayer == null || currentPlayer.PiecesPocketed == 0)
		{
			return;
		}

		// Return pieces (implementation simplified for now)
		int piecesToReturn = Math.Min(penalty.PiecesToReturn, currentPlayer.PiecesPocketed);
		currentPlayer.PiecesPocketed -= piecesToReturn;

		GD.Print($"[GameStateManager] Penalty: {piecesToReturn} pieces returned");
	}

	// ================================================================
	// WIN CONDITION
	// ================================================================
	private void CheckAndHandleWinCondition(string playerId)
	{
		var playerState = GetPlayerState(playerId);
		if (playerState == null)
		{
			return;
		}

		var winCondition = CarromRuleEngine.CheckWinCondition_Singles(playerState);
		if (winCondition.HasWon)
		{
			GD.Print($"[GameStateManager] {playerId} wins! {winCondition.Reason}");
			EmitSignal(SignalName.PlayerWon, playerId);
		}
	}

	// ================================================================
	// SERIALIZATION
	// ================================================================
	public string Serialize()
	{
		var snapshot = new SerializableGameState
		{
			GameMode = _gameMode.ToString(),
			CurrentPlayerIndex = _currentPlayerIndex,
			GamePhase = CurrentPhase.ToString(),
			IsBreakingTurn = _isBreakingTurn,
			BreakingAttempts = _breakingAttempts,
			CurrentTurn = TurnSnapshot.FromTurnContext(CurrentTurn),
			Players = _players.ToDictionary(
				kvp => kvp.Key,
				kvp => PlayerSnapshot.FromPlayerState(kvp.Value)),
			Pieces = [.. (_pieceFactory?.GetAllPieces() ?? (IReadOnlyList<CarromPiece>)[])
				.Where(p => GodotObject.IsInstanceValid(p))
				.Select(p => PieceSnapshot.FromPiece(p))
				.Where(s => s != null)],
		};

		return JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
		{
			WriteIndented = true,
		});
	}

	public void Load(string json)
	{
		try
		{
			var snapshot = JsonSerializer.Deserialize<SerializableGameState>(json);
			if (snapshot == null)
			{
				GD.PrintErr("[GameStateManager] Failed to deserialize snapshot");
				return;
			}

			// Restore players
			_players.Clear();
			_playerOrder.Clear();
			foreach (var kvp in snapshot.Players)
			{
				_players[kvp.Key] = kvp.Value.ToPlayerState();
				_playerOrder.Add(kvp.Key);
			}

			// Restore turn state
			CurrentTurn = snapshot.CurrentTurn.ToTurnContext();
			_currentPlayerIndex = snapshot.CurrentPlayerIndex;
			_isBreakingTurn = snapshot.IsBreakingTurn;
			_breakingAttempts = snapshot.BreakingAttempts;

			// Restore phase
			if (Enum.TryParse<GamePhase>(snapshot.GamePhase, out var phase))
			{
				TransitionToPhase(phase);
			}

			// Restore game mode
			if (Enum.TryParse<CarromGameMode>(snapshot.GameMode, out var mode))
			{
				_gameMode = mode;
			}

			GD.Print($"[GameStateManager] State loaded from snapshot ({snapshot.Players.Count} players, {snapshot.Pieces.Count} pieces)");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[GameStateManager] Failed to load state: {ex.Message}");
		}
	}

	public void ApplySnapshotToPieces(SerializableGameState snapshot, List<CarromPiece> pieceNodes)
	{
		// Match pieces by type and apply positions/velocities
		var piecesByType = pieceNodes
			.Where(p => GodotObject.IsInstanceValid(p))
			.GroupBy(p => p.Type)
			.ToDictionary(g => g.Key, g => g.ToList());

		foreach (var pieceSnapshot in snapshot.Pieces)
		{
			var pieceType = pieceSnapshot.GetPieceType();

			if (piecesByType.TryGetValue(pieceType, out var pieces) && pieces.Count > 0)
			{
				var piece = pieces[0];
				pieces.RemoveAt(0);

				piece.GlobalPosition = pieceSnapshot.GetPosition();
				piece.LinearVelocity = pieceSnapshot.GetVelocity();
				piece.AngularVelocity = pieceSnapshot.AngularVelocity;
				piece.Visible = pieceSnapshot.Visible;
			}
		}

		GD.Print($"[GameStateManager] Applied snapshot to {snapshot.Pieces.Count} pieces");
	}

	// ================================================================
	// ACCESSORS
	// ================================================================
	public PlayerState GetCurrentPlayer()
	{
		if (_playerOrder.Count == 0 || _currentPlayerIndex >= _playerOrder.Count)
		{
			return null;
		}

		var playerId = _playerOrder[_currentPlayerIndex];
		return _players.GetValueOrDefault(playerId);
	}

	public PlayerState GetPlayerState(string playerId)
	{
		return _players.GetValueOrDefault(playerId);
	}

	public int GetCurrentPlayerIndex() => _currentPlayerIndex;

	public int GetCurrentTurnNumber() => _currentTurnNumber;

	public bool IsBreakingTurn() => _isBreakingTurn;

	public int GetBreakingAttempts() => _breakingAttempts;

	public bool GetValidPocketThisStroke() => _validPocketThisStroke;

	public bool GetFoulThisStroke() => _foulThisStroke;

	/// <summary>
	/// Record a valid pocket - updates stroke flag and turn context
	/// </summary>
	public void RecordValidPocket() => _validPocketThisStroke = true;

	/// <summary>
	/// Record a foul - updates stroke flag and turn context
	/// </summary>
	public void RecordFoul() => _foulThisStroke = true;

	// ================================================================
	// ICARROMGAMESTATE INTERFACE IMPLEMENTATION
	// ================================================================
	public GamePhase GetCurrentPhase() => CurrentPhase;

	public string GetCurrentStateName() => CurrentPhase.ToString();

	/// <summary>
	/// Force transition to Ready phase (for error recovery)
	/// </summary>
	public void ForceToReady()
	{
		TransitionToPhase(GamePhase.Ready);
	}

	/// <summary>
	/// Compatibility property for old code that accessed CurrentState
	/// </summary>
	public GamePhase CurrentState => CurrentPhase;

	// ================================================================
	// COMPATIBILITY METHODS (for existing CarromGame.cs code)
	// ================================================================

	/// <summary>
	/// Get all active piece nodes for physics monitoring
	/// </summary>
	public IReadOnlyList<CarromPiece> GetActivePieces() => _pieceFactory?.GetAllPieces() ?? (IReadOnlyList<CarromPiece>)[];

	/// <summary>
	/// Trigger settlement from external systems (for compatibility)
	/// </summary>
	public void TriggerSettlement()
	{
		// Settlement is handled internally via phase transitions
		if (CurrentPhase == GamePhase.Settlement)
		{
			// Already in settlement, processing will happen automatically
			return;
		}

		// Force settlement processing
		TransitionToPhase(GamePhase.Settlement);
	}

	/// <summary>
	/// Get list of all players (for UI display)
	/// </summary>
	public List<CarromPlayer> GetPlayers()
	{
		return _playerNodes;
	}

	/// <summary>
	/// Get CarromPlayer node (not just state) by ID
	/// </summary>
	public CarromPlayer GetPlayerNode(string playerId)
	{
		return _playerNodes.FirstOrDefault(p => p != null && p.PlayerId == playerId);
	}

	/// <summary>
	/// Get current player node
	/// </summary>
	public CarromPlayer GetCurrentPlayerNode()
	{
		if (_playerOrder.Count == 0 || _currentPlayerIndex >= _playerOrder.Count)
		{
			return null;
		}

		var playerId = _playerOrder[_currentPlayerIndex];
		return GetPlayerNode(playerId);
	}

	/// <summary>
	/// Cleanup on node exit
	/// </summary>
	public override void _Notification(int what)
	{
		if (what == NotificationExitTree)
		{
			// Disconnect physics monitor signal
			if (_physicsMonitor != null && GodotObject.IsInstanceValid(_physicsMonitor))
			{
				_physicsMonitor.AllPiecesStopped -= OnAllPiecesStopped;
			}
		}
	}
}

/// <summary>
/// Internal piece state tracking
/// </summary>
internal class PieceState
{
	public Guid Id { get; set; } = Guid.NewGuid();

	public PieceType Type { get; set; }

	public Vector2 Position { get; set; }

	public Vector2 Velocity { get; set; }

	public float AngularVelocity { get; set; }

	public bool Visible { get; set; } = true;
}
