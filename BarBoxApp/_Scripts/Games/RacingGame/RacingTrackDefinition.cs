using Godot;

/// <summary>
/// Concrete track definition class that serves as the root node of track scenes
/// Uses Line2D as the visual track representation and source of track bounds
/// </summary>
[Tool]
[GlobalClass]
public partial class RacingTrackDefinition : Node2D, IRacingTrackDefinition
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
	private RacingLineTrigger _startLineCache;
	private RacingLineTrigger _finishLineCache;
	private RacingCheckpointTrigger[] _checkpointTriggersCache;
	private RayCast2D _trackDirectionCache;

	// Properties that lazily load and cache the actual node references
	public RacingLineTrigger StartLine => _startLineCache ??= GetNodeOrNull<RacingLineTrigger>(StartLinePath);
	public RacingLineTrigger FinishLine => _finishLineCache ??= GetNodeOrNull<RacingLineTrigger>(FinishLinePath);
	public RayCast2D TrackDirection => _trackDirectionCache ??= GetNodeOrNull<RayCast2D>(TrackDirectionPath);

	public RacingCheckpointTrigger[] CheckpointTriggers
	{
		get
		{
			if (_checkpointTriggersCache == null && CheckpointTriggerPaths != null)
			{
				_checkpointTriggersCache = new RacingCheckpointTrigger[CheckpointTriggerPaths.Count];
				for (int i = 0; i < CheckpointTriggerPaths.Count; i++)
				{
					_checkpointTriggersCache[i] = GetNodeOrNull<RacingCheckpointTrigger>(CheckpointTriggerPaths[i]);
				}
			}
			return _checkpointTriggersCache ?? new RacingCheckpointTrigger[0];
		}
	}

	public void SetupTrack()
	{
		_cachedCurve = null;

		if (TrackLine == null)
		{
			GD.PrintErr($"RacingTrackDefinition '{TrackName}': No TrackLine assigned");
			return;
		}

		if (TrackLine.GetPointCount() < 2)
		{
			GD.PrintErr($"RacingTrackDefinition '{TrackName}': TrackLine must have at least 2 points");
			return;
		}

		if (TrackLine.Width <= 0)
		{
			GD.PrintErr($"RacingTrackDefinition '{TrackName}': TrackLine Width must be greater than 0");
			return;
		}

		_cachedCurve = LineUtils.Line2DToCurve2D(TrackLine);
		
		if (_cachedCurve == null)
		{
			GD.PrintErr($"RacingTrackDefinition '{TrackName}': Failed to convert Line2D to Curve2D");
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

		GD.Print($"RacingTrackDefinition '{TrackName}': Track setup complete with {TrackLine.GetPointCount()} points, width {TrackLine.Width}");
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
		
		// Further transform to TrackLine's local coordinate space to account for TrackLine position offset
		var trackLineLocalPoint = localPoint - TrackLine.Position;
		
		var result = LineUtils.IsPointNearLine2D(trackLineLocalPoint, TrackLine);

		// // Debug logging for coordinate transformation (remove after testing)
		// if (GD.Randf() < 0.01f) // Log ~1% of checks to avoid spam
		// {
		// 	GD.Print($"Track Check - Global: {point}, Local: {localPoint}, TrackLineLocal: {trackLineLocalPoint}, OnTrack: {result}, LineWidth: {TrackLine.Width}");
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
	/// Calculate the centroid (center point) of the track based on all track points
	/// </summary>
	public Vector2 GetTrackCentroid()
	{
		if (TrackLine == null || TrackLine.GetPointCount() == 0)
			return Vector2.Zero;

		Vector2 centroid = Vector2.Zero;
		int pointCount = TrackLine.GetPointCount();

		for (int i = 0; i < pointCount; i++)
		{
			centroid += TrackLine.GetPointPosition(i);
		}

		centroid /= pointCount;
		
		// Convert to global coordinates
		return ToGlobal(centroid);
	}

	/// <summary>
	/// Get the bounding rectangle that contains the entire track including width
	/// </summary>
	public Rect2 GetTrackBounds()
	{
		if (TrackLine == null || TrackLine.GetPointCount() == 0)
			return new Rect2();
		
		Vector2 minPoint = TrackLine.GetPointPosition(0);
		Vector2 maxPoint = TrackLine.GetPointPosition(0);

		// Find min/max points
		for (int i = 1; i < TrackLine.GetPointCount(); i++)
		{
			Vector2 point = TrackLine.GetPointPosition(i);
			minPoint = new Vector2(Mathf.Min(minPoint.X, point.X), Mathf.Min(minPoint.Y, point.Y));
			maxPoint = new Vector2(Mathf.Max(maxPoint.X, point.X), Mathf.Max(maxPoint.Y, point.Y));
		}

		// Expand bounds by track width to include the full track area
		float halfWidth = TrackLine.Width / 2.0f;
		minPoint -= new Vector2(halfWidth, halfWidth);
		maxPoint += new Vector2(halfWidth, halfWidth);

		// Create bounds rectangle
		Vector2 size = maxPoint - minPoint;
		var localBounds = new Rect2(minPoint, size);

		// Convert to global coordinates - transform both position and size through node scale
		Vector2 globalTopLeft = ToGlobal(localBounds.Position);
		Vector2 globalBottomRight = ToGlobal(localBounds.Position + localBounds.Size);
		Vector2 globalSize = globalBottomRight - globalTopLeft;

		return new Rect2(globalTopLeft, globalSize);
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