using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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

	// Arc HUD specific data
	public float MaxSpeed { get; set; } = 1800.0f;
	public float LapProgress { get; set; } = 0.0f; // 0.0 to 1.0 for current lap completion
	public int CountdownNumber { get; set; } = 0; // 3, 2, 1, or 0 for GO
	public float CountdownProgress { get; set; } = 0.0f; // 0.0 to 1.0 for arc animation

	// Authentication
	public bool IsUserLoggedIn { get; set; }

	// Game over
	public bool ShowGameOverOverlay { get; set; }
	public float FinalTime { get; set; }
	public bool CanAffordReplay { get; set; }

	// Tracks & Leaderboard overlay
	public bool ShowTracksLeaderboardOverlay { get; set; }
	public bool CanShowTracksLeaderboard { get; set; }
	public Dictionary<string, RacingUIManager.TrackMetadata> TrackMetadata { get; set; } = new();
	public string CurrentTrackId { get; set; } = "";
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
	private RacingCar _racingCar;

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
	
	// Track ID management for stable data storage
	private Dictionary<int, string> _trackIndexToIdMap = new();
	private string _currentTrackId = "unknown_track";

	// ================================================================
	// PRIVATE Fields - UI SYSTEM
	// ================================================================
	
	// UI manager for all racing game UI elements
	private RacingUIManager _uiManager;
	
	// Cached service references
	private UserManager _cachedUserManager;
	private SessionManager _sessionManager;

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
		// _Ready() starting
		GameId = "racing_game";
		SetGameMode(GameMode.Practice); // Start in practice mode

		// Initialize systems - no special context handling needed
		// Login is required for data persistence, but games work without it
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
		
		// Get SessionManager for global high score tracking
		_sessionManager = SessionManager.GetInstance();
		
		// Ensure UI gets initial update after setup completes
		CallDeferred(MethodName.UpdateUI);
		
		// Initialize tracks & leaderboard system
		CallDeferred(MethodName.InitializeTracksLeaderboardSystem);
		
		// _Ready() completed
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
		
		// RacingTimingSystem initialized successfully
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
		
		// RacingTrackValidationSystem initialized successfully
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
	
		// RacingCameraController initialized
	}

	/// <summary>
	/// Setup the racing car controller system
	/// </summary>
	private void SetupCarController()
	{
		_racingCar = new RacingCar();

		// Set default car settings
		_racingCar.MaxSpeed = 1800.0f;
		_racingCar.MinSpeed = 100.0f;
		_racingCar.MaxInputDistance = 500.0f;
		_racingCar.AccelerationRate = 300.0f;
		_racingCar.DecelerationRate = 600.0f;
		_racingCar.RotationLerpSpeed = 5.0f;
		_racingCar.CarSize = new Vector2(40, 80);
		
		AddChild(_racingCar);

		// Initialize with validated dependencies and phone number as player ID
		var phoneNumber = GetCurrentPlayerPhoneNumber() ?? "guest_player";
		_racingCar.Initialize(_cameraController, _trackValidationSystem, phoneNumber, "Player");

		// Connect car movement events for visual feedback
		_racingCar.CarMoved += OnCarMoved;
		
		// Add player to game controller
		var player = _racingCar.GetPlayer();
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
		
		// RacingCarController initialized successfully
	}

	/// <summary>
	/// Setup visual feedback renderer
	/// </summary>
	private void SetupVisualRenderer()
	{
		_visualRenderer = new RacingVisualFeedbackRenderer();
		_visualRenderer.Initialize(_racingCar);
		AddChild(_visualRenderer);

		// RacingVisualFeedbackRenderer initialized successfully
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

		if (!IsInstanceValid(_racingCar) || !_racingCar.IsInitialized)
			return;

		// Update track validation and penalties
		if (IsInstanceValid(_trackValidationSystem))
		{
			_trackValidationSystem.UpdateOffTrackPenalties(_racingCar.GetCarPosition(), delta);
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
		                               IsInstanceValid(_racingCar) && _racingCar.IsInitialized;
	}

	// ================================================================
	// HIGH-LEVEL SIGNAL HANDLERS
	// ================================================================

	/// <summary>
	/// Handle lap completion - centralized signal processing and data saving
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
		
		// Save best lap time only if user is logged in (uses phone number as real ID)
		var currentPhoneNumber = GetCurrentPlayerPhoneNumber();
		if (currentPhoneNumber != null)
		{
			_ = SaveBestLapTime(currentPhoneNumber, lapTime);
		}
		
		// Emit signal for external integrations (GameHost) at high level
		EmitSignal(SignalName.LapCompleted, playerId, lapNumber, lapTime);
	}

	/// <summary>
	/// Handle race completion - centralized signal processing and global high score tracking
	/// </summary>
	private void OnTimingSystemRaceCompleted(string playerId, float totalTime)
	{
		// Save global high score only if user is logged in (uses phone number as real ID)
		var currentPhoneNumber = GetCurrentPlayerPhoneNumber();
		if (currentPhoneNumber != null)
		{
			_ = SaveGlobalHighScore(currentPhoneNumber, totalTime);
		}

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
	/// Provides comprehensive help content for the Racing Game
	/// Explains controls, game modes, track mastery, and scoring systems
	/// </summary>
	protected override HelpContentData GetHelpContent()
	{
		return new HelpContentData("RACING HOW-TO")
			.AddSection("🏁 BUCKLE UP 🚘",
				"• Touch and drag to steer your car around the track",
				"• The car will always move towards your finger's position",
				"• The closer your finger is to the car the slower it will go",
				"• Complete laps as fast as possible by crossing all checkpoints in order",
				"• If you go off the track you'll get Slowed Down",
				"• Practice mode runs continuously - Time Trial races against the clock!")

			.AddSection("🏎️ GAME MODES 🏎️",
				"• Practice Mode - FREE continuous racing to learn tracks and improve lap times",
				"• Time Trial - PREMIUM timed races that cost credits but save your official high scores",
				"• Switch between tracks to explore different challenges!");
	}

	/// <summary>
	/// Save global high score for racing game using direct DataStore integration
	/// Tracks best times globally with modern C# patterns and fire-and-forget saves
	/// </summary>
	private async Task SaveGlobalHighScore(string playerId, float totalTime)
	{
		if (string.IsNullOrEmpty(playerId) || totalTime <= 0.0f) 
			return;
		
		var dataStore = DataStore.GetInstance();
		if (dataStore == null)
		{
			GD.PrintErr("[RacingGame] DataStore not available for saving high score");
			return;
		}
		
		// Fire-and-forget save with proper error handling
		try
		{
			var globalDataResult = await dataStore.GetGlobalDataAsync(playerId);
			if (!globalDataResult.IsSuccess)
			{
				GD.PrintErr($"[RacingGame] Failed to get global data: {globalDataResult.Error}");
				return;
			}
			
			var globalData = globalDataResult.Value;
			
			// Create complete race entry with comprehensive timing data
			var raceEntry = new RaceEntry(
				RaceId: RaceDatabase.GenerateRaceId(),
				Date: DateTime.Now,
				TrackId: _currentTrackId,
				PlayerId: playerId,
				Username: globalData.UserName,
				TotalLaps: _targetLaps,
				TotalTime: totalTime,
				LapTimes: GetPlayerLapTimes(playerId).ToArray(),
				Checkpoints: GetPlayerCheckpointTimes(playerId)
			);

			// Add race entry to database
			globalData.RaceDb.AddRaceEntry(raceEntry);

			// Save updated database
			var saveResult = await dataStore.SetGlobalDataAsync(playerId, globalData);
			if (saveResult.IsSuccess)
			{
				// Check if this was a new best time
				float currentBestTime = globalData.RaceDb.GetBestRaceTime(_currentTrackId, _targetLaps);
				bool isNewBest = Mathf.Abs(currentBestTime - totalTime) < 0.001f;

				// Thread-safe logging via CallDeferred
				if (isNewBest)
				{
					CallDeferred(MethodName.LogHighScoreSaved, totalTime, $"{_currentTrackId}_{_targetLaps}laps");
				}
				else
				{
					CallDeferred(MethodName.LogRaceSaved, totalTime, _currentTrackId);
				}
			}
			else
			{
				// Thread-safe error logging via CallDeferred
				CallDeferred(MethodName.LogHighScoreError, saveResult.Error);
			}
		}
		catch (System.Exception ex)
		{
			// Thread-safe error logging via CallDeferred
			CallDeferred(MethodName.LogAsyncError, $"Exception saving high score: {ex.Message}");
		}
	}
	
	/// <summary>
	/// Thread-safe logging method for high score saves
	/// </summary>
	private void LogHighScoreSaved(float totalTime, string trackKey)
	{
		GD.Print($"[RacingGame] New best time saved: {totalTime:F3}s for {trackKey}");
	}
	
	/// <summary>
	/// Thread-safe logging method for high score errors
	/// </summary>
	private void LogHighScoreError(string error)
	{
		GD.PrintErr($"[RacingGame] Failed to save race data: {error}");
	}

	private void LogRaceSaved(float totalTime, string trackId)
	{
		GD.Print($"[RacingGame] Race completed and saved - Time: {totalTime:F3}s, Track: {trackId}");
	}

	private void LogPotentialBestLap(float lapTime, string trackId)
	{
		GD.Print($"[RacingGame] Potential best lap time - Time: {lapTime:F3}s, Track: {trackId} (will save on race completion)");
	}
	
	/// <summary>
	/// Thread-safe logging method for best lap saves
	/// </summary>
	private void LogBestLapSaved(float lapTime, string trackKey)
	{
		GD.Print($"[RacingGame] New best lap time saved: {lapTime:F3}s for {trackKey}");
	}
	
	/// <summary>
	/// Thread-safe logging method for async errors
	/// </summary>
	private void LogAsyncError(string message)
	{
		GD.PrintErr($"[RacingGame] {message}");
	}

	/// <summary>
	/// Save best lap time for racing game using direct DataStore integration
	/// </summary>
	private async Task SaveBestLapTime(string playerId, float lapTime)
	{
		if (string.IsNullOrEmpty(playerId) || lapTime <= 0.0f) return;
		
		var dataStore = DataStore.GetInstance();
		if (dataStore == null) return;
		
		// Fire-and-forget save with proper error handling
		try
		{
			var globalDataResult = await dataStore.GetGlobalDataAsync(playerId);
			if (!globalDataResult.IsSuccess) return;
			
			var globalData = globalDataResult.Value;
			
			// Note: Individual lap times are now saved as part of complete race entries
			// This method is called during races but actual persistence happens only on race completion
			// Check current best lap time for comparison
			float currentBestLapTime = globalData.RaceDb.GetBestLapTime(_currentTrackId);

			// Check if this is a new best lap time (0 means no time recorded)
			bool isNewBest = currentBestLapTime == 0f || currentBestLapTime > lapTime;

			if (isNewBest)
			{
				// Log the potential new best lap time (will be saved on race completion)
				CallDeferred(MethodName.LogPotentialBestLap, lapTime, _currentTrackId);
			}

			// No immediate save - lap times are persisted only when race completes
		}
		catch (System.Exception ex)
		{
			// Thread-safe error logging via CallDeferred
			CallDeferred(MethodName.LogAsyncError, $"Exception accessing lap time data: {ex.Message}");
		}
	}

	/// <summary>
	/// Handle user login - refresh UI to enable time trial button
	/// </summary>
	private void OnUserLoggedIn(string phoneNumber, string userName)
	{
		// Just update UI - no cache manipulation needed
		UpdateUI();
	}

	/// <summary>
	/// Handle user logout - refresh UI to disable time trial button
	/// </summary>
	private void OnUserLoggedOut(string phoneNumber)
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
			// Time trial cancelled - already in time trial mode
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
			// Time trial cancelled - user must be logged in
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
			
			if (_sessionManager == null || !IsInstanceValid(_sessionManager))
			{
				GD.PrintErr("Time trial cancelled - session system not available");
				_timingSystem?.StopRacing(); // Reset to idle state
				return;
			}
			
			var currentSession = _sessionManager.GetPrimaryUserSession();
			if (currentSession == null)
			{
				GD.PrintErr("Time trial cancelled - no active user session");
				_timingSystem?.StopRacing(); // Reset to idle state
				return;
			}
			
			bool creditsSpent = await _sessionManager.CheckAndSpendGlobalCreditsAsync(currentSession.PhoneNumber, TimeTrialCreditCost, "Time Trial Race");
			if (!creditsSpent)
			{
				// Credits not spent - don't start the race
				// Time trial cancelled - credits not spent
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

			// Start first lap for all players (same as EndCountdown does)
			foreach (var player in _players)
			{
				_timingSystem.StartPlayerLap(player.PlayerId);
			}
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
		ResetCheckpoints();          // Reset checkpoint tracking and visuals
		PositionCarAtStart();        // Reset position AND physics state (calls ResetCarState)
		_racingCar?.SetActive(true); // Reactivate racing car
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
	/// Initialize track ID mappings for stable data storage
	/// </summary>
	private void InitializeTrackIdMappings()
	{
		_trackIndexToIdMap.Clear();
		
		if (TrackScenes == null || TrackScenes.Count == 0) 
			return;

		// Generate stable track IDs for all tracks
		for (int i = 0; i < TrackScenes.Count; i++)
		{
			var trackScene = TrackScenes[i];
			if (trackScene?.ResourcePath != null)
			{
				var trackId = RaceDatabase.GenerateTrackId(trackScene);
				_trackIndexToIdMap[i] = trackId;
			}
		}
	}

	/// <summary>
	/// Initialize track system with track ID mapping
	/// </summary>
	private void InitializeTrackSystem()
	{
		// Initialize track ID mappings for all available tracks
		InitializeTrackIdMappings();
		
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
				
				// Generate track ID from track name for direct child tracks
				_currentTrackId = RaceDatabase.GenerateTrackIdFromName(trackDef.TrackName);
				_trackIndexToIdMap[0] = _currentTrackId;
				
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
			
			// Update current track ID
			_currentTrackIndex = trackIndex;
			_currentTrackId = _trackIndexToIdMap.GetValueOrDefault(trackIndex, "unknown_track");
			
			// Position track at screen center like original working implementation
			var viewport = GetViewport();
			if (viewport != null)
			{
				var screenSize = viewport.GetVisibleRect().Size;
				Vector2 screenCenter = screenSize / 2;
				_trackDefinition.GlobalPosition = screenCenter;
				// Track positioned at screen center
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
		if (_trackDefinition == null || _racingCar?.IsInitialized != true) 
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
			_racingCar.PositionCar(screenCenter);
			return; // Early return since we positioned car at center
		}
		
		var carPosition = startPoint - startLineDirection * 50;
		var carRotation = startLineDirection.Angle() + Mathf.Pi / 2;
		_racingCar.PositionCar(carPosition, carRotation);
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
		if (!IsInstanceValid(_racingCar) || !_racingCar.IsInitialized) 
			return;

		if (body != _racingCar.GetCarBody()) 
			return;

		BasePlayer player = _racingCar.GetPlayer();
		if (player == null) 
			return;

		string playerId = GetCurrentGamePlayerId();
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
		if (!IsInstanceValid(_racingCar) || !_racingCar.IsInitialized) 
			return;
		
		if (body != _racingCar.GetCarBody()) 
			return;
		
		BasePlayer player = _racingCar.GetPlayer();
		if (player == null) 
			return;

		string playerId = GetCurrentGamePlayerId();
		
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
			ResetCheckpoints();
			// Restore practice state
			_timingSystem?.StartPracticeMode();
		}
	}

	private void OnCheckpointTriggered(Node2D body, int checkpointIndex)
	{
		if (!IsInstanceValid(_racingCar) || !_racingCar.IsInitialized) 
			return;

		if (body != _racingCar.GetCarBody()) 
			return;

		BasePlayer player = _racingCar.GetPlayer();
		if (player == null) 
			return;

		string playerId = GetCurrentGamePlayerId();
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
			// Always properly end any active game before restarting
			if (_isGameActive)
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
		_uiManager.TracksLeaderboardRequested += OnTracksLeaderboardRequested;
		_uiManager.TrackLoadFromOverlayRequested += OnTrackLoadFromOverlayRequested;
		_uiManager.AddCreditsRequested += OnAddCreditsRequested;
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
		// Use base class method which includes confirmation dialog
		ReturnToMainMenu();
	}

	private void OnEndToPracticeModeRequested()
	{
		// Reset state and start practice
		_timingSystem?.StopRacing(); // Reset to idle
		StartPractice();
	}

	private async void OnRaceAgainRequested()
	{
		// Always require login first, regardless of development mode
		if (_cachedUserManager == null || !IsInstanceValid(_cachedUserManager))
		{
			GD.PrintErr("Race again cancelled - user management system not available");
			return;
		}

		bool isLoggedIn = _cachedUserManager.IsUserLoggedIn();
		if (!isLoggedIn)
		{
			GD.PrintErr("Race again cancelled - user must be logged in");
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

			if (_sessionManager == null || !IsInstanceValid(_sessionManager))
			{
				GD.PrintErr("Race again cancelled - session system not available");
				_timingSystem?.StopRacing(); // Reset to idle state
				return;
			}

			var currentSession = _sessionManager.GetPrimaryUserSession();
			if (currentSession == null)
			{
				GD.PrintErr("Race again cancelled - no active user session");
				_timingSystem?.StopRacing(); // Reset to idle state
				return;
			}

			bool creditsSpent = await _sessionManager.CheckAndSpendGlobalCreditsAsync(currentSession.PhoneNumber, TimeTrialCreditCost, "Race Again");
			if (!creditsSpent)
			{
				// Credits not spent - don't start the race
				GD.PrintErr("Race again cancelled - insufficient credits");
				_timingSystem?.StopRacing(); // Reset to idle state
				return;
			}
		}

		// Reset state and start new time trial
		_timingSystem?.StopRacing(); // Reset to idle
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

			// Start first lap for all players (same as EndCountdown does)
			foreach (var player in _players)
			{
				_timingSystem.StartPlayerLap(player.PlayerId);
			}
		}
	}

	/// <summary>
	/// Handle add credits request from race complete overlay
	/// </summary>
	private void OnAddCreditsRequested()
	{
		// Reset idle timer when accessing premium features
		if (_cachedUserManager != null && IsInstanceValid(_cachedUserManager))
		{
			_cachedUserManager.ResetUserIdleTimer();
		}

		// Get primary user for single-user racing context
		var currentSession = _sessionManager?.GetPrimaryUserSession();
		if (currentSession == null)
		{
			GD.PrintErr("[RacingGame] No active user session for credit purchase");
			return;
		}

		// Show credit purchase modal through UIManager for primary user
		var uiManager = UIManager.GetInstance();
		if (uiManager != null && IsInstanceValid(uiManager))
		{
			uiManager.ShowBuyCreditsModal(currentSession.PhoneNumber);
		}
		else
		{
			GD.PrintErr("[RacingGame] UIManager not available for credit purchase");
		}
	}

	/// <summary>
	/// Handle credits acquired event from credit purchase modal
	/// </summary>
	private void OnCreditsAcquired(string userId, int amount)
	{
		// Update UI manager to reflect new credit balance
		_uiManager?.OnCreditsAcquired();
	}

	/// <summary>
	/// Handle tracks leaderboard request from UI manager
	/// </summary>
	private void OnTracksLeaderboardRequested()
	{
		// Use the actual phone number from session as player ID for leaderboard
		var playerId = GetCurrentPlayerPhoneNumber();
		var trackMetadata = GatherTrackMetadata();

		_uiManager?.UpdateTrackMetadataWithLeaderboard(trackMetadata, _currentTrackId, playerId);
	}

	/// <summary>
	/// Handle track load request from tracks & leaderboard overlay
	/// </summary>
	/// <param name="trackId">Track ID to load</param>
	private void OnTrackLoadFromOverlayRequested(string trackId)
	{
		// Convert track ID to index using the mapping
		var trackIndex = -1;
		foreach (var kvp in _trackIndexToIdMap)
		{
			if (kvp.Value == trackId)
			{
				trackIndex = kvp.Key;
				break;
			}
		}
		
		if (trackIndex >= 0)
		{
			// Use existing track switching logic
			OnTrackSwitchRequested(trackIndex);
		}
	}

	/// <summary>
	/// Initialize tracks & leaderboard system after UI setup
	/// </summary>
	private void InitializeTracksLeaderboardSystem()
	{
		if (_uiManager == null) return;
		
		var trackMetadata = GatherTrackMetadata();
		_uiManager.InitializeTracksLeaderboard(trackMetadata, _currentTrackId);
	}

	/// <summary>
	/// Show high score display (NEW PATTERN: Global high scores)
	/// </summary>
	public void ShowHighScores()
	{
		_timingSystem?.SetHighScoreDisplay();
		
		// Show global high scores from SessionManager
		ShowGlobalHighScores();
	}

	/// <summary>
	/// Show global high scores using direct DataStore integration
	/// </summary>
	private async void ShowGlobalHighScores()
	{
		// Use the actual phone number from session as player ID for high scores
		var playerId = GetCurrentPlayerPhoneNumber();
		
		var dataStore = DataStore.GetInstance();
		if (dataStore == null)
		{
			ShowHighScoresFallback();
			return;
		}
		
		try
		{
			// Get global data to extract racing scores
			var globalDataResult = await dataStore.GetGlobalDataAsync(playerId);
			
			// Check if object is still valid after await
			if (!IsInstanceValid(this))
				return;
				
			if (!globalDataResult.IsSuccess)
			{
				ShowHighScoresFallback();
				return;
			}
			
			// Build high score display from global data
			var highScores = new System.Text.StringBuilder("🏁 RACING HIGH SCORES 🏁\n\n");
			
			// Get racing best times from typed properties
			var racingData = globalDataResult.Value.RaceDb;
			var allBestTimes = new List<(string track, float time, string type, string username)>();

			// Add best times from all tracks
			var tracksWithRaces = racingData.GetTracksWithRaces();
			foreach (var trackId in tracksWithRaces)
			{
				// Add best lap time for this track
				var bestLapEntry = racingData.GetBestLapTimeEntry(trackId);
				if (bestLapEntry != null && bestLapEntry.LapTimes.Any(lap => lap > 0f))
				{
					var bestLapTime = bestLapEntry.LapTimes.Where(lap => lap > 0f).Min();
					allBestTimes.Add((trackId, bestLapTime, "Best Lap", bestLapEntry.Username));
				}

				// Add best race times for different lap counts
				for (int laps = 1; laps <= 10; laps++)
				{
					var bestRaceEntry = racingData.GetBestRaceTimeEntry(trackId, laps);
					if (bestRaceEntry != null && bestRaceEntry.TotalTime > 0f)
					{
						allBestTimes.Add((trackId, bestRaceEntry.TotalTime, $"Best Race ({laps} laps)", bestRaceEntry.Username));
					}
				}
			}
			
			var racingScores = allBestTimes
				.OrderBy(x => x.time) // Lower times are better
				.Take(10); // Top 10
			
			if (racingScores.Any())
			{
				foreach (var score in racingScores)
				{
					var username = string.IsNullOrEmpty(score.username) ? "Unknown" : score.username;
					highScores.AppendLine($"{score.track} - {score.type}: {username} - {score.time:F3}s");
				}
				
				// Add total races completed
				if (racingData.TotalRacesCompleted > 0)
				{
					highScores.AppendLine($"\nTotal Races Completed: {racingData.TotalRacesCompleted}");
				}
			}
			else
			{
				highScores.AppendLine("No high scores yet!");
				highScores.AppendLine("Complete some races to set records.");
			}
			
			GD.Print(highScores.ToString());
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"[RacingGame] Exception showing high scores: {ex.Message}");
			ShowHighScoresFallback();
		}
		
		// Return to previous state after brief delay
		CreateAutoCleanupTimer(5.0f, () => {
			if (GetGameMode() == GameMode.Practice)
				_timingSystem?.StartPracticeMode();
			else
				_timingSystem?.StopRacing();
		});
	}

	private void ShowHighScoresFallback()
	{
		GD.Print("🏁 RACING HIGH SCORES 🏁\n\nHigh scores feature not available - DataStore service unavailable.");
		
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

	/// <summary>
	/// Override ReturnToMainMenu to pause active time trial races during confirmation
	/// </summary>
	protected override async void ReturnToMainMenu()
	{
		// Check if we should pause the game during confirmation
		bool shouldPauseForConfirmation = IsGameActive() &&
		                                  GetGameMode() == GameMode.TimeTrial &&
		                                  !IsGamePaused();

		// Pause the game if time trial is active to prevent race interference
		if (shouldPauseForConfirmation)
		{
			PauseGame();
		}

		try
		{
			// Get UIManager for confirmation dialog
			var uiManager = UIManager.GetInstance();
			if (uiManager != null)
			{
				// Show confirmation dialog
				bool confirmed = await uiManager.ShowConfirmationAsync(
					"Return to Menu",
					"Are you sure you want to return to the main menu?\n\nYour current race progress will be lost.",
					"Return to Menu",
					"Cancel"
				);

				if (!confirmed)
				{
					// User cancelled - resume the game if we paused it
					if (shouldPauseForConfirmation)
					{
						ResumeGame();
					}
					return;
				}
			}

			// User confirmed or no UIManager available - call base implementation
			// (can't call base async method directly, so replicate the logic)
			var gameHost = GameHost.GetInstance();
			if (gameHost != null && GodotObject.IsInstanceValid(gameHost))
			{
				gameHost.ReturnToMainMenu();
			}
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"[RacingGame] Error in ReturnToMainMenu: {ex.Message}");

			// Resume game on error to prevent being stuck in paused state
			if (shouldPauseForConfirmation)
			{
				ResumeGame();
			}
		}
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
	/// Gather track metadata for UI updates
	/// </summary>
	private Dictionary<string, RacingUIManager.TrackMetadata> GatherTrackMetadata()
	{
		var metadata = new Dictionary<string, RacingUIManager.TrackMetadata>();
		
		if (TrackScenes == null || TrackScenes.Count == 0)
			return metadata;

		// Build basic metadata from track scenes
		for (int i = 0; i < TrackScenes.Count; i++)
		{
			var trackScene = TrackScenes[i];
			if (trackScene?.ResourcePath == null)
				continue;
				
			var trackId = _trackIndexToIdMap.GetValueOrDefault(i, RaceDatabase.GenerateTrackId(trackScene));
			var trackName = GetTrackName(trackScene, i);
			var defaultLaps = GetTrackDefaultLaps(trackScene);
			
			metadata[trackId] = new RacingUIManager.TrackMetadata
			{
				TrackId = trackId,
				TrackName = trackName,
				DefaultLaps = defaultLaps,
				Scene = trackScene,
				Index = i,
				IsCurrentTrack = trackId == _currentTrackId
			};
		}
		
		return metadata;
	}

	/// <summary>
	/// Get track name from scene or use fallback
	/// </summary>
	private string GetTrackName(PackedScene trackScene, int index)
	{
		try
		{
			var tempInstance = trackScene.Instantiate();
			if (tempInstance is RacingTrackDefinition trackDef)
			{
				var name = trackDef.TrackName;
				tempInstance.QueueFree();
				return !string.IsNullOrEmpty(name) ? name : $"Track {index + 1}";
			}
			tempInstance.QueueFree();
		}
		catch (System.Exception)
		{
			// Fallback if instantiation fails
		}
		
		return $"Track {index + 1}";
	}

	/// <summary>
	/// Get track default laps from scene or use fallback
	/// </summary>
	private int GetTrackDefaultLaps(PackedScene trackScene)
	{
		try
		{
			var tempInstance = trackScene.Instantiate();
			if (tempInstance is RacingTrackDefinition trackDef)
			{
				var laps = trackDef.NumberOfLaps;
				tempInstance.QueueFree();
				return laps > 0 ? laps : 3;
			}
			tempInstance.QueueFree();
		}
		catch (System.Exception)
		{
			// Fallback if instantiation fails
		}
		
		return 3; // Default laps
	}

	/// <summary>
	/// Gather current game state for UI updates
	/// </summary>
	private RacingUIState GatherCurrentState()
	{
		// Use consistent player ID throughout - phone number when logged in
		var playerId = GetCurrentGamePlayerId();
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

		// Enable replay button if user is logged in - actual credit check happens in OnRaceAgainRequested
		bool canAffordReplay = loggedIn;
		
		// Check if tracks & leaderboard can be shown (not during races or countdown)
		bool canShowTracksLeaderboard = !isTimeTrialInProgress &&
			currentRacingState != RacingTimingSystem.RacingState.WaitingForCredits &&
			currentRacingState != RacingTimingSystem.RacingState.TrackLoading &&
			currentRacingState != RacingTimingSystem.RacingState.GameOverDeciding &&
			!IsInCountdown();
		
		// Calculate lap progress for arc display
		float lapProgress = CalculateLapProgress(playerId);

		// Get countdown state for arc animation
		int countdownNumber = GetCountdownNumber();
		float countdownProgress = CalculateCountdownProgress();

		return new RacingUIState
		{
			IsGamePaused = IsGamePaused(),
			IsInCountdown = IsInCountdown(),
			IsTimeTrialInProgress = isTimeTrialInProgress,
			CanStartTimeTrial = canStartTimeTrial,
			GameMode = gameMode,
			CarSpeed = _racingCar?.GetCarSpeed() ?? 0f,
			CurrentLap = GetPlayerCurrentLap(playerId),
			TargetLaps = TargetLaps,
			TimeDisplay = gameMode == GameMode.Practice ?
				GetPlayerGapTime(playerId) : GetPlayerCurrentLapTime(playerId),
			TimeLabel = gameMode == GameMode.Practice ? "Gap" : "Time",

			// Arc HUD specific data
			MaxSpeed = _racingCar?.MaxSpeed ?? 1800.0f,
			LapProgress = lapProgress,
			CountdownNumber = countdownNumber,
			CountdownProgress = countdownProgress,

			IsUserLoggedIn = loggedIn,
			ShowGameOverOverlay = currentRacingState == RacingTimingSystem.RacingState.GameOverDeciding,
			FinalTime = GetPlayerScore(playerId),
			CanAffordReplay = canAffordReplay,

			// Tracks & Leaderboard overlay state
			ShowTracksLeaderboardOverlay = _uiManager?.IsTracksLeaderboardVisible ?? false,
			CanShowTracksLeaderboard = canShowTracksLeaderboard,
			TrackMetadata = GatherTrackMetadata(),
			CurrentTrackId = _currentTrackId
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
		
		_racingCar.SetActive(false);
	}


	// ================================================================
	// PLAYER ID MANAGEMENT - SIMPLIFIED
	// ================================================================

	/// <summary>
	/// Get the current player's phone number from SessionManager (null if not logged in)
	/// </summary>
	private string GetCurrentPlayerPhoneNumber()
	{
		try
		{
			var sessionManager = SessionManager.GetInstance();
			if (sessionManager != null && GodotObject.IsInstanceValid(sessionManager))
			{
				var currentSession = sessionManager.GetPrimaryUserSession();
				if (currentSession != null)
				{
					return currentSession.PhoneNumber;
				}
			}
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"[RacingGame] Error getting current player phone number: {ex.Message}");
		}

		// Return null if no user is logged in - no fallbacks
		return null;
	}

	/// <summary>
	/// Get the current player ID for all game operations
	/// Uses phone number when logged in, guest fallback otherwise
	/// SINGLE SOURCE OF TRUTH for player identification
	/// </summary>
	private string GetCurrentGamePlayerId()
	{
		// First try to get from the racing car (already initialized with phone number)
		var player = _racingCar?.GetPlayer();
		if (player != null && !string.IsNullOrEmpty(player.PlayerId) && player.PlayerId != "player1")
			return player.PlayerId;

		// Fallback to phone number lookup
		var phoneNumber = GetCurrentPlayerPhoneNumber();
		if (!string.IsNullOrEmpty(phoneNumber))
			return phoneNumber;

		// Guest player fallback
		return "guest_player";
	}

	// ================================================================
	// RACING DATA GETTERS
	// ================================================================

	public int GetPlayerCurrentLap(string playerId) => IsInstanceValid(_timingSystem) && !string.IsNullOrEmpty(playerId) ? _timingSystem.GetPlayerCurrentLap(playerId) : 0;
	public float GetPlayerCurrentLapTime(string playerId) => IsInstanceValid(_timingSystem) && !string.IsNullOrEmpty(playerId) ? _timingSystem.GetPlayerCurrentLapTime(playerId) : 0.0f;
	public float GetPlayerBestLapTime(string playerId) => IsInstanceValid(_timingSystem) && !string.IsNullOrEmpty(playerId) ? _timingSystem.GetPlayerBestLapTime(playerId) : 0.0f;
	public float GetPlayerGapTime(string playerId) => IsInstanceValid(_timingSystem) && !string.IsNullOrEmpty(playerId) ? _timingSystem.GetPlayerGapTime(playerId) : 0.0f;
	public List<float> GetPlayerLapTimes(string playerId) => IsInstanceValid(_timingSystem) && !string.IsNullOrEmpty(playerId) ? _timingSystem.GetPlayerLapTimes(playerId) : new List<float>();

	/// <summary>
	/// Get checkpoint times for a player as an array for race entry creation
	/// </summary>
	private CheckpointTime[] GetPlayerCheckpointTimes(string playerId)
	{
		if (IsInstanceValid(_timingSystem) && !string.IsNullOrEmpty(playerId))
		{
			return _timingSystem.GetPlayerCheckpointTimes(playerId).ToArray();
		}
		return Array.Empty<CheckpointTime>();
	}
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

		if (IsInstanceValid(_racingCar))
		{
			_racingCar.CarMoved -= OnCarMoved;
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

	// ================================================================
	// ARC HUD HELPER METHODS
	// ================================================================

	/// <summary>
	/// Calculate lap progress for arc display (0.0 to 1.0)
	/// </summary>
	private float CalculateLapProgress(string playerId)
	{
		if (!IsInstanceValid(_timingSystem))
			return 0.0f;

		// For practice mode, use gap time as a rough progress indicator
		if (GetGameMode() == GameMode.Practice)
		{
			var gapTime = GetPlayerGapTime(playerId);
			// Convert gap time to progress (lower gap = more progress)
			// This is approximate - ideally we'd track checkpoint progress
			return gapTime > 0 ? Mathf.Clamp(1.0f - (gapTime / 60.0f), 0.0f, 1.0f) : 0.0f;
		}

		// For time trial, calculate progress based on checkpoints
		if (_checkpointTriggers != null && _checkpointTriggers.Length > 0)
		{
			return Mathf.Clamp((float)_nextCheckpointIndex / _checkpointTriggers.Length, 0.0f, 1.0f);
		}

		// Fallback: use current lap time as progress estimate
		var currentLapTime = GetPlayerCurrentLapTime(playerId);
		var bestLapTime = GetPlayerBestLapTime(playerId);

		if (bestLapTime > 0)
		{
			return Mathf.Clamp(currentLapTime / bestLapTime, 0.0f, 1.0f);
		}

		// Final fallback: estimate based on time (assume ~30s lap)
		return Mathf.Clamp(currentLapTime / 30.0f, 0.0f, 1.0f);
	}

	/// <summary>
	/// Calculate countdown progress for arc animation (0.0 to 1.0)
	/// </summary>
	private float CalculateCountdownProgress()
	{
		if (!IsInstanceValid(_timingSystem) || !_timingSystem.IsInCountdown)
			return 0.0f;

		// Estimate progress based on countdown number
		var countdownNumber = _timingSystem.CountdownNumber;
		var totalCountdownNumbers = 4; // 3, 2, 1, GO

		// Convert countdown number to progress (3->0.25, 2->0.5, 1->0.75, 0->1.0)
		if (countdownNumber > 0)
		{
			return Mathf.Clamp((float)(totalCountdownNumbers - countdownNumber) / totalCountdownNumbers, 0.0f, 1.0f);
		}
		else
		{
			return 1.0f; // GO state
		}
	}
}