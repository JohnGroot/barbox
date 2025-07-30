using Godot;
using System.Collections.Generic;

/// <summary>
/// Centralized phase management for Carrom game with validation and transition logging
/// </summary>
[GlobalClass]
public partial class CarromPhaseManager : Node
{
	[Signal] public delegate void PhaseChangedEventHandler(GamePhase oldPhase, GamePhase newPhase);
	[Signal] public delegate void PiecesSettledEventHandler();

	private GamePhase _currentPhase = GamePhase.Setup;

	/// <summary>
	/// Current game phase
	/// </summary>
	public GamePhase CurrentPhase => _currentPhase;

	/// <summary>
	/// Transition to a new phase with validation
	/// </summary>
	public bool TransitionTo(GamePhase newPhase)
	{
		// Skip validation for no-op transitions
		if (_currentPhase == newPhase)
		{
			GD.Print($"[CarromPhaseManager] No-op transition: {_currentPhase} (already in this phase)");
			return true;
		}
		
		if (!IsValidTransition(_currentPhase, newPhase))
		{
			GD.PrintErr($"[CarromPhaseManager] Invalid phase transition: {_currentPhase} -> {newPhase}");
			LogValidTransitions(_currentPhase);
			return false;
		}

		var oldPhase = _currentPhase;
		_currentPhase = newPhase;

		GD.Print($"[CarromPhaseManager] Phase transition: {oldPhase} -> {newPhase}");
		EmitSignal(SignalName.PhaseChanged, (int)oldPhase, (int)newPhase);

		return true;
	}
	
	/// <summary>
	/// Log valid transitions from current phase for debugging
	/// </summary>
	private void LogValidTransitions(GamePhase fromPhase)
	{
		var validTransitions = new List<GamePhase>();
		
		foreach (GamePhase phase in System.Enum.GetValues<GamePhase>())
		{
			if (IsValidTransition(fromPhase, phase))
			{
				validTransitions.Add(phase);
			}
		}
		
		GD.Print($"[CarromPhaseManager] Valid transitions from {fromPhase}: [{string.Join(", ", validTransitions)}]");
	}

	/// <summary>
	/// Check if phase transition is valid
	/// </summary>
	private bool IsValidTransition(GamePhase from, GamePhase to)
	{
		// Define valid phase transitions
		return (from, to) switch
		{
			// From Setup
			(GamePhase.Setup, GamePhase.Active) => true,
			
			// From Active
			(GamePhase.Active, GamePhase.Processing) => true,
			(GamePhase.Active, GamePhase.Setup) => true, // Reset
			
			// From Processing
			(GamePhase.Processing, GamePhase.Active) => true,
			(GamePhase.Processing, GamePhase.Setup) => true, // Reset
			
			// Same phase (no-op)
			var (a, b) when a == b => true,
			
			// All other transitions are invalid
			_ => false
		};
	}

	/// <summary>
	/// Start game (Setup -> Active)
	/// </summary>
	public bool StartGame()
	{
		return TransitionTo(GamePhase.Active);
	}

	/// <summary>
	/// Begin processing after strike
	/// </summary>
	public bool BeginProcessing()
	{
		if (!CanReceiveInput())
		{
			GD.PrintErr($"[CarromPhaseManager] Cannot begin processing - not in active state (current: {_currentPhase})");
			return false;
		}
		return TransitionTo(GamePhase.Processing);
	}

	/// <summary>
	/// Pieces have settled, ready for next turn
	/// </summary>
	public void OnPiecesSettled()
	{
		if (_currentPhase == GamePhase.Processing)
		{
			EmitSignal(SignalName.PiecesSettled);
			// Don't automatically transition - let mode managers decide
		}
	}

	/// <summary>
	/// Reset game to setup phase
	/// </summary>
	public bool ResetGame()
	{
		return TransitionTo(GamePhase.Setup);
	}

	/// <summary>
	/// Check if game is in an active state (can receive input)
	/// </summary>
	public bool CanReceiveInput()
	{
		return _currentPhase == GamePhase.Active;
	}

	/// <summary>
	/// Check if game is processing (pieces moving)
	/// </summary>
	public new bool IsProcessing()
	{
		return _currentPhase == GamePhase.Processing;
	}
	
	/// <summary>
	/// Get current phase status for debugging
	/// </summary>
	public string GetPhaseStatus()
	{
		return $"Current: {_currentPhase}, CanReceiveInput: {CanReceiveInput()}, IsProcessing: {IsProcessing()}";
	}
	
	/// <summary>
	/// Check if game operations (strikes, moves) can be executed
	/// </summary>
	public bool CanExecuteGameOperation(string operationName = "Game operation")
	{
		if (!CanReceiveInput())
		{
			GD.PrintErr($"[CarromPhaseManager] {operationName} requires Active phase, current: {_currentPhase}");
			return false;
		}
		
		return true;
	}
	
	/// <summary>
	/// Check if administrative operations (resets, cleanups) can be executed
	/// </summary>
	public bool CanExecuteAdminOperation(string operationName = "Admin operation")
	{
		// Admin operations are always allowed regardless of phase
		GD.Print($"[CarromPhaseManager] {operationName} allowed (admin operation)");
		return true;
	}
	
	/// <summary>
	/// Check if query operations (reads, status checks) can be executed
	/// </summary>
	public bool CanExecuteQuery(string operationName = "Query operation")
	{
		// Query operations are always allowed
		return true;
	}
}