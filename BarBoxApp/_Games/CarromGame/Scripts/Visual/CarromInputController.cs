using Godot;
using System;
using System.Collections.Generic;

namespace BarBox.Games.Carrom;

/// <summary>
/// Input controller for carrom striker control with deadzone logic
/// </summary>
[GlobalClass]
public partial class CarromInputController : Node2D
{
	[Signal] public delegate void StrikeExecutedEventHandler(Vector2 force);
	[Signal] public delegate void AimingStateChangedEventHandler(bool isAiming, Vector2 aimDirection, float power);
	[Signal] public delegate void StrikerPositionChangedEventHandler(Vector2 newPosition);

	
	// Physics config reference for strike power and angle
	private CarromPhysicsConfig _physicsConfig;

	// Visual parameters - set by CarromGame.cs  
	private float _aimLineLength = 100.0f;
	private float _powerBarWidth = 60.0f;
	private float _powerBarHeight = 8.0f;
	
	// Trajectory prediction parameters
	private float _trajectoryMaxDistance = 300.0f;
	private float _trajectoryPostCollisionDistance = 25.0f;
	private float _trajectoryStepSize = 1f;
	private int _trajectoryMaxSteps = 100;

	[ExportCategory("Visual Feedback")]
	[Export] public bool ShowAimingLine { get; set; } = true;
	[Export] public bool ShowPowerIndicator { get; set; } = true;
	[Export] public Color AimingLineColor { get; set; } = Colors.Yellow;
	[Export] public Color PowerBarColor { get; set; } = Colors.Green;
	[Export] public float AimingLineWidth { get; set; } = 3.0f;
	
	[ExportCategory("Trajectory Prediction")]
	[Export] public float TrajectoryMaxDistance { get; set; } = 300.0f;
	[Export] public float TrajectoryPostCollisionDistance { get; set; } = 25.0f;
	[Export] public Color TrajectoryColor { get; set; } = Colors.Yellow;
	[Export] public Color PostCollisionTrajectoryColor { get; set; } = Colors.Orange;
	[Export] public Color CollisionMarkerColor { get; set; } = Colors.Red;
	[Export] public Color HitPieceTrajectoryColor { get; set; } = Colors.Cyan;
	[Export] public float HitPieceTrajectoryDistance { get; set; } = 150.0f;
	[Export] public bool ShowHitPieceTrajectory { get; set; } = true;
	
	[ExportCategory("Gradient Colors")]
	[Export] public Color PowerGradientLow { get; set; } = Colors.Green;
	[Export] public Color PowerGradientMid { get; set; } = Colors.Yellow;
	[Export] public Color PowerGradientHigh { get; set; } = Colors.Red;
	[Export] public bool UseGradientForAimLine { get; set; } = true;
	
	[ExportCategory("Input Parameters")]
	[Export] public float DeadZone { get; set; } = 40.0f;
	[Export] public float MaxAimDistance { get; set; } = 200.0f;
	[Export] public float LateralSensitivity { get; set; } = 1.5f;
	[Export(PropertyHint.Range, "5.0,90.0,1.0")] public float LateralAngleThreshold { get; set; } = 55.0f;

	[ExportCategory("Deadzone Visualization")]
	[Export] public bool ShowDeadzoneRing { get; set; } = true;
	[Export] public Color DeadzoneRingColor { get; set; } = Colors.Gray;
	[Export] public Color LateralAngleLineColor { get; set; } = Colors.White;
	[Export] public Color LateralAngleArcColor { get; set; } = new Color(1.0f, 1.0f, 1.0f, 0.3f);
	[Export] public Color PowerAimingZoneColor { get; set; } = new Color(0.2f, 0.8f, 0.2f, 0.3f);
	[Export] public Color LateralMovementZoneColor { get; set; } = new Color(0.8f, 0.2f, 0.2f, 0.3f);
	[Export] public float DeadzoneRingWidth { get; set; } = 2.0f;
	[Export] public float LateralAngleLineWidth { get; set; } = 1.5f;
	[Export] public float LateralAngleLineLength { get; set; } = 30.0f;
	[Export] public float ZoneArcRadiusMultiplier { get; set; } = 1.2f;
	[Export] public float ZoneArcWidthMultiplier { get; set; } = 1.5f;
	[Export] public int ZoneArcSegments { get; set; } = 16;
	[Export] public float FeedbackDimmingFactor { get; set; } = 0.6f;

	// Input state
	private CarromPiece _striker;
	private int _activeFingerId = -1;
	private bool _isAiming = false;
	private Vector2 _aimStartPosition;
	private Vector2 _currentAimPosition;
	private Vector2 _strikerStartPosition;
	private InputMode _currentInputMode = InputMode.None;
	
	// Kinematic movement state
	private Vector2 _targetStrikerPosition;
	private bool _isMovingStriker = false;
	private const float STRIKER_MOVE_SPEED = 2500.0f; // Pixels per second
	
	private const float TRAJECTORY_CACHE_DIRECTION_TOLERANCE_SQUARED = 0.000001f;
	private const float TRAJECTORY_CACHE_POWER_TOLERANCE = 0.001f;
	
	// Simplified state management - single source of truth
	private ICarromGameState _gameState;
	
	// Visual update batching
	private bool _needsVisualUpdate = false;
	
	// Coordinate conversion caching
	private Vector2 _lastScreenPosition;
	private Vector2 _lastBoardPosition;
	private bool _hasValidConversionCache = false;
	
	// OPTIMIZATION: Trajectory calculation with pooled buffers
	private Vector2 _lastTrajectoryDirection;
	private float _lastTrajectoryPower;
	private Vector2[] _trajectoryPointsBuffer = new Vector2[200]; // Max trajectory steps
	private int _trajectoryPointCount = 0;
	private Vector2? _cachedCollisionPoint;
	private Vector2[] _postCollisionBuffer = new Vector2[50]; // Post-collision distance
	private int _postCollisionCount = 0;
	private CarromPiece _cachedHitPiece;
	private Vector2[] _hitPieceTrajectoryBuffer = new Vector2[100]; // Hit piece trajectory
	private int _hitPieceTrajectoryCount = 0;
	private bool _trajectoryCacheValid = false;

	// GC OPTIMIZATION: Pre-allocated ray exclusion array to avoid per-raycast allocation
	private readonly Godot.Collections.Array<Rid> _rayExcludeBuffer = new();

	// GC OPTIMIZATION: Reused ray query params to avoid per-step allocation during trajectory aiming
	private readonly PhysicsRayQueryParameters2D _rayQuery = new();

	// Input modes
	private enum InputMode
	{
		None,
		LateralMovement,  // Moving striker along baseline
		PowerAiming       // Aiming for strike power
	}

	// Baseline tracking
	private Vector2[] _baselinePositions = new Vector2[0];
	private int _currentPlayerIndex = 0;
	private CarromBoard _board;
	private CarromCameraController _cameraController;
	
	// Cached baseline geometry for performance optimization
	private Vector2 _cachedBaselineStart;
	private Vector2 _cachedBaselineEnd;
	private Vector2 _cachedBaselineVector;
	private float _cachedBaselineLength;
	private bool _baselineGeometryCached = false;
	
	// Collision feedback state
	private bool _showCollisionFeedback = false;
	private Vector2 _lastTargetPosition = Vector2.Zero;
	private bool _lastPositionWasValid = true;
	
	// Input blocked visual state
	private bool _isInputBlocked = false;
	private float _inputBlockedAlpha = 0.5f;
	private readonly Color INPUT_BLOCKED_COLOR = new Color(0.9f, 0.2f, 0.1f, 0.6f);
	private readonly Color INPUT_READY_COLOR = new Color(0.2f, 0.9f, 0.3f, 0.3f);
	
	// Memory management tracking
	private bool _isDisposed = false;

	public override void _Ready()
	{
		SetProcessInput(true);
		SetPhysicsProcess(true); // Enable physics processing for smooth movement
		SetProcess(true); // Enable process for batched visual updates
		
		// Initialize memory tracking
		_isDisposed = false;
	}
	
	/// <summary>
	/// Handle batched visual updates
	/// </summary>
	public override void _Process(double delta)
	{
		// Batch visual updates to once per frame instead of multiple times per input event
		if (_needsVisualUpdate)
		{
			QueueRedraw();
			_needsVisualUpdate = false;
		}
	}
	
	/// <summary>
	/// Request a visual update - will be batched until next frame
	/// </summary>
	private void RequestVisualUpdate()
	{
		_needsVisualUpdate = true;
	}
	
	/// <summary>
	/// Convert screen position to board position with caching
	/// </summary>
	private Vector2 ScreenToBoardPositionCached(Vector2 screenPosition)
	{
		// Use cache if position hasn't changed significantly (within 2 pixels)
		if (_hasValidConversionCache && _lastScreenPosition.DistanceTo(screenPosition) < 0.25f)
		{
			return _lastBoardPosition;
		}
		
		// Calculate new conversion and cache result
		Vector2 boardPosition = _cameraController?.ScreenToBoardPosition(screenPosition) ?? screenPosition;
		
		_lastScreenPosition = screenPosition;
		_lastBoardPosition = boardPosition;
		_hasValidConversionCache = true;
		
		return boardPosition;
	}
	
	/// <summary>
	/// Handle smooth kinematic striker movement during physics updates
	/// </summary>
	public override void _PhysicsProcess(double delta)
	{
		// Only move striker when input is accepted
		if (_gameState == null || !_gameState.CanAcceptInput)
		{
			return;
		}
		
		if (_isMovingStriker && _striker != null && IsInstanceValid(_striker))
		{
			Vector2 currentPosition = _striker.GlobalPosition;
			Vector2 newPosition = new Vector2(
				Mathf.MoveToward(currentPosition.X, _targetStrikerPosition.X, STRIKER_MOVE_SPEED * (float)delta), 
				Mathf.MoveToward(currentPosition.Y, _targetStrikerPosition.Y, STRIKER_MOVE_SPEED * (float)delta));
			
			_striker.GlobalPosition = newPosition;

			// Ensure physics velocities are zeroed during kinematic movement
			_striker.LinearVelocity = Vector2.Zero;
			_striker.AngularVelocity = 0.0f;

			// FIXED: Use new position for distance calculation and proper completion detection
			float distanceSquaredToTarget = newPosition.DistanceSquaredTo(_targetStrikerPosition);
			if (distanceSquaredToTarget < 1.0f)
			{
				_isMovingStriker = false;
			}
		}
	}

	/// <summary>
	/// Set physics config reference for strike power values
	/// </summary>
	public void SetPhysicsConfig(CarromPhysicsConfig physicsConfig)
	{
		_physicsConfig = physicsConfig;
	}

	/// <summary>
	/// Set visual parameters from CarromGame
	/// </summary>
	public void SetVisualParameters(float aimLineLength, float powerBarWidth, float powerBarHeight)
	{
		_aimLineLength = aimLineLength;
		_powerBarWidth = powerBarWidth;
		_powerBarHeight = powerBarHeight;
		
		// Update trajectory parameters from exports
		_trajectoryMaxDistance = TrajectoryMaxDistance;
		_trajectoryPostCollisionDistance = TrajectoryPostCollisionDistance;
		
		// Invalidate trajectory cache when parameters change
		_trajectoryCacheValid = false;
	}

	/// <summary>
	/// Initialize with board reference - called explicitly by CarromGame
	/// </summary>
	public void InitializeWithBoard(CarromBoard board)
	{
		_board = board;

		if (_board != null)
		{
			UpdateBaselinePositions();
		}
	}

	/// <summary>
	/// Set the camera controller for coordinate transformations
	/// </summary>
	public void SetCameraController(CarromCameraController cameraController)
	{
		_cameraController = cameraController;
	}

	/// <summary>
	/// Set the game state interface
	/// Input controller now polls CanAcceptInput directly instead of using signals
	/// </summary>
	public void SetGameState(ICarromGameState gameState)
	{
		_gameState = gameState;
		RequestVisualUpdate();
	}

	/// <summary>
	/// Update baseline positions for current player and cache geometry
	/// </summary>
	private void UpdateBaselinePositions()
	{
		if (_board != null)
		{
			_baselinePositions = _board.GetBaselinePositions(_currentPlayerIndex);
			CacheBaselineGeometry();
		}
	}
	
	/// <summary>
	/// Cache baseline geometry for fast position calculations
	/// </summary>
	private void CacheBaselineGeometry()
	{
		if (_board == null)
		{
			_baselineGeometryCached = false;
			return;
		}
		
		// Get baseline center and geometry from board
		Vector2 baselineCenter = _board.GetBaselinePosition(_currentPlayerIndex);
		float baselineLength = _board.BaselineLength - (_board.BaselineCapRadius * 2);
		Vector2 baselineDirection = GetBaselineDirection();
		
		// Calculate baseline endpoints using actual board geometry
		float halfLength = baselineLength / 2;
		_cachedBaselineStart = baselineCenter - baselineDirection * halfLength;
		_cachedBaselineEnd = baselineCenter + baselineDirection * halfLength;
		_cachedBaselineVector = _cachedBaselineEnd - _cachedBaselineStart;
		_cachedBaselineLength = _cachedBaselineVector.LengthSquared(); // Store squared length for dot product optimization
		
		_baselineGeometryCached = true;
	}
	
	/// <summary>
	/// Fast baseline position calculation using cached geometry
	/// </summary>
	private Vector2 GetClosestPositionOnBaselineCached(Vector2 position)
	{
		if (!_baselineGeometryCached)
		{
			// Fallback to board method if cache not available
			return _board?.GetClosestPositionOnBaseline(position, _currentPlayerIndex) ?? position;
		}
		
		// Project position onto baseline line segment using cached values
		Vector2 positionVector = position - _cachedBaselineStart;
		
		// Calculate projection parameter (0.0 = start, 1.0 = end)
		float projectionParam = positionVector.Dot(_cachedBaselineVector) / _cachedBaselineLength;
		
		// Clamp to baseline segment bounds
		projectionParam = Mathf.Clamp(projectionParam, 0.0f, 1.0f);
		
		// Calculate final projected position
		return _cachedBaselineStart + _cachedBaselineVector * projectionParam;
	}

	/// <summary>
	/// Handle input events with comprehensive debugging
	/// </summary>
	public override void _Input(InputEvent @event)
	{
		if (!CanAcceptInput() || _striker == null) return;

		HandleMouseInput(@event);
		HandleTouchInput(@event);
	}

	/// <summary>
	/// Handle mouse input
	/// </summary>
	private void HandleMouseInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseButton)
		{
			if (mouseButton.ButtonIndex == MouseButton.Left)
			{
				if (mouseButton.Pressed)
				{
					StartInput(mouseButton.GlobalPosition);
				}
				else
				{
					EndInput();
				}
			}
		}
		else if (@event is InputEventMouseMotion mouseMotion && _isAiming)
		{
			UpdateInput(mouseMotion.GlobalPosition);
		}
	}

	/// <summary>
	/// Handle touch input
	/// </summary>
	private void HandleTouchInput(InputEvent @event)
	{
		if (@event is InputEventScreenTouch touch)
		{
			if (touch.Pressed)
			{
				if (_activeFingerId == -1)
				{
					_activeFingerId = touch.Index;
					StartInput(touch.Position);
				}
			}
			else if (touch.Index == _activeFingerId)
			{
				_activeFingerId = -1;
				EndInput();
			}
		}
		else if (@event is InputEventScreenDrag drag && _isAiming && drag.Index == _activeFingerId)
		{
			UpdateInput(drag.Position);
		}
	}

	/// <summary>
	/// Start input handling
	/// </summary>
	private void StartInput(Vector2 screenPosition)
	{
		if (!CanStartInput()) 
			return;

		// Convert screen position to board coordinates using cached conversion
		Vector2 boardPosition = ScreenToBoardPositionCached(screenPosition);
		Vector2 strikerPosition = _striker.GlobalPosition; // Striker is already in board coordinates
		
		// Check if input is near striker
		float strikerRadius = _striker.PhysicsConfig?.GetRadiusForPieceType(_striker.Type) ?? 15.0f;
		if (strikerPosition.DistanceTo(boardPosition) > strikerRadius + 50.0f)
		{
			return; // Input too far from striker
		}

		_isAiming = true;
		_aimStartPosition = boardPosition;
		_strikerStartPosition = _striker.GlobalPosition;
		_currentAimPosition = boardPosition;
		_currentInputMode = InputMode.None;

		RequestVisualUpdate(); // Show aiming indicators
	}

	/// <summary>
	/// Update input during drag
	/// </summary>
	private void UpdateInput(Vector2 screenPosition)
	{
		// Convert screen position to board coordinates using cached conversion
		_currentAimPosition = ScreenToBoardPositionCached(screenPosition);
		
		Vector2 inputVector = _currentAimPosition - _aimStartPosition;
		float inputDistance = inputVector.Length();

		// Determine input mode based on movement direction
		if (_currentInputMode == InputMode.None && inputDistance > DeadZone)
		{
			DetermineInputMode(inputVector);
		}

		// Handle input based on current mode
		switch (_currentInputMode)
		{
			case InputMode.LateralMovement:
				HandleLateralMovement(inputVector);
				break;
			case InputMode.PowerAiming:
				HandlePowerAiming(inputVector);
				// Invalidate trajectory cache when direction changes
				InvalidateTrajectoryCache();
				break;
		}

		RequestVisualUpdate(); // Update visual feedback
	}
	
	/// <summary>
	/// Invalidate trajectory cache to force recalculation
	/// </summary>
	private void InvalidateTrajectoryCache()
	{
		_trajectoryCacheValid = false;
	}

	/// <summary>
	/// Determine input mode based on initial movement direction relative to baseline.
	/// Uses lateral angle threshold to distinguish between striker positioning (lateral movement)
	/// and strike power aiming (forward/backward movement along the baseline direction).
	/// 
	/// Input vectors within the threshold angle are treated as power aiming,
	/// while vectors beyond the threshold are treated as lateral positioning.
	/// </summary>
	/// <param name="inputVector">Raw input movement vector from touch/mouse</param>
	private void DetermineInputMode(Vector2 inputVector)
	{
		Vector2 baselineDirection = -GetForwardDirection();
		Vector2 normalizedInput = inputVector.Normalized();
		
		// Calculate angle from baseline direction to input vector
		float angleFromBaseline = Mathf.Abs(baselineDirection.AngleTo(normalizedInput));
		float angleFromBaselineDegrees = Mathf.RadToDeg(angleFromBaseline);
		
		// Check if input angle is greater than lateral threshold
		bool isLateralInput = angleFromBaselineDegrees > LateralAngleThreshold;
		if (isLateralInput)
		{
			_currentInputMode = InputMode.LateralMovement;
		}
		else
		{
			// Stop kinematic movement when switching to power aiming
			if (_isMovingStriker)
			{
				_isMovingStriker = false;
				// Ensure striker physics are ready for force application
				if (_striker != null && IsInstanceValid(_striker))
				{
					_striker.LinearVelocity = Vector2.Zero;
					_striker.AngularVelocity = 0.0f;
				}
			}
			_currentInputMode = InputMode.PowerAiming;
		}
	}

	/// <summary>
	/// Handle lateral striker movement along baseline with collision detection
	/// </summary>
	private void HandleLateralMovement(Vector2 inputVector)
	{
		if (_board == null || _striker == null) 
			return;
		
		// Use cached geometry for fast position calculation
		Vector2 targetPosition = GetClosestPositionOnBaselineCached(_currentAimPosition);
		float strikerRadius = _striker.PhysicsConfig?.GetRadiusForPieceType(_striker.Type) ?? 15.0f;
		
		// Check if original target position would be obstructed
		bool originalPositionValid = !_board.IsPositionObstructed(targetPosition, strikerRadius, _striker);
		
		// Check if target position is obstructed and find nearest valid position
		Vector2? validPosition = _board.FindNearestValidPositionOnBaseline(
			targetPosition, _currentPlayerIndex, strikerRadius, _striker);
		
		// Update collision feedback state
		_showCollisionFeedback = true;
		_lastTargetPosition = targetPosition;
		_lastPositionWasValid = originalPositionValid;
		
		if (validPosition.HasValue)
		{
			// Use kinematic movement to valid position
			StartKinematicMovement(validPosition.Value);
			EmitSignal(SignalName.StrikerPositionChanged, validPosition.Value);
		}
		else
		{
			// No valid position found - keep striker at current position
			// This should be rare but provides graceful handling
			GD.PrintErr($"[CarromInputController] No valid baseline position found for player {_currentPlayerIndex}");
			_lastPositionWasValid = false;
		}
		
		// Update aiming feedback - no aiming direction for lateral movement
		Vector2 aimDirection = Vector2.Zero;
		float power = 0.0f;
		EmitSignal(SignalName.AimingStateChanged, false, aimDirection, power);
	}
	
	/// <summary>
	/// Start smooth kinematic movement to target position
	/// </summary>
	private void StartKinematicMovement(Vector2 targetPosition)
	{
		_targetStrikerPosition = targetPosition;
		_isMovingStriker = true;
	}

	/// <summary>
	/// Handle power aiming for strike
	/// </summary>
	private void HandlePowerAiming(Vector2 inputVector)
	{
		// Ensure kinematic movement is disabled during power aiming
		if (_isMovingStriker)
		{
			_isMovingStriker = false;
		}
		
		// Calculate power based on total input distance
		float inputDistance = inputVector.Length();
		float clampedDistance = Mathf.Clamp(inputDistance, 0, MaxAimDistance);
		float power = clampedDistance / MaxAimDistance;
		
		// Calculate aim direction from input vector, then clamp to valid angle range
		Vector2 rawAimDirection = -inputVector.Normalized(); // Opposite of pull direction
		Vector2 forwardDirection = GetForwardDirection();
		
		// Clamp aim direction to valid angle range (±maxStrikeAngle from forward)
		float maxAngleRad = Mathf.DegToRad(GetMaxStrikeAngle());
		float currentAngle = forwardDirection.AngleTo(rawAimDirection);
		
		Vector2 aimDirection;
		if (Mathf.Abs(currentAngle) <= maxAngleRad)
		{
			// Within valid range, use raw direction
			aimDirection = rawAimDirection;
		}
		else
		{
			// Clamp to nearest valid angle
			float clampedAngle = Mathf.Sign(currentAngle) * maxAngleRad;
			aimDirection = forwardDirection.Rotated(clampedAngle);
		}
		
		EmitSignal(SignalName.AimingStateChanged, true, aimDirection, power);
	}

	/// <summary>
	/// End input and execute strike if applicable
	/// </summary>
	private void EndInput()
	{
		if (!_isAiming) return;

		if (_currentInputMode == InputMode.PowerAiming)
		{
			ExecuteStrike();
		}

		// Clear all input and movement states
		_isAiming = false;
		_activeFingerId = -1;
		_currentInputMode = InputMode.None;
		
		// Stop any ongoing kinematic movement
		_isMovingStriker = false;
		
		// Clear trajectory cache
		InvalidateTrajectoryCache();
		
		// Clear collision feedback state
		_showCollisionFeedback = false;
		
		// Clear visual feedback and emit state change signals
		EmitSignal(SignalName.AimingStateChanged, false, Vector2.Zero, 0.0f);
		RequestVisualUpdate(); // Hide aiming indicators
	}

	/// <summary>
	/// Execute striker strike
	/// </summary>
	private void ExecuteStrike()
	{
		Vector2 inputVector = _currentAimPosition - _aimStartPosition;
		float inputDistance = inputVector.Length();

		if (!(inputDistance > DeadZone))
			return;

		float clampedDistance = Mathf.Clamp(inputDistance, DeadZone, MaxAimDistance);
		float powerRatio = (clampedDistance - DeadZone) / (MaxAimDistance - DeadZone);
		float strikePower = Mathf.Lerp(GetMinStrikePower(), GetMaxStrikePower(), powerRatio);
			
		// Calculate strike direction with angle clamping (same as HandlePowerAiming)
		Vector2 rawStrikeDirection = -inputVector.Normalized(); // Opposite of pull direction
		Vector2 forwardDirection = GetForwardDirection();
			
		// Clamp strike direction to valid angle range (±maxStrikeAngle from forward)
		float maxAngleRad = Mathf.DegToRad(GetMaxStrikeAngle());
		float currentAngle = forwardDirection.AngleTo(rawStrikeDirection);
			
		Vector2 strikeDirection;
		if (Mathf.Abs(currentAngle) <= maxAngleRad)
		{
			// Within valid range, use raw direction
			strikeDirection = rawStrikeDirection;
		}
		else
		{
			// Clamp to nearest valid angle
			float clampedAngle = Mathf.Sign(currentAngle) * maxAngleRad;
			strikeDirection = forwardDirection.Rotated(clampedAngle);
		}
			
		Vector2 strikeForce = strikeDirection * strikePower;
			
		// Emit signal first to apply force before state transition
		EmitSignal(SignalName.StrikeExecuted, strikeForce);
			
		// Defer state machine notification to ensure force is applied
		CallDeferred(MethodName.NotifyStrikeExecuted);
	}

	/// <summary>
	/// Deferred method to notify state machine after force application
	/// </summary>
	private void NotifyStrikeExecuted()
	{
		_gameState?.OnStrikeExecuted();
	}

	/// <summary>
	/// Get baseline direction for current player
	/// </summary>
	private Vector2 GetBaselineDirection()
	{
		// Player 0,1 use horizontal baselines (left-right movement)
		// Player 2,3 use vertical baselines (up-down movement)
		bool isHorizontalBaseline = _currentPlayerIndex <= 1;
		return isHorizontalBaseline ? Vector2.Right : Vector2.Down;
	}

	/// <summary>
	/// Get forward direction toward board center
	/// </summary>
	private Vector2 GetForwardDirection()
	{
		// Direction toward board center from baseline
		return _currentPlayerIndex switch
		{
			0 => Vector2.Up,    // Player 1: move up toward center
			1 => Vector2.Down,  // Player 2: move down toward center
			2 => Vector2.Right, // Player 3: move right toward center
			3 => Vector2.Left,  // Player 4: move left toward center
			_ => Vector2.Up
		};
	}



	/// <summary>
	/// Check if input can be started
	/// </summary>
	private bool CanStartInput()
	{
		bool hasStriker = _striker != null;
		bool strikerStopped = hasStriker && _striker.IsStopped();
		bool canAccept = CanAcceptInput();
		
		// Input validation - early exit conditions
		if (!hasStriker)
		{
			// No striker available - wait for striker to be set
			return false;
		}
		else if (!strikerStopped)
		{
			// Striker still moving - wait for it to stop
			return false;
		}
		else if (!canAccept)
		{
			// Phase manager blocking input - wait for active phase
			return false;
		}
		
		return hasStriker && strikerStopped && canAccept;
	}
	
	/// <summary>
	/// Check if input can be accepted - polls game state directly
	/// </summary>
	private bool CanAcceptInput()
	{
		// Poll game state directly instead of relying on signals
		// This works with GameStateManager
		bool phaseReady = _gameState?.CanAcceptInput ?? false;

		// Validate striker is ready to accept input
		bool strikerValid = _striker != null &&
							GodotObject.IsInstanceValid(_striker) &&
							_striker.Visible &&
							!_striker.Freeze;

		return phaseReady && strikerValid;
	}

	/// <summary>
	/// Custom drawing for visual feedback
	/// </summary>
	public override void _Draw()
	{
		// Draw input blocked overlay if input is blocked
		if (_striker != null && IsInstanceValid(_striker))
		{
			DrawInputStateIndicator();
		}
		
		if (!_isAiming || _striker == null) return;

		// Since board is at origin, striker positions are already in the right coordinate space
		Vector2 strikerPos = _striker.GlobalPosition;
		Vector2 currentPos = _currentAimPosition;

		// Draw deadzone ring when input is in deadzone (before mode is determined)
		if (_currentInputMode == InputMode.None)
		{
			Vector2 inputVector = currentPos - _aimStartPosition;
			float inputDistance = inputVector.Length();
			
			if (inputDistance <= DeadZone)
			{
				DrawDeadzoneRing(strikerPos);
			}
		}

		// Draw collision feedback for lateral movement
		if (_currentInputMode == InputMode.LateralMovement && _showCollisionFeedback)
		{
			DrawCollisionFeedback();
		}

		if (_currentInputMode == InputMode.PowerAiming && ShowAimingLine)
		{
			DrawAimingLine(strikerPos, currentPos);
		}

		if (_currentInputMode == InputMode.PowerAiming && ShowPowerIndicator)
		{
			DrawPowerIndicator(strikerPos, currentPos);
		}
	}

	/// <summary>
	/// Draw aiming line with trajectory prediction
	/// </summary>
	private void DrawAimingLine(Vector2 strikerPos, Vector2 currentPos)
	{
		Vector2 inputVector = currentPos - _aimStartPosition;
		float inputDistance = inputVector.Length();
		
		// Only draw dotted line when outside the deadzone
		if (inputDistance > DeadZone)
		{
			float clampedDistance = Mathf.Clamp(inputDistance, 0, MaxAimDistance);
			Vector2 clampedInputPos = _aimStartPosition + inputVector.Normalized() * clampedDistance;
			
			// Calculate power for color matching
			float power = Mathf.Clamp((inputDistance - DeadZone) / (MaxAimDistance - DeadZone), 0.0f, 1.0f);
			
			// Start line from opposite edge of striker (offset by radius)
			Vector2 lineDirection = (clampedInputPos - strikerPos).Normalized();
			float strikerRadius = _striker.PhysicsConfig?.GetRadiusForPieceType(_striker.Type) ?? 15.0f;
			Vector2 lineStart = strikerPos + lineDirection * strikerRadius;
			
			if (UseGradientForAimLine)
			{
				DrawGradientDottedLine(lineStart, clampedInputPos, power, AimingLineWidth);
			}
			else
			{
				Color lineColor = power > 0.8f ? Colors.Red : 
								 power > 0.5f ? Colors.Yellow : PowerBarColor;
				DrawDottedLine(lineStart, clampedInputPos, lineColor, AimingLineWidth);
			}
		}
		
		if (inputDistance > DeadZone)
		{
			// Calculate aim direction with angle clamping (same as HandlePowerAiming)
			Vector2 rawAimDirection = -inputVector.Normalized(); // Opposite of pull direction
			Vector2 forwardDirection = GetForwardDirection();
			
			// Clamp aim direction to valid angle range (±maxStrikeAngle from forward)
			float maxAngleRad = Mathf.DegToRad(GetMaxStrikeAngle());
			float currentAngle = forwardDirection.AngleTo(rawAimDirection);
			
			Vector2 aimDirection;
			if (Mathf.Abs(currentAngle) <= maxAngleRad)
			{
				// Within valid range, use raw direction
				aimDirection = rawAimDirection;
			}
			else
			{
				// Clamp to nearest valid angle
				float clampedAngle = Mathf.Sign(currentAngle) * maxAngleRad;
				aimDirection = forwardDirection.Rotated(clampedAngle);
			}
			
			// Calculate power level
			float clampedDistance = Mathf.Clamp(inputDistance, DeadZone, MaxAimDistance);
			float power = (clampedDistance - DeadZone) / (MaxAimDistance - DeadZone);

			// Get trajectory prediction - populates class-level buffers
			var trajectory = CalculateTrajectoryPoints(strikerPos, aimDirection, power);

			// GC OPTIMIZATION: Draw directly from class-level buffers using counts
			DrawTrajectoryPath(_trajectoryPointsBuffer, trajectory.trajectoryCount, TrajectoryColor);

			// Draw collision marker if there's a collision
			if (trajectory.collisionPoint.HasValue)
			{
				DrawCollisionMarker(trajectory.collisionPoint.Value);
			}

			// Draw post-collision trajectory
			if (trajectory.postCollisionCount > 0)
			{
				DrawTrajectoryPath(_postCollisionBuffer, trajectory.postCollisionCount, PostCollisionTrajectoryColor);
			}

			// Draw hit piece trajectory if available
			if (ShowHitPieceTrajectory && trajectory.hitPieceCount > 0)
			{
				DrawHitPieceTrajectory(_hitPieceTrajectoryBuffer, trajectory.hitPieceCount, HitPieceTrajectoryColor);
			}
		}
	}
	
	/// <summary>
	/// Draw trajectory path as connected line segments
	/// GC OPTIMIZATION: Overload that accepts buffer + count to avoid array allocation
	/// </summary>
	private void DrawTrajectoryPath(Vector2[] buffer, int count, Color color)
	{
		if (count < 2)
			return;

		// Draw individual lines from buffer to avoid creating a new array for DrawPolyline
		for (int i = 0; i < count - 1; i++)
		{
			DrawLine(buffer[i], buffer[i + 1], color, AimingLineWidth, true);
		}
	}

	/// <summary>
	/// Draw trajectory path as connected line segments (legacy overload)
	/// </summary>
	private void DrawTrajectoryPath(Vector2[] points, Color color)
	{
		if (points.Length < 2)
			return;

		// Draw as solid polyline for main trajectory
		DrawPolyline(points, color, AimingLineWidth, true);
	}
	
	/// <summary>
	/// Draw collision marker
	/// </summary>
	private void DrawCollisionMarker(Vector2 position)
	{
		float markerRadius = 8.0f;
		DrawArc(position, markerRadius, 0, Mathf.Tau, 16, CollisionMarkerColor, AimingLineWidth * 1.5f, true);
	}

	/// <summary>
	/// Draw dotted line between two points with consistent dash sizes
	/// </summary>
	private void DrawDottedLine(Vector2 from, Vector2 to, Color color, float width)
	{
		// Draw manually to ensure consistent dash sizes regardless of line length
		float dashLength = 8.0f;
		float gapLength = 4.0f;
		float totalPattern = dashLength + gapLength;
		
		Vector2 direction = (to - from).Normalized();
		float totalDistance = from.DistanceTo(to);
		
		// Calculate how many complete dash-gap patterns we can fit
		int completePatterns = Mathf.FloorToInt(totalDistance / totalPattern);
		float remainingDistance = totalDistance - (completePatterns * totalPattern);
		
		// Draw complete patterns
		for (int i = 0; i < completePatterns; i++)
		{
			Vector2 dashStart = from + direction * (i * totalPattern);
			Vector2 dashEnd = dashStart + direction * dashLength;
			DrawLine(dashStart, dashEnd, color, width, true);
		}
		
		// Draw final dash if there's remaining space
		if (remainingDistance > 0)
		{
			Vector2 finalDashStart = from + direction * (completePatterns * totalPattern);
			Vector2 finalDashEnd = finalDashStart + direction * Mathf.Min(dashLength, remainingDistance);
			DrawLine(finalDashStart, finalDashEnd, color, width, true);
		}
	}
	
	/// <summary>
	/// Draw gradient dotted line between two points with consistent dash sizes
	/// </summary>
	private void DrawGradientDottedLine(Vector2 from, Vector2 to, float power, float width)
	{
		// Draw dashes manually with gradient colors and consistent sizing
		float dashLength = 8.0f;
		float gapLength = 4.0f;
		float totalPattern = dashLength + gapLength;
		
		Vector2 direction = (to - from).Normalized();
		float totalDistance = from.DistanceTo(to);
		
		// Calculate how many complete dash-gap patterns we can fit
		int completePatterns = Mathf.FloorToInt(totalDistance / totalPattern);
		float remainingDistance = totalDistance - (completePatterns * totalPattern);
		
		// Draw complete patterns with gradient
		for (int i = 0; i < completePatterns; i++)
		{
			float patternStart = i * totalPattern;
			float patternMid = patternStart + (dashLength / 2.0f);
			
			// Calculate gradient position for this dash
			float gradientT = patternMid / totalDistance;
			float colorT = Mathf.Lerp(0.0f, power, gradientT);
			Color dashColor = GetPowerGradientColor(colorT);
			
			Vector2 dashStart = from + direction * patternStart;
			Vector2 dashEnd = dashStart + direction * dashLength;
			DrawLine(dashStart, dashEnd, dashColor, width, true);
		}
		
		// Draw final dash if there's remaining space
		if (remainingDistance > 0)
		{
			float finalDashLength = Mathf.Min(dashLength, remainingDistance);
			float finalPatternStart = completePatterns * totalPattern;
			float finalPatternMid = finalPatternStart + (finalDashLength / 2.0f);
			
			// Calculate gradient for final dash
			float gradientT = finalPatternMid / totalDistance;
			float colorT = Mathf.Lerp(0.0f, power, gradientT);
			Color dashColor = GetPowerGradientColor(colorT);
			
			Vector2 finalDashStart = from + direction * finalPatternStart;
			Vector2 finalDashEnd = finalDashStart + direction * finalDashLength;
			DrawLine(finalDashStart, finalDashEnd, dashColor, width, true);
		}
	}
	
	/// <summary>
	/// Get gradient color based on power level
	/// </summary>
	private Color GetPowerGradientColor(float power)
	{
		if (power <= 0.5f)
		{
			// Interpolate between low and mid colors
			float t = power * 2.0f; // Scale 0-0.5 to 0-1
			return PowerGradientLow.Lerp(PowerGradientMid, t);
		}
		else
		{
			// Interpolate between mid and high colors
			float t = (power - 0.5f) * 2.0f; // Scale 0.5-1 to 0-1
			return PowerGradientMid.Lerp(PowerGradientHigh, t);
		}
	}
	
	/// <summary>
	/// Draw hit piece trajectory as dotted line with fading
	/// GC OPTIMIZATION: Overload that accepts buffer + count to avoid array allocation
	/// </summary>
	private void DrawHitPieceTrajectory(Vector2[] buffer, int count, Color color)
	{
		if (count < 2)
			return;

		// Define consistent dash pattern for hit piece
		float dashLength = 5.0f;
		float gapLength = 3.0f;
		float totalPattern = dashLength + gapLength;

		// Draw trajectory segments with fading alpha
		for (int i = 0; i < count - 1; i++)
		{
			Vector2 segmentStart = buffer[i];
			Vector2 segmentEnd = buffer[i + 1];
			Vector2 direction = (segmentEnd - segmentStart).Normalized();
			float segmentLength = segmentStart.DistanceTo(segmentEnd);

			// Add transparency fade based on distance from collision
			float fadeAlpha = 1.0f - (i * 0.1f); // Gradually fade trajectory
			fadeAlpha = Mathf.Max(fadeAlpha, 0.2f); // Keep minimum visibility
			Color fadedColor = new Color(color.R, color.G, color.B, color.A * fadeAlpha);

			// Draw dashes with consistent sizing
			int dashCount = Mathf.FloorToInt(segmentLength / totalPattern);
			for (int j = 0; j < dashCount; j++)
			{
				Vector2 dashStart = segmentStart + direction * (j * totalPattern);
				Vector2 dashEnd = dashStart + direction * dashLength;
				DrawLine(dashStart, dashEnd, fadedColor, AimingLineWidth * 0.8f, true);
			}

			// Draw final partial dash if needed
			float remaining = segmentLength - (dashCount * totalPattern);
			if (remaining > 0)
			{
				Vector2 finalDashStart = segmentStart + direction * (dashCount * totalPattern);
				Vector2 finalDashEnd = finalDashStart + direction * Mathf.Min(dashLength, remaining);
				DrawLine(finalDashStart, finalDashEnd, fadedColor, AimingLineWidth * 0.8f, true);
			}
		}
	}

	/// <summary>
	/// Draw hit piece trajectory as dotted line with fading (legacy overload)
	/// </summary>
	private void DrawHitPieceTrajectory(Vector2[] points, Color color)
	{
		DrawHitPieceTrajectory(points, points.Length, color);
	}
	
	/// <summary>
	/// Draw deadzone ring with lateral angle indicators
	/// </summary>
	private void DrawDeadzoneRing(Vector2 strikerPos)
	{
		if (!ShowDeadzoneRing) 
			return;
		
		// Draw deadzone ring
		DrawArc(strikerPos, DeadZone, 0, Mathf.Tau, 32, DeadzoneRingColor, DeadzoneRingWidth, true);
		
		// Get forward direction (toward board center)
		Vector2 forwardDirection = GetForwardDirection();
		
		// Calculate lateral angle threshold lines - these should be on the forward side
		float lateralAngleRad = Mathf.DegToRad(LateralAngleThreshold);
		
		// Left and right angle lines rotated from the forward direction (not offset from it)
		Vector2 leftAngleDirection = forwardDirection.Rotated(-lateralAngleRad);
		Vector2 rightAngleDirection = forwardDirection.Rotated(lateralAngleRad);
		
		// Draw dashed angle indicator lines extending from deadzone ring
		Vector2 leftLineEnd = strikerPos - leftAngleDirection * (DeadZone + LateralAngleLineLength);
		Vector2 rightLineEnd = strikerPos - rightAngleDirection * (DeadZone + LateralAngleLineLength);
		Vector2 leftLineStart = strikerPos - leftAngleDirection * DeadZone;
		Vector2 rightLineStart = strikerPos - rightAngleDirection * DeadZone;

		// Use dotted lines for angle indicators with consistent dash sizes
		DrawDottedLine(leftLineStart, leftLineEnd, LateralAngleLineColor, LateralAngleLineWidth);
		DrawDottedLine(rightLineStart, rightLineEnd, LateralAngleLineColor, LateralAngleLineWidth);

		// Draw translucent arcs to indicate the input behavior zones
		float forwardAngle = forwardDirection.Angle();

		// Power aiming zone: backward swipes (toward forward direction) within lateral threshold
		float powerAimingStartAngle = -forwardAngle - lateralAngleRad;
		float powerAimingEndAngle = -forwardAngle + lateralAngleRad;

		// Calculate colors based on current input direction within deadzone
		Color powerAimingArcColor = PowerAimingZoneColor;
		Color lateralMovementArcColor = LateralMovementZoneColor;
		
		// Check if currently in deadzone with active input
		Vector2 inputVector = _currentAimPosition - _aimStartPosition;
		float inputDistance = inputVector.Length();
		if (inputDistance > 0 && inputDistance <= DeadZone)
		{
			Vector2 baselineDirection = -GetForwardDirection();
			Vector2 normalizedInput = inputVector.Normalized();
			
			// Calculate angle from baseline direction to input vector
			float angleFromBaseline = Mathf.Abs(baselineDirection.AngleTo(normalizedInput));
			float angleFromBaselineDegrees = Mathf.RadToDeg(angleFromBaseline);
			
			// Check which direction input is more angled towards
			bool isLateralInput = angleFromBaselineDegrees > LateralAngleThreshold;
			if (isLateralInput)
			{
				// Input is in lateral zone - darken lateral movement arc
				lateralMovementArcColor = new Color(LateralMovementZoneColor.R * FeedbackDimmingFactor, LateralMovementZoneColor.G * FeedbackDimmingFactor, LateralMovementZoneColor.B * FeedbackDimmingFactor, LateralMovementZoneColor.A * FeedbackDimmingFactor);
			}
			else
			{
				// Input is in power aiming zone - darken power aiming arc
				powerAimingArcColor = new Color(PowerAimingZoneColor.R * FeedbackDimmingFactor, PowerAimingZoneColor.G * FeedbackDimmingFactor, PowerAimingZoneColor.B * FeedbackDimmingFactor, PowerAimingZoneColor.A * FeedbackDimmingFactor);
			}
		}

		// Draw power aiming zone arc (backward swipes toward board center)
		DrawArc(strikerPos, DeadZone * ZoneArcRadiusMultiplier, powerAimingStartAngle, powerAimingEndAngle, ZoneArcSegments, powerAimingArcColor, DeadZone * ZoneArcWidthMultiplier, true);
		DrawArc(strikerPos, DeadZone * ZoneArcRadiusMultiplier, powerAimingEndAngle, powerAimingStartAngle + Mathf.Tau, ZoneArcSegments, lateralMovementArcColor, DeadZone * ZoneArcWidthMultiplier, true);
	}

	/// <summary>
	/// Draw power indicator
	/// </summary>
	private void DrawPowerIndicator(Vector2 strikerPos, Vector2 currentPos)
	{
		Vector2 inputVector = currentPos - _aimStartPosition;
		float inputDistance = inputVector.Length();

		if (!(inputDistance > DeadZone))
			return;

		float clampedDistance = Mathf.Clamp(inputDistance, DeadZone, MaxAimDistance);
		float power = (clampedDistance - DeadZone) / (MaxAimDistance - DeadZone);
			
		// Draw power bar
		Vector2 barPosition = strikerPos + Vector2.Up * 40;
		float barWidth = _powerBarWidth;
		float barHeight = _powerBarHeight;
			
		// Background
		DrawRect(new Rect2(barPosition - Vector2.Right * barWidth / 2, 
			new Vector2(barWidth, barHeight)), Colors.Gray, true, -1f, true);
			
		// Power fill
		Color powerColor = power > 0.8f ? Colors.Red : 
			power > 0.5f ? Colors.Yellow : PowerBarColor;
		DrawRect(new Rect2(barPosition - Vector2.Right * barWidth / 2, 
			new Vector2(barWidth * power, barHeight)), powerColor, true, -1f, true);
	}
	
	/// <summary>
	/// Draw input state indicator around striker
	/// </summary>
	private void DrawInputStateIndicator()
	{
		// Input state feedback is now handled entirely by the score bar
		// No visual indicators around the striker to avoid gameplay confusion
	}
	
	/// <summary>
	/// Draw collision feedback for lateral movement
	/// </summary>
	private void DrawCollisionFeedback()
	{
		if (_striker == null) return;
		
		float strikerRadius = _striker.PhysicsConfig?.GetRadiusForPieceType(_striker.Type) ?? 15.0f;
		
		// Draw indicator at the last target position
		Color indicatorColor = _lastPositionWasValid ? Colors.Green : Colors.Red;
		Color ringColor = _lastPositionWasValid ? new Color(0.0f, 1.0f, 0.0f, 0.3f) : new Color(1.0f, 0.0f, 0.0f, 0.3f);
		
		// Draw filled circle to show collision area
		DrawCircle(_lastTargetPosition, strikerRadius, ringColor);
		
		// Draw outline ring
		DrawArc(_lastTargetPosition, strikerRadius, 0, Mathf.Tau, 32, indicatorColor, 3.0f, true);
		
		// If position was invalid, draw an X to indicate collision
		if (!_lastPositionWasValid)
		{
			float crossSize = strikerRadius * 0.6f;
			Vector2 offset1 = new Vector2(crossSize, crossSize);
			Vector2 offset2 = new Vector2(crossSize, -crossSize);
			
			DrawLine(_lastTargetPosition - offset1, _lastTargetPosition + offset1, Colors.Red, 4.0f);
			DrawLine(_lastTargetPosition - offset2, _lastTargetPosition + offset2, Colors.Red, 4.0f);
		}
		else
		{
			// If position was valid, draw a checkmark
			float checkSize = strikerRadius * 0.4f;
			Vector2 checkStart = _lastTargetPosition + new Vector2(-checkSize * 0.5f, 0);
			Vector2 checkMid = _lastTargetPosition + new Vector2(-checkSize * 0.2f, checkSize * 0.3f);
			Vector2 checkEnd = _lastTargetPosition + new Vector2(checkSize * 0.5f, -checkSize * 0.3f);
			
			DrawLine(checkStart, checkMid, Colors.Green, 4.0f, true);
			DrawLine(checkMid, checkEnd, Colors.Green, 4.0f, true);
		}
	}

	/// <summary>
	/// Set the striker for input handling
	/// </summary>
	public void SetStriker(CarromPiece striker)
	{
		_striker = striker;
	}

	/// <summary>
	/// Set current player for baseline calculations
	/// </summary>
	public void SetCurrentPlayer(int playerIndex)
	{
		if (_currentPlayerIndex == playerIndex)
			return;

		_currentPlayerIndex = playerIndex;
		UpdateBaselinePositions(); // Update immediately
		InvalidateTrajectoryCache(); // Player change affects trajectory calculation
		GD.Print($"[CarromInputController] Switched to player {playerIndex}, baseline updated immediately");
	}
	
	/// <summary>
	/// Check if currently aiming
	/// </summary>
	public bool IsAiming()
	{
		return _isAiming;
	}

	/// <summary>
	/// Get current input mode
	/// </summary>
	public string GetCurrentInputMode()
	{
		return _currentInputMode.ToString();
	}

	// Helper methods for physics config access
	private float GetMaxStrikePower() => _physicsConfig?.MaxStrikePower ?? 2000.0f;
	private float GetMinStrikePower() => _physicsConfig?.MinStrikePower ?? 200.0f;
	private float GetMaxStrikeAngle() => _physicsConfig?.MaxStrikeAngle ?? 60.0f;
	
	/// <summary>
	/// Calculate trajectory points with collision detection (constant distance)
	/// GC OPTIMIZATION: Populates class-level buffers and returns counts instead of allocating new arrays
	/// Use class-level buffers (_trajectoryPointsBuffer, _postCollisionBuffer, _hitPieceTrajectoryBuffer)
	/// and counts (_trajectoryPointCount, _postCollisionCount, _hitPieceTrajectoryCount) after calling
	/// </summary>
	private (int trajectoryCount, Vector2? collisionPoint, int postCollisionCount, int hitPieceCount) CalculateTrajectoryPoints(Vector2 startPos, Vector2 direction, float power)
	{
		if (_physicsConfig == null || _striker == null)
		{
			_trajectoryPointCount = 0;
			_postCollisionCount = 0;
			_hitPieceTrajectoryCount = 0;
			return (0, null, 0, 0);
		}

		if (_trajectoryCacheValid &&
			_lastTrajectoryDirection.DistanceSquaredTo(direction) < TRAJECTORY_CACHE_DIRECTION_TOLERANCE_SQUARED &&
			Mathf.Abs(_lastTrajectoryPower - power) < TRAJECTORY_CACHE_POWER_TOLERANCE)
		{
			// GC OPTIMIZATION: Return cached counts - buffers already populated
			return (_trajectoryPointCount, _cachedCollisionPoint, _postCollisionCount, _hitPieceTrajectoryCount);
		}

		// OPTIMIZATION: Use constant distance trajectory with pooled buffers
		var result = SimulateConstantTrajectoryPath(startPos, direction, power);

		_lastTrajectoryDirection = direction;
		_lastTrajectoryPower = power;
		_cachedCollisionPoint = result.collisionPoint;
		_cachedHitPiece = result.hitPiece;
		_trajectoryCacheValid = true;

		// GC OPTIMIZATION: Return counts - buffers are populated by SimulateConstantTrajectoryPath
		return (result.trajectoryCount, result.collisionPoint, result.postCollisionCount, result.hitPieceCount);
	}
	
	/// <summary>
	/// OPTIMIZATION: Simulate constant distance trajectory path using pooled buffers
	/// </summary>
	private (int trajectoryCount, Vector2? collisionPoint, int postCollisionCount, CarromPiece hitPiece, int hitPieceCount) SimulateConstantTrajectoryPath(Vector2 startPos, Vector2 direction, float power)
	{
		_trajectoryPointCount = 0;
		_postCollisionCount = 0;
		_hitPieceTrajectoryCount = 0;
		Vector2? collisionPoint = null;
		CarromPiece hitPiece = null;

		Vector2 position = startPos;
		float distanceTraveled = 0.0f;
		float strikerRadius = _physicsConfig.GetRadiusForPieceType(_striker.Type);

		_trajectoryPointsBuffer[_trajectoryPointCount++] = position;

		// Step along the direction until we hit max distance or collision
		while (distanceTraveled < _trajectoryMaxDistance && _trajectoryPointCount < _trajectoryPointsBuffer.Length)
		{
			Vector2 nextPosition = position + direction * _trajectoryStepSize;

			// Check for collisions
			var collision = GetFirstCollision(position, nextPosition, strikerRadius);

			if (collision.hasCollision)
			{
				// First collision detected
				collisionPoint = collision.point;
				hitPiece = collision.hitPiece;
				_trajectoryPointsBuffer[_trajectoryPointCount++] = collision.point;

				// GC OPTIMIZATION: Simulation methods now write directly to class-level buffers
				// Calculate post-collision trajectory with simple reflection
				Vector2 reflectedDirection = direction.Bounce(collision.normal);
				SimulateConstantPostCollisionPath(collision.point, reflectedDirection);

				// Calculate hit piece trajectory if we hit a piece
				if (hitPiece != null)
				{
					Vector2 hitPieceDirection = CalculateHitPieceDirection(direction, collision.point, hitPiece.GlobalPosition, power);

					// Start trajectory from far edge of hit piece
					float hitPieceRadius = _physicsConfig.GetRadiusForPieceType(hitPiece.Type);
					Vector2 trajectoryStartPos = hitPiece.GlobalPosition + hitPieceDirection * hitPieceRadius;

					SimulateHitPieceTrajectory(trajectoryStartPos, hitPieceDirection);
				}
				break;
			}

			// Continue main trajectory
			position = nextPosition;
			distanceTraveled += _trajectoryStepSize;
			if (_trajectoryPointCount < _trajectoryPointsBuffer.Length)
				_trajectoryPointsBuffer[_trajectoryPointCount++] = position;
		}

		return (_trajectoryPointCount, collisionPoint, _postCollisionCount, hitPiece, _hitPieceTrajectoryCount);
	}
	
	/// <summary>
	/// Get first collision along trajectory path
	/// </summary>
	private (bool hasCollision, Vector2 point, Vector2 normal, CarromPiece hitPiece) GetFirstCollision(Vector2 from, Vector2 to, float radius)
	{
		if (_board == null)
		{
			return (false, Vector2.Zero, Vector2.Zero, null);
		}
		
		// Check board boundaries first
		var boardCollision = CheckBoardBoundaryCollision(from, to, radius);
		if (boardCollision.hasCollision)
		{
			return (boardCollision.hasCollision, boardCollision.point, boardCollision.normal, null);
		}
		
		// Use physics ray casting for other pieces
		var spaceState = GetWorld2D()?.DirectSpaceState;
		if (spaceState != null)
		{
			// GC OPTIMIZATION: Reuse pre-allocated query params and exclusion array
			_rayQuery.From = from;
			_rayQuery.To = to;
			_rayQuery.CollisionMask = 1; // Collision layer for pieces

			_rayExcludeBuffer.Clear();
			_rayExcludeBuffer.Add(_striker.GetRid());
			_rayQuery.Exclude = _rayExcludeBuffer;

			var result = spaceState.IntersectRay(_rayQuery);
			if (result.Count > 0)
			{
				Vector2 collisionPoint = result["position"].AsVector2();
				Vector2 normal = result["normal"].AsVector2();
				
				// Try to get the hit piece
				CarromPiece hitPiece = null;
				if (result.ContainsKey("collider"))
				{
					var collider = result["collider"].AsGodotObject();
					hitPiece = collider as CarromPiece;
				}
				
				return (true, collisionPoint, normal, hitPiece);
			}
		}
		
		return (false, Vector2.Zero, Vector2.Zero, null);
	}
	
	/// <summary>
	/// Check collision with board boundaries
	/// </summary>
	private (bool hasCollision, Vector2 point, Vector2 normal) CheckBoardBoundaryCollision(Vector2 from, Vector2 to, float radius)
	{
		float boardHalfSize = _board.BoardHalfSize;
		float effectiveBoundary = boardHalfSize - radius;
		
		// Check each boundary
		Vector2 direction = (to - from).Normalized();
		
		// Right boundary
		if (to.X > effectiveBoundary && direction.X > 0)
		{
			float t = (effectiveBoundary - from.X) / direction.X;
			if (t is >= 0 and <= 1)
			{
				Vector2 intersectionPoint = from + direction * t * from.DistanceTo(to);
				return (true, intersectionPoint, Vector2.Left);
			}
		}
		
		// Left boundary
		if (to.X < -effectiveBoundary && direction.X < 0)
		{
			float t = (-effectiveBoundary - from.X) / direction.X;
			if (t is >= 0 and <= 1)
			{
				Vector2 intersectionPoint = from + direction * t * from.DistanceTo(to);
				return (true, intersectionPoint, Vector2.Right);
			}
		}
		
		// Top boundary
		if (to.Y < -effectiveBoundary && direction.Y < 0)
		{
			float t = (-effectiveBoundary - from.Y) / direction.Y;
			if (t is >= 0 and <= 1)
			{
				Vector2 intersectionPoint = from + direction * t * from.DistanceTo(to);
				return (true, intersectionPoint, Vector2.Down);
			}
		}
		
		// Bottom boundary
		if (to.Y > effectiveBoundary && direction.Y > 0)
		{
			float t = (effectiveBoundary - from.Y) / direction.Y;
			if (t is >= 0 and <= 1)
			{
				Vector2 intersectionPoint = from + direction * t * from.DistanceTo(to);
				return (true, intersectionPoint, Vector2.Up);
			}
		}
		
		return (false, Vector2.Zero, Vector2.Zero);
	}
	
	
	/// <summary>
	/// Simulate simple post-collision trajectory path (constant distance)
	/// GC OPTIMIZATION: Writes directly to class-level buffer instead of allocating List + ToArray
	/// </summary>
	private void SimulateConstantPostCollisionPath(Vector2 startPos, Vector2 direction)
	{
		_postCollisionCount = 0;
		Vector2 position = startPos;
		float distanceTraveled = 0.0f;

		// Step along the reflected direction for the post-collision distance
		while (distanceTraveled < _trajectoryPostCollisionDistance && _postCollisionCount < _postCollisionBuffer.Length)
		{
			position += direction * _trajectoryStepSize;
			distanceTraveled += _trajectoryStepSize;
			_postCollisionBuffer[_postCollisionCount++] = position;
		}
	}
	
	/// <summary>
	/// Calculate the direction the hit piece will move after collision
	/// Uses simplified physics for momentum transfer
	/// </summary>
	private Vector2 CalculateHitPieceDirection(Vector2 strikerDirection, Vector2 collisionPoint, Vector2 hitPiecePosition, float power)
	{
		// Calculate the impact vector from collision point to hit piece center
		Vector2 impactVector = (hitPiecePosition - collisionPoint).Normalized();
		
		// For simplicity, assume elastic collision with equal masses
		// The hit piece will move in the direction of the impact vector
		// with some influence from the striker's original direction
		
		// Blend between pure impact direction and striker direction
		// More power means more direct transfer of striker's momentum
		float directionalInfluence = Mathf.Clamp(power, 0.3f, 0.7f);
		Vector2 resultDirection = impactVector.Lerp(strikerDirection, directionalInfluence).Normalized();
		
		return resultDirection;
	}
	
	/// <summary>
	/// Simulate hit piece trajectory after being struck
	/// GC OPTIMIZATION: Writes directly to class-level buffer instead of allocating List + ToArray
	/// </summary>
	private void SimulateHitPieceTrajectory(Vector2 startPos, Vector2 direction)
	{
		_hitPieceTrajectoryCount = 0;
		Vector2 position = startPos;
		float distanceTraveled = 0.0f;

		// Simulate hit piece movement for configured distance
		while (distanceTraveled < HitPieceTrajectoryDistance && _hitPieceTrajectoryCount < _hitPieceTrajectoryBuffer.Length)
		{
			position += direction * _trajectoryStepSize;
			distanceTraveled += _trajectoryStepSize;
			_hitPieceTrajectoryBuffer[_hitPieceTrajectoryCount++] = position;

			// Could add collision detection here for more accurate prediction
			// For now, just show the initial trajectory
		}
	}

	/// <summary>
	/// Comprehensive cleanup to prevent memory leaks
	/// CRITICAL FIX: Add proper signal disconnection and cache clearing
	/// </summary>
	public override void _ExitTree()
	{
		if (_isDisposed) return;

		// Clear game state reference
		_gameState = null;
		
		// Clear all cached data to prevent memory leaks
		ClearAllCaches();
		
		// Clear piece references
		_striker = null;
		_board = null;
		_cameraController = null;
		_physicsConfig = null;
		
		// Stop all processing
		SetProcessInput(false);
		SetPhysicsProcess(false);
		SetProcess(false);
		
		_isDisposed = true;
		GD.Print($"[CarromInputController] Cleanup completed");
		
		base._ExitTree();
	}
	
	/// <summary>
	/// Clear all cached data to free memory
	/// </summary>
	private void ClearAllCaches()
	{
		// Clear coordinate conversion cache
		_hasValidConversionCache = false;
		_lastScreenPosition = Vector2.Zero;
		_lastBoardPosition = Vector2.Zero;
		
		// Clear trajectory calculation cache
		_trajectoryCacheValid = false;
		_trajectoryPointCount = 0;
		_postCollisionCount = 0;
		_hitPieceTrajectoryCount = 0;
		_cachedCollisionPoint = null;
		_cachedHitPiece = null;
		_lastTrajectoryDirection = Vector2.Zero;
		_lastTrajectoryPower = 0.0f;
		
		// Clear baseline geometry cache
		_baselineGeometryCached = false;
		_baselinePositions = new Vector2[0];
		_cachedBaselineStart = Vector2.Zero;
		_cachedBaselineEnd = Vector2.Zero;
		_cachedBaselineVector = Vector2.Zero;
		_cachedBaselineLength = 0.0f;
		
		GD.Print($"[CarromInputController] All caches cleared");
	}
	
	// Public accessors
	public bool IsInputEnabled() => !_isDisposed && CanAcceptInput();
	public CarromPiece GetStriker() => _isDisposed ? null : _striker;
	public float GetMaxStrikePowerValue() => GetMaxStrikePower();
	public float GetDeadZone() => DeadZone;
	public string GetGameStateStatus() => _gameState?.GetCurrentStateName() ?? "No game state";
	public bool HasGameState() => !_isDisposed && _gameState != null;
}