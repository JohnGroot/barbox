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

	/// <summary>
	/// Check if a position is on the track
	/// </summary>
	/// <param name="position">World position to check</param>
	/// <returns>True if position is on track</returns>
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

	/// <summary>
	/// Update off-track penalties based on car position
	/// </summary>
	/// <param name="carPosition">Current car position</param>
	/// <param name="delta">Frame delta time</param>
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
	/// Get speed modifier for car based on track position and penalties
	/// </summary>
	/// <param name="carPosition">Current car position</param>
	/// <returns>Speed multiplier (1.0 = normal, less than 1.0 = penalty)</returns>
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
	/// Get turn modifier for car based on track position and penalties
	/// </summary>
	/// <param name="carPosition">Current car position</param>
	/// <returns>Turn multiplier (1.0 = normal, less than 1.0 = penalty)</returns>
	public float GetTurnModifier(Vector2 carPosition)
	{
		return _currentTurnPenaltyMultiplier;
	}

	// ================================================================
	// UTILITY METHODS
	// ================================================================

	/// <summary>
	/// Reset penalty state to normal values
	/// </summary>
	public void ResetPenalties()
	{
		_isCurrentlyOffTrack = false;
		_currentSpeedPenaltyMultiplier = 1.0f;
		_currentTurnPenaltyMultiplier = 1.0f;
		_currentAccelerationPenaltyMultiplier = 1.0f;
	}

	/// <summary>
	/// Check if the system is properly initialized
	/// </summary>
	/// <returns>True if track definition and curve are set</returns>
	public bool IsInitialized()
	{
		return _trackDefinition != null && _trackCurve != null;
	}
}
}