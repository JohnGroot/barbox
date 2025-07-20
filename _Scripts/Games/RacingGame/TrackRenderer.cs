using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

[Tool]
public partial class TrackRenderer : Node2D
{
	[ExportCategory("Track Visual Settings")]
	[Export(PropertyHint.Range, "20,200,5")] public float TrackWidth { get; set; } = 80f;
	[Export] public Color InnerEdgeColor { get; set; } = Colors.Red;
	[Export] public Color OuterEdgeColor { get; set; } = Colors.Orange;
	[Export] public Color RacingLineColor { get; set; } = Colors.Yellow;
	[Export] public Color CheckpointColor { get; set; } = Colors.Green;
	[Export] public Color StartLineColor { get; set; } = Colors.White;
	
	[ExportCategory("Curve Settings")]
	[Export(PropertyHint.Range, "0.1,2.0,0.1")] public float CurveSmoothing { get; set; } = 0.5f;
	[Export(PropertyHint.Range, "10,100,5")] public int TessellationPoints { get; set; } = 50;
	
	[ExportCategory("Line Settings")]
	[Export(PropertyHint.Range, "2,10,1")] public float EdgeLineWidth { get; set; } = 4f;
	[Export(PropertyHint.Range, "2,8,1")] public float RacingLineWidth { get; set; } = 3f;
	[Export(PropertyHint.Range, "3,15,1")] public float StartLineWidth { get; set; } = 6f;
	[Export(PropertyHint.Range, "5,20,1")] public float DashLength { get; set; } = 10f;
	[Export(PropertyHint.Range, "3,15,1")] public float DashGap { get; set; } = 8f;
	
	[ExportCategory("Checkpoint Settings")]
	[Export(PropertyHint.Range, "5,20,1")] public float CheckpointRadius { get; set; } = 8f;
	[Export(PropertyHint.Range, "10,50,5")] public float CheckpointLineLength { get; set; } = 30f;
	[Export(PropertyHint.Range, "2,8,1")] public float CheckpointLineWidth { get; set; } = 3f;
	
	[ExportCategory("Smoothing Settings")]
	[Export(PropertyHint.Range, "0.5,3.0,0.1")] public float MinWidthFactor { get; set; } = 0.8f;
	[Export(PropertyHint.Range, "1.0,2.0,0.1")] public float MaxWidthFactor { get; set; } = 1.2f;
	[Export(PropertyHint.Range, "1,5,1")] public int SmoothingPasses { get; set; } = 3;
	[Export(PropertyHint.Range, "1,3,1")] public int CatmullRomPasses { get; set; } = 2;
	[Export(PropertyHint.Range, "1,3,1")] public int GaussianPasses { get; set; } = 2;
	[Export(PropertyHint.Range, "3,8,1")] public int DesiredCheckpointsMin { get; set; } = 3;
	[Export(PropertyHint.Range, "3,8,1")] public int DesiredCheckpointsMax { get; set; } = 5;
	
	[ExportCategory("Debug")]
	[Export] public bool ShowDebugInfo { get; set; } = false;
	[Export] public bool ShowOriginalPoints { get; set; } = false;
	
	private TrackData _trackData;
	private bool _isDataValid = false;
	
	// Performance optimization: Cache arrays to avoid repeated ToArray() calls
	private Vector2[] _cachedInnerEdgePoints;
	private Vector2[] _cachedOuterEdgePoints;
	private Rect2 _viewportBounds;
	
	public override void _Ready()
	{
		_trackData = new TrackData();
	}
	
	public void SetTrackGeneratorData(List<Vector2I> gridPoints, Vector2I startGridPoint, float cellSize, Vector2 screenSize)
	{
		if (gridPoints == null || gridPoints.Count < 3)
		{
			GD.PrintErr("Invalid track grid points provided to TrackRenderer");
			_isDataValid = false;
			return;
		}
		
		// Ensure _trackData is initialized
		_trackData ??= new TrackData();
		
		_trackData.Clear();
		_trackData.StartPoint = GridToWorld(startGridPoint, cellSize);
		_trackData.TrackWidth = TrackWidth;
		_trackData.CellSize = cellSize;
		_trackData.StartPointIndex = gridPoints.IndexOf(startGridPoint);
		
		// Convert grid points to world coordinates
		var worldPoints = new List<Vector2>();
		foreach (var gridPoint in gridPoints)
		{
			worldPoints.Add(GridToWorld(gridPoint, cellSize));
		}
		
		// Store original track points for checkpoint generation
		_trackData.OriginalTrackPoints = new List<Vector2>(worldPoints);
		
		// Generate track data
		GenerateSmoothTrackPoints(worldPoints);
		GenerateParallelEdges();
		GenerateRacingLine();
		GenerateCheckpoints();
		GenerateStartLine();
		
		_isDataValid = _trackData.IsValid;
		
		if (_isDataValid)
		{
			// Cache arrays for performance
			CacheDrawingArrays();
			QueueRedraw();
		}
		else
		{
			GD.PrintErr("Failed to generate valid track data");
		}
	}
	
	/// <summary>
	/// Converts grid coordinates to world coordinates, centering points within grid cells
	/// </summary>
	private Vector2 GridToWorld(Vector2I gridPoint, float cellSize)
	{
		var worldX = gridPoint.X * cellSize + cellSize / 2;
		var worldY = gridPoint.Y * cellSize + cellSize / 2;
		return new Vector2(worldX, worldY);
	}
	
	private void CacheDrawingArrays()
	{
		// Cache viewport bounds for culling
		var viewport = GetViewport();
		_viewportBounds = viewport?.GetVisibleRect() ?? new Rect2(0, 0, 1024, 768);
		
		// Cache arrays to avoid repeated ToArray() calls
		_cachedInnerEdgePoints = _trackData.InnerEdgePoints.ToArray();
		_cachedOuterEdgePoints = _trackData.OuterEdgePoints.ToArray();
	}
	
	private void GenerateSmoothTrackPoints(List<Vector2> worldPoints)
	{
		if (worldPoints.Count < 3) return;
		
		// Create a closed curve from the world points
		var curve = new Curve2D();
		
		// Add all points to the curve
		foreach (var point in worldPoints)
		{
			curve.AddPoint(point);
		}
		
		// Close the curve by adding the first point again
		curve.AddPoint(worldPoints[0]);
		
		// Apply smoothing by setting control points
		ApplyCurveSmoothing(curve);
		
		// Tessellate the curve to get smooth points
		var tessellatedPoints = TessellateCurve(curve, TessellationPoints);
		
		_trackData.SmoothTrackPoints = tessellatedPoints;
	}
	
	/// <summary>
	/// Sets tangent control points on curve to create smooth bezier interpolation
	/// </summary>
	private void ApplyCurveSmoothing(Curve2D curve)
	{
		int pointCount = curve.PointCount;
		if (pointCount < 3) return;
		
		for (int i = 0; i < pointCount; i++)
		{
			var prevIndex = (i - 1 + pointCount) % pointCount;
			var nextIndex = (i + 1) % pointCount;
			
			var prevPoint = curve.GetPointPosition(prevIndex);
			var currentPoint = curve.GetPointPosition(i);
			var nextPoint = curve.GetPointPosition(nextIndex);
			
			// Calculate tangent direction
			var tangent = (nextPoint - prevPoint).Normalized() * CurveSmoothing;
			
			// Set control points for smooth curves
			curve.SetPointIn(i, -tangent * 20f);
			curve.SetPointOut(i, tangent * 20f);
		}
	}
	
	/// <summary>
	/// Samples evenly-spaced points along the smooth curve to create tessellated geometry
	/// </summary>
	private List<Vector2> TessellateCurve(Curve2D curve, int segments)
	{
		var points = new List<Vector2>();
		var length = curve.GetBakedLength();
		
		for (int i = 0; i < segments; i++)
		{
			var t = (float)i / segments;
			var point = curve.SampleBaked(t * length);
			points.Add(point);
		}
		
		// Ensure the curve is properly closed by adding the first point as the last point
		if (points.Count > 0 && points[0] != points[points.Count - 1])
		{
			points.Add(points[0]);
		}
		
		return points;
	}
	
	private void GenerateParallelEdges()
	{
		if (_trackData.SmoothTrackPoints.Count < 3) return;
		
		var halfWidth = _trackData.TrackWidth / 2f;
		
		// Validate track width vs track size
		var trackBounds = GetTrackBounds(_trackData.SmoothTrackPoints);
		var maxDimension = Mathf.Max(trackBounds.Size.X, trackBounds.Size.Y);
		var validatedHalfWidth = Mathf.Min(halfWidth, maxDimension * 0.1f); // Max 10% of track size
		
		// Determine polygon winding order using smooth points
		bool isClockwise = IsPolygonClockwise(_trackData.SmoothTrackPoints);
		
		// Generate edges using smooth curve points with averaged normals
		var innerEdge = GenerateSmoothOffsetPolygon(_trackData.SmoothTrackPoints, validatedHalfWidth, true, isClockwise);
		var outerEdge = GenerateSmoothOffsetPolygon(_trackData.SmoothTrackPoints, validatedHalfWidth, false, isClockwise);
		
		// Apply enhanced smoothing to the edges
		innerEdge = ApplyEnhancedSmoothing(innerEdge);
		outerEdge = ApplyEnhancedSmoothing(outerEdge);
		
		_trackData.InnerEdgePoints = innerEdge;
		_trackData.OuterEdgePoints = outerEdge;
	}
	
	private List<Vector2> GenerateSmoothOffsetPolygon(List<Vector2> points, float offset, bool inward, bool isClockwise)
	{
		if (points.Count < 3) return new List<Vector2>();
		
		var offsetPoints = new List<Vector2>();
		
		for (int i = 0; i < points.Count; i++)
		{
			var current = points[i];
			
			// Calculate averaged normal vector from adjacent segments
			var avgNormal = CalculateAveragedNormal(points, i);
			
			// Determine offset direction based on winding order and inner/outer
			var offsetDirection = DetermineOffsetDirection(avgNormal, inward, isClockwise);
			
			// Apply offset with corner handling
			var offsetDistance = CalculateOffsetDistance(points, i, offset);
			var offsetPoint = current + offsetDirection * offsetDistance;
			
			offsetPoints.Add(offsetPoint);
		}
		
		// Ensure the offset polygon is properly closed
		if (offsetPoints.Count > 0 && offsetPoints[0] != offsetPoints[offsetPoints.Count - 1])
		{
			offsetPoints.Add(offsetPoints[0]);
		}
		
		return offsetPoints;
	}
	
	/// <summary>
	/// Computes the average perpendicular direction at a point by blending adjacent segment normals
	/// </summary>
	private Vector2 CalculateAveragedNormal(List<Vector2> points, int index)
	{
		var prevIndex = (index - 1 + points.Count) % points.Count;
		var nextIndex = (index + 1) % points.Count;
		
		var prevPoint = points[prevIndex];
		var currentPoint = points[index];
		var nextPoint = points[nextIndex];
		
		// Calculate normals for both adjacent segments
		var dir1 = (currentPoint - prevPoint).Normalized();
		var dir2 = (nextPoint - currentPoint).Normalized();
		
		var normal1 = new Vector2(-dir1.Y, dir1.X);
		var normal2 = new Vector2(-dir2.Y, dir2.X);
		
		// Average the normals and normalize
		var avgNormal = (normal1 + normal2).Normalized();
		
		// Handle the case where normals cancel out (straight line)
		if (avgNormal.LengthSquared() < 0.01f)
		{
			avgNormal = normal2; // Use the forward segment normal
		}
		
		return avgNormal;
	}
	
	/// <summary>
	/// Adjusts normal direction based on polygon winding order and desired edge type (inner/outer)
	/// </summary>
	private Vector2 DetermineOffsetDirection(Vector2 normal, bool inward, bool isClockwise)
	{
		var direction = normal;
		
		// For counter-clockwise polygons, normal points outward by default
		// For clockwise polygons, normal points inward by default
		// We want inner edges to point inward, outer edges to point outward
		
		if (isClockwise)
		{
			// For clockwise polygons, flip the normal to point outward
			direction = -direction;
		}
		
		if (inward)
		{
			// For inner edge, flip to point inward
			direction = -direction;
		}
		
		return direction;
	}
	
	/// <summary>
	/// Modulates track width at corners - reduces width for sharp turns, expands for gentle curves
	/// </summary>
	private float CalculateOffsetDistance(List<Vector2> points, int index, float baseOffset)
	{
		var prevIndex = (index - 1 + points.Count) % points.Count;
		var nextIndex = (index + 1) % points.Count;
		
		var prevPoint = points[prevIndex];
		var currentPoint = points[index];
		var nextPoint = points[nextIndex];
		
		// Calculate the angle between adjacent segments
		var dir1 = (currentPoint - prevPoint).Normalized();
		var dir2 = (nextPoint - currentPoint).Normalized();
		
		var dot = dir1.Dot(dir2);
		var angle = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f));
		
		// Enforce minimum track width - reduce corner offset reduction
		var minWidthFactor = MinWidthFactor; // Use exported parameter
		var maxWidthFactor = MaxWidthFactor; // Use exported parameter
		
		// For sharp corners, reduce the offset but enforce minimum width
		if (angle < Mathf.Pi * 0.5f) // Less than 90 degrees
		{
			var factor = Mathf.Lerp(minWidthFactor, 1f, angle / (Mathf.Pi * 0.5f));
			return baseOffset * factor;
		}
		// For very obtuse angles, slightly increase offset for smoother curves
		else if (angle > Mathf.Pi * 0.8f) // Greater than 144 degrees
		{
			var factor = Mathf.Lerp(1f, maxWidthFactor, (angle - Mathf.Pi * 0.8f) / (Mathf.Pi * 0.2f));
			return baseOffset * factor;
		}
		
		return baseOffset;
	}
	
	private List<Vector2> SmoothEdgePoints(List<Vector2> points)
	{
		if (points.Count < 3) return points;
		
		var smoothedPoints = new List<Vector2>(points);
		
		// Apply multiple smoothing passes for better results
		for (int pass = 0; pass < SmoothingPasses; pass++)
		{
			var tempPoints = new List<Vector2>();
			
			for (int i = 0; i < smoothedPoints.Count; i++)
			{
				var prevIndex = (i - 1 + smoothedPoints.Count) % smoothedPoints.Count;
				var nextIndex = (i + 1) % smoothedPoints.Count;
				
				var prevPoint = smoothedPoints[prevIndex];
				var currentPoint = smoothedPoints[i];
				var nextPoint = smoothedPoints[nextIndex];
				
				// Use weighted averaging for smoother results
				var smoothedPoint = (prevPoint * 0.25f + currentPoint * 0.5f + nextPoint * 0.25f);
				tempPoints.Add(smoothedPoint);
			}
			
			smoothedPoints = tempPoints;
		}
		
		// Ensure the smoothed polygon is still closed
		if (smoothedPoints.Count > 0 && smoothedPoints[0] != smoothedPoints[smoothedPoints.Count - 1])
		{
			smoothedPoints.Add(smoothedPoints[0]);
		}
		
		return smoothedPoints;
	}
	
	private List<Vector2> ApplyEnhancedSmoothing(List<Vector2> points)
	{
		if (points.Count < 3) return points;
		
		var smoothedPoints = new List<Vector2>(points);
		
		// Apply multiple smoothing techniques for ultra-smooth results
		
		// 1. Catmull-Rom inspired smoothing
		for (int pass = 0; pass < CatmullRomPasses; pass++)
		{
			var tempPoints = new List<Vector2>();
			
			for (int i = 0; i < smoothedPoints.Count; i++)
			{
				var prevIndex = (i - 1 + smoothedPoints.Count) % smoothedPoints.Count;
				var nextIndex = (i + 1) % smoothedPoints.Count;
				var nextNextIndex = (i + 2) % smoothedPoints.Count;
				
				var p0 = smoothedPoints[prevIndex];
				var p1 = smoothedPoints[i];
				var p2 = smoothedPoints[nextIndex];
				var p3 = smoothedPoints[nextNextIndex];
				
				// Catmull-Rom smoothing with tension control
				var smoothedPoint = p1 * 0.6f + (p0 + p2) * 0.15f + p3 * 0.1f;
				tempPoints.Add(smoothedPoint);
			}
			
			smoothedPoints = tempPoints;
		}
		
		// 2. Additional Gaussian-like smoothing
		for (int pass = 0; pass < GaussianPasses; pass++)
		{
			var tempPoints = new List<Vector2>();
			
			for (int i = 0; i < smoothedPoints.Count; i++)
			{
				var prevIndex = (i - 1 + smoothedPoints.Count) % smoothedPoints.Count;
				var nextIndex = (i + 1) % smoothedPoints.Count;
				
				var prevPoint = smoothedPoints[prevIndex];
				var currentPoint = smoothedPoints[i];
				var nextPoint = smoothedPoints[nextIndex];
				
				// Gaussian-like weighted averaging
				var smoothedPoint = (prevPoint * 0.2f + currentPoint * 0.6f + nextPoint * 0.2f);
				tempPoints.Add(smoothedPoint);
			}
			
			smoothedPoints = tempPoints;
		}
		
		// Ensure the smoothed polygon is still closed
		if (smoothedPoints.Count > 0 && smoothedPoints[0] != smoothedPoints[smoothedPoints.Count - 1])
		{
			smoothedPoints.Add(smoothedPoints[0]);
		}
		
		return smoothedPoints;
	}
	
	/// <summary>
	/// Determines polygon winding order using shoelace formula for signed area calculation
	/// </summary>
	private bool IsPolygonClockwise(List<Vector2> points)
	{
		if (points.Count < 3) return false;
		
		float signedArea = 0f;
		for (int i = 0; i < points.Count; i++)
		{
			var current = points[i];
			var next = points[(i + 1) % points.Count];
			signedArea += (next.X - current.X) * (next.Y + current.Y);
		}
		
		return signedArea > 0f;
	}
	
	/// <summary>
	/// Calculates axis-aligned bounding box encompassing all track points
	/// </summary>
	private Rect2 GetTrackBounds(List<Vector2> points)
	{
		if (points.Count == 0) return new Rect2();
		
		var minX = points[0].X;
		var maxX = points[0].X;
		var minY = points[0].Y;
		var maxY = points[0].Y;
		
		foreach (var point in points)
		{
			minX = Mathf.Min(minX, point.X);
			maxX = Mathf.Max(maxX, point.X);
			minY = Mathf.Min(minY, point.Y);
			maxY = Mathf.Max(maxY, point.Y);
		}
		
		return new Rect2(minX, minY, maxX - minX, maxY - minY);
	}
	
	
	
	
	private void GenerateRacingLine()
	{
		// Calculate racing line as the true center between processed inner and outer edges
		if (_trackData.InnerEdgePoints.Count == 0 || _trackData.OuterEdgePoints.Count == 0)
		{
			// Fallback to smooth track points if edges aren't available
			_trackData.RacingLinePoints = new List<Vector2>(_trackData.SmoothTrackPoints);
			return;
		}
		
		_trackData.RacingLinePoints = CalculateCenterLineBetweenEdges(_trackData.InnerEdgePoints, _trackData.OuterEdgePoints);
	}
	
	/// <summary>
	/// Generates racing line by interpolating midpoints between inner and outer track edges
	/// </summary>
	private List<Vector2> CalculateCenterLineBetweenEdges(List<Vector2> innerEdge, List<Vector2> outerEdge)
	{
		var centerLine = new List<Vector2>();
		
		// Handle case where edge point counts don't match
		var minCount = Mathf.Min(innerEdge.Count, outerEdge.Count);
		if (minCount == 0) return centerLine;
		
		// Calculate midpoint between corresponding inner and outer edge points
		for (int i = 0; i < minCount; i++)
		{
			var innerPoint = innerEdge[i];
			var outerPoint = outerEdge[i];
			
			// Calculate the midpoint between the two edge points
			var centerPoint = (innerPoint + outerPoint) / 2f;
			centerLine.Add(centerPoint);
		}
		
		// Ensure the racing line is properly closed
		if (centerLine.Count > 0 && centerLine[0] != centerLine[centerLine.Count - 1])
		{
			centerLine.Add(centerLine[0]);
		}
		
		return centerLine;
	}
	
	private void GenerateCheckpoints()
	{
		if (_trackData.OriginalTrackPoints.Count < 3) return;
		
		var checkpoints = new List<Vector2>();
		var checkpointLines = new List<Vector2[]>();
		var startIndex = _trackData.StartPointIndex;
		var halfWidth = _trackData.TrackWidth / 2f;
		
		// Calculate optimal checkpoint spacing - use exported parameters
		var totalPoints = _trackData.OriginalTrackPoints.Count;
		var desiredCheckpoints = Math.Max(DesiredCheckpointsMin, Math.Min(DesiredCheckpointsMax, totalPoints / 2));
		var checkpointSpacing = totalPoints / desiredCheckpoints;
		
		// Generate checkpoints at regular intervals, avoiding start area
		for (int i = 0; i < desiredCheckpoints; i++)
		{
			var checkpointIndex = (startIndex + (i + 1) * checkpointSpacing) % totalPoints;
			
			// Ensure we're not too close to the start
			var distanceFromStart = Math.Min(
				Math.Abs(checkpointIndex - startIndex),
				totalPoints - Math.Abs(checkpointIndex - startIndex)
			);
			
			if (distanceFromStart <= 2) continue;
			
			var current = _trackData.OriginalTrackPoints[checkpointIndex];
			var next = _trackData.OriginalTrackPoints[(checkpointIndex + 1) % totalPoints];
			
			// Checkpoint at midpoint between track points
			var checkpoint = (current + next) / 2f;
			checkpoints.Add(checkpoint);
			
			// Generate checkpoint line across the track with proper width
			var direction = (next - current).Normalized();
			var perpendicular = new Vector2(-direction.Y, direction.X);
			
			// Use consistent track width for checkpoint lines
			var lineStart = checkpoint + perpendicular * halfWidth;
			var lineEnd = checkpoint - perpendicular * halfWidth;
			
			checkpointLines.Add(new Vector2[] { lineStart, lineEnd });
		}
		
		_trackData.CheckpointPositions = checkpoints;
		_trackData.CheckpointLines = checkpointLines;
	}
	
	private void GenerateStartLine()
	{
		if (_trackData.OriginalTrackPoints.Count < 3) return;
		
		var startIndex = _trackData.StartPointIndex;
		if (startIndex >= _trackData.OriginalTrackPoints.Count) return;
		
		var startPoint = _trackData.OriginalTrackPoints[startIndex];
		var nextPoint = _trackData.OriginalTrackPoints[(startIndex + 1) % _trackData.OriginalTrackPoints.Count];
		
		// Calculate perpendicular direction for start line
		var direction = (nextPoint - startPoint).Normalized();
		var perpendicular = new Vector2(-direction.Y, direction.X);
		
		var halfWidth = _trackData.TrackWidth / 2f;
		var startLine1 = startPoint + perpendicular * halfWidth;
		var startLine2 = startPoint - perpendicular * halfWidth;
		
		_trackData.StartLinePoints = new List<Vector2> { startLine1, startLine2 };
	}
	
	public override void _Draw()
	{
		if (!_isDataValid) return;
		
		// Draw track edges
		DrawTrackEdges();
		
		// Draw racing line (dotted)
		DrawRacingLine();
		
		// Draw checkpoints
		DrawCheckpoints();
		
		// Draw start line
		DrawStartLine();
		
		// Draw debug info
		if (ShowDebugInfo)
		{
			DrawDebugInfo();
		}
	}
	
	private void DrawTrackEdges()
	{
		// Draw inner edge (red) - use cached array
		if (_cachedInnerEdgePoints is { Length: > 2 })
		{
			DrawPolyline(_cachedInnerEdgePoints, InnerEdgeColor, EdgeLineWidth, true);
		}
		
		// Draw outer edge (orange) - use cached array
		if (_cachedOuterEdgePoints is { Length: > 2 })
		{
			DrawPolyline(_cachedOuterEdgePoints, OuterEdgeColor, EdgeLineWidth, true);
		}
	}
	
	private void DrawRacingLine()
	{
		if (_trackData.RacingLinePoints.Count < 2) return;
		
		DrawDottedPolyline(_trackData.RacingLinePoints, RacingLineColor, RacingLineWidth, DashLength, DashGap);
	}
	
	private void DrawCheckpoints()
	{
		// Draw checkpoint lines across the track
		foreach (var checkpointLine in _trackData.CheckpointLines)
		{
			if (checkpointLine.Length == 2)
			{
				var start = checkpointLine[0];
				var end = checkpointLine[1];
				
				// Performance optimization: Skip checkpoints outside viewport bounds
				if (_viewportBounds.HasPoint(start) || _viewportBounds.HasPoint(end))
				{
					DrawLine(start, end, CheckpointColor, CheckpointLineWidth);
				}
			}
		}
	}
	
	private void DrawStartLine()
	{
		if (_trackData.StartLinePoints.Count == 2)
		{
			DrawLine(_trackData.StartLinePoints[0], _trackData.StartLinePoints[1], StartLineColor, StartLineWidth);
		}
	}
	
	private void DrawDottedPolyline(List<Vector2> points, Color color, float width, float dashLength, float gapLength)
	{
		if (points.Count < 2) return;
		
		for (int i = 0; i < points.Count; i++)
		{
			var start = points[i];
			var end = points[(i + 1) % points.Count];
			
			DrawDottedLine(start, end, color, width, dashLength, gapLength);
		}
	}
	
	private void DrawDottedLine(Vector2 start, Vector2 end, Color color, float width, float dashLength, float gapLength)
	{
		var totalLength = start.DistanceTo(end);
		
		// Performance optimization: Skip very short lines
		if (totalLength < dashLength * 0.5f)
		{
			DrawLine(start, end, color, width);
			return;
		}
		
		var direction = (end - start).Normalized();
		var dashStep = dashLength + gapLength;
		
		// Performance optimization: Calculate number of dashes upfront
		var numDashes = Mathf.CeilToInt(totalLength / dashStep);
		
		var currentPos = 0f;
		for (int i = 0; i < numDashes && currentPos < totalLength; i++)
		{
			var dashStart = start + direction * currentPos;
			var dashEnd = start + direction * Mathf.Min(currentPos + dashLength, totalLength);
			
			DrawLine(dashStart, dashEnd, color, width);
			currentPos += dashStep;
		}
	}
	
	private void DrawDebugInfo()
	{
		if (ShowOriginalPoints && _trackData.SmoothTrackPoints.Count > 0)
		{
			foreach (var point in _trackData.SmoothTrackPoints)
			{
				DrawCircle(point, 3f, Colors.Cyan);
			}
		}
	}
	
	public TrackData GetTrackData()
	{
		return _trackData;
	}
	
	public bool IsTrackDataValid()
	{
		return _isDataValid;
	}
	
	public void ClearTrackData()
	{
		_trackData?.Clear();
		_isDataValid = false;
		
		// Clear cached arrays for memory optimization
		_cachedInnerEdgePoints = null;
		_cachedOuterEdgePoints = null;
		
		QueueRedraw();
	}
}