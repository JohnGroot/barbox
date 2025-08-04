using Godot;
using System.Collections.Generic;

/// <summary>
/// Abstract base class for Carrom mode managers providing common functionality
/// and template method patterns for mode-specific behavior
/// </summary>
[GlobalClass]
public abstract partial class CarromModeManagerBase : Node2D
{
	// ================================================================
	// SHARED SIGNALS (mode managers can add their own)
	// ================================================================
	
	[Signal] public delegate void ModeSetupCompleteEventHandler();
	[Signal] public delegate void ModeResetRequestedEventHandler();

	// ================================================================
	// SHARED DEPENDENCIES
	// ================================================================
	
	protected CarromBoard _board;
	protected CarromInputController _inputController;
	protected CarromPhysicsConfig _physicsConfig;
	protected CarromPieceFactory _pieceFactory;
	protected CarromPhaseManager _phaseManager;

	// ================================================================
	// SHARED STATE
	// ================================================================
	
	protected CarromPiece _striker;
	protected bool _isActive = false;
	
	// Settlement state (now synchronous)
	private bool _isSettling = false; // Flag to prevent concurrent settlements
	
	// ================================================================
	// ABSTRACT METHODS (must be implemented by subclasses)
	// ================================================================
	
	/// <summary>
	/// Create and position pieces specific to this mode
	/// </summary>
	protected abstract void CreateModeSpecificPieces();
	
	/// <summary>
	/// Get all pieces managed by this mode (excluding striker)
	/// </summary>
	protected abstract List<CarromPiece> GetManagedPieces();
	
	/// <summary>
	/// Clear all mode-specific pieces
	/// </summary>
	protected abstract void ClearModeSpecificPieces();
	
	/// <summary>
	/// Handle mode-specific settlement behavior
	/// </summary>
	protected abstract void ExecuteModeSpecificSettlement();
	
	/// <summary>
	/// Check if win condition is met for the given player
	/// </summary>
	public abstract bool CheckWinCondition(string playerId);
	
	/// <summary>
	/// Determine if current turn should continue
	/// </summary>
	public abstract bool ShouldContinueTurn(string playerId);
	
	// ================================================================
	// VIRTUAL METHODS (default implementations that can be overridden)
	// ================================================================
	
	/// <summary>
	/// Handle piece being pocketed (default: trigger settlement check)
	/// </summary>
	public virtual void OnPiecePocketed(CarromPiece piece)
	{
		if (!ValidatePieceAndPhase(piece, "Piece pocketed")) return;
		
		// Subclasses can override for mode-specific pocketing behavior
		HandlePiecePocketed(piece);
	}
	
	/// <summary>
	/// Position striker at baseline (default: use board baseline for player 0)
	/// </summary>
	protected virtual void PositionStrikerAtBaseline()
	{
		if (!IsStrikerValid()) return;
		
		var baselinePosition = _board.ToGlobal(_board.GetBaselinePosition(0));
		_striker.GlobalPosition = baselinePosition;
		_striker.LinearVelocity = Vector2.Zero;
		_striker.AngularVelocity = 0.0f;
	}
	
	/// <summary>
	/// Validate piece operations (can be overridden for mode-specific validation)
	/// </summary>
	protected virtual bool ValidatePieceAndPhase(CarromPiece piece, string operation)
	{
		if (piece == null || !GodotObject.IsInstanceValid(piece))
		{
			GD.PrintErr($"[{GetType().Name}] ERROR: {operation} called with invalid piece");
			return false;
		}
		
		if (_phaseManager != null && !_phaseManager.CanExecuteAdminOperation(operation))
		{
			return false;
		}
		
		return true;
	}

	/// <summary>
	/// Handle mode-specific piece pocketing logic (can be overridden)
	/// </summary>
	protected virtual void HandlePiecePocketed(CarromPiece piece)
	{
		// Default: no special handling, subclasses implement as needed
	}

	// ================================================================
	// CONCRETE METHODS (shared implementations - ICarromModeManager)
	// ================================================================
	
	/// <summary>
	/// Initialize the mode manager with required dependencies
	/// </summary>
	public void Initialize(CarromBoard board, CarromInputController inputController, CarromPhysicsConfig physicsConfig)
	{
		_board = board;
		_inputController = inputController;
		_physicsConfig = physicsConfig;
		_isActive = true;
		
		ConfigurePhysicsScaling();
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
	/// Template method for mode setup
	/// </summary>
	public void SetupMode()
	{
		if (!ValidateSetupPreconditions()) return;
		
		ClearAllPieces();
		CreateStriker();
		CreateModeSpecificPieces();
		PositionStrikerAtBaseline();
		FinalizeSetup();
		
		EmitSignal(SignalName.ModeSetupComplete);
	}
	
	/// <summary>
	/// Handle pieces settled with immediate synchronous settlement
	/// </summary>
	public void OnPiecesSettled()
	{
		// Prevent concurrent settlements
		if (_isSettling)
		{
			GD.Print($"[{GetType().Name}] Settlement already in progress, ignoring duplicate call");
			return;
		}
		
		GD.Print($"[{GetType().Name}] OnPiecesSettled - executing immediate settlement");
		
		// Validate phase manager
		if (_phaseManager == null)
		{
			GD.PrintErr($"[{GetType().Name}] Phase manager is null - cannot execute settlement");
			return;
		}
		
		// Execute settlement immediately - no timer delays
		_isSettling = true;
		try
		{
			ExecuteSettlement();
		}
		finally
		{
			_isSettling = false;
		}
	}
	
	/// <summary>
	/// Get the current striker piece
	/// </summary>
	public CarromPiece GetStriker()
	{
		return _striker;
	}
	
	/// <summary>
	/// Check if all pieces have stopped moving
	/// </summary>
	public bool AreAllPiecesStopped()
	{
		// Check managed pieces
		foreach (var piece in GetManagedPieces())
		{
			if (!GodotObject.IsInstanceValid(piece)) continue;
			
			if (piece.Visible && !piece.IsStopped())
			{
				return false;
			}
		}
		
		// Check striker
		return IsStrikerStopped();
	}
	
	/// <summary>
	/// Get all active pieces in this mode
	/// </summary>
	public List<CarromPiece> GetActivePieces()
	{
		var allPieces = new List<CarromPiece>(GetManagedPieces());
		if (IsStrikerValid())
		{
			allPieces.Add(_striker);
		}
		return allPieces;
	}
	
	/// <summary>
	/// Clean up mode resources
	/// </summary>
	public void CleanupMode()
	{
		CancelPendingOperations();
		ClearAllPieces();
		ClearModeSpecificState();
		_phaseManager = null;
		_isActive = false;
	}
	
	/// <summary>
	/// Check if the mode is currently active/initialized
	/// </summary>
	public bool IsActive => _isActive;

	// ================================================================
	// TEMPLATE METHOD PATTERN - SETTLEMENT PROCESSING
	// ================================================================
	
	public override void _Process(double delta)
	{
		// No longer need timer-based settlement processing
		// Settlement is now immediate and synchronous
	}
	
	/// <summary>
	/// Execute settlement operations with immediate synchronous phase management
	/// </summary>
	private void ExecuteSettlement()
	{
		GD.Print($"[{GetType().Name}] ExecuteSettlement starting - current phase: {_phaseManager.CurrentPhase}");
		
		// Disable input during settlement to prevent position overrides
		DisableInputDuringSettlement();
		
		try
		{
			// Transition back to Active phase first for proper operation context
			bool transitionResult = _phaseManager.TransitionTo(GamePhase.Active);
			
			GD.Print($"[{GetType().Name}] Phase transition to Active result: {transitionResult}, new phase: {_phaseManager.CurrentPhase}");
			
			if (transitionResult)
			{
				// Execute mode-specific settlement behavior immediately
				GD.Print($"[{GetType().Name}] Executing mode-specific settlement");
				ExecuteModeSpecificSettlement();
				GD.Print($"[{GetType().Name}] Mode-specific settlement completed");
			}
			else
			{
				GD.PrintErr($"[{GetType().Name}] Failed to transition to Active phase - settlement aborted");
			}
		}
		finally
		{
			// Re-enable input after settlement
			EnableInputAfterSettlement();
			GD.Print($"[{GetType().Name}] ExecuteSettlement completed");
		}
	}

	// ================================================================
	// PRIVATE HELPER METHODS
	// ================================================================
	
	private void ConfigurePhysicsScaling()
	{
		if (_board != null && _physicsConfig != null)
		{
			_physicsConfig.SetBoardScaling(
				_board.ScaleFactor,
				_board.PieceRadius,
				_board.OfficialStrikerRadius
			);
		}
	}
	
	private bool ValidateSetupPreconditions()
	{
		if (_board == null) return false;
		
		if (_phaseManager != null && !_phaseManager.CanExecuteAdminOperation("Mode setup"))
		{
			return false;
		}
		
		return true;
	}
	
	private void CreateStriker()
	{
		if (_pieceFactory == null)
		{
			GD.PrintErr($"[{GetType().Name}] Piece factory not set - cannot create striker");
			return;
		}
		
		_striker = _pieceFactory.CreatePiece(PieceType.Striker, Vector2.Zero);
		_inputController?.SetStriker(_striker);
	}
	
	private void ClearAllPieces()
	{
		ClearModeSpecificPieces();
		ClearStriker();
	}
	
	private void ClearStriker()
	{
		if (_striker != null && GodotObject.IsInstanceValid(_striker))
		{
			_striker.QueueFree();
			_striker = null;
		}
	}
	
	private bool IsStrikerValid()
	{
		return _striker != null && GodotObject.IsInstanceValid(_striker);
	}
	
	private bool IsStrikerStopped()
	{
		if (!IsStrikerValid()) 
			return true;
		
		// Pocketed strikers (invisible) are considered stopped
		if (!_striker.Visible)
			return true;
			
		return _striker.IsStopped();
	}
	
	/// <summary>
	/// Disable input controller during settlement to prevent position overrides
	/// </summary>
	private void DisableInputDuringSettlement()
	{
		if (_inputController != null && GodotObject.IsInstanceValid(_inputController))
		{
			GD.Print($"[{GetType().Name}] Disabling input during settlement");
			_inputController.SetPhaseManager(null); // Temporarily disconnect phase manager to disable input
		}
	}
	
	/// <summary>
	/// Re-enable input controller after settlement
	/// </summary>
	private void EnableInputAfterSettlement()
	{
		if (_inputController != null && GodotObject.IsInstanceValid(_inputController))
		{
			GD.Print($"[{GetType().Name}] Re-enabling input after settlement");
			_inputController.SetPhaseManager(_phaseManager); // Restore phase manager to enable input
		}
	}
	
	private void CancelPendingOperations()
	{
		// No longer need timer-based operations
		CancelModeSpecificOperations();
	}
	
	/// <summary>
	/// Finalize setup after piece creation (can be overridden)
	/// </summary>
	protected virtual void FinalizeSetup() { }
	
	/// <summary>
	/// Clear mode-specific state during cleanup (can be overridden)
	/// </summary>
	protected virtual void ClearModeSpecificState() { }
	
	/// <summary>
	/// Cancel mode-specific pending operations (can be overridden)
	/// </summary>
	protected virtual void CancelModeSpecificOperations() { }
	
	/// <summary>
	/// Create a piece using the centralized piece factory
	/// </summary>
	protected CarromPiece CreatePiece(PieceType type, Vector2 position)
	{
		if (_pieceFactory == null)
		{
			GD.PrintErr($"[{GetType().Name}] Piece factory not set - cannot create pieces");
			return null;
		}
		
		// Use centralized piece factory which applies physics parameters
		return _pieceFactory.CreatePiece(type, position);
	}
}