using Godot;
using System;
using System.Collections.Generic;

[Tool]
public partial class GridTrackGenerator : Node2D, ITrackGenerator
{
		[ExportCategory("Grid Settings")]
		
		/// <summary>Grid dimensions (square grid). Higher values create more detailed tracks.</summary>
		[Export(PropertyHint.Range, "5,50,1,or_greater")] public int GridSize { get; set; } = 20;
		
		/// <summary>Number of track points to generate. Range: 3-16 for best results.</summary>
		[Export(PropertyHint.Range, "3,16,1")] public int TrackPointCount { get; set; } = 8;
		
		[ExportGroup("Distance Settings")]
		/// <summary>Minimum distance from center as fraction of grid radius (0.1-0.9).</summary>
		[Export(PropertyHint.Range, "0.1,0.9,0.05")] public float MinDistanceFromCenter { get; set; } = 0.3f;
		
		/// <summary>Maximum distance from center as fraction of grid radius (0.1-0.9).</summary>
		[Export(PropertyHint.Range, "0.1,0.9,0.05")] public float MaxDistanceFromCenter { get; set; } = 0.8f;
		
		/// <summary>Minimum distance between track points in grid units (1-5).</summary>
		[Export(PropertyHint.Range, "1,5,1")] public int MinDistanceBetweenPoints { get; set; } = 2;
		
		[ExportGroup("Angle Settings")]
		/// <summary>Maximum angle variation in degrees for point placement (0-90).</summary>
		[Export(PropertyHint.Range, "0,90,1,suffix:°")] public float AngleVariation { get; set; } = 30.0f;

		[ExportCategory("Generation Settings")]
		
		/// <summary>Maximum generation attempts before failure (1-50).</summary>
		[Export(PropertyHint.Range, "1,50,1")] public int MaxRetries { get; set; } = 10;
		
		/// <summary>Center point variation as fraction of grid size (0.05-0.25).</summary>
		[Export(PropertyHint.Range, "0.05,0.25,0.005")] public float CenterVariationFactor { get; set; } = 0.125f; // 1/8
		
		/// <summary>Grid boundary padding in units (1-5).</summary>
		[Export(PropertyHint.Range, "1,5,1")] public int BoundaryPadding { get; set; } = 2;
		
		[ExportGroup("Search Parameters")]
		/// <summary>Angle increment for systematic point search in degrees (1-10).</summary>
		[Export(PropertyHint.Range, "1,10,0.5,suffix:°")] public float AngleStep { get; set; } = 5.0f;
		
		/// <summary>Distance increment for systematic point search (0.05-0.2).</summary>
		[Export(PropertyHint.Range, "0.05,0.2,0.005")] public float DistanceStep { get; set; } = 0.1f;
		
		/// <summary>Maximum random placement attempts per point (10-100).</summary>
		[Export(PropertyHint.Range, "10,100,5")] public int MaxRandomAttempts { get; set; } = 30;

		[ExportCategory("Straightaway Settings")]
		
		/// <summary>Minimum track points needed for straightaway generation (3-8).</summary>
		[Export(PropertyHint.Range, "3,8,1")] public int MinPointsForStraightaway { get; set; } = 4;
		
		/// <summary>Distance in grid units for straightaway segments (1-5).</summary>
		[Export(PropertyHint.Range, "1,5,1")] public int StraightawayDistance { get; set; } = 3;

		[ExportCategory("Validation Settings")]
		
		/// <summary>Minimum points required for valid track (3-6).</summary>
		[Export(PropertyHint.Range, "3,6,1")] public int MinValidTrackPoints { get; set; } = 3;

		[ExportCategory("Debug Display")]
		
		[ExportGroup("Debug Visibility")]
		/// <summary>Show debug grid overlay.</summary>
		[Export] public bool ShowDebugGrid { get; set; } = true;
		
		/// <summary>Show debug points and path visualization.</summary>
		[Export] public bool ShowDebugPoints { get; set; } = true;
		
		[ExportGroup("Debug Visuals")]
		/// <summary>Visual radius for center point (4-12).</summary>
		[Export(PropertyHint.Range, "4,12,1,suffix:px")] public int CenterPointRadius { get; set; } = 8;
		
		/// <summary>Visual radius for track points (3-10).</summary>
		[Export(PropertyHint.Range, "3,10,1,suffix:px")] public int TrackPointRadius { get; set; } = 6;
		
		/// <summary>Visual width for path lines (1-5).</summary>
		[Export(PropertyHint.Range, "1,5,1,suffix:px")] public int PathLineWidth { get; set; } = 3;
		
		[ExportCategory("Controls")]
		[ExportToolButton("Generate Track")]
		public Callable GenerateTrackButton => Callable.From(GenerateTrack);

		private Vector2I _gridSize;
		private Vector2I _centerGridPoint;
		private Vector2I _startGridPoint;
		private List<Vector2I> _trackGridPoints = new();
		private List<Vector2I> _trackPathPoints = new();
		private Curve2D _trackCurve;
		private Vector2 _screenSize;
		private float _cellSize;
		private bool[,] _occupiedGrid;

		public override void _Ready()
		{
			InitializeGrid();
		}

		private void InitializeGrid()
		{
			var viewport = GetViewport();
			if (viewport != null)
			{
				_screenSize = viewport.GetVisibleRect().Size;
			}
			else
			{
				// Fallback for editor context
				_screenSize = new Vector2(1024, 768);
			}
			
			_cellSize = Mathf.Min(_screenSize.X, _screenSize.Y) / GridSize;
			_gridSize = new Vector2I(GridSize, GridSize);
		}

		public void GenerateTrack()
		{
			// Ensure grid is initialized (for editor context)
			if (_gridSize == Vector2I.Zero)
			{
				InitializeGrid();
			}

			bool trackGenerated = false;
			
			for (int attempt = 0; attempt < MaxRetries && !trackGenerated; attempt++)
			{
				// Clear previous generation
				_trackGridPoints.Clear();
				_trackPathPoints.Clear();
				_trackCurve = null;
				
				// Initialize grid occupancy tracking
				_occupiedGrid = new bool[_gridSize.X, _gridSize.Y];

				// Step 1: Pick random center point near grid center
				_centerGridPoint = PickCenterPoint();

				// Step 2: Pick starting point with offset from center
				_startGridPoint = PickStartingPoint();

				// Step 3: Generate track points clockwise with variable distances
				GenerateTrackPoints();

				// Step 4: Ensure straightaway at start/finish before connecting paths
				EnsureStartStraightaway();

				// Step 5: Validate complete track before connecting
				if (ValidateCompleteTrack())
				{
					// Step 6: Connect points with simple line drawing
					if (ConnectTrackPoints())
					{
						// Step 7: Convert to smooth curve for gameplay
						ConvertToSmoothCurve();
						trackGenerated = true;
					}
				}
				// If validation or connection fails, retry the entire generation
			}
			
			// If we couldn't generate a valid track after all retries, generate a simple fallback
			if (!trackGenerated)
			{
				GD.PrintErr("Couldn't generate track!");
			}

			QueueRedraw();
		}

		private Vector2I PickCenterPoint()
		{
			var centerX = _gridSize.X / 2;
			var centerY = _gridSize.Y / 2;
			var variation = Mathf.RoundToInt(_gridSize.X * CenterVariationFactor);

			var randomX = GD.RandRange(centerX - variation, centerX + variation);
			var randomY = GD.RandRange(centerY - variation, centerY + variation);

			return new Vector2I(
				Mathf.Clamp(randomX, variation, _gridSize.X - variation),
				Mathf.Clamp(randomY, variation, _gridSize.Y - variation)
			);
		}

		private Vector2I PickStartingPoint()
		{
			var angle = GD.RandRange(0.0f, 360.0f);
			var distance = GD.RandRange(MinDistanceFromCenter, MaxDistanceFromCenter);
			var maxRadius = _gridSize.X / 2 - BoundaryPadding;

			var offsetX = Mathf.RoundToInt(Mathf.Cos(Mathf.DegToRad(angle)) * distance * maxRadius);
			var offsetY = Mathf.RoundToInt(Mathf.Sin(Mathf.DegToRad(angle)) * distance * maxRadius);

			return new Vector2I(
				Mathf.Clamp(_centerGridPoint.X + offsetX, BoundaryPadding, _gridSize.X - BoundaryPadding),
				Mathf.Clamp(_centerGridPoint.Y + offsetY, BoundaryPadding, _gridSize.Y - BoundaryPadding)
			);
		}

		private void GenerateTrackPoints()
		{
			_trackGridPoints.Add(_startGridPoint);
			MarkGridPointOccupied(_startGridPoint);

			var angleStep = 360.0f / TrackPointCount;
			var startAngle = GetAngleFromCenter(_startGridPoint);
			
			for (int i = 1; i < TrackPointCount; i++)
			{
				var targetAngle = startAngle + (angleStep * i);
				Vector2I gridPoint = Vector2I.Zero;
				bool foundValidPoint = false;
				
				// Try random selection first (like PickStartingPoint)
				int attempts = 0;
				while (!foundValidPoint && attempts < MaxRandomAttempts)
				{
					// Random angle around target angle (within variation)
					var angleVar = GD.RandRange(-AngleVariation, AngleVariation);
					var finalAngle = targetAngle + angleVar;

					// Random distance within valid range
					var distance = GD.RandRange(MinDistanceFromCenter, MaxDistanceFromCenter);
					var maxRadius = _gridSize.X / 2 - BoundaryPadding;

					var offsetX = Mathf.RoundToInt(Mathf.Cos(Mathf.DegToRad(finalAngle)) * distance * maxRadius);
					var offsetY = Mathf.RoundToInt(Mathf.Sin(Mathf.DegToRad(finalAngle)) * distance * maxRadius);

					gridPoint = new Vector2I(
						Mathf.Clamp(_centerGridPoint.X + offsetX, BoundaryPadding, _gridSize.X - BoundaryPadding),
						Mathf.Clamp(_centerGridPoint.Y + offsetY, BoundaryPadding, _gridSize.Y - BoundaryPadding)
					);

					// Validate the randomly selected point
					if (IsPointAtValidDistance(gridPoint) && !IsGridPointOccupied(gridPoint) && IsPointConnectable(gridPoint))
					{
						// Accept distance-valid points with relaxed proximity checks
						if (!IsPointTooCloseToExisting(gridPoint))
						{
							foundValidPoint = true;
						}
					}
					attempts++;
				}
				
				// If random approach fails, try systematic optimization as fallback
				if (!foundValidPoint)
				{
					for (float distance = MinDistanceFromCenter; distance <= MaxDistanceFromCenter && !foundValidPoint; distance += DistanceStep)
					{
						for (float angleVar = -AngleVariation; angleVar <= AngleVariation && !foundValidPoint; angleVar += AngleStep)
						{
							var finalAngle = targetAngle + angleVar;
							var maxRadius = _gridSize.X / 2 - BoundaryPadding;

							var offsetX = Mathf.RoundToInt(Mathf.Cos(Mathf.DegToRad(finalAngle)) * distance * maxRadius);
							var offsetY = Mathf.RoundToInt(Mathf.Sin(Mathf.DegToRad(finalAngle)) * distance * maxRadius);

							gridPoint = new Vector2I(
								Mathf.Clamp(_centerGridPoint.X + offsetX, BoundaryPadding, _gridSize.X - BoundaryPadding),
								Mathf.Clamp(_centerGridPoint.Y + offsetY, BoundaryPadding, _gridSize.Y - BoundaryPadding)
							);

							// Check if point meets distance requirements
							if (IsPointAtValidDistance(gridPoint) && !IsGridPointOccupied(gridPoint) && IsPointConnectable(gridPoint))
							{
								// Accept distance-valid points with relaxed proximity checks
								if (!IsPointTooCloseToExisting(gridPoint))
								{
									foundValidPoint = true;
								}
								// If point is too close but meets distance requirements, accept it as fallback
								else
								{
									foundValidPoint = true;
								}
							}
						}
					}
				}
				
				if (foundValidPoint)
				{
					_trackGridPoints.Add(gridPoint);
					MarkGridPointOccupied(gridPoint);
				}
				else
				{
					// Final fallback: try a wider search with more relaxed constraints
					bool fallbackFound = false;
					
					// Try a wider search for distance-valid points
					for (float distance = MinDistanceFromCenter; distance <= MaxDistanceFromCenter && !fallbackFound; distance += DistanceStep * 2)
					{
						for (float angleVar = -AngleVariation * 2; angleVar <= AngleVariation * 2 && !fallbackFound; angleVar += AngleStep * 2)
						{
							var finalAngle = targetAngle + angleVar;
							var maxRadius = _gridSize.X / 2 - BoundaryPadding;

							var offsetX = Mathf.RoundToInt(Mathf.Cos(Mathf.DegToRad(finalAngle)) * distance * maxRadius);
							var offsetY = Mathf.RoundToInt(Mathf.Sin(Mathf.DegToRad(finalAngle)) * distance * maxRadius);

							var fallbackPoint = new Vector2I(
								Mathf.Clamp(_centerGridPoint.X + offsetX, BoundaryPadding, _gridSize.X - BoundaryPadding),
								Mathf.Clamp(_centerGridPoint.Y + offsetY, BoundaryPadding, _gridSize.Y - BoundaryPadding)
							);

							// Use more relaxed validation for final fallback
							if (IsPointAtValidDistance(fallbackPoint) && !IsGridPointOccupied(fallbackPoint))
							{
								gridPoint = fallbackPoint;
								fallbackFound = true;
							}
						}
					}
					
					// If no valid fallback found, use the last attempted point
					_trackGridPoints.Add(gridPoint);
					MarkGridPointOccupied(gridPoint);
				}
			}
		}

		private float GetAngleFromCenter(Vector2I gridPoint)
		{
			var diff = gridPoint - _centerGridPoint;
			return Mathf.RadToDeg(Mathf.Atan2(diff.Y, diff.X));
		}

		private bool ConnectTrackPoints()
		{
			_trackPathPoints.Clear();

			// Connect all track points in sequence - points are already validated to be connectable
			for (int i = 0; i < _trackGridPoints.Count; i++)
			{
				var startPoint = _trackGridPoints[i];
				var endPoint = _trackGridPoints[(i + 1) % _trackGridPoints.Count];

				var linePoints = DrawLine(startPoint, endPoint);
				
				// Mark the path as occupied
				MarkPathOccupied(startPoint, endPoint);
				
				// Add points with simple duplicate avoidance
				for (int j = 0; j < linePoints.Count; j++)
				{
					var point = linePoints[j];
					
					// Skip the first point if it's the same as the last point we added
					if (j == 0 && _trackPathPoints.Count > 0 && _trackPathPoints[_trackPathPoints.Count - 1] == point)
						continue;
					
					_trackPathPoints.Add(point);
				}
			}
			
			return true; // All connections successful
		}

		private List<Vector2I> DrawLine(Vector2I start, Vector2I end)
		{
			var points = new List<Vector2I>();
			
			// Bresenham's line algorithm
			int dx = Mathf.Abs(end.X - start.X);
			int dy = Mathf.Abs(end.Y - start.Y);
			int sx = start.X < end.X ? 1 : -1;
			int sy = start.Y < end.Y ? 1 : -1;
			int err = dx - dy;

			var current = start;
			
			while (true)
			{
				points.Add(current);
				
				if (current.X == end.X && current.Y == end.Y)
					break;
					
				int e2 = 2 * err;
				if (e2 > -dy)
				{
					err -= dy;
					current.X += sx;
				}
				if (e2 < dx)
				{
					err += dx;
					current.Y += sy;
				}
			}
			
			return points;
		}

		private void ConvertToSmoothCurve()
		{
			_trackCurve = new Curve2D();

			foreach (var gridPoint in _trackGridPoints)
			{
				var worldPoint = GridToWorld(gridPoint);
				_trackCurve.AddPoint(worldPoint);
			}

			// Close the loop
			_trackCurve.AddPoint(GridToWorld(_trackGridPoints[0]));
		}

		private Vector2 GridToWorld(Vector2I gridPoint)
		{
			var worldX = gridPoint.X * _cellSize + _cellSize / 2;
			var worldY = gridPoint.Y * _cellSize + _cellSize / 2;
			return new Vector2(worldX, worldY);
		}
		
		private bool IsGridPointOccupied(Vector2I gridPoint)
		{
			if (gridPoint.X < 0 || gridPoint.X >= _gridSize.X || gridPoint.Y < 0 || gridPoint.Y >= _gridSize.Y)
				return true; // Out of bounds is considered occupied
			return _occupiedGrid[gridPoint.X, gridPoint.Y];
		}
		
		private void MarkGridPointOccupied(Vector2I gridPoint)
		{
			if (gridPoint.X >= 0 && gridPoint.X < _gridSize.X && gridPoint.Y >= 0 && gridPoint.Y < _gridSize.Y)
				_occupiedGrid[gridPoint.X, gridPoint.Y] = true;
		}
		
		private bool IsPathClear(Vector2I start, Vector2I end)
		{
			var linePoints = DrawLine(start, end);
			foreach (var point in linePoints)
			{
				if (IsGridPointOccupied(point))
				{
					// Allow paths to connect through track points themselves
					if (!_trackGridPoints.Contains(point))
						return false;
				}
			}
			return true;
		}
		
		private bool WouldSegmentOverlap(Vector2I start, Vector2I end)
		{
			// Check if segment passes through other track points
			if (DoesSegmentPassThroughOtherTrackPoints(start, end))
				return true;
			
			// Check for geometric intersection with existing track segments
			var newSegmentStart = Vector2IToVector2(start);
			var newSegmentEnd = Vector2IToVector2(end);
			
			// Check intersection with all existing track segments
			for (int i = 0; i < _trackGridPoints.Count - 1; i++)
			{
				var existingSegmentStart = Vector2IToVector2(_trackGridPoints[i]);
				var existingSegmentEnd = Vector2IToVector2(_trackGridPoints[i + 1]);
				
				// Check if segments intersect geometrically
				if (DoLineSegmentsIntersect(newSegmentStart, newSegmentEnd, existingSegmentStart, existingSegmentEnd))
				{
					// Allow intersection at endpoints (connections between segments)
					if (!(newSegmentStart == existingSegmentStart || newSegmentStart == existingSegmentEnd ||
						  newSegmentEnd == existingSegmentStart || newSegmentEnd == existingSegmentEnd))
					{
						return true;
					}
				}
			}
			
			var newSegmentPoints = DrawLine(start, end);
			
			// Check if any points in the new segment already exist in the track path
			foreach (var point in newSegmentPoints)
			{
				if (_trackPathPoints.Contains(point))
				{
					// Allow connection at designated start and end points only
					if (point != start && point != end)
						return true;
				}
			}
			
			return false;
		}
		
		private bool IsPointConnectable(Vector2I newPoint)
		{
			if (_trackGridPoints.Count == 0)
				return true; // First point is always connectable
			
			// Get all existing path points so far
			var existingPathPoints = new List<Vector2I>();
			
			// Build path from existing track points
			for (int i = 0; i < _trackGridPoints.Count - 1; i++)
			{
				var segmentPoints = DrawLine(_trackGridPoints[i], _trackGridPoints[i + 1]);
				foreach (var point in segmentPoints)
				{
					if (!existingPathPoints.Contains(point))
						existingPathPoints.Add(point);
				}
			}
			
			// Check connection from last existing point to new point
			if (_trackGridPoints.Count > 0)
			{
				var lastPoint = _trackGridPoints[_trackGridPoints.Count - 1];
				
				// Check if segment passes through other track points
				if (DoesSegmentPassThroughOtherTrackPoints(lastPoint, newPoint))
					return false;
				
				var connectionToNew = DrawLine(lastPoint, newPoint);
				
				// Check if this connection would overlap with existing path
				foreach (var point in connectionToNew)
				{
					if (existingPathPoints.Contains(point))
					{
						// Allow connection at designated start and end points only
						if (point != lastPoint && point != newPoint)
							return false;
					}
				}
			}
			
			// If this would be the last point, check if we can close the loop
			if (_trackGridPoints.Count == TrackPointCount - 1)
			{
				var firstPoint = _trackGridPoints[0];
				
				// Check if closing segment passes through other track points
				if (DoesSegmentPassThroughOtherTrackPoints(newPoint, firstPoint))
					return false;
				
				var closingConnection = DrawLine(newPoint, firstPoint);
				
				// Check if closing connection would overlap
				foreach (var point in closingConnection)
				{
					if (existingPathPoints.Contains(point))
					{
						// Allow connection at designated start and end points only
						if (point != newPoint && point != firstPoint)
							return false;
					}
				}
			}
			
			return true;
		}
		
		private bool DoesSegmentPassThroughOtherTrackPoints(Vector2I segmentStart, Vector2I segmentEnd)
		{
			var segmentPoints = DrawLine(segmentStart, segmentEnd);
			
			// Check if any points in the segment pass through track points other than start/end
			foreach (var point in segmentPoints)
			{
				if (_trackGridPoints.Contains(point))
				{
					// Only allow passing through the designated start and end points
					if (point != segmentStart && point != segmentEnd)
						return true;
				}
			}
			
			return false;
		}
		
		private bool ValidateCompleteTrack()
		{
			if (_trackGridPoints.Count < MinValidTrackPoints)
				return false;
			
			var allPathPoints = new List<Vector2I>();
			
			// Build complete path from all track points and check for geometric intersections
			for (int i = 0; i < _trackGridPoints.Count; i++)
			{
				var startPoint = _trackGridPoints[i];
				var endPoint = _trackGridPoints[(i + 1) % _trackGridPoints.Count];
				
				// Check if segment passes through other track points
				if (DoesSegmentPassThroughOtherTrackPoints(startPoint, endPoint))
					return false;
				
				// Check for geometric intersections with other track segments
				var segmentStart = Vector2IToVector2(startPoint);
				var segmentEnd = Vector2IToVector2(endPoint);
				
				for (int j = 0; j < _trackGridPoints.Count; j++)
				{
					// Skip adjacent segments (they're supposed to connect)
					if (j == i || j == (i + 1) % _trackGridPoints.Count || 
						(i == 0 && j == _trackGridPoints.Count - 1) || 
						(j == 0 && i == _trackGridPoints.Count - 1))
						continue;
					
					var otherStart = Vector2IToVector2(_trackGridPoints[j]);
					var otherEnd = Vector2IToVector2(_trackGridPoints[(j + 1) % _trackGridPoints.Count]);
					
					// Check if segments intersect geometrically
					if (DoLineSegmentsIntersect(segmentStart, segmentEnd, otherStart, otherEnd))
					{
						// Allow intersection only at endpoints (valid connections)
						if (!(segmentStart == otherStart || segmentStart == otherEnd ||
							  segmentEnd == otherStart || segmentEnd == otherEnd))
						{
							return false;
						}
					}
				}
				
				var segmentPoints = DrawLine(startPoint, endPoint);
				
				// Check for overlaps within this segment
				foreach (var point in segmentPoints)
				{
					if (allPathPoints.Contains(point))
					{
						// Allow overlap at track points themselves
						if (!_trackGridPoints.Contains(point))
							return false;
					}
					else
					{
						allPathPoints.Add(point);
					}
				}
			}
			
			return true;
		}
		
		private void MarkPathOccupied(Vector2I start, Vector2I end)
		{
			var linePoints = DrawLine(start, end);
			foreach (var point in linePoints)
			{
				MarkGridPointOccupied(point);
			}
		}
		
		private bool IsPointTooCloseToExisting(Vector2I newPoint)
		{
			foreach (var existingPoint in _trackGridPoints)
			{
				var distance = Mathf.Sqrt(
					(newPoint.X - existingPoint.X) * (newPoint.X - existingPoint.X) +
					(newPoint.Y - existingPoint.Y) * (newPoint.Y - existingPoint.Y)
				);
				
				if (distance < MinDistanceBetweenPoints)
					return true;
			}
			return false;
		}

		private bool IsPointAtValidDistance(Vector2I newPoint)
		{
			// Calculate distance from center point
			var diff = newPoint - _centerGridPoint;
			var distanceFromCenter = Mathf.Sqrt(diff.X * diff.X + diff.Y * diff.Y);
			
			// Convert to fraction of max radius
			var maxRadius = _gridSize.X / 2 - BoundaryPadding;
			var normalizedDistance = distanceFromCenter / maxRadius;
			
			return normalizedDistance >= MinDistanceFromCenter && normalizedDistance <= MaxDistanceFromCenter;
		}

		private void EnsureStartStraightaway()
		{
			if (_trackGridPoints.Count < MinPointsForStraightaway)
				return;
			
			var startIndex = 0;
			var pointCount = _trackGridPoints.Count;
			
			// Get the direction from start to next point
			var startPoint = _trackGridPoints[startIndex];
			var nextPoint = _trackGridPoints[1];
			var direction = (nextPoint - startPoint);
			
			// Normalize direction
			var length = Mathf.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
			if (length < 0.001f) return; // Avoid division by zero
			
			var normalizedDir = new Vector2(direction.X / length, direction.Y / length);
			
			// Create straight segments before/after start
			var straightDistance = StraightawayDistance;
			
			// Calculate proposed straightaway points
			var prevIndex = pointCount - 1;
			var straightPrevPoint = new Vector2I(
				Mathf.RoundToInt(startPoint.X - normalizedDir.X * straightDistance),
				Mathf.RoundToInt(startPoint.Y - normalizedDir.Y * straightDistance)
			);
			
			// Clamp to grid bounds
			straightPrevPoint = new Vector2I(
				Mathf.Clamp(straightPrevPoint.X, BoundaryPadding, _gridSize.X - BoundaryPadding),
				Mathf.Clamp(straightPrevPoint.Y, BoundaryPadding, _gridSize.Y - BoundaryPadding)
			);
			
			var straightNextPoint = new Vector2I(
				Mathf.RoundToInt(startPoint.X + normalizedDir.X * straightDistance),
				Mathf.RoundToInt(startPoint.Y + normalizedDir.Y * straightDistance)
			);
			
			// Clamp to grid bounds
			straightNextPoint = new Vector2I(
				Mathf.Clamp(straightNextPoint.X, BoundaryPadding, _gridSize.X - BoundaryPadding),
				Mathf.Clamp(straightNextPoint.Y, BoundaryPadding, _gridSize.Y - BoundaryPadding)
			);
			
			// Store original points for potential rollback
			var originalPrevPoint = _trackGridPoints[prevIndex];
			var originalNextPoint = _trackGridPoints[1];
			
			// Temporarily update the track points
			_trackGridPoints[prevIndex] = straightPrevPoint;
			_trackGridPoints[1] = straightNextPoint;
			
			// Check if the modified connections would create intersections
			bool wouldCreateIntersection = false;
			
			// Check connection from point before prev to new prev point
			if (prevIndex > 0)
			{
				var prevPrevPoint = _trackGridPoints[prevIndex - 1];
				if (WouldSegmentOverlap(prevPrevPoint, straightPrevPoint))
					wouldCreateIntersection = true;
			}
			
			// Check connection from new prev point to start
			if (!wouldCreateIntersection && WouldSegmentOverlap(straightPrevPoint, startPoint))
				wouldCreateIntersection = true;
			
			// Check connection from start to new next point
			if (!wouldCreateIntersection && WouldSegmentOverlap(startPoint, straightNextPoint))
				wouldCreateIntersection = true;
			
			// Check connection from new next point to the point after
			if (!wouldCreateIntersection && _trackGridPoints.Count > 2)
			{
				var nextNextPoint = _trackGridPoints[2];
				if (WouldSegmentOverlap(straightNextPoint, nextNextPoint))
					wouldCreateIntersection = true;
			}
			
			// If modifications would create intersections, rollback to original points
			if (wouldCreateIntersection)
			{
				_trackGridPoints[prevIndex] = originalPrevPoint;
				_trackGridPoints[1] = originalNextPoint;
			}
			// Otherwise, keep the straightaway modifications
		}

		public override void _Draw()
		{
			if (!ShowDebugGrid && !ShowDebugPoints)
				return;

			if (ShowDebugGrid)
				DrawDebugGrid();

			if (ShowDebugPoints)
				DrawDebugPoints();
		}

		private void DrawDebugGrid()
		{
			var gridColor = Colors.Gray;
			gridColor.A = 0.3f;

			// Draw vertical lines
			for (int x = 0; x <= _gridSize.X; x++)
			{
				var start = new Vector2(x * _cellSize, 0);
				var end = new Vector2(x * _cellSize, _gridSize.Y * _cellSize);
				DrawLine(start, end, gridColor);
			}

			// Draw horizontal lines
			for (int y = 0; y <= _gridSize.Y; y++)
			{
				var start = new Vector2(0, y * _cellSize);
				var end = new Vector2(_gridSize.X * _cellSize, y * _cellSize);
				DrawLine(start, end, gridColor);
			}
		}

		private void DrawDebugPoints()
		{
			if (_trackGridPoints.Count == 0)
				return;

			// Draw center point
			var centerWorld = GridToWorld(_centerGridPoint);
			DrawCircle(centerWorld, CenterPointRadius, Colors.Red);

			// Draw track points
			for (int i = 0; i < _trackGridPoints.Count; i++)
			{
				var worldPoint = GridToWorld(_trackGridPoints[i]);
				var color = i == 0 ? Colors.Green : Colors.Blue;
				DrawCircle(worldPoint, TrackPointRadius, color);
			}

			// Draw path
			if (_trackPathPoints.Count > 0)
			{
				var pathColor = Colors.Yellow;
				pathColor.A = 0.7f;
				
				for (int i = 0; i < _trackPathPoints.Count - 1; i++)
				{
					var start = GridToWorld(_trackPathPoints[i]);
					var end = GridToWorld(_trackPathPoints[i + 1]);
					DrawLine(start, end, pathColor, PathLineWidth);
				}
				
				// Draw connection from last point back to first to complete the loop
				if (_trackPathPoints.Count > 2)
				{
					var lastPoint = GridToWorld(_trackPathPoints[_trackPathPoints.Count - 1]);
					var firstPoint = GridToWorld(_trackPathPoints[0]);
					DrawLine(lastPoint, firstPoint, pathColor, PathLineWidth);
				}
			}
		}

		public Curve2D GetTrackCurve()
		{
			return _trackCurve;
		}

		public bool IsValidTrackPoint(Vector2 point)
		{
			// Convert world point to grid coordinates
			var gridX = Mathf.RoundToInt(point.X / _cellSize);
			var gridY = Mathf.RoundToInt(point.Y / _cellSize);
			
			// Check if within grid bounds
			return gridX >= 0 && gridX < _gridSize.X && gridY >= 0 && gridY < _gridSize.Y;
		}

		/// <summary>
		/// Helper method to convert Vector2I to Vector2 for geometric calculations
		/// </summary>
		private Vector2 Vector2IToVector2(Vector2I point)
		{
			return new Vector2(point.X, point.Y);
		}

		/// <summary>
		/// Calculate the cross product of two 2D vectors
		/// </summary>
		private float CrossProduct2D(Vector2 a, Vector2 b)
		{
			return a.X * b.Y - a.Y * b.X;
		}

		/// <summary>
		/// Check if two line segments intersect geometrically (not just discrete points)
		/// </summary>
		private bool DoLineSegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
		{
			// Direction vectors
			var d1 = p2 - p1;
			var d2 = p4 - p3;
			
			// Calculate cross products
			var cross = CrossProduct2D(d1, d2);
			
			// If cross product is zero, lines are parallel
			if (Mathf.Abs(cross) < 1e-10f)
				return false;
			
			// Calculate parameters for intersection point
			var t = CrossProduct2D(p3 - p1, d2) / cross;
			var u = CrossProduct2D(p3 - p1, d1) / cross;
			
			// Check if intersection point lies within both line segments
			return t >= 0 && t <= 1 && u >= 0 && u <= 1;
		}
	}