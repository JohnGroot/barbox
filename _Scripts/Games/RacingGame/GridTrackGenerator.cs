using Godot;
using System;
using System.Collections.Generic;


/*
Now that the @_Scripts/Games/RacingGame/GridTrackGenerator.cs is in a place I like, think about how we can write a script to parse the output into the track body and edges
use this reference image as the guide for the strategy of parsing the track data from the grid-based context:
- First use the grid based line of the track to draw the _inside_ edge of the track (shown in red in the image), using bezier curve logic (or a better alternative for the context you find via research)
to smooth corners and the stairstepped nature of straightaways in  the grid based context
- Then draw the outside edge the track width value's distance out from the inside edge, clamping to the screen margin as needed. This is shown in orange in the image
- Then draw a "racing line" edge in the center of the track using dotted line drawing (use these docs for context on all of this 2d drawing: https://github.com/godotengine/godot-docs/blob/master/tutorials/2d/custom_drawing_in_2d.rst )
- Then draw lines across the track to demarcate Checkpoints, shown as green dots in the image, which should be at the midpoint between each track point EXCEPT for the points by the "start" point (this can be seen in the image as well)
- Finally draw the "start line" across the track at the "start point" of the track generation
This track renderer system should be as encapsulated as possible, taking in an object with all necessary data and have it parse that track gen data into a new data structure that it utilizes in its _Draw method
 * 
 */

[Tool]
public partial class GridTrackGenerator : Node2D, ITrackGenerator
{
		[ExportCategory("Track Settings")]
		[Export(PropertyHint.Range, "5,50,1")] public int GridSize { get; set; } = 20;
		[Export(PropertyHint.Range, "3,16,1")] public int TrackPointCount { get; set; } = 8;
		[Export(PropertyHint.Range, "0.1,0.9,0.05")] public float MinDistanceFromCenter { get; set; } = 0.3f;
		[Export(PropertyHint.Range, "0.1,0.9,0.05")] public float MaxDistanceFromCenter { get; set; } = 0.8f;
		[Export(PropertyHint.Range, "1,5,1")] public int MinDistanceBetweenPoints { get; set; } = 2;
		[Export(PropertyHint.Range, "0,90,1,suffix:°")] public float AngleVariation { get; set; } = 30.0f;

		[ExportCategory("Generation Settings")]
		[Export(PropertyHint.Range, "1,50,1")] public int MaxRetries { get; set; } = 10;
		[Export(PropertyHint.Range, "0.00,0.25,0.005")] public float CenterVariationFactor { get; set; } = 0.125f;
		[Export(PropertyHint.Range, "1,5,1")] public int BoundaryPadding { get; set; } = 2;
		[Export(PropertyHint.Range, "3,8,1")] public int MinPointsForStraightaway { get; set; } = 4;
		[Export(PropertyHint.Range, "1,5,1")] public int StraightawayDistance { get; set; } = 3;
		[Export(PropertyHint.Range, "3,6,1")] public int MinValidTrackPoints { get; set; } = 3;

		
		[ExportCategory("Track Rendering")]
		[Export] public bool ShowTrackRenderer { get; set; } = true;
		[Export] public bool ShowDebugVisualization { get; set; } = false;
		[Export] public TrackRenderer TrackRenderer { get; private set; }

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
			InitializeTrackRenderer();
		}
		
		private void InitializeTrackRenderer()
		{
			if (TrackRenderer != null)
				return;

			TrackRenderer = new TrackRenderer();
			AddChild(TrackRenderer);
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
						
						// Step 8: Update track renderer with generated data
						UpdateTrackRenderer();
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
				const int maxRandomAttempts = 100;
				while (!foundValidPoint && attempts < maxRandomAttempts)
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

				if (foundValidPoint)
				{
					_trackGridPoints.Add(gridPoint);
					MarkGridPointOccupied(gridPoint);
				}
				else
				{
					// If no valid point found, use last attempted point as fallback
					GD.PrintErr($"Could not find valid placement for point {i}, using fallback");
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
		
		private void UpdateTrackRenderer()
		{
			// Ensure track renderer is initialized (for editor context)
			if (TrackRenderer == null)
			{
				InitializeTrackRenderer();
			}
			
			if (TrackRenderer != null && _trackGridPoints.Count > 0)
			{
				TrackRenderer.SetTrackGeneratorData(_trackGridPoints, _startGridPoint, _cellSize, _screenSize, _centerGridPoint);
			}
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
		
		private bool IsValidSegment(Vector2I start, Vector2I end)
		{
			var segmentPoints = DrawLine(start, end);
			
			// Check if segment passes through other track points (except endpoints)
			foreach (var point in segmentPoints)
			{
				if (_trackGridPoints.Contains(point))
				{
					if (point != start && point != end)
						return false;
				}
			}
			
			// Check if segment would overlap with existing path
			foreach (var point in segmentPoints)
			{
				if (_trackPathPoints.Contains(point))
				{
					if (point != start && point != end)
						return false;
				}
			}
			
			return true;
		}
		
		private bool IsPointConnectable(Vector2I newPoint)
		{
			if (_trackGridPoints.Count == 0)
				return true; // First point is always connectable
			
			// Check connection from last existing point to new point
			if (_trackGridPoints.Count > 0)
			{
				var lastPoint = _trackGridPoints[_trackGridPoints.Count - 1];
				if (!IsValidSegment(lastPoint, newPoint))
					return false;
			}
			
			// If this would be the last point, check if we can close the loop
			if (_trackGridPoints.Count == TrackPointCount - 1)
			{
				var firstPoint = _trackGridPoints[0];
				if (!IsValidSegment(newPoint, firstPoint))
					return false;
			}
			
			return true;
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
				
				// Check if segment is valid using simplified validation
				if (!IsValidSegment(startPoint, endPoint))
					return false;
				
				
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
			
			// Get the direction from start to next point
			var startPoint = _trackGridPoints[0];
			var nextPoint = _trackGridPoints[1];
			var direction = nextPoint - startPoint;
			
			// Normalize direction (simplified)
			if (direction.LengthSquared() < 1) return; // Avoid very short segments
			
			var length = direction.Length();
			var normalizedDir = new Vector2(direction.X / length, direction.Y / length);
			
			// Calculate straightaway points
			var straightDistance = StraightawayDistance;
			var prevIndex = _trackGridPoints.Count - 1;
			
			var newPrevPoint = new Vector2I(
				Mathf.Clamp(Mathf.RoundToInt(startPoint.X - normalizedDir.X * straightDistance), 
					BoundaryPadding, _gridSize.X - BoundaryPadding),
				Mathf.Clamp(Mathf.RoundToInt(startPoint.Y - normalizedDir.Y * straightDistance), 
					BoundaryPadding, _gridSize.Y - BoundaryPadding)
			);
			
			var newNextPoint = new Vector2I(
				Mathf.Clamp(Mathf.RoundToInt(startPoint.X + normalizedDir.X * straightDistance), 
					BoundaryPadding, _gridSize.X - BoundaryPadding),
				Mathf.Clamp(Mathf.RoundToInt(startPoint.Y + normalizedDir.Y * straightDistance), 
					BoundaryPadding, _gridSize.Y - BoundaryPadding)
			);
			
			// Apply straightaway if it doesn't break the track
			if (IsValidSegment(newPrevPoint, startPoint) && IsValidSegment(startPoint, newNextPoint))
			{
				_trackGridPoints[prevIndex] = newPrevPoint;
				_trackGridPoints[1] = newNextPoint;
			}
		}

		public override void _Draw()
		{
			if (ShowDebugVisualization)
			{
				DrawDebugVisualization();
			}
			
			// TrackRenderer handles its own drawing via its _Draw method
			// We just need to ensure it's visible
			if (TrackRenderer != null)
			{
				TrackRenderer.Visible = ShowTrackRenderer;
			}
		}

		private void DrawDebugVisualization()
		{
			// Draw grid
			var gridColor = Colors.Gray;
			gridColor.A = 0.2f;
			for (int x = 0; x <= _gridSize.X; x++)
			{
				DrawLine(new Vector2(x * _cellSize, 0), new Vector2(x * _cellSize, _gridSize.Y * _cellSize), gridColor);
			}
			for (int y = 0; y <= _gridSize.Y; y++)
			{
				DrawLine(new Vector2(0, y * _cellSize), new Vector2(_gridSize.X * _cellSize, y * _cellSize), gridColor);
			}

			if (_trackGridPoints.Count == 0) return;

			// Draw center point
			DrawCircle(GridToWorld(_centerGridPoint), 8, Colors.Red);

			// Draw lines from center to all track points
			var centerWorldPos = GridToWorld(_centerGridPoint);
			var radiusLineColor = Colors.Orange;
			radiusLineColor.A = 0.6f;
			for (int i = 0; i < _trackGridPoints.Count; i++)
			{
				var trackPointWorldPos = GridToWorld(_trackGridPoints[i]);
				DrawLine(centerWorldPos, trackPointWorldPos, radiusLineColor, 2);
			}

			// Draw track points
			for (int i = 0; i < _trackGridPoints.Count; i++)
			{
				var color = i == 0 ? Colors.Green : Colors.Blue;
				DrawCircle(GridToWorld(_trackGridPoints[i]), 6, color);
			}

			// Draw track path
			if (_trackPathPoints.Count > 1)
			{
				var pathColor = Colors.Yellow;
				pathColor.A = 0.8f;
				for (int i = 0; i < _trackPathPoints.Count - 1; i++)
				{
					DrawLine(GridToWorld(_trackPathPoints[i]), GridToWorld(_trackPathPoints[i + 1]), pathColor, 3);
				}
				// Close the loop
				if (_trackPathPoints.Count > 2)
				{
					DrawLine(GridToWorld(_trackPathPoints[_trackPathPoints.Count - 1]), GridToWorld(_trackPathPoints[0]), pathColor, 3);
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

	}