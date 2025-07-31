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
		
		GD.Print("[CarromPracticeModeManager] Initialized with board scaling");
	}
	
	/// <summary>
	/// Set the phase manager for phase-aware operations
	/// </summary>
	public void SetPhaseManager(CarromPhaseManager phaseManager)
	{
		_phaseManager = phaseManager;
		GD.Print("[CarromPracticeModeManager] Phase manager set");
	}

	/// <summary>
	/// Set the piece factory for centralized piece creation
	/// </summary>
	public void SetPieceFactory(CarromPieceFactory pieceFactory)
	{
		_pieceFactory = pieceFactory;
		GD.Print("[CarromPracticeModeManager] Piece factory set");
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
		
		// Create striker
		_striker = CreatePracticePiece(PieceType.Striker, Vector2.Zero);
		_inputController?.SetStriker(_striker);
		
		// Create single practice piece in center
		var centerPiece = CreatePracticePiece(PieceType.Black, Vector2.Zero);
		
		// Store initial position for practice piece (center of board)
		if (centerPiece != null)
		{
			_practiceInitialPositions[centerPiece] = Vector2.Zero;
		}
		
		// Position striker at baseline
		PositionStrikerAtBaseline();
		
		GD.Print("[CarromPracticeModeManager] Practice mode setup complete");
		EmitSignal(SignalName.PracticeModeSetupComplete);
	}

	/// <summary>
	/// Handle piece being pocketed in practice mode
	/// </summary>
	public void OnPiecePocketed(CarromPiece piece)
	{
		if (piece.Type != PieceType.Striker)
		{
			// Reset practice mode after a short delay
			SchedulePracticeReset();
		}
	}
	
	/// <summary>
	/// Handle pieces settled (called when all pieces have stopped)
	/// </summary>
	public void OnPiecesSettled()
	{
		GD.Print("[CarromPracticeModeManager] OnPiecesSettled() called - starting reset sequence");
		
		// In practice mode, automatically reset when pieces settle
		SchedulePracticeReset();
		
		GD.Print("[CarromPracticeModeManager] Reset scheduled, pieces being reset with frozen/deferred unfreeze");
		
		// CRITICAL: Defer the phase transition until after pieces are unfrozen
		// This prevents the race condition where phase becomes Active while pieces are still frozen
		CallDeferred(MethodName.DeferredPhaseTransition);
	}
	
	/// <summary>
	/// Deferred phase transition to ensure pieces are unfrozen before accepting input
	/// </summary>
	private void DeferredPhaseTransition()
	{
		GD.Print("[CarromPracticeModeManager] Deferred phase transition - pieces should be unfrozen now");
		_phaseManager?.TransitionTo(GamePhase.Active);
		GD.Print("[CarromPracticeModeManager] Phase transitioned to Active, ready for next strike");
	}

	/// <summary>
	/// Reset practice mode immediately
	/// </summary>
	public void SchedulePracticeReset()
	{
		ResetPracticeMode();
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
		
		// Reset all pieces to their initial positions immediately
		ResetAllPiecesToStart();
		
		EmitSignal(SignalName.PracticeResetRequested);
	}

	/// <summary>
	/// Create a practice piece using the centralized piece factory
	/// </summary>
	private CarromPiece CreatePracticePiece(PieceType type, Vector2 position)
	{
		if (_pieceFactory == null)
		{
			GD.PrintErr("[CarromPracticeModeManager] Piece factory not set - cannot create pieces");
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
	/// Position striker at baseline
	/// </summary>
	private void PositionStrikerAtBaseline()
	{
		if (_striker == null || _board == null) return;
		
		// Use board's proportional baseline positioning for player 1 (bottom baseline)
		Vector2 baselinePosition = _board.GetBaselinePosition(0); // Player 0 = bottom baseline
		_striker.Position = baselinePosition; // Use local position relative to board
		_striker.LinearVelocity = Vector2.Zero;
		_striker.AngularVelocity = 0.0f;
		
		GD.Print($"[CarromPracticeModeManager] Striker positioned at baseline: {baselinePosition}");
	}

	/// <summary>
	/// Clear all practice pieces
	/// </summary>
	private void ClearPracticePieces()
	{
		foreach (var piece in _practicePieces)
		{
			if (GodotObject.IsInstanceValid(piece))
			{
				piece.QueueFree();
			}
		}
		_practicePieces.Clear();
		
		if (_striker != null && GodotObject.IsInstanceValid(_striker))
		{
			_striker.QueueFree();
			_striker = null;
		}
	}

	/// <summary>
	/// Reset all pieces to their starting positions
	/// </summary>
	private void ResetAllPiecesToStart()
	{
		// Reset striker to baseline
		if (_striker != null && GodotObject.IsInstanceValid(_striker))
		{
			ResetPieceToStart(_striker, _board?.GetBaselinePosition(0) ?? Vector2.Zero);
		}
		
		// Reset all other pieces to their initial positions
		foreach (var kvp in _practiceInitialPositions)
		{
			var piece = kvp.Key;
			var initialPosition = kvp.Value;
			
			if (IsInstanceValid(piece))
			{
				ResetPieceToStart(piece, initialPosition);
			}
		}
	}

	/// <summary>
	/// Reset a single piece to its starting position
	/// </summary>
	private void ResetPieceToStart(CarromPiece piece, Vector2 position)
	{
		if (!IsInstanceValid(piece)) return;
		
		GD.Print($"[CarromPracticeModeManager] Resetting {piece.Type} to position {position}");
		
		// Use the optimized physics-safe reset method
		piece.ResetToPosition(position);
		
		GD.Print($"[CarromPracticeModeManager] Reset {piece.Type} using optimized method");
	}


	/// <summary>
	/// Get current striker
	/// </summary>
	public CarromPiece GetStriker()
	{
		return _striker;
	}

	/// <summary>
	/// Get all practice pieces
	/// </summary>
	public List<CarromPiece> GetPracticePieces()
	{
		return new List<CarromPiece>(_practicePieces);
	}
	
	/// <summary>
	/// Get all active pieces in this mode
	/// </summary>
	public List<CarromPiece> GetActivePieces()
	{
		var allPieces = new List<CarromPiece>(_practicePieces);
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
			if (!IsInstanceValid(piece)) continue;
			
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