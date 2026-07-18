using System;
using System.Collections.Generic;
using Godot;

namespace BarBox.Games.Carrom;

/// <summary>
/// Input controller for carrom striker control with deadzone logic
/// </summary>
[GlobalClass]
public partial class CarromInputController : Node2D
{
	[Signal]
	public delegate void StrikeExecutedEventHandler(Vector2 force);

	[Signal]
	public delegate void AimingStateChangedEventHandler(bool isAiming, Vector2 aimDirection, float power);

	[Signal]
	public delegate void StrikerPositionChangedEventHandler(Vector2 newPosition);

	private CarromPhysicsConfig _physicsConfig;

	// Visual parameters - set by CarromGame.cs
	private float _aimLineLength = 100.0f;
	private float _powerBarWidth = 60.0f;
	private float _powerBarHeight = 8.0f;

	private float _trajectoryMaxDistance = 300.0f;
	private float _trajectoryPostCollisionDistance = 25.0f;
	private float _trajectoryStepSize = 1f;
	private int _trajectoryMaxSteps = 100;

	[ExportCategory("Visual Feedback")]
	[Export]
	public bool ShowAimingLine { get; set; } = true;

	[Export]
	public bool ShowPowerIndicator { get; set; } = true;

	[Export]
	public Color AimingLineColor { get; set; } = Colors.Yellow;

	[Export]
	public Color PowerBarColor { get; set; } = Colors.Green;

	[Export]
	public float AimingLineWidth { get; set; } = 3.0f;

	[ExportCategory("Trajectory Prediction")]
	[Export]
	public float TrajectoryMaxDistance { get; set; } = 300.0f;

	[Export]
	public float TrajectoryPostCollisionDistance { get; set; } = 25.0f;

	[Export]
	public Color TrajectoryColor { get; set; } = Colors.Yellow;

	[Export]
	public Color PostCollisionTrajectoryColor { get; set; } = Colors.Orange;

	[Export]
	public Color CollisionMarkerColor { get; set; } = Colors.Red;

	[Export]
	public Color HitPieceTrajectoryColor { get; set; } = Colors.Cyan;

	[Export]
	public float HitPieceTrajectoryDistance { get; set; } = 150.0f;

	[Export]
	public bool ShowHitPieceTrajectory { get; set; } = true;

	[ExportCategory("Gradient Colors")]
	[Export]
	public Color PowerGradientLow { get; set; } = Colors.Green;

	[Export]
	public Color PowerGradientMid { get; set; } = Colors.Yellow;

	[Export]
	public Color PowerGradientHigh { get; set; } = Colors.Red;

	[Export]
	public bool UseGradientForAimLine { get; set; } = true;

	[ExportCategory("Input Parameters")]
	[Export]
	public float DeadZone { get; set; } = 40.0f;

	[Export]
	public float MaxAimDistance { get; set; } = 200.0f;

	[Export]
	public float LateralSensitivity { get; set; } = 1.5f;

	[Export(PropertyHint.Range, "5.0,90.0,1.0")]
	public float LateralAngleThreshold { get; set; } = 55.0f;

	[ExportCategory("Deadzone Visualization")]
	[Export]
	public bool ShowDeadzoneRing { get; set; } = true;

	[Export]
	public Color DeadzoneRingColor { get; set; } = Colors.Gray;

	[Export]
	public Color LateralAngleLineColor { get; set; } = Colors.White;

	[Export]
	public Color LateralAngleArcColor { get; set; } = CarromPalette.AngleArcOverlay;

	[Export]
	public Color PowerAimingZoneColor { get; set; } = CarromPalette.PowerAimingZone;

	[Export]
	public Color LateralMovementZoneColor { get; set; } = CarromPalette.LateralMovementZone;

	[Export]
	public float DeadzoneRingWidth { get; set; } = 2.0f;

	[Export]
	public float LateralAngleLineWidth { get; set; } = 1.5f;

	[Export]
	public float LateralAngleLineLength { get; set; } = 30.0f;

	[Export]
	public float ZoneArcRadiusMultiplier { get; set; } = 1.2f;

	[Export]
	public float ZoneArcWidthMultiplier { get; set; } = 1.5f;

	[Export]
	public int ZoneArcSegments { get; set; } = 16;

	[Export]
	public float FeedbackDimmingFactor { get; set; } = 0.6f;

	private CarromPiece _striker;
	private int _activeFingerId = -1;
	private bool _isAiming = false;
	private Vector2 _aimStartPosition;
	private Vector2 _currentAimPosition;
	private Vector2 _strikerStartPosition;
	private InputMode _currentInputMode = InputMode.None;

	private Vector2 _targetStrikerPosition;
	private bool _isMovingStriker = false;
	private const float STRIKER_MOVE_SPEED = 2500.0f; // Pixels per second

	private const float TRAJECTORY_CACHE_DIRECTION_TOLERANCE_SQUARED = 0.000001f;
	private const float TRAJECTORY_CACHE_POWER_TOLERANCE = 0.001f;

	private ICarromGameState _gameState;

	private bool _needsVisualUpdate = false;

	private Vector2 _lastScreenPosition;
	private Vector2 _lastBoardPosition;
	private bool _hasValidConversionCache = false;

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

	// Pre-allocated ray exclusion array to avoid per-raycast allocation
	private readonly Godot.Collections.Array<Rid> _rayExcludeBuffer = new();

	// Reused ray query params to avoid per-step allocation during trajectory aiming
	private readonly PhysicsRayQueryParameters2D _rayQuery = new();

	private enum InputMode
	{
		None,
		LateralMovement,
		PowerAiming,
	}

	private Vector2[] _baselinePositions = [];
	private int _currentPlayerIndex = 0;
	private CarromBoard _board;
	private CarromCameraController _cameraController;

	private Vector2 _cachedBaselineStart;
	private Vector2 _cachedBaselineEnd;
	private Vector2 _cachedBaselineVector;
	private float _cachedBaselineLength;
	private bool _baselineGeometryCached = false;

	private bool _showCollisionFeedback = false;
	private Vector2 _lastTargetPosition = Vector2.Zero;
	private bool _lastPositionWasValid = true;

	private bool _isInputBlocked = false;
	private float _inputBlockedAlpha = 0.5f;
	private readonly Color _iNPUT_BLOCKED_COLOR = CarromPalette.InputBlockedGlow;
	private readonly Color _iNPUT_READY_COLOR = CarromPalette.InputReadyGlow;

	private bool _isDisposed = false;

	public override void _Ready()
	{
		SetProcessInput(true);
		SetPhysicsProcess(true); // Enable physics processing for smooth movement
		SetProcess(true); // Enable process for batched visual updates

		_isDisposed = false;
	}

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

	private Vector2 ScreenToBoardPositionCached(Vector2 screenPosition)
	{
		// Use cache if position hasn't changed significantly (within 2 pixels)
		if (_hasValidConversionCache && _lastScreenPosition.DistanceTo(screenPosition) < 0.25f)
		{
			return _lastBoardPosition;
		}

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

			float distanceSquaredToTarget = newPosition.DistanceSquaredTo(_targetStrikerPosition);
			if (distanceSquaredToTarget < 1.0f)
			{
				_isMovingStriker = false;
			}
		}
	}

	public void SetPhysicsConfig(CarromPhysicsConfig physicsConfig)
	{
		_physicsConfig = physicsConfig;
	}

	public void SetVisualParameters(float aimLineLength, float powerBarWidth, float powerBarHeight)
	{
		_aimLineLength = aimLineLength;
		_powerBarWidth = powerBarWidth;
		_powerBarHeight = powerBarHeight;

		_trajectoryMaxDistance = TrajectoryMaxDistance;
		_trajectoryPostCollisionDistance = TrajectoryPostCollisionDistance;

		// Invalidate trajectory cache when parameters change
		_trajectoryCacheValid = false;
	}

	/// <summary>
	/// Called explicitly by CarromGame
	/// </summary>
	public void InitializeWithBoard(CarromBoard board)
	{
		_board = board;

		if (_board != null)
		{
			UpdateBaselinePositions();
		}
	}

	public void SetCameraController(CarromCameraController cameraController)
	{
		_cameraController = cameraController;
	}

	public void SetGameState(ICarromGameState gameState)
	{
		_gameState = gameState;
		RequestVisualUpdate();
	}

	private void UpdateBaselinePositions()
	{
		if (_board != null)
		{
			_baselinePositions = _board.GetBaselinePositions(_currentPlayerIndex);
			CacheBaselineGeometry();
		}
	}

	private void CacheBaselineGeometry()
	{
		if (_board == null)
		{
			_baselineGeometryCached = false;
			return;
		}

		Vector2 baselineCenter = _board.GetBaselinePosition(_currentPlayerIndex);
		float baselineLength = _board.BaselineLength - (_board.BaselineCapRadius * 2);
		Vector2 baselineDirection = GetBaselineDirection();

		float halfLength = baselineLength / 2;
		_cachedBaselineStart = baselineCenter - (baselineDirection * halfLength);
		_cachedBaselineEnd = baselineCenter + (baselineDirection * halfLength);
		_cachedBaselineVector = _cachedBaselineEnd - _cachedBaselineStart;
		_cachedBaselineLength = _cachedBaselineVector.LengthSquared(); // Store squared length for dot product optimization

		_baselineGeometryCached = true;
	}

	private Vector2 GetClosestPositionOnBaselineCached(Vector2 position)
	{
		if (!_baselineGeometryCached)
		{
			return _board?.GetClosestPositionOnBaseline(position, _currentPlayerIndex) ?? position;
		}

		Vector2 positionVector = position - _cachedBaselineStart;

		// Calculate projection parameter (0.0 = start, 1.0 = end)
		float projectionParam = positionVector.Dot(_cachedBaselineVector) / _cachedBaselineLength;

		projectionParam = Mathf.Clamp(projectionParam, 0.0f, 1.0f);

		return _cachedBaselineStart + (_cachedBaselineVector * projectionParam);
	}

	public override void _Input(InputEvent @event)
	{
		if (!CanAcceptInput() || _striker == null)
		{
			return;
		}

		HandleMouseInput(@event);
		HandleTouchInput(@event);
	}

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

	private void StartInput(Vector2 screenPosition)
	{
		if (!CanStartInput())
		{
			return;
		}

		Vector2 boardPosition = ScreenToBoardPositionCached(screenPosition);
		Vector2 strikerPosition = _striker.GlobalPosition; // Striker is already in board coordinates

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

	private void UpdateInput(Vector2 screenPosition)
	{
		_currentAimPosition = ScreenToBoardPositionCached(screenPosition);

		Vector2 inputVector = _currentAimPosition - _aimStartPosition;
		float inputDistance = inputVector.Length();

		if (_currentInputMode == InputMode.None && inputDistance > DeadZone)
		{
			DetermineInputMode(inputVector);
		}

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

		RequestVisualUpdate();
	}

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

		float angleFromBaseline = Mathf.Abs(baselineDirection.AngleTo(normalizedInput));
		float angleFromBaselineDegrees = Mathf.RadToDeg(angleFromBaseline);

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

	private void HandleLateralMovement(Vector2 inputVector)
	{
		if (_board == null || _striker == null)
		{
			return;
		}

		Vector2 targetPosition = GetClosestPositionOnBaselineCached(_currentAimPosition);
		float strikerRadius = _striker.PhysicsConfig?.GetRadiusForPieceType(_striker.Type) ?? 15.0f;

		bool originalPositionValid = !_board.IsPositionObstructed(targetPosition, strikerRadius, _striker);

		Vector2? validPosition = _board.FindNearestValidPositionOnBaseline(
			targetPosition, _currentPlayerIndex, strikerRadius, _striker);

		_showCollisionFeedback = true;
		_lastTargetPosition = targetPosition;
		_lastPositionWasValid = originalPositionValid;

		if (validPosition.HasValue)
		{
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

	private void StartKinematicMovement(Vector2 targetPosition)
	{
		_targetStrikerPosition = targetPosition;
		_isMovingStriker = true;
	}

	private void HandlePowerAiming(Vector2 inputVector)
	{
		if (_isMovingStriker)
		{
			_isMovingStriker = false;
		}

		float inputDistance = inputVector.Length();
		float clampedDistance = Mathf.Clamp(inputDistance, 0, MaxAimDistance);
		float power = clampedDistance / MaxAimDistance;

		Vector2 rawAimDirection = -inputVector.Normalized(); // Opposite of pull direction
		Vector2 forwardDirection = GetForwardDirection();

		// Clamp aim direction to valid angle range (±maxStrikeAngle from forward)
		float maxAngleRad = Mathf.DegToRad(GetMaxStrikeAngle());
		float currentAngle = forwardDirection.AngleTo(rawAimDirection);

		Vector2 aimDirection;
		if (Mathf.Abs(currentAngle) <= maxAngleRad)
		{
			aimDirection = rawAimDirection;
		}
		else
		{
			float clampedAngle = Mathf.Sign(currentAngle) * maxAngleRad;
			aimDirection = forwardDirection.Rotated(clampedAngle);
		}

		EmitSignal(SignalName.AimingStateChanged, true, aimDirection, power);
	}

	private void EndInput()
	{
		if (!_isAiming)
		{
			return;
		}

		if (_currentInputMode == InputMode.PowerAiming)
		{
			ExecuteStrike();
		}

		_isAiming = false;
		_activeFingerId = -1;
		_currentInputMode = InputMode.None;

		_isMovingStriker = false;

		InvalidateTrajectoryCache();

		_showCollisionFeedback = false;

		EmitSignal(SignalName.AimingStateChanged, false, Vector2.Zero, 0.0f);
		RequestVisualUpdate(); // Hide aiming indicators
	}

	private void ExecuteStrike()
	{
		Vector2 inputVector = _currentAimPosition - _aimStartPosition;
		float inputDistance = inputVector.Length();

		if (!(inputDistance > DeadZone))
		{
			return;
		}

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
			strikeDirection = rawStrikeDirection;
		}
		else
		{
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

	private Vector2 GetBaselineDirection()
	{
		// Player 0,1 use horizontal baselines (left-right movement)
		// Player 2,3 use vertical baselines (up-down movement)
		bool isHorizontalBaseline = _currentPlayerIndex <= 1;
		return isHorizontalBaseline ? Vector2.Right : Vector2.Down;
	}

	private Vector2 GetForwardDirection()
	{
		return _currentPlayerIndex switch
		{
			0 => Vector2.Up,    // Player 1: move up toward center
			1 => Vector2.Down,  // Player 2: move down toward center
			2 => Vector2.Right, // Player 3: move right toward center
			3 => Vector2.Left,  // Player 4: move left toward center
			_ => Vector2.Up,
		};
	}

	private bool CanStartInput()
	{
		bool hasStriker = _striker != null;
		bool strikerStopped = hasStriker && _striker.IsStopped();
		bool canAccept = CanAcceptInput();

		if (!hasStriker)
		{
			return false;
		}
		else if (!strikerStopped)
		{
			return false;
		}
		else if (!canAccept)
		{
			return false;
		}

		return hasStriker && strikerStopped && canAccept;
	}

	private bool CanAcceptInput()
	{
		bool phaseReady = _gameState?.CanAcceptInput ?? false;

		bool strikerValid = _striker != null &&
							GodotObject.IsInstanceValid(_striker) &&
							_striker.Visible &&
							!_striker.Freeze;

		return phaseReady && strikerValid;
	}

	public override void _Draw()
	{
		if (_striker != null && IsInstanceValid(_striker))
		{
			DrawInputStateIndicator();
		}

		if (!_isAiming || _striker == null)
		{
			return;
		}

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

	private void DrawAimingLine(Vector2 strikerPos, Vector2 currentPos)
	{
		Vector2 inputVector = currentPos - _aimStartPosition;
		float inputDistance = inputVector.Length();

		if (inputDistance > DeadZone)
		{
			float clampedDistance = Mathf.Clamp(inputDistance, 0, MaxAimDistance);
			Vector2 clampedInputPos = _aimStartPosition + (inputVector.Normalized() * clampedDistance);

			float power = Mathf.Clamp((inputDistance - DeadZone) / (MaxAimDistance - DeadZone), 0.0f, 1.0f);

			// Start line from opposite edge of striker (offset by radius)
			Vector2 lineDirection = (clampedInputPos - strikerPos).Normalized();
			float strikerRadius = _striker.PhysicsConfig?.GetRadiusForPieceType(_striker.Type) ?? 15.0f;
			Vector2 lineStart = strikerPos + (lineDirection * strikerRadius);

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
				aimDirection = rawAimDirection;
			}
			else
			{
				float clampedAngle = Mathf.Sign(currentAngle) * maxAngleRad;
				aimDirection = forwardDirection.Rotated(clampedAngle);
			}

			float clampedDistance = Mathf.Clamp(inputDistance, DeadZone, MaxAimDistance);
			float power = (clampedDistance - DeadZone) / (MaxAimDistance - DeadZone);

			// Get trajectory prediction - populates class-level buffers
			var trajectory = CalculateTrajectoryPoints(strikerPos, aimDirection, power);

			// Draw directly from class-level buffers using counts to avoid array allocation
			DrawTrajectoryPath(_trajectoryPointsBuffer, trajectory.trajectoryCount, TrajectoryColor);

			if (trajectory.collisionPoint.HasValue)
			{
				DrawCollisionMarker(trajectory.collisionPoint.Value);
			}

			if (trajectory.postCollisionCount > 0)
			{
				DrawTrajectoryPath(_postCollisionBuffer, trajectory.postCollisionCount, PostCollisionTrajectoryColor);
			}

			if (ShowHitPieceTrajectory && trajectory.hitPieceCount > 0)
			{
				DrawHitPieceTrajectory(_hitPieceTrajectoryBuffer, trajectory.hitPieceCount, HitPieceTrajectoryColor);
			}
		}
	}

	/// <summary>
	/// Overload that accepts buffer + count to avoid array allocation
	/// </summary>
	private void DrawTrajectoryPath(Vector2[] buffer, int count, Color color)
	{
		if (count < 2)
		{
			return;
		}

		// Draw individual lines from buffer to avoid creating a new array for DrawPolyline
		for (int i = 0; i < count - 1; i++)
		{
			DrawLine(buffer[i], buffer[i + 1], color, AimingLineWidth, true);
		}
	}

	private void DrawTrajectoryPath(Vector2[] points, Color color)
	{
		if (points.Length < 2)
		{
			return;
		}

		DrawPolyline(points, color, AimingLineWidth, true);
	}

	private void DrawCollisionMarker(Vector2 position)
	{
		float markerRadius = 8.0f;
		DrawArc(position, markerRadius, 0, Mathf.Tau, 16, CollisionMarkerColor, AimingLineWidth * 1.5f, true);
	}

	private void DrawDottedLine(Vector2 from, Vector2 to, Color color, float width)
	{
		// Draw manually to ensure consistent dash sizes regardless of line length
		float dashLength = 8.0f;
		float gapLength = 4.0f;
		float totalPattern = dashLength + gapLength;

		Vector2 direction = (to - from).Normalized();
		float totalDistance = from.DistanceTo(to);

		int completePatterns = Mathf.FloorToInt(totalDistance / totalPattern);
		float remainingDistance = totalDistance - (completePatterns * totalPattern);

		for (int i = 0; i < completePatterns; i++)
		{
			Vector2 dashStart = from + (direction * (i * totalPattern));
			Vector2 dashEnd = dashStart + (direction * dashLength);
			DrawLine(dashStart, dashEnd, color, width, true);
		}

		if (remainingDistance > 0)
		{
			Vector2 finalDashStart = from + (direction * (completePatterns * totalPattern));
			Vector2 finalDashEnd = finalDashStart + (direction * Mathf.Min(dashLength, remainingDistance));
			DrawLine(finalDashStart, finalDashEnd, color, width, true);
		}
	}

	private void DrawGradientDottedLine(Vector2 from, Vector2 to, float power, float width)
	{
		float dashLength = 8.0f;
		float gapLength = 4.0f;
		float totalPattern = dashLength + gapLength;

		Vector2 direction = (to - from).Normalized();
		float totalDistance = from.DistanceTo(to);

		int completePatterns = Mathf.FloorToInt(totalDistance / totalPattern);
		float remainingDistance = totalDistance - (completePatterns * totalPattern);

		for (int i = 0; i < completePatterns; i++)
		{
			float patternStart = i * totalPattern;
			float patternMid = patternStart + (dashLength / 2.0f);

			float gradientT = patternMid / totalDistance;
			float colorT = Mathf.Lerp(0.0f, power, gradientT);
			Color dashColor = GetPowerGradientColor(colorT);

			Vector2 dashStart = from + (direction * patternStart);
			Vector2 dashEnd = dashStart + (direction * dashLength);
			DrawLine(dashStart, dashEnd, dashColor, width, true);
		}

		if (remainingDistance > 0)
		{
			float finalDashLength = Mathf.Min(dashLength, remainingDistance);
			float finalPatternStart = completePatterns * totalPattern;
			float finalPatternMid = finalPatternStart + (finalDashLength / 2.0f);

			float gradientT = finalPatternMid / totalDistance;
			float colorT = Mathf.Lerp(0.0f, power, gradientT);
			Color dashColor = GetPowerGradientColor(colorT);

			Vector2 finalDashStart = from + (direction * finalPatternStart);
			Vector2 finalDashEnd = finalDashStart + (direction * finalDashLength);
			DrawLine(finalDashStart, finalDashEnd, dashColor, width, true);
		}
	}

	private Color GetPowerGradientColor(float power)
	{
		if (power <= 0.5f)
		{
			float t = power * 2.0f; // Scale 0-0.5 to 0-1
			return PowerGradientLow.Lerp(PowerGradientMid, t);
		}
		else
		{
			float t = (power - 0.5f) * 2.0f; // Scale 0.5-1 to 0-1
			return PowerGradientMid.Lerp(PowerGradientHigh, t);
		}
	}

	/// <summary>
	/// Overload that accepts buffer + count to avoid array allocation
	/// </summary>
	private void DrawHitPieceTrajectory(Vector2[] buffer, int count, Color color)
	{
		if (count < 2)
		{
			return;
		}

		float dashLength = 5.0f;
		float gapLength = 3.0f;
		float totalPattern = dashLength + gapLength;

		for (int i = 0; i < count - 1; i++)
		{
			Vector2 segmentStart = buffer[i];
			Vector2 segmentEnd = buffer[i + 1];
			Vector2 direction = (segmentEnd - segmentStart).Normalized();
			float segmentLength = segmentStart.DistanceTo(segmentEnd);

			// Fade alpha based on distance from collision (index along trajectory)
			float fadeAlpha = 1.0f - (i * 0.1f);
			fadeAlpha = Mathf.Max(fadeAlpha, 0.2f); // Keep minimum visibility
			Color fadedColor = new Color(color.R, color.G, color.B, color.A * fadeAlpha);

			int dashCount = Mathf.FloorToInt(segmentLength / totalPattern);
			for (int j = 0; j < dashCount; j++)
			{
				Vector2 dashStart = segmentStart + (direction * (j * totalPattern));
				Vector2 dashEnd = dashStart + (direction * dashLength);
				DrawLine(dashStart, dashEnd, fadedColor, AimingLineWidth * 0.8f, true);
			}

			float remaining = segmentLength - (dashCount * totalPattern);
			if (remaining > 0)
			{
				Vector2 finalDashStart = segmentStart + (direction * (dashCount * totalPattern));
				Vector2 finalDashEnd = finalDashStart + (direction * Mathf.Min(dashLength, remaining));
				DrawLine(finalDashStart, finalDashEnd, fadedColor, AimingLineWidth * 0.8f, true);
			}
		}
	}

	private void DrawHitPieceTrajectory(Vector2[] points, Color color)
	{
		DrawHitPieceTrajectory(points, points.Length, color);
	}

	private void DrawDeadzoneRing(Vector2 strikerPos)
	{
		if (!ShowDeadzoneRing)
		{
			return;
		}

		DrawArc(strikerPos, DeadZone, 0, Mathf.Tau, 32, DeadzoneRingColor, DeadzoneRingWidth, true);

		Vector2 forwardDirection = GetForwardDirection();

		// Calculate lateral angle threshold lines - these should be on the forward side
		float lateralAngleRad = Mathf.DegToRad(LateralAngleThreshold);

		// Left and right angle lines rotated from the forward direction (not offset from it)
		Vector2 leftAngleDirection = forwardDirection.Rotated(-lateralAngleRad);
		Vector2 rightAngleDirection = forwardDirection.Rotated(lateralAngleRad);

		Vector2 leftLineEnd = strikerPos - (leftAngleDirection * (DeadZone + LateralAngleLineLength));
		Vector2 rightLineEnd = strikerPos - (rightAngleDirection * (DeadZone + LateralAngleLineLength));
		Vector2 leftLineStart = strikerPos - (leftAngleDirection * DeadZone);
		Vector2 rightLineStart = strikerPos - (rightAngleDirection * DeadZone);

		DrawDottedLine(leftLineStart, leftLineEnd, LateralAngleLineColor, LateralAngleLineWidth);
		DrawDottedLine(rightLineStart, rightLineEnd, LateralAngleLineColor, LateralAngleLineWidth);

		float forwardAngle = forwardDirection.Angle();

		// Power aiming zone: backward swipes (toward forward direction) within lateral threshold
		float powerAimingStartAngle = -forwardAngle - lateralAngleRad;
		float powerAimingEndAngle = -forwardAngle + lateralAngleRad;

		Color powerAimingArcColor = PowerAimingZoneColor;
		Color lateralMovementArcColor = LateralMovementZoneColor;

		Vector2 inputVector = _currentAimPosition - _aimStartPosition;
		float inputDistance = inputVector.Length();
		if (inputDistance > 0 && inputDistance <= DeadZone)
		{
			Vector2 baselineDirection = -GetForwardDirection();
			Vector2 normalizedInput = inputVector.Normalized();

			float angleFromBaseline = Mathf.Abs(baselineDirection.AngleTo(normalizedInput));
			float angleFromBaselineDegrees = Mathf.RadToDeg(angleFromBaseline);

			bool isLateralInput = angleFromBaselineDegrees > LateralAngleThreshold;
			if (isLateralInput)
			{
				lateralMovementArcColor = new Color(LateralMovementZoneColor.R * FeedbackDimmingFactor, LateralMovementZoneColor.G * FeedbackDimmingFactor, LateralMovementZoneColor.B * FeedbackDimmingFactor, LateralMovementZoneColor.A * FeedbackDimmingFactor);
			}
			else
			{
				powerAimingArcColor = new Color(PowerAimingZoneColor.R * FeedbackDimmingFactor, PowerAimingZoneColor.G * FeedbackDimmingFactor, PowerAimingZoneColor.B * FeedbackDimmingFactor, PowerAimingZoneColor.A * FeedbackDimmingFactor);
			}
		}

		DrawArc(strikerPos, DeadZone * ZoneArcRadiusMultiplier, powerAimingStartAngle, powerAimingEndAngle, ZoneArcSegments, powerAimingArcColor, DeadZone * ZoneArcWidthMultiplier, true);
		DrawArc(strikerPos, DeadZone * ZoneArcRadiusMultiplier, powerAimingEndAngle, powerAimingStartAngle + Mathf.Tau, ZoneArcSegments, lateralMovementArcColor, DeadZone * ZoneArcWidthMultiplier, true);
	}

	private void DrawPowerIndicator(Vector2 strikerPos, Vector2 currentPos)
	{
		Vector2 inputVector = currentPos - _aimStartPosition;
		float inputDistance = inputVector.Length();

		if (!(inputDistance > DeadZone))
		{
			return;
		}

		float clampedDistance = Mathf.Clamp(inputDistance, DeadZone, MaxAimDistance);
		float power = (clampedDistance - DeadZone) / (MaxAimDistance - DeadZone);

		Vector2 barPosition = strikerPos + (Vector2.Up * 40);
		float barWidth = _powerBarWidth;
		float barHeight = _powerBarHeight;

		DrawRect(
			new Rect2(
			barPosition - (Vector2.Right * barWidth / 2),
			new Vector2(barWidth, barHeight)), Colors.Gray, true, -1f, true);

		Color powerColor = power > 0.8f ? Colors.Red :
			power > 0.5f ? Colors.Yellow : PowerBarColor;
		DrawRect(
			new Rect2(
			barPosition - (Vector2.Right * barWidth / 2),
			new Vector2(barWidth * power, barHeight)), powerColor, true, -1f, true);
	}

	private void DrawInputStateIndicator()
	{
		// No visual indicators around the striker to avoid gameplay confusion
	}

	private void DrawCollisionFeedback()
	{
		if (_striker == null)
		{
			return;
		}

		float strikerRadius = _striker.PhysicsConfig?.GetRadiusForPieceType(_striker.Type) ?? 15.0f;

		Color indicatorColor = _lastPositionWasValid ? Colors.Green : Colors.Red;
		Color ringColor = _lastPositionWasValid ? CarromPalette.ValidPositionRing : CarromPalette.InvalidPositionRing;

		DrawCircle(_lastTargetPosition, strikerRadius, ringColor);

		DrawArc(_lastTargetPosition, strikerRadius, 0, Mathf.Tau, 32, indicatorColor, 3.0f, true);

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
			float checkSize = strikerRadius * 0.4f;
			Vector2 checkStart = _lastTargetPosition + new Vector2(-checkSize * 0.5f, 0);
			Vector2 checkMid = _lastTargetPosition + new Vector2(-checkSize * 0.2f, checkSize * 0.3f);
			Vector2 checkEnd = _lastTargetPosition + new Vector2(checkSize * 0.5f, -checkSize * 0.3f);

			DrawLine(checkStart, checkMid, Colors.Green, 4.0f, true);
			DrawLine(checkMid, checkEnd, Colors.Green, 4.0f, true);
		}
	}

	public void SetStriker(CarromPiece striker)
	{
		_striker = striker;
	}

	public void SetCurrentPlayer(int playerIndex)
	{
		if (_currentPlayerIndex == playerIndex)
		{
			return;
		}

		_currentPlayerIndex = playerIndex;
		UpdateBaselinePositions();
		InvalidateTrajectoryCache(); // Player change affects trajectory calculation
		GD.Print($"[CarromInputController] Switched to player {playerIndex}, baseline updated immediately");
	}

	public bool IsAiming()
	{
		return _isAiming;
	}

	public string GetCurrentInputMode()
	{
		return _currentInputMode.ToString();
	}

	private float GetMaxStrikePower() => _physicsConfig?.MaxStrikePower ?? 2000.0f;

	private float GetMinStrikePower() => _physicsConfig?.MinStrikePower ?? 200.0f;

	private float GetMaxStrikeAngle() => _physicsConfig?.MaxStrikeAngle ?? 60.0f;

	/// <summary>
	/// Populates class-level buffers and returns counts instead of allocating new arrays.
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
			// Return cached counts - buffers already populated
			return (_trajectoryPointCount, _cachedCollisionPoint, _postCollisionCount, _hitPieceTrajectoryCount);
		}

		var result = SimulateConstantTrajectoryPath(startPos, direction, power);

		_lastTrajectoryDirection = direction;
		_lastTrajectoryPower = power;
		_cachedCollisionPoint = result.collisionPoint;
		_cachedHitPiece = result.hitPiece;
		_trajectoryCacheValid = true;

		// Return counts - buffers are populated by SimulateConstantTrajectoryPath
		return (result.trajectoryCount, result.collisionPoint, result.postCollisionCount, result.hitPieceCount);
	}

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
			Vector2 nextPosition = position + (direction * _trajectoryStepSize);

			var collision = GetFirstCollision(position, nextPosition, strikerRadius);

			if (collision.hasCollision)
			{
				collisionPoint = collision.point;
				hitPiece = collision.hitPiece;
				_trajectoryPointsBuffer[_trajectoryPointCount++] = collision.point;

				// Simulation methods write directly to class-level buffers
				Vector2 reflectedDirection = direction.Bounce(collision.normal);
				SimulateConstantPostCollisionPath(collision.point, reflectedDirection);

				if (hitPiece != null)
				{
					Vector2 hitPieceDirection = CalculateHitPieceDirection(direction, collision.point, hitPiece.GlobalPosition, power);

					// Start trajectory from far edge of hit piece
					float hitPieceRadius = _physicsConfig.GetRadiusForPieceType(hitPiece.Type);
					Vector2 trajectoryStartPos = hitPiece.GlobalPosition + (hitPieceDirection * hitPieceRadius);

					SimulateHitPieceTrajectory(trajectoryStartPos, hitPieceDirection);
				}

				break;
			}

			position = nextPosition;
			distanceTraveled += _trajectoryStepSize;
			if (_trajectoryPointCount < _trajectoryPointsBuffer.Length)
			{
				_trajectoryPointsBuffer[_trajectoryPointCount++] = position;
			}
		}

		return (_trajectoryPointCount, collisionPoint, _postCollisionCount, hitPiece, _hitPieceTrajectoryCount);
	}

	private (bool hasCollision, Vector2 point, Vector2 normal, CarromPiece hitPiece) GetFirstCollision(Vector2 from, Vector2 to, float radius)
	{
		if (_board == null)
		{
			return (false, Vector2.Zero, Vector2.Zero, null);
		}

		var boardCollision = CheckBoardBoundaryCollision(from, to, radius);
		if (boardCollision.hasCollision)
		{
			return (boardCollision.hasCollision, boardCollision.point, boardCollision.normal, null);
		}

		var spaceState = GetWorld2D()?.DirectSpaceState;
		if (spaceState != null)
		{
			// Reuse pre-allocated query params and exclusion array
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

	private (bool hasCollision, Vector2 point, Vector2 normal) CheckBoardBoundaryCollision(Vector2 from, Vector2 to, float radius)
	{
		float boardHalfSize = _board.BoardHalfSize;
		float effectiveBoundary = boardHalfSize - radius;

		Vector2 direction = (to - from).Normalized();

		// Right boundary
		if (to.X > effectiveBoundary && direction.X > 0)
		{
			float t = (effectiveBoundary - from.X) / direction.X;
			if (t is >= 0 and <= 1)
			{
				Vector2 intersectionPoint = from + (direction * t * from.DistanceTo(to));
				return (true, intersectionPoint, Vector2.Left);
			}
		}

		// Left boundary
		if (to.X < -effectiveBoundary && direction.X < 0)
		{
			float t = (-effectiveBoundary - from.X) / direction.X;
			if (t is >= 0 and <= 1)
			{
				Vector2 intersectionPoint = from + (direction * t * from.DistanceTo(to));
				return (true, intersectionPoint, Vector2.Right);
			}
		}

		// Top boundary
		if (to.Y < -effectiveBoundary && direction.Y < 0)
		{
			float t = (-effectiveBoundary - from.Y) / direction.Y;
			if (t is >= 0 and <= 1)
			{
				Vector2 intersectionPoint = from + (direction * t * from.DistanceTo(to));
				return (true, intersectionPoint, Vector2.Down);
			}
		}

		// Bottom boundary
		if (to.Y > effectiveBoundary && direction.Y > 0)
		{
			float t = (effectiveBoundary - from.Y) / direction.Y;
			if (t is >= 0 and <= 1)
			{
				Vector2 intersectionPoint = from + (direction * t * from.DistanceTo(to));
				return (true, intersectionPoint, Vector2.Up);
			}
		}

		return (false, Vector2.Zero, Vector2.Zero);
	}

	/// <summary>
	/// Writes directly to class-level buffer instead of allocating List + ToArray
	/// </summary>
	private void SimulateConstantPostCollisionPath(Vector2 startPos, Vector2 direction)
	{
		_postCollisionCount = 0;
		Vector2 position = startPos;
		float distanceTraveled = 0.0f;

		while (distanceTraveled < _trajectoryPostCollisionDistance && _postCollisionCount < _postCollisionBuffer.Length)
		{
			position += direction * _trajectoryStepSize;
			distanceTraveled += _trajectoryStepSize;
			_postCollisionBuffer[_postCollisionCount++] = position;
		}
	}

	/// <summary>
	/// Uses simplified physics for momentum transfer
	/// </summary>
	private Vector2 CalculateHitPieceDirection(Vector2 strikerDirection, Vector2 collisionPoint, Vector2 hitPiecePosition, float power)
	{
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
	/// Writes directly to class-level buffer instead of allocating List + ToArray
	/// </summary>
	private void SimulateHitPieceTrajectory(Vector2 startPos, Vector2 direction)
	{
		_hitPieceTrajectoryCount = 0;
		Vector2 position = startPos;
		float distanceTraveled = 0.0f;

		while (distanceTraveled < HitPieceTrajectoryDistance && _hitPieceTrajectoryCount < _hitPieceTrajectoryBuffer.Length)
		{
			position += direction * _trajectoryStepSize;
			distanceTraveled += _trajectoryStepSize;
			_hitPieceTrajectoryBuffer[_hitPieceTrajectoryCount++] = position;
		}
	}

	/// <summary>
	/// Cleanup must disconnect signals and clear cached node references -
	/// either outliving the scene leaks the nodes they point at.
	/// </summary>
	public override void _ExitTree()
	{
		if (_isDisposed)
		{
			return;
		}

		_gameState = null;

		ClearAllCaches();

		_striker = null;
		_board = null;
		_cameraController = null;
		_physicsConfig = null;

		SetProcessInput(false);
		SetPhysicsProcess(false);
		SetProcess(false);

		_isDisposed = true;
		GD.Print($"[CarromInputController] Cleanup completed");

		base._ExitTree();
	}

	private void ClearAllCaches()
	{
		_hasValidConversionCache = false;
		_lastScreenPosition = Vector2.Zero;
		_lastBoardPosition = Vector2.Zero;

		_trajectoryCacheValid = false;
		_trajectoryPointCount = 0;
		_postCollisionCount = 0;
		_hitPieceTrajectoryCount = 0;
		_cachedCollisionPoint = null;
		_cachedHitPiece = null;
		_lastTrajectoryDirection = Vector2.Zero;
		_lastTrajectoryPower = 0.0f;

		_baselineGeometryCached = false;
		_baselinePositions = [];
		_cachedBaselineStart = Vector2.Zero;
		_cachedBaselineEnd = Vector2.Zero;
		_cachedBaselineVector = Vector2.Zero;
		_cachedBaselineLength = 0.0f;

		GD.Print($"[CarromInputController] All caches cleared");
	}

	public bool IsInputEnabled() => !_isDisposed && CanAcceptInput();

	public CarromPiece GetStriker() => _isDisposed ? null : _striker;

	public float GetMaxStrikePowerValue() => GetMaxStrikePower();

	public float GetDeadZone() => DeadZone;

	public string GetGameStateStatus() => _gameState?.GetCurrentStateName() ?? "No game state";

	public bool HasGameState() => !_isDisposed && _gameState != null;
}
