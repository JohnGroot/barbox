using System;
using System.Collections.Generic;
using BarBox.Core.Drawing;
using Godot;

namespace BarBox.Games.Racing;

/// <summary>
/// Handles all visual feedback rendering for racing games including tire trails, input lines, and mouse indicators
/// Extracted from RacingGame to follow single responsibility principle
/// </summary>
[GlobalClass]
public partial class RacingVisualFeedbackRenderer : ShapeCanvas
{
	// ================================================================
	// EXPORT PROPERTIES - VISUAL SETTINGS
	// ================================================================
	[ExportCategory("Visual Feedback")]
	[Export]
	public Color InputLineActiveColor { get; set; } = Palette.White;

	[Export]
	public Color InputLineInactiveColor { get; set; } = Palette.EdgeGray;

	[Export]
	public Color MouseMovingColor { get; set; } = Palette.Cyan;

	[Export]
	public Color MouseStationaryColor { get; set; } = Palette.Green;

	[Export]
	public Color TireTrailColor { get; set; } = Palette.EdgeGray;

	[Export]
	public float InputLineWidth { get; set; } = 10.0f;

	[Export]
	public float MouseIndicatorRadius { get; set; } = 75.0f;

	[Export]
	public float TireTrailWidth { get; set; } = 5.0f;

	[Export]
	public int MaxTrailPoints { get; set; } = 1000;

	[Export]
	public float TrailUpdateDistance { get; set; } = 3.0f;

	[Export]
	public float TrailLifetime { get; set; } = 3000.0f;

	// ================================================================
	// PRIVATE FIELDS
	// ================================================================
	private const int TrailStopCount = 8;

	private static readonly float[] InputLineDashPattern = [10f, 5f];

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

	// True when this frame's state warrants pushing geometry to the retained shapes below —
	// computed once in UpdateVisualFeedback and read by UpdateTireTrails, so the two externally
	// invoked methods (RacingGame calls them in sequence) share one gating decision instead of
	// each rolling its own.
	private bool _needsRedraw;

	// Retained shapes, committed once and mutated per push. All Dynamic(): every element here
	// tracks a moving car/finger position and changes on essentially every active-play frame, so
	// there is no unrelated static geometry in this canvas for the bucket split to protect.
	private Shape _leftTrailShape;
	private Shape _rightTrailShape;
	private Shape _inputLineShape;
	private Shape _touchStationaryShape;
	private Shape _touchArcShape;

	// Pooled per-trail color-stop scratch, reused every frame — Shape.SetStroke's CopyStops only
	// reallocates when the array length changes, and it never does here.
	private readonly ColorStop[] _leftTrailStops = new ColorStop[TrailStopCount];
	private readonly ColorStop[] _rightTrailStops = new ColorStop[TrailStopCount];

	// Pre-allocated buffer for trail rendering (eliminates per-frame GC allocations)
	private Vector2[] _trailPointsBuffer;
	private int _trailBufferCapacity = 0;

	// Zone color caching (reduces zone iteration overhead)
	private Color _cachedLeftTireZoneColor;
	private Color _cachedRightTireZoneColor;
	private Vector2 _lastZoneCheckPosition = Vector2.Zero;
	private const float ZONE_CHECK_DISTANCE_SQ = 400f; // 20 pixels squared

	// Dependencies
	private RacingCar _carController;

	// ================================================================
	// PUBLIC PROPERTIES
	// ================================================================
	public bool ShouldRender
	{
		get => Visible;
		set => Visible = value;
	}

	// ================================================================
	// INITIALIZATION
	// ================================================================
	public void Initialize(RacingCar carController)
	{
		_carController = carController;
		ResetVisualState();
	}

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

		// Reset zone color cache
		_cachedLeftTireZoneColor = TireTrailColor;
		_cachedRightTireZoneColor = TireTrailColor;
		_lastZoneCheckPosition = Vector2.Zero;
	}

	// ================================================================
	// SHAPE SETUP
	// ================================================================
	private void EnsureShapesCommitted()
	{
		if (_inputLineShape != null)
		{
			return;
		}

		_leftTrailShape = Build().Polyline([Vector2.Zero, Vector2.Zero]).Dynamic().Hidden()
			.Stroke(new StrokeStyle { Width = TireTrailWidth, Color = TireTrailColor, Cap = CapMode.Round })
			.Commit();

		_rightTrailShape = Build().Polyline([Vector2.Zero, Vector2.Zero]).Dynamic().Hidden()
			.Stroke(new StrokeStyle { Width = TireTrailWidth, Color = TireTrailColor, Cap = CapMode.Round })
			.Commit();

		_inputLineShape = Build().Polyline([Vector2.Zero, Vector2.Zero]).Dynamic().Hidden()
			.Stroke(new StrokeStyle { Width = InputLineWidth, Color = InputLineActiveColor, DashPattern = InputLineDashPattern, Cap = CapMode.Butt })
			.Commit();

		_touchStationaryShape = Build().Circle(Vector2.Zero, MouseIndicatorRadius * 0.4f).Dynamic().Hidden()
			.Fill(MouseStationaryColor)
			.Commit();

		_touchArcShape = Build().Arc(Vector2.Zero, MouseIndicatorRadius, 0f, 0.01f).Dynamic().Hidden()
			.Stroke(new StrokeStyle { Width = 3f, Color = MouseMovingColor })
			.Commit();
	}

	// ================================================================
	// UPDATE METHODS
	// ================================================================
	public void UpdateVisualFeedback(float delta)
	{
		if (_carController?.IsInitialized != true)
		{
			return;
		}

		EnsureShapesCommitted();

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
		if (_arcRotation > Mathf.Pi * 2)
		{
			_arcRotation -= Mathf.Pi * 2;
		}

		// Update cached values for performance
		UpdateCachedValues();

		// Track input state change
		bool inputStateChanged = hasInput != _wasInputActive;
		if (inputStateChanged)
		{
			_wasInputActive = hasInput;
		}

		// Gates this frame's shape pushes (here and in UpdateTireTrails, called right after this by
		// RacingGame) — one gate shared across both externally invoked methods, not two.
		_needsRedraw = inputStateChanged
			|| hasInput
			|| _mouseStationaryTime < 2.0f
			|| carSpeed > 1.0f;

		if (_needsRedraw)
		{
			PushInputLine();
			PushMouseIndicators();
		}
	}

	/// <summary>
	/// Update tire trail positions
	/// </summary>
	public void UpdateTireTrails()
	{
		if (_carController?.IsInitialized != true)
		{
			return;
		}

		var currentTime = Time.GetTicksMsec();

		// Clean up expired trail points
		CleanupExpiredTrailPoints(_leftTireTrail, currentTime);
		CleanupExpiredTrailPoints(_rightTireTrail, currentTime);

		// Only create new trail points when moving at decent speed
		if (_carController.GetCarSpeed() >= 5.0f)
		{
			var carBody = _carController.GetCarBody();
			if (carBody != null)
			{
				var carPosition = carBody.GlobalPosition;

				// Check if car has moved enough to add new trail points
				if (_lastTrailPosition.DistanceTo(carPosition) >= TrailUpdateDistance)
				{
					_cachedValuesValid = false;

					// Use cached tire positions from car controller for performance
					var leftTirePosition = _carController.LeftTirePosition;
					var rightTirePosition = _carController.RightTirePosition;

					// Update zone colors only when car moves significantly (reduces zone iteration overhead)
					if (carPosition.DistanceSquaredTo(_lastZoneCheckPosition) >= ZONE_CHECK_DISTANCE_SQ)
					{
						_cachedLeftTireZoneColor = GetBlendedZoneColor(leftTirePosition);
						_cachedRightTireZoneColor = GetBlendedZoneColor(rightTirePosition);
						_lastZoneCheckPosition = carPosition;
					}

					// Add trail points for both tires with cached zone colors
					AddTrailPoint(_leftTireTrail, leftTirePosition, currentTime, _cachedLeftTireZoneColor);
					AddTrailPoint(_rightTireTrail, rightTirePosition, currentTime, _cachedRightTireZoneColor);

					_lastTrailPosition = carPosition;
				}
			}
		}

		if (_needsRedraw)
		{
			PushTrailLine(_leftTireTrail, _leftTrailShape, _leftTrailStops);
			PushTrailLine(_rightTireTrail, _rightTrailShape, _rightTrailStops);
		}
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
		if (trail == null)
		{
			return;
		}

		// Trails are added chronologically (oldest first), so scan from the front
		// and stop at the first non-expired point.
		int expiredCount = 0;
		while (expiredCount < trail.Count && currentTime - trail[expiredCount].timestamp > TrailLifetime)
		{
			expiredCount++;
		}

		if (expiredCount > 0)
		{
			trail.RemoveRange(0, expiredCount);
		}
	}

	// ================================================================
	// SHAPE PUSHES
	// ================================================================

	/// <summary>
	/// Push the input line from car to target position to its retained shape
	/// </summary>
	private void PushInputLine()
	{
		var targetPosition = _carController.GetTargetPosition();
		var hasInput = _carController.HasInput();
		var carBody = _carController.GetCarBody();

		if (carBody == null || targetPosition == Vector2.Zero)
		{
			_inputLineShape.SetVisible(false);
			return;
		}

		// Use cached positions from car controller for performance
		var carFrontPosition = _carController.CarFrontPosition;
		var clampedTarget = _carController.ClampedTargetPosition;

		var inputLineColor = hasInput ? InputLineActiveColor :
			(_carController.GetCarSpeed() > 1.0f ? InputLineInactiveColor : InputLineInactiveColor * new Color(1, 1, 1, 0.3f));

		_inputLineShape.SetPoints([carFrontPosition, clampedTarget]);
		_inputLineShape.SetStrokeColor(inputLineColor);
		_inputLineShape.SetVisible(true);
	}

	/// <summary>
	/// Push mouse/touch position indicators to their retained shapes
	/// </summary>
	private void PushMouseIndicators()
	{
		var targetPosition = _carController.GetTargetPosition();
		var hasInput = _carController.HasInput();

		if (targetPosition == Vector2.Zero || (!hasInput && _carController.GetCarSpeed() < 1.0f))
		{
			_touchStationaryShape.SetVisible(false);
			_touchArcShape.SetVisible(false);
			return;
		}

		// Use cached clamped target position from car controller for performance
		var clampedTarget = _carController.ClampedTargetPosition;

		if (clampedTarget != Vector2.Zero)
		{
			_touchStationaryShape.SetTransform(new Transform2D(0f, clampedTarget));
			_touchStationaryShape.SetVisible(true);
		}
		else
		{
			_touchStationaryShape.SetVisible(false);
		}

		// Draw arc under finger/mouse position only if there's active input
		if (hasInput)
		{
			// Use cached values for consistent behavior (restored from original)
			if (!_cachedValuesValid)
			{
				UpdateCachedValues();
			}

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

				_touchArcShape.SetTransform(new Transform2D(0f, targetPosition));
				_touchArcShape.SetArc(startAngle, endAngle);
				_touchArcShape.SetStrokeWidth(arcWidth);
				_touchArcShape.SetVisible(true);
			}
			else
			{
				_touchArcShape.SetVisible(false);
			}
		}
		else
		{
			_touchArcShape.SetVisible(false);
		}
	}

	/// <summary>
	/// Push a single trail's positions and age/zone-faded color stops to its retained shape.
	/// Uses pre-allocated buffers to eliminate per-frame GC allocations.
	/// </summary>
	private void PushTrailLine(List<(Vector2 position, ulong timestamp, Color zoneColor)> trail, Shape trailShape, ColorStop[] stops)
	{
		if (trail.Count < 2)
		{
			trailShape.SetVisible(false);
			return;
		}

		var currentTime = Time.GetTicksMsec();
		var trailCount = trail.Count;

		// Ensure buffer is large enough (rarely allocates after warmup)
		EnsureTrailBufferCapacity(trailCount);

		for (int i = 0; i < trailCount; i++)
		{
			_trailPointsBuffer[i] = trail[i].position;
		}

		// Sample a small, fixed number of stops along the trail rather than one color per point —
		// StrokeStyle only supports ColorStops, not an arbitrary per-vertex Color array. Index 0
		// samples the oldest (most faded) point and matches T=0 (the first point in the polyline);
		// the last stop samples the newest point and matches T=1.
		for (int i = 0; i < TrailStopCount; i++)
		{
			float t = i / (float)(TrailStopCount - 1);
			int sampleIndex = (int)(t * (trailCount - 1));
			var (_, timestamp, zoneColor) = trail[sampleIndex];
			var age = currentTime - timestamp;
			var normalizedAge = Mathf.Clamp(age / TrailLifetime, 0.0f, 1.0f);
			var alpha = (1.0f - normalizedAge) * 0.8f;
			stops[i] = new ColorStop(t, zoneColor * new Color(1, 1, 1, alpha));
		}

		trailShape.SetPoints(_trailPointsBuffer.AsSpan(0, trailCount));
		trailShape.SetStroke(new StrokeStyle
		{
			Width = TireTrailWidth,
			Color = TireTrailColor,
			ColorStops = stops,
			Cap = CapMode.Round,
		});
		trailShape.SetVisible(true);
	}

	/// <summary>
	/// Ensure trail rendering buffer has sufficient capacity.
	/// Allocates with headroom to avoid frequent resizing.
	/// </summary>
	private void EnsureTrailBufferCapacity(int requiredCapacity)
	{
		if (_trailBufferCapacity >= requiredCapacity)
		{
			return;
		}

		// Allocate with headroom to avoid frequent resizing
		_trailBufferCapacity = Math.Max(requiredCapacity, MaxTrailPoints);
		_trailPointsBuffer = new Vector2[_trailBufferCapacity];
	}

	// ================================================================
	// CACHED VALUE MANAGEMENT (Performance Optimization)
	// ================================================================

	/// <summary>
	/// Update cached values for expensive calculations (restored from original)
	/// </summary>
	private void UpdateCachedValues()
	{
		if (_cachedValuesValid)
		{
			return;
		}

		if (_carController?.IsInitialized != true)
		{
			return;
		}

		var carBody = _carController.GetCarBody();
		if (carBody == null)
		{
			return;
		}

		var targetPosition = _carController.GetTargetPosition();
		if (targetPosition == Vector2.Zero)
		{
			return;
		}

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
		{
			return TireTrailColor;
		}

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
			Mathf.Max(0.8f, blended.A / zones.Count));
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
