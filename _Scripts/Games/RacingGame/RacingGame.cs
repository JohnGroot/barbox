using Godot;
using System.Collections.Generic;

/// <summary>
/// 2D time trial racing game with daily seeded race tracks
/// Car drives toward finger/mouse position with distance-based speed control
/// Track generation from spline with random offset points and minimum distance constraints
/// </summary>
[GlobalClass]
public partial class RacingGame : Node2D
{
	// Optional GameHost integration signals
	[Signal] public delegate void GameStartedEventHandler();
	[Signal] public delegate void GameEndedEventHandler();
	[Signal] public delegate void ScoreChangedEventHandler(string playerId, float lapTime);
	[Signal] public delegate void PlayerAddedEventHandler(string playerId);

	// Game settings
	[ExportCategory("Track Settings")]
	[Export] public Godot.Collections.Array<PackedScene> TrackScenes { get; set; } = new Godot.Collections.Array<PackedScene>();

	[ExportCategory("Game Settings")]
	[Export] public float MaxSpeed { get; set; } = 400.0f;
	[Export] public float MinSpeed { get; set; } = 50.0f;
	[Export] public float MaxInputDistance { get; set; } = 300.0f;
	[Export] public float AccelerationRate { get; set; } = 800.0f;
	[Export] public float DecelerationRate { get; set; } = 600.0f;

	[ExportCategory("Track Performance")]
	[Export] public float CenterLineProximityRange { get; set; } = 40.0f;
	[Export] public float CenterLineAccelerationBonus { get; set; } = 1.5f;
	[Export] public float TrackProximityRange { get; set; } = 20.0f;

	[ExportCategory("Off-Track Penalties")]
	[Export] public float OffTrackSpeedPenalty { get; set; } = 0.3f;
	[Export] public float OffTrackTurnPenalty { get; set; } = 0.5f;
	[Export] public float OffTrackAccelerationPenalty { get; set; } = 0.2f;

	[ExportCategory("Car Settings")]
	[Export] public Vector2 CarSize { get; set; } = new Vector2(80, 160);
	[Export] public float RotationLerpSpeed { get; set; } = 5.0f;

	[ExportCategory("Visual Feedback")]
	[Export] public Color InputLineActiveColor { get; set; } = Colors.White;
	[Export] public Color InputLineInactiveColor { get; set; } = Colors.Gray;
	[Export] public Color MouseMovingColor { get; set; } = Colors.Cyan;
	[Export] public Color MouseStationaryColor { get; set; } = Colors.LimeGreen;
	[Export] public Color TireTrailColor { get; set; } = Colors.DarkGray;
	[Export] public float InputLineWidth { get; set; } = 3.0f;
	[Export] public float MouseIndicatorRadius { get; set; } = 25.0f;
	[Export] public float TireTrailWidth { get; set; } = 2.0f;
	[Export] public int MaxTrailPoints { get; set; } = 60;
	[Export] public float TrailUpdateDistance { get; set; } = 3.0f;
	[Export] public float TrailLifetime { get; set; } = 3000.0f; // How long trails stay visible (milliseconds)

	// Game state
	private bool _gameActive = false;
	private bool _gamePaused = false;
	private bool _inTimeTrialMode = false;
	private bool _inCountdown = false;
	private float _countdownTime = 0.0f;
	private int _countdownNumber = 0;
	private string _playerId = "player1";
	private float _currentLapTime = 0.0f;
	private float _bestLapTime = float.MaxValue;
	private int _currentLap = 0;
	private int _targetLaps = 3;

	// Enhanced timing data
	private List<float> _completedLapTimes = new List<float>();
	private List<List<float>> _lapGapTimes = new List<List<float>>(); // Gap times for each checkpoint in each lap
	private float _lastCompletedLapTime = 0.0f;
	private float _currentGapTime = 0.0f; // Time since last checkpoint (practice mode)
	private float _lastCheckpointTime = 0.0f;

	// Context detection
	private GameHost _gameHost;
	private bool _isProductionContext = false;

	// Car physics
	private CharacterBody2D _car;
	private Vector2 _targetPosition;
	private Vector2 _lastTargetPosition;
	private bool _hasInput = false;
	private Vector2 _velocity = Vector2.Zero;
	private float _currentSpeed = 0.0f;

	// Visual feedback
	private List<(Vector2 position, ulong timestamp)> _leftTireTrail = new List<(Vector2, ulong)>();
	private List<(Vector2 position, ulong timestamp)> _rightTireTrail = new List<(Vector2, ulong)>();
	private Vector2 _lastTrailPosition = Vector2.Zero;
	private bool _wasInputActive = false;
	private Vector2 _lastMousePosition = Vector2.Zero;
	private float _mouseStationaryTime = 0.0f;
	private float _arcRotation = 0.0f;

	// Centralized cached drawing/physics values
	private Vector2 _cachedCarForward = Vector2.Zero;
	private Vector2 _cachedCarRight = Vector2.Zero;
	private Vector2 _cachedCarFrontPosition = Vector2.Zero;
	private Vector2 _cachedCarBackPosition = Vector2.Zero;
	private Vector2 _cachedLeftTirePosition = Vector2.Zero;
	private Vector2 _cachedRightTirePosition = Vector2.Zero;
	private Vector2 _cachedClampedTarget = Vector2.Zero;
	private Vector2 _cachedDirectionToTarget = Vector2.Zero;
	private float _cachedDistanceToTarget = 0.0f;
	private Color _cachedInputLineColor = Colors.White;
	private bool _cachedIsMouseMoving = false;
	private float _cachedArcSweepAngle = 0.0f;
	private float _cachedMovementArcRotation = 0.0f;

	// Track system
	private TrackDefinition _trackDefinition;
	private Curve2D _trackCurve;
	private bool _crossedStartLine = false;
	private float _lastStartLineCross = 0.0f;

	// Checkpoint system
	private CheckpointTrigger[] _checkpointTriggers;
	private bool[] _checkpointsCrossed;
	private int _nextCheckpointIndex = 0;


	// Built-in UI elements
	private CanvasLayer _uiLayer;
	private Label _speedLabel;
	private Label _lapLabel;
	private Label _timeLabel;
	private Label _modeLabel;
	private Label _proximityLabel;
	private Button _pauseButton;
	private Button _timeTrialButton;
	private Control _pauseOverlay;
	private Control _gameOverOverlay;
	private Label _finalTimeLabel;

	// Enhanced timing UI
	private Label _gapTimeLabel;
	private Label _lastLapLabel;
	private VBoxContainer _lapTimesContainer;
	private ScrollContainer _timingScrollContainer;

	// Countdown UI
	private Control _countdownOverlay;
	private Label _countdownLabel;

	// Daily seeded track generation
	private const string TRACK_SEED_KEY = "track_seed_";

	public override void _Ready()
	{
		SetupUI();
		SetupCar();
		InitializeTrackGenerator();
		DetectAndAdaptToContext();
	}

	public override void _Process(double delta)
	{
		if (!_gameActive || _gamePaused) return;

		float deltaF = (float)delta;

		// Handle countdown for time attack
		if (_inCountdown)
		{
			HandleCountdown(deltaF);
			return; // Don't process other game logic during countdown
		}

		// Update lap timer for time attack mode
		if (_inTimeTrialMode && _currentLap > 0)
		{
			_currentLapTime += deltaF;
		}

		// Update gap timer for practice mode (time since last checkpoint)
		if (!_inTimeTrialMode && _gameActive)
		{
			_currentGapTime += deltaF;
		}

		UpdateCarPhysics(deltaF);
		CheckStartLineCollision();
		UpdateCachedValues(deltaF);
		UpdateUI();
		UpdateVisualFeedback(deltaF);
	}

	private void HandleCountdown(float delta)
	{
		_countdownTime += delta;

		int newCountdownNumber = 4 - (int)(_countdownTime / 1.0f);
		
		if (newCountdownNumber != _countdownNumber)
		{
			_countdownNumber = newCountdownNumber;
			
			if (_countdownNumber > 0)
			{
				_countdownLabel.Text = _countdownNumber.ToString();
				GD.Print($"Countdown: {_countdownNumber}");
			}
			else
			{
				_countdownLabel.Text = "GO!";
				GD.Print("Countdown: GO!");
			}
		}

		// End countdown after 4 seconds (3-2-1-GO)
		if (_countdownTime >= 4.0f)
		{
			EndCountdown();
		}
	}

	private void EndCountdown()
	{
		_inCountdown = false;
		_countdownOverlay.Visible = false;
		
		// Actually start the time trial now
		_currentLap = 0;
		_currentLapTime = 0.0f;
		
		GD.Print("Time Trial Started!");
	}

	public override void _Input(InputEvent inputEvent)
	{
		if (!_gameActive || _gamePaused || _inCountdown) return;

		// Handle pause key
		if (inputEvent.IsActionPressed("ui_cancel") || 
		   (inputEvent is InputEventKey key && key.Keycode == Key.Escape))
		{
			TogglePause();
			return;
		}

		Vector2 inputPosition = Vector2.Zero;
		bool inputActive = false;

		// Handle touch input
		if (inputEvent is InputEventScreenTouch touch)
		{
			if (touch.Pressed)
			{
				inputPosition = touch.Position;
				inputActive = true;
			}
			else
			{
				inputActive = false;
			}
		}
		else if (inputEvent is InputEventScreenDrag drag)
		{
			inputPosition = drag.Position;
			inputActive = true;
		}
		// Handle mouse input
		else if (inputEvent is InputEventMouseButton mouse)
		{
			if (mouse.ButtonIndex == MouseButton.Left)
			{
				if (mouse.Pressed)
				{
					inputPosition = mouse.Position;
					inputActive = true;
				}
				else
				{
					inputActive = false;
				}
			}
		}
		else if (inputEvent is InputEventMouseMotion motion && _hasInput)
		{
			inputPosition = motion.Position;
			inputActive = true;
		}

		if (inputActive)
		{
			_targetPosition = inputPosition;
			_lastTargetPosition = inputPosition;
			_hasInput = true;
		}
		else
		{
			_hasInput = false;
		}
	}

	public override void _Draw()
	{
		if (!_gameActive || _gamePaused || _inCountdown || _car == null) return;

		// Draw input line from car front to mouse position
		DrawInputLine();

		// Draw mouse position indicators
		DrawMouseIndicators();

		// Draw tire trails
		DrawTireTrails();
	}

	private void DrawInputLine()
	{
		if (_targetPosition == Vector2.Zero) return;
		
		// Only draw if there's input or car is still moving
		if (!_hasInput && _currentSpeed < 1.0f) return;

		// Use cached values - no recalculation needed
		DrawDashedLine(_cachedCarFrontPosition, _cachedClampedTarget, _cachedInputLineColor, InputLineWidth, 10.0f, 5.0f);
	}

	private void DrawMouseIndicators()
	{
		if (_targetPosition == Vector2.Zero) return;
		
		// Only draw indicators if there's input or car is still moving
		if (!_hasInput && _currentSpeed < 1.0f) return;

		// Draw circle at the end of the input line (clamped target position)
		if (_cachedClampedTarget != Vector2.Zero)
		{
			var circleColor = MouseStationaryColor;
			DrawCircle(_cachedClampedTarget, MouseIndicatorRadius * 0.4f, circleColor);
		}

		// Draw arc under finger/mouse position only if there's active input
		if (_hasInput)
		{
			var mousePos = _targetPosition;
			var arcColor = MouseMovingColor;
			
			// Use movement-based rotation and speed-based sweep angle
			var centerDirection = _cachedMovementArcRotation;
			var sweepAngle = _cachedArcSweepAngle;
			
			// Only draw arc if there's some sweep angle (i.e., car is moving)
			if (sweepAngle > 0.1f)
			{
				// Center the arc on the input direction by offsetting start angle
				var startAngle = centerDirection - (sweepAngle / 2.0f);
				var endAngle = centerDirection + (sweepAngle / 2.0f);
				DrawArc(mousePos, MouseIndicatorRadius, startAngle, endAngle, 32, arcColor, 3.0f, true);
			}
		}
	}

	private void DrawTireTrails()
	{
		DrawTrailLine(_leftTireTrail);
		DrawTrailLine(_rightTireTrail);
	}

	private void DrawTrailLine(List<(Vector2 position, ulong timestamp)> trail)
	{
		if (trail.Count < 2) return;

		var currentTime = Time.GetTicksMsec();

		for (int i = 0; i < trail.Count - 1; i++)
		{
			var age = (float)(currentTime - trail[i].timestamp);
			var normalizedAge = Mathf.Clamp(age / TrailLifetime, 0.0f, 1.0f);
			var alpha = 1.0f - normalizedAge; // Fade out as trail gets older
			
			var trailColor = TireTrailColor * new Color(1, 1, 1, alpha * 0.8f);
			DrawLine(trail[i].position, trail[i + 1].position, trailColor, TireTrailWidth);
		}
	}

	private void DrawDashedLine(Vector2 from, Vector2 to, Color color, float width, float dashLength, float gapLength)
	{
		var direction = (to - from).Normalized();
		var totalDistance = from.DistanceTo(to);
		var segmentLength = dashLength + gapLength;
		
		var currentPos = from;
		var distanceTraveled = 0.0f;

		while (distanceTraveled < totalDistance)
		{
			var remainingDistance = totalDistance - distanceTraveled;
			var currentDashLength = Mathf.Min(dashLength, remainingDistance);
			
			var dashEnd = currentPos + direction * currentDashLength;
			DrawLine(currentPos, dashEnd, color, width);
			
			distanceTraveled += segmentLength;
			currentPos += direction * segmentLength;
		}
	}

	private void UpdateCachedValues(float delta)
	{
		if (_car == null) return;

		// Calculate car orientation vectors
		_cachedCarForward = new Vector2(Mathf.Sin(_car.Rotation), -Mathf.Cos(_car.Rotation));
		_cachedCarRight = new Vector2(_cachedCarForward.Y, -_cachedCarForward.X);

		// Calculate car positions relative to car size
		//_cachedCarRight * (-CarSize.X * 0.075f) + 
		var halfLength = CarSize.Y * 0.5f; // Half of car length
		var halfWidth = CarSize.X * 0.5f; // Half of car width
		var frontOffset = _cachedCarRight * -halfWidth * 0.75f + _cachedCarForward * halfLength * 0.25f;
		var backOffset = _cachedCarRight * -halfWidth * 0.75f - _cachedCarForward * halfLength * 1.5f;
		
		_cachedCarFrontPosition = _car.GlobalPosition + frontOffset;
		_cachedCarBackPosition = _car.GlobalPosition + backOffset;

		// Calculate tire positions (back corners of the car)
		var tireOffset = halfWidth * 0.625f; // 62.5% of half width for tire spacing
		_cachedLeftTirePosition = _cachedCarBackPosition - _cachedCarRight * tireOffset;
		_cachedRightTirePosition = _cachedCarBackPosition + _cachedCarRight * tireOffset;

		// Calculate target-related values
		if (_targetPosition != Vector2.Zero)
		{
			_cachedDirectionToTarget = (_targetPosition - _car.GlobalPosition);
			_cachedDistanceToTarget = _cachedDirectionToTarget.Length();
			_cachedClampedTarget = _car.GlobalPosition + _cachedDirectionToTarget.Normalized() * Mathf.Min(_cachedDistanceToTarget, MaxInputDistance);
		}
		else
		{
			_cachedDirectionToTarget = Vector2.Zero;
			_cachedDistanceToTarget = 0.0f;
			_cachedClampedTarget = Vector2.Zero;
		}

		// Calculate input line color
		_cachedInputLineColor = _hasInput ? InputLineActiveColor : 
			(_currentSpeed > 1.0f ? InputLineInactiveColor : InputLineInactiveColor * new Color(1, 1, 1, 0.3f));

		// Calculate mouse movement state
		_cachedIsMouseMoving = _mouseStationaryTime < 0.2f;

		// Calculate arc rotation based on input direction (direction of input line)
		if (_cachedDirectionToTarget.Length() > 0.1f)
		{
			_cachedMovementArcRotation = _cachedDirectionToTarget.Angle();
		}

		// Calculate arc sweep angle based on current speed (0 to 270 degrees)
		var normalizedSpeed = Mathf.Clamp(_currentSpeed / MaxSpeed, 0.0f, 1.0f);
		_cachedArcSweepAngle = Mathf.Lerp(0.0f, Mathf.Pi , normalizedSpeed); // 0 to 270 degrees
	}

	private void UpdateVisualFeedback(float delta)
	{
		// Track mouse movement state
		if (_targetPosition != _lastMousePosition)
		{
			_mouseStationaryTime = 0.0f;
			_lastMousePosition = _targetPosition;
		}
		else
		{
			_mouseStationaryTime += delta;
		}

		// Update arc rotation for moving indicator
		_arcRotation += delta * 3.0f; // Rotate at 3 rad/s
		if (_arcRotation > Mathf.Pi * 2) _arcRotation -= Mathf.Pi * 2;

		// Track input state changes for visual feedback
		if (_hasInput != _wasInputActive)
		{
			_wasInputActive = _hasInput;
			QueueRedraw(); // Trigger redraw when input state changes
		}

		// Queue redraw for animated elements
		if (_hasInput || _mouseStationaryTime < 2.0f || _currentSpeed > 1.0f)
		{
			QueueRedraw();
		}
	}

	private void UpdateTireTrails()
	{
		if (_car == null) return;

		var currentTime = Time.GetTicksMsec();

		// Clean up expired trail points
		CleanupExpiredTrailPoints(_leftTireTrail, currentTime);
		CleanupExpiredTrailPoints(_rightTireTrail, currentTime);

		// Only create new trail points when moving at decent speed
		if (_currentSpeed < 5.0f) return;

		var carPosition = _car.GlobalPosition;
		
		// Only update trails if car has moved significantly
		if (_lastTrailPosition.DistanceTo(carPosition) < TrailUpdateDistance) return;
		
		// Use cached tire positions with timestamp
		AddTrailPoint(_leftTireTrail, _cachedLeftTirePosition, currentTime);
		AddTrailPoint(_rightTireTrail, _cachedRightTirePosition, currentTime);
		
		_lastTrailPosition = carPosition;
	}

	private void AddTrailPoint(List<(Vector2 position, ulong timestamp)> trail, Vector2 point, ulong timestamp)
	{
		trail.Add((point, timestamp));
		
		// Remove old points to maintain max trail length (as backup limit)
		while (trail.Count > MaxTrailPoints)
		{
			trail.RemoveAt(0);
		}
	}

	private void CleanupExpiredTrailPoints(List<(Vector2 position, ulong timestamp)> trail, ulong currentTime)
	{
		// Remove points that are older than TrailLifetime
		for (int i = trail.Count - 1; i >= 0; i--)
		{
			var age = (float)(currentTime - trail[i].timestamp);
			if (age > TrailLifetime)
			{
				trail.RemoveAt(i);
			}
		}
	}

	private void ClearTireTrails()
	{
		_leftTireTrail.Clear();
		_rightTireTrail.Clear();
		_lastTrailPosition = Vector2.Zero;
	}

	private void SetupUI()
	{
		_uiLayer = new CanvasLayer();
		AddChild(_uiLayer);

		// Main game UI
		var mainUI = new Control();
		mainUI.AnchorLeft = 0;
		mainUI.AnchorTop = 0;
		mainUI.AnchorRight = 1;
		mainUI.AnchorBottom = 1;
		mainUI.MouseFilter = Control.MouseFilterEnum.Ignore;
		_uiLayer.AddChild(mainUI);

		// Top bar with game info
		var topBar = new HBoxContainer();
		topBar.AnchorLeft = 0;
		topBar.AnchorTop = 0;
		topBar.AnchorRight = 1;
		topBar.AnchorBottom = 0;
		topBar.OffsetBottom = 60;
		topBar.Alignment = BoxContainer.AlignmentMode.Center;
		mainUI.AddChild(topBar);

		_speedLabel = new Label();
		_speedLabel.Text = "Speed: 0";
		_speedLabel.HorizontalAlignment = HorizontalAlignment.Center;
		topBar.AddChild(_speedLabel);

		var separator1 = new VSeparator();
		topBar.AddChild(separator1);

		_lapLabel = new Label();
		_lapLabel.Text = "Practice Mode";
		_lapLabel.HorizontalAlignment = HorizontalAlignment.Center;
		topBar.AddChild(_lapLabel);

		var separator2 = new VSeparator();
		topBar.AddChild(separator2);

		_timeLabel = new Label();
		_timeLabel.Text = "Time: 0.0s";
		_timeLabel.HorizontalAlignment = HorizontalAlignment.Center;
		topBar.AddChild(_timeLabel);

		var separator3 = new VSeparator();
		topBar.AddChild(separator3);

		_proximityLabel = new Label();
		_proximityLabel.Text = "Track: Normal";
		_proximityLabel.HorizontalAlignment = HorizontalAlignment.Center;
		topBar.AddChild(_proximityLabel);

		var separator4 = new VSeparator();
		topBar.AddChild(separator4);

		_pauseButton = new Button();
		_pauseButton.Text = "Pause";
		_pauseButton.Pressed += TogglePause;
		topBar.AddChild(_pauseButton);

		// Bottom controls
		var bottomBar = new HBoxContainer();
		bottomBar.AnchorLeft = 0;
		bottomBar.AnchorTop = 1;
		bottomBar.AnchorRight = 1;
		bottomBar.AnchorBottom = 1;
		bottomBar.OffsetTop = -60;
		bottomBar.Alignment = BoxContainer.AlignmentMode.Center;
		mainUI.AddChild(bottomBar);

		_timeTrialButton = new Button();
		_timeTrialButton.Text = "Start Time Trial (3 Laps)";
		_timeTrialButton.Pressed += StartTimeTrial;
		bottomBar.AddChild(_timeTrialButton);

		var separator5 = new VSeparator();
		bottomBar.AddChild(separator5);

		var restartButton = new Button();
		restartButton.Text = "Restart Practice";
		restartButton.Pressed += RestartPractice;
		bottomBar.AddChild(restartButton);

		SetupTimingPanel(mainUI);
		SetupCountdownOverlay();
		SetupPauseOverlay();
		SetupGameOverOverlay();
	}

	private void SetupTimingPanel(Control mainUI)
	{
		// Right-side timing panel
		var timingPanel = new Panel();
		timingPanel.AnchorLeft = 1;
		timingPanel.AnchorTop = 0;
		timingPanel.AnchorRight = 1;
		timingPanel.AnchorBottom = 1;
		timingPanel.OffsetLeft = -300;
		timingPanel.OffsetTop = 70;
		timingPanel.OffsetBottom = -70;
		timingPanel.MouseFilter = Control.MouseFilterEnum.Ignore;
		mainUI.AddChild(timingPanel);

		var timingVBox = new VBoxContainer();
		timingVBox.AnchorLeft = 0;
		timingVBox.AnchorTop = 0;
		timingVBox.AnchorRight = 1;
		timingVBox.AnchorBottom = 1;
		timingVBox.OffsetLeft = 10;
		timingVBox.OffsetTop = 10;
		timingVBox.OffsetRight = -10;
		timingVBox.OffsetBottom = -10;
		timingPanel.AddChild(timingVBox);

		// Gap time label (for practice mode)
		_gapTimeLabel = new Label();
		_gapTimeLabel.Text = "Gap: 0.0s";
		_gapTimeLabel.HorizontalAlignment = HorizontalAlignment.Left;
		timingVBox.AddChild(_gapTimeLabel);

		// Last lap time label (for practice mode after time attack)
		_lastLapLabel = new Label();
		_lastLapLabel.Text = "";
		_lastLapLabel.HorizontalAlignment = HorizontalAlignment.Left;
		_lastLapLabel.Visible = false;
		timingVBox.AddChild(_lastLapLabel);

		// Separator
		var separator = new HSeparator();
		timingVBox.AddChild(separator);

		// Scrollable container for lap times (time attack mode)
		_timingScrollContainer = new ScrollContainer();
		_timingScrollContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_timingScrollContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		_timingScrollContainer.Visible = false;
		timingVBox.AddChild(_timingScrollContainer);

		_lapTimesContainer = new VBoxContainer();
		_timingScrollContainer.AddChild(_lapTimesContainer);
	}

	private void SetupCountdownOverlay()
	{
		_countdownOverlay = new Control();
		_countdownOverlay.AnchorLeft = 0;
		_countdownOverlay.AnchorTop = 0;
		_countdownOverlay.AnchorRight = 1;
		_countdownOverlay.AnchorBottom = 1;
		_countdownOverlay.Visible = false;
		_uiLayer.AddChild(_countdownOverlay);

		var background = new ColorRect();
		background.AnchorLeft = 0;
		background.AnchorTop = 0;
		background.AnchorRight = 1;
		background.AnchorBottom = 1;
		background.Color = new Color(0, 0, 0, 0.5f);
		_countdownOverlay.AddChild(background);

		_countdownLabel = new Label();
		_countdownLabel.AnchorLeft = 0.5f;
		_countdownLabel.AnchorTop = 0.5f;
		_countdownLabel.AnchorRight = 0.5f;
		_countdownLabel.AnchorBottom = 0.5f;
		_countdownLabel.OffsetLeft = -100;
		_countdownLabel.OffsetTop = -50;
		_countdownLabel.OffsetRight = 100;
		_countdownLabel.OffsetBottom = 50;
		_countdownLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_countdownLabel.VerticalAlignment = VerticalAlignment.Center;
		_countdownLabel.Text = "3";
		
		// Make the countdown text very large and bold
		var labelSettings = new LabelSettings();
		labelSettings.FontSize = 72;
		labelSettings.FontColor = Colors.White;
		_countdownLabel.LabelSettings = labelSettings;
		
		_countdownOverlay.AddChild(_countdownLabel);
	}

	private void SetupPauseOverlay()
	{
		_pauseOverlay = new Control();
		_pauseOverlay.AnchorLeft = 0;
		_pauseOverlay.AnchorTop = 0;
		_pauseOverlay.AnchorRight = 1;
		_pauseOverlay.AnchorBottom = 1;
		_pauseOverlay.Visible = false;
		_uiLayer.AddChild(_pauseOverlay);

		var background = new ColorRect();
		background.AnchorLeft = 0;
		background.AnchorTop = 0;
		background.AnchorRight = 1;
		background.AnchorBottom = 1;
		background.Color = new Color(0, 0, 0, 0.7f);
		_pauseOverlay.AddChild(background);

		var pausePanel = new Panel();
		pausePanel.AnchorLeft = 0.5f;
		pausePanel.AnchorTop = 0.5f;
		pausePanel.AnchorRight = 0.5f;
		pausePanel.AnchorBottom = 0.5f;
		pausePanel.OffsetLeft = -150;
		pausePanel.OffsetTop = -100;
		pausePanel.OffsetRight = 150;
		pausePanel.OffsetBottom = 100;
		_pauseOverlay.AddChild(pausePanel);

		var vbox = new VBoxContainer();
		vbox.AnchorLeft = 0.5f;
		vbox.AnchorTop = 0.5f;
		vbox.AnchorRight = 0.5f;
		vbox.AnchorBottom = 0.5f;
		vbox.OffsetLeft = -75;
		vbox.OffsetTop = -50;
		vbox.OffsetRight = 75;
		vbox.OffsetBottom = 50;
		vbox.Alignment = BoxContainer.AlignmentMode.Center;
		pausePanel.AddChild(vbox);

		var pauseLabel = new Label();
		pauseLabel.Text = "PAUSED";
		pauseLabel.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(pauseLabel);

		var resumeButton = new Button();
		resumeButton.Text = "Resume";
		resumeButton.Pressed += TogglePause;
		vbox.AddChild(resumeButton);

		var restartButton = new Button();
		restartButton.Text = "Restart";
		restartButton.Pressed += RestartPractice;
		vbox.AddChild(restartButton);
	}

	private void SetupGameOverOverlay()
	{
		_gameOverOverlay = new Control();
		_gameOverOverlay.AnchorLeft = 0;
		_gameOverOverlay.AnchorTop = 0;
		_gameOverOverlay.AnchorRight = 1;
		_gameOverOverlay.AnchorBottom = 1;
		_gameOverOverlay.Visible = false;
		_uiLayer.AddChild(_gameOverOverlay);

		var background = new ColorRect();
		background.AnchorLeft = 0;
		background.AnchorTop = 0;
		background.AnchorRight = 1;
		background.AnchorBottom = 1;
		background.Color = new Color(0, 0, 0, 0.8f);
		_gameOverOverlay.AddChild(background);

		var gameOverPanel = new Panel();
		gameOverPanel.AnchorLeft = 0.5f;
		gameOverPanel.AnchorTop = 0.5f;
		gameOverPanel.AnchorRight = 0.5f;
		gameOverPanel.AnchorBottom = 0.5f;
		gameOverPanel.OffsetLeft = -200;
		gameOverPanel.OffsetTop = -150;
		gameOverPanel.OffsetRight = 200;
		gameOverPanel.OffsetBottom = 150;
		_gameOverOverlay.AddChild(gameOverPanel);

		var vbox = new VBoxContainer();
		vbox.AnchorLeft = 0.5f;
		vbox.AnchorTop = 0.5f;
		vbox.AnchorRight = 0.5f;
		vbox.AnchorBottom = 0.5f;
		vbox.OffsetLeft = -100;
		vbox.OffsetTop = -75;
		vbox.OffsetRight = 100;
		vbox.OffsetBottom = 75;
		vbox.Alignment = BoxContainer.AlignmentMode.Center;
		gameOverPanel.AddChild(vbox);

		var raceCompleteLabel = new Label();
		raceCompleteLabel.Text = "RACE COMPLETE!";
		raceCompleteLabel.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(raceCompleteLabel);

		_finalTimeLabel = new Label();
		_finalTimeLabel.Text = "Final Time: 0.0s";
		_finalTimeLabel.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(_finalTimeLabel);

		var raceAgainButton = new Button();
		raceAgainButton.Text = "Race Again";
		raceAgainButton.Pressed += StartTimeTrial;
		vbox.AddChild(raceAgainButton);

		var practiceButton = new Button();
		practiceButton.Text = "Practice Mode";
		practiceButton.Pressed += RestartPractice;
		vbox.AddChild(practiceButton);
	}

	private void SetupCar()
	{
		_car = new CharacterBody2D();

		var carColor = Colors.Red;
		
		// Car visual
		var carSprite = new ColorRect();
		carSprite.Size = CarSize;
		carSprite.Color = carColor;
		carSprite.Position = new Vector2(-CarSize.X * 0.125f, -CarSize.Y * 0.125f); // Offset by 12.5% of size
		_car.AddChild(carSprite);

		// Car collision
		var carCollision = new CollisionShape2D();
		var carShape = new RectangleShape2D();
		carShape.Size = CarSize;
		carCollision.Shape = carShape;
		_car.AddChild(carCollision);

		AddChild(_car);

		// Position car at center initially
		var viewport = GetViewport();
		if (viewport != null)
		{
			var screenSize = viewport.GetVisibleRect().Size;
			_car.GlobalPosition = screenSize / 2;
			_targetPosition = _car.GlobalPosition;
			_lastTargetPosition = _car.GlobalPosition;
		}
	}

	private void LoadTrack(int trackIndex)
	{
		if (TrackScenes == null || TrackScenes.Count == 0)
		{
			GD.PrintErr("RacingGame: No track scenes configured. Add track scenes to TrackScenes array.");
			return;
		}

		if (trackIndex < 0 || trackIndex >= TrackScenes.Count)
		{
			GD.PrintErr($"RacingGame: Invalid track index {trackIndex}. Available tracks: {TrackScenes.Count}");
			return;
		}

		// Clear existing track if any
		if (_trackDefinition != null)
		{
			DisconnectTrackSignals();
			_trackDefinition.QueueFree();
		}

		// Load and instantiate new track scene
		var trackScene = TrackScenes[trackIndex];
		if (trackScene == null)
		{
			GD.PrintErr($"RacingGame: Track scene at index {trackIndex} is null");
			return;
		}

		var trackInstance = trackScene.Instantiate();
		if (trackInstance is TrackDefinition trackDef)
		{
			_trackDefinition = trackDef;
			AddChild(_trackDefinition);
			
			// Center the track on screen
			var viewport = GetViewport();
			if (viewport != null)
			{
				var screenSize = viewport.GetVisibleRect().Size;
				var screenCenter = screenSize / 2;
				_trackDefinition.GlobalPosition = screenCenter;
				GD.Print($"RacingGame: Positioned track '{_trackDefinition.TrackName}' at screen center {screenCenter}");
			}
			
			GD.Print($"RacingGame: Loaded track '{_trackDefinition.TrackName}'");
		}
		else
		{
			GD.PrintErr($"RacingGame: Track scene at index {trackIndex} does not contain a TrackDefinition root node");
			trackInstance.QueueFree();
			return;
		}

		// Setup the loaded track
		SetupLoadedTrack();
	}

	private void SetupLoadedTrack()
	{
		if (_trackDefinition == null) return;

		// Setup track
		_trackDefinition.SetupTrack();
		_trackCurve = _trackDefinition.GetTrackCurve();
		
		if (_trackCurve == null)
		{
			GD.PrintErr("RacingGame: Track definition failed to provide a valid curve");
			return;
		}
		
		// Update target laps from track definition
		_targetLaps = _trackDefinition.NumberOfLaps;
		
		// Initialize checkpoint tracking from track definition
		InitializeCheckpointTracking();
		
		// Connect signals from track definition's child nodes
		ConnectTrackSignals();
		
		// Position car at start line
		PositionCarAtStart();
		
		// Auto-start practice mode in development context
		if (!_isProductionContext && !_gameActive)
		{
			StartPracticeMode();
		}
	}

	private void InitializeTrackGenerator()
	{
		// Try to load first available track
		if (TrackScenes != null && TrackScenes.Count > 0)
		{
			LoadTrack(0);
		}
		else
		{
			// Fallback: Look for existing TrackDefinition child node
			foreach (Node child in GetChildren())
			{
				if (child is TrackDefinition trackDef)
				{
					_trackDefinition = trackDef;
					GD.Print($"RacingGame: Using existing track definition '{child.Name}'");
					SetupLoadedTrack();
					return;
				}
			}
			GD.PrintErr("RacingGame: No track scenes configured and no TrackDefinition child node found.");
		}
	}


	private void InitializeCheckpointTracking()
	{
		if (_trackDefinition == null) return;

		_checkpointTriggers = _trackDefinition.CheckpointTriggers;
		if (_checkpointTriggers.Length == 0)
		{
			GD.Print("RacingGame: No checkpoints defined for this track");
			return;
		}

		// Initialize checkpoint tracking arrays
		_checkpointsCrossed = new bool[_checkpointTriggers.Length];
		_nextCheckpointIndex = 0;

		GD.Print($"RacingGame: Initialized tracking for {_checkpointTriggers.Length} checkpoints");
	}

	private void ConnectTrackSignals()
	{
		if (_trackDefinition == null) return;

		// Connect start line trigger signal
		var startLineTrigger = _trackDefinition.StartLine;
		if (startLineTrigger != null)
		{
			startLineTrigger.StartLineCrossed += OnStartLineTriggered;
		}

		// Connect checkpoint trigger signals
		var checkpointTriggers = _trackDefinition.CheckpointTriggers;
		for (int i = 0; i < checkpointTriggers.Length; i++)
		{
			var trigger = checkpointTriggers[i];
			if (trigger != null)
			{
				trigger.CheckpointCrossed += OnCheckpointTriggered;
			}
		}
	}

	private void PositionCarAtStart()
	{
		if (_trackDefinition == null || _car == null) return;

		var startPoint = _trackDefinition.GetStartLinePosition();
		var perpendicular = _trackDefinition.GetStartLineDirection();
		var trackDirection = new Vector2(perpendicular.Y, -perpendicular.X); // Convert perpendicular back to track direction

		// Position car at start line
		_car.GlobalPosition = startPoint - trackDirection * 50;
		_car.Rotation = trackDirection.Angle() + Mathf.Pi / 2;
	}


	private void UpdateCarPhysics(float delta)
	{
		if (_car == null) return;

		Vector2 inputDirection = Vector2.Zero;
		float targetSpeed = 0.0f;

		if (_hasInput)
		{
			// Calculate direction and distance to target
			var directionToTarget = (_targetPosition - _car.GlobalPosition);
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
			var directionToLastTarget = (_lastTargetPosition - _car.GlobalPosition);
			if (directionToLastTarget.Length() > 1.0f)
			{
				inputDirection = directionToLastTarget.Normalized();
			}
			targetSpeed = 0.0f;
		}

		// Apply track proximity modifiers	
		var carPosition = _car.GlobalPosition;
		var accelerationModifier = GetTrackAccelerationModifier(carPosition);
		var speedModifier = GetOffTrackSpeedModifier(carPosition);
		var turnModifier = GetOffTrackTurnModifier(carPosition);

		// Apply speed penalty for off-track
		targetSpeed *= speedModifier;

		// Update velocity with acceleration modifiers
		if (targetSpeed > _currentSpeed)
		{
			_currentSpeed = Mathf.MoveToward(_currentSpeed, targetSpeed, AccelerationRate * accelerationModifier * delta);
		}
		else
		{
			_currentSpeed = Mathf.MoveToward(_currentSpeed, targetSpeed, DecelerationRate * delta);
		}

		if (_currentSpeed > 0.1f && inputDirection != Vector2.Zero)
		{
			// Lerp car rotation towards target direction
			if (_hasInput)
			{
				var targetDirection = (_targetPosition - _car.GlobalPosition).Normalized();
				var targetAngle = targetDirection.Angle() + Mathf.Pi / 2;
				_car.Rotation = Mathf.LerpAngle(_car.Rotation, targetAngle, RotationLerpSpeed * delta);
			}
			else
			{
				// When not actively steering, rotate towards movement direction
				_car.Rotation = Mathf.LerpAngle(_car.Rotation, _velocity.Angle() + Mathf.Pi / 2, RotationLerpSpeed * delta);
			}
			
			// Calculate velocity relative to car's forward direction
			var carForward = new Vector2(Mathf.Sin(_car.Rotation), -Mathf.Cos(_car.Rotation));
			_velocity = carForward * _currentSpeed * turnModifier;
		}
		else
		{
			_velocity = Vector2.Zero;
		}

		// Apply movement
		_car.Velocity = _velocity;
		_car.MoveAndSlide();

		// Update tire trails
		UpdateTireTrails();
	}

	private void CheckStartLineCollision()
	{
		var startLineTrigger = _trackDefinition?.StartLine;
		if (startLineTrigger == null || _car == null) return;

		var carGlobalPos = _car.GlobalPosition;
		var startLinePos = startLineTrigger.GlobalPosition;
		var distance = carGlobalPos.DistanceTo(startLinePos);

		if (distance < 40.0f) // Within start line detection range
		{
			if (!_crossedStartLine)
			{
				_crossedStartLine = true;
				_lastStartLineCross = (float)Time.GetUnixTimeFromSystem();
				OnStartLineCrossed();
			}
		}
		else if (distance > 60.0f) // Far enough to reset detection
		{
			_crossedStartLine = false;
		}
	}

	private float GetDistanceToTrackCenterLine(Vector2 position)
	{
		if (_trackCurve == null) return float.MaxValue;
		
		var closestPoint = _trackCurve.GetClosestPoint(position);
		return position.DistanceTo(closestPoint);
	}

	private float GetTrackAccelerationModifier(Vector2 position)
	{
		if (_trackDefinition == null) 
			return 1.0f;
		
		// Check if position is on track
		if (!IsOnTrack(position))
		{
			return OffTrackAccelerationPenalty;
		}
		
		// Get distance to center line
		var distanceToCenter = GetDistanceToTrackCenterLine(position);
		
		// Apply center line bonus if within proximity range
		if (distanceToCenter <= CenterLineProximityRange)
		{
			return CenterLineAccelerationBonus;
		}
		
		// Normal acceleration when on track but not in center line bonus zone
		return 1.0f;
	}

	private bool IsOnTrack(Vector2 position)
	{
		if (_trackDefinition == null) return true;
		return _trackDefinition.IsValidTrackPoint(position);
	}

	private float GetOffTrackSpeedModifier(Vector2 position)
	{
		if (!IsOnTrack(position))
		{
			return OffTrackSpeedPenalty;
		}
		return 1.0f;
	}

	private float GetOffTrackTurnModifier(Vector2 position)
	{
		if (!IsOnTrack(position))
		{
			return OffTrackTurnPenalty;
		}
		return 1.0f;
	}

	private void OnStartLineTriggered(Node2D body)
	{
		if (body != _car) return;
		
		// Trigger-based start line detection
		if (!_crossedStartLine)
		{
			_crossedStartLine = true;
			_lastStartLineCross = (float)Time.GetUnixTimeFromSystem();
			OnStartLineCrossed();
		}
	}

	private void OnCheckpointTriggered(Node2D body, int checkpointIndex)
	{
		if (body != _car) return;

		// Handle checkpoint crossing for both practice and time attack modes
		if (_inTimeTrialMode)
		{
			// Time attack mode: check order and record gap times
			if (checkpointIndex == _nextCheckpointIndex)
			{
				_checkpointsCrossed[checkpointIndex] = true;
				_nextCheckpointIndex++;

				// Record gap time for this checkpoint in current lap
				if (_currentLap > 0 && _currentLap <= _lapGapTimes.Count)
				{
					var currentLapGapTimes = _lapGapTimes[_currentLap - 1];
					if (checkpointIndex < currentLapGapTimes.Count)
					{
						currentLapGapTimes[checkpointIndex] = _currentLapTime;
					}
				}

				// Mark checkpoint as crossed using the trigger system
				var checkpointTriggers = _trackDefinition?.CheckpointTriggers;
				if (checkpointTriggers != null && checkpointIndex < checkpointTriggers.Length && checkpointTriggers[checkpointIndex] != null)
				{
					checkpointTriggers[checkpointIndex].MarkAsCrossed();
				}

				var checkpointName = _checkpointTriggers[checkpointIndex]?.TriggerName ?? $"Checkpoint {checkpointIndex + 1}";
				GD.Print($"Checkpoint {checkpointIndex + 1} crossed: {checkpointName} - Gap: {_currentLapTime:F2}s");

				// Check if all checkpoints have been crossed
				if (_nextCheckpointIndex >= _checkpointTriggers.Length)
				{
					GD.Print("All checkpoints crossed - ready for finish line");
				}
			}
			else
			{
				GD.Print($"Checkpoint {checkpointIndex + 1} crossed out of order - expected checkpoint {_nextCheckpointIndex + 1}");
			}
		}
		else
		{
			// Practice mode: just reset gap timer
			_currentGapTime = 0.0f;
			var checkpointName = _checkpointTriggers[checkpointIndex]?.TriggerName ?? $"Checkpoint {checkpointIndex + 1}";
			GD.Print($"Checkpoint {checkpointIndex + 1} crossed: {checkpointName} (Practice Mode)");
		}
	}

	private void OnStartLineCrossed()
	{
		if (!_inTimeTrialMode) 
		{
			// In practice mode, just reset gap timer
			_currentGapTime = 0.0f;
			return;
		}

		if (_currentLap == 0)
		{
			// Starting first lap
			_currentLap = 1;
			_currentLapTime = 0.0f;
			
			// Initialize gap times storage for first lap
			_lapGapTimes.Add(new List<float>());
			for (int i = 0; i < _checkpointTriggers.Length; i++)
			{
				_lapGapTimes[_currentLap - 1].Add(0.0f);
			}
			
			ResetCheckpoints();
			GD.Print("Lap 1 started!");
		}
		else if (_currentLap <= _targetLaps)
		{
			// Check if all checkpoints were crossed before allowing lap completion
			if (_checkpointTriggers.Length > 0 && _nextCheckpointIndex < _checkpointTriggers.Length)
			{
				GD.Print($"Cannot complete lap - only {_nextCheckpointIndex}/{_checkpointTriggers.Length} checkpoints crossed");
				return;
			}

			// Completed a lap
			GD.Print($"Lap {_currentLap} completed in {_currentLapTime:F2}s");
			
			// Store completed lap time
			_completedLapTimes.Add(_currentLapTime);
			_lastCompletedLapTime = _currentLapTime;
			
			if (_currentLapTime < _bestLapTime)
			{
				_bestLapTime = _currentLapTime;
			}

			_currentLap++;
			
			if (_currentLap > _targetLaps)
			{
				// Race completed
				EndTimeTrial();
			}
			else
			{
				// Start next lap - initialize gap times for this lap
				_lapGapTimes.Add(new List<float>());
				for (int i = 0; i < _checkpointTriggers.Length; i++)
				{
					_lapGapTimes[_currentLap - 1].Add(0.0f);
				}
				
				_currentLapTime = 0.0f;
				ResetCheckpoints();
				GD.Print($"Lap {_currentLap} started!");
			}
		}
	}

	private void ResetCheckpoints()
	{
		if (_checkpointTriggers == null) return;

		// Reset checkpoint tracking
		for (int i = 0; i < _checkpointsCrossed.Length; i++)
		{
			_checkpointsCrossed[i] = false;
		}
		_nextCheckpointIndex = 0;

		// Reset all checkpoint triggers to uncrossed state
		_trackDefinition?.ResetAllCheckpoints();
	}

	private void DetectAndAdaptToContext()
	{
		_gameHost = GameHost.GetInstance();
		
		if (_gameHost != null)
		{
			_isProductionContext = true;
			var playerSession = _gameHost.GetPlayerSession("default");
			if (playerSession != null)
			{
				_playerId = playerSession.PlayerId;
			}
			else
			{
				_playerId = _gameHost.GetCurrentPlayerId();
			}
			GD.Print("RacingGame: Production context detected with GameHost autoload");
		}
		else
		{
			_isProductionContext = false;
			_playerId = "dev_player";
			GD.Print("RacingGame: Development context detected - will auto-start practice mode after track setup");
		}
	}

	public void StartPracticeMode()
	{
		if (_gameActive) return;

		_gameActive = true;
		_gamePaused = false;
		_inTimeTrialMode = false;
		_currentLap = 0;
		_currentLapTime = 0.0f;
		_currentGapTime = 0.0f;

		// Clear visual feedback state
		ClearTireTrails();
		_mouseStationaryTime = 0.0f;
		_arcRotation = 0.0f;

		_pauseOverlay.Visible = false;
		_gameOverOverlay.Visible = false;

		EmitSignal(SignalName.PlayerAdded, _playerId);
		EmitSignal(SignalName.GameStarted);

		GD.Print("Racing Game - Practice Mode Started!");
	}

	public void StartTimeTrial()
	{
		if (_isProductionContext)
		{
			// Check and deduct credits for time trial
			var userManager = UserManager.GetAutoload();
			if (userManager != null)
			{
				if (!userManager.SpendCredits(1))
				{
					GD.Print("Not enough credits for time trial!");
					return; // Not enough credits
				}
				GD.Print("1 credit deducted for time trial");
			}
		}

		_gameActive = true;
		_gamePaused = false;
		_inTimeTrialMode = true;
		_bestLapTime = float.MaxValue;

		// Reset timing data
		_completedLapTimes.Clear();
		_lapGapTimes.Clear();
		_currentGapTime = 0.0f;

		// Clear visual feedback state
		ClearTireTrails();
		_mouseStationaryTime = 0.0f;
		_arcRotation = 0.0f;

		_pauseOverlay.Visible = false;
		_gameOverOverlay.Visible = false;

		// Position car at start line
		PositionCarAtStart();

		// Start countdown instead of immediate start
		StartCountdown();

		EmitSignal(SignalName.GameStarted);
		GD.Print("Racing Game - Time Trial Countdown Started!");
	}

	private void StartCountdown()
	{
		_inCountdown = true;
		_countdownTime = 0.0f;
		_countdownNumber = 3;
		_countdownLabel.Text = "3";
		_countdownOverlay.Visible = true;
	}

	public void EndTimeTrial()
	{
		if (!_inTimeTrialMode) return;

		// Update high score in production context
		if (_isProductionContext && _bestLapTime < float.MaxValue)
		{
			var userManager = UserManager.GetAutoload();
			if (userManager != null)
			{
				// Convert lap time to score (lower time = higher score)
				// Use 1000000 - (time in milliseconds) so lower times get higher scores
				int score = (int)(1000000 - (_bestLapTime * 1000));
				userManager.UpdateHighScore(_playerId, "RacingGame", score);
				GD.Print($"High score updated: {score} (lap time: {_bestLapTime:F2}s)");
			}
		}

		// Optional GameHost notification with best lap time
		EmitSignal(SignalName.ScoreChanged, _playerId, _bestLapTime);
		EmitSignal(SignalName.GameEnded);

		GD.Print($"Time Trial Completed! Best Lap: {_bestLapTime:F2}s");

		// Return to practice mode instead of showing game over
		_inTimeTrialMode = false;
		_currentLap = 0;
		_currentLapTime = 0.0f;
		_currentGapTime = 0.0f;
		
		// Keep _lastCompletedLapTime for display in practice mode
		GD.Print($"Returning to practice mode. Last completed lap: {_lastCompletedLapTime:F2}s");
	}

	public void RestartPractice()
	{
		_gameActive = false;
		_inTimeTrialMode = false;
		_gamePaused = false;
		
		// Clear visual feedback state
		ClearTireTrails();
		_mouseStationaryTime = 0.0f;
		_arcRotation = 0.0f;
		
		StartPracticeMode();
	}


	private void DisconnectTrackSignals()
	{
		if (_trackDefinition == null) return;

		// Disconnect start line trigger signal
		var startLineTrigger = _trackDefinition.StartLine;
		if (startLineTrigger != null)
		{
			startLineTrigger.StartLineCrossed -= OnStartLineTriggered;
		}

		// Disconnect checkpoint trigger signals
		var checkpointTriggers = _trackDefinition.CheckpointTriggers;
		for (int i = 0; i < checkpointTriggers.Length; i++)
		{
			var trigger = checkpointTriggers[i];
			if (trigger != null)
			{
				trigger.CheckpointCrossed -= OnCheckpointTriggered;
			}
		}
	}

	public void TogglePause()
	{
		if (!_gameActive) return;

		if (_gamePaused)
		{
			_gamePaused = false;
			_pauseOverlay.Visible = false;
			_pauseButton.Text = "Pause";
		}
		else
		{
			_gamePaused = true;
			_pauseOverlay.Visible = true;
			_pauseButton.Text = "Resume";
		}
	}

	private void UpdateUI()
	{
		if (_speedLabel != null)
			_speedLabel.Text = $"Speed: {_currentSpeed:F0}";

		if (_lapLabel != null)
		{
			if (_inTimeTrialMode)
				_lapLabel.Text = $"Lap: {_currentLap}/{_targetLaps}";
			else
				_lapLabel.Text = "Practice Mode";
		}

		if (_timeLabel != null)
		{
			if (_inTimeTrialMode && _currentLap > 0)
				_timeLabel.Text = $"Time: {_currentLapTime:F1}s";
			else
				_timeLabel.Text = "Time: --";
		}

		UpdateTimingUI();

		if (_proximityLabel != null && _car != null)
		{
			var carPosition = _car.GlobalPosition;
			var distanceToCenter = GetDistanceToTrackCenterLine(carPosition);
			var isOnTrack = IsOnTrack(carPosition);
			
			if (!isOnTrack)
			{
				_proximityLabel.Text = "Track: OFF-TRACK";
				_proximityLabel.Modulate = Colors.Red;
			}
			else if (distanceToCenter <= CenterLineProximityRange)
			{
				_proximityLabel.Text = "Track: CENTER LINE";
				_proximityLabel.Modulate = Colors.Green;
			}
			else
			{
				_proximityLabel.Text = "Track: Normal";
				_proximityLabel.Modulate = Colors.White;
			}
		}
	}

	private void UpdateTimingUI()
	{
		if (_inTimeTrialMode)
		{
			// Time Attack mode: show lap times and gap times
			_gapTimeLabel.Visible = false;
			_lastLapLabel.Visible = false;
			_timingScrollContainer.Visible = true;

			UpdateLapTimesDisplay();
		}
		else
		{
			// Practice mode: show gap time and last lap time
			_timingScrollContainer.Visible = false;
			_gapTimeLabel.Visible = true;
			
			if (_gapTimeLabel != null)
			{
				_gapTimeLabel.Text = $"Gap: {_currentGapTime:F1}s";
			}

			// Show last lap time if available
			if (_lastCompletedLapTime > 0)
			{
				_lastLapLabel.Visible = true;
				_lastLapLabel.Text = $"Last Lap: {_lastCompletedLapTime:F2}s";
			}
			else
			{
				_lastLapLabel.Visible = false;
			}
		}
	}

	private void UpdateLapTimesDisplay()
	{
		if (_lapTimesContainer == null) return;

		// Clear existing lap time displays
		foreach (Node child in _lapTimesContainer.GetChildren())
		{
			child.QueueFree();
		}

		// Add header
		var header = new Label();
		header.Text = "LAP TIMES";
		header.HorizontalAlignment = HorizontalAlignment.Center;
		_lapTimesContainer.AddChild(header);

		// Add completed lap times
		for (int i = 0; i < _completedLapTimes.Count; i++)
		{
			var lapLabel = new Label();
			lapLabel.Text = $"Lap {i + 1}: {_completedLapTimes[i]:F2}s";
			_lapTimesContainer.AddChild(lapLabel);

			// Add gap times for this lap if available
			if (i < _lapGapTimes.Count && _lapGapTimes[i].Count > 0)
			{
				for (int j = 0; j < _lapGapTimes[i].Count; j++)
				{
					if (_lapGapTimes[i][j] > 0)
					{
						var gapLabel = new Label();
						gapLabel.Text = $"  CP{j + 1}: {_lapGapTimes[i][j]:F2}s";
						gapLabel.Modulate = new Color(0.8f, 0.8f, 0.8f);
						_lapTimesContainer.AddChild(gapLabel);
					}
				}
			}
		}

		// Show current lap progress if in progress
		if (_currentLap > 0 && _currentLap <= _targetLaps)
		{
			var currentLapLabel = new Label();
			currentLapLabel.Text = $"Lap {_currentLap}: {_currentLapTime:F1}s";
			currentLapLabel.Modulate = Colors.Yellow;
			_lapTimesContainer.AddChild(currentLapLabel);

			// Show current lap gap times
			if (_currentLap <= _lapGapTimes.Count)
			{
				var currentGapTimes = _lapGapTimes[_currentLap - 1];
				for (int i = 0; i < currentGapTimes.Count && i < _nextCheckpointIndex; i++)
				{
					if (currentGapTimes[i] > 0)
					{
						var gapLabel = new Label();
						gapLabel.Text = $"  CP{i + 1}: {currentGapTimes[i]:F2}s";
						gapLabel.Modulate = Colors.LightYellow;
						_lapTimesContainer.AddChild(gapLabel);
					}
				}
			}
		}
	}

}