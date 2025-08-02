using Godot;

/// <summary>
/// Checkpoint trigger for racing games
/// Provides checkpoint-specific signal, state management, and behavior coordination
/// </summary>
[GlobalClass]
public partial class RacingCheckpointTrigger : RacingLineTrigger
{

	public enum CheckpointState
	{
		Future,      // Not yet required (yellow)
		NextRequired, // Next required checkpoint (bright/enhanced)
		Crossed      // Already crossed (green)
	}

	[ExportCategory("Checkpoint State")]
	[Export] public int CheckpointIndex { get; set; } = 0;
	[Export] public bool IsCrossed { get; set; } = false;

	private Color _uncrossedColor = Colors.Yellow;
	private Color _crossedColor = Colors.Green;
	private Color _nextRequiredColor = Colors.Cyan;
	private CheckpointState _currentState = CheckpointState.Future;

	public RacingCheckpointTrigger()
	{
		TriggerName = "Checkpoint";
	}

	public override void _Ready()
	{
		base._Ready();
		
		// Set initial colors based on current visual line color
		if (VisualLine != null)
		{
			_uncrossedColor = VisualLine.DefaultColor;
		}
	}

	protected override void OnBodyEntered(Node2D body)
	{
		// Emit generic trigger hit and specific checkpoint crossed signals (not start line)
		EmitSignal(SignalName.TriggerHit, body, this);
		EmitSignal(SignalName.CheckpointCrossed, body, CheckpointIndex);
	}

	/// <summary>
	/// Mark this checkpoint as crossed and change visual color
	/// </summary>
	public void MarkAsCrossed()
	{
		IsCrossed = true;
		_currentState = CheckpointState.Crossed;
		SetLineColor(_crossedColor);
	}

	/// <summary>
	/// Reset this checkpoint to uncrossed state
	/// </summary>
	public void ResetCrossed()
	{
		IsCrossed = false;
		_currentState = CheckpointState.Future;
		SetLineColor(_uncrossedColor);
	}

	/// <summary>
	/// Mark this checkpoint as the next required checkpoint with enhanced visual
	/// </summary>
	public void SetNextRequiredState()
	{
		if (!IsCrossed)
		{
			_currentState = CheckpointState.NextRequired;
			SetLineColor(_nextRequiredColor);
		}
	}

	/// <summary>
	/// Set this checkpoint to future state (not yet required)
	/// </summary>
	public void SetFutureState()
	{
		if (!IsCrossed)
		{
			_currentState = CheckpointState.Future;
			SetLineColor(_uncrossedColor);
		}
	}

	/// <summary>
	/// Get current checkpoint state
	/// </summary>
	public CheckpointState GetCurrentState()
	{
		return _currentState;
	}

	/// <summary>
	/// Set custom colors for checkpoint states
	/// </summary>
	/// <param name="uncrossed">Color when checkpoint is future/not yet required</param>
	/// <param name="crossed">Color when checkpoint has been crossed</param>
	/// <param name="nextRequired">Color when checkpoint is next required (optional)</param>
	public void SetCheckpointColors(Color uncrossed, Color crossed, Color? nextRequired = null)
	{
		_uncrossedColor = uncrossed;
		_crossedColor = crossed;
		if (nextRequired.HasValue)
		{
			_nextRequiredColor = nextRequired.Value;
		}
		
		// Update current color based on current state
		UpdateVisualState();
	}

	/// <summary>
	/// Update visual appearance based on current state
	/// </summary>
	private void UpdateVisualState()
	{
		switch (_currentState)
		{
			case CheckpointState.Future:
				SetLineColor(_uncrossedColor);
				break;
			case CheckpointState.NextRequired:
				SetLineColor(_nextRequiredColor);
				break;
			case CheckpointState.Crossed:
				SetLineColor(_crossedColor);
				break;
		}
	}
}