using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
	[Signal] public delegate void TurnChangedEventHandler(string playerId, int turnNumber);
	[Signal] public delegate void TurnReadyForPassEventHandler(string playerId, int turnNumber);
	[Signal] public delegate void PlayerWonEventHandler(string playerId);
	[Signal] public delegate void FoulCommittedEventHandler(string playerId);
	[Signal] public delegate void CompetitiveModeSetupCompleteEventHandler();
	[Signal] public delegate void RoundCompletedEventHandler(int roundNumber);
	[Signal] public delegate void MaxRoundsReachedEventHandler(string winnerId);

	// Competitive mode state
	private List<CarromPiece> _competitivePieces = new List<CarromPiece>();
	private int _currentPlayerIndex = 0;
	private List<CarromPlayer> _players = new List<CarromPlayer>();
	
	// Round tracking
	private int _currentRound = 1;
	private int _maxRounds = 50; // Default 50 rounds as per specification
	private Dictionary<string, int> _tournamentPoints = new Dictionary<string, int>();
	
	// Turn state tracking
	private bool _validPocketThisStroke = false;
	private bool _foulThisStroke = false;
	
	// Breaking turn state (OFFICIAL CARROM RULE: 3 attempts to break)
	private bool _isBreakingTurn = true;
	private int _breakingAttempts = 0;
	private const int MAX_BREAKING_ATTEMPTS = 3;
	private bool _piecesDisturbedInBreaking = false;

	// Piece templates
	private PackedScene _whitePieceTemplate;
	private PackedScene _blackPieceTemplate;
	private PackedScene _redPieceTemplate;
	private PackedScene _strikerTemplate;

	// Settings
	private int _competitiveCreditCost = 1;
	private int _playerCount = 2; // Can be 2 or 4 players

	/// <summary>
	/// Initialize with piece templates (competitive-specific version)
	/// </summary>
	public void Initialize(CarromBoard board, CarromInputController inputController, 
						   CarromPhysicsConfig physicsConfig, PackedScene whiteTemplate, 
						   PackedScene blackTemplate, PackedScene redTemplate, 
						   PackedScene strikerTemplate, int creditCost)
	{
		base.Initialize(board, inputController, physicsConfig);
		_whitePieceTemplate = whiteTemplate;
		_blackPieceTemplate = blackTemplate;
		_redPieceTemplate = redTemplate;
		_strikerTemplate = strikerTemplate;
		_competitiveCreditCost = creditCost;
	}

	// ================================================================
	// ABSTRACT METHOD IMPLEMENTATIONS
	// ================================================================
	
	/// <summary>
	/// Create and position pieces specific to competitive mode
	/// </summary>
	protected override void CreateModeSpecificPieces()
	{
		// Clear existing pieces
		_competitivePieces.Clear();
		
		// Create full set of carrom pieces in center formation
		CreateCompetitivePieces();
		
		// Setup players for competitive mode
		SetupCompetitivePlayers();
	}
	
	/// <summary>
	/// Get all pieces managed by competitive mode (excluding striker)
	/// </summary>
	protected override List<CarromPiece> GetManagedPieces()
	{
		return new List<CarromPiece>(_competitivePieces);
	}
	
	/// <summary>
	/// Clear all competitive-specific pieces
	/// </summary>
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
	/// Handle competitive mode settlement - check turn continuation before switching players
	/// </summary>
	protected override void ExecuteModeSpecificSettlement()
	{
		// Settlement only handles game rule validation and piece management
		// Turn advancement is now explicit through manual pass turn only
		
		// Get current player for settlement logic
		var currentPlayer = GetCurrentPlayer();
		string currentPlayerId = currentPlayer?.PlayerId ?? "unknown";
		
		// Check if current player should continue their turn based on game rules
		bool shouldContinue = ShouldContinueTurn(currentPlayerId);
		
		if (shouldContinue)
		{
			GD.Print($"[CarromCompetitive] {currentPlayerId} continues turn after successful pocketing");
			
			// Only restore striker if player continues their turn
			var carromGame = GetParent<CarromGame>();
			if (carromGame != null)
			{
				carromGame.RestoreStrikerToBaseline();
			}
			else
			{
				// Fallback to local striker restoration
				EnsureStrikerRestored();
				PositionStrikerAtBaseline();
			}
		}
		else
		{
			// Emit TurnReadyForPass signal to trigger pass turn button display
			// This shows the button without actually switching players yet
			// Striker restoration will happen AFTER pass turn is pressed
			EmitSignal(SignalName.TurnReadyForPass, currentPlayerId, _currentPlayerIndex + 1);
		}
	}
	
	/// <summary>
	/// Check if win condition is met for competitive mode
	/// Official Carrom Rules: Player/team wins by pocketing all assigned pieces and covering queen if pocketed
	/// </summary>
	public override bool CheckWinCondition(string playerId)
	{
		// Find the player by ID
		var player = _players.FirstOrDefault(p => p.PlayerId == playerId);
		if (player == null)
		{
			return false;
		}

		if (_playerCount == 2)
		{
			// 2-player mode: Individual player must meet win condition
			return player.HasWon();
		}
		else if (_playerCount == 4)
		{
			// 4-player doubles mode: Check team win condition
			// Team wins when combined efforts meet the win requirements
			var playerPieceType = player.AssignedPieceType;
			var teamPlayers = _players.Where(p => p.AssignedPieceType == playerPieceType).ToList();
			
			// Calculate team statistics
			int teamPiecesPocketed = teamPlayers.Sum(p => p.PiecesPocketed);
			bool teamHasQueen = teamPlayers.Any(p => p.HasQueen);
			bool teamQueenCovered = teamPlayers.Any(p => p.QueenCovered);
			
			// Team wins if they've pocketed all 9 pieces and covered queen if they have it
			return teamPiecesPocketed >= 9 && (!teamHasQueen || teamQueenCovered);
		}

		return false;
	}
	
	/// <summary>
	/// Determine if current turn should continue in competitive mode
	/// Official Carrom Rule: "As long as a player pockets his own pieces and/or Queen, his turn shall continue"
	/// OFFICIAL BREAKING RULE: Breaking player gets up to 3 attempts to disturb pieces
	/// </summary>
	public override bool ShouldContinueTurn(string playerId)
	{
		// Get the current player
		var currentPlayer = GetCurrentPlayer();
		if (currentPlayer == null || currentPlayer.PlayerId != playerId)
		{
			return false;
		}

		// OFFICIAL BREAKING RULE: Handle breaking turn logic
		if (_isBreakingTurn)
		{
			return HandleBreakingTurnContinuation();
		}
		
		// Apply official carrom rules for normal turn continuation:
		// Turn continues if player pocketed valid pieces AND committed no fouls
		// Turn ends if player committed fouls OR pocketed no valid pieces
		bool shouldContinue = _validPocketThisStroke && !_foulThisStroke;
		
		// Reset stroke flags for next stroke AFTER storing the decision
		// This ensures flags are available for settlement logic
		_validPocketThisStroke = false;
		_foulThisStroke = false;
		
		return shouldContinue;
	}
	
	/// <summary>
	/// Position striker at baseline for current player
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
		
		// Use board's proportional baseline positioning for current player
		Vector2 baselinePosition = _board.GetBaselinePosition(_currentPlayerIndex);
		var globalPosition = _board.ToGlobal(baselinePosition);
		
		// Use synchronized Reset method instead of direct position assignment
		// This ensures physics engine state is immediately synchronized
		_striker.Reset(globalPosition);
	}
	
	/// <summary>
	/// Handle competitive-specific piece pocketing logic
	/// Implements official carrom rules for piece pocketing, fouls, and turn continuation
	/// </summary>
	protected override void HandlePiecePocketed(CarromPiece piece)
	{
		// Get current player
		var currentPlayer = GetCurrentPlayer();
		if (currentPlayer == null)
		{
			GD.PrintErr($"[CarromCompetitive] No current player during piece pocket: {piece?.Type}. Attempting player reset.");
			
			// Attempt recovery - reset to player 0
			_currentPlayerIndex = 0;
			currentPlayer = GetCurrentPlayer();
			
			if (currentPlayer == null)
			{
				GD.PrintErr($"[CarromCompetitive] Player reset failed. Aborting piece processing. Players count: {_players.Count}");
				return;
			}
		}

		string playerId = currentPlayer.PlayerId;
		bool validPocket = false;

		// Handle different piece types according to carrom rules
		switch (piece.Type)
		{
			case PieceType.Striker:
				// Striker pocketing is always a foul
				_foulThisStroke = true;
				currentPlayer.RecordFoul();
				EmitSignal(SignalName.FoulCommitted, playerId);
				// Apply striker pocketing penalty
				ApplyFoulPenalty(currentPlayer, FoulType.StrikerPocketed);
				break;

			case PieceType.Red: // Queen
				// OFFICIAL RULE FIX: Queen can be pocketed at any time during the game
				// The covering eligibility is validated at end of turn, not during pocketing
				if (currentPlayer.CanPocketQueen())
				{
					validPocket = true;
					_validPocketThisStroke = true;
					currentPlayer.RecordPocketedPiece(PieceType.Red);
					GD.Print($"[CarromCompetitive] {playerId} pocketed queen - can cover: {currentPlayer.CanCoverQueen()}");
				}
				else
				{
					// Player already has queen - this is a foul (second queen attempt)
					_foulThisStroke = true;
					currentPlayer.RecordFoul();
					EmitSignal(SignalName.FoulCommitted, playerId);
					// Return the queen to center and apply additional penalty
					ReturnPieceToCenter(PieceType.Red);
					ApplyFoulPenalty(currentPlayer, FoulType.ImproperQueenPocketing);
				}
				break;

			case PieceType.White:
			case PieceType.Black:
				// Check if this is the player's assigned piece type
				if (currentPlayer.IsAssignedPiece(piece.Type))
				{
					validPocket = true;
					_validPocketThisStroke = true;
					currentPlayer.RecordPocketedPiece(piece.Type);
				}
				else
				{
					// Pocketing opponent's piece is a foul
					_foulThisStroke = true;
					currentPlayer.RecordFoul();
					EmitSignal(SignalName.FoulCommitted, playerId);
					// Return the opponent's piece to center and apply additional penalty
					ReturnPieceToCenter(piece.Type);
					ApplyFoulPenalty(currentPlayer, FoulType.OpponentPiecePocketed);
				}
				break;
		}

		// Check win condition after valid pocketing
		if (validPocket && CheckWinCondition(playerId))
		{
			// Award tournament points to winner
			AwardTournamentPointsToWinner(currentPlayer);
			
			EmitSignal(SignalName.PlayerWon, playerId);
		}

		// Note: Turn continuation is determined by ShouldContinueTurn() after all pieces settle
		// Valid pocketing (validPocket = true) allows turn to continue
		// Fouls (_foulThisStroke = true) end the turn
	}

	// ================================================================
	// COMPETITIVE MODE SPECIFIC METHODS
	// ================================================================

	/// <summary>
	/// Start competitive mode with credit checking
	/// </summary>
	public async Task<bool> StartCompetitiveMode()
	{
		// Check credits for competitive mode
		if (_competitiveCreditCost > 0)
		{
			var creditManager = CreditManager.GetInstance();
			if (creditManager != null && IsInstanceValid(creditManager))
			{
				var userManager = UserManager.GetAutoload();
				if (userManager != null && IsInstanceValid(userManager))
				{
					userManager.ResetUserIdleTimer();
				}
				
				bool creditsSpent = await creditManager.CheckAndSpendCredits(_competitiveCreditCost, "Competitive Carrom");
				if (!creditsSpent)
				{
					return false;
				}
			}
		}

		// Setup competitive mode
		SetupCompetitiveMode();

		return true;
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
			Vector2 pos = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * secondRowRadius;
			
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
			_ => null
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
	/// Set the maximum number of rounds for this tournament
	/// </summary>
	public void SetMaxRounds(int maxRounds)
	{
		_maxRounds = maxRounds > 0 ? maxRounds : 50; // Default to 50 if invalid
	}
	
	/// <summary>
	/// Get current round information
	/// </summary>
	public (int current, int max) GetRoundInfo()
	{
		return (_currentRound, _maxRounds);
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
			_players.Add(player1);
			
			var player2 = new CarromPlayer();
			player2.PlayerId = "player2";
			player2.AssignPieceType(PieceType.Black);
			player2.ResetGameStats();
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
			_players.Add(player1);

			var player2 = new CarromPlayer();
			player2.PlayerId = "player2";
			player2.AssignPieceType(PieceType.Black);
			player2.ResetGameStats();
			_players.Add(player2);

			var player3 = new CarromPlayer();
			player3.PlayerId = "player3";
			player3.AssignPieceType(PieceType.White); // Partner with player1
			player3.ResetGameStats();
			_players.Add(player3);

			var player4 = new CarromPlayer();
			player4.PlayerId = "player4";
			player4.AssignPieceType(PieceType.Black); // Partner with player2
			player4.ResetGameStats();
			_players.Add(player4);
		}
		
		// Start with player 1 (white pieces) as breaking player
		_currentPlayerIndex = 0;
		
		// Sync input controller with initial player
		_inputController?.SetCurrentPlayer(_currentPlayerIndex);
		
		EmitSignal(SignalName.TurnChanged, GetCurrentPlayer()?.PlayerId ?? "player1", 1);
	}

	/// <summary>
	/// Switch to next player - synchronous, linear operation
	/// OFFICIAL CARROM RULE: Handle queen covering validation at end of turn
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
				// Return uncovered queen to center
				ReturnPieceToCenter(PieceType.Red);
				GD.Print($"[CarromCompetitive] Returned uncovered queen to center for {currentPlayer.PlayerId}");
			}
		}
		
		// Step 2: Switch to next player index
		_currentPlayerIndex = (_currentPlayerIndex + 1) % _players.Count;
		
		// Check for round completion - when we cycle back to first player
		if (_currentPlayerIndex == 0)
		{
			_currentRound++;
			EmitSignal(SignalName.RoundCompleted, _currentRound);
			GD.Print($"[CarromCompetitive] Round {_currentRound - 1} completed, starting round {_currentRound}");
			
			// Check if max rounds reached
			if (_currentRound > _maxRounds)
			{
				string winnerId = DetermineWinnerByPoints();
				EmitSignal(SignalName.MaxRoundsReached, winnerId);
				GD.Print($"[CarromCompetitive] Max rounds ({_maxRounds}) reached, tournament won by {winnerId}");
				return; // End the game, don't continue with turn setup
			}
		}

		// Step 3: Start new turn for next player
		var nextPlayer = GetCurrentPlayer();
		nextPlayer?.StartNewTurn();

		// Step 4: Sync input controller with new player
		_inputController?.SetCurrentPlayer(_currentPlayerIndex);

		// Step 5: Emit turn changed signal (striker positioning will happen after camera transition)
		EmitSignal(SignalName.TurnChanged, nextPlayer?.PlayerId ?? "unknown", _currentPlayerIndex + 1);
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
	/// Get current player
	/// </summary>
	public CarromPlayer GetCurrentPlayer()
	{
		if (_currentPlayerIndex >= 0 && _currentPlayerIndex < _players.Count)
		{
			return _players[_currentPlayerIndex];
		}
		return null;
	}

	/// <summary>
	/// Get all competitive pieces
	/// </summary>
	public List<CarromPiece> GetCompetitivePieces()
	{
		return [.._competitivePieces];
	}

	/// <summary>
	/// Clear competitive-specific state during cleanup
	/// </summary>
	protected override void ClearModeSpecificState()
	{
		_players.Clear();
		_currentPlayerIndex = 0;
		
		// Reset breaking turn state
		_isBreakingTurn = true;
		_breakingAttempts = 0;
		_piecesDisturbedInBreaking = false;
		
		// Reset round tracking
		_currentRound = 1;
		_tournamentPoints.Clear();
		
		// Sync input controller with reset player
		_inputController?.SetCurrentPlayer(_currentPlayerIndex);
	}

	/// <summary>
	/// Handle breaking turn continuation logic
	/// OFFICIAL RULE: Breaking player gets up to 3 attempts to disturb the central formation
	/// </summary>
	private bool HandleBreakingTurnContinuation()
	{
		_breakingAttempts++;
		
		// Check if pieces were disturbed in this breaking attempt
		bool piecesDisturbedThisAttempt = CheckIfPiecesWereDisturbed();
		if (piecesDisturbedThisAttempt)
		{
			_piecesDisturbedInBreaking = true;
		}
		
		// If pieces were disturbed OR player pocketed valid pieces, breaking is successful
		if (_piecesDisturbedInBreaking || _validPocketThisStroke)
		{
			// Breaking successful - end breaking turn, continue with normal rules
			_isBreakingTurn = false;
			GD.Print($"[CarromCompetitive] Breaking successful on attempt {_breakingAttempts}");
			
			// Apply normal turn continuation rules
			bool shouldContinue = _validPocketThisStroke && !_foulThisStroke;
			
			// Reset stroke flags
			_validPocketThisStroke = false;
			_foulThisStroke = false;
			
			return shouldContinue;
		}
		
		// Check if max attempts reached
		if (_breakingAttempts >= MAX_BREAKING_ATTEMPTS)
		{
			// Breaking failed after 3 attempts - end turn and pass to opponent
			_isBreakingTurn = false;
			GD.Print($"[CarromCompetitive] Breaking failed after {MAX_BREAKING_ATTEMPTS} attempts - passing turn");
			
			// Reset stroke flags
			_validPocketThisStroke = false;
			_foulThisStroke = false;
			
			return false; // End turn
		}
		
		// Still have breaking attempts left - continue turn
		GD.Print($"[CarromCompetitive] Breaking attempt {_breakingAttempts}/{MAX_BREAKING_ATTEMPTS} - pieces disturbed: {piecesDisturbedThisAttempt}");
		
		// Reset stroke flags for next breaking attempt
		_validPocketThisStroke = false;
		_foulThisStroke = false;
		
		return true; // Continue breaking attempts
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
			if (!GodotObject.IsInstanceValid(piece) || !piece.Visible) continue;
			
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
	/// Award tournament points to the winning player according to ICF rules
	/// </summary>
	private void AwardTournamentPointsToWinner(CarromPlayer winner)
	{
		if (winner == null) return;
		
		// Calculate tournament points based on opponent pieces remaining
		int tournamentPoints = winner.CalculateTournamentPoints(_players);
		
		// Award points to winner
		winner.AwardTournamentPoints(tournamentPoints);
		
		// Record losses for other players
		foreach (var player in _players)
		{
			if (player.PlayerId != winner.PlayerId)
			{
				player.RecordGameLoss();
			}
		}
		
		GD.Print($"[Tournament] Game complete - {winner.PlayerId} awarded {tournamentPoints} points");
		
		// Check if winner has won the tournament
		if (winner.HasWonTournament())
		{
			GD.Print($"[Tournament] {winner.PlayerId} has won the tournament with {winner.TournamentPoints} points!");
			// Could emit a TournamentWon signal here if needed
		}
	}
	
	/// <summary>
	/// Get current tournament standings sorted by points
	/// </summary>
	public List<CarromPlayer> GetTournamentStandings()
	{
		return _players.OrderByDescending(p => p.TournamentPoints)
					  .ThenByDescending(p => p.GetWinPercentage())
					  .ToList();
	}
	
	/// <summary>
	/// Get tournament summary for display
	/// </summary>
	public string GetTournamentSummary()
	{
		var standings = GetTournamentStandings();
		var summary = new System.Text.StringBuilder("=== TOURNAMENT STANDINGS ===\n\n");
		
		for (int i = 0; i < standings.Count; i++)
		{
			var player = standings[i];
			string position = (i + 1) switch
			{
				1 => "1st",
				2 => "2nd",
				3 => "3rd",
				4 => "4th",
				_ => $"{i + 1}th"
			};
			
			summary.AppendLine($"{position}: {player.PlayerId}");
			summary.AppendLine($"   Points: {player.TournamentPoints}");
			summary.AppendLine($"   Games: {player.GamesWon}/{player.GamesPlayed}");
			summary.AppendLine($"   Win Rate: {player.GetWinPercentage():P1}");
			summary.AppendLine(); // Empty line between players
		}
		
		return summary.ToString();
	}
	
	/// <summary>
	/// Reset tournament for all players
	/// </summary>
	public void ResetTournament()
	{
		foreach (var player in _players)
		{
			player.ResetTournamentStats();
		}
		GD.Print("[Tournament] Tournament statistics reset for all players");
	}
	
	/// <summary>
	/// Determine winner based on current scores when max rounds are reached
	/// Uses tournament points system per ICF rules
	/// </summary>
	private string DetermineWinnerByPoints()
	{
		if (_players.Count == 0)
		{
			return "unknown";
		}
		
		// Calculate current tournament points for each player
		// Tournament points = opponent's remaining pieces (max 12 per round)
		// Queen adds 3 points if covered and player wins
		var playerScores = new Dictionary<string, int>();
		
		foreach (var player in _players)
		{
			int tournamentPoints = player.TournamentPoints;
			
			// Add current round points (pieces pocketed)
			int currentRoundPoints = CalculateCurrentRoundPoints(player);
			
			playerScores[player.PlayerId] = tournamentPoints + currentRoundPoints;
		}
		
		// Find player with highest score
		var winner = playerScores.Aggregate((l, r) => l.Value > r.Value ? l : r);
		
		GD.Print($"[Tournament] Final scores when max rounds reached:");
		foreach (var score in playerScores)
		{
			GD.Print($"  {score.Key}: {score.Value} points");
		}
		
		return winner.Key;
	}
	
	/// <summary>
	/// Calculate tournament points for current round based on ICF rules
	/// </summary>
	private int CalculateCurrentRoundPoints(CarromPlayer player)
	{
		// Tournament points = opponent's remaining pieces
		int opponentRemainingPieces = 0;
		
		foreach (var opponent in _players)
		{
			if (opponent.PlayerId != player.PlayerId)
			{
				// Count remaining pieces (9 total - pocketed)
				opponentRemainingPieces += Mathf.Max(0, 9 - opponent.PiecesPocketed);
			}
		}
		
		// Add queen points if player has queen covered
		int queenPoints = 0;
		if (player.HasQueen && player.QueenCovered)
		{
			// Queen is worth 3 points, but only up to 21 total points per ICF rules
			queenPoints = 3;
		}
		
		// Maximum 12 points per board per ICF rules
		int totalPoints = Mathf.Min(12, opponentRemainingPieces + queenPoints);
		
		return totalPoints;
	}
	
	/// <summary>
	/// Get all players
	/// </summary>
	public List<CarromPlayer> GetPlayers()
	{
		return [.._players];
	}

	// ================================================================
	// FOUL PENALTY SYSTEM
	// ================================================================

	/// <summary>
	/// Apply foul penalty according to official carrom rules
	/// OFFICIAL RULES:
	/// - Striker pocketing: Return one pocketed piece
	/// - Opponent piece pocketing: Return opponent's piece + additional penalty piece
	/// - Improper queen pocketing: Return queen + penalty piece
	/// </summary>
	private void ApplyFoulPenalty(CarromPlayer foulPlayer, FoulType foulType = FoulType.General)
	{
		if (foulPlayer == null)
			return;

		switch (foulType)
		{
			case FoulType.StrikerPocketed:
				// Striker pocketing: Return one pocketed piece
				ApplyStandardPenalty(foulPlayer, "striker pocketing");
				break;
				
			case FoulType.OpponentPiecePocketed:
				// Opponent piece pocketing: Additional penalty piece beyond returning opponent's piece
				ApplyStandardPenalty(foulPlayer, "opponent piece pocketing penalty");
				break;
				
			case FoulType.ImproperQueenPocketing:
				// Improper queen pocketing: Additional penalty beyond returning queen
				ApplyStandardPenalty(foulPlayer, "improper queen pocketing penalty");
				break;
				
			case FoulType.General:
			default:
				// General foul: Standard penalty
				ApplyStandardPenalty(foulPlayer, "general foul");
				break;
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
			// Create the piece at center of board
			ReturnPieceToCenter(pieceTypeToReturn.Value);
			
			GD.Print($"[FOUL PENALTY] {foulPlayer.PlayerId} returns {pieceTypeToReturn.Value} piece to center for {foulReason}");
		}
	}

	/// <summary>
	/// Create a piece at the center of the board (for foul penalties and queen returns)
	/// </summary>
	private void ReturnPieceToCenter(PieceType pieceType)
	{
		if (_board == null) return;

		// FIRST: Try to find and restore an existing hidden piece of this type
		var hiddenPiece = FindHiddenPieceOfType(pieceType);
		if (hiddenPiece != null)
		{
			// Find a safe position near center that doesn't overlap with existing pieces
			Vector2 centerPosition = _board.GetCenterPosition();
			Vector2 safePosition = FindSafePositionNearCenter(centerPosition, pieceType);
			
			// Restore the existing hidden piece instead of creating new one
			hiddenPiece.Reset(safePosition, validatePlacement: true);
			return;
		}

		// FALLBACK: Only create new piece if no hidden piece found
		Vector2 fallbackPosition = FindSafePositionNearCenter(_board.GetCenterPosition(), pieceType);
		var returnedPiece = CreatePiece(pieceType, fallbackPosition);
		if (returnedPiece != null && pieceType != PieceType.Striker)
		{ 
			// Add to competitive pieces list so it's tracked
			_competitivePieces.Add(returnedPiece);
		}
	}

	/// <summary>
	/// Find a hidden (pocketed) piece of the specified type that can be restored
	/// </summary>
	private CarromPiece FindHiddenPieceOfType(PieceType pieceType)
	{
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
					continue;

				float distance = testPosition.DistanceTo(existingPiece.GlobalPosition);
				if (distance < pieceRadius * 2.2f) // Pieces need to be separated
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
				Mathf.Sin(angle) * searchRadius
			);
			
			// Increase search radius for next attempt
			searchRadius += pieceRadius * 0.5f;
		}

		// Fallback to center if no safe position found
		GD.PrintErr($"[WARNING] Could not find safe position for returned {pieceType} piece, using center");
		return center;
	}
}