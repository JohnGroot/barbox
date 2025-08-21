using Godot;
using System.Collections.Generic;
using System.Linq;

namespace BarBox.Games.Racing;

/// <summary>
/// Complete UI state for Racing Game - eliminates need for state caching
/// </summary>
public class RacingUIState
{
	// Game status
	public bool IsGamePaused { get; set; }
	public bool IsInCountdown { get; set; }
	public bool IsTimeTrialInProgress { get; set; } // Tracks formal race vs practice
	public bool CanStartTimeTrial { get; set; }
	public GameController.GameMode GameMode { get; set; }
		
	// Player data
	public float CarSpeed { get; set; }
	public int CurrentLap { get; set; }
	public int TargetLaps { get; set; }
	public float TimeDisplay { get; set; }
	public string TimeLabel { get; set; }
		
	// Authentication
	public bool IsUserLoggedIn { get; set; }
		
	// Game over
	public bool ShowGameOverOverlay { get; set; }
	public float FinalTime { get; set; }
	public bool CanAffordReplay { get; set; }
}

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
	[Export] public int TargetLaps 
	{ 
		get => _targetLaps; 
		set 
		{ 
			_targetLaps = value; 
			if (_timingSystem != null) 
			{
				_timingSystem.TargetLaps = value;
			}
		} 
	}
	private int _targetLaps = 3;
	[Export] public bool ShowCountdown { get; set; } = true;
	[Export] public float CountdownDuration { get; set; } = 4.0f; // 3-2-1-GO = 4 seconds
	[Export] public int TimeTrialCreditCost { get; set; } = 1; // Credit cost for time trial mode

	[ExportCategory("Track Settings")]
	[Export] public Godot.Collections.Array<PackedScene> TrackScenes { get; set; } = new Godot.Collections.Array<PackedScene>();

	// ================================================================
	// CONSTANTS
	// ================================================================
	
	// Total height reserved for TopMenuBar (100px) + ContextButtonBar (50px)
	private const float TOP_MENU_HEIGHT = 150.0f;

	// ================================================================
	// PRIVATE FIELDS - RACING LOGIC
	// ================================================================
	
	// Racing timing system
	private RacingTimingSystem _timingSystem;
	
	// Track validation system
	private RacingTrackValidationSystem _trackValidationSystem;
	
	// Camera controller
	private RacingCameraController _cameraController;
	
	// Racing car controller
	private RacingCarController _carController;

	// ================================================================
	// PRIVATE FIELDS - TRACK AND WORLD SYSTEMS
	// ================================================================
	
	// Track system
	private RacingTrackDefinition _trackDefinition;
	private Curve2D _trackCurve;
	private RacingCheckpointTrigger[] _checkpointTriggers;
	private bool[] _checkpointsCrossed;
	private int _nextCheckpointIndex = 0;
	private int _currentTrackIndex = 0;

	// ================================================================
	// PRIVATE Fields - UI SYSTEM
	// ================================================================
	
	// UI manager for all racing game UI elements
	private RacingUIManager _uiManager;
	
	// Cached service references
	private UserManager _cachedUserManager;

	// ================================================================
	// PRIVATE FIELDS - VISUAL FEEDBACK SYSTEM
	// ================================================================
	
	// Visual feedback renderer for tire trails, input lines, and mouse indicators
	private RacingVisualFeedbackRenderer _visualRenderer;
	
	// Direct input handling (restored for arc positioning fix)
	private Vector2 _directTargetPosition = Vector2.Zero;
	private bool _hasDirectInput = false;

	// ================================================================
	// INITIALIZATION
	// ================================================================

	public override void _Ready()
	{
		GD.Print("[RacingGame] _Ready() starting");
		GameId = "racing_game";
		SetGameMode(GameMode.Practice); // Start in practice mode

		// Detect context FIRST to ensure proper player data is available
		DetectAndAdaptToContext();

		// Initialize all systems AFTER context detection
		// This ensures timing system exists when InitializeGame() is called
		SetupTimingSystem();
		SetupTrackValidationSystem();
		SetupCameraController();
		SetupCarController();
		SetupUI();
		InitializeTrackSystem();

		// THEN call base which triggers InitializeGame() -> StartPractice()
		base._Ready();

		// Connect to user authentication signals for UI updates - cache reference
		_cachedUserManager = UserManager.GetAutoload();
		if (_cachedUserManager != null && IsInstanceValid(_cachedUserManager))
		{
			_cachedUserManager.UserLoggedIn += OnUserLoggedIn;
			_cachedUserManager.UserLoggedOut += OnUserLoggedOut;
		}
		
		// Ensure UI gets initial update after setup completes
		CallDeferred(MethodName.UpdateUI);
		
		GD.Print("[RacingGame] _Ready() completed");
	}

	protected override void InitializeGame()
	{
		base.InitializeGame();
		StartPractice(); // Default to practice mode
	}

	/// <summary>
	/// Setup the racing timing system
	/// </summary>
	private void SetupTimingSystem()
	{
		_timingSystem = new RacingTimingSystem();
		_timingSystem.TargetLaps = TargetLaps;
		_timingSystem.SetGameMode(_currentGameMode);
		AddChild(_timingSystem);

		// Connect timing system signals for direct processing
		_timingSystem.LapCompleted += OnTimingSystemLapCompleted;
		_timingSystem.RaceCompleted += OnTimingSystemRaceCompleted;
		_timingSystem.CheckpointCrossed += OnTimingSystemCheckpointCrossed;
		
		GD.Print("[RacingGame] RacingTimingSystem initialized successfully");
	}

	/// <summary>
	/// Setup the track validation system
	/// </summary>
	private void SetupTrackValidationSystem()
	{
		_trackValidationSystem = new RacingTrackValidationSystem();
		
		// Set default values (these would be configurable via exports on the system)
		_trackValidationSystem.CenterLineProximityRange = 40.0f;
		_trackValidationSystem.CenterLineAccelerationBonus = 1.5f;
		_trackValidationSystem.TrackProximityRange = 20.0f;
		_trackValidationSystem.OffTrackSpeedPenalty = 0.3f;
		_trackValidationSystem.OffTrackTurnPenalty = 0.3f;
		_trackValidationSystem.OffTrackAccelerationPenalty = 0.3f;
		_trackValidationSystem.OffTrackPenaltyLerpSpeed = 3.0f;
		
		AddChild(_trackValidationSystem);
		
		GD.Print("[RacingGame] RacingTrackValidationSystem initialized successfully");
	}

	/// <summary>
	/// Setup the camera controller system
	/// </summary>
	private void SetupCameraController()
	{
		_cameraController = new RacingCameraController();
		_cameraController.ScreenEdgeColliderThickness = 8.0f; // Default value
		AddChild(_cameraController);

		_cameraController.Initialize();
	
		GD.Print("[RacingGame] RacingCameraController initialized");
	}

	/// <summary>
	/// Setup the racing car controller system
	/// </summary>
	private void SetupCarController()
	{
		_carController = new RacingCarController();

		// Set default car settings
		_carController.MaxSpeed = 1800.0f;
		_carController.MinSpeed = 100.0f;
		_carController.MaxInputDistance = 500.0f;
		_carController.AccelerationRate = 300.0f;
		_carController.DecelerationRate = 600.0f;
		_carController.RotationLerpSpeed = 5.0f;
		_carController.CarSize = new Vector2(40, 80);
		
		AddChild(_carController);

		// Initialize with validated dependencies
		_carController.Initialize(_cameraController, _trackValidationSystem);

		// Connect car movement events for visual feedback
		_carController.CarMoved += OnCarMoved;
		
		// Add player to game controller
		var player = _carController.GetPlayer();
		if (player != null)
		{
			AddPlayer(player);
		}
		else
		{
			GD.PrintErr("[RacingGame] Failed to get player from car controller");
			return;
		}
		
		// Initialize visual feedback renderer
		SetupVisualRenderer();
		
		GD.Print("[RacingGame] RacingCarController initialized successfully");
	}

	/// <summary>
	/// Setup visual feedback renderer
	/// </summary>
	private void SetupVisualRenderer()
	{
		_visualRenderer = new RacingVisualFeedbackRenderer();
		_visualRenderer.Initialize(_carController);
		AddChild(_visualRenderer);

		GD.Print("[RacingGame] RacingVisualFeedbackRenderer initialized successfully");
	}

	/// <summary>
	/// Override SetGameMode to update timing system
	/// </summary>
	public new void SetGameMode(GameMode mode)
	{
		base.SetGameMode(mode);
		
		// Timing system may be null during initial setup
		_timingSystem?.SetGameMode(mode);
	}

	// ================================================================
	// DIRECT INPUT HANDLING (Arc Positioning Fix)
	// ================================================================
	
	/// <summary>
	/// Handle direct input polling for accurate arc positioning
	/// </summary>
	private void HandleDirectInput()
	{
		// Check if input should be enabled based on racing state
		if (!_isGameActive || _isGamePaused || !IsInputEnabled()) 
		{
			_hasDirectInput = false;
			return;
		}
		
		var inputManager = InputManager.GetAutoload();
		if (inputManager == null) 
		{
			_hasDirectInput = false;
			return;
		}
		
		// Get direct input state from InputManager
		_hasDirectInput = inputManager.IsTouchActive();
		
		if (_hasDirectInput)
		{
			// Get raw screen position from InputManager - direct 1:1 mapping like original
			_directTargetPosition = inputManager.GetTouchPosition();
		}
	}

	/// <summary>
	/// Check if input should be enabled based on current racing state
	/// </summary>
	public bool IsInputEnabled()
	{
		return _timingSystem?.IsInputEnabled() ?? true;
	}

	// ================================================================
	// CORE GAME LOOP
	// ================================================================

	public override void _Process(double delta)
	{
		base._Process(delta);
		
		// Handle direct input for arc positioning fix
		HandleDirectInput();
		
		if (_timingSystem != null && _timingSystem.IsInCountdown)
		{
			HandleCountdown((float)delta);
		}
		else if (_isGameActive && !_isGamePaused && _timingSystem != null)
		{
			_timingSystem.UpdateRacingTimers((float)delta, _players);
		}
		
		UpdateUI();
	}

	protected override void UpdateGame(float delta)
	{
		base.UpdateGame(delta);

		if (!IsInstanceValid(_carController) || !_carController.IsInitialized)
			return;

		// Update track validation and penalties
		if (IsInstanceValid(_trackValidationSystem))
		{
			_trackValidationSystem.UpdateOffTrackPenalties(_carController.GetCarPosition(), delta);
		}
			
		// Update visual feedback renderer
		if (IsInstanceValid(_visualRenderer))
		{
			_visualRenderer.UpdateVisualFeedback(delta);
			_visualRenderer.UpdateTireTrails();
		}
	}

	public override void _Draw()
	{
		// Visual feedback is now handled by VisualFeedbackRenderer
		// Update renderer visibility based on game state
		if (!IsInstanceValid(_visualRenderer))
			return;

		_visualRenderer.ShouldRender = IsGameActive() && !IsGamePaused() && 
		                               IsInstanceValid(_carController) && _carController.IsInitialized;
	}

	// ================================================================
	// HIGH-LEVEL SIGNAL HANDLERS
	// ================================================================

	/// <summary>
	/// Handle lap completion - centralized signal processing
	/// </summary>
	private void OnTimingSystemLapCompleted(string playerId, int lapNumber, float lapTime)
	{
		// Update player score (best lap time or total time)
		if (_currentGameMode == GameMode.Practice)
		{
			UpdateScore(playerId, _timingSystem.GetPlayerBestLapTime(playerId));
		}
		else
		{
			// In time trial, score is total time
			float totalTime = _timingSystem.CalculatePlayerScore(playerId, _currentGameMode);
			UpdateScore(playerId, totalTime);
		}
		
		// Emit signal for external integrations (GameHost) at high level
		EmitSignal(SignalName.LapCompleted, playerId, lapNumber, lapTime);
	}

	/// <summary>
	/// Handle race completion - centralized signal processing
	/// </summary>
	private void OnTimingSystemRaceCompleted(string playerId, float totalTime)
	{
		// End the game when race is completed
		EndGame();
		
		// Emit signal for external integrations (GameHost) at high level
		EmitSignal(SignalName.RaceCompleted, playerId, totalTime);
	}

	/// <summary>
	/// Handle checkpoint crossing - centralized signal processing
	/// </summary>
	private void OnTimingSystemCheckpointCrossed(string playerId, int checkpointIndex, float gapTime)
	{
		// Emit signal for external integrations (GameHost) at high level
		EmitSignal(SignalName.CheckpointCrossed, playerId, checkpointIndex, gapTime);
	}

	/// <summary>
	/// Handle user login - refresh UI to enable time trial button
	/// </summary>
	private void OnUserLoggedIn(UserData userData)
	{
		// Just update UI - no cache manipulation needed
		UpdateUI();
	}

	/// <summary>
	/// Handle user logout - refresh UI to disable time trial button
	/// </summary>
	private void OnUserLoggedOut()
	{
		// Just update UI - no cache manipulation needed
		UpdateUI();
	}

	// ================================================================
	// RACING MECHANICS - GAME MODES
	// ================================================================

	/// <summary>
	/// Start a time trial race with countdown
	/// </summary>
	public virtual async void StartTimeTrial()
	{
		// Can only start time trial when not already in time trial
		if (GetGameMode() == GameMode.TimeTrial) 
		{
			GD.Print("Time trial cancelled - already in time trial mode");
			return;
		}
		
		// Always require login first, regardless of development mode
		if (_cachedUserManager == null || !IsInstanceValid(_cachedUserManager))
		{
			GD.PrintErr("Time trial cancelled - user management system not available");
			return;
		}
		
		bool isLoggedIn = _cachedUserManager.IsUserLoggedIn();
		if (!isLoggedIn)
		{
			GD.Print("Time trial cancelled - user must be logged in");
			return;
		}
		
		// Reset idle timer when starting a premium feature
		_cachedUserManager.ResetUserIdleTimer();
		
		// Check credits only in production mode or when explicitly required
		bool isDevelopmentMode = Engine.IsEditorHint() || OS.IsDebugBuild();
		if (TimeTrialCreditCost > 0 && !isDevelopmentMode)
		{
			// Set state to waiting for credits during async check
			_timingSystem?.SetWaitingForCredits();
			
			var creditManager = CreditManager.GetInstance();
			if (creditManager == null || !IsInstanceValid(creditManager))
			{
				GD.PrintErr("Time trial cancelled - credit system not available");
				_timingSystem?.StopRacing(); // Reset to idle state
				return;
			}
			
			bool creditsSpent = await creditManager.CheckAndSpendCredits(TimeTrialCreditCost, "Time Trial Race");
			if (!creditsSpent)
			{
				// Credits not spent - don't start the race
				GD.Print("Time trial cancelled - credits not spent");
				_timingSystem?.StopRacing(); // Reset to idle state
				return;
			}
		}
		
		SetGameMode(GameMode.TimeTrial);
		ResetForNewRace(); // Complete reset including car physics state
		
		if (ShowCountdown)
		{
			StartCountdown();
		}
		else
		{
			_timingSystem?.StartRacing(); // Set racing state
			StartGame();
		}
	}

	/// <summary>
	/// Start practice mode (no countdown, continuous play)
	/// </summary>
	public virtual void StartPractice()
	{
		SetGameMode(GameMode.Practice);
		ResetForNewRace(); // Complete reset including car physics state
		_timingSystem?.StartPracticeMode(); // Set appropriate state
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
		_timingSystem.StartCountdown(CountdownDuration);
		OnCountdownStarted();
	}

	/// <summary>
	/// Handle countdown timer and progression
	/// </summary>
	protected virtual void HandleCountdown(float delta)
	{
		bool countdownChanged = _timingSystem.UpdateCountdown(delta, CountdownDuration);

		// Check if countdown number changed
		int countdownNumber = _timingSystem.CountdownNumber;
		switch (countdownChanged)
		{
			case true when countdownNumber > 0:
				OnCountdownNumber(countdownNumber);
				break;
			case true:
				OnCountdownGo();
				break;
		}

		// Check if countdown completed
		if (!_timingSystem.IsInCountdown)
		{
			EndCountdown();
		}
	}

	/// <summary>
	/// End countdown and start the actual race
	/// </summary>
	protected virtual void EndCountdown()
	{
		_timingSystem.EndCountdown();
		_timingSystem.StartRacing();
		StartGame();
		OnCountdownEnded();
			
		// Start first lap for all players in time trial mode
		foreach (var player in _players)
		{
			_timingSystem.StartPlayerLap(player.PlayerId);
		}
	}

	// Countdown event handlers - override in RacingGame for custom display
	protected virtual void OnCountdownStarted()
	{
		_uiManager?.SetCountdownOverlayVisible(true);
	}

	protected virtual void OnCountdownNumber(int number)
	{
		_uiManager?.UpdateCountdownText(number.ToString());
	}

	protected virtual void OnCountdownGo()
	{
		_uiManager?.UpdateCountdownText("GO!");
	}

	protected virtual void OnCountdownEnded()
	{
		_uiManager?.SetCountdownOverlayVisible(false);
	}

	// ================================================================
	// RACING MECHANICS - LAP MANAGEMENT
	// ================================================================

	/// <summary>
	/// Start a new lap for a player - delegates to timing system
	/// </summary>
	public virtual void StartPlayerLap(string playerId)
	{
		_timingSystem.StartPlayerLap(playerId);
	}

	/// <summary>
	/// Complete a lap for a player - delegates to timing system
	/// </summary>
	public virtual void CompletePlayerLap(string playerId, float lapTime)
	{
		_timingSystem.CompletePlayerLap(playerId, lapTime);
		
		// Get current lap AFTER completing the lap
		int currentLap = _timingSystem.GetPlayerCurrentLap(playerId);
		
		if (_currentGameMode == GameMode.Practice)
		{
			// Always start next lap in practice mode
			_timingSystem.StartPlayerLap(playerId);
			_timingSystem?.StartPracticeMode();
		}
		else if (currentLap >= TargetLaps)
		{
			// Race complete - all target laps finished
			CompletePlayerRace(playerId);
		}
		else
		{
			// Start next lap in time trial
			_timingSystem.StartPlayerLap(playerId);
			_timingSystem?.StartRacing();
		}
	}

	/// <summary>
	/// Complete the race for a player - delegates to timing system
	/// </summary>
	protected virtual void CompletePlayerRace(string playerId)
	{
		_timingSystem.CompletePlayerRace(playerId, _players);
			
		// Check if race state changed to finished
		if (_timingSystem.CurrentRacingState == RacingTimingSystem.RacingState.Finished)
		{
			EndGame();
		}
	}

	/// <summary>
	/// Player crossed a checkpoint - delegates to timing system
	/// </summary>
	public virtual void OnPlayerCheckpointCrossed(string playerId, int checkpointIndex)
	{
		_timingSystem.OnPlayerCheckpointCrossed(playerId, checkpointIndex);
	}

	/// <summary>
	/// Reset all racing-specific data - delegates to timing system
	/// </summary>
	protected virtual void ResetRacingData()
	{
		_timingSystem.ResetRacingData();
	}

	/// <summary>
	/// Complete reset for starting a new race or practice session
	/// Ensures both timing data and car physics state are properly reset
	/// </summary>
	protected virtual void ResetForNewRace()
	{
		ResetRacingData();           // Reset timing data
		PositionCarAtStart();        // Reset position AND physics state (calls ResetCarState)
		_carController?.SetActive(true); // Reactivate car controller
	}

	// ================================================================
	// CAR MANAGEMENT
	// ================================================================

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
		if (TrackScenes is { Count: > 0 })
		{
			_currentTrackIndex = 0;
			LoadTrack(0);
		}
		else
		{
			// Look for existing TrackDefinition child
			foreach (Node child in GetChildren())
			{
				if (child is not RacingTrackDefinition trackDef)
					continue;

				_trackDefinition = trackDef;
				_currentTrackIndex = 0;
				SetupLoadedTrack();
				return;
			}

			GD.PrintErr("RacingGame: No track scenes configured and no RacingTrackDefinition child node found.");
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
		
		if (trackInstance is RacingTrackDefinition trackDef)
		{
			_trackDefinition = trackDef;
			AddChild(_trackDefinition);
			
			// Position track at screen center like original working implementation
			var viewport = GetViewport();
			if (viewport != null)
			{
				var screenSize = viewport.GetVisibleRect().Size;
				Vector2 screenCenter = screenSize / 2;
				_trackDefinition.GlobalPosition = screenCenter;
				GD.Print($"[RacingGame] Track positioned at screen center (like original): {screenCenter}");
			}
			
			SetupLoadedTrack();
		}
		else
		{
			GD.PrintErr("Track scene does not contain RacingTrackDefinition");
			trackInstance.QueueFree();
		}
	}

	/// <summary>
	/// Setup loaded track
	/// </summary>
	private void SetupLoadedTrack()
	{
		if (_trackDefinition == null) 
			return;

		_trackDefinition.SetupTrack();
		_trackCurve = _trackDefinition.GetTrackCurve();
		
		// Initialize track validation system with track data
		_trackValidationSystem.Initialize(_trackDefinition, _trackCurve);
		
		// Initialize camera controller with track data
		_cameraController.SetTrackDefinition(_trackDefinition);
		_cameraController.PositionCameraOverTrack();
		_cameraController.SetupScreenEdgeColliders();

		TargetLaps = _trackDefinition.NumberOfLaps;
		InitializeCheckpointTracking();
		ConnectTrackSignals();
		PositionCarAtStart();
	}

	/// <summary>
	/// Position car at track start
	/// </summary>
	private void PositionCarAtStart()
	{
		if (_trackDefinition == null || _carController?.IsInitialized != true) 
			return;

		var startPoint = _trackDefinition.GetStartLinePosition();
		var startLineDirection = _trackDefinition.GetStartLineDirection();
		
		if (startPoint == Vector2.Zero)
		{
			var viewport = GetViewport();
			if (viewport == null)
				return;

			var screenSize = viewport.GetVisibleRect().Size;
			Vector2 screenCenter = screenSize / 2;
			_carController.PositionCar(screenCenter);
			return; // Early return since we positioned car at center
		}
		
		var carPosition = startPoint - startLineDirection * 50;
		var carRotation = startLineDirection.Angle() + Mathf.Pi / 2;
		_carController.PositionCar(carPosition, carRotation);
	}

	// Track utility methods - delegates to track validation system
	public bool IsOnTrack(Vector2 position)
	{
		return _trackValidationSystem?.IsOnTrack(position) ?? true;
	}

	public float GetDistanceToTrackCenterLine(Vector2 position)
	{
		return _trackValidationSystem?.GetDistanceToTrackCenterLine(position) ?? float.MaxValue;
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
		if (_trackDefinition == null || !IsInstanceValid(_trackDefinition)) 
			return;

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
		if (_trackDefinition == null || !IsInstanceValid(_trackDefinition))
			return;

		var startLineTrigger = _trackDefinition.StartLine;
		if (startLineTrigger != null && IsInstanceValid(startLineTrigger))
		{
			startLineTrigger.StartLineCrossed -= OnStartLineTriggered;
		}

		var finishLineTrigger = _trackDefinition.FinishLine;
		if (finishLineTrigger != null && IsInstanceValid(finishLineTrigger) && finishLineTrigger != startLineTrigger)
		{
			finishLineTrigger.StartLineCrossed -= OnFinishLineTriggered;
		}

		if (_checkpointTriggers != null)
		{
			foreach (var trigger in _checkpointTriggers)
			{
				if (trigger != null && IsInstanceValid(trigger))
				{
					trigger.CheckpointCrossed -= OnCheckpointTriggered;
				}
			}
		}
	}

	// Track signal handlers
	private void OnStartLineTriggered(Node2D body)
	{
		if (!IsInstanceValid(_carController) || !_carController.IsInitialized) 
			return;

		if (body != _carController.GetCarBody()) 
			return;

		BasePlayer player = _carController.GetPlayer();
		if (player == null) 
			return;

		string playerId = player.PlayerId ?? "player1";
		if (GetGameMode() == GameMode.TimeTrial && GetPlayerCurrentLap(playerId) > 0)
		{
			// Check if this should be treated as finish line crossing
			bool shouldTreatAsFinishLine = _checkpointTriggers.Length == 0 ? 
				GetPlayerCurrentLap(playerId) > 1 : 
				_nextCheckpointIndex >= _checkpointTriggers.Length;
			
			if (shouldTreatAsFinishLine)
			{
				OnFinishLineTriggered(body);
				return;
			}
		}
		
		// Reset gap timer in practice mode
		if (GetGameMode() == GameMode.Practice)
		{
			OnPlayerCheckpointCrossed(playerId, -1); // -1 indicates start line
		}
	}

	private void OnFinishLineTriggered(Node2D body)
	{
		if (!IsInstanceValid(_carController) || !_carController.IsInitialized) 
			return;
		
		if (body != _carController.GetCarBody()) 
			return;
		
		BasePlayer player = _carController.GetPlayer();
		if (player == null) 
			return;

		string playerId = player.PlayerId ?? "player1";
		
		// Set processing state during finish line handling
		_timingSystem?.SetCrossingFinish();
		
		if (GetGameMode() == GameMode.TimeTrial)
		{
			// Complete the lap - let CompletePlayerLap handle all the logic
			float lapTime = GetPlayerCurrentLapTime(playerId);
			CompletePlayerLap(playerId, lapTime);
			ResetCheckpoints();
			// Do NOT duplicate lap checking here - CompletePlayerLap handles it
		}
		else
		{
			// Practice mode - always start new lap
			OnPlayerCheckpointCrossed(playerId, -1);
			// Restore practice state
			_timingSystem?.StartPracticeMode();
		}
	}

	private void OnCheckpointTriggered(Node2D body, int checkpointIndex)
	{
		if (!IsInstanceValid(_carController) || !_carController.IsInitialized) 
			return;

		if (body != _carController.GetCarBody()) 
			return;

		BasePlayer player = _carController.GetPlayer();
		if (player == null) 
			return;

		string playerId = player.PlayerId ?? "player1";
		if (GetGameMode() == GameMode.TimeTrial && checkpointIndex == _nextCheckpointIndex)
		{
			_checkpointsCrossed[checkpointIndex] = true;
			_nextCheckpointIndex++;
				
			if (_checkpointTriggers[checkpointIndex] != null)
			{
				_checkpointTriggers[checkpointIndex].MarkAsCrossed();
			}
				
			UpdateCheckpointVisuals();
		}
		
		OnPlayerCheckpointCrossed(playerId, checkpointIndex);
	}

	private void ResetCheckpoints()
	{
		if (_checkpointsCrossed == null) 
			return;
		
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
	// UI SYSTEM
	// ================================================================

	private void SetupUI()
	{
		_uiManager = new RacingUIManager();
		var trackScenesList = TrackScenes?.ToList();
		_uiManager.Initialize(trackScenesList, _currentTrackIndex, TimeTrialCreditCost);
		AddChild(_uiManager);

		// Connect UI manager signals
		_uiManager.TimeTrialRequested += StartTimeTrial;
		_uiManager.RestartRequested += () => { 
			// Only call EndGame() if we're in time trial mode
			if (GetGameMode() == GameMode.TimeTrial)
			{
				EndGame();
			}
			StartPractice(); 
		};
		_uiManager.TrackSwitchRequested += OnTrackSwitchRequested;
		_uiManager.ResumeRequested += ResumeGame;
		_uiManager.MainMenuRequested += HandleMainMenuRequest;
		_uiManager.RaceAgainRequested += OnRaceAgainRequested;
		_uiManager.PracticeModeRequested += OnEndToPracticeModeRequested;
	}

	/// <summary>
	/// Handle track switch request from UI manager
	/// </summary>
	/// <param name="trackIndex">Index of track to switch to</param>
	private void OnTrackSwitchRequested(int trackIndex)
	{
		if (IsGameActive() && GetGameMode() == GameMode.TimeTrial) 
		{
			return; // Don't allow track switching during time trial
		}
		
		// Set track loading state to disable input
		_timingSystem?.SetTrackLoading();
		
		// End current race/practice session
		if (IsGameActive())
		{
			EndGame();
		}
		
		_currentTrackIndex = trackIndex;
		LoadTrack(trackIndex);
		_uiManager?.SetCurrentTrackIndex(trackIndex);
		
		// Restart practice mode with new track
		StartPractice();
	}

	/// <summary>
	/// Handle main menu request from UI manager
	/// </summary>
	private void HandleMainMenuRequest()
	{
		// Return to main menu via GameHost
		var gameHost = GameHost.GetInstance();
		if (gameHost != null && IsInstanceValid(gameHost))
		{
			gameHost.ReturnToMainMenu();
		}
		else
		{
			// Fallback: just end the game
			EndGame();
		}
	}

	private void OnEndToPracticeModeRequested()
	{
		// Reset state and start practice
		_timingSystem?.StopRacing(); // Reset to idle
		StartPractice();
	}

	private void OnRaceAgainRequested()
	{
		// Reset state and start new time trial
		_timingSystem?.StopRacing(); // Reset to idle
		SetGameMode(GameMode.Practice); // Reset game mode so StartTimeTrial can work
		StartTimeTrial(); // StartTimeTrial will call ResetForNewRace() which handles complete reset
	}

	/// <summary>
	/// Show high score display
	/// </summary>
	public void ShowHighScores()
	{
		_timingSystem?.SetHighScoreDisplay();
		
		// Try to get LeaderboardManager - it may not be available yet
		try
		{
			// Check if LeaderboardManager exists in autoload
			var autoloadNode = GetTree().Root.GetNodeOrNull("LeaderboardManager");
			if (autoloadNode != null)
			{
				// Display leaderboard UI
				GD.Print("Showing racing game leaderboard");
				// TODO: Implement leaderboard UI integration when available
			}
			else
			{
				ShowHighScoresFallback();
			}
		}
		catch
		{
			ShowHighScoresFallback();
		}
	}

	private void ShowHighScoresFallback()
	{
		// Fallback: show simple message
		GD.Print("High scores feature not available");
		// Return to previous state after brief delay
		CreateAutoCleanupTimer(2.0f, () => {
			if (GetGameMode() == GameMode.Practice)
				_timingSystem?.StartPracticeMode();
			else
				_timingSystem?.StopRacing();
		});
	}

	/// <summary>
	/// Hide high score display and return to appropriate state
	/// </summary>
	public void HideHighScores()
	{
		if (GetGameMode() == GameMode.Practice)
			_timingSystem?.StartPracticeMode();
		else
			_timingSystem?.StopRacing(); // Return to idle
	}

	/// <summary>
	/// Create a timer that automatically cleans up after timeout
	/// </summary>
	private Timer CreateAutoCleanupTimer(float waitTime, System.Action onTimeout)
	{
		var timer = new Timer();
		timer.WaitTime = waitTime;
		timer.OneShot = true;
		timer.Timeout += () => {
			onTimeout?.Invoke();
			timer.QueueFree();
		};
		AddChild(timer);
		timer.Start();
		return timer;
	}

	/// <summary>
	/// Override pause to handle racing state properly
	/// </summary>
	public override void PauseGame()
	{
		base.PauseGame();
		_timingSystem?.SetPaused(true);
	}

	/// <summary>
	/// Override resume to handle racing state properly
	/// </summary>
	public override void ResumeGame()
	{
		base.ResumeGame();
		_timingSystem?.SetPaused(false);
	}
	
	private void UpdateUI()
	{
		if (!IsInstanceValid(_uiManager)) 
			return;
		
		// Always gather fresh state - no caching, no change detection
		var state = GatherCurrentState();
		
		// Always update UI - let Godot handle rendering optimization
		_uiManager.UpdateFromState(state);
	}

	/// <summary>
	/// Gather current game state for UI updates
	/// </summary>
	private RacingUIState GatherCurrentState()
	{
		var player = _carController?.GetPlayer();
		var playerId = player?.PlayerId ?? "player1";
		var gameMode = GetGameMode();
		var loggedIn = _cachedUserManager?.IsUserLoggedIn() ?? false;
		var currentRacingState = _timingSystem?.CurrentRacingState ?? RacingTimingSystem.RacingState.Idle;
		
		// Determine if a formal time trial is in progress (vs practice mode)
		bool isTimeTrialInProgress = gameMode == GameMode.TimeTrial && 
			currentRacingState != RacingTimingSystem.RacingState.Idle &&
			currentRacingState != RacingTimingSystem.RacingState.PracticeMode;
		
		// Check if user can start time trial based on state and affordability
		bool canStartTimeTrial = loggedIn && 
			!isTimeTrialInProgress && 
			currentRacingState != RacingTimingSystem.RacingState.WaitingForCredits &&
			currentRacingState != RacingTimingSystem.RacingState.TrackLoading &&
			currentRacingState != RacingTimingSystem.RacingState.GameOverDeciding &&
			currentRacingState != RacingTimingSystem.RacingState.HighScoreDisplay;

		// Check if user can afford replay (simplified check for now)
		bool isDevelopmentMode = Engine.IsEditorHint() || OS.IsDebugBuild();
		bool canAffordReplay = loggedIn && (isDevelopmentMode || TimeTrialCreditCost == 0);
		
		return new RacingUIState
		{
			IsGamePaused = IsGamePaused(),
			IsInCountdown = IsInCountdown(),
			IsTimeTrialInProgress = isTimeTrialInProgress,
			CanStartTimeTrial = canStartTimeTrial,
			GameMode = gameMode,
			CarSpeed = _carController?.GetCarSpeed() ?? 0f,
			CurrentLap = GetPlayerCurrentLap(playerId),
			TargetLaps = TargetLaps,
			TimeDisplay = gameMode == GameMode.Practice ? 
				GetPlayerGapTime(playerId) : GetPlayerCurrentLapTime(playerId),
			TimeLabel = gameMode == GameMode.Practice ? "Gap" : "Time",
			IsUserLoggedIn = loggedIn,
			ShowGameOverOverlay = currentRacingState == RacingTimingSystem.RacingState.GameOverDeciding,
			FinalTime = GetPlayerScore(playerId),
			CanAffordReplay = canAffordReplay
		};
	}

	/// <summary>
	/// Override game end to show completion UI
	/// </summary>
	public override void EndGame()
	{
		base.EndGame();
		
		// Show game over overlay for time trials by updating UI state
		if (GetGameMode() != GameMode.TimeTrial)
			return;

		// Set game over state to disable input
		_timingSystem?.SetGameOverDeciding();

		// Update UI - GatherCurrentState will detect GameOverDeciding state and show overlay
		var state = GatherCurrentState();
		_uiManager?.UpdateFromState(state);
		
		_carController.SetActive(false);
	}


	// ================================================================
	// CONTEXT DETECTION AND INTEGRATION
	// ================================================================

	private void DetectAndAdaptToContext()
	{
		GD.Print("[RacingGame] DetectAndAdaptToContext() called");
		var gameHost = GameHost.GetInstance();
		GD.Print($"[RacingGame] GameHost instance: {gameHost != null}");

		if (gameHost != null && IsInstanceValid(gameHost))
		{
			// Production context
			var playerSession = gameHost.GetPlayerSession("default");
			if (playerSession != null && IsInstanceValid(_carController) && _carController.IsInitialized)
			{
				var racingCar = _carController.RacingCar;
				if (racingCar != null)
				{
					racingCar.PlayerId = playerSession.PlayerId;
					_carController.SetUserData(playerSession.UserData);
				}
			}
		}
		else if (IsInstanceValid(_carController) && _carController.IsInitialized)
		{
			// Development context - auto-start practice mode
			var racingCar = _carController.RacingCar;
			if (racingCar != null)
			{
				racingCar.PlayerId = "dev_player";
			}
		}
	}

	// ================================================================
	// RACING DATA GETTERS
	// ================================================================

	public int GetPlayerCurrentLap(string playerId) => IsInstanceValid(_timingSystem) ? _timingSystem.GetPlayerCurrentLap(playerId) : 0;
	public float GetPlayerCurrentLapTime(string playerId) => IsInstanceValid(_timingSystem) ? _timingSystem.GetPlayerCurrentLapTime(playerId) : 0.0f;
	public float GetPlayerBestLapTime(string playerId) => IsInstanceValid(_timingSystem) ? _timingSystem.GetPlayerBestLapTime(playerId) : 0.0f;
	public float GetPlayerGapTime(string playerId) => IsInstanceValid(_timingSystem) ? _timingSystem.GetPlayerGapTime(playerId) : 0.0f;
	public List<float> GetPlayerLapTimes(string playerId) => IsInstanceValid(_timingSystem) ? _timingSystem.GetPlayerLapTimes(playerId) : new List<float>();
	public RacingTimingSystem.RacingState GetRacingState() => IsInstanceValid(_timingSystem) ? _timingSystem.CurrentRacingState : RacingTimingSystem.RacingState.Idle;
	public bool IsInCountdown() => IsInstanceValid(_timingSystem) && _timingSystem.IsInCountdown;
	public int GetCountdownNumber() => IsInstanceValid(_timingSystem) ? _timingSystem.CountdownNumber : 0;
	
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
			if (_cachedUserManager != null && IsInstanceValid(_cachedUserManager))
			{
				_cachedUserManager.ResetUserIdleTimer();
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
		if (GetGameMode() == GameMode.Practice && !IsInCountdown())
		{
			buttons.Add(new ContextButtonData("Restart", () => {
				if (_cachedUserManager != null && IsInstanceValid(_cachedUserManager))
				{
					_cachedUserManager.ResetUserIdleTimer();
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

	public override void _ExitTree()
	{
		base._ExitTree();

		// Clean up signals and references
		DisconnectTrackSignals();

		if (IsInstanceValid(_carController))
		{
			_carController.CarMoved -= OnCarMoved;
		}

		// Disconnect timing system signals
		if (IsInstanceValid(_timingSystem))
		{
			_timingSystem.LapCompleted -= OnTimingSystemLapCompleted;
			_timingSystem.RaceCompleted -= OnTimingSystemRaceCompleted;
			_timingSystem.CheckpointCrossed -= OnTimingSystemCheckpointCrossed;
		}

		// Disconnect user manager signals
		if (_cachedUserManager != null && IsInstanceValid(_cachedUserManager))
		{
			_cachedUserManager.UserLoggedIn -= OnUserLoggedIn;
			_cachedUserManager.UserLoggedOut -= OnUserLoggedOut;
		}
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
			if (_cameraController != null)
			{
				_cameraController.OnViewportSizeChanged();
			}
		}
	}
}