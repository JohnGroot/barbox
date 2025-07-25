using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 2D time trial racing game with comprehensive racing mechanics, track system, and UI
/// </summary>
[GlobalClass]
public partial class RacingGame : GameController
{
	// ================================================================
	// SIGNALS
	// ================================================================
	
	[Signal] public delegate void LapCompletedEventHandler(string playerId, int lapNumber, float lapTime);
	[Signal] public delegate void RaceCompletedEventHandler(string playerId, float totalTime);
	[Signal] public delegate void CheckpointCrossedEventHandler(string playerId, int checkpointIndex, float gapTime);

	// ================================================================
	// EXPORT PROPERTIES - RACING SETTINGS
	// ================================================================
	
	[ExportCategory("Racing Settings")]
	[Export] public int TargetLaps { get; set; } = 3;
	[Export] public bool ShowCountdown { get; set; } = true;
	[Export] public float CountdownDuration { get; set; } = 4.0f; // 3-2-1-GO = 4 seconds
	[Export] public int TimeTrialCreditCost { get; set; } = 1; // Credit cost for time trial mode

	[ExportCategory("Track Settings")]
	[Export] public Godot.Collections.Array<PackedScene> TrackScenes { get; set; } = new Godot.Collections.Array<PackedScene>();

	[ExportCategory("Car Settings")]
	[Export] public float MaxSpeed { get; set; } = 1800.0f;
	[Export] public float MinSpeed { get; set; } = 100.0f;
	[Export] public float MaxInputDistance { get; set; } = 500.0f;
	[Export] public float AccelerationRate { get; set; } = 300.0f;
	[Export] public float DecelerationRate { get; set; } = 600.0f;
	[Export] public float RotationLerpSpeed { get; set; } = 5.0f;
	[Export] public Vector2 CarSize { get; set; } = new Vector2(40, 80);

	[ExportCategory("Track Performance")]
	[Export] public float CenterLineProximityRange { get; set; } = 40.0f;
	[Export] public float CenterLineAccelerationBonus { get; set; } = 1.5f;
	[Export] public float TrackProximityRange { get; set; } = 20.0f;

	[ExportCategory("Off-Track Penalties")]
	[Export] public float OffTrackSpeedPenalty { get; set; } = 0.3f;
	[Export] public float OffTrackTurnPenalty { get; set; } = 0.3f;
	[Export] public float OffTrackAccelerationPenalty { get; set; } = 0.3f;
	[Export] public float OffTrackPenaltyLerpSpeed { get; set; } = 3.0f;

	[ExportCategory("Screen Boundaries")]
	[Export] public float ScreenEdgeColliderThickness { get; set; } = 8.0f;

	[ExportCategory("Visual Feedback")]
	[Export] public Color InputLineActiveColor { get; set; } = Colors.White;
	[Export] public Color InputLineInactiveColor { get; set; } = Colors.Gray;
	[Export] public Color MouseMovingColor { get; set; } = Colors.Cyan;
	[Export] public Color MouseStationaryColor { get; set; } = Colors.LimeGreen;
	[Export] public Color TireTrailColor { get; set; } = Colors.DarkGray;
	[Export] public float InputLineWidth { get; set; } = 3.0f;
	[Export] public float MouseIndicatorRadius { get; set; } = 25.0f;
	[Export] public float TireTrailWidth { get; set; } = 2.0f;
	[Export] public int MaxTrailPoints { get; set; } = 1000;
	[Export] public float TrailUpdateDistance { get; set; } = 3.0f;
	[Export] public float TrailLifetime { get; set; } = 3000.0f;

	// ================================================================
	// CONSTANTS
	// ================================================================
	
	// Total height reserved for TopMenuBar (100px) + ContextButtonBar (50px)
	private const float TOP_MENU_HEIGHT = 150.0f;

	// ================================================================
	// RACING STATE AND ENUMS
	// ================================================================
	
	public enum RacingState { Stopped, Countdown, Racing, Finished }

	// ================================================================
	// PRIVATE FIELDS - RACING LOGIC
	// ================================================================
	
	// Racing state management
	private RacingState _racingState = RacingState.Stopped;
	private bool _inCountdown = false;
	private float _countdownTime = 0.0f;
	private int _countdownNumber = 0;
	
	// Lap and timing tracking per player
	private Dictionary<string, int> _playerCurrentLap = new();
	private Dictionary<string, List<float>> _playerLapTimes = new();
	private Dictionary<string, float> _playerCurrentLapTime = new();
	private Dictionary<string, float> _playerBestLapTime = new();
	private Dictionary<string, float> _playerGapTime = new(); // Time since last checkpoint in practice mode

	// ================================================================
	// PRIVATE FIELDS - TRACK AND WORLD SYSTEMS
	// ================================================================
	
	// Track system
	private TrackDefinition _trackDefinition;
	private Curve2D _trackCurve;
	private CheckpointTrigger[] _checkpointTriggers;
	private bool[] _checkpointsCrossed;
	private int _nextCheckpointIndex = 0;
	private int _currentTrackIndex = 0;

	// Camera system
	private Camera2D _trackCamera;
	private List<StaticBody2D> _screenEdgeColliders = new List<StaticBody2D>();

	// Racing player
	private RacingCarPlayer _racingCar;

	// ================================================================
	// PRIVATE FIELDS - UI SYSTEM
	// ================================================================
	
	// Built-in UI elements
	private CanvasLayer _uiLayer;
	private Label _modeLabel;
	private Label _proximityLabel;
	
	// Status display labels
	private Label _speedLabel;
	private Label _lapLabel;
	private Label _timeLabel;
	private Button _pauseButton;
	private Button _timeTrialButton;
	private Control _pauseOverlay;
	private Control _gameOverOverlay;
	private Label _finalTimeLabel;
	private Button[] _trackSelectionButtons;
	private Label _gapTimeLabel;
	private Label _lastLapLabel;
	private VBoxContainer _lapTimesContainer;
	private ScrollContainer _timingScrollContainer;
	private Control _countdownOverlay;
	private Label _countdownLabel;

	// ================================================================
	// PRIVATE FIELDS - VISUAL FEEDBACK
	// ================================================================
	
	// Visual feedback tracking
	private List<(Vector2 position, ulong timestamp)> _leftTireTrail = new List<(Vector2, ulong)>();
	private List<(Vector2 position, ulong timestamp)> _rightTireTrail = new List<(Vector2, ulong)>();
	private Vector2 _lastTrailPosition = Vector2.Zero;
	private bool _wasInputActive = false;
	private Vector2 _lastMousePosition = Vector2.Zero;
	private float _mouseStationaryTime = 0.0f;
	private float _arcRotation = 0.0f;

	// Off-track penalty state
	private bool _isCurrentlyOffTrack = false;
	private float _currentSpeedPenaltyMultiplier = 1.0f;
	private float _currentTurnPenaltyMultiplier = 1.0f;
	private float _currentAccelerationPenaltyMultiplier = 1.0f;

	// ================================================================
	// INITIALIZATION
	// ================================================================

	public override void _Ready()
	{
		base._Ready();
		GD.Print("[RacingGame] _Ready() starting");
		GameId = "racing_game";
		SetGameMode(GameMode.Practice); // Start in practice mode
		
		SetupUI();
		SetupCamera();
		SetupRacingCar();
		InitializeTrackSystem();
		DetectAndAdaptToContext();
		
		GD.Print("[RacingGame] _Ready() completed");
	}

	protected override void InitializeGame()
	{
		base.InitializeGame();
		StartPractice(); // Default to practice mode
	}

	// ================================================================
	// CORE GAME LOOP
	// ================================================================

	public override void _Process(double delta)
	{
		base._Process(delta);
		
		if (_inCountdown)
		{
			HandleCountdown((float)delta);
		}
		else if (_isGameActive && !_isGamePaused)
		{
			UpdateRacingTimers((float)delta);
		}
		
		UpdateUI();
	}

	protected override void UpdateGame(float delta)
	{
		base.UpdateGame(delta);
		
		if (_racingCar != null)
		{
			UpdateOffTrackPenalties(delta);
			UpdateVisualFeedback(delta);
			UpdateTireTrails();
		}
	}

	public override void _Draw()
	{
		if (!IsGameActive() || IsGamePaused() || _racingCar == null) return;

		DrawInputLine();
		DrawMouseIndicators();
		DrawTireTrails();
	}

	// ================================================================
	// RACING MECHANICS - GAME MODES
	// ================================================================

	/// <summary>
	/// Start a time trial race with countdown
	/// </summary>
	public virtual async void StartTimeTrial()
	{
		if (_isGameActive) return;
		
		// Check if credits are required and handle credit spending
		if (TimeTrialCreditCost > 0)
		{
			var creditManager = CreditManager.GetInstance();
			if (creditManager != null && GodotObject.IsInstanceValid(creditManager))
			{
				// Reset idle timer when starting a premium feature
				var userManager = UserManager.GetAutoload();
				if (userManager != null && GodotObject.IsInstanceValid(userManager))
				{
					userManager.ResetUserIdleTimer();
				}
				
				bool creditsSpent = await creditManager.CheckAndSpendCredits(TimeTrialCreditCost, "Time Trial Race");
				if (!creditsSpent)
				{
					// Credits not spent - don't start the race
					GD.Print("Time trial cancelled - credits not spent");
					return;
				}
			}
		}
		
		SetGameMode(GameMode.TimeTrial);
		ResetRacingData();
		
		if (ShowCountdown)
		{
			StartCountdown();
		}
		else
		{
			StartGame();
		}
	}

	/// <summary>
	/// Start practice mode (no countdown, continuous play)
	/// </summary>
	public virtual void StartPractice()
	{
		SetGameMode(GameMode.Practice);
		ResetRacingData();
		StartGame();
		
		// In practice mode, immediately start first "lap" for timing
		foreach (var player in _players)
		{
			StartPlayerLap(player.PlayerId);
		}
	}

	// ================================================================
	// RACING MECHANICS - COUNTDOWN SYSTEM
	// ================================================================

	/// <summary>
	/// Start countdown sequence for time trials
	/// </summary>
	protected virtual void StartCountdown()
	{
		_inCountdown = true;
		_racingState = RacingState.Countdown;
		_countdownTime = 0.0f;
		_countdownNumber = 0;
		OnCountdownStarted();
	}

	/// <summary>
	/// Handle countdown timer and progression
	/// </summary>
	protected virtual void HandleCountdown(float delta)
	{
		_countdownTime += delta;
		int newCountdownNumber = 4 - (int)(_countdownTime / 1.0f);
		
		if (newCountdownNumber != _countdownNumber)
		{
			_countdownNumber = newCountdownNumber;
			
			if (_countdownNumber > 0)
			{
				OnCountdownNumber(_countdownNumber);
			}
			else
			{
				OnCountdownGo();
			}
		}

		// End countdown and start race
		if (_countdownTime >= CountdownDuration)
		{
			EndCountdown();
		}
	}

	/// <summary>
	/// End countdown and start the actual race
	/// </summary>
	protected virtual void EndCountdown()
	{
		_inCountdown = false;
		_racingState = RacingState.Racing;
		StartGame();
		OnCountdownEnded();
		
		// Start first lap for all players in time trial mode
		foreach (var player in _players)
		{
			StartPlayerLap(player.PlayerId);
		}
	}

	// Countdown event handlers - override in RacingGame for custom display
	protected virtual void OnCountdownStarted()
	{
		if (_countdownOverlay != null) _countdownOverlay.Visible = true;
	}

	protected virtual void OnCountdownNumber(int number)
	{
		if (_countdownLabel != null) _countdownLabel.Text = number.ToString();
	}

	protected virtual void OnCountdownGo()
	{
		if (_countdownLabel != null) _countdownLabel.Text = "GO!";
	}

	protected virtual void OnCountdownEnded()
	{
		if (_countdownOverlay != null) _countdownOverlay.Visible = false;
	}

	// ================================================================
	// RACING MECHANICS - LAP MANAGEMENT
	// ================================================================

	/// <summary>
	/// Start a new lap for a player
	/// </summary>
	public virtual void StartPlayerLap(string playerId)
	{
		if (!_playerCurrentLap.ContainsKey(playerId))
			_playerCurrentLap[playerId] = 0;
		
		_playerCurrentLap[playerId]++;
		_playerCurrentLapTime[playerId] = 0.0f;
		_playerGapTime[playerId] = 0.0f;
		
		if (!_playerLapTimes.ContainsKey(playerId))
			_playerLapTimes[playerId] = new List<float>();
	}

	/// <summary>
	/// Complete a lap for a player
	/// </summary>
	public virtual void CompletePlayerLap(string playerId, float lapTime)
	{
		if (!_playerCurrentLap.ContainsKey(playerId)) return;
		
		int lapNumber = _playerCurrentLap[playerId];
		_playerLapTimes[playerId].Add(lapTime);
		
		// Update best lap time
		if (!_playerBestLapTime.ContainsKey(playerId) || lapTime < _playerBestLapTime[playerId])
		{
			_playerBestLapTime[playerId] = lapTime;
		}
		
		// Update player score (best lap time or total time)
		if (_currentGameMode == GameMode.Practice)
		{
			UpdateScore(playerId, _playerBestLapTime[playerId]);
		}
		else
		{
			// In time trial, score is total time
			float totalTime = _playerLapTimes[playerId].Sum();
			UpdateScore(playerId, totalTime);
		}
		
		EmitSignal(SignalName.LapCompleted, playerId, lapNumber, lapTime);
		
		// Check if race is complete
		if (_currentGameMode == GameMode.TimeTrial && lapNumber >= TargetLaps)
		{
			CompletePlayerRace(playerId);
		}
		else if (_currentGameMode == GameMode.Practice || lapNumber < TargetLaps)
		{
			// Start next lap
			StartPlayerLap(playerId);
		}
	}

	/// <summary>
	/// Complete the race for a player
	/// </summary>
	protected virtual void CompletePlayerRace(string playerId)
	{
		if (!_playerLapTimes.ContainsKey(playerId)) return;
		
		float totalTime = _playerLapTimes[playerId].Sum();
		var lapTimes = new List<float>(_playerLapTimes[playerId]);
		
		EmitSignal(SignalName.RaceCompleted, playerId, totalTime);
		
		// If all players finished, end the game
		bool allPlayersFinished = true;
		foreach (var player in _players)
		{
			if (_playerCurrentLap.GetValueOrDefault(player.PlayerId, 0) < TargetLaps)
			{
				allPlayersFinished = false;
				break;
			}
		}
		
		if (allPlayersFinished)
		{
			_racingState = RacingState.Finished;
			EndGame();
		}
	}

	/// <summary>
	/// Player crossed a checkpoint
	/// </summary>
	public virtual void OnPlayerCheckpointCrossed(string playerId, int checkpointIndex)
	{
		float gapTime = _playerGapTime.GetValueOrDefault(playerId, 0.0f);
		_playerGapTime[playerId] = 0.0f; // Reset gap timer
		
		EmitSignal(SignalName.CheckpointCrossed, playerId, checkpointIndex, gapTime);
	}

	/// <summary>
	/// Reset all racing-specific data
	/// </summary>
	protected virtual void ResetRacingData()
	{
		_playerCurrentLap.Clear();
		_playerLapTimes.Clear();
		_playerCurrentLapTime.Clear();
		_playerBestLapTime.Clear();
		_playerGapTime.Clear();
		_racingState = RacingState.Stopped;
	}

	/// <summary>
	/// Update racing timers for all players
	/// </summary>
	protected virtual void UpdateRacingTimers(float delta)
	{
		foreach (var player in _players)
		{
			string playerId = player.PlayerId;
			
			if (_playerCurrentLapTime.ContainsKey(playerId))
			{
				_playerCurrentLapTime[playerId] += delta;
			}
			
			if (_playerGapTime.ContainsKey(playerId))
			{
				_playerGapTime[playerId] += delta;
			}
		}
	}

	// ================================================================
	// CAR MANAGEMENT
	// ================================================================

	/// <summary>
	/// Setup the racing car player
	/// </summary>
	private void SetupRacingCar()
	{
		_racingCar = new RacingCarPlayer();
		_racingCar.PlayerId = "player1";
		_racingCar.PlayerName = "Player";
		
		// Set car properties from exports (use scene values)
		_racingCar.MaxSpeed = MaxSpeed;
		_racingCar.MinSpeed = MinSpeed;
		_racingCar.MaxInputDistance = MaxInputDistance;
		_racingCar.AccelerationRate = AccelerationRate;
		_racingCar.DecelerationRate = DecelerationRate;
		_racingCar.RotationLerpSpeed = RotationLerpSpeed;
		_racingCar.CarSize = CarSize;
		
		// Connect car events
		_racingCar.CarMoved += OnCarMoved;
		
		AddPlayer(_racingCar);
		AddChild(_racingCar);
	}

	/// <summary>
	/// Racing-specific car that extends RacingPlayer with track penalties
	/// </summary>
	public partial class RacingCarPlayer : RacingPlayer
	{
		public RacingGame ParentGame { get; set; }

		protected override Vector2 TransformScreenToWorldPosition(Vector2 screenPosition)
		{
			if (ParentGame?._trackCamera == null) return screenPosition;

			var viewport = GetViewport();
			if (viewport == null) return screenPosition;

			Vector2 viewportSize = viewport.GetVisibleRect().Size;
			
			// Account for top menu bar - adjust screen position relative to playable area
			Vector2 playableAreaSize = new Vector2(viewportSize.X, viewportSize.Y - TOP_MENU_HEIGHT);
			Vector2 adjustedScreenPosition = new Vector2(
				screenPosition.X,
				Mathf.Max(0, screenPosition.Y - TOP_MENU_HEIGHT) // Clamp to playable area
			);
			
			Vector2 normalizedPosition = adjustedScreenPosition / playableAreaSize;
			Vector2 cameraRelative = normalizedPosition - new Vector2(0.5f, 0.5f);
			Vector2 cameraZoom = ParentGame._trackCamera.Zoom;
			Vector2 worldSize = playableAreaSize / cameraZoom;
			Vector2 worldOffset = cameraRelative * worldSize;
			Vector2 worldPosition = ParentGame._trackCamera.GlobalPosition + worldOffset;

			return worldPosition;
		}

		protected override float GetSpeedModifier()
		{
			return ParentGame?._currentSpeedPenaltyMultiplier ?? 1.0f;
		}

		protected override float GetAccelerationModifier()
		{
			float baseModifier = ParentGame?._currentAccelerationPenaltyMultiplier ?? 1.0f;
			
			// Apply center line bonus if on track
			if (ParentGame != null && ParentGame.IsOnTrack(GetCarBody()?.GlobalPosition ?? Vector2.Zero))
			{
				var distanceToCenter = ParentGame.GetDistanceToTrackCenterLine(GetCarBody()?.GlobalPosition ?? Vector2.Zero);
				if (distanceToCenter <= ParentGame.CenterLineProximityRange)
				{
					baseModifier = Mathf.Max(baseModifier, ParentGame.CenterLineAccelerationBonus);
				}
			}
			
			return baseModifier;
		}

		protected override float GetTurnModifier()
		{
			return ParentGame?._currentTurnPenaltyMultiplier ?? 1.0f;
		}
	}

	/// <summary>
	/// Handle car movement events for visual feedback
	/// </summary>
	private void OnCarMoved(Vector2 position, Vector2 velocity)
	{
		QueueRedraw(); // Trigger visual updates
	}

	// ================================================================
	// TRACK SYSTEM
	// ================================================================

	/// <summary>
	/// Initialize track system
	/// </summary>
	private void InitializeTrackSystem()
	{
		if (TrackScenes != null && TrackScenes.Count > 0)
		{
			_currentTrackIndex = 0;
			LoadTrack(0);
		}
		else
		{
			// Look for existing TrackDefinition child
			foreach (Node child in GetChildren())
			{
				if (child is TrackDefinition trackDef)
				{
					_trackDefinition = trackDef;
					_currentTrackIndex = 0;
					SetupLoadedTrack();
					return;
				}
			}
			GD.PrintErr("RacingGame: No track scenes configured and no TrackDefinition child node found.");
		}
	}

	/// <summary>
	/// Load a specific track by index
	/// </summary>
	private void LoadTrack(int trackIndex)
	{
		if (TrackScenes == null || trackIndex < 0 || trackIndex >= TrackScenes.Count)
		{
			GD.PrintErr($"Invalid track index: {trackIndex}");
			return;
		}

		// Clear existing track
		if (_trackDefinition != null)
		{
			DisconnectTrackSignals();
			_trackDefinition.QueueFree();
		}

		// Load new track
		var trackScene = TrackScenes[trackIndex];
		var trackInstance = trackScene.Instantiate();
		
		if (trackInstance is TrackDefinition trackDef)
		{
			_trackDefinition = trackDef;
			AddChild(_trackDefinition);
			
			// Center track in playable area (below top menu bar)
			var viewport = GetViewport();
			if (viewport != null)
			{
				var screenSize = viewport.GetVisibleRect().Size;
				Vector2 playableCenter = new Vector2(screenSize.X / 2, TOP_MENU_HEIGHT + (screenSize.Y - TOP_MENU_HEIGHT) / 2);
				_trackDefinition.GlobalPosition = playableCenter;
				GD.Print($"[RacingGame] Track positioned at playable center: {playableCenter}");
			}
			
			SetupLoadedTrack();
		}
		else
		{
			GD.PrintErr("Track scene does not contain TrackDefinition");
			trackInstance.QueueFree();
		}
	}

	/// <summary>
	/// Setup loaded track
	/// </summary>
	private void SetupLoadedTrack()
	{
		if (_trackDefinition == null) return;

		_trackDefinition.SetupTrack();
		_trackCurve = _trackDefinition.GetTrackCurve();
		
		PositionCameraOverTrack();
		SetupScreenEdgeColliders();
		TargetLaps = _trackDefinition.NumberOfLaps;
		InitializeCheckpointTracking();
		ConnectTrackSignals();
		PositionCarAtStart();
		
		// Associate racing car with this game for penalties
		if (_racingCar is RacingCarPlayer racingCar)
		{
			racingCar.ParentGame = this;
		}
	}

	/// <summary>
	/// Position car at track start
	/// </summary>
	private void PositionCarAtStart()
	{
		if (_trackDefinition == null || _racingCar == null) return;

		var startPoint = _trackDefinition.GetStartLinePosition();
		var startLineDirection = _trackDefinition.GetStartLineDirection();
		
		if (startPoint == Vector2.Zero)
		{
			var viewport = GetViewport();
			if (viewport != null)
			{
				var screenSize = viewport.GetVisibleRect().Size;
				Vector2 playableCenter = new Vector2(screenSize.X / 2, TOP_MENU_HEIGHT + (screenSize.Y - TOP_MENU_HEIGHT) / 2);
				_racingCar.GlobalPosition = playableCenter;
			}
			return;
		}
		
		var carBody = _racingCar.GetCarBody();
		if (carBody != null)
		{
			var carPosition = startPoint - startLineDirection * 50;
			carBody.GlobalPosition = carPosition;
			carBody.Rotation = startLineDirection.Angle() + Mathf.Pi / 2;
		}
	}

	// Track utility methods
	public bool IsOnTrack(Vector2 position)
	{
		return _trackDefinition?.IsValidTrackPoint(position) ?? true;
	}

	public float GetDistanceToTrackCenterLine(Vector2 position)
	{
		if (_trackCurve == null) return float.MaxValue;
		var closestPoint = _trackCurve.GetClosestPoint(position);
		return position.DistanceTo(closestPoint);
	}

	// ================================================================
	// CAMERA SYSTEM
	// ================================================================

	/// <summary>
	/// Setup camera system
	/// </summary>
	private void SetupCamera()
	{
		_trackCamera = new Camera2D();
		_trackCamera.Enabled = true;
		AddChild(_trackCamera);

		var viewport = GetViewport();
		if (viewport != null)
		{
			var screenSize = viewport.GetVisibleRect().Size;
			
			// Account for top menu bar - position camera in center of playable area
			Vector2 playableCenter = new Vector2(screenSize.X / 2, TOP_MENU_HEIGHT + (screenSize.Y - TOP_MENU_HEIGHT) / 2);
			_trackCamera.GlobalPosition = playableCenter;
		}
	}

	/// <summary>
	/// Position camera over track and zoom to fit
	/// </summary>
	private void PositionCameraOverTrack()
	{
		if (_trackDefinition == null || _trackCamera == null) return;

		Vector2 trackCenter = _trackDefinition.GlobalPosition;
		Rect2 trackBounds = _trackDefinition.GetTrackBounds();
		
		GD.Print($"[RacingGame] Track center: {trackCenter}, Track bounds: {trackBounds}");
		
		if (trackBounds.Size == Vector2.Zero) 
		{
			GD.PrintErr("[RacingGame] Track bounds size is zero - cannot calculate zoom");
			return;
		}
		
		var viewport = GetViewport();
		if (viewport != null)
		{
			Vector2 viewportSize = viewport.GetVisibleRect().Size;
			
			// Account for top menu bar in playable area calculation
			Vector2 playableArea = new Vector2(viewportSize.X, viewportSize.Y - TOP_MENU_HEIGHT);
			
			float padding = 0.15f; // Increased from 5% to 15% for better visual breathing room
			Vector2 paddedTrackSize = trackBounds.Size * (1.0f + padding * 2.0f);
			float scaleX = playableArea.X / paddedTrackSize.X;
			float scaleY = playableArea.Y / paddedTrackSize.Y;
			float zoom = Mathf.Clamp(Mathf.Min(scaleX, scaleY), 0.2f, 1.0f); // Lowered minimum from 0.3f to 0.2f
			_trackCamera.Zoom = new Vector2(zoom, zoom);
			
			GD.Print($"[RacingGame] Track bounds: {trackBounds.Size}, Playable area: {playableArea}, Calculated zoom: {zoom}");
			
			// Center camera in playable area
			Vector2 playableCenter = new Vector2(viewportSize.X / 2, TOP_MENU_HEIGHT + playableArea.Y / 2);
			_trackCamera.GlobalPosition = playableCenter;
			
			GD.Print($"[RacingGame] Camera positioned at: {playableCenter}");
		}
	}

	/// <summary>
	/// Setup screen edge colliders around camera view
	/// </summary>
	private void SetupScreenEdgeColliders()
	{
		var viewport = GetViewport();
		if (viewport == null || _trackCamera == null) return;

		ClearScreenEdgeColliders();

		Vector2 viewportSize = viewport.GetVisibleRect().Size;
		
		// Account for top menu bar in playable area calculation
		Vector2 playableAreaSize = new Vector2(viewportSize.X, viewportSize.Y - TOP_MENU_HEIGHT);
		
		Vector2 cameraPos = _trackCamera.GlobalPosition;
		Vector2 cameraZoom = _trackCamera.Zoom;
		Vector2 viewSize = playableAreaSize / cameraZoom;
		Vector2 viewTopLeft = cameraPos - viewSize / 2;
		Vector2 viewBottomRight = cameraPos + viewSize / 2;

		// Create four edge colliders within playable area
		CreateScreenEdgeCollider(new Vector2(viewTopLeft.X - ScreenEdgeColliderThickness / 2, cameraPos.Y), new Vector2(ScreenEdgeColliderThickness, viewSize.Y));
		CreateScreenEdgeCollider(new Vector2(viewBottomRight.X + ScreenEdgeColliderThickness / 2, cameraPos.Y), new Vector2(ScreenEdgeColliderThickness, viewSize.Y));
		CreateScreenEdgeCollider(new Vector2(cameraPos.X, viewTopLeft.Y - ScreenEdgeColliderThickness / 2), new Vector2(viewSize.X, ScreenEdgeColliderThickness));
		CreateScreenEdgeCollider(new Vector2(cameraPos.X, viewBottomRight.Y + ScreenEdgeColliderThickness / 2), new Vector2(viewSize.X, ScreenEdgeColliderThickness));
	}

	private void ClearScreenEdgeColliders()
	{
		foreach (var collider in _screenEdgeColliders)
		{
			if (GodotObject.IsInstanceValid(collider))
				collider.QueueFree();
		}
		_screenEdgeColliders.Clear();
	}

	private void CreateScreenEdgeCollider(Vector2 position, Vector2 size)
	{
		var staticBody = new StaticBody2D();
		staticBody.GlobalPosition = position;

		var collisionShape = new CollisionShape2D();
		var rectangleShape = new RectangleShape2D();
		rectangleShape.Size = size;
		collisionShape.Shape = rectangleShape;

		staticBody.AddChild(collisionShape);
		AddChild(staticBody);
		_screenEdgeColliders.Add(staticBody);
	}

	// ================================================================
	// CHECKPOINT AND TRIGGER MANAGEMENT
	// ================================================================

	/// <summary>
	/// Initialize checkpoint tracking
	/// </summary>
	private void InitializeCheckpointTracking()
	{
		if (_trackDefinition == null) return;

		_checkpointTriggers = _trackDefinition.CheckpointTriggers;
		if (_checkpointTriggers.Length == 0) return;

		_checkpointsCrossed = new bool[_checkpointTriggers.Length];
		_nextCheckpointIndex = 0;
		UpdateCheckpointVisuals();
	}

	/// <summary>
	/// Connect track signals
	/// </summary>
	private void ConnectTrackSignals()
	{
		if (_trackDefinition == null) return;

		var startLineTrigger = _trackDefinition.StartLine;
		if (startLineTrigger != null)
		{
			startLineTrigger.StartLineCrossed += OnStartLineTriggered;
		}

		var finishLineTrigger = _trackDefinition.FinishLine;
		if (finishLineTrigger != null && finishLineTrigger != startLineTrigger)
		{
			finishLineTrigger.StartLineCrossed += OnFinishLineTriggered;
		}

		for (int i = 0; i < _checkpointTriggers.Length; i++)
		{
			var trigger = _checkpointTriggers[i];
			if (trigger != null)
			{
				trigger.CheckpointCrossed += OnCheckpointTriggered;
			}
		}
	}

	/// <summary>
	/// Disconnect track signals
	/// </summary>
	private void DisconnectTrackSignals()
	{
		if (_trackDefinition == null || !GodotObject.IsInstanceValid(_trackDefinition)) return;

		var startLineTrigger = _trackDefinition.StartLine;
		if (startLineTrigger != null && GodotObject.IsInstanceValid(startLineTrigger))
		{
			startLineTrigger.StartLineCrossed -= OnStartLineTriggered;
		}

		var finishLineTrigger = _trackDefinition.FinishLine;
		if (finishLineTrigger != null && GodotObject.IsInstanceValid(finishLineTrigger) && finishLineTrigger != startLineTrigger)
		{
			finishLineTrigger.StartLineCrossed -= OnFinishLineTriggered;
		}

		if (_checkpointTriggers != null)
		{
			for (int i = 0; i < _checkpointTriggers.Length; i++)
			{
				var trigger = _checkpointTriggers[i];
				if (trigger != null && GodotObject.IsInstanceValid(trigger))
				{
					trigger.CheckpointCrossed -= OnCheckpointTriggered;
				}
			}
		}
	}

	// Track signal handlers
	private void OnStartLineTriggered(Node2D body)
	{
		if (body != _racingCar?.GetCarBody()) return;
		
		if (GetGameMode() == GameMode.TimeTrial && GetPlayerCurrentLap(_racingCar.PlayerId) > 0)
		{
			// Check if this should be treated as finish line crossing
			bool shouldTreatAsFinishLine = (_checkpointTriggers.Length == 0) ? 
				(GetPlayerCurrentLap(_racingCar.PlayerId) > 1) : 
				(_nextCheckpointIndex >= _checkpointTriggers.Length);
			
			if (shouldTreatAsFinishLine)
			{
				OnFinishLineTriggered(body);
				return;
			}
		}
		
		// Reset gap timer in practice mode
		if (GetGameMode() == GameMode.Practice)
		{
			OnPlayerCheckpointCrossed(_racingCar.PlayerId, -1); // -1 indicates start line
		}
	}

	private void OnFinishLineTriggered(Node2D body)
	{
		if (body != _racingCar?.GetCarBody()) return;
		
		if (GetGameMode() == GameMode.TimeTrial)
		{
			// Complete the lap
			float lapTime = GetPlayerCurrentLapTime(_racingCar.PlayerId);
			CompletePlayerLap(_racingCar.PlayerId, lapTime);
			ResetCheckpoints();
		}
		else
		{
			// Practice mode - just reset gap timer
			OnPlayerCheckpointCrossed(_racingCar.PlayerId, -1);
		}
	}

	private void OnCheckpointTriggered(Node2D body, int checkpointIndex)
	{
		if (body != _racingCar?.GetCarBody()) return;
		
		if (GetGameMode() == GameMode.TimeTrial)
		{
			if (checkpointIndex == _nextCheckpointIndex)
			{
				_checkpointsCrossed[checkpointIndex] = true;
				_nextCheckpointIndex++;
				
				if (_checkpointTriggers[checkpointIndex] != null)
				{
					_checkpointTriggers[checkpointIndex].MarkAsCrossed();
				}
				
				UpdateCheckpointVisuals();
			}
		}
		
		OnPlayerCheckpointCrossed(_racingCar.PlayerId, checkpointIndex);
	}

	private void ResetCheckpoints()
	{
		if (_checkpointsCrossed == null) return;
		
		for (int i = 0; i < _checkpointsCrossed.Length; i++)
		{
			_checkpointsCrossed[i] = false;
		}
		_nextCheckpointIndex = 0;
		
		_trackDefinition?.ResetAllCheckpoints();
		_trackDefinition?.ResetStartLineColor();
		UpdateCheckpointVisuals();
	}

	private void UpdateCheckpointVisuals()
	{
		if (_checkpointTriggers == null || _checkpointTriggers.Length == 0) return;

		for (int i = 0; i < _checkpointTriggers.Length; i++)
		{
			var checkpoint = _checkpointTriggers[i];
			if (checkpoint == null) continue;

			if (i < _nextCheckpointIndex)
			{
				checkpoint.MarkAsCrossed();
			}
			else if (i == _nextCheckpointIndex && GetGameMode() == GameMode.TimeTrial)
			{
				checkpoint.SetNextRequiredState();
			}
			else
			{
				checkpoint.SetFutureState();
			}
		}
	}

	// ================================================================
	// OFF-TRACK PENALTY SYSTEM
	// ================================================================

	private void UpdateOffTrackPenalties(float delta)
	{
		if (_racingCar?.GetCarBody() == null) return;

		var carPosition = _racingCar.GetCarBody().GlobalPosition;
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

	// ================================================================
	// UI SYSTEM
	// ================================================================

	private void SetupUI()
	{
		_uiLayer = new CanvasLayer();
		AddChild(_uiLayer);

		SetupMainUI();
		SetupCountdownOverlay();
		SetupPauseOverlay();
		SetupGameOverOverlay();
	}

	private void SetupMainUI()
	{
		var mainUI = new Control();
		mainUI.AnchorLeft = 0;
		mainUI.AnchorTop = 0;
		mainUI.AnchorRight = 1;
		mainUI.AnchorBottom = 1;
		mainUI.MouseFilter = Control.MouseFilterEnum.Ignore;
		_uiLayer.AddChild(mainUI);

		// Game status display bar above bottom controls
		var statusBar = new HBoxContainer();
		statusBar.AnchorLeft = 0;
		statusBar.AnchorTop = 1;
		statusBar.AnchorRight = 1;
		statusBar.AnchorBottom = 1;
		statusBar.OffsetTop = -120; // Position above bottom controls
		statusBar.OffsetBottom = -80;
		statusBar.Alignment = BoxContainer.AlignmentMode.Center;
		mainUI.AddChild(statusBar);

		_speedLabel = new Label() { Text = "Speed: 0", HorizontalAlignment = HorizontalAlignment.Center };
		_speedLabel.AddThemeColorOverride("font_color", Colors.White);
		_speedLabel.AddThemeFontSizeOverride("font_size", 16);
		statusBar.AddChild(_speedLabel);
		statusBar.AddChild(new VSeparator());

		_lapLabel = new Label() { Text = "Practice Mode", HorizontalAlignment = HorizontalAlignment.Center };
		_lapLabel.AddThemeColorOverride("font_color", Colors.White);
		_lapLabel.AddThemeFontSizeOverride("font_size", 16);
		statusBar.AddChild(_lapLabel);
		statusBar.AddChild(new VSeparator());

		_timeLabel = new Label() { Text = "Gap: 0.0s", HorizontalAlignment = HorizontalAlignment.Center };
		_timeLabel.AddThemeColorOverride("font_color", Colors.White);
		_timeLabel.AddThemeFontSizeOverride("font_size", 16);
		statusBar.AddChild(_timeLabel);

		// Bottom controls
		var bottomBar = new HBoxContainer();
		bottomBar.AnchorLeft = 0;
		bottomBar.AnchorTop = 1;
		bottomBar.AnchorRight = 1;
		bottomBar.AnchorBottom = 1;
		bottomBar.OffsetTop = -60;
		bottomBar.Alignment = BoxContainer.AlignmentMode.Center;
		mainUI.AddChild(bottomBar);

		// Update button text to show credit cost if applicable
		string timeTrialText = "Start Time Trial (3 Laps)";
		if (TimeTrialCreditCost > 0 && !GameHost.ShouldBypassCredits())
		{
			timeTrialText += $" - {TimeTrialCreditCost} Credit{(TimeTrialCreditCost != 1 ? "s" : "")}";
		}
		_timeTrialButton = new Button() { Text = timeTrialText };
		_timeTrialButton.Pressed += () => { if (!IsGameActive()) StartTimeTrial(); };
		bottomBar.AddChild(_timeTrialButton);
		bottomBar.AddChild(new VSeparator());

		var restartButton = new Button() { Text = "Restart Practice" };
		restartButton.Pressed += () => { 
			// Reset idle timer on interaction
			var userManager = UserManager.GetAutoload();
			if (userManager != null && GodotObject.IsInstanceValid(userManager))
			{
				userManager.ResetUserIdleTimer();
			}
			EndGame(); 
			StartPractice(); 
		};
		bottomBar.AddChild(restartButton);

		// Track selection buttons
		if (TrackScenes != null && TrackScenes.Count > 1)
		{
			bottomBar.AddChild(new VSeparator());
			SetupTrackSelectionButtons(bottomBar);
		}
	}

	private void SetupTrackSelectionButtons(Container parent)
	{
		if (TrackScenes == null || TrackScenes.Count <= 1) return;

		_trackSelectionButtons = new Button[TrackScenes.Count];

		for (int i = 0; i < TrackScenes.Count; i++)
		{
			var trackIndex = i; // Capture loop variable
			var trackButton = new Button();
			
			// Get track name from the scene
			var trackScene = TrackScenes[i];
			if (trackScene != null)
			{
				var tempInstance = trackScene.Instantiate();
				if (tempInstance is TrackDefinition trackDef)
				{
					trackButton.Text = trackDef.TrackName;
					tempInstance.QueueFree();
				}
				else
				{
					trackButton.Text = $"Track {i + 1}";
					tempInstance.QueueFree();
				}
			}
			else
			{
				trackButton.Text = $"Track {i + 1}";
			}

			trackButton.Pressed += () => SwitchToTrack(trackIndex);
			_trackSelectionButtons[i] = trackButton;
			parent.AddChild(trackButton);
		}

		UpdateTrackSelectionButtons();
	}

	private void SwitchToTrack(int trackIndex)
	{
		if (IsGameActive() && GetGameMode() == GameMode.TimeTrial) 
		{
			return; // Don't allow track switching during time trial
		}
		
		// Reset idle timer on interaction
		var userManager = UserManager.GetAutoload();
		if (userManager != null && GodotObject.IsInstanceValid(userManager))
		{
			userManager.ResetUserIdleTimer();
		}
		
		_currentTrackIndex = trackIndex;
		LoadTrack(trackIndex);
		UpdateTrackSelectionButtons();
		
		// Restart practice mode with new track
		EndGame();
		StartPractice();
	}

	private void UpdateTrackSelectionButtons()
	{
		if (_trackSelectionButtons == null) return;

		// Allow track switching in practice mode, but not during time trial or countdown
		bool shouldDisableAll = GetGameMode() == GameMode.TimeTrial && IsGameActive() || IsInCountdown();

		for (int i = 0; i < _trackSelectionButtons.Length; i++)
		{
			if (_trackSelectionButtons[i] != null)
			{
				_trackSelectionButtons[i].Disabled = (i == _currentTrackIndex) || shouldDisableAll;
			}
		}
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

		var pauseLabel = new Label() { Text = "PAUSED", HorizontalAlignment = HorizontalAlignment.Center };
		vbox.AddChild(pauseLabel);

		var resumeButton = new Button() { Text = "Resume" };
		resumeButton.Pressed += ResumeGame;
		vbox.AddChild(resumeButton);

		var restartButton = new Button() { Text = "Restart" };
		restartButton.Pressed += () => { 
			// Reset idle timer on interaction
			var userManager = UserManager.GetAutoload();
			if (userManager != null && GodotObject.IsInstanceValid(userManager))
			{
				userManager.ResetUserIdleTimer();
			}
			ResumeGame(); 
			EndGame(); 
			StartPractice(); 
		};
		vbox.AddChild(restartButton);
		
		var mainMenuButton = new Button() { Text = "Main Menu" };
		mainMenuButton.Pressed += () => {
			// Reset idle timer on interaction
			var userManager = UserManager.GetAutoload();
			if (userManager != null && GodotObject.IsInstanceValid(userManager))
			{
				userManager.ResetUserIdleTimer();
			}
			
			// Return to main menu via GameHost
			var gameHost = GameHost.GetInstance();
			if (gameHost != null && GodotObject.IsInstanceValid(gameHost))
			{
				gameHost.ReturnToMainMenu();
			}
			else
			{
				// Fallback: just end the game
				ResumeGame();
				EndGame();
			}
		};
		vbox.AddChild(mainMenuButton);
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

		var raceCompleteLabel = new Label() { Text = "RACE COMPLETE!", HorizontalAlignment = HorizontalAlignment.Center };
		vbox.AddChild(raceCompleteLabel);

		_finalTimeLabel = new Label() { Text = "Final Time: 0.0s", HorizontalAlignment = HorizontalAlignment.Center };
		vbox.AddChild(_finalTimeLabel);

		var raceAgainButton = new Button() { Text = "Race Again" };
		raceAgainButton.Pressed += () => { 
			// Reset idle timer on interaction
			var userManager = UserManager.GetAutoload();
			if (userManager != null && GodotObject.IsInstanceValid(userManager))
			{
				userManager.ResetUserIdleTimer();
			}
			_gameOverOverlay.Visible = false; 
			StartTimeTrial(); 
		};
		vbox.AddChild(raceAgainButton);

		var practiceButton = new Button() { Text = "Practice Mode" };
		practiceButton.Pressed += () => { 
			// Reset idle timer on interaction
			var userManager = UserManager.GetAutoload();
			if (userManager != null && GodotObject.IsInstanceValid(userManager))
			{
				userManager.ResetUserIdleTimer();
			}
			_gameOverOverlay.Visible = false; 
			StartPractice(); 
		};
		vbox.AddChild(practiceButton);
	}

	private void UpdateUI()
	{
		if (_racingCar == null) return;

		// Update status display labels directly
		UpdateStatusLabels();

		// Update button states
		if (_timeTrialButton != null)
		{
			_timeTrialButton.Disabled = IsGameActive() || IsInCountdown();
		}

		// Update track selection buttons
		UpdateTrackSelectionButtons();

		// Update pause overlay
		if (_pauseOverlay != null)
		{
			_pauseOverlay.Visible = IsGamePaused();
		}
	}

	/// <summary>
	/// Update the status labels in the bottom status bar
	/// </summary>
	private void UpdateStatusLabels()
	{
		if (_racingCar == null) return;

		// Update speed display
		if (_speedLabel != null)
		{
			_speedLabel.Text = $"Speed: {(int)_racingCar.GetCurrentSpeed()}";
		}

		// Update lap/mode display
		if (_lapLabel != null)
		{
			if (GetGameMode() == GameMode.Practice)
			{
				_lapLabel.Text = "Practice Mode";
			}
			else
			{
				int currentLap = GetPlayerCurrentLap(_racingCar.PlayerId);
				_lapLabel.Text = $"Lap: {currentLap}/{TargetLaps}";
			}
		}

		// Update time display
		if (_timeLabel != null)
		{
			if (GetGameMode() == GameMode.Practice)
			{
				float gapTime = GetPlayerGapTime(_racingCar.PlayerId);
				_timeLabel.Text = $"Gap: {gapTime:F1}s";
			}
			else
			{
				float lapTime = GetPlayerCurrentLapTime(_racingCar.PlayerId);
				_timeLabel.Text = $"Time: {lapTime:F1}s";
			}
		}
	}

	/// <summary>
	/// Override game end to show completion UI
	/// </summary>
	public override void EndGame()
	{
		base.EndGame();
		
		if (GetGameMode() == GameMode.TimeTrial && _racingCar != null)
		{
			var playerScore = GetPlayerScore(_racingCar.PlayerId);
			if (_finalTimeLabel != null)
			{
				_finalTimeLabel.Text = $"Final Time: {playerScore:F2}s";
			}
			if (_gameOverOverlay != null)
			{
				_gameOverOverlay.Visible = true;
			}
		}
	}

	// ================================================================
	// VISUAL FEEDBACK SYSTEM
	// ================================================================

	private void UpdateVisualFeedback(float delta)
	{
		if (_racingCar == null) return;
		
		var targetPosition = _racingCar.GetTargetPosition();
		var hasInput = _racingCar.HasInput();
		
		// Track mouse movement state
		if (targetPosition != _lastMousePosition)
		{
			_mouseStationaryTime = 0.0f;
			_lastMousePosition = targetPosition;
		}
		else
		{
			_mouseStationaryTime += delta;
		}

		// Update arc rotation for moving indicator
		_arcRotation += delta * 3.0f;
		if (_arcRotation > Mathf.Pi * 2) _arcRotation -= Mathf.Pi * 2;

		// Track input state changes for visual feedback
		if (hasInput != _wasInputActive)
		{
			_wasInputActive = hasInput;
			QueueRedraw();
		}

		// Queue redraw for animated elements
		if (hasInput || _mouseStationaryTime < 2.0f || _racingCar.GetCurrentSpeed() > 1.0f)
		{
			QueueRedraw();
		}
	}

	private void UpdateTireTrails()
	{
		if (_racingCar?.GetCarBody() == null) return;

		var currentTime = Time.GetTicksMsec();
		
		// Clean up expired trail points
		CleanupExpiredTrailPoints(_leftTireTrail, currentTime);
		CleanupExpiredTrailPoints(_rightTireTrail, currentTime);

		// Only create new trail points when moving at decent speed
		if (_racingCar.GetCurrentSpeed() < 5.0f) return;

		var carBody = _racingCar.GetCarBody();
		var carPosition = carBody.GlobalPosition;
		
		// Only update trails if car has moved significantly
		if (_lastTrailPosition.DistanceTo(carPosition) < TrailUpdateDistance) return;
		
		// Calculate tire positions
		var carForward = new Vector2(Mathf.Sin(carBody.Rotation), -Mathf.Cos(carBody.Rotation));
		var carRight = new Vector2(carForward.Y, -carForward.X);
		var carSize = _racingCar.CarSize;
		var halfLength = carSize.Y * 0.5f;
		var halfWidth = carSize.X * 0.5f;
		var backOffset = carRight * -halfWidth * 0.75f - carForward * halfLength * 1.5f;
		var carBackPosition = carPosition + backOffset;
		var tireOffset = halfWidth * 0.625f;
		var leftTirePosition = carBackPosition - carRight * tireOffset;
		var rightTirePosition = carBackPosition + carRight * tireOffset;
		
		AddTrailPoint(_leftTireTrail, leftTirePosition, currentTime);
		AddTrailPoint(_rightTireTrail, rightTirePosition, currentTime);
		
		_lastTrailPosition = carPosition;
	}

	private void AddTrailPoint(List<(Vector2 position, ulong timestamp)> trail, Vector2 point, ulong timestamp)
	{
		trail.Add((point, timestamp));
		
		while (trail.Count > MaxTrailPoints)
		{
			trail.RemoveAt(0);
		}
	}

	private void CleanupExpiredTrailPoints(List<(Vector2 position, ulong timestamp)> trail, ulong currentTime)
	{
		if (trail == null) return;
		
		for (int i = trail.Count - 1; i >= 0; i--)
		{
			var age = (float)(currentTime - trail[i].timestamp);
			if (age > TrailLifetime)
			{
				trail.RemoveAt(i);
			}
		}
	}

	// ================================================================
	// DRAWING METHODS
	// ================================================================

	private void DrawInputLine()
	{
		if (_racingCar?.GetCarBody() == null) return;
		
		var targetPosition = _racingCar.GetTargetPosition();
		var hasInput = _racingCar.HasInput();
		
		if (targetPosition == Vector2.Zero || (!hasInput && _racingCar.GetCurrentSpeed() < 1.0f)) return;

		var carBody = _racingCar.GetCarBody();
		var carForward = new Vector2(Mathf.Sin(carBody.Rotation), -Mathf.Cos(carBody.Rotation));
		var carRight = new Vector2(carForward.Y, -carForward.X);
		var carSize = _racingCar.CarSize;
		var halfLength = carSize.Y * 0.5f;
		var halfWidth = carSize.X * 0.5f;
		var frontOffset = carRight * -halfWidth * 0.75f + carForward * halfLength * 0.25f;
		var carFrontPosition = carBody.GlobalPosition + frontOffset;
		
		var directionToTarget = (targetPosition - carBody.GlobalPosition);
		var distanceToTarget = directionToTarget.Length();
		var clampedTarget = carBody.GlobalPosition + directionToTarget.Normalized() * Mathf.Min(distanceToTarget, _racingCar.MaxInputDistance);
		
		var inputLineColor = hasInput ? InputLineActiveColor : 
			(_racingCar.GetCurrentSpeed() > 1.0f ? InputLineInactiveColor : InputLineInactiveColor * new Color(1, 1, 1, 0.3f));

		DrawDashedLine(carFrontPosition, clampedTarget, inputLineColor, InputLineWidth, 10.0f, 5.0f);
	}

	private void DrawMouseIndicators()
	{
		if (_racingCar?.GetCarBody() == null) return;
		
		var targetPosition = _racingCar.GetTargetPosition();
		var hasInput = _racingCar.HasInput();
		
		if (targetPosition == Vector2.Zero || (!hasInput && _racingCar.GetCurrentSpeed() < 1.0f)) return;

		var carBody = _racingCar.GetCarBody();
		var directionToTarget = (targetPosition - carBody.GlobalPosition);
		var distanceToTarget = directionToTarget.Length();
		var clampedTarget = carBody.GlobalPosition + directionToTarget.Normalized() * Mathf.Min(distanceToTarget, _racingCar.MaxInputDistance);
		
		// Draw circle at clamped target position
		if (clampedTarget != Vector2.Zero)
		{
			DrawCircle(clampedTarget, MouseIndicatorRadius * 0.4f, MouseStationaryColor);
		}

		// Draw arc under finger/mouse position only if there's active input
		if (hasInput)
		{
			var arcColor = MouseMovingColor;
			var centerDirection = directionToTarget.Angle();
			var normalizedSpeed = Mathf.Clamp(_racingCar.GetCurrentSpeed() / _racingCar.MaxSpeed, 0.0f, 1.0f);
			var sweepAngle = Mathf.Lerp(0.0f, Mathf.Pi, normalizedSpeed);
			
			if (sweepAngle > 0.1f)
			{
				var startAngle = centerDirection - (sweepAngle / 2.0f);
				var endAngle = centerDirection + (sweepAngle / 2.0f);
				DrawArc(targetPosition, MouseIndicatorRadius, startAngle, endAngle, 32, arcColor, 3.0f, true);
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
			var alpha = 1.0f - normalizedAge;
			
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

	// ================================================================
	// CONTEXT DETECTION AND INTEGRATION
	// ================================================================

	private void DetectAndAdaptToContext()
	{
		GD.Print("[RacingGame] DetectAndAdaptToContext() called");
		var gameHost = GameHost.GetInstance();
		GD.Print($"[RacingGame] GameHost instance: {gameHost != null}");
		
		if (gameHost != null)
		{
			// Production context
			var playerSession = gameHost.GetPlayerSession("default");
			if (playerSession != null && _racingCar != null)
			{
				_racingCar.PlayerId = playerSession.PlayerId;
				_racingCar.SetUserData(playerSession.UserData);
			}
		}
		else
		{
			// Development context - auto-start practice mode
			if (_racingCar != null)
			{
				_racingCar.PlayerId = "dev_player";
			}
		}
	}

	// ================================================================
	// RACING DATA GETTERS
	// ================================================================

	public int GetPlayerCurrentLap(string playerId) => _playerCurrentLap.GetValueOrDefault(playerId, 0);
	public float GetPlayerCurrentLapTime(string playerId) => _playerCurrentLapTime.GetValueOrDefault(playerId, 0.0f);
	public float GetPlayerBestLapTime(string playerId) => _playerBestLapTime.GetValueOrDefault(playerId, float.MaxValue);
	public float GetPlayerGapTime(string playerId) => _playerGapTime.GetValueOrDefault(playerId, 0.0f);
	public List<float> GetPlayerLapTimes(string playerId) => _playerLapTimes.GetValueOrDefault(playerId, new List<float>());
	public RacingState GetRacingState() => _racingState;
	public bool IsInCountdown() => _inCountdown;
	public int GetCountdownNumber() => _countdownNumber;
	
	// ================================================================
	// UI INTEGRATION OVERRIDES
	// ================================================================

	/// <summary>
	/// Override to provide racing-specific context buttons
	/// </summary>
	public override ContextButtonData[] GetContextButtons()
	{
		var buttons = new List<ContextButtonData>();

		// Standard "Return to Menu" button
		buttons.Add(GameContextButton.CreateReturnToMenuButton(() => {
			var userManager = UserManager.GetAutoload();
			if (userManager != null && GodotObject.IsInstanceValid(userManager))
			{
				userManager.ResetUserIdleTimer();
			}
			ReturnToMainMenu();
		}));

		// Racing-specific pause/resume button
		if (CanPause)
		{
			if (_isGamePaused)
			{
				buttons.Add(GameContextButton.CreateResumeButton(() => {
					ResumeGame();
					RefreshUI();
				}));
			}
			else if (_isGameActive)
			{
				buttons.Add(GameContextButton.CreatePauseButton(() => {
					PauseGame();
					RefreshUI();
				}));
			}
		}

		// Racing-specific restart button (only in practice mode)
		if (GetGameMode() == GameMode.Practice && !_inCountdown)
		{
			buttons.Add(new ContextButtonData("Restart", () => {
				var userManager = UserManager.GetAutoload();
				if (userManager != null && GodotObject.IsInstanceValid(userManager))
				{
					userManager.ResetUserIdleTimer();
				}
				EndGame();
				StartPractice();
			}, "🔄", true, "Restart practice session"));
		}

		return buttons.ToArray();
	}

	/// <summary>
	/// Override to provide racing game title
	/// </summary>
	public override string GetGameTitle()
	{
		if (GetGameMode() == GameMode.Practice)
		{
			return "Racing Game - Practice";  
		}
		else if (GetGameMode() == GameMode.TimeTrial)
		{
			return "Racing Game - Time Trial";
		}
		return "Racing Game";
	}

	/// <summary>
	/// Handle viewport size changes (e.g., window resize, orientation change)
	/// </summary>
	public override void _Notification(int what)
	{
		base._Notification(what);
		
		if (what == NotificationWMSizeChanged)
		{
			// Recalculate camera positioning and zoom when viewport changes
			if (_trackDefinition != null && _trackCamera != null)
			{
				CallDeferred(MethodName.PositionCameraOverTrack);
				CallDeferred(MethodName.SetupScreenEdgeColliders);
			}
		}
		else if (what == NotificationExitTree)
		{
			// Clean up signals and references
			DisconnectTrackSignals();
			
			if (_racingCar != null && GodotObject.IsInstanceValid(_racingCar))
			{
				_racingCar.CarMoved -= OnCarMoved;
			}
			
			ClearScreenEdgeColliders();
		}
	}
}