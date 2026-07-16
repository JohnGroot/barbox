using System;
using System.Collections.Generic;
using Godot;

namespace BarBox.Games.Racing;

/// <summary>
/// Unified racing car that combines physics, input handling, and racing system integration.
/// </summary>
[GlobalClass]
public partial class RacingCar : BasePlayer
{
	#region Exports

	[ExportCategory("Car Settings")]
	[Export]
	public Vector2 CarSize { get; set; } = new Vector2(80, 160);

	[Export]
	public float MaxSpeed { get; set; } = 400.0f;

	[Export]
	public float MinSpeed { get; set; } = 50.0f;

	[Export]
	public float AccelerationRate { get; set; } = 800.0f;

	[Export]
	public float DecelerationRate { get; set; } = 600.0f;

	[Export]
	public float RotationLerpSpeed { get; set; } = 5.0f;

	[ExportCategory("Headlight Settings")]
	[Export]
	public bool HeadlightEnabled { get; set; } = true;

	[Export]
	public float HeadlightRange { get; set; } = 200.0f;

	[Export]
	public float HeadlightWidth { get; set; } = 47.0f;

	[Export]
	public Color HeadlightColor { get; set; } = new Color(1.0f, 1.0f, 0.8f, 0.27f);

	[ExportCategory("Input Settings")]
	[Export]
	public float MaxInputDistance { get; set; } = 300.0f;

	[ExportCategory("Turn Penalty Settings")]
	[Export]
	public float TurnPenaltySpeedMultiplier { get; set; } = 0.8f;

	[Export]
	public float TurnPenaltyAccelMultiplier { get; set; } = 0.8f;

	[Export]
	public float MaxAngularVelocityForPenalty { get; set; } = 10.0f;

	[Export]
	public float TurnPenaltyLerpSpeed { get; set; } = 3.0f;

	#endregion

	#region Constants

	private const float CAR_VISUAL_OFFSET_RATIO = 0.125f;
	private const float HEADLIGHT_CONE_HALF_WIDTH_RATIO = 0.5f;
	private const float HEADLIGHT_HORIZONTAL_OFFSET_RATIO = 0.375f;
	private const float HEADLIGHT_VERTICAL_OFFSET_RATIO = 0.25f;
	private const float CAR_FRONT_HORIZONTAL_RATIO = 0.75f;
	private const float CAR_FRONT_VERTICAL_RATIO = 0.25f;
	private const float CAR_BACK_VERTICAL_RATIO = 1.5f;
	private const float TIRE_OFFSET_RATIO = 0.625f;

	#endregion

	#region Fields

	// References to racing game systems for modifiers and transformations
	private RacingCameraController _cameraController;
	private RacingTrackValidationSystem _trackValidationSystem;

	/// <summary>
	/// Callback to check if input is enabled. Set by parent game to decouple from parent type.
	/// Returns true if input should be processed, false otherwise.
	/// </summary>
	public Func<bool> InputEnabledCallback { get; set; }

	protected CharacterBody2D _carBody;
	protected CollisionShape2D _carCollision;
	protected ColorRect _carVisual;
	protected Polygon2D _headlightCone;

	protected Vector2 _targetPosition;
	protected Vector2 _lastTargetPosition;
	protected Vector2 _velocity = Vector2.Zero;
	protected float _currentSpeed = 0.0f;
	protected bool _hasInput = false;
	private int _activeFingerId = -1;

	private bool _isInFrictionlessState = false;
	private float _frictionlessTimeRemaining = 0.0f;
	private Vector2 _lockedVelocity = Vector2.Zero;
	private float _lockedRotation = 0.0f;

	private float _previousRotation;
	private float _angularVelocity;
	private float _currentTurnPenalty = 1.0f;

	// Cached directional vectors for performance
	protected Vector2 _cachedCarForward = Vector2.Zero;
	protected Vector2 _cachedCarRight = Vector2.Zero;

	// Cached position data for visual feedback and components
	protected Vector2 _cachedCarFrontPosition = Vector2.Zero;
	protected Vector2 _cachedCarBackPosition = Vector2.Zero;
	protected Vector2 _cachedLeftTirePosition = Vector2.Zero;
	protected Vector2 _cachedRightTirePosition = Vector2.Zero;
	protected Vector2 _cachedClampedTargetPosition = Vector2.Zero;

	protected bool _positionCacheDirty = true;

	protected InputManager _inputManager;

	public bool IsInitialized => _cameraController != null && _trackValidationSystem != null;

	#endregion

	#region Initialization

	protected override void InitializePlayer()
	{
		base.InitializePlayer();

		SetupCarBody();
		SetupInputManager();
	}

	public void Initialize(RacingCameraController cameraController, RacingTrackValidationSystem trackValidationSystem, string playerId = "player1", string playerName = "Player")
	{
		if (string.IsNullOrEmpty(playerId))
		{
			throw new System.ArgumentException("Player ID cannot be null or empty", nameof(playerId));
		}

		if (string.IsNullOrEmpty(playerName))
		{
			throw new System.ArgumentException("Player name cannot be null or empty", nameof(playerName));
		}

		_cameraController = cameraController ?? throw new System.ArgumentNullException(nameof(cameraController), "Camera controller is required for car initialization");
		_trackValidationSystem = trackValidationSystem ?? throw new System.ArgumentNullException(nameof(trackValidationSystem), "Track validation system is required for car initialization");

		PlayerId = playerId;
		PlayerName = playerName;

		// Note: Physics properties (MaxSpeed, CarSize, etc.) are set by RacingGame
		// before AddChild, so they're already configured when this is called
	}

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

	#endregion

	#region Car Body Setup

	protected virtual void SetupCarBody()
	{
		_carBody = new CharacterBody2D();
		AddChild(_carBody);

		_carVisual = new ColorRect();
		_carVisual.Size = CarSize;
		_carVisual.Color = Colors.Red;
		_carVisual.Position = new Vector2(-CarSize.X * CAR_VISUAL_OFFSET_RATIO, -CarSize.Y * CAR_VISUAL_OFFSET_RATIO);
		_carBody.AddChild(_carVisual);

		_carCollision = new CollisionShape2D();
		var carShape = new RectangleShape2D();
		carShape.Size = CarSize;
		_carCollision.Shape = carShape;
		_carBody.AddChild(_carCollision);

		_carBody.GlobalPosition = GlobalPosition;

		SetupHeadlightCone();
	}

	protected virtual void SetupHeadlightCone()
	{
		if (!HeadlightEnabled)
		{
			return;
		}

		_headlightCone = new Polygon2D();
		_carBody.AddChild(_headlightCone);

		// Create triangle cone shape pointing forward using cached car front position
		var halfWidth = Mathf.DegToRad(HeadlightWidth * HEADLIGHT_CONE_HALF_WIDTH_RATIO);
		var range = HeadlightRange;

		// Use cached front position offset for consistent positioning with visual feedback
		var carFrontOffset = (CarRight * -CarSize.X * HEADLIGHT_HORIZONTAL_OFFSET_RATIO) + (CarForward * CarSize.Y * HEADLIGHT_VERTICAL_OFFSET_RATIO);

		// Triangle vertices: start at front center of car, cone extends forward
		var vertices = new Vector2[]
		{
			carFrontOffset, // Start at front center of car (using cached calculation)
			carFrontOffset + new Vector2(-range * Mathf.Sin(halfWidth), -range * Mathf.Cos(halfWidth)), // Left edge
			carFrontOffset + new Vector2(range * Mathf.Sin(halfWidth), -range * Mathf.Cos(halfWidth)), // Right edge
		};

		_headlightCone.Polygon = vertices;
		_headlightCone.Color = HeadlightColor;

		// Position at origin since vertices already include proper offset
		_headlightCone.Position = Vector2.Zero;
	}

	#endregion

	#region Input System Setup

	protected virtual void SetupInputManager()
	{
		_inputManager = InputManager.GetAutoload();
		if (_inputManager == null || !IsInstanceValid(_inputManager))
		{
			return;
		}

		_inputManager.TouchStarted += OnTouchStarted;
		_inputManager.TouchEnded += OnTouchEnded;
		_inputManager.ClickStarted += OnClickStarted;
		_inputManager.ClickEnded += OnClickEnded;
	}

	#endregion

	#region Core Game Loop

	public override void _Process(double delta)
	{
		if (!IsActive())
		{
			return;
		}

		if (!IsRacingInputEnabled())
		{
			_hasInput = false;
			_activeFingerId = -1;
		}

		float deltaF = (float)delta;

		if (_inputManager != null && IsInstanceValid(_inputManager) && _hasInput)
		{
			if (_inputManager.IsTouchActive())
			{
				Vector2 screenPosition = _inputManager.GetTouchPosition(_activeFingerId);
				Vector2 worldPosition = TransformScreenToWorldPosition(screenPosition);
				_targetPosition = worldPosition;
				_lastTargetPosition = worldPosition;
			}
			else
			{
				_hasInput = false;
			}
		}

		UpdateCarPhysics(deltaF);
	}

	#endregion

	#region Physics And Movement

	protected virtual void UpdateCarPhysics(float delta)
	{
		// Check for frictionless state first - maintains locked velocity and ignores all input
		if (_isInFrictionlessState)
		{
			UpdateFrictionlessState(delta);
			return;
		}

		CheckFrictionlessZoneEntry();

		UpdateTurnPenalty(delta);

		Vector2 inputDirection = Vector2.Zero;
		float targetSpeed = 0.0f;

		if (_hasInput)
		{
			var directionToTarget = _targetPosition - _carBody.GlobalPosition;
			var distanceToTarget = directionToTarget.Length();

			if (distanceToTarget > 1.0f)
			{
				inputDirection = directionToTarget.Normalized();

				// Calculate speed based on distance (closer = slower, farther = faster)
				var normalizedDistance = Mathf.Clamp(distanceToTarget / MaxInputDistance, 0.0f, 1.0f);
				targetSpeed = Mathf.Lerp(MinSpeed, MaxSpeed, normalizedDistance);
			}
		}
		else if (_lastTargetPosition != Vector2.Zero)
		{
			// Decelerate in last known direction
			var directionToLastTarget = _lastTargetPosition - _carBody.GlobalPosition;
			if (directionToLastTarget.Length() > 1.0f)
			{
				inputDirection = directionToLastTarget.Normalized();
			}

			targetSpeed = 0.0f;
		}

		targetSpeed *= GetSpeedModifier();

		if (targetSpeed > _currentSpeed)
		{
			_currentSpeed = Mathf.MoveToward(_currentSpeed, targetSpeed, AccelerationRate * GetAccelerationModifier() * delta);
		}
		else
		{
			_currentSpeed = Mathf.MoveToward(_currentSpeed, targetSpeed, DecelerationRate * delta);
		}

		if (_currentSpeed > 0.1f && inputDirection != Vector2.Zero)
		{
			var effectiveRotationSpeed = RotationLerpSpeed * GetTurnModifier() * delta;

			if (_hasInput)
			{
				var targetDirection = (_targetPosition - _carBody.GlobalPosition).Normalized();
				var targetAngle = targetDirection.Angle() + (Mathf.Pi / 2);
				_carBody.Rotation = Mathf.LerpAngle(_carBody.Rotation, targetAngle, effectiveRotationSpeed);
			}
			else
			{
				// When not actively steering, rotate towards movement direction
				_carBody.Rotation = Mathf.LerpAngle(_carBody.Rotation, _velocity.Angle() + (Mathf.Pi / 2), effectiveRotationSpeed);
			}

			var carForward = new Vector2(Mathf.Sin(_carBody.Rotation), -Mathf.Cos(_carBody.Rotation));
			_velocity = carForward * _currentSpeed * GetTurnModifier();
		}
		else
		{
			_velocity = Vector2.Zero;
		}

		_carBody.Velocity = _velocity;
		_carBody.MoveAndSlide();

		InvalidatePositionCache();
	}

	#endregion

	#region Frictionless Zone Handling

	private void CheckFrictionlessZoneEntry()
	{
		var zoneManager = _trackValidationSystem.GetZoneManager();
		if (zoneManager == null)
		{
			return;
		}

		var (isActive, timeRemaining, lockedVelocity, lockedRotation) = zoneManager.GetFrictionlessState(_carBody);
		if (isActive && !_isInFrictionlessState)
		{
			EnterFrictionlessState(lockedVelocity, lockedRotation, timeRemaining);
		}
	}

	/// <summary>
	/// Get zones at a specific world position (for visual feedback like tire trails)
	/// </summary>
	public List<RacingZone> GetZonesAtPosition(Vector2 position)
	{
		return _trackValidationSystem?.GetZoneManager()?.GetZonesAtPosition(position) ?? [];
	}

	/// <summary>
	/// Enter frictionless state - locks velocity and rotation, ignores input
	/// </summary>
	public void EnterFrictionlessState(Vector2 velocity, float rotation, float duration)
	{
		_isInFrictionlessState = true;
		_frictionlessTimeRemaining = duration;
		_lockedVelocity = velocity;
		_lockedRotation = rotation;

		_carBody.Rotation = rotation;
	}

	private void UpdateFrictionlessState(float delta)
	{
		_frictionlessTimeRemaining -= delta;

		if (_frictionlessTimeRemaining <= 0)
		{
			ExitFrictionlessState();
			return;
		}

		_carBody.Rotation = _lockedRotation;
		_carBody.Velocity = _lockedVelocity;
		_carBody.MoveAndSlide();

		_velocity = _lockedVelocity;
		_currentSpeed = _lockedVelocity.Length();

		InvalidatePositionCache();
	}

	private void ExitFrictionlessState()
	{
		_isInFrictionlessState = false;
		_frictionlessTimeRemaining = 0.0f;

		// Keep current velocity and rotation, just resume normal physics
	}

	public bool IsInFrictionlessState => _isInFrictionlessState;

	public float FrictionlessTimeRemaining => _frictionlessTimeRemaining;

	#endregion

	#region Turn Penalty Calculation

	private void UpdateTurnPenalty(float delta)
	{
		if (delta <= 0)
		{
			return;
		}

		// Calculate angular velocity (rad/s)
		float rotationDelta = Mathf.AngleDifference(_previousRotation, _carBody.Rotation);
		_angularVelocity = Mathf.Abs(rotationDelta / delta);
		_previousRotation = _carBody.Rotation;

		// Normalize to 0-1 range (0 = no turn, 1 = max turn)
		float turnIntensity = Mathf.Clamp(_angularVelocity / MaxAngularVelocityForPenalty, 0f, 1f);

		// Target penalty factor (1.0 = no penalty, lower = more penalty)
		float targetPenalty = 1.0f - (turnIntensity * (1.0f - TurnPenaltySpeedMultiplier));

		// Smooth the penalty to avoid jarring changes
		_currentTurnPenalty = Mathf.Lerp(_currentTurnPenalty, targetPenalty, TurnPenaltyLerpSpeed * delta);
	}

	private float GetTurnAccelerationPenalty()
	{
		float turnIntensity = Mathf.Clamp(_angularVelocity / MaxAngularVelocityForPenalty, 0f, 1f);
		return 1.0f - (turnIntensity * (1.0f - TurnPenaltyAccelMultiplier));
	}

	#endregion

	#region Position Cache Management

	protected virtual void UpdatePositionCache()
	{
		if (!_positionCacheDirty || _carBody == null)
		{
			return;
		}

		_cachedCarForward = new Vector2(Mathf.Sin(_carBody.Rotation), -Mathf.Cos(_carBody.Rotation));
		_cachedCarRight = new Vector2(_cachedCarForward.Y, -_cachedCarForward.X);

		// Cache car size components for repeated calculations
		var halfLength = CarSize.Y * 0.5f;
		var halfWidth = CarSize.X * 0.5f;
		var carPosition = _carBody.GlobalPosition;

		// Cache front position (used by headlights and visual feedback)
		var frontOffset = (_cachedCarRight * -halfWidth * CAR_FRONT_HORIZONTAL_RATIO) + (_cachedCarForward * halfLength * CAR_FRONT_VERTICAL_RATIO);
		_cachedCarFrontPosition = carPosition + frontOffset;

		// Cache back position and tire positions (used by visual feedback)
		var backOffset = (_cachedCarRight * -halfWidth * CAR_FRONT_HORIZONTAL_RATIO) - (_cachedCarForward * halfLength * CAR_BACK_VERTICAL_RATIO);
		_cachedCarBackPosition = carPosition + backOffset;

		var tireOffset = halfWidth * TIRE_OFFSET_RATIO;
		_cachedLeftTirePosition = _cachedCarBackPosition - (_cachedCarRight * tireOffset);
		_cachedRightTirePosition = _cachedCarBackPosition + (_cachedCarRight * tireOffset);

		// Cache clamped target position for input visualization
		if (_targetPosition != Vector2.Zero)
		{
			var directionToTarget = _targetPosition - carPosition;
			var distanceToTarget = directionToTarget.Length();
			_cachedClampedTargetPosition = carPosition + (directionToTarget.Normalized() * Mathf.Min(distanceToTarget, MaxInputDistance));
		}
		else
		{
			_cachedClampedTargetPosition = Vector2.Zero;
		}

		_positionCacheDirty = false;
	}

	protected virtual void InvalidatePositionCache()
	{
		_positionCacheDirty = true;
	}

	#endregion

	#region Racing-Specific Integrations

	protected virtual Vector2 TransformScreenToWorldPosition(Vector2 screenPosition)
	{
		return _cameraController?.TransformScreenToWorldPosition(screenPosition) ?? screenPosition;
	}

	protected virtual float GetSpeedModifier()
	{
		var carBody = GetCarBody();
		if (carBody == null || _trackValidationSystem == null)
		{
			return _currentTurnPenalty;
		}

		// Use body-based overload to include zone modifiers, then apply turn penalty
		return _trackValidationSystem.GetSpeedModifier(carBody, carBody.GlobalPosition) * _currentTurnPenalty;
	}

	protected virtual float GetAccelerationModifier()
	{
		var carBody = GetCarBody();
		float turnAccelPenalty = GetTurnAccelerationPenalty();
		if (carBody == null || _trackValidationSystem == null)
		{
			return turnAccelPenalty;
		}

		// Use body-based overload to include zone modifiers, then apply turn penalty
		return _trackValidationSystem.GetAccelerationModifier(carBody, carBody.GlobalPosition) * turnAccelPenalty;
	}

	protected virtual float GetTurnModifier()
	{
		var carBody = GetCarBody();
		if (carBody == null || _trackValidationSystem == null)
		{
			return 1.0f;
		}

		// Use body-based overload to include zone modifiers
		return _trackValidationSystem.GetTurnModifier(carBody, carBody.GlobalPosition);
	}

	#endregion

	#region Input Handlers

	private bool IsRacingInputEnabled()
	{
		return InputEnabledCallback?.Invoke() ?? true;
	}

	protected virtual void OnTouchStarted(Vector2 position, int fingerId)
	{
		if (!IsActive() || !IsRacingInputEnabled())
		{
			return;
		}

		if (_activeFingerId == -1)
		{
			_activeFingerId = fingerId;
			_hasInput = true;
		}
	}

	protected virtual void OnTouchEnded(Vector2 position, int fingerId)
	{
		if (!IsActive() || !IsRacingInputEnabled())
		{
			return;
		}

		if (fingerId == _activeFingerId)
		{
			_activeFingerId = -1;
			_hasInput = false;
		}
	}

	protected virtual void OnClickStarted(Vector2 position)
	{
		OnTouchStarted(position, 0);
	}

	protected virtual void OnClickEnded(Vector2 position)
	{
		OnTouchEnded(position, 0);
	}

	#endregion

	#region Car Management

	/// <summary>
	/// Resets all physics state to ensure clean positioning
	/// </summary>
	public void PositionCar(Vector2 position, float rotation = 0.0f)
	{
		var carBody = GetCarBody();
		if (carBody != null)
		{
			carBody.GlobalPosition = position;
			carBody.Rotation = rotation;
		}

		ResetCarState();

		InvalidatePositionCache();
	}

	public Vector2 GetCarPosition()
	{
		return GetCarBody()?.GlobalPosition ?? Vector2.Zero;
	}

	public float GetCarSpeed()
	{
		return GetCurrentSpeed();
	}

	public void ResetCarState()
	{
		_velocity = Vector2.Zero;
		_currentSpeed = 0.0f;
		_targetPosition = Vector2.Zero;
		_lastTargetPosition = Vector2.Zero;
		_hasInput = false;
		_activeFingerId = -1;

		_isInFrictionlessState = false;
		_frictionlessTimeRemaining = 0.0f;
		_lockedVelocity = Vector2.Zero;
		_lockedRotation = 0.0f;

		if (_carBody != null)
		{
			_carBody.Velocity = Vector2.Zero;
		}
	}

	#endregion

	#region Dependency Management

	public void UpdateDependencies(RacingCameraController cameraController, RacingTrackValidationSystem trackValidationSystem)
	{
		_cameraController = cameraController;
		_trackValidationSystem = trackValidationSystem;
	}

	public bool HasValidDependencies()
	{
		return _cameraController != null && _trackValidationSystem != null;
	}

	#endregion

	#region Public Accessors

	public CharacterBody2D GetCarBody() => _carBody;

	public Vector2 GetVelocity() => _velocity;

	public float GetCurrentSpeed() => _currentSpeed;

	public bool HasInput() => _hasInput;

	public Vector2 GetTargetPosition() => _targetPosition;

	public void SetTargetPosition(Vector2 target)
	{
		_targetPosition = target;
		InvalidatePositionCache(); // Target position affects clamped target calculation
	}

	// Cached position accessors (automatically updates cache if dirty)
	public Vector2 CarForward
	{
		get
		{
			UpdatePositionCache();
			return _cachedCarForward;
		}
	}

	public Vector2 CarRight
	{
		get
		{
			UpdatePositionCache();
			return _cachedCarRight;
		}
	}

	public Vector2 CarFrontPosition
	{
		get
		{
			UpdatePositionCache();
			return _cachedCarFrontPosition;
		}
	}

	public Vector2 CarBackPosition
	{
		get
		{
			UpdatePositionCache();
			return _cachedCarBackPosition;
		}
	}

	public Vector2 LeftTirePosition
	{
		get
		{
			UpdatePositionCache();
			return _cachedLeftTirePosition;
		}
	}

	public Vector2 RightTirePosition
	{
		get
		{
			UpdatePositionCache();
			return _cachedRightTirePosition;
		}
	}

	public Vector2 ClampedTargetPosition
	{
		get
		{
			UpdatePositionCache();
			return _cachedClampedTargetPosition;
		}
	}

	public void UpdateHeadlightSettings(bool enabled, float range, float width, Color color)
	{
		HeadlightEnabled = enabled;
		HeadlightRange = range;
		HeadlightWidth = width;
		HeadlightColor = color;

		if (_headlightCone != null && GodotObject.IsInstanceValid(_headlightCone))
		{
			_headlightCone.Visible = enabled;
			if (enabled)
			{
				// Recalculate cone shape with new parameters using cached positions
				var halfWidth = Mathf.DegToRad(width * HEADLIGHT_CONE_HALF_WIDTH_RATIO);
				var carFrontOffset = (CarRight * -CarSize.X * HEADLIGHT_HORIZONTAL_OFFSET_RATIO) + (CarForward * CarSize.Y * HEADLIGHT_VERTICAL_OFFSET_RATIO);
				var vertices = new Vector2[]
				{
					carFrontOffset, // Start at front center of car (using cached calculation)
					carFrontOffset + new Vector2(-range * Mathf.Sin(halfWidth), -range * Mathf.Cos(halfWidth)), // Left edge
					carFrontOffset + new Vector2(range * Mathf.Sin(halfWidth), -range * Mathf.Cos(halfWidth)), // Right edge
				};
				_headlightCone.Polygon = vertices;
				_headlightCone.Color = color;
			}
		}
	}

	#endregion

	#region Cleanup

	public override void _ExitTree()
	{
		if (_inputManager != null && GodotObject.IsInstanceValid(_inputManager))
		{
			_inputManager.TouchStarted -= OnTouchStarted;
			_inputManager.TouchEnded -= OnTouchEnded;
			_inputManager.ClickStarted -= OnClickStarted;
			_inputManager.ClickEnded -= OnClickEnded;
		}

		base._ExitTree();
	}

	#endregion
}
