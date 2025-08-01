using Godot;
using System.Collections.Generic;

/// <summary>
/// Manages practice mode functionality for Carrom game
/// </summary>
[GlobalClass]
public partial class CarromPracticeModeManager : Node2D, ICarromModeManager
{
	[Signal] public delegate void PracticeResetRequestedEventHandler();
	[Signal] public delegate void PracticeModeSetupCompleteEventHandler();

	// Dependencies
	private CarromBoard _board;
	private CarromInputController _inputController;
	private CarromPhysicsConfig _physicsConfig;
	private CarromPieceFactory _pieceFactory;

	// Practice mode state
	private Dictionary<CarromPiece, Vector2> _practiceInitialPositions = new Dictionary<CarromPiece, Vector2>();
	private List<CarromPiece> _practicePieces = new List<CarromPiece>();
	private CarromPiece _striker;
	
	// Settlement confirmation timer
	private float _settlementConfirmationTimer = 0.0f;
	private const float SETTLEMENT_CONFIRMATION_TIME = 0.1f; // Wait 0.1 seconds to confirm settlement

	// Piece templates
	private PackedScene _blackPieceTemplate;
	private PackedScene _strikerTemplate;
	private CarromPhaseManager _phaseManager;
	
	/// <summary>
	/// Check if the mode is currently active/initialized
	/// </summary>
	public bool IsActive { get; private set; } = false;

	/// <summary>
	/// Initialize the practice mode manager
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
		
	}
	
	/// <summary>
	/// Set the phase manager for phase-aware operations
	/// </summary>
	public void SetPhaseManager(CarromPhaseManager phaseManager)
	{
		_phaseManager = phaseManager;
	}

	/// <summary>
	/// Set the piece factory for centralized piece creation
	/// </summary>
	public void SetPieceFactory(CarromPieceFactory pieceFactory)
	{
		_pieceFactory = pieceFactory;
	}
	
	/// <summary>
	/// Initialize with piece templates (practice-specific version)
	/// </summary>
	public void Initialize(CarromBoard board, CarromInputController inputController, 
						   CarromPhysicsConfig physicsConfig, PackedScene blackTemplate, 
						   PackedScene strikerTemplate)
	{
		Initialize(board, inputController, physicsConfig);
		_blackPieceTemplate = blackTemplate;
		_strikerTemplate = strikerTemplate;
	}

	/// <summary>
	/// Setup practice mode pieces (striker + single piece)
	/// </summary>
	public void SetupMode()
	{
		SetupPracticeMode();
	}
	
	/// <summary>
	/// Setup practice mode pieces (striker + single piece) - internal implementation
	/// </summary>
	public void SetupPracticeMode()
	{
		if (_board == null) return;
		
		// Validate phase state before setup
		if (_phaseManager != null && !_phaseManager.CanExecuteAdminOperation("Practice mode setup"))
		{
			return;
		}
		
		// Clear existing pieces and position tracking
		ClearPracticePieces();
		_practiceInitialPositions.Clear();
		
		// Get initial positions using global coordinates for consistency
		var strikerInitialPosition = _board.ToGlobal(_board.GetBaselinePosition(0));
		var centerPieceInitialPosition = _board.ToGlobal(Vector2.Zero); // Center of board in global coords
		
		// Create striker at baseline position
		_striker = CreatePracticePiece(PieceType.Striker, Vector2.Zero);
		_inputController?.SetStriker(_striker);
		
		// Create single practice piece in center
		var centerPiece = CreatePracticePiece(PieceType.Black, Vector2.Zero);
		
		// Store initial positions for both pieces using global coordinates
		if (_striker != null)
		{
			_practiceInitialPositions[_striker] = strikerInitialPosition;
		}
		
		if (centerPiece != null)
		{
			_practiceInitialPositions[centerPiece] = centerPieceInitialPosition;
		}
		
		// Position pieces at their initial positions
		PositionPiecesAtInitialPositions();
		
		EmitSignal(SignalName.PracticeModeSetupComplete);
	}

	/// <summary>
	/// Handle piece being pocketed in practice mode
	/// </summary>
	public void OnPiecePocketed(CarromPiece piece)
	{
		if (piece == null || !GodotObject.IsInstanceValid(piece))
		{
			GD.PrintErr("[CarromPracticeModeManager] ERROR: OnPiecePocketed called with invalid piece");
			return;
		}
		
		// Check if phase manager allows reset operations
		if (_phaseManager != null && !_phaseManager.CanExecuteAdminOperation("Practice mode reset after pocketing"))
		{
			return;
		}
		
		// Reset practice mode after piece is pocketed
		ResetPracticeMode();
	}
	
	/// <summary>
	/// Handle pieces settled (called when all pieces have stopped)
	/// </summary>
	public void OnPiecesSettled()
	{
		// Start settlement confirmation timer to ensure pieces are truly settled
		_settlementConfirmationTimer = SETTLEMENT_CONFIRMATION_TIME;
	}
	/// <summary>
	/// Reset practice mode to initial state
	/// </summary>
	public void ResetPracticeMode()
	{
		// Validate phase state before reset
		if (_phaseManager != null && !_phaseManager.CanExecuteAdminOperation("Practice mode reset"))
		{
			return;
		}

		// Log current piece states for debugging
		foreach (var kvp in _practiceInitialPositions)
		{
			var piece = kvp.Key;
			if (IsInstanceValid(piece))
			{
				var currentPos = piece.GlobalPosition;
				var velocity = piece.LinearVelocity;
				var isStopped = piece.IsStopped();
			}
		}

		// Reset all pieces to their stored initial positions using immediate method
		foreach (var kvp in _practiceInitialPositions)
		{
			var piece = kvp.Key;
			var initialGlobalPosition = kvp.Value;
			
			if (IsInstanceValid(piece))
			{
				ResetPieceToStartImmediate(piece, initialGlobalPosition);
			}
		}

		EmitSignal(SignalName.PracticeResetRequested);

		// Immediate phase transition after reset
		_phaseManager?.TransitionTo(GamePhase.Active);
	}

	/// <summary>
	/// Create a practice piece using the centralized piece factory
	/// </summary>
	private CarromPiece CreatePracticePiece(PieceType type, Vector2 position)
	{
		if (_pieceFactory == null)
		{
			GD.PrintErr("[CarromPracticeModeManager] ERROR: Piece factory not set - cannot create pieces");
			return null;
		}
		
		// Use centralized piece factory which applies physics parameters
		var piece = _pieceFactory.CreatePiece(type, position);
		
		// Track practice pieces (factory handles board addition and physics setup)
		if (piece != null && type != PieceType.Striker)
		{
			_practicePieces.Add(piece);
		}
		
		return piece;
	}

	/// <summary>
	/// Position all pieces at their initial positions using global coordinates
	/// </summary>
	private void PositionPiecesAtInitialPositions()
	{
		foreach (var kvp in _practiceInitialPositions)
		{
			var piece = kvp.Key;
			var globalPosition = kvp.Value;

			if (IsInstanceValid(piece))
			{
				piece.GlobalPosition = globalPosition;
				piece.LinearVelocity = Vector2.Zero;
				piece.AngularVelocity = 0.0f;
			}
		}
	}
	
	/// <summary>
	/// Position striker at baseline (legacy method - now uses stored positions)
	/// </summary>
	private void PositionStrikerAtBaseline()
	{
		if (_striker == null || _board == null)
			return;

		// Use stored initial position instead of recalculating
		if (_practiceInitialPositions.TryGetValue(_striker, out var storedPosition))
		{
			_striker.GlobalPosition = storedPosition;
		}
		else
		{
			// Fallback: calculate baseline position
			Vector2 baselinePosition = _board.GetBaselinePosition(0);
			_striker.Position = baselinePosition;
		}

		_striker.LinearVelocity = Vector2.Zero;
		_striker.AngularVelocity = 0.0f;
	}

	/// <summary>
	/// Clear all practice pieces
	/// </summary>
	private void ClearPracticePieces()
	{
		foreach (var piece in _practicePieces)
		{
			if (IsInstanceValid(piece))
			{
				piece.QueueFree();
			}
		}
		_practicePieces.Clear();
		
		if (_striker != null && IsInstanceValid(_striker))
		{
			_striker.QueueFree();
			_striker = null;
		}
	}

	/// <summary>
	/// Reset a single piece to its starting position using immediate synchronous method
	/// </summary>
	private void ResetPieceToStartImmediate(CarromPiece piece, Vector2 globalPosition)
	{
		if (!IsInstanceValid(piece)) 
			return;

		// First restore from pocketed state if the piece was pocketed
		if (!piece.Visible) // Check if piece was pocketed (invisible)
		{
			piece.Reset();
		}

		// Then use the immediate synchronous reset method for positioning
		piece.Reset(globalPosition);
	}

	/// <summary>
	/// Get current striker
	/// </summary>
	public CarromPiece GetStriker()
	{
		return _striker;
	}

	/// <summary>
	/// Process settlement confirmation timer
	/// </summary>
	public override void _Process(double delta)
	{
		// Handle settlement confirmation timer
		if (_settlementConfirmationTimer > 0)
		{
			float deltaF = (float)delta;
			_settlementConfirmationTimer -= deltaF;
			
			// Check if timer expired and pieces are still settled
			if (_settlementConfirmationTimer <= 0)
			{
				if (AreAllPiecesStopped())
				{
					ResetPracticeMode();
				}
				else
				{
				}
				_settlementConfirmationTimer = 0.0f;
			}
		}
	}

	/// <summary>
	/// Cancel pending reset operations (called when new strike is initiated)
	/// </summary>
	public void CancelPendingReset()
	{
		if (_settlementConfirmationTimer > 0)
		{
			_settlementConfirmationTimer = 0.0f;
		}
	}
	
	/// <summary>
	/// Get all active pieces in this mode
	/// </summary>
	public List<CarromPiece> GetActivePieces()
	{
		var allPieces = new List<CarromPiece>(_practicePieces);
		if (_striker != null && IsInstanceValid(_striker))
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
		ClearPracticePieces();
		_practiceInitialPositions.Clear();
		_phaseManager = null;
		IsActive = false;
	}

	/// <summary>
	/// Check if all pieces are stopped
	/// </summary>
	public bool AreAllPiecesStopped()
	{
		// Check all practice pieces
		foreach (CarromPiece piece in _practicePieces)
		{
			if (!IsInstanceValid(piece)) 
				continue;
			
			if (piece.Visible && !piece.IsStopped())
			{
				return false;
			}
		}
		
		// Check striker
		if (_striker != null && IsInstanceValid(_striker))
		{
			return _striker.IsStopped();
		}
		
		return true;
	}
}