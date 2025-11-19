using Godot;

namespace BarBox.Games.Racing
{
	/// <summary>
	/// Handles track validation and off-track penalty calculations for racing games
	/// Extracted from RacingGame to follow single responsibility principle
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

	// Off-track penalty state
	private bool _isCurrentlyOffTrack = false;
	private float _currentSpeedPenaltyMultiplier = 1.0f;
	private float _currentTurnPenaltyMultiplier = 1.0f;
	private float _currentAccelerationPenaltyMultiplier = 1.0f;

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
	}

	// ================================================================
	// TRACK VALIDATION
	// ================================================================

	public bool IsOnTrack(Vector2 position)
	{
		return _trackDefinition?.IsValidTrackPoint(position) ?? true;
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

	// ================================================================
	// PENALTY SYSTEM
	// ================================================================

	public void UpdateOffTrackPenalties(Vector2 carPosition, float delta)
	{
		bool isOnTrack = IsOnTrack(carPosition);
		_isCurrentlyOffTrack = !isOnTrack;

		float targetSpeedMultiplier = isOnTrack ? 1.0f : OffTrackSpeedPenalty;
		float targetTurnMultiplier = isOnTrack ? 1.0f : OffTrackTurnPenalty;
		float targetAccelerationMultiplier = isOnTrack ? 1.0f : OffTrackAccelerationPenalty;

		float lerpSpeed = OffTrackPenaltyLerpSpeed * delta;
		_currentSpeedPenaltyMultiplier = Mathf.Lerp(_currentSpeedPenaltyMultiplier, targetSpeedMultiplier, lerpSpeed);
		_currentTurnPenaltyMultiplier = Mathf.Lerp(_currentTurnPenaltyMultiplier, targetTurnMultiplier, lerpSpeed);
		_currentAccelerationPenaltyMultiplier = Mathf.Lerp(_currentAccelerationPenaltyMultiplier, targetAccelerationMultiplier, lerpSpeed);
	}

	/// <summary>
	/// Returns speed multiplier (1.0 = normal, less than 1.0 = penalty)
	/// </summary>
	public float GetSpeedModifier(Vector2 carPosition)
	{
		return _currentSpeedPenaltyMultiplier;
	}

	/// <summary>
	/// Get acceleration modifier for car based on track position and bonuses/penalties
	/// </summary>
	/// <param name="carPosition">Current car position</param>
	/// <returns>Acceleration multiplier (1.0 = normal, greater than 1.0 = bonus, less than 1.0 = penalty)</returns>
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
	/// Returns turn multiplier (1.0 = normal, less than 1.0 = penalty)
	/// </summary>
	public float GetTurnModifier(Vector2 carPosition)
	{
		return _currentTurnPenaltyMultiplier;
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
	}

	public bool IsInitialized()
	{
		return _trackDefinition != null && _trackCurve != null;
	}
}
}