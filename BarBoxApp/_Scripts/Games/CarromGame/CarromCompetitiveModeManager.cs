using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Manages competitive mode functionality for Carrom game
/// </summary>
[GlobalClass]
public partial class CarromCompetitiveModeManager : Node2D, ICarromModeManager
{
	[Signal] public delegate void TurnChangedEventHandler(string playerId, int turnNumber);
	[Signal] public delegate void PlayerWonEventHandler(string playerId);
	[Signal] public delegate void FoulCommittedEventHandler(string playerId);
	[Signal] public delegate void CompetitiveModeSetupCompleteEventHandler();

	// Dependencies
	private CarromBoard _board;
	private CarromInputController _inputController;
	private CarromPhysicsConfig _physicsConfig;
	private CarromPieceFactory _pieceFactory;

	// Competitive mode state
	private List<CarromPiece> _competitivePieces = new List<CarromPiece>();
	private CarromPiece _striker;
	private int _currentPlayerIndex = 0;
	private List<CarromPlayer> _players = new List<CarromPlayer>();

	// Piece templates
	private PackedScene _whitePieceTemplate;
	private PackedScene _blackPieceTemplate;
	private PackedScene _redPieceTemplate;
	private PackedScene _strikerTemplate;

	// Settings
	private int _competitiveCreditCost = 1;
	private CarromPhaseManager _phaseManager;
	
	/// <summary>
	/// Check if the mode is currently active/initialized
	/// </summary>
	public bool IsActive { get; private set; } = false;

	/// <summary>
	/// Initialize the competitive mode manager
	/// </summary>
	public void Initialize(CarromBoard board, CarromInputController inputController, CarromPhysicsConfig physicsConfig)
	{
		_board = board;
		_inputController = inputController;
		_physicsConfig = physicsConfig;
		IsActive = true;
		
		// Configure physics config with official board scaling for proportional piece sizes
		if (_board != null && _physicsConfig != null)
		{
			_physicsConfig.SetBoardScaling(
				_board.ScaleFactor,
				_board.PieceRadius,
				_board.OfficialStrikerRadius
			);
		}
		
		GD.Print("[CarromCompetitiveModeManager] Initialized with board scaling");
	}
	
	/// <summary>
	/// Set the phase manager for phase-aware operations
	/// </summary>
	public void SetPhaseManager(CarromPhaseManager phaseManager)
	{
		_phaseManager = phaseManager;
		GD.Print("[CarromCompetitiveModeManager] Phase manager set");
	}

	/// <summary>
	/// Set the piece factory for centralized piece creation
	/// </summary>
	public void SetPieceFactory(CarromPieceFactory pieceFactory)
	{
		_pieceFactory = pieceFactory;
		GD.Print("[CarromCompetitiveModeManager] Piece factory set");
	}
	
	/// <summary>
	/// Initialize with piece templates (competitive-specific version)
	/// </summary>
	public void Initialize(CarromBoard board, CarromInputController inputController, 
						   CarromPhysicsConfig physicsConfig, PackedScene whiteTemplate, 
						   PackedScene blackTemplate, PackedScene redTemplate, 
						   PackedScene strikerTemplate, int creditCost)
	{
		Initialize(board, inputController, physicsConfig);
		_whitePieceTemplate = whiteTemplate;
		_blackPieceTemplate = blackTemplate;
		_redPieceTemplate = redTemplate;
		_strikerTemplate = strikerTemplate;
		_competitiveCreditCost = creditCost;
	}

	/// <summary>
	/// Start competitive mode with credit checking
	/// </summary>
	public async Task<bool> StartCompetitiveMode()
	{
		// Check credits for competitive mode
		if (_competitiveCreditCost > 0)
		{
			var creditManager = CreditManager.GetInstance();
			if (creditManager != null && GodotObject.IsInstanceValid(creditManager))
			{
				var userManager = UserManager.GetAutoload();
				if (userManager != null && GodotObject.IsInstanceValid(userManager))
				{
					userManager.ResetUserIdleTimer();
				}
				
				bool creditsSpent = await creditManager.CheckAndSpendCredits(_competitiveCreditCost, "Competitive Carrom");
				if (!creditsSpent)
				{
					GD.Print("Competitive carrom cancelled - credits not spent");
					return false;
				}
			}
		}
		
		// Setup competitive mode
		SetupCompetitiveMode();
		
		GD.Print("[CarromCompetitiveModeManager] Competitive mode started");
		return true;
	}

	/// <summary>
	/// Setup competitive mode pieces (full carrom layout)
	/// </summary>
	public void SetupMode()
	{
		SetupCompetitiveMode();
	}
	
	/// <summary>
	/// Setup competitive mode pieces (full carrom layout) - internal implementation
	/// </summary>
	public void SetupCompetitiveMode()
	{
		if (_board == null) return;
		
		// Validate phase state before setup
		if (_phaseManager != null && !_phaseManager.CanExecuteAdminOperation("Competitive mode setup"))
		{
			return;
		}
		
		// Clear existing pieces
		ClearCompetitivePieces();
		
		// Create striker
		_striker = CreateCompetitivePiece(PieceType.Striker, Vector2.Zero);
		_inputController?.SetStriker(_striker);
		
		// Create full set of carrom pieces in center formation
		CreateCompetitivePieces();
		
		// Position striker at baseline
		PositionStrikerAtBaseline();
		
		// Setup players for competitive mode
		SetupCompetitivePlayers();
		
		GD.Print("[CarromCompetitiveModeManager] Competitive mode setup complete");
		EmitSignal(SignalName.CompetitiveModeSetupComplete);
	}

	/// <summary>
	/// Handle piece being pocketed in competitive mode
	/// </summary>
	public void OnPiecePocketed(CarromPiece piece)
	{
		// Get current player ID
		string playerId = GetCurrentPlayer()?.PlayerId ?? "player1";
		OnPiecePocketed(playerId, piece);
	}
	
	/// <summary>
	/// Handle piece being pocketed in competitive mode (with player ID)
	/// </summary>
	public void OnPiecePocketed(string playerId, CarromPiece piece)
	{
		if (piece.Type == PieceType.Striker)
		{
			// Striker foul
			EmitSignal(SignalName.FoulCommitted, playerId);
		}
		else
		{
			// Check for continue turn or win condition
			CheckWinCondition(playerId);
		}
	}
	
	/// <summary>
	/// Handle pieces settled (called when all pieces have stopped)
	/// </summary>
	public void OnPiecesSettled()
	{
		// In competitive mode, switch to next player when pieces settle
		SwitchToNextPlayer();
		
		// Transition back to Active phase for next turn
		_phaseManager?.TransitionTo(GamePhase.Active);
	}

	/// <summary>
	/// Create competitive pieces in center formation
	/// </summary>
	private void CreateCompetitivePieces()
	{
		// Traditional carrom setup: 9 white, 9 black, 1 red queen
		// Arranged in center circle formation
		
		Vector2 center = Vector2.Zero;
		float radius = 60.0f;
		
		// Create pieces in a hexagonal pattern
		var pieces = new List<PieceType>();
		
		// Add pieces to pattern (9 white, 9 black, 1 red)
		for (int i = 0; i < 9; i++) pieces.Add(PieceType.White);
		for (int i = 0; i < 9; i++) pieces.Add(PieceType.Black);
		pieces.Add(PieceType.Red); // Queen in center
		
		// Arrange in hexagonal pattern
		CreateCompetitivePiece(PieceType.Red, center); // Queen in center
		
		// Inner ring
		for (int i = 0; i < 6 && i < pieces.Count - 1; i++)
		{
			float angle = i * Mathf.Pi / 3;
			Vector2 pos = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (radius * 0.5f);
			CreateCompetitivePiece(pieces[i], pos);
		}
		
		// Outer ring
		for (int i = 6; i < 18 && i < pieces.Count - 1; i++)
		{
			float angle = (i - 6) * Mathf.Pi / 6;
			Vector2 pos = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
			CreateCompetitivePiece(pieces[i], pos);
		}
	}

	/// <summary>
	/// Create a competitive piece using the centralized piece factory
	/// </summary>
	private CarromPiece CreateCompetitivePiece(PieceType type, Vector2 position)
	{
		if (_pieceFactory == null)
		{
			GD.PrintErr("[CarromCompetitiveModeManager] Piece factory not set - cannot create pieces");
			return null;
		}
		
		// Use centralized piece factory which applies physics parameters
		var piece = _pieceFactory.CreatePiece(type, position);
		
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
	/// Position striker at baseline
	/// </summary>
	private void PositionStrikerAtBaseline()
	{
		if (_striker == null || _board == null) return;
		
		// Use board's proportional baseline positioning for current player
		Vector2 baselinePosition = _board.GetBaselinePosition(_currentPlayerIndex);
		_striker.Position = baselinePosition;
		_striker.LinearVelocity = Vector2.Zero;
		_striker.AngularVelocity = 0.0f;
		
		GD.Print($"[CarromCompetitiveModeManager] Striker positioned at baseline: {baselinePosition}");
	}

	/// <summary>
	/// Switch to next player
	/// </summary>
	public void SwitchToNextPlayer()
	{
		// Validate phase state before switching players
		if (_phaseManager != null && !_phaseManager.CanExecuteAdminOperation("Player switch"))
		{
			return;
		}
		
		_currentPlayerIndex = (_currentPlayerIndex + 1) % _players.Count;
		PositionStrikerAtBaseline();
		
		var currentPlayer = GetCurrentPlayer();
		EmitSignal(SignalName.TurnChanged, currentPlayer?.PlayerId ?? "unknown", _currentPlayerIndex + 1);
		
		GD.Print($"[CarromCompetitiveModeManager] Switched to player: {currentPlayer?.PlayerId}");
	}

	/// <summary>
	/// Check win condition
	/// </summary>
	private void CheckWinCondition(string playerId)
	{
		// Simple win condition: check if player has pocketed all their pieces
		// This would be expanded with proper carrom rules
		
		int remainingPieces = CountRemainingPiecesForPlayer(playerId);
		if (remainingPieces == 0)
		{
			EmitSignal(SignalName.PlayerWon, playerId);
		}
	}

	/// <summary>
	/// Count remaining pieces for player
	/// </summary>
	private int CountRemainingPiecesForPlayer(string playerId)
	{
		// Simplified logic - would need proper player piece assignment
		return _competitivePieces.Count(p => p.Visible);
	}

	/// <summary>
	/// Clear all competitive pieces
	/// </summary>
	private void ClearCompetitivePieces()
	{
		foreach (var piece in _competitivePieces)
		{
			if (GodotObject.IsInstanceValid(piece))
			{
				piece.QueueFree();
			}
		}
		_competitivePieces.Clear();
		
		if (_striker != null && GodotObject.IsInstanceValid(_striker))
		{
			_striker.QueueFree();
			_striker = null;
		}
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
	/// Get current striker
	/// </summary>
	public CarromPiece GetStriker()
	{
		return _striker;
	}

	/// <summary>
	/// Get all competitive pieces
	/// </summary>
	public List<CarromPiece> GetCompetitivePieces()
	{
		return new List<CarromPiece>(_competitivePieces);
	}
	
	/// <summary>
	/// Get all active pieces in this mode
	/// </summary>
	public List<CarromPiece> GetActivePieces()
	{
		var allPieces = new List<CarromPiece>(_competitivePieces);
		if (_striker != null && GodotObject.IsInstanceValid(_striker))
		{
			allPieces.Add(_striker);
		}
		return allPieces;
	}
	
	
	/// <summary>
	/// Clean up mode resources (pieces, references, etc.)
	/// </summary>
	public void CleanupMode()
	{
		ClearCompetitivePieces();
		_players.Clear();
		_currentPlayerIndex = 0;
		_phaseManager = null;
		IsActive = false;
	}

	/// <summary>
	/// Check if all pieces are stopped
	/// </summary>
	public bool AreAllPiecesStopped()
	{
		foreach (CarromPiece piece in _competitivePieces)
		{
			if (piece.Visible && !piece.IsStopped())
			{
				return false;
			}
		}
		return _striker == null || _striker.IsStopped();
	}

	/// <summary>
	/// Get all players
	/// </summary>
	public List<CarromPlayer> GetPlayers()
	{
		return new List<CarromPlayer>(_players);
	}
}