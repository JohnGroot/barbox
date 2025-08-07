using Godot;
using System.Collections.Generic;

/// <summary>
/// Abstract base class for Carrom mode managers providing common functionality
/// and template method patterns for mode-specific behavior
/// </summary>
[GlobalClass]
public abstract partial class CarromModeManagerBase : Node2D, ICarromModeManager
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

	// ================================================================
	// SHARED STATE
	// ================================================================
	
	protected CarromPiece _striker;
	protected bool _isActive = false;
	
	// Settlement state (now synchronous)
	private bool _isSettling = false; // Flag to prevent concurrent settlements
	// Retry mechanisms removed - no longer needed with brute force state machine
	
	// Settlement loop detection and debugging
	private int _settlementLoopCount = 0; // Track consecutive settlement attempts
	private double _lastSettlementTime = 0.0; // Track timing between settlements
	private const int MAX_SETTLEMENT_LOOPS = 5; // Maximum consecutive settlements before intervention
	private const double MIN_SETTLEMENT_INTERVAL = 0.5; // Minimum time between settlements (500ms)
	
	// Settlement context system removed - no longer needed with brute force state machine approach
	
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
	/// Position striker at baseline with collision detection to prevent overlapping with other pieces
	/// </summary>
	protected virtual void PositionStrikerAtBaseline()
	{
		if (!IsStrikerValid()) return;
		
		// Get default baseline position (existing behavior)
		var baselinePosition = _board.ToGlobal(_board.GetBaselinePosition(0));
		float strikerRadius = _striker.PhysicsConfig?.GetRadiusForPieceType(_striker.Type) ?? 15.0f;
		
		// Check for collisions with other pieces
		if (!_board.IsPositionObstructed(baselinePosition, strikerRadius, _striker))
		{
			// Original position is clear - use it
			_striker.GlobalPosition = baselinePosition;
		}
		else
		{
			// Find alternative position on baseline to avoid collision
			var validPosition = _board.FindNearestValidPositionOnBaseline(
				baselinePosition, 0, strikerRadius, _striker);
				
			if (validPosition.HasValue)
			{
				_striker.GlobalPosition = validPosition.Value;
				GD.Print($"[{GetType().Name}] Striker repositioned to avoid collision: {validPosition.Value}");
			}
			else
			{
				// Fallback to original position with warning
				_striker.GlobalPosition = baselinePosition;
				GD.PrintErr($"[{GetType().Name}] Could not find valid striker position, using baseline center");
			}
		}
		
		// Clear movement (existing behavior)
		_striker.LinearVelocity = Vector2.Zero;
		_striker.AngularVelocity = 0.0f;
	}
	
	/// <summary>
	/// Ensure striker is restored from pocketed state with comprehensive validation and retry logic
	/// </summary>
	protected void EnsureStrikerRestored()
	{
		if (!IsStrikerValid())
		{
			return;
		}
		
		// Check if striker needs restoration
		bool needsRestoration = !_striker.Visible || _striker.Freeze;
		if (!needsRestoration)
		{
			return;
		}
		
		// Calculate target baseline position before restoration
		Vector2 targetPosition = CalculateStrikerBaselinePosition();
		
		// Attempt restoration with the target position
		_striker.Reset(targetPosition);
		
		// Validate restoration succeeded
		if (!ValidateStrikerRestoration(targetPosition))
		{
			AttemptFallbackRestoration(targetPosition);
		}
	}
	
	/// <summary>
	/// Calculate the target baseline position for striker restoration
	/// </summary>
	private Vector2 CalculateStrikerBaselinePosition()
	{
		if (_board == null)
		{
			return Vector2.Zero;
		}
		
		// Default to player 0 baseline - subclasses can override PositionStrikerAtBaseline for different behavior
		var baselinePosition = _board.GetBaselinePosition(0);
		var globalPosition = _board.ToGlobal(baselinePosition);
		return globalPosition;
	}
	
	/// <summary>
	/// Validate that striker restoration was successful
	/// </summary>
	private bool ValidateStrikerRestoration(Vector2 expectedPosition)
	{
		if (!IsStrikerValid())
		{
			return false;
		}
		
		// Check visibility was restored
		if (!_striker.Visible)
		{
			return false;
		}
		
		// Check freeze state was cleared
		if (_striker.Freeze)
		{
			return false;
		}
		
		// Check position is reasonable (within tolerance)
		float positionTolerance = 50.0f; // Allow 50 pixel tolerance
		float actualDistance = _striker.GlobalPosition.DistanceTo(expectedPosition);
		if (actualDistance > positionTolerance)
		{
			return false;
		}
		
		return true;
	}
	
	/// <summary>
	/// Attempt fallback restoration if initial restoration failed
	/// </summary>
	private void AttemptFallbackRestoration(Vector2 targetPosition)
	{
		// Attempt 1: Try basic Reset() without position
		_striker.Reset();
		
		// Attempt 2: Manually set properties
		_striker.Visible = true;
		_striker.Freeze = false;
		_striker.GlobalPosition = targetPosition;
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
			return false;
		}
		
		// Admin operations are always allowed in the new architecture
		
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
	/// Process settlement immediately and synchronously (implements ICarromModeManager)
	/// Replaces complex async settlement with simple immediate processing
	/// </summary>
	public virtual void ProcessSettlement()
	{
		try
		{
			// Execute mode-specific settlement immediately
			ExecuteModeSpecificSettlement();
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"[{GetType().Name}] ProcessSettlement failed: {ex.Message}");
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
	/// Check if all pieces have stopped moving - simplified without settlement context
	/// </summary>
	public bool AreAllPiecesStopped()
	{
		// Simplified settlement detection without complex context tracking
		
		var allPieces = GetManagedPieces();
		var movingPieces = new List<string>();
		var ignoredPieces = new List<string>();
		int totalVisible = 0;
		int totalStopped = 0;
		
		// Check managed pieces with detailed tracking
		foreach (var piece in allPieces)
		{
			if (!GodotObject.IsInstanceValid(piece)) continue;
			
			// Skip invisible pieces (pocketed)
			if (!piece.Visible) continue;
			
			totalVisible++;
			
			// Simplified: no complex context checking needed with brute force approach
			
			if (!piece.IsStopped())
			{
				var velocity = piece.LinearVelocity.Length();
				var angularVel = Mathf.Abs(piece.AngularVelocity);
				movingPieces.Add($"{piece.Type}[v:{velocity:F2},a:{angularVel:F2}]");
			}
			else
			{
				totalStopped++;
			}
		}
		
		// Check striker with context awareness
		bool strikerStopped = IsStrikerStoppedWithContext();
		if (!strikerStopped && IsStrikerValid())
		{
			var velocity = _striker.LinearVelocity.Length();
			var angularVel = Mathf.Abs(_striker.AngularVelocity);
			movingPieces.Add($"Striker[v:{velocity:F2},a:{angularVel:F2}]");
		}
		
		bool allStopped = movingPieces.Count == 0 && strikerStopped;
		
		return allStopped;
	}
	
	/// <summary>
	/// Check if striker has stopped with settlement context awareness
	/// </summary>
	private bool IsStrikerStoppedWithContext()
	{
		if (!IsStrikerValid()) 
			return true;
		
		// Pocketed strikers (invisible) are considered stopped
		if (!_striker.Visible)
			return true;
		
		// Simplified: no complex context checking needed
			
		return _striker.IsStopped();
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
		_isActive = false;
	}
	
	/// <summary>
	/// Check if the mode is currently active/initialized
	/// </summary>
	public bool IsActive => _isActive;
	
	/// <summary>
	/// Mark a piece as recently restored - no longer needed with brute force settlement
	/// </summary>
	public void MarkRecentRestoration(CarromPiece piece)
	{
		// No longer needed with simplified settlement detection
	}

	// ================================================================
	// TEMPLATE METHOD PATTERN - SETTLEMENT PROCESSING
	// ================================================================
	
	public override void _Process(double delta)
	{
		// No longer need timer-based settlement processing
		// Settlement is now immediate and synchronous
	}
	
	/// <summary>
	/// Execute settlement operations with proper timing - striker restoration BEFORE phase transition
	/// </summary>
	private async void ExecuteSettlement()
	{
		// Settlement loop detection and prevention
		double currentTime = Time.GetUnixTimeFromSystem();
		double timeSinceLastSettlement = currentTime - _lastSettlementTime;
		
		if (timeSinceLastSettlement < MIN_SETTLEMENT_INTERVAL)
		{
			_settlementLoopCount++;
			
			if (_settlementLoopCount >= MAX_SETTLEMENT_LOOPS)
			{
				ForcePhaseTransitionToBreakLoop();
				return;
			}
		}
		else
		{
			// Reset loop counter if enough time has passed
			_settlementLoopCount = 0;
		}
		
		_lastSettlementTime = currentTime;
		
		// Settlement context no longer needed
		
		// Disable input during settlement to prevent position overrides
		DisableInputDuringSettlement();
		
		try
		{
			// Execute mode-specific settlement (including striker restoration) FIRST
			ExecuteModeSpecificSettlement();
			
			// Wait for physics sync before validation
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
			
			// Validate settlement succeeded before transitioning phase
			bool settlementValid = ValidateSettlementSuccess();
			
			if (settlementValid)
			{
				// Post-restoration validation: Check if restoration caused pieces to move again
				bool needsAdditionalSettlement = CheckPostRestorationSettlement();
				
				if (needsAdditionalSettlement)
				{
					SchedulePostRestorationSettlement();
				}
				else
				{
					// Settlement completed successfully - state machine handles transition
				}
			}
			else
			{
				// Consider retry mechanism here
				AttemptSettlementRetry();
			}
		}
		finally
		{
			// Re-enable input after settlement
			EnableInputAfterSettlement();
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
		return _board != null;
	}
	
	private void CreateStriker()
	{
		if (_pieceFactory == null)
		{
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
	/// Input state is now automatically managed by CarromGameStateMachine
	/// No manual input control needed - state machine handles input availability
	/// </summary>
	private void DisableInputDuringSettlement()
	{
		// Input is automatically disabled during settlement by state machine
	}
	
	/// <summary>
	/// Input state is now automatically managed by CarromGameStateMachine
	/// No manual input control needed - state machine handles input availability
	/// </summary>
	private void EnableInputAfterSettlement()
	{
		// Input is automatically enabled when ready by state machine
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
	
	// ================================================================
	// SETTLEMENT VALIDATION AND DEBUGGING
	// ================================================================
	
	
	
	/// <summary>
	/// Validate that settlement operations completed successfully - with settlement context for all pieces
	/// </summary>
	private bool ValidateSettlementSuccess()
	{
		// Check 1: Striker should be valid and visible after restoration
		if (!IsStrikerValid())
		{
			return false;
		}
		
		if (!_striker.Visible)
		{
			return false;
		}
		
		if (_striker.Freeze)
		{
			return false;
		}
		
		// Check 2: Validate that game pieces are stopped WITH settlement context awareness
		// CRITICAL FIX: Apply settlement context to ALL pieces, not just striker
		var gamePieces = GetManagedPieces();
		foreach (var piece in gamePieces)
		{
			if (piece != null && GodotObject.IsInstanceValid(piece) && piece.Visible)
			{
				// Simplified validation without context checking
				
				if (!piece.IsStopped())
				{
					return false;
				}
			}
		}
		
		
		return true;
	}
	
	/// <summary>
	/// Settlement retry no longer needed with brute force state machine approach
	/// </summary>
	private void AttemptSettlementRetry()
	{
		// No longer needed - state machine handles settlement automatically
	}
	
	
	
	/// <summary>
	/// Check if striker restoration caused pieces to start moving again
	/// CRITICAL FIX: Don't check settlement context pieces - only check for actual movement
	/// </summary>
	private bool CheckPostRestorationSettlement()
	{
		// Get pieces that are actually moving (not in settlement context)
		var allPieces = GetManagedPieces();
		var actuallyMovingPieces = new List<CarromPiece>();
		
		// Check each piece for actual movement (ignore settlement context)
		foreach (var piece in allPieces)
		{
			if (!GodotObject.IsInstanceValid(piece) || !piece.Visible) continue;
			
			// Check raw physics movement, not IsStopped() which considers settlement context
			float velocity = piece.LinearVelocity.Length();
			float angularVel = Mathf.Abs(piece.AngularVelocity);
			
			if (velocity > 1.0f || angularVel > 0.1f) // Raw movement thresholds
			{
				actuallyMovingPieces.Add(piece);
			}
		}
		
		// Check striker for actual movement (ignore settlement context)
		bool strikerActuallyMoving = false;
		if (IsStrikerValid() && _striker.Visible)
		{
			float strikerVel = _striker.LinearVelocity.Length();
			float strikerAngVel = Mathf.Abs(_striker.AngularVelocity);
			strikerActuallyMoving = strikerVel > 1.0f || strikerAngVel > 0.1f;
		}
		
		bool needsAdditionalSettlement = actuallyMovingPieces.Count > 0 || strikerActuallyMoving;
		
		
		return needsAdditionalSettlement;
	}
	
	/// <summary>
	/// Schedule additional settlement after restoration caused movement
	/// </summary>
	private void SchedulePostRestorationSettlement()
	{
		// Brief delay to allow pieces to settle after restoration
		CreateAutoCleanupTimer(0.2f, () => {
			// Settlement completed - state machine handles the transition automatically
		});
	}
	
	/// <summary>
	/// Force phase transition to break out of settlement loops
	/// </summary>
	private void ForcePhaseTransitionToBreakLoop()
	{
		// Clear all settlement state
		_settlementLoopCount = 0;
		
		// Force striker restoration if needed
		if (IsStrikerValid() && (!_striker.Visible || _striker.Freeze))
		{
			EnsureStrikerRestored();
		}
		
		// State machine will handle transition automatically when pieces settle
	}
	
	
	/// <summary>
	/// Create a timer that automatically cleans itself up after execution
	/// </summary>
	private Timer CreateAutoCleanupTimer(float waitTime, System.Action onTimeout)
	{
		var timer = new Timer();
		timer.WaitTime = waitTime;
		timer.OneShot = true;
		timer.Timeout += () => {
			onTimeout?.Invoke();
			timer.QueueFree(); // Ensure timer is cleaned up
		};
		AddChild(timer);
		timer.Start();
		return timer;
	}
	
	/// <summary>
	/// Create a piece using the centralized piece factory
	/// </summary>
	protected CarromPiece CreatePiece(PieceType type, Vector2 position)
	{
		if (_pieceFactory == null)
		{
			return null;
		}
		
		// Use centralized piece factory which applies physics parameters
		return _pieceFactory.CreatePiece(type, position);
	}
}