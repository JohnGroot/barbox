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

	[ExportCategory("Input Settings")]
	[Export] public float MaxStrikePower { get; set; } = 2000.0f;
	[Export] public float MinStrikePower { get; set; } = 200.0f;
	[Export] public float DeadZone { get; set; } = 30.0f;
	[Export] public float MaxAimDistance { get; set; } = 200.0f;
	[Export] public float LateralSensitivity { get; set; } = 1.5f; // Sensitivity for lateral striker movement
	[Export(PropertyHint.Range, "5.0,90.0,1.0")] public float LateralAngleThreshold { get; set; } = 45.0f; // Degrees, range 5-90°

	[ExportCategory("Visual Feedback")]
	[Export] public bool ShowAimingLine { get; set; } = true;
	[Export] public bool ShowPowerIndicator { get; set; } = true;
	[Export] public Color AimingLineColor { get; set; } = Colors.Yellow;
	[Export] public Color PowerBarColor { get; set; } = Colors.Green;
	[Export] public float AimingLineWidth { get; set; } = 3.0f;

	// Input state
	private CarromPiece _striker;
	private bool _isAiming = false;
	private Vector2 _aimStartPosition;
	private Vector2 _currentAimPosition;
	private Vector2 _strikerStartPosition;
	private InputMode _currentInputMode = InputMode.None;

	// Input modes
	private enum InputMode
	{
		None,
		LateralMovement,  // Moving striker along baseline
		PowerAiming       // Aiming for strike power
	}

	// Baseline tracking
	private Vector2[] _baselinePositions;
	private int _currentPlayerIndex = 0;
	private CarromBoard _board;
	private CarromCameraController _cameraController;
	private CarromPhaseManager _phaseManager;

	public override void _Ready()
	{
		SetProcessInput(true);
		GD.Print("[CarromInputController] Input controller ready - waiting for explicit initialization");
	}

	/// <summary>
	/// Initialize with board reference - called explicitly by CarromGame
	/// </summary>
	public void InitializeWithBoard(CarromBoard board)
	{
		_board = board;
		
		if (_board != null)
		{
			GD.Print("[CarromInputController] Initialized with CarromBoard successfully");
			UpdateBaselinePositions();
		}
		else
		{
			GD.PrintErr("[CarromInputController] Initialized with null CarromBoard");
		}
	}
	
	/// <summary>
	/// Set the phase manager for input validation
	/// </summary>
	public void SetPhaseManager(CarromPhaseManager phaseManager)
	{
		_phaseManager = phaseManager;
		GD.Print("[CarromInputController] Phase manager set");
	}

	/// <summary>
	/// Set the camera controller for coordinate transformations
	/// </summary>
	public void SetCameraController(CarromCameraController cameraController)
	{
		_cameraController = cameraController;
		GD.Print("[CarromInputController] Camera controller set");
	}

	/// <summary>
	/// Update baseline positions for current player
	/// </summary>
	private void UpdateBaselinePositions()
	{
		if (_board != null)
		{
			_baselinePositions = _board.GetBaselinePositions(_currentPlayerIndex);
			GD.Print($"[CarromInputController] Baseline positions updated for player {_currentPlayerIndex}: {_baselinePositions?.Length} positions");
		}
	}

	/// <summary>
	/// Handle input events
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
		if (!CanStartInput()) return;

		// Convert screen position to board coordinates using camera
		Vector2 boardPosition = _cameraController?.ScreenToBoardPosition(screenPosition) ?? screenPosition;
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

		QueueRedraw(); // Show aiming indicators
		GD.Print("[CarromInputController] Input started");
	}

	/// <summary>
	/// Update input during drag
	/// </summary>
	private void UpdateInput(Vector2 screenPosition)
	{
		// Convert screen position to board coordinates using camera
		_currentAimPosition = _cameraController?.ScreenToBoardPosition(screenPosition) ?? screenPosition;
		
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
				break;
		}
		
		QueueRedraw(); // Update visual feedback
	}

	/// <summary>
	/// Determine input mode based on initial movement direction
	/// </summary>
	private void DetermineInputMode(Vector2 inputVector)
	{
		Vector2 backwardDirection = GetBackwardDirection();
		
		// Check if input is backward (power aiming mode)
		float backwardComponent = inputVector.Normalized().Dot(backwardDirection);
		bool isBackwardMovement = backwardComponent > 0.0f; // Any positive backward component
		
		// Use power aiming for backward pull, lateral movement for everything else
		if (isBackwardMovement)
		{
			_currentInputMode = InputMode.PowerAiming;
			GD.Print("[CarromInputController] Power aiming mode (backward pull)");
		}
		else
		{
			_currentInputMode = InputMode.LateralMovement;
			GD.Print("[CarromInputController] Lateral movement mode (forward/lateral movement)");
		}
	}

	/// <summary>
	/// Handle lateral striker movement along baseline
	/// </summary>
	private void HandleLateralMovement(Vector2 inputVector)
	{
		if (_baselinePositions == null || _baselinePositions.Length == 0) return;
		
		Vector2 baselineDirection = GetBaselineDirection();
		float lateralMovement = inputVector.Dot(baselineDirection) * LateralSensitivity;
		
		// Calculate new position by offsetting from the original striker start position
		// Since board is at origin, striker positions are already in board coordinates
		Vector2 newPosition = _strikerStartPosition + baselineDirection * lateralMovement;
		Vector2 clampedPosition = ClampToBaseline(newPosition);
		
		// Move striker to new position (no coordinate conversion needed)
		_striker.GlobalPosition = clampedPosition;
		EmitSignal(SignalName.StrikerPositionChanged, clampedPosition);
		
		// Update aiming feedback - no aiming direction for lateral movement
		Vector2 aimDirection = Vector2.Zero;
		float power = 0.0f;
		EmitSignal(SignalName.AimingStateChanged, false, aimDirection, power);
	}

	/// <summary>
	/// Handle power aiming for strike
	/// </summary>
	private void HandlePowerAiming(Vector2 inputVector)
	{
		// Calculate power based on total input distance
		float inputDistance = inputVector.Length();
		float clampedDistance = Mathf.Clamp(inputDistance, 0, MaxAimDistance);
		float power = clampedDistance / MaxAimDistance;
		
		// Calculate aim direction from input vector, then clamp to valid angle range
		Vector2 rawAimDirection = -inputVector.Normalized(); // Opposite of pull direction
		Vector2 forwardDirection = GetForwardDirection();
		
		// Clamp aim direction to valid angle range (±60° from forward)
		float maxAngleRad = Mathf.DegToRad(60.0f);
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

		_isAiming = false;
		_currentInputMode = InputMode.None;
		EmitSignal(SignalName.AimingStateChanged, false, Vector2.Zero, 0.0f);
		QueueRedraw(); // Hide aiming indicators
		
		GD.Print("[CarromInputController] Input ended");
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
			float strikePower = Mathf.Lerp(MinStrikePower, MaxStrikePower, powerRatio);
			
			// Calculate strike direction with angle clamping (same as HandlePowerAiming)
			Vector2 rawStrikeDirection = -inputVector.Normalized(); // Opposite of pull direction
			Vector2 forwardDirection = GetForwardDirection();
			
			// Clamp strike direction to valid angle range (±60° from forward)
			float maxAngleRad = Mathf.DegToRad(60.0f);
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
			
			EmitSignal(SignalName.StrikeExecuted, strikeForce);
			GD.Print($"[CarromInputController] Strike executed: power={strikePower:F0}, direction={strikeDirection}");
		}
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
	/// Get backward direction for power aiming
	/// </summary>
	private Vector2 GetBackwardDirection()
	{
		// Direction away from the board center
		return _currentPlayerIndex switch
		{
			0 => Vector2.Down,  // Player 1: pull down
			1 => Vector2.Up,    // Player 2: pull up
			2 => Vector2.Left,  // Player 3: pull left
			3 => Vector2.Right, // Player 4: pull right
			_ => Vector2.Down
		};
	}

	/// <summary>
	/// Clamp position to baseline using smooth projection
	/// </summary>
	private Vector2 ClampToBaseline(Vector2 position)
	{
		if (_baselinePositions == null || _baselinePositions.Length < 2)
		{
			return position;
		}

		// Get baseline endpoints (first and last positions)
		Vector2 baselineStart = _baselinePositions[0];
		Vector2 baselineEnd = _baselinePositions[_baselinePositions.Length - 1];
		
		// Project position onto baseline line segment
		Vector2 baselineVector = baselineEnd - baselineStart;
		Vector2 positionVector = position - baselineStart;
		
		// Calculate projection parameter (0.0 = start, 1.0 = end)
		float projectionParam = positionVector.Dot(baselineVector) / baselineVector.LengthSquared();
		
		// Clamp to baseline segment bounds
		projectionParam = Mathf.Clamp(projectionParam, 0.0f, 1.0f);
		
		// Calculate final projected position
		Vector2 projectedPosition = baselineStart + baselineVector * projectionParam;
		
		return projectedPosition;
	}

	/// <summary>
	/// Check if input can be started
	/// </summary>
	private bool CanStartInput()
	{
		bool hasStriker = _striker != null;
		bool strikerStopped = hasStriker && _striker.IsStopped();
		bool canAccept = CanAcceptInput();
		
		if (!hasStriker)
		{
			GD.Print("[CarromInputController] Input blocked - no striker available");
		}
		else if (!strikerStopped)
		{
			GD.Print($"[CarromInputController] Input blocked - striker not stopped (frozen: {_striker.Freeze})");
		}
		else if (!canAccept)
		{
			GD.Print("[CarromInputController] Input blocked - phase not accepting input");
		}
		
		return hasStriker && strikerStopped && canAccept;
	}
	
	/// <summary>
	/// Check if input can be accepted based on phase state
	/// </summary>
	private bool CanAcceptInput()
	{
		// Use phase manager if available, fallback to default enabled state
		if (_phaseManager != null)
		{
			bool canReceive = _phaseManager.CanReceiveInput();
			if (!canReceive)
			{
				GD.Print($"[CarromInputController] Input blocked - phase manager reports: {_phaseManager.GetPhaseStatus()}");
			}
			return canReceive;
		}
		
		// Fallback for cases where phase manager isn't set (development/testing)
		return true;
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
	/// Draw aiming line
	/// </summary>
	private void DrawAimingLine(Vector2 strikerPos, Vector2 currentPos)
	{
		Vector2 inputVector = currentPos - _aimStartPosition;
		float inputDistance = inputVector.Length();
		
		if (inputDistance > DeadZone)
		{
			// Calculate aim direction with angle clamping (same as HandlePowerAiming)
			Vector2 rawAimDirection = -inputVector.Normalized(); // Opposite of pull direction
			Vector2 forwardDirection = GetForwardDirection();
			
			// Clamp aim direction to valid angle range (±60° from forward)
			float maxAngleRad = Mathf.DegToRad(60.0f);
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
			
			Vector2 aimEnd = strikerPos + aimDirection * 100.0f; // Fixed length for visibility
			
			DrawLine(strikerPos, aimEnd, AimingLineColor, AimingLineWidth);
			
			// Draw arrowhead
			Vector2 arrowBase = aimEnd - aimDirection * 15.0f;
			Vector2 perpendicular = aimDirection.Rotated(Mathf.Pi / 2) * 8.0f;
			
			DrawLine(aimEnd, arrowBase + perpendicular, AimingLineColor, AimingLineWidth);
			DrawLine(aimEnd, arrowBase - perpendicular, AimingLineColor, AimingLineWidth);
		}
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
			float barWidth = 60.0f;
			float barHeight = 8.0f;
			
			// Background
			DrawRect(new Rect2(barPosition - Vector2.Right * barWidth / 2, 
							   new Vector2(barWidth, barHeight)), Colors.Gray);
			
			// Power fill
			Color powerColor = power > 0.8f ? Colors.Red : 
							  power > 0.5f ? Colors.Yellow : PowerBarColor;
			DrawRect(new Rect2(barPosition - Vector2.Right * barWidth / 2, 
							   new Vector2(barWidth * power, barHeight)), powerColor);
		}
	}

	/// <summary>
	/// Set the striker for input handling
	/// </summary>
	public void SetStriker(CarromPiece striker)
	{
		_striker = striker;
		GD.Print($"[CarromInputController] Striker set: {striker?.Type}");
	}

	/// <summary>
	/// Set current player for baseline calculations
	/// </summary>
	public void SetCurrentPlayer(int playerIndex)
	{
		_currentPlayerIndex = playerIndex;
		UpdateBaselinePositions();
		GD.Print($"[CarromInputController] Current player set to: {playerIndex}");
	}

	/// <summary>
	/// Enable or disable input (deprecated - use phase manager instead)
	/// </summary>
	[System.Obsolete("Use CarromPhaseManager for input state control")]
	public void SetEnabled(bool enabled)
	{
		GD.PrintErr("[CarromInputController] SetEnabled is deprecated - use CarromPhaseManager instead");
		
		if (!enabled && _isAiming)
		{
			EndInput();
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

	// Public accessors
	public bool IsInputEnabled() => CanAcceptInput();
	public CarromPiece GetStriker() => _striker;
	public float GetMaxStrikePower() => MaxStrikePower;
	public float GetDeadZone() => DeadZone;
	public string GetPhaseStatus() => _phaseManager?.GetPhaseStatus() ?? "No phase manager";
	public bool HasPhaseManager() => _phaseManager != null;
}