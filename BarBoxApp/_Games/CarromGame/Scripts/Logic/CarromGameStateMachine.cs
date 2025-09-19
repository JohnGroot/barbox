using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Centralized state machine for CarromGame with brute force settlement detection
/// Replaces complex distributed state management with simple, deterministic flow
/// </summary>
public partial class CarromGameStateMachine : Node, ICarromGameState
{
	// ================================================================
	// SIGNALS
	// ================================================================
	
	[Signal] public delegate void StateChangedEventHandler(GameState oldState, GameState newState);
	[Signal] public delegate void InputAvailabilityChangedEventHandler(bool canAcceptInput);
	[Signal] public delegate void SettlementCompletedEventHandler();

	// ================================================================
	// ENUMS
	// ================================================================
	
	/// <summary>
	/// Simple game states for deterministic flow
	/// </summary>
	public enum GameState
	{
		/// <summary>Game initializing, not ready for play</summary>
		Initializing,
		/// <summary>Ready for player input - pieces settled, game active</summary>
		Ready,
		/// <summary>Strike executed, pieces beginning to move</summary>
		Strike,
		/// <summary>Pieces in motion, monitoring for settlement</summary>
		Physics,
		/// <summary>All pieces stopped, processing settlement rules</summary>
		Settlement
	}

	// ================================================================
	// PROPERTIES
	// ================================================================
	
	/// <summary>Current game state</summary>
	public GameState CurrentState { get; private set; } = GameState.Initializing;
	
	/// <summary>Whether input can be accepted (only true in Ready state)</summary>
	public bool CanAcceptInput => CurrentState == GameState.Ready;
	
	/// <summary>Physics threshold for considering a piece "stopped"</summary>
	[Export] public float StoppedVelocityThreshold { get; set; } = 1.0f;
	
	/// <summary>Time piece must be below threshold to be considered settled</summary>
	[Export] public float SettlementTimeThreshold { get; set; } = 0.1f;
	
	/// <summary>Minimum physics frames to wait before settlement checking begins</summary>
	[Export] public int MinimumSettlementFrames { get; set; } = 3;

	// ================================================================
	// PRIVATE FIELDS
	// ================================================================
	
	private List<CarromPiece> _allPieces = new();
	private ICarromModeManager _modeManager;
	private double _lastSettlementCheckTime;
	private bool _isPhysicsMonitoringActive = false;
	private int _physicsFramesSinceEnter = 0;

	// ================================================================
	// INITIALIZATION
	// ================================================================
	
	public override void _Ready()
	{
		SetPhysicsProcess(false); // Only enable during Physics state
	}

	/// <summary>
	/// Initialize the state machine with required dependencies
	/// </summary>
	/// <param name="pieces">All pieces on the board to monitor</param>
	/// <param name="modeManager">Mode manager for settlement processing</param>
	public void Initialize(List<CarromPiece> pieces, ICarromModeManager modeManager)
	{
		_allPieces = pieces ?? new List<CarromPiece>();
		_modeManager = modeManager;
		
		TransitionTo(GameState.Ready);
		GD.Print($"[CarromGameStateMachine] Initialized with {_allPieces.Count} pieces");
	}

	/// <summary>
	/// Update the list of pieces being monitored
	/// </summary>
	public void UpdatePieceList(List<CarromPiece> pieces)
	{
		_allPieces = pieces ?? new List<CarromPiece>();
		GD.Print($"[CarromGameStateMachine] Updated piece list: {_allPieces.Count} pieces");
	}

	// ================================================================
	// STATE TRANSITIONS
	// ================================================================
	
	/// <summary>
	/// Transition to a new state with proper cleanup and setup
	/// </summary>
	private void TransitionTo(GameState newState)
	{
		if (CurrentState == newState) return;
		
		var oldState = CurrentState;
		
		// Exit current state
		ExitState(oldState);
		
		// Update state
		CurrentState = newState;
		
		// Enter new state  
		EnterState(newState);
		
		// Emit signals
		EmitSignal(SignalName.StateChanged, (int)oldState, (int)newState);
		EmitSignal(SignalName.InputAvailabilityChanged, CanAcceptInput);
		
		GD.Print($"[CarromGameStateMachine] {oldState} → {newState}");
	}

	/// <summary>
	/// Exit current state - cleanup operations
	/// </summary>
	private void ExitState(GameState state)
	{
		switch (state)
		{
			case GameState.Physics:
				SetPhysicsProcess(false);
				_isPhysicsMonitoringActive = false;
				_physicsFramesSinceEnter = 0;
				break;
		}
	}

	/// <summary>
	/// Enter new state - setup operations
	/// </summary>
	private void EnterState(GameState state)
	{
		switch (state)
		{
			case GameState.Ready:
				// Game is ready for input
				break;
				
			case GameState.Strike:
				// Strike executed, transition immediately to Physics monitoring
				TransitionTo(GameState.Physics);
				break;

			case GameState.Physics:
				// Begin monitoring pieces for settlement
				SetPhysicsProcess(true);
				_isPhysicsMonitoringActive = true;
				_lastSettlementCheckTime = Time.GetUnixTimeFromSystem();
				_physicsFramesSinceEnter = 0;
				break;

			case GameState.Settlement:
				// Process settlement immediately and synchronously
				ProcessSettlement();
				break;
		}
	}

	// ================================================================
	// PUBLIC API
	// ================================================================
	
	/// <summary>
	/// Called when a strike is executed - begins physics monitoring
	/// </summary>
	public void OnStrikeExecuted()
	{
		if (CurrentState == GameState.Ready)
		{
			TransitionTo(GameState.Strike);
		}
	}

	/// <summary>
	/// Force transition to Ready state (for manual recovery)
	/// </summary>
	public void ForceToReady()
	{
		TransitionTo(GameState.Ready);
	}

	/// <summary>
	/// Get current state name (implements ICarromGameState interface)
	/// </summary>
	public string GetCurrentStateName()
	{
		return CurrentState.ToString();
	}

	/// <summary>
	/// Get current state for debugging
	/// </summary>
	public string GetStateDebugInfo()
	{
		return $"State: {CurrentState}, Input: {CanAcceptInput}, Pieces: {_allPieces.Count}, Monitoring: {_isPhysicsMonitoringActive}, PhysicsFrames: {_physicsFramesSinceEnter}";
	}

	// ================================================================
	// BRUTE FORCE SETTLEMENT DETECTION
	// ================================================================
	
	/// <summary>
	/// Physics process - brute force check all pieces every frame during Physics state
	/// </summary>
	public override void _PhysicsProcess(double delta)
	{
		if (!_isPhysicsMonitoringActive || CurrentState != GameState.Physics)
		{
			return;
		}

		// Increment frame counter
		_physicsFramesSinceEnter++;

		// Wait minimum frames before checking settlement to allow physics propagation
		if (_physicsFramesSinceEnter < MinimumSettlementFrames)
		{
			return;
		}

		// Brute force: check all pieces every physics frame
		if (AreAllPiecesStopped())
		{
			TransitionTo(GameState.Settlement);
		}
	}

	/// <summary>
	/// Brute force settlement detection - simple velocity check on all pieces
	/// </summary>
	private bool AreAllPiecesStopped()
	{
		// Simple brute force check - iterate all pieces and check velocity
		foreach (var piece in _allPieces)
		{
			if (!GodotObject.IsInstanceValid(piece))
				continue;
				
			// Simple velocity threshold check
			if (piece.LinearVelocity.Length() > StoppedVelocityThreshold)
			{
				return false;
			}
			
			// Angular velocity check for spinning pieces
			if (Mathf.Abs(piece.AngularVelocity) > StoppedVelocityThreshold)
			{
				return false;
			}
		}
		
		return true;
	}

	// ================================================================
	// SETTLEMENT PROCESSING
	// ================================================================
	

	/// <summary>
	/// Process settlement immediately and synchronously
	/// State machine controls the flow and determines next state
	/// </summary>
	private void ProcessSettlement()
	{
		try
		{
			// Execute mode-specific settlement logic synchronously
			if (_modeManager != null)
			{
				_modeManager.ProcessSettlement();
			}

			// Emit settlement completed signal for external listeners
			EmitSignal(SignalName.SettlementCompleted);

			// Always transition back to Ready state after settlement
			// Mode managers will handle their internal logic but won't control state transitions
			TransitionTo(GameState.Ready);
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"[CarromGameStateMachine] Settlement processing failed: {ex.Message}");
			GD.PrintErr($"[CarromGameStateMachine] Stack trace: {ex.StackTrace}");

			// Still emit settlement completed and return to Ready state for recovery
			EmitSignal(SignalName.SettlementCompleted);
			TransitionTo(GameState.Ready);
		}
	}

	// ================================================================
	// DEBUG AND DIAGNOSTICS
	// ================================================================
	
	/// <summary>
	/// Get detailed state information for debugging
	/// </summary>
	public Dictionary<string, object> GetDebugState()
	{
		return new Dictionary<string, object>
		{
			["CurrentState"] = CurrentState.ToString(),
			["CanAcceptInput"] = CanAcceptInput,
			["PieceCount"] = _allPieces.Count,
			["PhysicsMonitoring"] = _isPhysicsMonitoringActive,
			["PhysicsFramesSinceEnter"] = _physicsFramesSinceEnter,
			["MinimumSettlementFrames"] = MinimumSettlementFrames,
			["ValidPieces"] = _allPieces.Count(p => GodotObject.IsInstanceValid(p)),
			["MovingPieces"] = _allPieces.Count(p => GodotObject.IsInstanceValid(p) && p.LinearVelocity.Length() > StoppedVelocityThreshold),
			["LastSettlementCheck"] = _lastSettlementCheckTime
		};
	}

	/// <summary>
	/// Log current state and piece information
	/// </summary>
	public void LogDebugInfo()
	{
		var debugState = GetDebugState();
		GD.Print($"[CarromGameStateMachine] Debug State: {string.Join(", ", debugState.Select(kv => $"{kv.Key}={kv.Value}"))}");
		
		// Log individual piece states
		for (int i = 0; i < _allPieces.Count; i++)
		{
			var piece = _allPieces[i];
			if (GodotObject.IsInstanceValid(piece))
			{
				var velocity = piece.LinearVelocity.Length();
				var angular = piece.AngularVelocity;
				var stopped = velocity <= StoppedVelocityThreshold && Mathf.Abs(angular) <= StoppedVelocityThreshold;
				GD.Print($"  Piece {i}: Vel={velocity:F2}, Ang={angular:F2}, Stopped={stopped}");
			}
			else
			{
				GD.Print($"  Piece {i}: INVALID");
			}
		}
	}
}