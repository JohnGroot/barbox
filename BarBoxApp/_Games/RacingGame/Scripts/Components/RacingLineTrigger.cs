using System;
using Godot;

namespace BarBox.Games.Racing;

/// <summary>
/// Base class for line-based triggers that coordinate Area2D collision and Line2D visual components
/// Does not create or configure child components - only provides helper methods and signal coordination
/// Can be used directly for simple triggers or extended for complex behavior
/// </summary>
[GlobalClass]
public partial class RacingLineTrigger : Area2D
{
	[Signal]
	public delegate void TriggerHitEventHandler(Node2D body, RacingLineTrigger trigger);

	[Signal]
	public delegate void StartLineCrossedEventHandler(Node2D body);

	[Signal]
	public delegate void CheckpointCrossedEventHandler(Node2D body, int checkpointIndex);

	[ExportCategory("Component References")]
	[Export]
	public CollisionShape2D CollisionShape { get; set; }

	[Export]
	public Line2D VisualLine { get; set; }

	[ExportCategory("Trigger Info")]
	[Export]
	public string TriggerName { get; set; } = "Trigger";

	public override void _Ready()
	{
		// Validate required components
		if (CollisionShape == null)
		{
			GD.PrintErr($"RacingLineTrigger '{TriggerName}': CollisionShape2D reference not assigned");
		}

		if (VisualLine == null)
		{
			GD.PrintErr($"RacingLineTrigger '{TriggerName}': Line2D reference not assigned");
		}

		// Connect signal
		BodyEntered += OnBodyEntered;
	}

	public override void _ExitTree()
	{
		BodyEntered -= OnBodyEntered;
	}

	public float GetWidth()
	{
		// Try to get width from collision shape first
		if (CollisionShape?.Shape is RectangleShape2D rectShape)
		{
			return rectShape.Size.X;
		}

		// Every real trigger (StartTrigger/CheckpointTrigger prefabs) uses a SegmentShape2D, not
		// a RectangleShape2D, so this is the branch that actually matches in practice.
		if (CollisionShape?.Shape is SegmentShape2D segmentShape)
		{
			return segmentShape.A.DistanceTo(segmentShape.B);
		}

		// Fallback to visual line points
		if (VisualLine != null && VisualLine.GetPointCount() >= 2)
		{
			var point1 = VisualLine.GetPointPosition(0);
			var point2 = VisualLine.GetPointPosition(1);
			return point1.DistanceTo(point2);
		}

		return 0.0f;
	}

	public Color GetColor()
	{
		return VisualLine?.DefaultColor ?? Colors.White;
	}

	public void UpdatePosition(Vector2 position, Vector2 direction)
	{
		GlobalPosition = position;
		Rotation = direction.Angle();
	}

	/// <summary>
	/// Direct callback rather than a signal — internal-only integration point.
	/// RacingTrackRenderer subscribes to reroute checkpoint color feedback onto its own stroke
	/// once VisualLine is hidden.
	/// </summary>
	public Action<Color> VisualColorChanged;

	public void SetLineColor(Color color)
	{
		if (VisualLine != null)
		{
			VisualLine.DefaultColor = color;
		}

		VisualColorChanged?.Invoke(color);
	}

	protected virtual void OnBodyEntered(Node2D body)
	{
		EmitSignal(SignalName.TriggerHit, body, this);
		EmitSignal(SignalName.StartLineCrossed, body);
	}
}
