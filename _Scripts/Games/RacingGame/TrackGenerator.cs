using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

[Tool]
public partial class TrackGenerator : Node2D
{
	[ExportCategory("Track Layout")]
	[Export] public int TurnCount { get; set; } = 6;
	[Export] public float MinTurnAngle { get; set; } = 45.0f;
	[Export] public float MaxTurnAngle { get; set; } = 135.0f;
	[Export] public int TurnSmoothness { get; set; } = 16;
	[Export] public float TrackRadius { get; set; } = 400.0f;
	[Export] public string TrackSeed { get; set; } = "";

	[ExportCategory("Track Geometry")]
	[Export] public float TrackWidth { get; set; } = 80.0f;
	[Export] public int SplineSegments { get; set; } = 100;

	[ExportCategory("Kerb Settings")]
	[Export] public float KerbAngleThreshold { get; set; } = 90.0f;
	[Export] public float KerbWidth { get; set; } = 12.0f;
	[Export] public float KerbOffset { get; set; } = 5.0f;
	[Export] public int KerbStripeCount { get; set; } = 8;
	[Export] public Color KerbColor1 { get; set; } = Colors.Red;
	[Export] public Color KerbColor2 { get; set; } = Colors.White;

	[ExportCategory("Visual")]
	[Export] public Color TrackColor { get; set; } = new Color(0.4f, 0.4f, 0.4f);
	[Export] public Color EdgeColor { get; set; } = Colors.White;
	[Export] public float EdgeWidth { get; set; } = 3.0f;
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
	private Vector2[] _innerEdgePoints;
	private Vector2[] _outerEdgePoints;
	private Curve2D _trackCurve;
	private Curve2D _innerEdgeCurve;
	private Curve2D _outerEdgeCurve;
	private Path2D _trackPath;
	private Line2D _trackVisual;
	private Line2D _innerEdgeVisual;
	private Line2D _outerEdgeVisual;
	private Node2D _debugPointsParent;
	private Node2D _kerbParent;

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

		// Generate turn-based control points
		var turnPoints = GenerateTurnPoints(center, screenSize);

		// Store the final track points
		_trackPoints = turnPoints;

		// Create the track curve
		CreateTrackCurve();

		// Update visuals
		UpdateVisuals();

		return _trackPoints;
	}

	private Vector2[] GenerateTurnPoints(Vector2 center, Vector2 screenSize)
	{
		var points = new List<Vector2>();
		var margin = 50.0f;
		
		// Create a base oval shape to distribute turns around
		var radiusX = Mathf.Min(TrackRadius, (screenSize.X - margin * 2) / 2);
		var radiusY = Mathf.Min(TrackRadius * 0.7f, (screenSize.Y - margin * 2) / 2);

		for (int i = 0; i < TurnCount; i++)
		{
			// Distribute turns evenly around an ellipse
			float baseAngle = (float)(i * 2 * Mathf.Pi / TurnCount);
			
			// Add some variation to the angle within constraints
			float angleVariation = (float)GD.RandRange(-0.3, 0.3);
			float angle = baseAngle + angleVariation;
			
			// Add radius variation for more interesting shapes
			float radiusVariationX = (float)GD.RandRange(0.8, 1.2);
			float radiusVariationY = (float)GD.RandRange(0.8, 1.2);
			
			var point = center + new Vector2(
				Mathf.Cos(angle) * radiusX * radiusVariationX,
				Mathf.Sin(angle) * radiusY * radiusVariationY
			);

			// Ensure point is within screen bounds
			point.X = Mathf.Clamp(point.X, margin, screenSize.X - margin);
			point.Y = Mathf.Clamp(point.Y, margin, screenSize.Y - margin);

			points.Add(point);
		}

		return points.ToArray();
	}


	private void CreateTrackCurve()
	{
		if (_trackPoints == null || _trackPoints.Length < 3)
		{
			GD.PrintErr("TrackGenerator: Not enough track points to create curve");
			return;
		}

		_trackCurve = new Curve2D();

		// Create smooth Bézier curves between control points
		// First pass: Add all points with properly calculated handles for closed loop
		for (int i = 0; i < _trackPoints.Length; i++)
		{
			var currentPoint = _trackPoints[i];
			var nextPoint = _trackPoints[(i + 1) % _trackPoints.Length];
			var prevPoint = _trackPoints[(i - 1 + _trackPoints.Length) % _trackPoints.Length];
			
			// Calculate control point handles for smooth curves
			// This properly handles the wraparound for closed loops
			var toNext = (nextPoint - currentPoint);
			var toPrev = (currentPoint - prevPoint);
			var tangent = (toNext.Normalized() + toPrev.Normalized()).Normalized();
			
			// Scale control handles based on distance and turn smoothness setting
			var baseHandleLength = Mathf.Min(toNext.Length(), toPrev.Length()) * 0.25f;
			var smoothnessFactor = (float)TurnSmoothness / 16.0f; // Normalize to reasonable range
			var handleLength = baseHandleLength * smoothnessFactor;
			
			var inHandle = -tangent * handleLength;
			var outHandle = tangent * handleLength;
			
			_trackCurve.AddPoint(currentPoint, inHandle, outHandle);
		}
		
		// Close the loop by adding the first point again with consistent handles
		// This ensures the curve forms a seamless closed loop
		var firstPoint = _trackPoints[0];
		
		// Use the same handle calculation as for the first point to ensure consistency
		var nextPointForClosure = _trackPoints[1];
		var prevPointForClosure = _trackPoints[_trackPoints.Length - 1];
		
		var toNextClosure = (nextPointForClosure - firstPoint);
		var toPrevClosure = (firstPoint - prevPointForClosure);
		var closureTangent = (toNextClosure.Normalized() + toPrevClosure.Normalized()).Normalized();
		
		var closureBaseLength = Mathf.Min(toNextClosure.Length(), toPrevClosure.Length()) * 0.25f;
		var closureSmoothness = (float)TurnSmoothness / 16.0f;
		var closureHandleLength = closureBaseLength * closureSmoothness;
		
		var closureInHandle = -closureTangent * closureHandleLength;
		var closureOutHandle = closureTangent * closureHandleLength;
		
		_trackCurve.AddPoint(firstPoint, closureInHandle, closureOutHandle);
		
		// Generate track edges
		GenerateTrackEdges();
	}

	private void GenerateTrackEdges()
	{
		if (_trackCurve == null)
			return;

		// Generate edge point arrays for API compatibility
		// (Visual edges are now generated dynamically in CreateSmoothEdgeVisual)
		var innerPoints = new List<Vector2>();
		var outerPoints = new List<Vector2>();
		
		var bakeLength = _trackCurve.GetBakedLength();
		var segments = SplineSegments;
		
		for (int i = 0; i <= segments; i++)
		{
			var offset = (float)i / segments * bakeLength;
			var centerPoint = _trackCurve.SampleBaked(offset);
			
			// Calculate perpendicular direction for track width
			var nextOffset = offset + 1.0f;
			if (nextOffset > bakeLength)
				nextOffset = 0.0f;
			
			var nextPoint = _trackCurve.SampleBaked(nextOffset);
			var direction = (nextPoint - centerPoint).Normalized();
			var perpendicular = new Vector2(-direction.Y, direction.X);
			
			// Create inner and outer edge points
			var halfWidth = TrackWidth * 0.5f;
			innerPoints.Add(centerPoint - perpendicular * halfWidth);
			outerPoints.Add(centerPoint + perpendicular * halfWidth);
		}
		
		// Close the edge loops by adding the first points at the end
		if (innerPoints.Count > 0)
		{
			innerPoints.Add(innerPoints[0]);
			outerPoints.Add(outerPoints[0]);
		}
		
		_innerEdgePoints = innerPoints.ToArray();
		_outerEdgePoints = outerPoints.ToArray();
		
		// Create edge curves for API access
		_innerEdgeCurve = new Curve2D();
		_outerEdgeCurve = new Curve2D();
		
		foreach (var point in _innerEdgePoints)
			_innerEdgeCurve.AddPoint(point);
		
		foreach (var point in _outerEdgePoints)
			_outerEdgeCurve.AddPoint(point);
			
		// Generate kerb markings for sharp turns
		GenerateKerbMarkings();
	}

	private void GenerateKerbMarkings()
	{
		if (_trackCurve == null || _innerEdgePoints == null)
			return;

		var kerbSections = new List<KerbSection>();
		var bakeLength = _trackCurve.GetBakedLength();
		
		// Analyze curve for sharp turns
		for (int i = 0; i < _trackPoints.Length; i++)
		{
			var prevIndex = (i - 1 + _trackPoints.Length) % _trackPoints.Length;
			var nextIndex = (i + 1) % _trackPoints.Length;
			
			var prevPoint = _trackPoints[prevIndex];
			var currentPoint = _trackPoints[i];
			var nextPoint = _trackPoints[nextIndex];
			
			// Calculate turn angle
			var vec1 = (currentPoint - prevPoint).Normalized();
			var vec2 = (nextPoint - currentPoint).Normalized();
			var turnAngle = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(vec1.Dot(vec2), -1.0f, 1.0f)));
			
			// If turn is sharp enough, create kerb section
			if (turnAngle < KerbAngleThreshold)
			{
				// Find the position on the curve for this control point
				var closestOffset = FindClosestOffsetOnCurve(currentPoint);
				var kerbStartOffset = Mathf.Max(0, closestOffset - KerbWidth * 0.5f);
				var kerbEndOffset = Mathf.Min(bakeLength, closestOffset + KerbWidth * 0.5f);
				
				kerbSections.Add(new KerbSection
				{
					StartOffset = kerbStartOffset,
					EndOffset = kerbEndOffset,
					IsInnerEdge = DetermineKerbSide(prevPoint, currentPoint, nextPoint)
				});
			}
		}
		
		_kerbSections = kerbSections;
	}

	private float FindClosestOffsetOnCurve(Vector2 point)
	{
		if (_trackCurve == null)
			return 0.0f;
			
		var bakeLength = _trackCurve.GetBakedLength();
		var bestOffset = 0.0f;
		var bestDistance = float.MaxValue;
		
		// Sample the curve to find closest point
		for (int i = 0; i <= 100; i++)
		{
			var offset = (float)i / 100 * bakeLength;
			var curvePoint = _trackCurve.SampleBaked(offset);
			var distance = point.DistanceSquaredTo(curvePoint);
			
			if (distance < bestDistance)
			{
				bestDistance = distance;
				bestOffset = offset;
			}
		}
		
		return bestOffset;
	}

	private bool DetermineKerbSide(Vector2 prevPoint, Vector2 currentPoint, Vector2 nextPoint)
	{
		// Calculate cross product to determine turn direction
		var vec1 = currentPoint - prevPoint;
		var vec2 = nextPoint - currentPoint;
		var cross = vec1.X * vec2.Y - vec1.Y * vec2.X;
		
		// Negative cross product means right turn, kerbs go on inside (left side)
		return cross < 0;
	}

	private struct KerbSection
	{
		public float StartOffset;
		public float EndOffset;
		public bool IsInnerEdge;
	}

	private List<KerbSection> _kerbSections;

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

			// Create track surface visual with overlap for seamless closure
			_trackVisual = new Line2D();
			_trackVisual.Width = TrackWidth;
			_trackVisual.DefaultColor = TrackColor;

			var bakeLength = _trackCurve.GetBakedLength();
			// Add 15% extra sampling beyond full curve for visual overlap
			var overlapFactor = 1.15f;
			var totalSamples = (int)(SplineSegments * overlapFactor);
			
			for (int i = 0; i <= totalSamples; i++)
			{
				var normalizedOffset = (float)i / SplineSegments; // Deliberately goes beyond 1.0
				var offset = normalizedOffset * bakeLength;
				
				// Handle wraparound for sampling beyond curve length
				if (offset > bakeLength)
					offset = offset - bakeLength;
					
				var point = _trackCurve.SampleBaked(offset);
				_trackVisual.AddPoint(point);
			}

			AddChild(_trackVisual);

			// Create edge visuals
			CreateEdgeVisuals();
			
			// Create kerb visuals
			CreateKerbVisuals();
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

	private void CreateEdgeVisuals()
	{
		// Create smooth edge visuals with overlap closure like the track surface
		if (_trackCurve != null)
		{
			CreateSmoothEdgeVisual(true);  // Inner edge
			CreateSmoothEdgeVisual(false); // Outer edge
		}
	}

	private void CreateSmoothEdgeVisual(bool isInnerEdge)
	{
		var edgeVisual = new Line2D();
		edgeVisual.Width = EdgeWidth;
		edgeVisual.DefaultColor = EdgeColor;

		var bakeLength = _trackCurve.GetBakedLength();
		// Use same overlap factor as track surface for consistency
		var overlapFactor = 1.15f;
		var totalSamples = (int)(SplineSegments * overlapFactor);

		for (int i = 0; i <= totalSamples; i++)
		{
			var normalizedOffset = (float)i / SplineSegments;
			var offset = normalizedOffset * bakeLength;

			// Handle wraparound for sampling beyond curve length
			if (offset > bakeLength)
				offset = offset - bakeLength;

			var centerPoint = _trackCurve.SampleBaked(offset);

			// Calculate perpendicular direction for track width
			var nextSampleOffset = offset + 1.0f;
			if (nextSampleOffset > bakeLength)
				nextSampleOffset = nextSampleOffset - bakeLength;

			var nextPoint = _trackCurve.SampleBaked(nextSampleOffset);
			var direction = (nextPoint - centerPoint).Normalized();
			var perpendicular = new Vector2(-direction.Y, direction.X);

			// Create edge point offset from center
			var halfWidth = TrackWidth * 0.5f;
			var edgeOffset = isInnerEdge ? -halfWidth : halfWidth;
			var edgePoint = centerPoint + perpendicular * edgeOffset;

			edgeVisual.AddPoint(edgePoint);
		}

		// Store the visuals for cleanup
		if (isInnerEdge)
			_innerEdgeVisual = edgeVisual;
		else
			_outerEdgeVisual = edgeVisual;

		AddChild(edgeVisual);
	}

	private void CreateKerbVisuals()
	{
		if (_kerbSections == null || _trackCurve == null)
			return;

		_kerbParent = new Node2D();
		_kerbParent.Name = "KerbMarkings";
		AddChild(_kerbParent);

		foreach (var kerbSection in _kerbSections)
		{
			CreateKerbStripes(kerbSection);
		}
	}

	private void CreateKerbStripes(KerbSection kerbSection)
	{
		var bakeLength = _trackCurve.GetBakedLength();
		var sectionLength = kerbSection.EndOffset - kerbSection.StartOffset;
		var stripeLength = sectionLength / KerbStripeCount;

		for (int i = 0; i < KerbStripeCount; i++)
		{
			var stripeStart = kerbSection.StartOffset + i * stripeLength;
			var stripeEnd = stripeStart + stripeLength;
			
			// Determine stripe color (alternating)
			var stripeColor = (i % 2 == 0) ? KerbColor1 : KerbColor2;
			
			// Create stripe visual
			var stripeLine = new Line2D();
			stripeLine.Width = KerbWidth;
			stripeLine.DefaultColor = stripeColor;
			
			// Sample points along the stripe
			var stripePoints = 8; // Number of points per stripe for smooth curves
			for (int j = 0; j <= stripePoints; j++)
			{
				var offset = stripeStart + (stripeEnd - stripeStart) * j / stripePoints;
				if (offset > bakeLength) offset -= bakeLength;
				
				var centerPoint = _trackCurve.SampleBaked(offset);
				
				// Get perpendicular direction for offset
				var nextOffset = offset + 1.0f;
				if (nextOffset > bakeLength) nextOffset -= bakeLength;
				var nextPoint = _trackCurve.SampleBaked(nextOffset);
				var direction = (nextPoint - centerPoint).Normalized();
				var perpendicular = new Vector2(-direction.Y, direction.X);
				
				// Offset to create kerb marking
				var kerbOffset = (TrackWidth * 0.5f) + KerbOffset;
				if (!kerbSection.IsInnerEdge) kerbOffset = -kerbOffset;
				
				var kerbPoint = centerPoint + perpendicular * kerbOffset;
				stripeLine.AddPoint(kerbPoint);
			}
			
			_kerbParent.AddChild(stripeLine);
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

		if (_innerEdgeVisual != null)
		{
			_innerEdgeVisual.QueueFree();
			_innerEdgeVisual = null;
		}

		if (_outerEdgeVisual != null)
		{
			_outerEdgeVisual.QueueFree();
			_outerEdgeVisual = null;
		}

		if (_kerbParent != null)
		{
			_kerbParent.QueueFree();
			_kerbParent = null;
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

	public Curve2D GetInnerEdgeCurve()
	{
		return _innerEdgeCurve;
	}

	public Curve2D GetOuterEdgeCurve()
	{
		return _outerEdgeCurve;
	}

	public Vector2[] GetInnerEdgePoints()
	{
		return _innerEdgePoints;
	}

	public Vector2[] GetOuterEdgePoints()
	{
		return _outerEdgePoints;
	}

	public bool HasKerbAtPosition(Vector2 position)
	{
		if (_kerbSections == null || _trackCurve == null)
			return false;

		var closestOffset = FindClosestOffsetOnCurve(position);
		
		foreach (var kerbSection in _kerbSections)
		{
			if (closestOffset >= kerbSection.StartOffset && closestOffset <= kerbSection.EndOffset)
				return true;
		}
		
		return false;
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