using Godot;

/// <summary>
/// Start line trigger for racing games
/// Provides start-line specific signal and behavior coordination
/// </summary>
[GlobalClass]
public partial class StartLineTrigger : LineTrigger
{
	[Signal] public delegate void StartLineCrossedEventHandler(Node2D body);

	public StartLineTrigger()
	{
		TriggerName = "Start Line";
	}

	protected override void OnBodyEntered(Node2D body)
	{
		// Emit both the generic trigger hit and specific start line crossed signals
		base.OnBodyEntered(body);
		EmitSignal(SignalName.StartLineCrossed, body);
	}
}