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

	// Piece templates
	private PackedScene _whitePieceTemplate;
	private PackedScene _blackPieceTemplate;
	private PackedScene _redPieceTemplate;
	private PackedScene _strikerTemplate;

	// Settings
	private int _competitiveCreditCost = 1;

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
	/// Handle competitive mode settlement - switch players and position striker
	/// </summary>
	protected override void ExecuteModeSpecificSettlement()
	{
		// Switch to next player and position striker
		// Base class handles phase transition to Active before calling this method
		SwitchToNextPlayer();
	}
	
	/// <summary>
	/// Check if win condition is met for competitive mode
	/// </summary>
	public override bool CheckWinCondition(string playerId)
	{
		// Simple win condition: check if player has pocketed all their pieces
		int remainingPieces = CountRemainingPiecesForPlayer(playerId);
		return remainingPieces == 0;
	}
	
	/// <summary>
	/// Determine if current turn should continue in competitive mode
	/// </summary>
	public override bool ShouldContinueTurn(string playerId)
	{
		// In competitive mode, turns typically switch after each strike
		// This could be enhanced with actual carrom rules for continuing turns
		return false;
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
	/// </summary>
	protected override void HandlePiecePocketed(CarromPiece piece)
	{
		// Get current player ID
		string playerId = GetCurrentPlayer()?.PlayerId ?? "player1";
		
		if (piece.Type == PieceType.Striker)
		{
			// Striker foul
			EmitSignal(SignalName.FoulCommitted, playerId);
		}
		else
		{
			// Check for continue turn or win condition
			if (CheckWinCondition(playerId))
			{
				EmitSignal(SignalName.PlayerWon, playerId);
			}
		}
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
	/// Setup players for competitive mode
	/// </summary>
	private void SetupCompetitivePlayers()
	{
		// Create players for competitive mode
		_players.Clear();
		
		var player1 = new CarromPlayer();
		player1.PlayerId = "player1";
		_players.Add(player1);
		
		_currentPlayerIndex = 0;
		
		EmitSignal(SignalName.TurnChanged, GetCurrentPlayer()?.PlayerId ?? "player1", 1);
	}

	/// <summary>
	/// Switch to next player
	/// </summary>
	public void SwitchToNextPlayer()
	{
		// Validate phase state before operations
		if (_phaseManager != null && _phaseManager.CurrentPhase != GamePhase.Active)
		{
			// Wait one frame for phase to settle, then retry
			CallDeferred(MethodName.RetryPlayerSwitch);
			return;
		}
		
		// Direct, synchronous operations - matches practice mode pattern
		_currentPlayerIndex = (_currentPlayerIndex + 1) % _players.Count;
		
		// CRITICAL FIX: Disable input before positioning striker to prevent override
		bool inputWasEnabled = false;
		if (_inputController != null && GodotObject.IsInstanceValid(_inputController))
		{
			inputWasEnabled = _inputController.IsInputEnabled();
			if (inputWasEnabled)
			{
				_inputController.SetPhaseManager(null); // Temporarily disable input
			}
		}
		
		PositionStrikerAtBaseline();
		
		// Re-enable input after positioning if it was previously enabled
		if (inputWasEnabled && _inputController != null && GodotObject.IsInstanceValid(_inputController))
		{
			_inputController.SetPhaseManager(_phaseManager); // Restore input
		}
		
		var currentPlayer = GetCurrentPlayer();
		EmitSignal(SignalName.TurnChanged, currentPlayer?.PlayerId ?? "unknown", _currentPlayerIndex + 1);
	}

	/// <summary>
	/// Retry player switch after phase validation (deferred method)
	/// </summary>
	private void RetryPlayerSwitch()
	{
		if (_phaseManager != null && _phaseManager.CurrentPhase == GamePhase.Active)
		{
			SwitchToNextPlayer();
		}
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
		return new List<CarromPiece>(_competitivePieces);
	}

	/// <summary>
	/// Clear competitive-specific state during cleanup
	/// </summary>
	protected override void ClearModeSpecificState()
	{
		_players.Clear();
		_currentPlayerIndex = 0;
	}

	/// <summary>
	/// Get all players
	/// </summary>
	public List<CarromPlayer> GetPlayers()
	{
		return [.._players];
	}
}