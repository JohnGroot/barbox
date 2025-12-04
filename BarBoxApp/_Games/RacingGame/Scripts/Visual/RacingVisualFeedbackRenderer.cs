using Godot;
using System.Collections.Generic;

namespace BarBox.Games.Racing
{
	/// <summary>
	/// Handles all visual feedback rendering for racing games including tire trails, input lines, and mouse indicators
	/// Extracted from RacingGame to follow single responsibility principle
	/// </summary>
	[GlobalClass]
	public partial class RacingVisualFeedbackRenderer : Node2D
{
	// ================================================================
	// EXPORT PROPERTIES - VISUAL SETTINGS
	// ================================================================
	
	[ExportCategory("Visual Feedback")]
	[Export] public Color InputLineActiveColor { get; set; } = Colors.White;
	[Export] public Color InputLineInactiveColor { get; set; } = Colors.Gray;
	[Export] public Color MouseMovingColor { get; set; } = Colors.Cyan;
	[Export] public Color MouseStationaryColor { get; set; } = Colors.LimeGreen;
	[Export] public Color TireTrailColor { get; set; } = Colors.DarkGray;
	[Export] public float InputLineWidth { get; set; } = 10.0f;
	[Export] public float MouseIndicatorRadius { get; set; } = 75.0f;
	[Export] public float TireTrailWidth { get; set; } = 5.0f;
	[Export] public int MaxTrailPoints { get; set; } = 1000;
	[Export] public float TrailUpdateDistance { get; set; } = 3.0f;
	[Export] public float TrailLifetime { get; set; } = 3000.0f;

	// ================================================================
	// PRIVATE FIELDS
	// ================================================================

	// Visual feedback tracking
	private List<(Vector2 position, ulong timestamp, Color zoneColor)> _leftTireTrail = [];
	private List<(Vector2 position, ulong timestamp, Color zoneColor)> _rightTireTrail = [];
	private Vector2 _lastTrailPosition = Vector2.Zero;
	private bool _wasInputActive = false;
	private Vector2 _lastMousePosition = Vector2.Zero;
	private float _mouseStationaryTime = 0.0f;
	private float _arcRotation = 0.0f;
	
	// Cached values for performance (restored from original implementation)
	private Vector2 _cachedDirectionToTarget = Vector2.Zero;
	private float _cachedMovementArcRotation = 0.0f;
	private float _cachedArcSweepAngle = 0.0f;
	private float _cachedCurrentSpeed = 0.0f;
	private bool _cachedValuesValid = false;

	// Dependencies
	private RacingCar _carController;

	// Direct target position override (for arc positioning fix)
	private Vector2 _directTargetOverride = Vector2.Zero;

	// ================================================================
	// PUBLIC PROPERTIES
	// ================================================================

	public bool ShouldRender { get; set; } = true;

	// ================================================================
	// INITIALIZATION
	// ================================================================

	/// <summary>
	/// Initialize the visual feedback renderer with car controller dependency
	/// </summary>
	/// <param name="carController">Car controller for position and state data</param>
	public void Initialize(RacingCar carController)
	{
		_carController = carController;
		ResetVisualState();
	}

	/// <summary>
	/// Update visual feedback settings from external configuration
	/// </summary>
	public void UpdateSettings(
		Color inputLineActiveColor,
		Color inputLineInactiveColor,
		Color mouseMovingColor,
		Color mouseStationaryColor,
		Color tireTrailColor,
		float inputLineWidth,
		float mouseIndicatorRadius,
		float tireTrailWidth,
		int maxTrailPoints,
		float trailUpdateDistance,
		float trailLifetime)
	{
		InputLineActiveColor = inputLineActiveColor;
		InputLineInactiveColor = inputLineInactiveColor;
		MouseMovingColor = mouseMovingColor;
		MouseStationaryColor = mouseStationaryColor;
		TireTrailColor = tireTrailColor;
		InputLineWidth = inputLineWidth;
		MouseIndicatorRadius = mouseIndicatorRadius;
		TireTrailWidth = tireTrailWidth;
		MaxTrailPoints = maxTrailPoints;
		TrailUpdateDistance = trailUpdateDistance;
		TrailLifetime = trailLifetime;
	}

	/// <summary>
	/// Reset all visual state (trails, positions, etc.)
	/// </summary>
	public void ResetVisualState()
	{
		_leftTireTrail.Clear();
		_rightTireTrail.Clear();
		_lastTrailPosition = Vector2.Zero;
		_wasInputActive = false;
		_lastMousePosition = Vector2.Zero;
		_mouseStationaryTime = 0.0f;
		_arcRotation = 0.0f;
		
		// Reset cached values
		_cachedDirectionToTarget = Vector2.Zero;
		_cachedMovementArcRotation = 0.0f;
		_cachedArcSweepAngle = 0.0f;
		_cachedCurrentSpeed = 0.0f;
		_cachedValuesValid = false;
	}

	// ================================================================
	// UPDATE METHODS
	// ================================================================

	/// <summary>
	/// Update visual feedback state and animations
	/// </summary>
	/// <param name="delta">Frame delta time</param>
	public void UpdateVisualFeedback(float delta)
	{
		if (_carController?.IsInitialized != true) return;

		var targetPosition = _carController.GetTargetPosition();
		var hasInput = _carController.HasInput();
		var carSpeed = _carController.GetCarSpeed(); // Cache for multiple uses

		// Track mouse movement state
		if (targetPosition != _lastMousePosition)
		{
			_mouseStationaryTime = 0.0f;
			_lastMousePosition = targetPosition;
			_cachedValuesValid = false; // Invalidate cache when target changes
		}
		else
		{
			_mouseStationaryTime += delta;
		}

		// Update arc rotation for moving indicator (purely visual animation)
		_arcRotation += delta * 3.0f;
		if (_arcRotation > Mathf.Pi * 2) _arcRotation -= Mathf.Pi * 2;

		// Update cached values for performance
		UpdateCachedValues();

		// Track input state change
		bool inputStateChanged = hasInput != _wasInputActive;
		if (inputStateChanged)
		{
			_wasInputActive = hasInput;
		}

		// Queue redraw only once per frame when visual state needs updating
		// Consolidates multiple QueueRedraw calls into single conditional check
		bool needsRedraw = inputStateChanged
			|| hasInput
			|| _mouseStationaryTime < 2.0f
			|| carSpeed > 1.0f;

		if (needsRedraw)
		{
			QueueRedraw();
		}
	}

	/// <summary>
	/// Update tire trail positions
	/// </summary>
	public void UpdateTireTrails()
	{
		if (_carController?.IsInitialized != true) 
			return;

		var currentTime = Time.GetTicksMsec();
		
		// Clean up expired trail points
		CleanupExpiredTrailPoints(_leftTireTrail, currentTime);
		CleanupExpiredTrailPoints(_rightTireTrail, currentTime);

		// Only create new trail points when moving at decent speed
		if (_carController.GetCarSpeed() < 5.0f) return;

		var carBody = _carController.GetCarBody();
		if (carBody == null) return;
		
		var carPosition = carBody.GlobalPosition;
		
		// Check if car has moved enough to add new trail points
		if (_lastTrailPosition.DistanceTo(carPosition) < TrailUpdateDistance) return;

		_cachedValuesValid = false;

		// Use cached tire positions from car controller for performance
		var leftTirePosition = _carController.LeftTirePosition;
		var rightTirePosition = _carController.RightTirePosition;

		// Get zone colors at each tire position
		var leftZoneColor = GetBlendedZoneColor(leftTirePosition);
		var rightZoneColor = GetBlendedZoneColor(rightTirePosition);

		// Add trail points for both tires with zone colors
		AddTrailPoint(_leftTireTrail, leftTirePosition, currentTime, leftZoneColor);
		AddTrailPoint(_rightTireTrail, rightTirePosition, currentTime, rightZoneColor);

		_lastTrailPosition = carPosition;
	}

	// ================================================================
	// TRAIL MANAGEMENT
	// ================================================================

	/// <summary>
	/// Add a new trail point to the specified trail
	/// </summary>
	private void AddTrailPoint(List<(Vector2 position, ulong timestamp, Color zoneColor)> trail, Vector2 point, ulong timestamp, Color zoneColor)
	{
		trail.Add((point, timestamp, zoneColor));

		// Limit trail length
		while (trail.Count > MaxTrailPoints)
		{
			trail.RemoveAt(0);
		}
	}

	/// <summary>
	/// Remove expired trail points from the specified trail
	/// </summary>
	private void CleanupExpiredTrailPoints(List<(Vector2 position, ulong timestamp, Color zoneColor)> trail, ulong currentTime)
	{
		if (trail == null) return;

		for (int i = trail.Count - 1; i >= 0; i--)
		{
			var age = currentTime - trail[i].timestamp;
			if (age > TrailLifetime)
			{
				trail.RemoveAt(i);
			}
			else
			{
				break; // Since trails are added chronologically, we can stop here
			}
		}
	}

	// ================================================================
	// RENDERING
	// ================================================================

	/// <summary>
	/// Main drawing method - renders all visual feedback elements
	/// </summary>
	public override void _Draw()
	{
		if (!ShouldRender || _carController?.IsInitialized != true) return;

		DrawInputLine();
		DrawMouseIndicators();
		DrawTireTrails();
	}

	/// <summary>
	/// Draw the input line from car to target position
	/// </summary>
	private void DrawInputLine()
	{
		if (_carController?.IsInitialized == false) return;
		
		var targetPosition = _carController.GetTargetPosition();
		var hasInput = _carController.HasInput();
		var carBody = _carController.GetCarBody();
		
		if (carBody == null || targetPosition == Vector2.Zero) return;
		
		// Use cached positions from car controller for performance
		var carFrontPosition = _carController.CarFrontPosition;
		var clampedTarget = _carController.ClampedTargetPosition;
		
		var inputLineColor = hasInput ? InputLineActiveColor : 
			(_carController.GetCarSpeed() > 1.0f ? InputLineInactiveColor : InputLineInactiveColor * new Color(1, 1, 1, 0.3f));

		DrawDashedLine(carFrontPosition, clampedTarget, inputLineColor, InputLineWidth, 10.0f, 5.0f);
	}

	/// <summary>
	/// Draw mouse/touch position indicators
	/// </summary>
	private void DrawMouseIndicators()
	{
		if (_carController?.IsInitialized == false) return;
		
		var targetPosition = _carController.GetTargetPosition();
		var hasInput = _carController.HasInput();

		if (targetPosition == Vector2.Zero || (!hasInput && _carController.GetCarSpeed() < 1.0f)) return;

		// Use cached clamped target position from car controller for performance
		var clampedTarget = _carController.ClampedTargetPosition;
		
		// Draw circle at clamped target position
		if (clampedTarget != Vector2.Zero)
		{
			DrawCircle(clampedTarget, MouseIndicatorRadius * 0.4f, MouseStationaryColor);
		}

		// Draw arc under finger/mouse position only if there's active input
		if (hasInput)
		{
			// Use cached values for consistent behavior (restored from original)
			if (!_cachedValuesValid) UpdateCachedValues();
			
			var arcColor = MouseMovingColor;
			
			// Speed-based arc width (thickness) - restored missing feature
			var baseWidth = 3.0f;
			var speedMultiplier = Mathf.Clamp(_cachedCurrentSpeed / _carController.MaxSpeed, 0.3f, 1.5f);
			var arcWidth = baseWidth * speedMultiplier;
			
			if (_cachedArcSweepAngle > 0.1f)
			{
				// Use movement-based rotation (separate from animated rotation)
				var startAngle = _cachedMovementArcRotation - (_cachedArcSweepAngle / 2.0f);
				var endAngle = _cachedMovementArcRotation + (_cachedArcSweepAngle / 2.0f);
				
				// Apply animated rotation overlay for visual polish
				startAngle += _arcRotation * 0.1f; // Reduced influence for subtlety
				endAngle += _arcRotation * 0.1f;

				DrawArc(targetPosition, MouseIndicatorRadius, startAngle, endAngle, 32, arcColor, arcWidth, true);
			}
		}
	}

	/// <summary>
	/// Draw tire trails for both left and right tires
	/// </summary>
	private void DrawTireTrails()
	{
		DrawTrailLine(_leftTireTrail);
		DrawTrailLine(_rightTireTrail);
	}

	/// <summary>
	/// Draw a single trail line with fading effect.
	/// Optimized to use DrawPolylineColors for batched rendering instead of individual DrawLine calls.
	/// </summary>
	/// <param name="trail">Trail points to draw</param>
	private void DrawTrailLine(List<(Vector2 position, ulong timestamp, Color zoneColor)> trail)
	{
		if (trail.Count < 2) return;

		var currentTime = Time.GetTicksMsec();
		var trailCount = trail.Count;

		// Build arrays for batched drawing - much faster than individual DrawLine calls
		var points = new Vector2[trailCount];
		var colors = new Color[trailCount];

		for (int i = 0; i < trailCount; i++)
		{
			points[i] = trail[i].position;
			var age = currentTime - trail[i].timestamp;
			var normalizedAge = Mathf.Clamp(age / TrailLifetime, 0.0f, 1.0f);
			var alpha = 1.0f - normalizedAge;
			colors[i] = trail[i].zoneColor * new Color(1, 1, 1, alpha * 0.8f);
		}

		// Single batched draw call instead of (trailCount-1) individual DrawLine calls
		DrawPolylineColors(points, colors, TireTrailWidth);
	}

	/// <summary>
	/// Draw a dashed line between two points
	/// </summary>
	/// <param name="from">Start position</param>
	/// <param name="to">End position</param>
	/// <param name="color">Line color</param>
	/// <param name="width">Line width</param>
	/// <param name="dashLength">Length of each dash</param>
	/// <param name="gapLength">Length of gaps between dashes</param>
	private void DrawDashedLine(Vector2 from, Vector2 to, Color color, float width, float dashLength, float gapLength)
	{
		var direction = (to - from).Normalized();
		var totalDistance = from.DistanceTo(to);
		var segmentLength = dashLength + gapLength;
		
		var currentPos = from;
		var distanceTraveled = 0.0f;
		
		while (distanceTraveled < totalDistance)
		{
			var remainingDistance = totalDistance - distanceTraveled;
			var actualSegmentLength = Mathf.Min(segmentLength, remainingDistance);
			var actualDashLength = Mathf.Min(dashLength, actualSegmentLength);
			
			var dashEnd = currentPos + direction * actualDashLength;
			DrawLine(currentPos, dashEnd, color, width);
			
			distanceTraveled += segmentLength;
			currentPos += direction * segmentLength;
		}
	}

	// ================================================================
	// CACHED VALUE MANAGEMENT (Performance Optimization)
	// ================================================================

	/// <summary>
	/// Update cached values for expensive calculations (restored from original)
	/// </summary>
	private void UpdateCachedValues()
	{
		if (_cachedValuesValid) return;
		
		if (!(_carController?.IsInitialized == true)) return;
		
		var carBody = _carController.GetCarBody();
		if (carBody == null) return;
		
		var targetPosition = _carController.GetTargetPosition();
		if (targetPosition == Vector2.Zero) return;
		
		// Cache direction to target for consistent calculations
		_cachedDirectionToTarget = (targetPosition - carBody.GlobalPosition).Normalized();
		
		// Cache movement-based arc rotation (separate from animated rotation)
		_cachedMovementArcRotation = _cachedDirectionToTarget.Angle();
		
		// Cache current speed for consistent calculations
		_cachedCurrentSpeed = _carController.GetCarSpeed();
		
		// Cache arc sweep angle based on speed (0 to Pi radians = 0 to 180 degrees)
		var normalizedSpeed = Mathf.Clamp(_cachedCurrentSpeed / _carController.MaxSpeed, 0.0f, 1.0f);
		_cachedArcSweepAngle = Mathf.Lerp(0.0f, Mathf.Pi, normalizedSpeed);
		
		_cachedValuesValid = true;
	}
	
	// ================================================================
	// UTILITY METHODS
	// ================================================================

	/// <summary>
	/// Get blended color from all zones at the given position
	/// </summary>
	private Color GetBlendedZoneColor(Vector2 position)
	{
		var zones = _carController?.GetZonesAtPosition(position);
		if (zones == null || zones.Count == 0)
			return TireTrailColor;

		// Average all zone colors
		Color blended = new Color(0, 0, 0, 0);
		foreach (var zone in zones)
		{
			blended += zone.ZoneColor;
		}
		return new Color(
			blended.R / zones.Count,
			blended.G / zones.Count,
			blended.B / zones.Count,
			Mathf.Max(0.8f, blended.A / zones.Count)
		);
	}

	/// <summary>
	/// Update car controller dependency
	/// </summary>
	/// <param name="carController">New car controller reference</param>
	public void UpdateCarController(RacingCar carController)
	{
		_carController = carController;
		_cachedValuesValid = false; // Invalidate cache when controller changes
	}

	/// <summary>
	/// Check if the renderer is properly initialized
	/// </summary>
	/// <returns>True if car controller is set and initialized</returns>
	public bool IsInitialized()
	{
		return _carController?.IsInitialized == true;
	}
}
}