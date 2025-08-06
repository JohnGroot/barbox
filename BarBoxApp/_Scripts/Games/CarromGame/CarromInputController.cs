using Godot;

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
	private float _trajectoryStepSize = 5.0f;
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
	private bool _isAiming = false;
	private Vector2 _aimStartPosition;
	private Vector2 _currentAimPosition;
	private Vector2 _strikerStartPosition;
	private InputMode _currentInputMode = InputMode.None;
	
	// Kinematic movement state
	private Vector2 _targetStrikerPosition;
	private bool _isMovingStriker = false;
	private const float STRIKER_MOVE_SPEED = 2500.0f; // Pixels per second
	
	// Simplified state management - single source of truth
	private ICarromGameState _gameState;
	
	// Visual update batching
	private bool _needsVisualUpdate = false;
	
	// Coordinate conversion caching
	private Vector2 _lastScreenPosition;
	private Vector2 _lastBoardPosition;
	private bool _hasValidConversionCache = false;
	
	// Trajectory calculation caching
	private Vector2 _lastTrajectoryDirection;
	private float _lastTrajectoryPower;
	private Vector2[] _cachedTrajectoryPoints = new Vector2[0];
	private Vector2? _cachedCollisionPoint;
	private Vector2[] _cachedPostCollisionPoints = new Vector2[0];
	private bool _trajectoryCacheValid = false;

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
	private CarromBoard? _board;
	private CarromCameraController? _cameraController;
	
	// Cached baseline geometry for performance optimization
	private Vector2 _cachedBaselineStart;
	private Vector2 _cachedBaselineEnd;
	private Vector2 _cachedBaselineVector;
	private float _cachedBaselineLength;
	private bool _baselineGeometryCached = false;

	public override void _Ready()
	{
		SetProcessInput(true);
		SetPhysicsProcess(true); // Enable physics processing for smooth movement
		SetProcess(true); // Enable process for batched visual updates
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
		if (_hasValidConversionCache && _lastScreenPosition.DistanceTo(screenPosition) < 2.0f)
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
	/// Set the game state interface - replaces phase manager dependency
	/// </summary>
	public void SetGameState(ICarromGameState gameState)
	{
		_gameState = gameState;
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
				StartInput(touch.Position);
			}
			else
			{
				EndInput();
			}
		}
		else if (@event is InputEventScreenDrag drag && _isAiming)
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
		{
			return;
		}

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
	/// Handle lateral striker movement along baseline
	/// </summary>
	private void HandleLateralMovement(Vector2 inputVector)
	{
		if (_board == null) return;
		
		// Use cached geometry for fast position calculation
		Vector2 closestPosition = GetClosestPositionOnBaselineCached(_currentAimPosition);
		
		// Use kinematic movement instead of direct position assignment
		StartKinematicMovement(closestPosition);
		EmitSignal(SignalName.StrikerPositionChanged, closestPosition);
		
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
		_currentInputMode = InputMode.None;
		
		// Stop any ongoing kinematic movement
		_isMovingStriker = false;
		
		// Clear trajectory cache
		InvalidateTrajectoryCache();
		
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
		
		if (inputDistance > DeadZone)
		{
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
	/// Check if input can be accepted - simplified to single source of truth
	/// </summary>
	private bool CanAcceptInput()
	{
		return _gameState?.CanAcceptInput ?? false;
	}

	/// <summary>
	/// Custom drawing for visual feedback
	/// </summary>
	public override void _Draw()
	{
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
		
		// Draw dotted line from striker to input position, clamped to max distance
		if (inputDistance > 0)
		{
			float clampedDistance = Mathf.Clamp(inputDistance, 0, MaxAimDistance);
			Vector2 clampedInputPos = _aimStartPosition + inputVector.Normalized() * clampedDistance;
			
			// Calculate power for color matching
			float power = inputDistance > DeadZone ? 
				Mathf.Clamp((inputDistance - DeadZone) / (MaxAimDistance - DeadZone), 0.0f, 1.0f) : 0.0f;
			Color lineColor = power > 0.8f ? Colors.Red : 
							 power > 0.5f ? Colors.Yellow : PowerBarColor;
			
			// Start line from opposite edge of striker (offset by radius)
			Vector2 lineDirection = (clampedInputPos - strikerPos).Normalized();
			float strikerRadius = _striker.PhysicsConfig?.GetRadiusForPieceType(_striker.Type) ?? 15.0f;
			Vector2 lineStart = strikerPos + lineDirection * strikerRadius;
			
			DrawDottedLine(lineStart, clampedInputPos, lineColor, AimingLineWidth);
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
			
			// Get trajectory prediction
			var trajectory = CalculateTrajectoryPoints(strikerPos, aimDirection, power);
			
			// Draw main trajectory
			DrawTrajectoryPath(trajectory.trajectoryPoints, TrajectoryColor);
			
			// Draw collision marker if there's a collision
			if (trajectory.collisionPoint.HasValue)
			{
				DrawCollisionMarker(trajectory.collisionPoint.Value);
			}
			
			// Draw post-collision trajectory
			if (trajectory.postCollisionPoints.Length > 0)
			{
				DrawTrajectoryPath(trajectory.postCollisionPoints, PostCollisionTrajectoryColor);
			}
		}
	}
	
	/// <summary>
	/// Draw trajectory path as connected line segments
	/// </summary>
	private void DrawTrajectoryPath(Vector2[] points, Color color)
	{
		if (points.Length < 2) return;
		
		for (int i = 0; i < points.Length - 1; i++)
		{
			DrawLine(points[i], points[i + 1], color, AimingLineWidth, true);
		}
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
	/// Draw dotted line between two points
	/// </summary>
	private void DrawDottedLine(Vector2 from, Vector2 to, Color color, float width)
	{
		Vector2 direction = (to - from).Normalized();
		float totalDistance = from.DistanceTo(to);
		float dashLength = 8.0f;
		float gapLength = 4.0f;
		float segmentLength = dashLength + gapLength;
		
		Vector2 currentPos = from;
		float distanceTraveled = 0.0f;
		
		while (distanceTraveled < totalDistance)
		{
			float remainingDistance = totalDistance - distanceTraveled;
			float currentDashLength = Mathf.Min(dashLength, remainingDistance);
			
			Vector2 dashEnd = currentPos + direction * currentDashLength;
			DrawLine(currentPos, dashEnd, color, width, true);
			
			// Move to next dash position
			distanceTraveled += segmentLength;
			currentPos = from + direction * distanceTraveled;
		}
	}
	
	/// <summary>
	/// Draw deadzone ring with lateral angle indicators
	/// </summary>
	private void DrawDeadzoneRing(Vector2 strikerPos)
	{
		if (!ShowDeadzoneRing) return;
		
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

		// Use dashed lines instead of solid lines
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
		
		if (inputDistance > DeadZone)
		{
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
		if (_currentPlayerIndex != playerIndex)
		{
			_currentPlayerIndex = playerIndex;
			UpdateBaselinePositions(); // This will also update the cached geometry
			InvalidateTrajectoryCache(); // Player change affects trajectory calculation
		}
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
	/// </summary>
	private (Vector2[] trajectoryPoints, Vector2? collisionPoint, Vector2[] postCollisionPoints) CalculateTrajectoryPoints(Vector2 startPos, Vector2 direction, float power)
	{
		if (_physicsConfig == null || _striker == null)
		{
			return (new Vector2[0], null, new Vector2[0]);
		}
		
		// Check cache validity (only check direction, ignore power)
		if (_trajectoryCacheValid && 
			_lastTrajectoryDirection.DistanceTo(direction) < 0.01f)
		{
			return (_cachedTrajectoryPoints, _cachedCollisionPoint, _cachedPostCollisionPoints);
		}
		
		// Use constant distance trajectory regardless of power
		var result = SimulateConstantTrajectoryPath(startPos, direction);
		
		// Cache results
		_lastTrajectoryDirection = direction;
		_lastTrajectoryPower = power; // Still cache power for completeness
		_cachedTrajectoryPoints = result.trajectoryPoints;
		_cachedCollisionPoint = result.collisionPoint;
		_cachedPostCollisionPoints = result.postCollisionPoints;
		_trajectoryCacheValid = true;
		
		return result;
	}
	
	/// <summary>
	/// Simulate constant distance trajectory path with collision detection
	/// </summary>
	private (Vector2[] trajectoryPoints, Vector2? collisionPoint, Vector2[] postCollisionPoints) SimulateConstantTrajectoryPath(Vector2 startPos, Vector2 direction)
	{
		var trajectoryPoints = new System.Collections.Generic.List<Vector2>();
		var postCollisionPoints = new System.Collections.Generic.List<Vector2>();
		Vector2? collisionPoint = null;
		
		Vector2 position = startPos;
		float distanceTraveled = 0.0f;
		float strikerRadius = _physicsConfig.GetRadiusForPieceType(_striker.Type);
		
		trajectoryPoints.Add(position);
		
		// Step along the direction until we hit max distance or collision
		while (distanceTraveled < _trajectoryMaxDistance)
		{
			Vector2 nextPosition = position + direction * _trajectoryStepSize;
			
			// Check for collisions
			var collision = GetFirstCollision(position, nextPosition, strikerRadius);
			
			if (collision.hasCollision)
			{
				// First collision detected
				collisionPoint = collision.point;
				trajectoryPoints.Add(collision.point);
				
				// Calculate post-collision trajectory with simple reflection
				Vector2 reflectedDirection = direction.Bounce(collision.normal);
				var postCollisionResult = SimulateConstantPostCollisionPath(collision.point, reflectedDirection);
				postCollisionPoints.AddRange(postCollisionResult);
				break;
			}
			
			// Continue main trajectory
			position = nextPosition;
			distanceTraveled += _trajectoryStepSize;
			trajectoryPoints.Add(position);
		}
		
		return (trajectoryPoints.ToArray(), collisionPoint, postCollisionPoints.ToArray());
	}
	
	/// <summary>
	/// Get first collision along trajectory path
	/// </summary>
	private (bool hasCollision, Vector2 point, Vector2 normal) GetFirstCollision(Vector2 from, Vector2 to, float radius)
	{
		if (_board == null)
		{
			return (false, Vector2.Zero, Vector2.Zero);
		}
		
		// Check board boundaries first
		var boardCollision = CheckBoardBoundaryCollision(from, to, radius);
		if (boardCollision.hasCollision)
		{
			return boardCollision;
		}
		
		// Use physics ray casting for other pieces
		var spaceState = GetWorld2D()?.DirectSpaceState;
		if (spaceState != null)
		{
			var query = PhysicsRayQueryParameters2D.Create(from, to);
			query.CollisionMask = 1; // Collision layer for pieces
			query.Exclude = new Godot.Collections.Array<Rid> { _striker.GetRid() };
			
			var result = spaceState.IntersectRay(query);
			if (result.Count > 0)
			{
				Vector2 collisionPoint = result["position"].AsVector2();
				Vector2 normal = result["normal"].AsVector2();
				return (true, collisionPoint, normal);
			}
		}
		
		return (false, Vector2.Zero, Vector2.Zero);
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
			if (t >= 0 && t <= 1)
			{
				Vector2 intersectionPoint = from + direction * t * from.DistanceTo(to);
				return (true, intersectionPoint, Vector2.Left);
			}
		}
		
		// Left boundary
		if (to.X < -effectiveBoundary && direction.X < 0)
		{
			float t = (-effectiveBoundary - from.X) / direction.X;
			if (t >= 0 && t <= 1)
			{
				Vector2 intersectionPoint = from + direction * t * from.DistanceTo(to);
				return (true, intersectionPoint, Vector2.Right);
			}
		}
		
		// Top boundary
		if (to.Y < -effectiveBoundary && direction.Y < 0)
		{
			float t = (-effectiveBoundary - from.Y) / direction.Y;
			if (t >= 0 && t <= 1)
			{
				Vector2 intersectionPoint = from + direction * t * from.DistanceTo(to);
				return (true, intersectionPoint, Vector2.Down);
			}
		}
		
		// Bottom boundary
		if (to.Y > effectiveBoundary && direction.Y > 0)
		{
			float t = (effectiveBoundary - from.Y) / direction.Y;
			if (t >= 0 && t <= 1)
			{
				Vector2 intersectionPoint = from + direction * t * from.DistanceTo(to);
				return (true, intersectionPoint, Vector2.Up);
			}
		}
		
		return (false, Vector2.Zero, Vector2.Zero);
	}
	
	
	/// <summary>
	/// Simulate simple post-collision trajectory path (constant distance)
	/// </summary>
	private Vector2[] SimulateConstantPostCollisionPath(Vector2 startPos, Vector2 direction)
	{
		var points = new System.Collections.Generic.List<Vector2>();
		
		Vector2 position = startPos;
		float distanceTraveled = 0.0f;
		
		// Step along the reflected direction for the post-collision distance
		while (distanceTraveled < _trajectoryPostCollisionDistance)
		{
			position += direction * _trajectoryStepSize;
			distanceTraveled += _trajectoryStepSize;
			points.Add(position);
		}
		
		return points.ToArray();
	}

	// Public accessors
	public bool IsInputEnabled() => CanAcceptInput();
	public CarromPiece GetStriker() => _striker;
	public float GetMaxStrikePowerValue() => GetMaxStrikePower();
	public float GetDeadZone() => DeadZone;
	public string GetGameStateStatus() => _gameState?.GetCurrentStateName() ?? "No game state";
	public bool HasGameState() => _gameState != null;
}