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

	// Game state
	private bool _gameActive = false;
	private bool _gamePaused = false;
	private bool _inTimeTrialMode = false;
	private string _playerId = "player1";
	private float _currentLapTime = 0.0f;
	private float _bestLapTime = float.MaxValue;
	private int _currentLap = 0;
	private int _targetLaps = 3;

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

	// Track system
	private TrackGenerator _trackGenerator;
	private Curve2D _trackCurve;
	private Path2D _trackPath;
	private Line2D _trackVisual;
	private Line2D _startLine;
	private Area2D _startLineArea;
	private Vector2[] _trackPoints;
	private bool _crossedStartLine = false;
	private float _lastStartLineCross = 0.0f;

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

		// Update lap timer
		if (_inTimeTrialMode && _currentLap > 0)
		{
			_currentLapTime += deltaF;
		}

		UpdateCarPhysics(deltaF);
		CheckStartLineCollision();
		UpdateUI();
	}

	public override void _Input(InputEvent inputEvent)
	{
		if (!_gameActive || _gamePaused) return;

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

		SetupPauseOverlay();
		SetupGameOverOverlay();
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
		
		// Car visual
		var carSprite = new ColorRect();
		carSprite.Size = new Vector2(20, 40);
		carSprite.Color = Colors.Red;
		carSprite.Position = new Vector2(-10, -20);
		_car.AddChild(carSprite);

		// Car collision
		var carCollision = new CollisionShape2D();
		var carShape = new RectangleShape2D();
		carShape.Size = new Vector2(20, 40);
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

	private void InitializeTrackGenerator()
	{
		// Get or create TrackGenerator child node
		_trackGenerator = GetNode<TrackGenerator>("TrackGenerator");
		if (_trackGenerator == null)
		{
			GD.PrintErr("RacingGame: TrackGenerator node not found");
			return;
		}

		// Generate track with daily seed
		var today = Time.GetDateDictFromSystem();
		var seed = $"{today["year"]}-{today["month"]}-{today["day"]}";
		
		_trackPoints = _trackGenerator.GenerateTrack(seed);
		_trackCurve = _trackGenerator.GetTrackCurve();
		
		CreateStartLine();
	}


	private void CreateStartLine()
	{
		if (_trackPoints == null || _trackPoints.Length == 0) return;

		// Create start line at the first track point
		var startPoint = _trackPoints[0];
		var nextPoint = _trackPoints[1];
		var direction = (nextPoint - startPoint).Normalized();
		var perpendicular = new Vector2(-direction.Y, direction.X);

		_startLine = new Line2D();
		_startLine.Width = 4.0f;
		_startLine.DefaultColor = Colors.Green;
		_startLine.AddPoint(startPoint + perpendicular * 30);
		_startLine.AddPoint(startPoint - perpendicular * 30);
		AddChild(_startLine);

		// Create area for collision detection
		_startLineArea = new Area2D();
		var startLineCollision = new CollisionShape2D();
		var startLineShape = new RectangleShape2D();
		startLineShape.Size = new Vector2(60, 10);
		startLineCollision.Shape = startLineShape;
		_startLineArea.AddChild(startLineCollision);
		_startLineArea.GlobalPosition = startPoint;
		_startLineArea.Rotation = direction.Angle();
		_startLineArea.BodyEntered += OnStartLineEntered;
		AddChild(_startLineArea);

		// Position car at start line
		_car.GlobalPosition = startPoint - direction * 50;
		_car.Rotation = direction.Angle() + Mathf.Pi / 2;
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
			// Apply turn penalty for off-track
			var effectiveInputDirection = inputDirection * turnModifier;
			_velocity = effectiveInputDirection * _currentSpeed;
			
			// Rotate car to face movement direction
			_car.Rotation = _velocity.Angle() + Mathf.Pi / 2;
		}
		else
		{
			_velocity = Vector2.Zero;
		}

		// Apply movement
		_car.Velocity = _velocity;
		_car.MoveAndSlide();
	}

	private void CheckStartLineCollision()
	{
		if (_startLineArea == null || _car == null) return;

		var carGlobalPos = _car.GlobalPosition;
		var startLinePos = _startLineArea.GlobalPosition;
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
		if (_trackGenerator == null) return 1.0f;
		
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
		if (_trackGenerator == null) return true;
		return _trackGenerator.IsValidTrackPoint(position);
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

	private void OnStartLineEntered(Node2D body)
	{
		// This method exists for potential future use with Area2D signals
		// Currently using distance-based detection in CheckStartLineCollision
	}

	private void OnStartLineCrossed()
	{
		if (!_inTimeTrialMode) return;

		if (_currentLap == 0)
		{
			// Starting first lap
			_currentLap = 1;
			_currentLapTime = 0.0f;
			GD.Print("Lap 1 started!");
		}
		else if (_currentLap <= _targetLaps)
		{
			// Completed a lap
			GD.Print($"Lap {_currentLap} completed in {_currentLapTime:F2}s");
			
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
				// Start next lap
				_currentLapTime = 0.0f;
				GD.Print($"Lap {_currentLap} started!");
			}
		}
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
			CallDeferred(MethodName.StartPracticeMode);
			GD.Print("RacingGame: Development context detected - auto-starting practice mode");
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
		_currentLap = 0;
		_currentLapTime = 0.0f;
		_bestLapTime = float.MaxValue;

		_pauseOverlay.Visible = false;
		_gameOverOverlay.Visible = false;

		// Position car at start line
		if (_trackPoints != null && _trackPoints.Length > 1)
		{
			var startPoint = _trackPoints[0];
			var nextPoint = _trackPoints[1];
			var direction = (nextPoint - startPoint).Normalized();
			_car.GlobalPosition = startPoint - direction * 50;
			_car.Rotation = direction.Angle() + Mathf.Pi / 2;
		}

		EmitSignal(SignalName.GameStarted);
		GD.Print("Racing Game - Time Trial Started!");
	}

	public void EndTimeTrial()
	{
		if (!_inTimeTrialMode) return;

		_gameActive = false;
		_inTimeTrialMode = false;

		_finalTimeLabel.Text = $"Best Lap: {_bestLapTime:F2}s";
		_gameOverOverlay.Visible = true;

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
	}

	public void RestartPractice()
	{
		_gameActive = false;
		_inTimeTrialMode = false;
		_gamePaused = false;
		StartPracticeMode();
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
}