using Godot;

/// <summary>
/// Concrete track definition class that serves as the root node of track scenes
/// Uses Line2D as the visual track representation and source of track bounds
/// </summary>
[Tool]
[GlobalClass]
public partial class TrackDefinition : Node2D, ITrackDefinition
{
	[ExportCategory("Track Setup")]
	[Export] public Line2D TrackLine { get; set; }
	[Export] public string TrackName { get; set; } = "Unnamed Track";
	[Export] public int NumberOfLaps { get; set; } = 3;

	[ExportCategory("Scene Node References")]
	[Export] public NodePath StartLinePath { get; set; }
	[Export] public NodePath FinishLinePath { get; set; }
	[Export] public Godot.Collections.Array<NodePath> CheckpointTriggerPaths { get; set; } = new Godot.Collections.Array<NodePath>();
	[Export] public NodePath TrackDirectionPath { get; set; }

	private Curve2D _cachedCurve;

	// Cached references to avoid repeated GetNode calls
	private LineTrigger _startLineCache;
	private LineTrigger _finishLineCache;
	private CheckpointTrigger[] _checkpointTriggersCache;
	private RayCast2D _trackDirectionCache;

	// Properties that lazily load and cache the actual node references
	public LineTrigger StartLine => _startLineCache ??= GetNodeOrNull<LineTrigger>(StartLinePath);
	public LineTrigger FinishLine => _finishLineCache ??= GetNodeOrNull<LineTrigger>(FinishLinePath);
	public RayCast2D TrackDirection => _trackDirectionCache ??= GetNodeOrNull<RayCast2D>(TrackDirectionPath);

	public CheckpointTrigger[] CheckpointTriggers
	{
		get
		{
			if (_checkpointTriggersCache == null && CheckpointTriggerPaths != null)
			{
				_checkpointTriggersCache = new CheckpointTrigger[CheckpointTriggerPaths.Count];
				for (int i = 0; i < CheckpointTriggerPaths.Count; i++)
				{
					_checkpointTriggersCache[i] = GetNodeOrNull<CheckpointTrigger>(CheckpointTriggerPaths[i]);
				}
			}
			return _checkpointTriggersCache ?? new CheckpointTrigger[0];
		}
	}

	public void SetupTrack()
	{
		_cachedCurve = null;

		if (TrackLine == null)
		{
			GD.PrintErr($"TrackDefinition '{TrackName}': No TrackLine assigned");
			return;
		}

		if (TrackLine.GetPointCount() < 2)
		{
			GD.PrintErr($"TrackDefinition '{TrackName}': TrackLine must have at least 2 points");
			return;
		}

		if (TrackLine.Width <= 0)
		{
			GD.PrintErr($"TrackDefinition '{TrackName}': TrackLine Width must be greater than 0");
			return;
		}

		_cachedCurve = LineUtils.Line2DToCurve2D(TrackLine);
		
		if (_cachedCurve == null)
		{
			GD.PrintErr($"TrackDefinition '{TrackName}': Failed to convert Line2D to Curve2D");
			return;
		}

		// Clear cached references to ensure fresh node lookups
		ClearCache();

		// Setup checkpoint indices
		var triggers = CheckpointTriggers;
		for (int i = 0; i < triggers.Length; i++)
		{
			if (triggers[i] != null)
			{
				triggers[i].CheckpointIndex = i;
			}
		}

		// Setup track direction raycast (if needed)
		if (TrackDirection != null)
		{
			var startPoint = GetStartLinePosition();
			var perpendicular = GetStartLineDirection();
			
			// Convert perpendicular back to track direction
			var actualTrackDirection = new Vector2(perpendicular.Y, -perpendicular.X);
			
			TrackDirection.GlobalPosition = startPoint;
			TrackDirection.TargetPosition = actualTrackDirection * 100.0f;
			TrackDirection.Enabled = true;
		}

		GD.Print($"TrackDefinition '{TrackName}': Track setup complete with {TrackLine.GetPointCount()} points, width {TrackLine.Width}");
	}

	public Curve2D GetTrackCurve()
	{
		return _cachedCurve;
	}

	public bool IsValidTrackPoint(Vector2 point)
	{
		if (TrackLine == null)
			return false;

		// Convert global point to local coordinates relative to this TrackDefinition node
		var localPoint = ToLocal(point);
		var result = LineUtils.IsPointNearLine2D(localPoint, TrackLine);

		// // Debug logging for coordinate transformation (remove after testing)
		// if (GD.Randf() < 0.01f) // Log ~1% of checks to avoid spam
		// {
		// 	GD.Print($"Track Check - Global: {point}, Local: {localPoint}, OnTrack: {result}, LineWidth: {TrackLine.Width}");
		// }
		
		return result;
	}

	/// <summary>
	/// Get the world position of the starting line from the StartLine trigger
	/// </summary>
	public Vector2 GetStartLinePosition()
	{
		return StartLine?.GlobalPosition ?? Vector2.Zero;
	}

	/// <summary>
	/// Get the direction vector for the starting line from the StartLine trigger rotation
	/// </summary>
	public Vector2 GetStartLineDirection()
	{
		if (StartLine == null)
			return Vector2.Right;
		
		// Get direction from StartLine's rotation
		return Vector2.FromAngle(StartLine.Rotation);
	}

	/// <summary>
	/// Get the world position of the finish line from the FinishLine trigger
	/// Falls back to StartLine if FinishLine is not configured (looping track)
	/// </summary>
	public Vector2 GetFinishLinePosition()
	{
		return FinishLine?.GlobalPosition ?? StartLine?.GlobalPosition ?? Vector2.Zero;
	}

	/// <summary>
	/// Get the direction vector for the finish line from the FinishLine trigger rotation
	/// Falls back to StartLine if FinishLine is not configured (looping track)
	/// </summary>
	public Vector2 GetFinishLineDirection()
	{
		if (FinishLine != null)
			return Vector2.FromAngle(FinishLine.Rotation);
		if (StartLine != null)
			return Vector2.FromAngle(StartLine.Rotation);
		return Vector2.Right;
	}

	public void ResetAllCheckpoints()
	{
		var triggers = CheckpointTriggers; // Get the cached array once
		for (int i = 0; i < triggers.Length; i++)
		{
			if (triggers[i] != null)
			{
				triggers[i].ResetCrossed();
			}
		}
	}

	/// <summary>
	/// Reset start line color to default state
	/// </summary>
	public void ResetStartLineColor()
	{
		// Reset start line color to default (usually white or track color)
		StartLine?.SetLineColor(Colors.White);
	}

	/// <summary>
	/// Clear cached node references to force fresh lookups
	/// </summary>
	public void ClearCache()
	{
		_startLineCache = null;
		_finishLineCache = null;
		_checkpointTriggersCache = null;
		_trackDirectionCache = null;
	}
}