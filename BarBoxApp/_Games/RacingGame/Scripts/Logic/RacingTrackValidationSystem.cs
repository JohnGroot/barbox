using Godot;

namespace BarBox.Games.Racing
{
	/// <summary>
	/// Handles track validation and off-track penalty calculations for racing games.
	/// Integrates with RacingZoneManager to combine track penalties with zone modifiers.
	/// </summary>
	[GlobalClass]
	public partial class RacingTrackValidationSystem : Node
	{
		// ================================================================
		// EXPORT PROPERTIES - TRACK PERFORMANCE
		// ================================================================

		[ExportCategory("Track Performance")]
		[Export] public float CenterLineProximityRange { get; set; } = 40.0f;
		[Export] public float CenterLineAccelerationBonus { get; set; } = 1.5f;
		[Export] public float TrackProximityRange { get; set; } = 20.0f;

		[ExportCategory("Off-Track Penalties")]
		[Export] public float OffTrackSpeedPenalty { get; set; } = 0.3f;
		[Export] public float OffTrackTurnPenalty { get; set; } = 0.3f;
		[Export] public float OffTrackAccelerationPenalty { get; set; } = 0.3f;
		[Export] public float OffTrackPenaltyLerpSpeed { get; set; } = 3.0f;

		// ================================================================
		// PRIVATE FIELDS
		// ================================================================

		// Track references
		private IRacingTrackDefinition _trackDefinition;
		private Curve2D _trackCurve;

		// Zone manager for combining zone modifiers
		private RacingZoneManager _zoneManager;

		// Off-track penalty state
		private bool _isCurrentlyOffTrack = false;
		private float _currentSpeedPenaltyMultiplier = 1.0f;
		private float _currentTurnPenaltyMultiplier = 1.0f;
		private float _currentAccelerationPenaltyMultiplier = 1.0f;

		// Position cache for expensive off-track calculations (performance optimization)
		private Vector2 _lastCheckedPosition = Vector2.Zero;
		private float _lastCheckedRotation = 0f;
		private bool _cachedIsCompletelyOffTrack = false;
		private const float POSITION_CACHE_THRESHOLD_SQ = 25.0f; // 5 pixels squared
		private const float ROTATION_CACHE_THRESHOLD = 0.1f; // radians (~5.7 degrees)

		// Static unit offsets for car bounds sampling (GC optimization: avoids per-frame Vector2[8] allocation)
		private static readonly Vector2[] _sampleUnitOffsets =
		{
			new(-1, -1), new(1, -1), new(1, 1), new(-1, 1),  // corners
			new(0, -1), new(1, 0), new(0, 1), new(-1, 0),    // midpoints
		};
		private readonly Vector2[] _scaledSampleBuffer = new Vector2[8];

		// ================================================================
		// PUBLIC PROPERTIES
		// ================================================================

		public bool IsCurrentlyOffTrack => _isCurrentlyOffTrack;
		public float CurrentSpeedPenaltyMultiplier => _currentSpeedPenaltyMultiplier;
		public float CurrentTurnPenaltyMultiplier => _currentTurnPenaltyMultiplier;
		public float CurrentAccelerationPenaltyMultiplier => _currentAccelerationPenaltyMultiplier;

	// ================================================================
	// INITIALIZATION
	// ================================================================

	/// <summary>
	/// Initialize the track validation system with track data
	/// </summary>
	/// <param name="trackDefinition">The track definition to validate against</param>
	/// <param name="trackCurve">The track curve for distance calculations</param>
	public void Initialize(IRacingTrackDefinition trackDefinition, Curve2D trackCurve)
	{
		_trackDefinition = trackDefinition;
		_trackCurve = trackCurve;

		// Reset penalty state
		_isCurrentlyOffTrack = false;
		_currentSpeedPenaltyMultiplier = 1.0f;
		_currentTurnPenaltyMultiplier = 1.0f;
		_currentAccelerationPenaltyMultiplier = 1.0f;

		// Reset position cache
		_lastCheckedPosition = Vector2.Zero;
		_lastCheckedRotation = 0f;
		_cachedIsCompletelyOffTrack = false;
	}

	/// <summary>
	/// Set the zone manager for combining zone modifiers with track penalties
	/// </summary>
	public void SetZoneManager(RacingZoneManager zoneManager)
	{
		_zoneManager = zoneManager;
	}

	/// <summary>
	/// Get the zone manager (may be null if not set)
	/// </summary>
	public RacingZoneManager GetZoneManager() => _zoneManager;

	// ================================================================
	// TRACK VALIDATION
	// ================================================================

	/// <summary>
	/// Check if a position is on valid track surface.
	/// Includes kerb zones as valid track areas.
	/// </summary>
	public bool IsOnTrack(Vector2 position)
	{
		// First check if on main track surface
		if (_trackDefinition?.IsValidTrackPoint(position) == true)
			return true;

		// Then check if on a kerb zone (counts as on-track)
		if (_zoneManager?.IsInKerbZone(position) == true)
			return true;

		return false;
	}

	/// <summary>
	/// Get distance from position to track center line
	/// </summary>
	/// <param name="position">World position</param>
	/// <returns>Distance to center line, or float.MaxValue if no track curve</returns>
	public float GetDistanceToTrackCenterLine(Vector2 position)
	{
		if (_trackCurve == null) return float.MaxValue;
		var closestPoint = _trackCurve.GetClosestPoint(position);
		return position.DistanceTo(closestPoint);
	}

	/// <summary>
	/// Check if the car is completely off track (no part of car bounds touching track).
	/// Checks 8 sample points: 4 corners + 4 edge midpoints of rotated car bounds.
	/// </summary>
	public bool IsCarCompletelyOffTrack(Vector2 carCenter, float carRotation, Vector2 carSize)
	{
		// Fast path: center point check (most frames the car is on track)
		if (IsOnTrack(carCenter))
			return false;

		// Slow path: check 8 boundary points only when center is off track
		var halfSize = carSize / 2.0f;

		// Scale unit offsets into reusable buffer (no heap allocation)
		for (int i = 0; i < _sampleUnitOffsets.Length; i++)
		{
			_scaledSampleBuffer[i] = new Vector2(
				_sampleUnitOffsets[i].X * halfSize.X,
				_sampleUnitOffsets[i].Y * halfSize.Y
			);
		}

		// If ANY point is on track, car is not completely off
		for (int i = 0; i < _scaledSampleBuffer.Length; i++)
		{
			var rotatedOffset = RotatePoint(_scaledSampleBuffer[i], carRotation);
			var worldPoint = carCenter + rotatedOffset;

			if (IsOnTrack(worldPoint))
				return false;
		}

		return true; // All points off track
	}

	private static Vector2 RotatePoint(Vector2 point, float rotation)
	{
		float cos = Mathf.Cos(rotation);
		float sin = Mathf.Sin(rotation);
		return new Vector2(
			point.X * cos - point.Y * sin,
			point.X * sin + point.Y * cos
		);
	}

	// ================================================================
	// PENALTY SYSTEM
	// ================================================================

	public void UpdateOffTrackPenalties(Vector2 carPosition, float carRotation, Vector2 carSize, float delta)
	{
		// Performance optimization: Skip expensive recalculation if car hasn't moved significantly
		// Most frames the car moves < 5 pixels, so we can reuse the cached result
		bool needsRecalculation = _lastCheckedPosition.DistanceSquaredTo(carPosition) >= POSITION_CACHE_THRESHOLD_SQ
			|| Mathf.Abs(carRotation - _lastCheckedRotation) >= ROTATION_CACHE_THRESHOLD;

		if (needsRecalculation)
		{
			// Only apply penalties when car is COMPLETELY off track (no part touching)
			_cachedIsCompletelyOffTrack = IsCarCompletelyOffTrack(carPosition, carRotation, carSize);
			_lastCheckedPosition = carPosition;
			_lastCheckedRotation = carRotation;
		}

		_isCurrentlyOffTrack = _cachedIsCompletelyOffTrack;

		float targetSpeedMultiplier = _cachedIsCompletelyOffTrack ? OffTrackSpeedPenalty : 1.0f;
		float targetTurnMultiplier = _cachedIsCompletelyOffTrack ? OffTrackTurnPenalty : 1.0f;
		float targetAccelerationMultiplier = _cachedIsCompletelyOffTrack ? OffTrackAccelerationPenalty : 1.0f;

		float lerpSpeed = OffTrackPenaltyLerpSpeed * delta;
		_currentSpeedPenaltyMultiplier = Mathf.Lerp(_currentSpeedPenaltyMultiplier, targetSpeedMultiplier, lerpSpeed);
		_currentTurnPenaltyMultiplier = Mathf.Lerp(_currentTurnPenaltyMultiplier, targetTurnMultiplier, lerpSpeed);
		_currentAccelerationPenaltyMultiplier = Mathf.Lerp(_currentAccelerationPenaltyMultiplier, targetAccelerationMultiplier, lerpSpeed);
	}

	/// <summary>
	/// Returns speed multiplier from track penalties only (1.0 = normal, less than 1.0 = penalty)
	/// </summary>
	public float GetSpeedModifier(Vector2 carPosition)
	{
		return _currentSpeedPenaltyMultiplier;
	}

	/// <summary>
	/// Returns combined speed multiplier from track penalties AND zone modifiers.
	/// Modifiers are multiplicative (0.5 track penalty * 0.5 zone = 0.25 total)
	/// </summary>
	public float GetSpeedModifier(Node2D body, Vector2 carPosition)
	{
		float trackModifier = _currentSpeedPenaltyMultiplier;
		float zoneModifier = _zoneManager?.GetCombinedSpeedModifier(body) ?? 1.0f;
		return trackModifier * zoneModifier;
	}

	/// <summary>
	/// Get acceleration modifier from track position and bonuses/penalties only
	/// </summary>
	public float GetAccelerationModifier(Vector2 carPosition)
	{
		float baseModifier = _currentAccelerationPenaltyMultiplier;

		// Apply center line bonus if on track
		if (IsOnTrack(carPosition))
		{
			var distanceToCenter = GetDistanceToTrackCenterLine(carPosition);
			if (distanceToCenter <= CenterLineProximityRange)
			{
				baseModifier = Mathf.Max(baseModifier, CenterLineAccelerationBonus);
			}
		}

		return baseModifier;
	}

	/// <summary>
	/// Get combined acceleration modifier from track AND zone modifiers
	/// </summary>
	public float GetAccelerationModifier(Node2D body, Vector2 carPosition)
	{
		float trackModifier = GetAccelerationModifier(carPosition);
		float zoneModifier = _zoneManager?.GetCombinedAccelerationModifier(body) ?? 1.0f;
		return trackModifier * zoneModifier;
	}

	/// <summary>
	/// Returns turn multiplier from track penalties only (1.0 = normal, less than 1.0 = penalty)
	/// </summary>
	public float GetTurnModifier(Vector2 carPosition)
	{
		return _currentTurnPenaltyMultiplier;
	}

	/// <summary>
	/// Returns combined turn multiplier from track penalties AND zone modifiers
	/// </summary>
	public float GetTurnModifier(Node2D body, Vector2 carPosition)
	{
		float trackModifier = _currentTurnPenaltyMultiplier;
		float zoneModifier = _zoneManager?.GetCombinedTurnModifier(body) ?? 1.0f;
		return trackModifier * zoneModifier;
	}

	/// <summary>
	/// Check if input is blocked for the given body (e.g., in frictionless zone)
	/// </summary>
	public bool IsInputBlocked(Node2D body)
	{
		return _zoneManager?.IsInputBlocked(body) ?? false;
	}

	// ================================================================
	// UTILITY METHODS
	// ================================================================

	public void ResetPenalties()
	{
		_isCurrentlyOffTrack = false;
		_currentSpeedPenaltyMultiplier = 1.0f;
		_currentTurnPenaltyMultiplier = 1.0f;
		_currentAccelerationPenaltyMultiplier = 1.0f;

		// Reset position cache
		_lastCheckedPosition = Vector2.Zero;
		_lastCheckedRotation = 0f;
		_cachedIsCompletelyOffTrack = false;
	}

	public bool IsInitialized()
	{
		return _trackDefinition != null && _trackCurve != null;
	}
}
}