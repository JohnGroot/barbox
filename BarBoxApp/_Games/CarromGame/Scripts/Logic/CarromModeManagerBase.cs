using System.Collections.Generic;
using System.Linq;
using Godot;

namespace BarBox.Games.Carrom;

/// <summary>
/// Abstract base class for Carrom mode managers providing common functionality
/// and template method patterns for mode-specific behavior
/// </summary>
[GlobalClass]
public abstract partial class CarromModeManagerBase : Node2D, ICarromModeManager
{
	[Signal]
	public delegate void ModeSetupCompleteEventHandler();

	[Signal]
	public delegate void ModeResetRequestedEventHandler();

	protected CarromBoard _board;
	protected CarromInputController _inputController;
	protected CarromPhysicsConfig _physicsConfig;
	protected CarromPieceFactory _pieceFactory;

	protected CarromPiece _striker;
	protected bool _isActive = false;

	// Memory management tracking
	private List<Timer> _activeTimers = [];
	private bool _isDisposed = false;

	protected abstract void CreateModeSpecificPieces();

	/// <summary>
	/// Get all pieces managed by this mode (excluding striker)
	/// </summary>
	protected abstract List<CarromPiece> GetManagedPieces();

	protected abstract void ClearModeSpecificPieces();

	protected abstract void ExecuteModeSpecificSettlement();

	public abstract bool CheckWinCondition(string playerId);

	public abstract bool ShouldContinueTurn(string playerId);

	/// <summary>
	/// Handle piece being pocketed (default: trigger settlement check)
	/// </summary>
	public virtual void OnPiecePocketed(CarromPiece piece)
	{
		if (!ValidatePieceAndPhase(piece, "Piece pocketed"))
		{
			return;
		}

		// Subclasses can override for mode-specific pocketing behavior
		HandlePiecePocketed(piece);
	}

	/// <summary>
	/// Position striker at baseline with collision detection to prevent overlapping with other pieces
	/// </summary>
	protected virtual void PositionStrikerAtBaseline()
	{
		if (!IsStrikerValid())
		{
			return;
		}

		Vector2 validPosition = FindValidStrikerBaselinePosition();
		_striker.GlobalPosition = validPosition;

		_striker.LinearVelocity = Vector2.Zero;
		_striker.AngularVelocity = 0.0f;
	}

	/// <summary>
	/// Shared logic used by both positioning and restoration operations.
	/// </summary>
	private Vector2 FindValidStrikerBaselinePosition()
	{
		if (_board == null)
		{
			return Vector2.Zero;
		}

		var baselinePosition = _board.ToGlobal(_board.GetBaselinePosition(0));
		float strikerRadius = _striker.PhysicsConfig?.GetRadiusForPieceType(_striker.Type) ?? 15.0f;

		if (!_board.IsPositionObstructed(baselinePosition, strikerRadius, _striker))
		{
			return baselinePosition;
		}

		var validPosition = _board.FindNearestValidPositionOnBaseline(
			baselinePosition, 0, strikerRadius, _striker);

		if (validPosition.HasValue)
		{
			GD.Print($"[{GetType().Name}] Striker positioned at alternative location to avoid collision");
			return validPosition.Value;
		}

		GD.PrintErr($"[{GetType().Name}] Could not find valid striker position, using baseline center");
		return baselinePosition;
	}

	/// <summary>
	/// Ensure striker is restored from pocketed state with comprehensive validation and retry logic.
	/// </summary>
	protected void EnsureStrikerRestored()
	{
		if (!IsStrikerValid())
		{
			return;
		}

		bool needsRestoration = !_striker.Visible || _striker.Freeze;
		if (!needsRestoration)
		{
			return;
		}

		Vector2 targetPosition = CalculateValidStrikerBaselinePosition();

		_striker.Reset(targetPosition);

		if (!ValidateStrikerRestoration(targetPosition))
		{
			AttemptFallbackRestoration(targetPosition);
		}
	}

	private Vector2 CalculateValidStrikerBaselinePosition()
	{
		return FindValidStrikerBaselinePosition();
	}

	private bool ValidateStrikerRestoration(Vector2 expectedPosition)
	{
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

		float positionTolerance = 50.0f;
		float actualDistance = _striker.GlobalPosition.DistanceTo(expectedPosition);
		if (actualDistance > positionTolerance)
		{
			return false;
		}

		return true;
	}

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

	protected virtual bool ValidatePieceAndPhase(CarromPiece piece, string operation)
	{
		if (piece == null || !GodotObject.IsInstanceValid(piece))
		{
			return false;
		}

		return true;
	}

	protected virtual void HandlePiecePocketed(CarromPiece piece)
	{
	}

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
		if (!ValidateSetupPreconditions())
		{
			return;
		}

		ClearAllPieces();
		CreateStriker();
		CreateModeSpecificPieces();
		PositionStrikerAtBaseline();
		FinalizeSetup();

		EmitSignal(SignalName.ModeSetupComplete);
	}

	/// <summary>
	/// Process settlement immediately and synchronously (implements ICarromModeManager)
	/// Pure data processing - no state control, just execute mode-specific logic
	/// </summary>
	public virtual void ProcessSettlement()
	{
		ExecuteModeSpecificSettlement();
	}

	public CarromPiece GetStriker()
	{
		return _striker;
	}

	public bool AreAllPiecesStopped()
	{
		var allPieces = GetManagedPieces();
		var movingPieces = new List<string>();
		var ignoredPieces = new List<string>();
		int totalVisible = 0;
		int totalStopped = 0;

		foreach (var piece in allPieces)
		{
			if (!GodotObject.IsInstanceValid(piece))
			{
				continue;
			}

			// Skip invisible pieces (pocketed)
			if (!piece.Visible)
			{
				continue;
			}

			totalVisible++;

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

	private bool IsStrikerStoppedWithContext()
	{
		if (!IsStrikerValid())
		{
			return true;
		}

		// Pocketed strikers (invisible) are considered stopped
		if (!_striker.Visible)
		{
			return true;
		}

		return _striker.IsStopped();
	}

	public List<CarromPiece> GetActivePieces()
	{
		var allPieces = new List<CarromPiece>(GetManagedPieces());
		if (IsStrikerValid())
		{
			allPieces.Add(_striker);
		}

		return allPieces;
	}

	public void CleanupMode()
	{
		CancelPendingOperations();
		ClearAllPieces();
		ClearModeSpecificState();
		_isActive = false;
	}

	public bool IsActive => _isActive;

	public void MarkRecentRestoration(CarromPiece piece)
	{
	}

	private void ConfigurePhysicsScaling()
	{
		if (_board != null && _physicsConfig != null)
		{
			_physicsConfig.SetBoardScaling(
				_board.ScaleFactor,
				_board.PieceRadius,
				_board.OfficialStrikerRadius);
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
		{
			return true;
		}

		// Pocketed strikers (invisible) are considered stopped
		if (!_striker.Visible)
		{
			return true;
		}

		return _striker.IsStopped();
	}

	/// <summary>
	/// Input availability is managed by GameStateManager; no manual control needed here.
	/// </summary>
	private void DisableInputDuringSettlement()
	{
	}

	/// <summary>
	/// Input availability is managed by GameStateManager; no manual control needed here.
	/// </summary>
	private void EnableInputAfterSettlement()
	{
	}

	private void CancelPendingOperations()
	{
		CancelModeSpecificOperations();
	}

	/// <summary>
	/// Finalize setup after piece creation.
	/// </summary>
	protected virtual void FinalizeSetup()
	{
	}

	protected virtual void ClearModeSpecificState()
	{
	}

	protected virtual void CancelModeSpecificOperations()
	{
	}

	/// <summary>
	/// Deliberately minimal: deeper settlement validation here raced with the
	/// state machine's own checks and could lock the game.
	/// </summary>
	private bool ValidateSettlementSuccess()
	{
		if (!IsStrikerValid())
		{
			return false;
		}

		if (!_striker.Visible)
		{
			return false;
		}

		return true;
	}

	/// <summary>
	/// No-op: settlement retry is handled by the state machine. Retrying here
	/// as well raced with it and caused game locks — don't reintroduce.
	/// </summary>
	private void AttemptSettlementRetry()
	{
	}

	/// <summary>
	/// Always false: re-detecting piece movement after restoration raced with
	/// the state machine and added settlement delays — don't reintroduce.
	/// </summary>
	private bool CheckPostRestorationSettlement()
	{
		return false;
	}

	/// <summary>
	/// Settlement scheduling is handled by the state machine; this only logs the transition.
	/// </summary>
	private void SchedulePostRestorationSettlement()
	{
		GD.Print($"[{GetType().Name}] Settlement scheduled - state machine will handle transition");
	}

	private void ForcePhaseTransitionToBreakLoop()
	{
		if (IsStrikerValid() && (!_striker.Visible || _striker.Freeze))
		{
			EnsureStrikerRestored();
		}

		// State machine will handle transition automatically when pieces settle
	}

	/// <summary>
	/// Create a timer that automatically cleans itself up after execution.
	/// Tracks the timer to prevent orphaning.
	/// </summary>
	private Timer CreateAutoCleanupTimer(float waitTime, System.Action onTimeout)
	{
		if (_isDisposed)
		{
			return null;
		}

		var timer = new Timer();
		timer.WaitTime = waitTime;
		timer.OneShot = true;
		timer.Timeout += () =>
		{
			// Remove from tracking before cleanup
			_activeTimers.Remove(timer);

			onTimeout?.Invoke();
			timer.QueueFree();
		};

		_activeTimers.Add(timer);
		AddChild(timer);
		timer.Start();
		return timer;
	}

	protected CarromPiece CreatePiece(PieceType type, Vector2 position)
	{
		if (_pieceFactory == null)
		{
			return null;
		}

		// Use centralized piece factory which applies physics parameters
		return _pieceFactory.CreatePiece(type, position);
	}

	/// <summary>
	/// Cleans up all timers and references to prevent memory leaks.
	/// </summary>
	public override void _ExitTree()
	{
		if (_isDisposed)
		{
			return;
		}

		CleanupAllTimers();

		_board = null;
		_inputController = null;
		_physicsConfig = null;
		_pieceFactory = null;
		_striker = null;

		_isDisposed = true;
		GD.Print($"[{GetType().Name}] Mode manager cleanup completed");

		base._ExitTree();
	}

	private void CleanupAllTimers()
	{
		// ToList() avoids modification during iteration
		foreach (var timer in _activeTimers.ToList())
		{
			if (GodotObject.IsInstanceValid(timer))
			{
				timer.Stop();
				timer.QueueFree();
			}
		}

		_activeTimers.Clear();
		GD.Print($"[{GetType().Name}] Cleaned up {_activeTimers.Count} active timers");
	}
}
