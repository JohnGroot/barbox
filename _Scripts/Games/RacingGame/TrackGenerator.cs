using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

[Tool]
public partial class TrackGenerator : Node2D
{
	[ExportCategory("Track Generation")]
	[Export] public int InitialPointCount { get; set; } = 8;
	[Export] public float TrackRadius { get; set; } = 400.0f;
	[Export] public float MinPointDistance { get; set; } = 100.0f;
	[Export] public float MaxDisplacement { get; set; } = 150.0f;
	[Export] public float MinAngle { get; set; } = 30.0f;
	[Export] public float MaxAngle { get; set; } = 150.0f;
	[Export] public float TrackDifficulty { get; set; } = 0.5f;
	[Export] public int SplineSegments { get; set; } = 100;
	[Export] public bool UseConvexHull { get; set; } = true;
	[Export] public bool EnforceSeamlessLoop { get; set; } = true;
	[Export] public string TrackSeed { get; set; } = "";

	[ExportCategory("Visual")]
	[Export] public float TrackWidth { get; set; } = 8.0f;
	[Export] public Color TrackColor { get; set; } = Colors.White;
	[Export] public bool ShowDebugPoints { get; set; } = false;

	[ExportCategory("Editor Tools")]
	[Export] public bool RegenerateTrack
	{
		get => _regenerateTrack;
		set
		{
			if (value && value != _regenerateTrack)
			{
				_regenerateTrack = false;
				if (Engine.IsEditorHint())
				{
					CallDeferred(MethodName.RegenerateTrackDeferred);
				}
				else
				{
					// Also allow regeneration during runtime
					RegenerateTrackDeferred();
				}
			}
		}
	}

	private bool _regenerateTrack = false;
	private Vector2[] _trackPoints;
	private Curve2D _trackCurve;
	private Path2D _trackPath;
	private Line2D _trackVisual;
	private Node2D _debugPointsParent;

	public override void _Ready()
	{
		if (Engine.IsEditorHint())
		{
			SetNotifyTransform(true);
		}
	}

	public Vector2[] GenerateTrack(string seed = "")
	{
		if (!string.IsNullOrEmpty(seed))
		{
			TrackSeed = seed;
		}

		// Seed the random number generator
		if (!string.IsNullOrEmpty(TrackSeed))
		{
			GD.Seed((ulong)TrackSeed.GetHashCode());
		}

		var viewport = GetViewport();
		if (viewport == null)
		{
			GD.PrintErr("TrackGenerator: No viewport found");
			return Array.Empty<Vector2>();
		}

		var screenSize = viewport.GetVisibleRect().Size;
		var center = screenSize / 2;

		// Generate initial random points
		var initialPoints = GenerateInitialPoints(center, screenSize);

		// Apply convex hull if enabled
		Vector2[] hullPoints = UseConvexHull ? ComputeConvexHull(initialPoints) : initialPoints;

		// Apply displacement and constraints
		var processedPoints = ProcessTrackPoints(hullPoints, center, screenSize);

		// Store the final track points
		_trackPoints = processedPoints;

		// Create the track curve
		CreateTrackCurve();

		// Update visuals
		UpdateVisuals();

		return _trackPoints;
	}

	private Vector2[] GenerateInitialPoints(Vector2 center, Vector2 screenSize)
	{
		var points = new List<Vector2>();
		var margin = 50.0f;

		for (int i = 0; i < InitialPointCount; i++)
		{
			Vector2 point;
			int attempts = 0;
			const int maxAttempts = 100;

			do
			{
				// Generate point around a circle with random variation
				float angle = (float)(i * 2 * Mathf.Pi / InitialPointCount);
				var baseRadius = TrackRadius * (float)GD.RandRange(0.7, 1.3);
				
				point = center + new Vector2(
					Mathf.Cos(angle) * baseRadius,
					Mathf.Sin(angle) * baseRadius
				);

				// Ensure point is within screen bounds
				point.X = Mathf.Clamp(point.X, margin, screenSize.X - margin);
				point.Y = Mathf.Clamp(point.Y, margin, screenSize.Y - margin);

				attempts++;
			}
			while (attempts < maxAttempts && !IsValidPointPlacement(point, points));

			points.Add(point);
		}

		return points.ToArray();
	}

	private bool IsValidPointPlacement(Vector2 newPoint, List<Vector2> existingPoints)
	{
		foreach (var existingPoint in existingPoints)
		{
			if (newPoint.DistanceTo(existingPoint) < MinPointDistance)
				return false;
		}
		return true;
	}

	private void CreateTrackCurve()
	{
		if (_trackPoints == null || _trackPoints.Length < 3)
		{
			GD.PrintErr("TrackGenerator: Not enough track points to create curve");
			return;
		}

		_trackCurve = new Curve2D();

		foreach (var point in _trackPoints)
		{
			_trackCurve.AddPoint(point);
		}

		// Close the loop
		_trackCurve.AddPoint(_trackPoints[0]);
	}

	private void UpdateVisuals()
	{
		// Clear existing visuals
		ClearVisuals();

		// Create track path
		if (_trackCurve != null)
		{
			_trackPath = new Path2D();
			_trackPath.Curve = _trackCurve;
			AddChild(_trackPath);

			// Create visual line
			_trackVisual = new Line2D();
			_trackVisual.Width = TrackWidth;
			_trackVisual.DefaultColor = TrackColor;

			var bakeLength = _trackCurve.GetBakedLength();
			for (int i = 0; i <= SplineSegments; i++)
			{
				var offset = (float)i / SplineSegments * bakeLength;
				var point = _trackCurve.SampleBaked(offset);
				_trackVisual.AddPoint(point);
			}

			AddChild(_trackVisual);
		}

		// Create debug points if enabled
		if (ShowDebugPoints && _trackPoints != null)
		{
			_debugPointsParent = new Node2D();
			_debugPointsParent.Name = "DebugPoints";
			AddChild(_debugPointsParent);

			for (int i = 0; i < _trackPoints.Length; i++)
			{
				var debugPoint = new Node2D();
				debugPoint.Position = _trackPoints[i];
				debugPoint.Name = $"Point_{i}";
				_debugPointsParent.AddChild(debugPoint);
			}
		}
	}

	private void ClearVisuals()
	{
		if (_trackPath != null)
		{
			_trackPath.QueueFree();
			_trackPath = null;
		}

		if (_trackVisual != null)
		{
			_trackVisual.QueueFree();
			_trackVisual = null;
		}

		if (_debugPointsParent != null)
		{
			_debugPointsParent.QueueFree();
			_debugPointsParent = null;
		}
	}

	private void RegenerateTrackDeferred()
	{
		if (Engine.IsEditorHint())
		{
			GD.Print("TrackGenerator: Regenerating track in editor");
		}
		
		// Clear existing visuals first
		ClearVisuals();
		
		// Generate new track
		GenerateTrack();
		
		// Force editor to update if in editor mode
		if (Engine.IsEditorHint())
		{
			NotifyPropertyListChanged();
			QueueRedraw();
		}
	}

	// Graham scan algorithm for convex hull computation
	private Vector2[] ComputeConvexHull(Vector2[] points)
	{
		if (points.Length < 3)
			return points;

		// Find the bottom-most point (or left most in case of tie)
		var startPoint = points[0];
		int startIndex = 0;
		
		for (int i = 1; i < points.Length; i++)
		{
			if (points[i].Y < startPoint.Y || 
			    (points[i].Y == startPoint.Y && points[i].X < startPoint.X))
			{
				startPoint = points[i];
				startIndex = i;
			}
		}

		// Sort points by polar angle with respect to start point
		var sortedPoints = new List<Vector2>(points);
		sortedPoints.RemoveAt(startIndex);
		
		sortedPoints.Sort((a, b) =>
		{
			var angleA = Mathf.Atan2(a.Y - startPoint.Y, a.X - startPoint.X);
			var angleB = Mathf.Atan2(b.Y - startPoint.Y, b.X - startPoint.X);
			
			if (Mathf.Abs(angleA - angleB) < 0.001f)
			{
				// If angles are equal, sort by distance
				var distA = startPoint.DistanceSquaredTo(a);
				var distB = startPoint.DistanceSquaredTo(b);
				return distA.CompareTo(distB);
			}
			
			return angleA.CompareTo(angleB);
		});

		// Graham scan
		var hull = new List<Vector2> { startPoint };
		
		foreach (var point in sortedPoints)
		{
			// Remove points that create right turn
			while (hull.Count > 1 && 
			       CrossProduct(hull[hull.Count - 2], hull[hull.Count - 1], point) <= 0)
			{
				hull.RemoveAt(hull.Count - 1);
			}
			
			hull.Add(point);
		}

		return hull.ToArray();
	}

	// Helper method to compute cross product for determining turn direction
	private float CrossProduct(Vector2 o, Vector2 a, Vector2 b)
	{
		return (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
	}

	private Vector2[] ProcessTrackPoints(Vector2[] points, Vector2 center, Vector2 screenSize)
	{
		if (points.Length < 3)
			return points;

		var processedPoints = new List<Vector2>(points);
		
		// Step 1: Add midpoints between existing points for more detail
		var expandedPoints = new List<Vector2>();
		for (int i = 0; i < processedPoints.Count; i++)
		{
			expandedPoints.Add(processedPoints[i]);
			
			// Add midpoint to next point (wrapping around)
			var nextIndex = (i + 1) % processedPoints.Count;
			var midpoint = (processedPoints[i] + processedPoints[nextIndex]) / 2;
			expandedPoints.Add(midpoint);
		}

		// Step 2: Apply random displacement based on difficulty and max displacement
		var displacedPoints = new List<Vector2>();
		var margin = 50.0f;
		
		for (int i = 0; i < expandedPoints.Count; i++)
		{
			var originalPoint = expandedPoints[i];
			var displacement = Vector2.Zero;
			
			// Apply random displacement scaled by difficulty
			var displacementMagnitude = MaxDisplacement * TrackDifficulty * (float)GD.RandRange(0.0, 1.0);
			var displacementAngle = (float)GD.RandRange(0.0, 2 * Mathf.Pi);
			
			displacement = new Vector2(
				Mathf.Cos(displacementAngle) * displacementMagnitude,
				Mathf.Sin(displacementAngle) * displacementMagnitude
			);
			
			var newPoint = originalPoint + displacement;
			
			// Ensure point stays within screen bounds
			newPoint.X = Mathf.Clamp(newPoint.X, margin, screenSize.X - margin);
			newPoint.Y = Mathf.Clamp(newPoint.Y, margin, screenSize.Y - margin);
			
			displacedPoints.Add(newPoint);
		}

		// Step 3: Enforce minimum distance constraints
		var finalPoints = EnforceMinimumDistances(displacedPoints);

		// Step 4: Validate and fix angle constraints
		finalPoints = EnforceAngleConstraints(finalPoints);

		// Step 5: Ensure seamless loop closure
		if (EnforceSeamlessLoop)
		{
			finalPoints = EnsureSeamlessLoop(finalPoints);
		}

		return finalPoints.ToArray();
	}

	private List<Vector2> EnforceMinimumDistances(List<Vector2> points)
	{
		var result = new List<Vector2>(points);
		bool changed = true;
		int iterations = 0;
		const int maxIterations = 50;

		while (changed && iterations < maxIterations)
		{
			changed = false;
			iterations++;

			for (int i = 0; i < result.Count; i++)
			{
				for (int j = i + 1; j < result.Count; j++)
				{
					var distance = result[i].DistanceTo(result[j]);
					
					if (distance < MinPointDistance)
					{
						// Push points apart
						var direction = (result[j] - result[i]).Normalized();
						var pushDistance = (MinPointDistance - distance) / 2;
						
						result[i] -= direction * pushDistance;
						result[j] += direction * pushDistance;
						changed = true;
					}
				}
			}
		}

		return result;
	}

	private List<Vector2> EnforceAngleConstraints(List<Vector2> points)
	{
		if (points.Count < 3)
			return points;

		var result = new List<Vector2>(points);
		
		for (int i = 0; i < result.Count; i++)
		{
			var prevIndex = (i - 1 + result.Count) % result.Count;
			var nextIndex = (i + 1) % result.Count;
			
			var prevPoint = result[prevIndex];
			var currentPoint = result[i];
			var nextPoint = result[nextIndex];
			
			// Calculate angle between segments
			var vec1 = (currentPoint - prevPoint).Normalized();
			var vec2 = (nextPoint - currentPoint).Normalized();
			
			var angleDegrees = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(vec1.Dot(vec2), -1.0f, 1.0f)));
			
			// If angle is too sharp, adjust the current point
			if (angleDegrees < MinAngle)
			{
				// Move point to create a more gentle curve
				var bisector = (vec1 + vec2).Normalized();
				var adjustment = bisector * (MinPointDistance * 0.5f);
				result[i] = currentPoint + adjustment;
			}
			else if (angleDegrees > MaxAngle)
			{
				// Move point to create a sharper curve
				var bisector = (vec1 + vec2).Normalized();
				var adjustment = bisector * (MinPointDistance * -0.2f);
				result[i] = currentPoint + adjustment;
			}
		}

		return result;
	}

	private List<Vector2> EnsureSeamlessLoop(List<Vector2> points)
	{
		if (points.Count < 3)
			return points;

		var result = new List<Vector2>(points);
		
		// Calculate angles for the loop closure
		var lastPoint = result[result.Count - 1];
		var firstPoint = result[0];
		var secondPoint = result[1];
		var secondToLastPoint = result[result.Count - 2];
		
		// Calculate the angle from second-to-last to last point
		var incomingVector = (lastPoint - secondToLastPoint).Normalized();
		
		// Calculate the angle from first to second point
		var outgoingVector = (secondPoint - firstPoint).Normalized();
		
		// Calculate what the ideal last-to-first vector should be for smooth transition
		var idealVector = (incomingVector + outgoingVector).Normalized();
		
		// Adjust the distance between last and first point for smooth closure
		var currentDistance = lastPoint.DistanceTo(firstPoint);
		var idealDistance = Mathf.Max(MinPointDistance, currentDistance * 0.8f);
		
		// Calculate the ideal position for smooth loop closure
		var midpoint = (lastPoint + firstPoint) / 2;
		var adjustmentVector = idealVector * (idealDistance / 2);
		
		// Slightly adjust last point to create smoother angle transition
		var lastPointAdjustment = incomingVector.Orthogonal() * (idealDistance * 0.1f);
		result[result.Count - 1] = lastPoint + lastPointAdjustment;
		
		// Slightly adjust first point for better symmetry
		var firstPointAdjustment = outgoingVector.Orthogonal() * (idealDistance * 0.1f);
		result[0] = firstPoint - firstPointAdjustment;
		
		return result;
	}

	// Public API methods
	public Curve2D GetTrackCurve()
	{
		return _trackCurve;
	}

	public Vector2 GetStartPosition()
	{
		if (_trackPoints == null || _trackPoints.Length == 0)
			return Vector2.Zero;
		return _trackPoints[0];
	}

	public Vector2 GetStartDirection()
	{
		if (_trackPoints == null || _trackPoints.Length < 2)
			return Vector2.Right;
		return (_trackPoints[1] - _trackPoints[0]).Normalized();
	}

	public float GetTrackLength()
	{
		return _trackCurve?.GetBakedLength() ?? 0.0f;
	}

	public bool IsValidTrackPoint(Vector2 point)
	{
		if (_trackCurve == null) return false;
		return _trackCurve.GetClosestPoint(point).DistanceTo(point) <= TrackWidth;
	}

	public override void _Draw()
	{
		if (!Engine.IsEditorHint() || !ShowDebugPoints || _trackPoints == null)
			return;

		// Draw debug points
		for (int i = 0; i < _trackPoints.Length; i++)
		{
			DrawCircle(_trackPoints[i], 5.0f, Colors.Red);
			DrawString(ThemeDB.FallbackFont, _trackPoints[i] + Vector2.One * 10, i.ToString(), HorizontalAlignment.Left, -1, 12);
		}
	}
}