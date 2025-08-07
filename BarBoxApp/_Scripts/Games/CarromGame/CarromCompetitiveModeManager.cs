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
	[Signal] public delegate void PlayerWonEventHandler(string playerId);
	[Signal] public delegate void FoulCommittedEventHandler(string playerId);
	[Signal] public delegate void CompetitiveModeSetupCompleteEventHandler();

	// Competitive mode state
	private List<CarromPiece> _competitivePieces = new List<CarromPiece>();
	private int _currentPlayerIndex = 0;
	private List<CarromPlayer> _players = new List<CarromPlayer>();
	
	// Turn state tracking
	private bool _validPocketThisStroke = false;
	private bool _foulThisStroke = false;

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
	/// Handle competitive mode settlement - switch players then restore striker to new player's baseline
	/// </summary>
	protected override void ExecuteModeSpecificSettlement()
	{
		// First, switch to next player (this updates the current player index)
		SwitchToNextPlayer();
		
		// Then restore striker to the NEW player's baseline
		// Use centralized striker restoration through parent CarromGame
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
	/// </summary>
	public override bool ShouldContinueTurn(string playerId)
	{
		// Get the current player
		var currentPlayer = GetCurrentPlayer();
		if (currentPlayer == null || currentPlayer.PlayerId != playerId)
		{
			return false;
		}

		// Apply official carrom rules for turn continuation:
		// Turn continues if player pocketed valid pieces AND committed no fouls
		// Turn ends if player committed fouls OR pocketed no valid pieces
		bool shouldContinue = _validPocketThisStroke && !_foulThisStroke;
		
		// Reset stroke flags for next stroke
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
			return;
		}

		string playerId = currentPlayer.PlayerId;
		bool isFoul = false;
		bool validPocket = false;

		// Handle different piece types according to carrom rules
		switch (piece.Type)
		{
			case PieceType.Striker:
				// Striker pocketing is always a foul
				isFoul = true;
				_foulThisStroke = true;
				currentPlayer.RecordFoul();
				EmitSignal(SignalName.FoulCommitted, playerId);
				// Apply foul penalty (return piece)
				ApplyFoulPenalty(currentPlayer);
				break;

			case PieceType.Red: // Queen
				// Queen can only be pocketed if player has at least one assigned piece
				if (currentPlayer.CanPocketQueen())
				{
					validPocket = true;
					_validPocketThisStroke = true;
					currentPlayer.RecordPocketedPiece(PieceType.Red);
				}
				else
				{
					// Pocketing queen without eligibility - return queen to center and apply penalty
					isFoul = true;
					_foulThisStroke = true;
					currentPlayer.RecordFoul();
					EmitSignal(SignalName.FoulCommitted, playerId);
					// Return the queen to center and apply additional penalty
					ReturnPieceToCenter(PieceType.Red);
					ApplyFoulPenalty(currentPlayer);
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
					isFoul = true;
					_foulThisStroke = true;
					currentPlayer.RecordFoul();
					EmitSignal(SignalName.FoulCommitted, playerId);
					// Return the opponent's piece to center and apply penalty
					ReturnPieceToCenter(piece.Type);
					ApplyFoulPenalty(currentPlayer);
				}
				break;
		}

		// Check win condition after valid pocketing
		if (validPocket && CheckWinCondition(playerId))
		{
			EmitSignal(SignalName.PlayerWon, playerId);
		}

		// Note: Turn continuation is determined by ShouldContinueTurn() after all pieces settle
		// Valid pocketing (validPocket = true) allows turn to continue
		// Fouls (isFoul = true) end the turn
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
		if (playerCount == 2 || playerCount == 4)
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
	/// </summary>
	public void SwitchToNextPlayer()
	{ 
		// Step 1: Switch to next player index
		_currentPlayerIndex = (_currentPlayerIndex + 1) % _players.Count;

		// Step 2: Sync input controller with new player
		_inputController?.SetCurrentPlayer(_currentPlayerIndex);

		// Step 3: Position striker at new player's baseline (no input control needed here)
		PositionStrikerAtBaseline();

		// Step 4: Emit turn changed signal
		var currentPlayer = GetCurrentPlayer();
		EmitSignal(SignalName.TurnChanged, currentPlayer?.PlayerId ?? "unknown", _currentPlayerIndex + 1);
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
		
		// Sync input controller with reset player
		_inputController?.SetCurrentPlayer(_currentPlayerIndex);
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
	/// Apply foul penalty - return one of the player's pocketed pieces to center
	/// </summary>
	private void ApplyFoulPenalty(CarromPlayer foulPlayer)
	{
		if (foulPlayer == null || !foulPlayer.CanReturnPiece())
			return;

		// Get a piece to return from the player's pocketed pieces
		var pieceTypeToReturn = foulPlayer.ReturnPocketedPiece();
		if (pieceTypeToReturn.HasValue)
		{
			// Create the piece at center of board
			ReturnPieceToCenter(pieceTypeToReturn.Value);
			
			GD.Print($"[FOUL PENALTY] {foulPlayer.PlayerId} returns {pieceTypeToReturn.Value} piece to center");
		}
	}

	/// <summary>
	/// Create a piece at the center of the board (for foul penalties and queen returns)
	/// </summary>
	private void ReturnPieceToCenter(PieceType pieceType)
	{
		if (_board == null) return;

		// Find a safe position near center that doesn't overlap with existing pieces
		Vector2 centerPosition = _board.GetCenterPosition();
		Vector2 safePosition = FindSafePositionNearCenter(centerPosition, pieceType);

		// Create the piece using the centralized factory
		var returnedPiece = CreatePiece(pieceType, safePosition);
		if (returnedPiece != null)
		{
			// Add to competitive pieces list so it's tracked
			if (pieceType != PieceType.Striker)
			{
				_competitivePieces.Add(returnedPiece);
			}

			GD.Print($"[PIECE RETURN] {pieceType} piece returned to center at {safePosition}");
		}
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
				if (!GodotObject.IsInstanceValid(existingPiece) || !existingPiece.Visible)
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