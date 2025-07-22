using Godot;

/// <summary>
/// Checkpoint trigger for racing games
/// Provides checkpoint-specific signal, state management, and behavior coordination
/// </summary>
[GlobalClass]
public partial class CheckpointTrigger : LineTrigger
{
	[Signal] public delegate void CheckpointCrossedEventHandler(Node2D body, int checkpointIndex);

	[ExportCategory("Checkpoint State")]
	[Export] public int CheckpointIndex { get; set; } = 0;
	[Export] public bool IsCrossed { get; set; } = false;

	private Color _uncrossedColor = Colors.Yellow;
	private Color _crossedColor = Colors.Green;

	public CheckpointTrigger()
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
		// Emit both the generic trigger hit and specific checkpoint crossed signals
		base.OnBodyEntered(body);
		EmitSignal(SignalName.CheckpointCrossed, body, CheckpointIndex);
	}

	/// <summary>
	/// Mark this checkpoint as crossed and change visual color
	/// </summary>
	public void MarkAsCrossed()
	{
		IsCrossed = true;
		SetLineColor(_crossedColor);
	}

	/// <summary>
	/// Reset this checkpoint to uncrossed state
	/// </summary>
	public void ResetCrossed()
	{
		IsCrossed = false;
		SetLineColor(_uncrossedColor);
	}

	/// <summary>
	/// Set custom colors for crossed/uncrossed states
	/// </summary>
	/// <param name="uncrossed">Color when checkpoint hasn't been crossed</param>
	/// <param name="crossed">Color when checkpoint has been crossed</param>
	public void SetCheckpointColors(Color uncrossed, Color crossed)
	{
		_uncrossedColor = uncrossed;
		_crossedColor = crossed;
		
		// Update current color based on state
		SetLineColor(IsCrossed ? _crossedColor : _uncrossedColor);
	}
}