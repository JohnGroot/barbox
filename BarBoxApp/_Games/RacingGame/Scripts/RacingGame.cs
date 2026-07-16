using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BarBox.Core.Autoloads;
using BarBox.Core.Debug;
using BarBox.Core.Utils;
using Godot;

namespace BarBox.Games.Racing;

public enum RacingMode
{
	Practice,
	TimeTrial,
}

public class RacingUIState
{
	public bool IsGamePaused { get; set; }

	public bool IsInCountdown { get; set; }

	public bool IsTimeTrialInProgress { get; set; } // Tracks formal race vs practice

	public bool CanStartTimeTrial { get; set; }

	public RacingMode GameMode { get; set; }

	public float CarSpeed { get; set; }

	public int CurrentLap { get; set; }

	public int TargetLaps { get; set; }

	public float TimeDisplay { get; set; }

	public string TimeLabel { get; set; }

	public float MaxSpeed { get; set; } = 1800.0f;

	public float LapProgress { get; set; } = 0.0f; // 0.0 to 1.0 for current lap completion

	public int CountdownNumber { get; set; } = 0; // 3, 2, 1, or 0 for GO

	public float CountdownProgress { get; set; } = 0.0f; // 0.0 to 1.0 for arc animation

	public bool IsUserLoggedIn { get; set; }

	public bool ShowGameOverOverlay { get; set; }

	public float FinalTime { get; set; }

	public bool CanAffordReplay { get; set; }

	public bool ShowTracksLeaderboardOverlay { get; set; }

	public bool CanShowTracksLeaderboard { get; set; }

	public Dictionary<string, RacingUIManager.TrackMetadata> TrackMetadata { get; set; } = new();

	public string CurrentTrackId { get; set; } = string.Empty;

	/// <summary>
	/// Compare this state with another for equality.
	/// Used to avoid unnecessary UI updates when state hasn't changed.
	/// </summary>
	public bool StateEquals(RacingUIState other)
	{
		if (other == null)
		{
			return false;
		}

		return IsGamePaused == other.IsGamePaused
			&& IsInCountdown == other.IsInCountdown
			&& IsTimeTrialInProgress == other.IsTimeTrialInProgress
			&& CanStartTimeTrial == other.CanStartTimeTrial
			&& GameMode == other.GameMode
			&& Math.Abs(CarSpeed - other.CarSpeed) < 0.01f
			&& CurrentLap == other.CurrentLap
			&& TargetLaps == other.TargetLaps
			&& Math.Abs(TimeDisplay - other.TimeDisplay) < 0.001f
			&& TimeLabel == other.TimeLabel
			&& Math.Abs(MaxSpeed - other.MaxSpeed) < 0.01f
			&& Math.Abs(LapProgress - other.LapProgress) < 0.001f
			&& CountdownNumber == other.CountdownNumber
			&& Math.Abs(CountdownProgress - other.CountdownProgress) < 0.001f
			&& IsUserLoggedIn == other.IsUserLoggedIn
			&& ShowGameOverOverlay == other.ShowGameOverOverlay
			&& Math.Abs(FinalTime - other.FinalTime) < 0.001f
			&& CanAffordReplay == other.CanAffordReplay
			&& ShowTracksLeaderboardOverlay == other.ShowTracksLeaderboardOverlay
			&& CanShowTracksLeaderboard == other.CanShowTracksLeaderboard
			&& CurrentTrackId == other.CurrentTrackId;

		// Note: TrackMetadata dictionary is cached separately, no need to compare
	}
}

/// <summary>
/// 2D time trial racing game with comprehensive racing mechanics, track system, and UI
/// </summary>
[GlobalClass]
public partial class RacingGame : GameController
{
	protected override string GetGameId() => "racing";

	#region Signals

	[Signal]
	public delegate void RaceStartedEventHandler();

	[Signal]
	public delegate void RaceEndedEventHandler();

	[Signal]
	public delegate void RacePausedEventHandler();

	[Signal]
	public delegate void RaceResumedEventHandler();

	[Signal]
	public delegate void LapCompletedEventHandler(string playerId, int lapNumber, float lapTime);

	[Signal]
	public delegate void RaceCompletedEventHandler(string playerId, float totalTime);

	[Signal]
	public delegate void CheckpointCrossedEventHandler(string playerId, int checkpointIndex, float gapTime);

	#endregion

	#region Exports

	[ExportCategory("Racing Settings")]
	[Export]
	public int TargetLaps
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

	[Export]
	public bool ShowCountdown { get; set; } = true;

	[Export]
	public float CountdownDuration { get; set; } = 4.0f; // 3-2-1-GO = 4 seconds

	[Export]
	public int TimeTrialCreditCost { get; set; } = 1;

	[ExportCategory("Track Settings")]
	[Export]
	public Godot.Collections.Array<PackedScene> TrackScenes { get; set; } = new Godot.Collections.Array<PackedScene>();

	/// <summary>
	/// Override to only prevent logout during active time trial races, not during practice mode
	/// </summary>
	public override bool CanLogout
	{
		get
		{
			if (GetRacingMode() == RacingMode.Practice)
			{
				return true;
			}

			// Check if a time trial is actively in progress (not just idle/menu states)
			var currentRacingState = _timingSystem?.CurrentRacingState ?? RacingTimingSystem.RacingState.Idle;
			bool isTimeTrialInProgress = GetRacingMode() == RacingMode.TimeTrial &&
				currentRacingState != RacingTimingSystem.RacingState.Idle &&
				currentRacingState != RacingTimingSystem.RacingState.PracticeMode &&
				currentRacingState != RacingTimingSystem.RacingState.GameOverDeciding;

			return !isTimeTrialInProgress;
		}
	}

	#endregion

	#region Constants

	// Total height reserved for TopMenuBar (100px) + ContextButtonBar (50px)
	private const float TOP_MENU_HEIGHT = 150.0f;

	#endregion

	#region Fields

	private PlayerManagementComponent _playerMgmt;

	private RacingTimingSystem _timingSystem;

	private RacingTrackValidationSystem _trackValidationSystem;

	// Zone manager for track zones (boost, slowdown, frictionless, kerbs)
	private RacingZoneManager _zoneManager;

	private RacingMode _currentGameMode = RacingMode.Practice;

	private RacingCameraController _cameraController;

	private RacingCar _racingCar;

	private RacingEventService _racingEventService;
	private SessionEventService _eventService;

	// Lap-boundary backend saves are queued here so their async chain doesn't
	// run synchronously on the lap-crossing frame; flushed every frame, at
	// race completion, and on teardown (see RacingPendingSaveQueue).
	private readonly RacingPendingSaveQueue _pendingSaveQueue = new();

	private RacingTrackDefinition _trackDefinition;
	private Curve2D _trackCurve;
	private RacingCheckpointTrigger[] _checkpointTriggers;
	private bool[] _checkpointsCrossed;
	private int _nextCheckpointIndex = 0;
	private int _currentTrackIndex = 0;

	// Track ID management for stable data storage
	private Dictionary<int, string> _trackIndexToIdMap = new();
	private string _currentTrackId = "unknown_track";

	// Cached track metadata to avoid scene instantiation every frame
	private Dictionary<string, RacingUIManager.TrackMetadata> _cachedTrackMetadata;

	// Double-buffered UI state (GC optimization: avoids per-frame RacingUIState allocation)
	private RacingUIState _uiStateA = new();
	private RacingUIState _uiStateB = new();
	private bool _useStateA = true;
	private bool _forceNextUIUpdate = true;

	// UI update throttle (GC optimization: skip UI gathering on most frames)
	private int _uiFrameCounter = 0;
	private const int UI_UPDATE_INTERVAL = 3;

	// Debug performance timing (only allocated in debug builds)
	private System.Diagnostics.Stopwatch _debugValidationStopwatch;
	private static readonly bool _isDebugBuild = OS.IsDebugBuild();

	// Debug metric throttle (GC optimization: skip SetMetric string interpolation on most frames)
	private int _debugMetricFrameCounter = 0;
	private const int DEBUG_METRIC_UPDATE_INTERVAL = 3;

	private RacingUIManager _uiManager;

	private SessionManager _sessionManager;
	private CreditService _creditService;

	// Visual feedback renderer for tire trails, input lines, and mouse indicators
	private RacingVisualFeedbackRenderer _visualRenderer;

	private Vector2 _directTargetPosition = Vector2.Zero;
	private bool _hasDirectInput = false;

	#endregion

	#region Initialization

	/// <summary>
	/// Platform property is already populated when this is called.
	/// </summary>
	protected override void OnDiscoverServices()
	{
		_eventService = Platform.Events;
		_sessionManager = Platform.Session;
		_creditService = Platform.Credits;
	}

	protected override void OnInitializeComponents()
	{
		SetRacingMode(RacingMode.Practice);

		_playerMgmt = new PlayerManagementComponent();
		AddChild(_playerMgmt);

		_racingEventService = new RacingEventService(_eventService);

		SetupTimingSystem();
		SetupTrackValidationSystem();
		SetupCameraController();
		SetupCarController();

		SetupRacingUI();

		InitializeTrackSystem();

		InitializeTracksLeaderboardSystem();

		UpdateUI();
	}

	/// <summary>
	/// Game-specific cleanup on exit.
	/// Called during _ExitTree after UI cleanup and user signal disconnection.
	/// </summary>
	protected override void OnGameTeardown()
	{
		// Critical: flush any lap-boundary saves that haven't fired yet.
		// Without this, a player who quits between the lap-crossing frame and
		// the next _Process flush would silently lose that lap's save.
		_pendingSaveQueue.Flush();

		// Save partial race data when exiting (fire-and-forget)
		if (IsRaceActive() && GetRacingMode() == RacingMode.TimeTrial)
		{
			var playerId = GetCurrentPlayerPhoneNumber();
			if (playerId != null)
			{
				_ = SavePartialRaceData(playerId, "menu_exit");
			}
		}

		if (IsRaceActive() && GetRacingMode() == RacingMode.TimeTrial)
		{
			_ = SavePartialRaceData(GetCurrentGamePlayerId(), "app_exit");
		}

		// Backend session close is handled automatically by the base class.
		DisconnectTrackSignals();

		if (IsInstanceValid(_timingSystem))
		{
			_timingSystem.LapCompleted -= OnTimingSystemLapCompleted;
			_timingSystem.RaceCompleted -= OnTimingSystemRaceCompleted;
			_timingSystem.CheckpointCrossed -= OnTimingSystemCheckpointCrossed;
		}

		DebugPerformanceMonitor.Instance?.ClearGameMetrics();
	}

	/// <summary>
	/// Start gameplay or show initial state.
	/// Called after all initialization (including async) is complete.
	/// </summary>
	protected override void OnActivateGame()
	{
		// Auto-start practice mode for immediate testing
		StartPractice();
	}

	private void SetupTimingSystem()
	{
		_timingSystem = new RacingTimingSystem();
		_timingSystem.TargetLaps = TargetLaps;
		_timingSystem.SetGameMode(_currentGameMode);
		AddChild(_timingSystem);

		_timingSystem.LapCompleted += OnTimingSystemLapCompleted;
		_timingSystem.RaceCompleted += OnTimingSystemRaceCompleted;
		_timingSystem.CheckpointCrossed += OnTimingSystemCheckpointCrossed;
	}

	private void SetupTrackValidationSystem()
	{
		// Create zone manager first (will be used by track validation system)
		_zoneManager = new RacingZoneManager();
		_zoneManager.Name = "RacingZoneManager";
		AddChild(_zoneManager);

		_trackValidationSystem = new RacingTrackValidationSystem();

		// Set default values (these would be configurable via exports on the system)
		_trackValidationSystem.CenterLineProximityRange = 40.0f;
		_trackValidationSystem.CenterLineAccelerationBonus = 1.5f;
		_trackValidationSystem.TrackProximityRange = 20.0f;
		_trackValidationSystem.OffTrackSpeedPenalty = 0.3f;
		_trackValidationSystem.OffTrackTurnPenalty = 0.3f;
		_trackValidationSystem.OffTrackAccelerationPenalty = 0.3f;
		_trackValidationSystem.OffTrackPenaltyLerpSpeed = 3.0f;

		_trackValidationSystem.SetZoneManager(_zoneManager);

		AddChild(_trackValidationSystem);
	}

	private void SetupCameraController()
	{
		_cameraController = new RacingCameraController();
		_cameraController.ScreenEdgeColliderThickness = 8.0f;
		AddChild(_cameraController);

		_cameraController.Initialize();
	}

	private void SetupCarController()
	{
		_racingCar = new RacingCar();

		_racingCar.MaxSpeed = 3300.0f;
		_racingCar.MinSpeed = 100.0f;
		_racingCar.MaxInputDistance = 2000.0f;
		_racingCar.AccelerationRate = 1500.0f;
		_racingCar.DecelerationRate = 600.0f;
		_racingCar.RotationLerpSpeed = 4.0f;
		_racingCar.CarSize = new Vector2(100, 180);

		AddChild(_racingCar);

		// Initialize with validated dependencies and phone number as player ID
		var phoneNumber = GetCurrentPlayerPhoneNumber() ?? "guest_player";
		_racingCar.Initialize(_cameraController, _trackValidationSystem, phoneNumber, "Player");

		// Set input enabled callback to decouple car from parent type
		_racingCar.InputEnabledCallback = IsInputEnabled;

		_playerMgmt.AddPlayer(_racingCar);

		SetupVisualRenderer();
	}

	private void SetupVisualRenderer()
	{
		_visualRenderer = new RacingVisualFeedbackRenderer();
		_visualRenderer.Initialize(_racingCar);
		AddChild(_visualRenderer);
	}

	private void SetRacingMode(RacingMode mode)
	{
		_currentGameMode = mode;

		// Timing system may be null during initial setup
		_timingSystem?.SetGameMode(mode);
	}

	private RacingMode GetRacingMode() => _currentGameMode;

	#endregion

	#region Direct Input

	/// <summary>
	/// Handle direct input polling for accurate arc positioning
	/// </summary>
	private void HandleDirectInput()
	{
		if (!IsRaceActive() || IsRacePaused() || !IsInputEnabled())
		{
			_hasDirectInput = false;
			return;
		}

		var inputManager = Platform.Input;
		if (inputManager == null)
		{
			_hasDirectInput = false;
			return;
		}

		_hasDirectInput = inputManager.IsTouchActive();

		if (_hasDirectInput)
		{
			// Raw screen position - direct 1:1 mapping
			_directTargetPosition = inputManager.GetTouchPosition();
		}
	}

	public bool IsInputEnabled()
	{
		return _timingSystem.IsInputEnabled();
	}

	#endregion

	#region Game Loop

	public override void _Process(double delta)
	{
		// Drain any lap-boundary saves queued last frame - keeps the async
		// chain off the exact frame that queued them (see _pendingSaveQueue).
		_pendingSaveQueue.Flush();

		HandleDirectInput();

		if (_timingSystem.IsInCountdown)
		{
			HandleCountdown((float)delta);
		}
		else if (IsRaceActive() && !IsRacePaused())
		{
			_timingSystem.UpdateRacingTimers((float)delta, _playerMgmt.GetPlayers());
		}

		// Update racing-specific systems (guaranteed valid after OnInitializeComponents)
		if (IsRaceActive() && !IsRacePaused())
		{
			if (_isDebugBuild)
			{
				_debugValidationStopwatch ??= new System.Diagnostics.Stopwatch();
				_debugValidationStopwatch.Restart();
			}

			_trackValidationSystem.UpdateOffTrackPenalties(_racingCar.GetCarPosition(), _racingCar.GetCarBody().Rotation, _racingCar.CarSize, (float)delta);

			if (_isDebugBuild)
			{
				_debugValidationStopwatch.Stop();
				if (++_debugMetricFrameCounter >= DEBUG_METRIC_UPDATE_INTERVAL)
				{
					_debugMetricFrameCounter = 0;
					var monitor = DebugPerformanceMonitor.Instance;
					if (monitor != null)
					{
						monitor.SetMetric("Validation", $"{_debugValidationStopwatch.Elapsed.TotalMilliseconds:F2}ms");
						monitor.SetMetric("Off-track", _trackValidationSystem.IsCurrentlyOffTrack ? "YES" : "no");
						monitor.SetMetric("Zones", $"{_zoneManager.ZoneCount}");
					}
				}
			}
		}

		_zoneManager.UpdateFrictionlessEffects((float)delta);
		_visualRenderer.ShouldRender = IsRaceActive() && !IsRacePaused();
		_visualRenderer.UpdateVisualFeedback((float)delta);
		_visualRenderer.UpdateTireTrails();

		// Throttle UI updates to every 3rd frame (GC optimization)
		if (++_uiFrameCounter >= UI_UPDATE_INTERVAL)
		{
			_uiFrameCounter = 0;
			UpdateUI();
		}
	}

	#endregion

	#region Signal Handlers

	private void OnTimingSystemLapCompleted(string playerId, int lapNumber, float lapTime)
	{
		// Save best lap time only if user is logged in (uses phone number as real ID)
		var currentPhoneNumber = GetCurrentPlayerPhoneNumber();
		if (currentPhoneNumber != null)
		{
			// Capture lap state IMMEDIATELY (cheap - no I/O) so the queued save
			// still reflects this lap once flushed, since currentLap/checkpoints
			// change again as soon as the next lap starts. The actual backend
			// call (JSON serialize, retry/backoff) is deferred to a later flush
			// so it doesn't run synchronously on this lap-crossing frame.
			var currentLap = GetPlayerCurrentLap(currentPhoneNumber);
			var checkpoints = GetPlayerCheckpointTimes(currentPhoneNumber);
			_pendingSaveQueue.Enqueue(() => SaveBestLapTime(currentPhoneNumber, lapTime, currentLap, checkpoints));
		}

		// Emit signal for external integrations (GameHost) at high level
		EmitSignal(SignalName.LapCompleted, playerId, lapNumber, lapTime);
	}

	private void OnTimingSystemRaceCompleted(string playerId, float totalTime)
	{
		// Race completion is a natural, already-synchronous flush point - make
		// sure the final lap's queued save (just enqueued by the LapCompleted
		// signal that preceded this one) starts now rather than waiting for
		// next frame's drain.
		_pendingSaveQueue.Flush();

		// CRITICAL: Capture lap data IMMEDIATELY before async operation
		// This prevents race condition where ResetRacingData() clears lap times
		// before the async SaveGlobalHighScore completes
		var lapTimes = GetPlayerLapTimes(playerId).ToArray();
		var checkpoints = GetPlayerCheckpointTimes(playerId);

		// Save global high score only if user is logged in (uses phone number as real ID)
		var currentPhoneNumber = GetCurrentPlayerPhoneNumber();
		if (currentPhoneNumber != null)
		{
			// Intentionally NOT routed through _pendingSaveQueue: this fires once
			// per race (not once per lap), and race completion is already the
			// natural, acceptable synchronous flush point the queue defers to
			// above - there's no repeated hot-path hitch here to decouple.
			// Pass captured data to async method to prevent race condition
			_ = SaveGlobalHighScore(currentPhoneNumber, totalTime, lapTimes, checkpoints);
		}

		EndRace();

		// Emit signal for external integrations (GameHost) at high level
		EmitSignal(SignalName.RaceCompleted, playerId, totalTime);
	}

	private void OnTimingSystemCheckpointCrossed(string playerId, int checkpointIndex, float gapTime)
	{
		// Emit signal for external integrations (GameHost) at high level
		EmitSignal(SignalName.CheckpointCrossed, playerId, checkpointIndex, gapTime);
	}

	protected override HelpContentData GetHelpContent()
	{
		return new HelpContentData("RACING HOW-TO")
			.AddSection(
				"🏁 BUCKLE UP 🚘",
				"• Touch and drag to steer your car around the track",
				"• The car will always move towards your finger's position",
				"• The closer your finger is to the car the slower it will go",
				"• Complete laps as fast as possible by crossing all checkpoints in order",
				"• If you go off the track you'll get Slowed Down",
				"• Practice mode runs continuously - Time Trial races against the clock!")

			.AddSection(
				"🏎️ GAME MODES 🏎️",
				"• Practice Mode - FREE continuous racing to learn tracks and improve lap times",
				"• Time Trial - PREMIUM timed races that cost credits but save your official high scores",
				"• Switch between tracks to explore different challenges!");
	}

	/// <summary>
	/// Save race completion via event-sourced backend with retry logic
	/// Accepts pre-captured lap data to prevent race condition with ResetRacingData
	/// </summary>
	private async Task SaveGlobalHighScore(string playerId, float totalTime, float[] lapTimes, CheckpointTime[] checkpoints)
	{
		if (string.IsNullOrEmpty(playerId) || totalTime <= 0.0f)
		{
			return;
		}

		if (_racingEventService == null)
		{
			GD.PrintErr("[RacingGame] RacingEventService not found - cannot save race");
			return;
		}

		// Create complete race entry with PRE-CAPTURED timing data
		// Data was captured synchronously before this async method to prevent race condition
		var raceEntry = new RaceEntry(
			RaceId: RaceDatabase.GenerateRaceId(),
			Date: DateTime.UtcNow,
			TrackId: _currentTrackId,
			PlayerId: playerId,
			Username: string.Empty, // Backend will populate from player_id
			TotalLaps: _targetLaps,
			TotalTime: totalTime,
			LapTimes: lapTimes,
			Checkpoints: checkpoints);

		// Retry logic with exponential backoff
		const int MAX_RETRIES = 3;
		for (int attempt = 1; attempt <= MAX_RETRIES; attempt++)
		{
			try
			{
				var result = await _racingEventService.EmitRaceFinishAsync(raceEntry);
				if (result.IsSuccess(out var _))
				{
					CallDeferred(MethodName.LogRaceSaved, totalTime, _currentTrackId);
					return;
				}
				else if (result.IsFailure(out var error))
				{
					GD.PrintErr($"[RacingGame] Race save attempt {attempt} failed: {error.Message}");

					if (attempt < MAX_RETRIES)
					{
						await Task.Delay(1000 * attempt);
					}
					else
					{
						// Final retry failed - log for potential offline queue implementation
						CallDeferred(MethodName.LogHighScoreError, $"Failed after {MAX_RETRIES} attempts: {error.Message}");
						GD.Print($"[RacingGame] Race data could be queued for offline sync: Track={_currentTrackId}, Time={totalTime}");
					}
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[RacingGame] Exception on attempt {attempt}: {ex.Message}");
				if (attempt == MAX_RETRIES)
				{
					CallDeferred(MethodName.LogAsyncError, $"Exception after {MAX_RETRIES} attempts: {ex.Message}");
				}
				else
				{
					await Task.Delay(1000 * attempt);
				}
			}
		}
	}

	/// <summary>
	/// Save partial race data when race is abandoned mid-way
	/// Preserves lap times and progress for incomplete races
	/// </summary>
	private async Task SavePartialRaceData(string playerId, string reason)
	{
		if (string.IsNullOrEmpty(playerId))
		{
			return;
		}

		if (_racingEventService == null)
		{
			GD.PrintErr("[RacingGame] RacingEventService not found - cannot save partial race");
			return;
		}

		var lapTimes = GetPlayerLapTimes(playerId);
		var completedLaps = GetPlayerCurrentLap(playerId);

		if (lapTimes.Count == 0 && completedLaps == 0)
		{
			return;
		}

		try
		{
			var result = await _racingEventService.EmitRaceAbandonedAsync(
				_currentTrackId,
				playerId,
				completedLaps,
				[.. lapTimes],
				reason);

			if (result.IsSuccess(out var _))
			{
				GD.Print($"[RacingGame] Partial race data saved - Laps: {completedLaps}, Reason: {reason}");
			}
			else if (result.IsFailure(out var error))
			{
				GD.PrintErr($"[RacingGame] Failed to save partial race data: {error.Message}");
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[RacingGame] Exception saving partial race data: {ex.Message}");
		}
	}

	// Thread-safe logging methods for CallDeferred usage from async contexts
	private void LogHighScoreError(string error)
	{
		GD.PrintErr($"[RacingGame] Failed to save race data: {error}");
		Platform.Notifications?.Show("Your race time could not be saved.", NotificationSeverity.Error);
	}

	private void LogRaceSaved(float totalTime, string trackId) => GD.Print($"[RacingGame] Race completed - Time: {totalTime:F3}s, Track: {trackId}");

	private void LogPotentialBestLap(float lapTime, string trackId) => GD.Print($"[RacingGame] Potential best lap: {lapTime:F3}s, Track: {trackId}");

	private void LogAsyncError(string message) => GD.PrintErr($"[RacingGame] {message}");

	/// <summary>
	/// Emit lap complete event to backend
	/// Individual lap times are tracked via events and aggregated by backend
	/// Accepts pre-captured lap/checkpoint data - the caller queues this call
	/// and captures state at the lap-crossing moment, since currentLap and
	/// checkpoints change again as soon as the next lap starts
	/// </summary>
	private async Task SaveBestLapTime(string playerId, float lapTime, int currentLap, CheckpointTime[] checkpoints)
	{
		if (string.IsNullOrEmpty(playerId) || lapTime <= 0.0f)
		{
			return;
		}

		if (_racingEventService == null)
		{
			return;
		}

		try
		{
			var result = await _racingEventService.EmitLapCompleteAsync(_currentTrackId, currentLap, lapTime, checkpoints);

			if (result.IsSuccess(out var _))
			{
				// Log potential best lap (backend will determine if it's actually best)
				CallDeferred(MethodName.LogPotentialBestLap, lapTime, _currentTrackId);
			}
		}
		catch (Exception ex)
		{
			// Thread-safe error logging via CallDeferred
			CallDeferred(MethodName.LogAsyncError, $"Exception emitting lap complete event: {ex.Message}");
		}
	}

	/// <summary>
	/// Handle user login - refresh UI to enable time trial button.
	/// Auto-connected to SessionManager.UserLoggedIn signal by base class.
	/// </summary>
	protected override void OnUserLoggedIn(UserSession session)
	{
		UpdateUI();
	}

	/// <summary>
	/// Handle user logout - refresh UI to disable time trial button.
	/// Auto-connected to SessionManager.UserLoggedOut signal by base class.
	/// </summary>
	protected override void OnUserLoggedOut(string phoneNumber)
	{
		UpdateUI();
	}

	#endregion

	#region Game Modes

	public virtual async void StartTimeTrial()
	{
		if (GetRacingMode() == RacingMode.TimeTrial)
		{
			return;
		}

		bool isLoggedIn = _sessionManager.GetPrimarySession() != null;
		if (!isLoggedIn)
		{
			return;
		}

		// Reset idle timer when starting a premium feature
		_sessionManager.ResetAllIdleTimers();

		// Check credits only in production mode or when explicitly required
		bool isDevelopmentMode = Engine.IsEditorHint() || OS.IsDebugBuild();
		if (TimeTrialCreditCost > 0 && !isDevelopmentMode)
		{
			// Set state to waiting for credits during async check
			_timingSystem.SetWaitingForCredits();

			if (_sessionManager == null || !IsInstanceValid(_sessionManager))
			{
				GD.PrintErr("Time trial cancelled - session system not available");
				Platform.Notifications?.Show("Time trial cancelled - session system unavailable.", NotificationSeverity.Error);
				_timingSystem.StopRacing();
				return;
			}

			var currentSession = _sessionManager.GetPrimarySession();
			if (currentSession == null)
			{
				GD.PrintErr("Time trial cancelled - no active user session");
				Platform.Notifications?.Show("Time trial cancelled - please log in first.", NotificationSeverity.Warning);
				_timingSystem.StopRacing();
				return;
			}

			// Confirm and spend entry credits via the shared single-spend primitive
			bool creditsSpent = _creditService != null
				&& (await _creditService.SpendWithConfirmationAsync(currentSession.PlayerId, currentSession.PhoneNumber, TimeTrialCreditCost, "Time Trial Race")).IsSuccess(out var _);
			if (!creditsSpent)
			{
				_timingSystem?.StopRacing(); // Reset to idle state
				return;
			}
		}

		// Create ActivitySession for the time trial race
		if (_eventService != null && _sessionManager != null)
		{
			var currentSession = _sessionManager.GetPrimarySession();
			if (currentSession?.PlayerId != null && currentSession.PlayerId != Guid.Empty)
			{
				var locationManager = Platform.Location;
				if (locationManager != null && locationManager.IsConfigLoaded)
				{
					var boxId = locationManager.BoxId;
					var playerId = currentSession.PlayerId;

					GD.Print($"[RacingGame] Creating backend session for player {playerId} at box {boxId}");

					// Session create/close is owned by GameController (auto-closed on teardown).
					var sessionResult = await StartBackendSessionAsync(boxId, playerId);

					if (sessionResult.IsFailure(out var error))
					{
						GD.PrintErr($"[RacingGame] WARNING: Failed to create backend session: {error.Message}");

						// Continue anyway - game can work without backend session
					}
					else if (sessionResult.IsSuccess(out var sessionId))
					{
						GD.Print($"[RacingGame] Backend session created successfully: {sessionId}");
					}
				}
			}
		}

		// Invalidate UI state cache on mode transition
		_forceNextUIUpdate = true;

		SetRacingMode(RacingMode.TimeTrial);
		ResetForNewRace(); // Complete reset including car physics state

		if (ShowCountdown)
		{
			StartCountdown();
		}
		else
		{
			_timingSystem.StartRacing();
			StartRace();

			// Refresh UI to show pause button now that race is active
			RefreshUI();

			// Start first lap for all players (same as EndCountdown does)
			foreach (var player in _playerMgmt.GetPlayers())
			{
				_timingSystem.StartPlayerLap(player.PlayerId);
			}
		}
	}

	public virtual void StartPractice()
	{
		// Invalidate UI state cache on mode transition
		_forceNextUIUpdate = true;

		SetRacingMode(RacingMode.Practice);
		ResetForNewRace(); // Complete reset including car physics state
		_timingSystem.StartPracticeMode();
		StartRace();

		// In practice mode, immediately start first "lap" for timing
		foreach (var player in _playerMgmt.GetPlayers())
		{
			StartPlayerLap(player.PlayerId);
		}

		// Refresh UI to show pause button now that race is active
		RefreshUI();
	}

	#endregion

	#region Countdown

	protected virtual void StartCountdown()
	{
		_timingSystem.StartCountdown(CountdownDuration);
		OnCountdownStarted();
	}

	protected virtual void HandleCountdown(float delta)
	{
		bool countdownChanged = _timingSystem.UpdateCountdown(delta, CountdownDuration);

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

		if (!_timingSystem.IsInCountdown)
		{
			EndCountdown();
		}
	}

	protected virtual void EndCountdown()
	{
		_timingSystem.EndCountdown();
		_timingSystem.StartRacing();
		StartRace();
		OnCountdownEnded();

		// Refresh UI to show pause button now that race is active
		RefreshUI();

		foreach (var player in _playerMgmt.GetPlayers())
		{
			_timingSystem.StartPlayerLap(player.PlayerId);
		}
	}

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

	#endregion

	#region Lap Management

	public virtual void StartPlayerLap(string playerId)
	{
		_timingSystem.StartPlayerLap(playerId);
	}

	public virtual void CompletePlayerLap(string playerId, float lapTime)
	{
		_timingSystem.CompletePlayerLap(playerId, lapTime);

		// Get current lap AFTER completing the lap
		int currentLap = _timingSystem.GetPlayerCurrentLap(playerId);

		if (_currentGameMode == RacingMode.Practice)
		{
			_timingSystem.StartPlayerLap(playerId);
			_timingSystem.StartPracticeMode();
		}
		else if (currentLap >= TargetLaps)
		{
			CompletePlayerRace(playerId);
		}
		else
		{
			_timingSystem.StartPlayerLap(playerId);
			_timingSystem.StartRacing();
		}
	}

	protected virtual void CompletePlayerRace(string playerId)
	{
		_timingSystem.CompletePlayerRace(playerId, _playerMgmt.GetPlayers());

		if (_timingSystem.CurrentRacingState == RacingTimingSystem.RacingState.Finished)
		{
			EndRace();
		}
	}

	public virtual void OnPlayerCheckpointCrossed(string playerId, int checkpointIndex)
	{
		_timingSystem.OnPlayerCheckpointCrossed(playerId, checkpointIndex);
	}

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
		ResetRacingData();
		ResetCheckpoints();
		PositionCarAtStart();        // Reset position AND physics state (calls ResetCarState)
		_racingCar?.SetActive(true);
	}

	#endregion

	#region Track System

	/// <summary>
	/// Initialize track ID mappings for stable data storage
	/// </summary>
	private void InitializeTrackIdMappings()
	{
		_trackIndexToIdMap.Clear();

		if (TrackScenes == null || TrackScenes.Count == 0)
		{
			return;
		}

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
	/// Build track metadata cache once at initialization to avoid repeated scene instantiation.
	/// Track properties (name, laps) are immutable at runtime - only IsCurrentTrack changes.
	/// </summary>
	private void BuildTrackMetadataCache()
	{
		_cachedTrackMetadata = new Dictionary<string, RacingUIManager.TrackMetadata>();

		if (TrackScenes == null || TrackScenes.Count == 0)
		{
			return;
		}

		for (int i = 0; i < TrackScenes.Count; i++)
		{
			var trackScene = TrackScenes[i];
			if (trackScene?.ResourcePath == null)
			{
				continue;
			}

			var trackId = _trackIndexToIdMap.GetValueOrDefault(i, RaceDatabase.GenerateTrackId(trackScene));

			string trackName = $"Track {i + 1}"; // Fallback
			int defaultLaps = 3; // Fallback

			try
			{
				var tempInstance = trackScene.Instantiate();
				if (tempInstance is RacingTrackDefinition trackDef)
				{
					trackName = !string.IsNullOrEmpty(trackDef.TrackName)
						? trackDef.TrackName
						: $"Track {i + 1}";
					defaultLaps = trackDef.NumberOfLaps > 0
						? trackDef.NumberOfLaps
						: 3;
				}

				tempInstance.QueueFree();
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[RacingGame] Failed to read track metadata for index {i}: {ex.Message}");
			}

			_cachedTrackMetadata[trackId] = new RacingUIManager.TrackMetadata
			{
				TrackId = trackId,
				TrackName = trackName,
				DefaultLaps = defaultLaps,
				Scene = trackScene,
				Index = i,
				IsCurrentTrack = trackId == _currentTrackId,
			};
		}

		GD.Print($"[RacingGame] Track metadata cache built with {_cachedTrackMetadata.Count} tracks");
	}

	/// <summary>
	/// Update IsCurrentTrack flags in cached metadata (lightweight operation).
	/// Called when user switches tracks.
	/// </summary>
	private void UpdateCachedTrackMetadata()
	{
		if (_cachedTrackMetadata == null)
		{
			return;
		}

		// Only update the IsCurrentTrack flag - everything else is immutable
		foreach (var kvp in _cachedTrackMetadata)
		{
			var metadata = kvp.Value;
			metadata.IsCurrentTrack = metadata.TrackId == _currentTrackId;
		}
	}

	private void InitializeTrackSystem()
	{
		InitializeTrackIdMappings();

		BuildTrackMetadataCache();

		if (TrackScenes is { Count: > 0 })
		{
			_currentTrackIndex = 0;
			LoadTrack(0);
		}
		else
		{
			foreach (Node child in GetChildren())
			{
				if (child is not RacingTrackDefinition trackDef)
				{
					continue;
				}

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

	private void LoadTrack(int trackIndex)
	{
		if (TrackScenes == null || trackIndex < 0 || trackIndex >= TrackScenes.Count)
		{
			GD.PrintErr($"Invalid track index: {trackIndex}");
			return;
		}

		if (_trackDefinition != null)
		{
			DisconnectTrackSignals();
			_trackDefinition.QueueFree();
		}

		var trackScene = TrackScenes[trackIndex];
		var trackInstance = trackScene.Instantiate();

		if (trackInstance is RacingTrackDefinition trackDef)
		{
			_trackDefinition = trackDef;
			AddChild(_trackDefinition);

			_currentTrackIndex = trackIndex;
			_currentTrackId = _trackIndexToIdMap.GetValueOrDefault(trackIndex, "unknown_track");

			var viewport = GetViewport();
			if (viewport != null)
			{
				var screenSize = viewport.GetVisibleRect().Size;
				Vector2 screenCenter = screenSize / 2;
				_trackDefinition.GlobalPosition = screenCenter;
			}

			SetupLoadedTrack();
		}
		else
		{
			GD.PrintErr("Track scene does not contain RacingTrackDefinition");
			trackInstance.QueueFree();
		}
	}

	private void SetupLoadedTrack()
	{
		if (_trackDefinition == null)
		{
			return;
		}

		_trackDefinition.SetupTrack();
		_trackCurve = _trackDefinition.GetTrackCurve();

		_trackValidationSystem.Initialize(_trackDefinition, _trackCurve);

		RegisterTrackZones();

		_cameraController.SetTrackDefinition(_trackDefinition);
		_cameraController.PositionCameraOverTrack();
		_cameraController.SetupScreenEdgeColliders();

		TargetLaps = _trackDefinition.NumberOfLaps;
		InitializeCheckpointTracking();
		ConnectTrackSignals();
		PositionCarAtStart();
	}

	private void RegisterTrackZones()
	{
		if (_zoneManager == null || _trackDefinition == null)
		{
			return;
		}

		_zoneManager.ClearAllZones();

		var zones = FindZonesRecursive(_trackDefinition);

		foreach (var zone in zones)
		{
			_zoneManager.RegisterZone(zone);
		}

		if (zones.Count > 0)
		{
			GD.Print($"[RacingGame] Registered {zones.Count} zones from track");
		}
	}

	private List<RacingZone> FindZonesRecursive(Node root)
	{
		var zones = new List<RacingZone>();

		foreach (Node child in root.GetChildren())
		{
			if (child is RacingZone zone)
			{
				zones.Add(zone);
			}

			zones.AddRange(FindZonesRecursive(child));
		}

		return zones;
	}

	private void PositionCarAtStart()
	{
		if (_trackDefinition == null || _racingCar?.IsInitialized != true)
		{
			return;
		}

		var startPoint = _trackDefinition.GetStartLinePosition();
		var startLineDirection = _trackDefinition.GetStartLineDirection();

		if (startPoint == Vector2.Zero)
		{
			var viewport = GetViewport();
			if (viewport == null)
			{
				return;
			}

			var screenSize = viewport.GetVisibleRect().Size;
			Vector2 screenCenter = screenSize / 2;
			_racingCar.PositionCar(screenCenter);
			return;
		}

		var carPosition = startPoint - (startLineDirection * 50);
		var carRotation = startLineDirection.Angle() + (Mathf.Pi / 2);
		_racingCar.PositionCar(carPosition, carRotation);
	}

	public bool IsOnTrack(Vector2 position)
	{
		return _trackValidationSystem?.IsOnTrack(position) ?? true;
	}

	public float GetDistanceToTrackCenterLine(Vector2 position)
	{
		return _trackValidationSystem?.GetDistanceToTrackCenterLine(position) ?? float.MaxValue;
	}

	#endregion

	#region Checkpoints

	private void InitializeCheckpointTracking()
	{
		if (_trackDefinition == null)
		{
			return;
		}

		_checkpointTriggers = _trackDefinition.CheckpointTriggers;
		if (_checkpointTriggers.Length == 0)
		{
			return;
		}

		_checkpointsCrossed = new bool[_checkpointTriggers.Length];
		_nextCheckpointIndex = 0;
		UpdateCheckpointVisuals();
	}

	private void ConnectTrackSignals()
	{
		if (_trackDefinition == null || !IsInstanceValid(_trackDefinition))
		{
			return;
		}

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

	private void DisconnectTrackSignals()
	{
		if (_trackDefinition == null || !IsInstanceValid(_trackDefinition))
		{
			return;
		}

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

	private void OnStartLineTriggered(Node2D body)
	{
		if (!IsInstanceValid(_racingCar) || !_racingCar.IsInitialized)
		{
			return;
		}

		if (body != _racingCar.GetCarBody())
		{
			return;
		}

		string playerId = GetCurrentGamePlayerId();
		if (GetRacingMode() == RacingMode.TimeTrial && GetPlayerCurrentLap(playerId) > 0)
		{
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
		if (GetRacingMode() == RacingMode.Practice)
		{
			OnPlayerCheckpointCrossed(playerId, -1); // -1 indicates start line
		}
	}

	private void OnFinishLineTriggered(Node2D body)
	{
		if (!IsInstanceValid(_racingCar) || !_racingCar.IsInitialized)
		{
			return;
		}

		if (body != _racingCar.GetCarBody())
		{
			return;
		}

		string playerId = GetCurrentGamePlayerId();

		_timingSystem.SetCrossingFinish();

		if (GetRacingMode() == RacingMode.TimeTrial)
		{
			float lapTime = GetPlayerCurrentLapTime(playerId);
			CompletePlayerLap(playerId, lapTime);
			ResetCheckpoints();

			// Do NOT duplicate lap checking here - CompletePlayerLap handles it
		}
		else
		{
			OnPlayerCheckpointCrossed(playerId, -1);
			ResetCheckpoints();
			_timingSystem.StartPracticeMode();
		}
	}

	private void OnCheckpointTriggered(Node2D body, int checkpointIndex)
	{
		if (!IsInstanceValid(_racingCar) || !_racingCar.IsInitialized)
		{
			return;
		}

		if (body != _racingCar.GetCarBody())
		{
			return;
		}

		string playerId = GetCurrentGamePlayerId();
		if (GetRacingMode() == RacingMode.TimeTrial && checkpointIndex == _nextCheckpointIndex)
		{
			_checkpointsCrossed[checkpointIndex] = true;
			_nextCheckpointIndex++;

			_checkpointTriggers[checkpointIndex]?.MarkAsCrossed();

			UpdateCheckpointVisuals();
		}

		OnPlayerCheckpointCrossed(playerId, checkpointIndex);
	}

	private void ResetCheckpoints()
	{
		if (_checkpointsCrossed == null)
		{
			return;
		}

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
		if (_checkpointTriggers == null || _checkpointTriggers.Length == 0)
		{
			return;
		}

		for (int i = 0; i < _checkpointTriggers.Length; i++)
		{
			var checkpoint = _checkpointTriggers[i];
			if (checkpoint == null)
			{
				continue;
			}

			if (i < _nextCheckpointIndex)
			{
				checkpoint.MarkAsCrossed();
			}
			else if (i == _nextCheckpointIndex && GetRacingMode() == RacingMode.TimeTrial)
			{
				checkpoint.SetNextRequiredState();
			}
			else
			{
				checkpoint.SetFutureState();
			}
		}
	}

	#endregion

	#region UI System

	/// <summary>
	/// Setup the racing UI manager and connect all UI signals.
	/// Called during OnInitializeComponents.
	/// </summary>
	private void SetupRacingUI()
	{
		_uiManager = new RacingUIManager();
		var trackScenesList = TrackScenes?.ToList();
		_uiManager.Initialize(trackScenesList, _currentTrackIndex, TimeTrialCreditCost);
		AddChild(_uiManager);

		_uiManager.TimeTrialRequested += StartTimeTrial;
		_uiManager.RestartRequested += () =>
		{
			// Always properly end any active game before restarting
			if (IsRaceActive())
			{
				EndRace();
			}

			StartPractice();
		};
		_uiManager.TrackSwitchRequested += OnTrackSwitchRequested;
		_uiManager.ResumeRequested += Resume;
		_uiManager.MainMenuRequested += HandleMainMenuRequest;
		_uiManager.RaceAgainRequested += OnRaceAgainRequested;
		_uiManager.PracticeModeRequested += OnEndToPracticeModeRequested;
		_uiManager.TracksLeaderboardRequested += OnTracksLeaderboardRequested;
		_uiManager.TrackLoadFromOverlayRequested += OnTrackLoadFromOverlayRequested;
		_uiManager.AddCreditsRequested += OnAddCreditsRequested;
	}

	private async void OnTrackSwitchRequested(int trackIndex)
	{
		if (IsRaceActive() && GetRacingMode() == RacingMode.TimeTrial)
		{
			return; // Don't allow track switching during time trial
		}

		if (IsRaceActive() && GetRacingMode() == RacingMode.TimeTrial)
		{
			var playerId = GetCurrentPlayerPhoneNumber();
			if (playerId != null)
			{
				await SavePartialRaceData(playerId, "track_switch");
			}
		}

		// Set track loading state to disable input
		_timingSystem.SetTrackLoading();

		if (IsRaceActive())
		{
			EndRace();
		}

		_currentTrackIndex = trackIndex;
		LoadTrack(trackIndex);
		_uiManager.SetCurrentTrackIndex(trackIndex);

		// Invalidate caches on track switch
		_forceNextUIUpdate = true;
		UpdateCachedTrackMetadata();

		StartPractice();
	}

	private void HandleMainMenuRequest()
	{
		// Use base class method which includes confirmation dialog
		ReturnToMainMenu();
	}

	private void OnEndToPracticeModeRequested()
	{
		_timingSystem.StopRacing();
		StartPractice();
	}

	private async void OnRaceAgainRequested()
	{
		// Always require login first, regardless of development mode
		if (_sessionManager == null || !IsInstanceValid(_sessionManager))
		{
			GD.PrintErr("Race again cancelled - user management system not available");
			Platform.Notifications?.Show("Race again cancelled - session system unavailable.", NotificationSeverity.Error);
			return;
		}

		bool isLoggedIn = _sessionManager.GetPrimarySession() != null;
		if (!isLoggedIn)
		{
			GD.PrintErr("Race again cancelled - user must be logged in");
			Platform.Notifications?.Show("Race again cancelled - please log in first.", NotificationSeverity.Warning);
			return;
		}

		// Reset idle timer when starting a premium feature
		_sessionManager.ResetAllIdleTimers();

		// Check credits only in production mode or when explicitly required
		bool isDevelopmentMode = Engine.IsEditorHint() || OS.IsDebugBuild();
		if (TimeTrialCreditCost > 0 && !isDevelopmentMode)
		{
			// Set state to waiting for credits during async check
			_timingSystem.SetWaitingForCredits();

			if (_sessionManager == null || !IsInstanceValid(_sessionManager))
			{
				GD.PrintErr("Race again cancelled - session system not available");
				Platform.Notifications?.Show("Race again cancelled - session system unavailable.", NotificationSeverity.Error);
				_timingSystem.StopRacing();
				return;
			}

			var currentSession = _sessionManager.GetPrimarySession();
			if (currentSession == null)
			{
				GD.PrintErr("Race again cancelled - no active user session");
				Platform.Notifications?.Show("Race again cancelled - please log in first.", NotificationSeverity.Warning);
				_timingSystem.StopRacing();
				return;
			}

			// Confirm and spend entry credits via the shared single-spend primitive
			bool creditsSpent = _creditService != null
				&& (await _creditService.SpendWithConfirmationAsync(currentSession.PlayerId, currentSession.PhoneNumber, TimeTrialCreditCost, "Race Again")).IsSuccess(out var _);
			if (!creditsSpent)
			{
				GD.PrintErr("Race again cancelled - insufficient credits");
				Platform.Notifications?.Show("Race again cancelled - insufficient credits.", NotificationSeverity.Warning);
				_timingSystem?.StopRacing(); // Reset to idle state
				return;
			}
		}

		_timingSystem?.StopRacing(); // Reset to idle
		SetRacingMode(RacingMode.TimeTrial);
		ResetForNewRace(); // Complete reset including car physics state

		if (ShowCountdown)
		{
			StartCountdown();
		}
		else
		{
			_timingSystem?.StartRacing();
			StartRace();

			// Start first lap for all players (same as EndCountdown does)
			foreach (var player in _playerMgmt.GetPlayers())
			{
				_timingSystem.StartPlayerLap(player.PlayerId);
			}
		}
	}

	private void OnAddCreditsRequested()
	{
		// Reset idle timer when accessing premium features
		if (_sessionManager != null && IsInstanceValid(_sessionManager))
		{
			_sessionManager.ResetAllIdleTimers();
		}

		// Get primary user for single-user racing context
		var currentSession = _sessionManager?.GetPrimarySession();
		if (currentSession == null)
		{
			GD.PrintErr("[RacingGame] No active user session for credit purchase");
			Platform.Notifications?.Show("Please log in to buy credits.", NotificationSeverity.Warning);
			return;
		}

		var uiManager = Platform.UI;
		if (uiManager != null && IsInstanceValid(uiManager))
		{
			uiManager.ShowBuyCreditsModal(currentSession.PhoneNumber);
		}
		else
		{
			GD.PrintErr("[RacingGame] UIManager not available for credit purchase");
			Platform.Notifications?.Show("Credit purchase is unavailable right now.", NotificationSeverity.Error);
		}
	}

	private void OnCreditsAcquired(string userId, int amount)
	{
		_uiManager?.OnCreditsAcquired();
	}

	private void OnTracksLeaderboardRequested()
	{
		// Use the actual phone number from session as player ID for leaderboard
		var playerId = GetCurrentPlayerPhoneNumber();
		var trackMetadata = GatherTrackMetadata();

		_uiManager?.UpdateTrackMetadataWithLeaderboard(trackMetadata, _currentTrackId, playerId);
	}

	private void OnTrackLoadFromOverlayRequested(string trackId)
	{
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
			OnTrackSwitchRequested(trackIndex);
		}
	}

	/// <summary>
	/// Initialize tracks & leaderboard system after UI setup
	/// </summary>
	private void InitializeTracksLeaderboardSystem()
	{
		if (_uiManager == null)
		{
			return;
		}

		var trackMetadata = GatherTrackMetadata();
		_uiManager.InitializeTracksLeaderboard(trackMetadata, _currentTrackId);
	}

	public void ShowHighScores()
	{
		_timingSystem.SetHighScoreDisplay();

		ShowGlobalHighScores();
	}

	private async void ShowGlobalHighScores()
	{
		try
		{
			if (_racingEventService == null)
			{
				GD.PrintErr("[RacingGame] RacingEventService not found, showing fallback");
				ShowHighScoresFallback();
				return;
			}

			var leaderboardResult = await _racingEventService.GetLeaderboardAsync(_currentTrackId, "best_race", _targetLaps);

			if (leaderboardResult.IsSuccess(out var leaderboard))
			{
				var scoreText = "🏁 RACING HIGH SCORES 🏁\n\n";
				scoreText += $"Track: {leaderboard.TrackId}\n";
				scoreText += $"Mode: {leaderboard.Metric}\n\n";

				if (leaderboard.Leaderboard.Count == 0)
				{
					scoreText += "No scores yet - be the first!\n";
				}
				else
				{
					scoreText += "Rank | Player          | Time\n";
					scoreText += "-----+----------------+--------\n";

					int rank = 1;
					foreach (var entry in leaderboard.Leaderboard)
					{
						var playerName = entry.Username.Length > 14
							? entry.Username.Substring(0, 14)
							: entry.Username.PadRight(14);
						var timeStr = FormatTime(entry.MetricValue);

						scoreText += $" {rank,2}  | {playerName} | {timeStr}\n";
						rank++;
					}
				}

				GD.Print(scoreText);
			}
			else if (leaderboardResult.IsFailure(out var error))
			{
				GD.PrintErr($"[RacingGame] Failed to fetch leaderboard: {error.Message}");
				ShowHighScoresFallback();
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[RacingGame] Exception showing high scores: {ex.Message}");
			ShowHighScoresFallback();
		}

		// Return to previous state after brief delay
		CreateAutoCleanupTimer(5.0f, () =>
		{
			if (GetRacingMode() == RacingMode.Practice)
			{
				_timingSystem?.StartPracticeMode();
			}
			else
			{
				_timingSystem?.StopRacing();
			}
		});
	}

	private void ShowHighScoresFallback()
	{
		GD.Print("🏁 RACING HIGH SCORES 🏁\n\nHigh scores feature not available - DataStore service unavailable.");

		// Return to previous state after brief delay
		CreateAutoCleanupTimer(2.0f, () =>
		{
			if (GetRacingMode() == RacingMode.Practice)
			{
				_timingSystem?.StartPracticeMode();
			}
			else
			{
				_timingSystem?.StopRacing();
			}
		});
	}

	public void HideHighScores()
	{
		if (GetRacingMode() == RacingMode.Practice)
		{
			_timingSystem.StartPracticeMode();
		}
		else
		{
			_timingSystem.StopRacing();
		}
	}

	private Timer CreateAutoCleanupTimer(float waitTime, Action onTimeout)
	{
		var timer = new Timer();
		timer.WaitTime = waitTime;
		timer.OneShot = true;
		timer.Timeout += () =>
		{
			onTimeout?.Invoke();
			timer.QueueFree();
		};
		AddChild(timer);
		timer.Start();
		return timer;
	}

	#endregion

	#region Race State

	/// <summary>
	/// Check if a race is currently active (started and not in game over state)
	/// </summary>
	public bool IsRaceActive()
	{
		if (_timingSystem == null)
		{
			return false;
		}

		var state = _timingSystem.CurrentRacingState;
		return state != RacingTimingSystem.RacingState.Idle &&
			   state != RacingTimingSystem.RacingState.GameOverDeciding;
	}

	public bool IsRacePaused()
	{
		return _timingSystem.CurrentRacingState == RacingTimingSystem.RacingState.RacePaused;
	}

	#endregion

	#region Race Lifecycle

	public void StartRace()
	{
		Platform.Host?.NotifyGameStarted();

		EmitSignal(SignalName.RaceStarted);
		GD.Print("[RacingGame] Race started");
	}

	public void EndRace()
	{
		// Invalidate UI state cache on race end
		_forceNextUIUpdate = true;

		// Show game over overlay for time trials by updating UI state
		if (GetRacingMode() == RacingMode.TimeTrial)
		{
			// Set game over state to disable input
			_timingSystem.SetGameOverDeciding();

			// Update UI - PopulateState will detect GameOverDeciding state and show overlay
			var current = _useStateA ? _uiStateA : _uiStateB;
			PopulateState(current);
			_uiManager.UpdateFromState(current);
			_useStateA = !_useStateA;

			_racingCar.SetActive(false);
		}

		Platform.Host?.NotifyGameEnded();

		EmitSignal(SignalName.RaceEnded);
		GD.Print("[RacingGame] Race ended");
	}

	protected override void OnPause()
	{
		_timingSystem.SetPaused(true);

		EmitSignal(SignalName.RacePaused);
		GD.Print("[RacingGame] Race paused");
	}

	protected override void OnResume()
	{
		_timingSystem.SetPaused(false);

		EmitSignal(SignalName.RaceResumed);
		GD.Print("[RacingGame] Race resumed");
	}

	private void UpdateUI()
	{
		if (!IsInstanceValid(_uiManager))
		{
			return;
		}

		// Double-buffer: populate current buffer, compare against previous
		var current = _useStateA ? _uiStateA : _uiStateB;
		var previous = _useStateA ? _uiStateB : _uiStateA;
		PopulateState(current);

		// Skip UI update if state hasn't changed (unless forced by mode transition)
		if (!_forceNextUIUpdate && current.StateEquals(previous))
		{
			return;
		}

		_forceNextUIUpdate = false;
		_uiManager.UpdateFromState(current);
		_useStateA = !_useStateA;
	}

	/// <summary>
	/// Gather track metadata for UI updates.
	/// Returns cached metadata instead of rebuilding every frame.
	/// </summary>
	private Dictionary<string, RacingUIManager.TrackMetadata> GatherTrackMetadata()
	{
		if (_cachedTrackMetadata != null && _cachedTrackMetadata.Count > 0)
		{
			return _cachedTrackMetadata;
		}

		// Fallback to empty dictionary if cache not built (shouldn't happen after initialization)
		GD.PrintErr("[RacingGame] Track metadata cache not initialized - returning empty dictionary");
		return new Dictionary<string, RacingUIManager.TrackMetadata>();
	}

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
		catch (Exception)
		{
			// Fallback if instantiation fails
		}

		return $"Track {index + 1}";
	}

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
		catch (Exception)
		{
			// Fallback if instantiation fails
		}

		return 3;
	}

	private void PopulateState(RacingUIState target)
	{
		// Use consistent player ID throughout - phone number when logged in
		var playerId = GetCurrentGamePlayerId();
		var gameMode = GetRacingMode();
		var loggedIn = _sessionManager?.GetPrimarySession() != null;
		var currentRacingState = _timingSystem.CurrentRacingState;

		bool isTimeTrialInProgress = gameMode == RacingMode.TimeTrial &&
			currentRacingState != RacingTimingSystem.RacingState.Idle &&
			currentRacingState != RacingTimingSystem.RacingState.PracticeMode;

		bool canStartTimeTrial = loggedIn &&
			!isTimeTrialInProgress &&
			currentRacingState != RacingTimingSystem.RacingState.WaitingForCredits &&
			currentRacingState != RacingTimingSystem.RacingState.TrackLoading &&
			currentRacingState != RacingTimingSystem.RacingState.GameOverDeciding &&
			currentRacingState != RacingTimingSystem.RacingState.HighScoreDisplay;

		// Enable replay button if user is logged in - actual credit check happens in OnRaceAgainRequested
		bool canAffordReplay = loggedIn;

		bool canShowTracksLeaderboard = !isTimeTrialInProgress &&
			currentRacingState != RacingTimingSystem.RacingState.WaitingForCredits &&
			currentRacingState != RacingTimingSystem.RacingState.TrackLoading &&
			currentRacingState != RacingTimingSystem.RacingState.GameOverDeciding &&
			!IsInCountdown();

		target.IsGamePaused = IsRacePaused();
		target.IsInCountdown = IsInCountdown();
		target.IsTimeTrialInProgress = isTimeTrialInProgress;
		target.CanStartTimeTrial = canStartTimeTrial;
		target.GameMode = gameMode;
		target.CarSpeed = _racingCar?.GetCarSpeed() ?? 0f;
		target.CurrentLap = GetPlayerCurrentLap(playerId);
		target.TargetLaps = TargetLaps;
		target.TimeDisplay = gameMode == RacingMode.Practice ?
			GetPlayerGapTime(playerId) : GetPlayerCurrentLapTime(playerId);
		target.TimeLabel = gameMode == RacingMode.Practice ? "Gap" : "Time";

		target.MaxSpeed = _racingCar?.MaxSpeed ?? 1800.0f;
		target.LapProgress = CalculateLapProgress(playerId);
		target.CountdownNumber = GetCountdownNumber();
		target.CountdownProgress = CalculateCountdownProgress();

		target.IsUserLoggedIn = loggedIn;
		target.ShowGameOverOverlay = currentRacingState == RacingTimingSystem.RacingState.GameOverDeciding;
		target.FinalTime = !string.IsNullOrEmpty(playerId) ? _timingSystem.GetPlayerTotalTime(playerId) : 0.0f;
		target.CanAffordReplay = canAffordReplay;

		target.ShowTracksLeaderboardOverlay = _uiManager?.IsTracksLeaderboardVisible ?? false;
		target.CanShowTracksLeaderboard = canShowTracksLeaderboard;
		target.TrackMetadata = GatherTrackMetadata();
		target.CurrentTrackId = _currentTrackId;
	}

	#endregion

	#region Player Identity

	/// <summary>
	/// Get the current player's phone number from SessionManager (null if not logged in)
	/// </summary>
	private string GetCurrentPlayerPhoneNumber()
	{
		try
		{
			var sessionManager = Platform.Session;
			if (sessionManager != null && IsInstanceValid(sessionManager))
			{
				var currentSession = sessionManager.GetPrimarySession();
				if (currentSession != null)
				{
					return currentSession.PhoneNumber;
				}
			}
		}
		catch (Exception ex)
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
		if (_racingCar is not null && !string.IsNullOrEmpty(_racingCar.PlayerId) && _racingCar.PlayerId != "player1")
		{
			return _racingCar.PlayerId;
		}

		var phoneNumber = GetCurrentPlayerPhoneNumber();
		if (!string.IsNullOrEmpty(phoneNumber))
		{
			return phoneNumber;
		}

		return "guest_player";
	}

	#endregion

	#region Data Getters

	public int GetPlayerCurrentLap(string playerId) => _timingSystem.GetPlayerCurrentLap(playerId);

	public float GetPlayerCurrentLapTime(string playerId) => _timingSystem.GetPlayerCurrentLapTime(playerId);

	public float GetPlayerBestLapTime(string playerId) => _timingSystem.GetPlayerBestLapTime(playerId);

	public float GetPlayerGapTime(string playerId) => _timingSystem.GetPlayerGapTime(playerId);

	public List<float> GetPlayerLapTimes(string playerId) => _timingSystem.GetPlayerLapTimes(playerId);

	private CheckpointTime[] GetPlayerCheckpointTimes(string playerId)
	{
		return [.. _timingSystem.GetPlayerCheckpointTimes(playerId)];
	}

	public RacingTimingSystem.RacingState GetRacingState() => _timingSystem.CurrentRacingState;

	public bool IsInCountdown() => _timingSystem.IsInCountdown;

	public int GetCountdownNumber() => _timingSystem.CountdownNumber;

	#endregion

	#region UI Integration

	public override ContextButtonData[] GetContextButtons()
	{
		var buttons = new List<ContextButtonData>();

		buttons.Add(GameContextButton.CreateReturnToMenuButton(() =>
		{
			if (_sessionManager != null && IsInstanceValid(_sessionManager))
			{
				_sessionManager.ResetAllIdleTimers();
			}

			ReturnToMainMenu();
		}));

		if (IsRacePaused())
		{
			buttons.Add(GameContextButton.CreateResumeButton(() =>
			{
				Resume();
				RefreshUI();
			}));
		}
		else if (IsRaceActive())
		{
			buttons.Add(GameContextButton.CreatePauseButton(() =>
			{
				Pause();
				RefreshUI();
			}));
		}

		return [.. buttons];
	}

	public override string GetGameTitle()
	{
		if (GetRacingMode() == RacingMode.Practice)
		{
			return "Racing Game - Practice";
		}
		else if (GetRacingMode() == RacingMode.TimeTrial)
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
			_cameraController?.OnViewportSizeChanged();
		}
	}

	#endregion

	#region Arc HUD Helpers

	/// <summary>
	/// Calculate lap progress for arc display (0.0 to 1.0)
	/// </summary>
	private float CalculateLapProgress(string playerId)
	{
		if (!IsInstanceValid(_timingSystem))
		{
			return 0.0f;
		}

		// For practice mode, use gap time as a rough progress indicator
		if (GetRacingMode() == RacingMode.Practice)
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
		{
			return 0.0f;
		}

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

	/// <summary>
	/// Format time in MM:SS.mmm format
	/// </summary>
	private string FormatTime(float timeSeconds)
	{
		int minutes = (int)(timeSeconds / 60.0f);
		float seconds = timeSeconds % 60.0f;
		return $"{minutes:00}:{seconds:00.000}";
	}

	#endregion
}
