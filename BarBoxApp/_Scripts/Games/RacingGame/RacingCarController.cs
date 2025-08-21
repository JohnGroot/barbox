using Godot;

namespace BarBox.Games.Racing
{
	/// <summary>
	/// Consolidated racing car controller that extends RacingPlayer with racing game system integration
	/// Combines car management and racing-specific functionality for better cohesion
	/// </summary>
	[GlobalClass]
	public partial class RacingCarController : RacingPlayer
{
	// ================================================================
	// SYSTEM DEPENDENCIES - RACING GAME SYSTEMS
	// ================================================================

	// References to racing game systems for modifiers and transformations
	private RacingCameraController _cameraController;
	private RacingTrackValidationSystem _trackValidationSystem;

	// ================================================================
	// PUBLIC PROPERTIES
	// ================================================================

	public bool IsInitialized => _cameraController != null && _trackValidationSystem != null;
	
	/// <summary>
	/// Backwards compatibility property - returns this instance as RacingPlayer
	/// </summary>
	public RacingPlayer RacingCar => this;

	// ================================================================
	// INITIALIZATION
	// ================================================================

	/// <summary>
	/// Initialize the racing car controller with system dependencies
	/// </summary>
	/// <param name="cameraController">Camera controller for coordinate transformation</param>
	/// <param name="trackValidationSystem">Track validation system for penalties/bonuses</param>
	/// <param name="playerId">Player ID for the car</param>
	/// <param name="playerName">Player name for the car</param>
	public void Initialize(RacingCameraController cameraController, RacingTrackValidationSystem trackValidationSystem, string playerId = "player1", string playerName = "Player")
	{
		// Validate required dependencies
		if (cameraController == null)
			throw new System.ArgumentNullException(nameof(cameraController), "Camera controller is required for car initialization");
		if (trackValidationSystem == null)
			throw new System.ArgumentNullException(nameof(trackValidationSystem), "Track validation system is required for car initialization");
		if (string.IsNullOrEmpty(playerId))
			throw new System.ArgumentException("Player ID cannot be null or empty", nameof(playerId));
		if (string.IsNullOrEmpty(playerName))
			throw new System.ArgumentException("Player name cannot be null or empty", nameof(playerName));

		_cameraController = cameraController;
		_trackValidationSystem = trackValidationSystem;

		// Set player properties
		PlayerId = playerId;
		PlayerName = playerName;

		// Set default car properties
		MaxSpeed = 1800.0f;
		MinSpeed = 100.0f;
		MaxInputDistance = 500.0f;
		AccelerationRate = 300.0f;
		DecelerationRate = 600.0f;
		RotationLerpSpeed = 5.0f;
		CarSize = new Vector2(40, 80);
	}

	/// <summary>
	/// Update car settings from external configuration
	/// </summary>
	/// <param name="maxSpeed">Maximum car speed</param>
	/// <param name="minSpeed">Minimum car speed</param>
	/// <param name="maxInputDistance">Maximum input distance</param>
	/// <param name="accelerationRate">Acceleration rate</param>
	/// <param name="decelerationRate">Deceleration rate</param>
	/// <param name="rotationLerpSpeed">Rotation lerp speed</param>
	/// <param name="carSize">Car size</param>
	public void UpdateSettings(float maxSpeed, float minSpeed, float maxInputDistance, float accelerationRate, float decelerationRate, float rotationLerpSpeed, Vector2 carSize)
	{
		MaxSpeed = maxSpeed;
		MinSpeed = minSpeed;
		MaxInputDistance = maxInputDistance;
		AccelerationRate = accelerationRate;
		DecelerationRate = decelerationRate;
		RotationLerpSpeed = rotationLerpSpeed;
		CarSize = carSize;
	}

	// ================================================================
	// CAR MANAGEMENT
	// ================================================================

	/// <summary>
	/// Position car at a specific world position and rotation
	/// Also resets all physics state for clean repositioning
	/// </summary>
	/// <param name="position">World position</param>
	/// <param name="rotation">Car rotation in radians</param>
	public void PositionCar(Vector2 position, float rotation = 0.0f)
	{
		var carBody = GetCarBody();
		if (carBody != null)
		{
			carBody.GlobalPosition = position;
			carBody.Rotation = rotation;
		}
		
		// Reset all internal physics state to ensure clean positioning
		ResetCarState();
	}

	/// <summary>
	/// Get current car position
	/// </summary>
	/// <returns>Car world position, or Vector2.Zero if not initialized</returns>
	public Vector2 GetCarPosition()
	{
		return GetCarBody()?.GlobalPosition ?? Vector2.Zero;
	}

	/// <summary>
	/// Get current car speed
	/// </summary>
	/// <returns>Car speed, or 0 if not initialized</returns>
	public float GetCarSpeed()
	{
		return GetCurrentSpeed();
	}

	// ================================================================
	// RACING-SPECIFIC OVERRIDES
	// ================================================================

	/// <summary>
	/// Transform screen coordinates to world coordinates using racing game camera
	/// </summary>
	/// <param name="screenPosition">Screen position from input</param>
	/// <returns>World position</returns>
	protected override Vector2 TransformScreenToWorldPosition(Vector2 screenPosition)
	{
		return _cameraController?.TransformScreenToWorldPosition(screenPosition) ?? screenPosition;
	}

	/// <summary>
	/// Get speed modifier based on track validation (off-track penalties)
	/// </summary>
	/// <returns>Speed multiplier (1.0 = normal, less than 1.0 = penalty)</returns>
	protected override float GetSpeedModifier()
	{
		var carPosition = GetCarBody()?.GlobalPosition ?? Vector2.Zero;
		return _trackValidationSystem?.GetSpeedModifier(carPosition) ?? 1.0f;
	}

	/// <summary>
	/// Get acceleration modifier based on track validation (center line bonus, off-track penalties)
	/// </summary>
	/// <returns>Acceleration multiplier (1.0 = normal, greater than 1.0 = bonus, less than 1.0 = penalty)</returns>
	protected override float GetAccelerationModifier()
	{
		var carPosition = GetCarBody()?.GlobalPosition ?? Vector2.Zero;
		return _trackValidationSystem?.GetAccelerationModifier(carPosition) ?? 1.0f;
	}

	/// <summary>
	/// Get turn modifier based on track validation (off-track penalties)
	/// </summary>
	/// <returns>Turn multiplier (1.0 = normal, less than 1.0 = penalty)</returns>
	protected override float GetTurnModifier()
	{
		var carPosition = GetCarBody()?.GlobalPosition ?? Vector2.Zero;
		return _trackValidationSystem?.GetTurnModifier(carPosition) ?? 1.0f;
	}

	// ================================================================
	// PLAYER INTEGRATION
	// ================================================================

	/// <summary>
	/// Set user data for the car player (for GameHost integration)
	/// </summary>
	/// <param name="userData">User data resource</param>
	public new void SetUserData(UserData userData)
	{
		base.SetUserData(userData);
	}

	/// <summary>
	/// Get the underlying BasePlayer instance for game controller integration
	/// </summary>
	/// <returns>BasePlayer instance</returns>
	public BasePlayer GetPlayer()
	{
		return this;
	}

	// ================================================================
	// INPUT HANDLING OVERRIDES
	// ================================================================

	/// <summary>
	/// Override touch input to check racing state
	/// </summary>
	protected override void OnTouchStarted(Vector2 position, int fingerId)
	{
		if (!IsActive() || !IsRacingInputEnabled()) 
			return;
		base.OnTouchStarted(position, fingerId);
	}

	/// <summary>
	/// Override touch input to check racing state
	/// </summary>
	protected override void OnTouchEnded(Vector2 position, int fingerId)
	{
		if (!IsActive() || !IsRacingInputEnabled()) 
			return;
		base.OnTouchEnded(position, fingerId);
	}

	/// <summary>
	/// Override click input to check racing state
	/// </summary>
	protected override void OnClickStarted(Vector2 position)
	{
		if (!IsActive() || !IsRacingInputEnabled())
			return;
		base.OnClickStarted(position);
	}

	/// <summary>
	/// Override click input to check racing state
	/// </summary>
	protected override void OnClickEnded(Vector2 position)
	{
		if (!IsActive() || !IsRacingInputEnabled())
			return;
		base.OnClickEnded(position);
	}

	/// <summary>
	/// Check if racing input should be enabled based on current racing state
	/// </summary>
	private bool IsRacingInputEnabled()
	{
		var racingGame = GetParent<RacingGame>();
		return racingGame?.IsInputEnabled() ?? true;
	}

	/// <summary>
	/// Override _Process to check racing state before processing input
	/// </summary>
	public override void _Process(double delta)
	{
		// Check racing state before processing any input
		if (!IsRacingInputEnabled())
		{
			// Clear any existing input when state doesn't allow it
			_hasInput = false;
		}
		
		base._Process(delta);
	}

	// ================================================================
	// DEPENDENCY MANAGEMENT
	// ================================================================

	/// <summary>
	/// Update system dependencies (useful when systems are recreated)
	/// </summary>
	/// <param name="cameraController">Updated camera controller</param>
	/// <param name="trackValidationSystem">Updated track validation system</param>
	public void UpdateDependencies(RacingCameraController cameraController, RacingTrackValidationSystem trackValidationSystem)
	{
		_cameraController = cameraController;
		_trackValidationSystem = trackValidationSystem;
	}

	/// <summary>
	/// Check if dependencies are properly initialized
	/// </summary>
	/// <returns>True if both camera controller and track validation system are set</returns>
	public bool HasValidDependencies()
	{
		return _cameraController != null && _trackValidationSystem != null;
	}
}
}