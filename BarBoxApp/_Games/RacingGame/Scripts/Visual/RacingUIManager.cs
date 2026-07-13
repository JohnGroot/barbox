using Godot;
using LightResults;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BarBox.Core.UI;

namespace BarBox.Games.Racing
{
	/// <summary>
	/// Manages all UI elements for racing games including overlays, status displays, and controls
	/// Extracted from RacingGame to follow single responsibility principle
	/// </summary>
	[GlobalClass]
	public partial class RacingUIManager : Node
	{
	// ================================================================
	// SIGNALS
	// ================================================================
	
	[Signal] public delegate void TimeTrialRequestedEventHandler();
	[Signal] public delegate void RestartRequestedEventHandler();
	[Signal] public delegate void TrackSwitchRequestedEventHandler(int trackIndex);
	[Signal] public delegate void ResumeRequestedEventHandler();
	[Signal] public delegate void MainMenuRequestedEventHandler();
	[Signal] public delegate void RaceAgainRequestedEventHandler();
	[Signal] public delegate void PracticeModeRequestedEventHandler();
	[Signal] public delegate void TracksLeaderboardRequestedEventHandler();
	[Signal] public delegate void TrackLoadFromOverlayRequestedEventHandler(string trackId);
	[Signal] public delegate void AddCreditsRequestedEventHandler();

	// ================================================================
	// EXPORT PROPERTIES
	// ================================================================
	
	[ExportCategory("UI Settings")]
	[Export] public int TimeTrialCreditCost { get; set; } = 1;

	// ================================================================
	// CONSTANTS
	// ================================================================
	
	// Total height reserved for TopMenuBar (100px) + ContextButtonBar (50px)
	private const float TOP_MENU_HEIGHT = 150.0f;
	
	// ================================================================
	// PRIVATE FIELDS - UI COMPONENTS
	// ================================================================

	// Main UI layer
	private CanvasLayer _uiLayer;
	
	// Arc-based HUD renderer
	private RacingHUDArcRenderer _hudArcRenderer;

	// Time trial timer display
	private Control _timerDisplayContainer;
	private Panel _timerBackground;
	private Label _timerLabel;
	
	// Control buttons
	private Button _timeTrialButton;
	private Button _restartButton;
	private Button _tracksLeaderboardButton;

	// Overlays
	private Control _pauseOverlay;
	private Control _countdownOverlay;
	private Label _countdownLabel;

	// Track selection data
	private List<PackedScene> _trackScenes;
	private int _currentTrackIndex = 0;
	
	// Tracks & Leaderboard overlay data
	private RacingTracksLeaderboardUI _tracksLeaderboardOverlay;
	private Dictionary<string, TrackMetadata> _trackMetadataCache = [];

	// Race complete overlay data
	private RacingRaceCompleteUI _raceCompleteOverlay;

	// Countdown state tracking
	private bool _wasCountdownVisible = false;

	// Race complete overlay visibility tracking
	private bool _raceCompleteOverlayWasVisible = false;

	// Backend service integration
	private RacingEventService _racingEventService;

	// Circuit breaker for leaderboard failures
	private int _leaderboardFailureCount = 0;
	private const int MAX_LEADERBOARD_FAILURES = 3;

	// Concurrency guards for async operations
	private bool _isLoadingRaceComplete = false;
	private bool _isLoadingTracksLeaderboard = false;

	// ================================================================
	// NESTED CLASSES FOR TRACK & LEADERBOARD MANAGEMENT
	// ================================================================

	/// <summary>
	/// Track metadata for display and leaderboard organization
	/// </summary>
	public class TrackMetadata
	{
		public string TrackId { get; set; } = "";
		public string TrackName { get; set; } = "";
		public int DefaultLaps { get; set; } = 3;
		public PackedScene Scene { get; set; }
		public int Index { get; set; } = -1;
		public float BestRaceTime { get; set; } = 0f;
		public bool HasPlayerScores { get; set; } = false;
		public bool IsCurrentTrack { get; set; } = false;
		public int RaceCount { get; set; } = 0;
	}

	/// <summary>
	/// Individual leaderboard entry for display
	/// </summary>
	public class LeaderboardEntry
	{
		public string TrackId { get; set; } = "";
		public string TrackName { get; set; } = "";
		public float Time { get; set; } = 0f;
		public string Type { get; set; } = ""; // "Best Race"
		public int Laps { get; set; } = 0;
		public bool IsCurrentPlayer { get; set; } = false;
		public DateTime SetDate { get; set; } = DateTime.UtcNow;
		public string Username { get; set; } = "";

		// Rank for display purposes
		public string DisplayRank { get; set; } = "";

		public string FormattedTime => Time > 0f ? $"{Time:F3}s" : "No time";
		public string CategoryDisplay => $"Best Race ({Laps} laps)";
	}

	// ================================================================
	// PUBLIC PROPERTIES
	// ================================================================

	public bool IsPauseOverlayVisible => _pauseOverlay.Visible;
	public bool IsGameOverOverlayVisible => _raceCompleteOverlay.IsVisible;
	public bool IsRaceCompleteOverlayVisible => _raceCompleteOverlay.IsVisible;
	public bool IsCountdownOverlayVisible => _countdownOverlay.Visible;

	// ================================================================
	// INITIALIZATION
	// ================================================================

	public void Initialize(List<PackedScene> trackScenes = null, int currentTrackIndex = 0, int timeTrialCreditCost = 1)
	{
		_trackScenes = trackScenes;
		_currentTrackIndex = currentTrackIndex;
		TimeTrialCreditCost = timeTrialCreditCost;

		// Initialize singleton racing event service
		_racingEventService = new RacingEventService(SessionEventService.GetInstance());

		SetupUILayer();
		SetupMainUI();
		SetupOverlays();
	}

	private void SetupUILayer()
	{
		_uiLayer = new CanvasLayer();
		AddChild(_uiLayer);
	}

	private void SetupMainUI()
	{
		var mainUI = new Control();
		mainUI.AnchorLeft = 0;
		mainUI.AnchorTop = 0;
		mainUI.AnchorRight = 1;
		mainUI.AnchorBottom = 1;
		_uiLayer.AddChild(mainUI);

		SetupStatusBar(mainUI);
		SetupTimerDisplay(mainUI);
		SetupBottomControls(mainUI);
	}

	/// <summary>
	/// Setup the status bar with modern arc-based HUD elements
	/// </summary>
	/// <param name="parent">Parent container</param>
	private void SetupStatusBar(Control parent)
	{
		// Create arc-based HUD renderer
		_hudArcRenderer = new RacingHUDArcRenderer();
		_uiLayer.AddChild(_hudArcRenderer);
	}

	/// <summary>
	/// Setup the time trial timer display
	/// </summary>
	/// <param name="parent">Parent container</param>
	private void SetupTimerDisplay(Control parent)
	{
		// Container for timer (centered above bottom bar)
		_timerDisplayContainer = new Control();
		_timerDisplayContainer.AnchorLeft = 0.5f;
		_timerDisplayContainer.AnchorTop = 1.0f;
		_timerDisplayContainer.AnchorRight = 0.5f;
		_timerDisplayContainer.AnchorBottom = 1.0f;
		_timerDisplayContainer.OffsetLeft = -150; // Half of 300px width
		_timerDisplayContainer.OffsetTop = -140; // 140px from bottom (above 60px bottom bar + 80px spacing)
		_timerDisplayContainer.OffsetRight = 150;
		_timerDisplayContainer.OffsetBottom = -80; // 60px height
		_timerDisplayContainer.Visible = false; // Hidden by default, shown during time trial
		parent.AddChild(_timerDisplayContainer);

		// Semi-transparent background panel
		_timerBackground = new Panel();
		_timerBackground.AnchorLeft = 0;
		_timerBackground.AnchorTop = 0;
		_timerBackground.AnchorRight = 1;
		_timerBackground.AnchorBottom = 1;

		// Create StyleBoxFlat for rounded corners and semi-transparent background
		var styleBox = new StyleBoxFlat();
		styleBox.BgColor = new Color(0.1f, 0.1f, 0.15f, 0.85f); // Dark semi-transparent
		styleBox.CornerRadiusTopLeft = 8;
		styleBox.CornerRadiusTopRight = 8;
		styleBox.CornerRadiusBottomLeft = 8;
		styleBox.CornerRadiusBottomRight = 8;
		styleBox.BorderWidthTop = 2;
		styleBox.BorderWidthBottom = 2;
		styleBox.BorderWidthLeft = 2;
		styleBox.BorderWidthRight = 2;
		styleBox.BorderColor = new Color(0.3f, 0.6f, 0.9f, 0.6f); // Subtle blue border
		_timerBackground.AddThemeStyleboxOverride("panel", styleBox);
		_timerDisplayContainer.AddChild(_timerBackground);

		// Timer label
		_timerLabel = new Label();
		_timerLabel.Text = "00:00.000"; // Leading zero for consistent width
		_timerLabel.AnchorLeft = 0;
		_timerLabel.AnchorTop = 0;
		_timerLabel.AnchorRight = 1;
		_timerLabel.AnchorBottom = 1;
		_timerLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_timerLabel.VerticalAlignment = VerticalAlignment.Center;

		// Set fixed minimum size to prevent jittering when text changes width
		_timerLabel.CustomMinimumSize = new Vector2(200, 60);

		// Configure size flags to prevent dynamic resizing
		_timerLabel.SizeFlagsHorizontal = Control.SizeFlags.Fill;
		_timerLabel.SizeFlagsVertical = Control.SizeFlags.Fill;

		// Load Inter font and create FontVariation with tabular figures for zero jitter
		var fontFile = GD.Load<FontFile>("res://_Core/Fonts/Inter/InterDisplay-SemiBold.ttf");
		var fontVariation = new FontVariation();
		fontVariation.SetBaseFont(fontFile);

		// Enable tabular figures (tnum) via OpenType features for equal-width digits
		var opentypeFeatures = new Godot.Collections.Dictionary();
		opentypeFeatures["tnum"] = 1; // Tabular numbers - all digits same width
		fontVariation.SetOpentypeFeatures(opentypeFeatures);

		// Create LabelSettings for font styling
		var labelSettings = new LabelSettings();
		labelSettings.Font = fontVariation; // Use font variation with tabular figures
		labelSettings.FontSize = 52; // Large, readable font
		labelSettings.FontColor = Colors.White;
		labelSettings.OutlineSize = 4; // Add outline for better contrast
		labelSettings.OutlineColor = new Color(0, 0, 0, 0.8f); // Dark outline
		_timerLabel.LabelSettings = labelSettings;

		_timerDisplayContainer.AddChild(_timerLabel);
	}

	/// <summary>
	/// Setup the bottom control buttons
	/// </summary>
	/// <param name="parent">Parent container</param>
	private void SetupBottomControls(Control parent)
	{
		var bottomBar = new HBoxContainer();
		bottomBar.AnchorLeft = 0;
		bottomBar.AnchorTop = 1;
		bottomBar.AnchorRight = 1;
		bottomBar.AnchorBottom = 1;
		bottomBar.OffsetTop = -60;
		bottomBar.Alignment = BoxContainer.AlignmentMode.Center;
		parent.AddChild(bottomBar);

		// Time trial button
		SetupTimeTrialButton(bottomBar);
		bottomBar.AddChild(new VSeparator());

		// Restart button
		SetupRestartButton(bottomBar);

		// Tracks & Leaderboard button
		if (_trackScenes != null && _trackScenes.Count > 0)
		{
			bottomBar.AddChild(new VSeparator());
			SetupTracksLeaderboardButton(bottomBar);
		}
	}

	/// <summary>
	/// Setup the time trial button
	/// </summary>
	/// <param name="parent">Parent container</param>
	private void SetupTimeTrialButton(Container parent)
	{
		// Update button text to show credit cost if applicable
		string timeTrialText = "Start Time Trial (3 Laps)";
		if (TimeTrialCreditCost > 0 && !GameHost.ShouldBypassCredits())
		{
			timeTrialText += $" - {TimeTrialCreditCost} Credit{(TimeTrialCreditCost != 1 ? "s" : "")}";
		}
		
		_timeTrialButton = new Button() { Text = timeTrialText };
		_timeTrialButton.Pressed += () => EmitSignal(SignalName.TimeTrialRequested);
		parent.AddChild(_timeTrialButton);
	}

	/// <summary>
	/// Setup the restart button
	/// </summary>
	/// <param name="parent">Parent container</param>
	private void SetupRestartButton(Container parent)
	{
		_restartButton = new Button() { Text = "Restart Practice" };
		_restartButton.Pressed += () => {
			ResetUserIdleTimer();
			EmitSignal(SignalName.RestartRequested);
		};
		parent.AddChild(_restartButton);
	}

	/// <summary>
	/// Setup tracks & leaderboard button
	/// </summary>
	/// <param name="parent">Parent container</param>
	private void SetupTracksLeaderboardButton(Container parent)
	{
		_tracksLeaderboardButton = new Button() { Text = "Tracks & Leaderboard" };
		_tracksLeaderboardButton.Pressed += () => {
			ResetUserIdleTimer();
			ShowTracksLeaderboard();
		};
		parent.AddChild(_tracksLeaderboardButton);
	}

	/// <summary>
	/// Setup all overlay UIs
	/// </summary>
	private void SetupOverlays()
	{
		SetupCountdownOverlay();
		SetupPauseOverlay();
		SetupRaceCompleteOverlay();
	}

	// ================================================================
	// OVERLAY MANAGEMENT
	// ================================================================

	/// <summary>
	/// Setup countdown overlay
	/// </summary>
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

	/// <summary>
	/// Setup pause overlay
	/// </summary>
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

		var vbox = new VBoxContainer();
		vbox.AnchorLeft = 0.5f;
		vbox.AnchorTop = 0.5f;
		vbox.AnchorRight = 0.5f;
		vbox.AnchorBottom = 0.5f;
		vbox.OffsetLeft = -100;
		vbox.OffsetTop = -100;
		vbox.OffsetRight = 100;
		vbox.OffsetBottom = 100;
		vbox.Alignment = BoxContainer.AlignmentMode.Center;
		_pauseOverlay.AddChild(vbox);

		var pauseLabel = new Label() { Text = "PAUSED", HorizontalAlignment = HorizontalAlignment.Center };
		vbox.AddChild(pauseLabel);

		var resumeButton = new Button() { Text = "Resume" };
		resumeButton.Pressed += () => EmitSignal(SignalName.ResumeRequested);
		vbox.AddChild(resumeButton);

		var restartButton = new Button() { Text = "Restart" };
		restartButton.Pressed += () => { 
			ResetUserIdleTimer();
			EmitSignal(SignalName.RestartRequested);
		};
		vbox.AddChild(restartButton);
		
		var mainMenuButton = new Button() { Text = "Main Menu" };
		mainMenuButton.Pressed += () => {
			ResetUserIdleTimer();
			EmitSignal(SignalName.MainMenuRequested);
		};
		vbox.AddChild(mainMenuButton);
	}

	/// <summary>
	/// Setup race complete overlay with comprehensive results display
	/// </summary>
	private void SetupRaceCompleteOverlay()
	{
		_raceCompleteOverlay = new RacingRaceCompleteUI();
		_raceCompleteOverlay.CreateOverlay(_uiLayer);

		// Connect button signals
		_raceCompleteOverlay.TryAgainButton.Pressed += () => {
			ResetUserIdleTimer();
			EmitSignal(SignalName.RaceAgainRequested);
		};

		_raceCompleteOverlay.AddCreditsButton.Pressed += () => {
			ResetUserIdleTimer();
			EmitSignal(SignalName.AddCreditsRequested);
		};

		_raceCompleteOverlay.ReturnToPracticeButton.Pressed += () => {
			ResetUserIdleTimer();
			EmitSignal(SignalName.PracticeModeRequested);
		};
	}

	// ================================================================
	// UI UPDATE METHODS
	// ================================================================

	/// <summary>
	/// Update status labels and arc HUD with current game state
	/// </summary>
	/// <param name="carSpeed">Current car speed</param>
	/// <param name="gameMode">Current game mode</param>
	/// <param name="currentLap">Current lap number</param>
	/// <param name="targetLaps">Target number of laps</param>
	/// <param name="timeDisplay">Time value to display</param>
	/// <param name="timeLabel">Label for the time display</param>
	/// <param name="maxSpeed">Maximum car speed for percentage calculation</param>
	/// <param name="lapProgress">Current lap progress (0.0 to 1.0)</param>
	public void UpdateStatusLabels(float carSpeed, RacingMode gameMode, int currentLap, int targetLaps, float timeDisplay, string timeLabel, float maxSpeed = 1800.0f, float lapProgress = 0.0f)
	{
		// Update arc-based HUD (primary display)
		if (_hudArcRenderer != null && IsInstanceValid(_hudArcRenderer))
		{
			var timeText = $"{timeLabel}: {timeDisplay:F1}s";
			var isPracticeMode = (gameMode == RacingMode.Practice);
			_hudArcRenderer.UpdateHUDState(carSpeed, maxSpeed, currentLap, targetLaps, lapProgress, timeText, isPracticeMode);
		}
	}

	/// <summary>
	/// Update all UI elements from complete game state - replaces individual update methods
	/// </summary>
	/// <param name="state">Complete racing game UI state</param>
	public void UpdateFromState(RacingUIState state)
	{
		// Update everything, every time - let Godot handle rendering optimization
		UpdateStatusLabels(state.CarSpeed, state.GameMode, state.CurrentLap,
						  state.TargetLaps, state.TimeDisplay, state.TimeLabel, state.MaxSpeed, state.LapProgress);

		UpdateButtonStates(state.IsTimeTrialInProgress, state.CanStartTimeTrial, state.IsInCountdown,
						  state.GameMode == RacingMode.TimeTrial, state.IsUserLoggedIn);

		SetPauseOverlayVisible(state.IsGamePaused);

		// Use new race complete overlay with track information
		if (state.ShowGameOverOverlay)
		{
			SetRaceCompleteOverlayVisible(true, state.FinalTime, state.CanAffordReplay, state.CurrentTrackId, TimeTrialCreditCost);
		}
		else
		{
			SetRaceCompleteOverlayVisible(false);
		}

		// Update countdown display in arc renderer
		UpdateCountdownArc(state.IsInCountdown, state.CountdownNumber, state.CountdownProgress);

		// Update time trial timer display
		UpdateTimerDisplay(state.GameMode == RacingMode.TimeTrial, state.TimeDisplay);
	}

	/// <summary>
	/// Update button states based on racing state and authentication
	/// </summary>
	/// <param name="isTimeTrialInProgress">Whether a formal time trial race is in progress</param>
	/// <param name="canStartTimeTrial">Whether user can start a time trial</param>
	/// <param name="isInCountdown">Whether countdown is active</param>
	/// <param name="isTimeTrial">Whether in time trial mode</param>
	/// <param name="isUserLoggedIn">Whether user is logged in (required for time trial)</param>
	public void UpdateButtonStates(bool isTimeTrialInProgress, bool canStartTimeTrial, bool isInCountdown, bool isTimeTrial, bool isUserLoggedIn)
	{
		// Update time trial button - disabled during time trial or when not logged in
		if (_timeTrialButton != null)
		{
			_timeTrialButton.Disabled = !canStartTimeTrial || isTimeTrialInProgress || !isUserLoggedIn;
		}

		// Update restart button - hide during time trials and countdown
		if (_restartButton != null)
		{
			_restartButton.Visible = !isTimeTrialInProgress && !isInCountdown;
		}

		// Update track selection buttons - disable during time trial or countdown
		UpdateTracksLeaderboardButton(isTimeTrial && isTimeTrialInProgress, isInCountdown);
	}

	/// <summary>
	/// Update tracks & leaderboard button state
	/// </summary>
	/// <param name="isActiveTimeTrial">Whether time trial is active</param>
	/// <param name="isInCountdown">Whether countdown is active</param>
	public void UpdateTracksLeaderboardButton(bool isActiveTimeTrial, bool isInCountdown)
	{
		if (_tracksLeaderboardButton == null) return;
		
		// Disable during active time trial or countdown
		bool shouldDisable = isActiveTimeTrial || isInCountdown;
		_tracksLeaderboardButton.Disabled = shouldDisable;
	}

	// ================================================================
	// OVERLAY CONTROL
	// ================================================================

	public void SetPauseOverlayVisible(bool visible)
	{
		_pauseOverlay.Visible = visible;
	}

	public void SetRaceCompleteOverlayVisible(bool visible, float finalTime = 0f, bool canAffordReplay = true, string currentTrackId = "", int creditCost = 1)
	{

		if (visible && !_raceCompleteOverlayWasVisible)
		{
			// Overlay transitioning from hidden to visible - load data once
			// Update button states first
			_raceCompleteOverlay.UpdateButtonStates(canAffordReplay, creditCost);

			// Load high score data for this track
			LoadRaceCompleteDataAsync(currentTrackId, finalTime);

			_raceCompleteOverlay.ShowOverlay();
		}
		else if (!visible && _raceCompleteOverlayWasVisible)
		{
			// Overlay transitioning from visible to hidden
			_raceCompleteOverlay.HideOverlay();
		}

		// Track visibility state for next call
		_raceCompleteOverlayWasVisible = visible;
	}

	public void SetGameOverOverlayVisible(bool visible, float finalTime, bool canAffordReplay)
	{
		// Legacy method - now delegates to race complete overlay for time trials
		// Keep the old simple overlay for non-time-trial modes if needed
		if (visible)
		{
			SetRaceCompleteOverlayVisible(visible, finalTime, canAffordReplay, "", TimeTrialCreditCost);
		}
		else
		{
			SetRaceCompleteOverlayVisible(false);
		}
	}

	public void SetCountdownOverlayVisible(bool visible)
	{
		// Hide traditional overlay completely - arc renderer handles countdown
		_countdownOverlay.Visible = false;

		// Arc renderer handles the primary countdown display
		if (_hudArcRenderer != null && IsInstanceValid(_hudArcRenderer))
		{
			if (!visible)
			{
				_hudArcRenderer.HideCountdown();
			}
		}
	}

	public void UpdateCountdownText(string text)
	{
		_countdownLabel.Text = text;
	}

	/// <summary>
	/// Update countdown arc animation with number and progress
	/// </summary>
	/// <param name="isVisible">Whether countdown is active</param>
	/// <param name="countdownNumber">Current countdown number (3, 2, 1, or 0 for GO)</param>
	/// <param name="countdownProgress">Progress from 0.0 (start) to 1.0 (complete)</param>
	private void UpdateCountdownArc(bool isVisible, int countdownNumber, float countdownProgress)
	{
		if (_hudArcRenderer != null && IsInstanceValid(_hudArcRenderer))
		{
			if (isVisible)
			{
				// If countdown just became visible, start the countdown animation
				if (!_wasCountdownVisible)
				{
					_hudArcRenderer.StartCountdown(countdownNumber, countdownProgress);
				}
				else
				{
					_hudArcRenderer.UpdateCountdown(countdownNumber, countdownProgress);
				}
			}
			else
			{
				_hudArcRenderer.HideCountdown();
			}

			// Track previous state
			_wasCountdownVisible = isVisible;
		}
	}

	private void UpdateTimerDisplay(bool isTimeTrial, float currentTime)
	{
		// Show timer only during time trial mode
		_timerDisplayContainer.Visible = isTimeTrial;

		if (isTimeTrial)
		{
			// Update timer text with formatted time
			_timerLabel.Text = FormatLapTime(currentTime);
		}
	}

	private async void LoadRaceCompleteDataAsync(string trackId, float finalTime)
	{
		// Concurrency guard - prevent overlapping requests
		if (_isLoadingRaceComplete)
			return;
		_isLoadingRaceComplete = true;

		// Circuit breaker - stop after too many failures
		if (_leaderboardFailureCount >= MAX_LEADERBOARD_FAILURES)
		{
			GD.PrintErr($"[RacingUIManager] Too many leaderboard failures ({_leaderboardFailureCount}), using fallback");
			UpdateRaceCompleteUIFallback(trackId, finalTime);
			_isLoadingRaceComplete = false;
			return;
		}
		try
		{
			// Get current player's phone number (set by RacingGame via UpdateTrackMetadataWithLeaderboard)
			var playerId = _currentPlayerId;
			var trackName = GetTrackNameFromId(trackId);

			// Load high score data from backend
			var highScores = new List<LeaderboardEntry>();
			int playerPosition = 0;
			bool isNewHighScore = false;

			if (playerId != null && _racingEventService != null)
			{
				// Get track lap count from metadata
				int trackLaps = 3; // Default
				if (_trackMetadataCache.TryGetValue(trackId, out var trackMetadata))
				{
					trackLaps = trackMetadata.DefaultLaps;
				}

				var leaderboardResult = await _racingEventService.GetLeaderboardAsync(
					trackId,
					"best_race",
					trackLaps,
					limit: 10
				);

				if (leaderboardResult.IsSuccess(out var leaderboard))
				{

					// Convert to LeaderboardEntry format
					for (int i = 0; i < leaderboard.Leaderboard.Count; i++)
					{
						var entry = leaderboard.Leaderboard[i];
						highScores.Add(new LeaderboardEntry
						{
							TrackId = trackId,
							TrackName = trackName,
							Time = entry.MetricValue,
							Type = "Best Race",
							Laps = trackLaps,
							Username = entry.Username,
							IsCurrentPlayer = entry.PlayerId.ToString() == playerId,
							DisplayRank = (i + 1).ToString()
						});

						// Calculate player position
						if (entry.PlayerId.ToString() == playerId)
						{
							playerPosition = i + 1;
							isNewHighScore = (i == 0); // First place
						}
					}

					// Reset failure counter on success
					_leaderboardFailureCount = 0;
				}
				else if (leaderboardResult.IsFailure(out var error))
				{
					_leaderboardFailureCount++;
					GD.PrintErr($"[RacingUIManager] Failed to load leaderboard ({_leaderboardFailureCount}/{MAX_LEADERBOARD_FAILURES}): {error.Message}");
				}
			}

			// If no data loaded, show empty state
			if (highScores.Count == 0 && finalTime > 0f)
			{
				playerPosition = 1; // First recorded time
				isNewHighScore = true;
			}

			// Update UI on main thread
			var callable = Callable.From(() => UpdateRaceCompleteUI(trackName, finalTime, playerPosition, isNewHighScore, highScores, playerId));
			callable.CallDeferred();
		}
		catch (System.Exception ex)
		{
			_leaderboardFailureCount++;
			GD.PrintErr($"[RacingUIManager] Exception loading race complete data ({_leaderboardFailureCount}/{MAX_LEADERBOARD_FAILURES}): {ex.Message}");

			// Fallback UI update
			var fallbackCallable = Callable.From(() => UpdateRaceCompleteUIFallback(trackId, finalTime));
			fallbackCallable.CallDeferred();
		}
		finally
		{
			_isLoadingRaceComplete = false;
		}
	}

	/// <summary>
	/// Update race complete UI on main thread
	/// </summary>
	private void UpdateRaceCompleteUI(string trackName, float finalTime, int position, bool isNewHighScore, List<LeaderboardEntry> highScores, string currentPlayerPhoneNumber)
	{
		if (_raceCompleteOverlay == null)
			return;

		// Update race results display
		_raceCompleteOverlay.UpdateRaceResults(finalTime, trackName, position, isNewHighScore);

		// Update high scores table
		_raceCompleteOverlay.UpdateHighScores(highScores, currentPlayerPhoneNumber);
	}

	/// <summary>
	/// Fallback race complete UI update when DataStore is unavailable
	/// </summary>
	private void UpdateRaceCompleteUIFallback(string trackId, float finalTime)
	{
		if (_raceCompleteOverlay == null)
			return;

		var trackName = GetTrackNameFromId(trackId);

		// Show basic results without leaderboard data
		_raceCompleteOverlay.UpdateRaceResults(finalTime, trackName, 1, true); // Assume first place without data
		_raceCompleteOverlay.UpdateHighScores(new List<LeaderboardEntry>(), null);
	}

	/// <summary>
	/// Get track name from track ID (fallback if not in cache)
	/// </summary>
	private string GetTrackNameFromId(string trackId)
	{
		if (_trackMetadataCache.TryGetValue(trackId, out var track))
		{
			return track.TrackName;
		}

		// Fallback: convert track ID back to readable name
		return trackId.Replace("_", " ").Replace("-", " ");
	}

	// ================================================================
	// TRACK MANAGEMENT
	// ================================================================

	public void SetCurrentTrackIndex(int trackIndex)
	{
		_currentTrackIndex = trackIndex;
		UpdateTracksLeaderboardButton(false, false);
	}

	public void UpdateTrackScenes(List<PackedScene> trackScenes)
	{
		_trackScenes = trackScenes;
		// Note: This would require rebuilding the track selection buttons
		// For now, assume this is called during initialization
	}

	// ================================================================
	// CONTEXT BUTTON SUPPORT
	// ================================================================

	/// <summary>
	/// Get context button data for top menu integration
	/// </summary>
	/// <param name="isGamePaused">Whether game is currently paused</param>
	/// <param name="isGameActive">Whether game is currently active</param>
	/// <returns>Array of context button data</returns>
	public ContextButtonData[] GetContextButtons(bool isGamePaused, bool isGameActive)
	{
		var buttons = new List<ContextButtonData>();

		// Standard "Return to Menu" button
		buttons.Add(GameContextButton.CreateReturnToMenuButton(() => {
			ResetUserIdleTimer();
			EmitSignal(SignalName.MainMenuRequested);
		}));

		// Pause/Resume button
		if (isGameActive)
		{
			if (isGamePaused)
			{
				buttons.Add(GameContextButton.CreateResumeButton(() => {
					EmitSignal(SignalName.ResumeRequested);
				}));
			}
			else
			{
				buttons.Add(GameContextButton.CreatePauseButton(() => {
					// Note: Pause signal would need to be added if needed
				}));
			}
		}

		// Restart button
		if (isGameActive)
		{
			buttons.Add(new ContextButtonData("Restart", () => {
				ResetUserIdleTimer();
				EmitSignal(SignalName.RestartRequested);
			}));
		}

		return buttons.ToArray();
	}

	// ================================================================
	// UTILITY METHODS
	// ================================================================

	private void ResetUserIdleTimer()
	{
		SessionManager.GetInstance()?.ResetAllIdleTimers();
	}

	// ================================================================
	// TRACKS & LEADERBOARD MANAGEMENT
	// ================================================================

	/// <summary>
	/// Initialize tracks & leaderboard overlay
	/// </summary>
	/// <param name="trackMetadata">Track metadata for display</param>
	/// <param name="currentTrackId">Currently loaded track ID</param>
	public void InitializeTracksLeaderboard(Dictionary<string, TrackMetadata> trackMetadata, string currentTrackId)
	{
		// Create overlay if it doesn't exist
		if (_tracksLeaderboardOverlay == null)
		{
			_tracksLeaderboardOverlay = new RacingTracksLeaderboardUI();
			_tracksLeaderboardOverlay.CreateOverlay(_uiLayer);
			
			// Connect signals
			_tracksLeaderboardOverlay.CloseButton.Pressed += HideTracksLeaderboard;
			_tracksLeaderboardOverlay.LoadTrackButton.Pressed += OnLoadTrackFromOverlay;
		}

		// Update track metadata cache and populate track list
		_trackMetadataCache = trackMetadata;
		PopulateTrackList(currentTrackId);
	}

	public void ShowTracksLeaderboard()
	{
		if (_tracksLeaderboardOverlay == null)
			return;

		// Reset idle timer when opening premium UI
		ResetUserIdleTimer();
		
		_tracksLeaderboardOverlay.ShowOverlay();
		EmitSignal(SignalName.TracksLeaderboardRequested);
	}

	public void HideTracksLeaderboard()
	{
		_tracksLeaderboardOverlay?.HideOverlay();
	}

	/// <summary>
	/// Update track metadata and refresh overlay if visible
	/// </summary>
	public void UpdateTrackMetadata(Dictionary<string, TrackMetadata> trackMetadata, string currentTrackId)
	{
		_trackMetadataCache = trackMetadata;
		
		if (_tracksLeaderboardOverlay?.IsVisible == true)
		{
			PopulateTrackList(currentTrackId);
			
			// Refresh leaderboard for currently selected track
			if (!string.IsNullOrEmpty(_tracksLeaderboardOverlay.SelectedTrackId))
			{
				UpdateLeaderboardForTrack(_tracksLeaderboardOverlay.SelectedTrackId);
			}
		}
	}

	private void PopulateTrackList(string currentTrackId)
	{
		if (_tracksLeaderboardOverlay == null)
			return;

		// Clear existing buttons
		_tracksLeaderboardOverlay.ClearTrackButtons();

		// Add track buttons
		foreach (var track in _trackMetadataCache.Values.OrderBy(t => t.Index))
		{
			bool isCurrent = track.TrackId == currentTrackId;
			_tracksLeaderboardOverlay.AddTrackButton(track.TrackId, track.TrackName, isCurrent, track.HasPlayerScores);
			
			// Connect button signal
			var button = _tracksLeaderboardOverlay.GetTrackButton(track.TrackId);
			if (button != null)
			{
				var trackId = track.TrackId; // Capture for closure
				button.Pressed += () => OnTrackSelected(trackId);
			}
		}

		// Select first track or current track by default
		if (_trackMetadataCache.Count > 0)
		{
			var defaultTrack = _trackMetadataCache.Values.FirstOrDefault(t => t.TrackId == currentTrackId) ?? 
							   _trackMetadataCache.Values.OrderBy(t => t.Index).First();
			OnTrackSelected(defaultTrack.TrackId);
		}
	}

	private void OnTrackSelected(string trackId)
	{
		if (!_trackMetadataCache.TryGetValue(trackId, out var track))
			return;

		bool canLoad = !track.IsCurrentTrack;
		_tracksLeaderboardOverlay?.UpdateSelectedTrack(trackId, track.TrackName, canLoad);
		UpdateLeaderboardForTrack(trackId);
	}

	private void OnLoadTrackFromOverlay()
	{
		if (_tracksLeaderboardOverlay == null || string.IsNullOrEmpty(_tracksLeaderboardOverlay.SelectedTrackId))
			return;

		ResetUserIdleTimer();
		EmitSignal(SignalName.TrackLoadFromOverlayRequested, _tracksLeaderboardOverlay.SelectedTrackId);
		HideTracksLeaderboard();
	}

	private void UpdateLeaderboardForTrack(string trackId)
	{
		if (_tracksLeaderboardOverlay == null || !_trackMetadataCache.TryGetValue(trackId, out var track))
			return;

		// Load leaderboard data asynchronously
		LoadLeaderboardDataAsync(trackId);
	}

	/// <summary>
	/// Load leaderboard data from backend for a specific track with retry logic
	/// </summary>
	private async void LoadLeaderboardDataAsync(string trackId)
	{
		// Concurrency guard - prevent overlapping requests
		if (_isLoadingTracksLeaderboard)
			return;
		_isLoadingTracksLeaderboard = true;

		try
		{
			// Circuit breaker - stop after too many failures
			if (_leaderboardFailureCount >= MAX_LEADERBOARD_FAILURES)
			{
				GD.PrintErr($"[RacingUIManager] Too many leaderboard failures ({_leaderboardFailureCount}), using fallback");
				UpdateLeaderboardFallback(trackId);
				return;
			}

			// Get current player's phone number (set by RacingGame via UpdateTrackMetadataWithLeaderboard)
			// Note: Leaderboard should work even when no user is logged in (shows all players)
			var playerId = _currentPlayerId;

			// Query backend for leaderboard data using singleton service
			if (_racingEventService == null)
			{
				GD.PrintErr("[RacingUIManager] Racing event service not initialized");
				UpdateLeaderboardFallback(trackId);
				return;
			}

			// Get track lap count from metadata
			int trackLaps = 3; // Default
			if (_trackMetadataCache.TryGetValue(trackId, out var trackMetadata))
			{
				trackLaps = trackMetadata.DefaultLaps;
			}

			// Validate parameters before query
			if (trackLaps <= 0)
			{
				GD.PrintErr($"[RacingUIManager] Invalid lap count {trackLaps} for track {trackId}");
				UpdateLeaderboardFallback(trackId);
				return;
			}

			GD.Print($"[RacingUIManager] Querying leaderboard: trackId={trackId}, metric=best_race, laps={trackLaps}");

			// Retry logic with exponential backoff (matching SaveGlobalHighScore pattern)
			const int MAX_RETRIES = 3;
			Result<RacingLeaderboardData>? leaderboardResult = null;

			for (int attempt = 1; attempt <= MAX_RETRIES; attempt++)
			{
				try
				{
					leaderboardResult = await _racingEventService.GetLeaderboardAsync(
						trackId,
						"best_race",
						trackLaps,
						limit: 10
					);

					if (leaderboardResult.HasValue && leaderboardResult.Value.IsSuccess(out _))
					{
						// Success - break out of retry loop
						break;
					}
					else if (leaderboardResult.HasValue && leaderboardResult.Value.IsFailure(out var retryError))
					{
						GD.PrintErr($"[RacingUIManager] Leaderboard query attempt {attempt}/{MAX_RETRIES} failed: {retryError.Message}");

						if (attempt < MAX_RETRIES)
						{
							// Exponential backoff before retry
							int delayMs = 1000 * attempt;
							GD.Print($"[RacingUIManager] Retrying in {delayMs}ms...");
							await System.Threading.Tasks.Task.Delay(delayMs);
						}
					}
				}
				catch (System.Exception retryEx)
				{
					GD.PrintErr($"[RacingUIManager] Exception on leaderboard attempt {attempt}/{MAX_RETRIES}: {retryEx.Message}");

					if (attempt == MAX_RETRIES)
					{
						// Final attempt failed, throw to outer catch
						throw;
					}
					else
					{
						// Retry with backoff
						int delayMs = 1000 * attempt;
						await System.Threading.Tasks.Task.Delay(delayMs);
					}
				}
			}

			// Process result after retry loop
			if (leaderboardResult.HasValue && leaderboardResult.Value.IsSuccess(out var finalLeaderboard))
			{
				var lapEntries = new List<LeaderboardEntry>();
				var raceEntries = new List<LeaderboardEntry>();

				// Check if leaderboard is empty (no scores yet)
				if (finalLeaderboard.Leaderboard == null || finalLeaderboard.Leaderboard.Count == 0)
				{
					GD.Print($"[RacingUIManager] Leaderboard for {trackId} is empty (no scores yet)");
					// Show empty leaderboard (not an error)
					var emptyCallable = Callable.From(() => _tracksLeaderboardOverlay?.UpdateLeaderboard(lapEntries, raceEntries));
					emptyCallable.CallDeferred();
				}
				else
				{
					// Convert to LeaderboardEntry format for race entries
					for (int i = 0; i < finalLeaderboard.Leaderboard.Count; i++)
					{
						var entry = finalLeaderboard.Leaderboard[i];
						raceEntries.Add(new LeaderboardEntry
						{
							TrackId = trackId,
							TrackName = trackMetadata?.TrackName ?? trackId,
							Time = entry.MetricValue,
							Type = "Best Race",
							Laps = trackLaps,
							Username = entry.Username,
							IsCurrentPlayer = playerId != null && entry.PlayerId.ToString() == playerId,
							DisplayRank = (i + 1).ToString()
						});
					}

					GD.Print($"[RacingUIManager] Loaded {raceEntries.Count} leaderboard entries for {trackId}");

					// Update leaderboard UI on main thread
					var callable = Callable.From(() => _tracksLeaderboardOverlay?.UpdateLeaderboard(lapEntries, raceEntries));
					callable.CallDeferred();
				}

				// Reset failure counter on success
				_leaderboardFailureCount = 0;
			}
			else
			{
				// All retries failed
				_leaderboardFailureCount++;
				GD.PrintErr($"[RacingUIManager] All {MAX_RETRIES} attempts failed to load leaderboard ({_leaderboardFailureCount}/{MAX_LEADERBOARD_FAILURES})");
				UpdateLeaderboardFallback(trackId);
			}
		}
		catch (System.Exception ex)
		{
			_leaderboardFailureCount++;
			GD.PrintErr($"[RacingUIManager] Exception loading leaderboard ({_leaderboardFailureCount}/{MAX_LEADERBOARD_FAILURES}): {ex.Message}");
			var fallbackCallable = Callable.From(() => UpdateLeaderboardFallback(trackId));
			fallbackCallable.CallDeferred();
		}
		finally
		{
			_isLoadingTracksLeaderboard = false;
		}
	}

	/// <summary>
	/// Fallback leaderboard update when DataStore is unavailable
	/// </summary>
	private void UpdateLeaderboardFallback(string trackId)
	{
		if (_tracksLeaderboardOverlay?.SelectedTrackId != trackId)
			return;

		var emptyLapEntries = new List<LeaderboardEntry>();
		var emptyRaceEntries = new List<LeaderboardEntry>();
		
		_tracksLeaderboardOverlay.UpdateLeaderboard(emptyLapEntries, emptyRaceEntries);
	}

	/// <summary>
	/// Get current player ID (this should be set by the game during initialization)
	/// </summary>
	private string _currentPlayerId = "player1";
	
	public void SetCurrentPlayerId(string playerId)
	{
		_currentPlayerId = playerId;
	}
	
	private string GetCurrentPlayerId()
	{
		return _currentPlayerId;
	}

	/// <summary>
	/// Enhanced track metadata update with DataStore-powered leaderboard data
	/// </summary>
	public async void UpdateTrackMetadataWithLeaderboard(Dictionary<string, TrackMetadata> baseTrackMetadata, string currentTrackId, string playerId)
	{
		try
		{
			_currentPlayerId = playerId;

			// Get enhanced metadata with leaderboard data
			var enhancedMetadata = await EnhanceTrackMetadataWithScores(baseTrackMetadata, playerId);

			// Check validity after async operation - scene could be destroyed
			if (!IsInstanceValid(this))
				return;

			// Update cache and UI
			_trackMetadataCache = enhancedMetadata;

			if (_tracksLeaderboardOverlay?.IsVisible == true)
			{
				PopulateTrackList(currentTrackId);

				// Refresh leaderboard for currently selected track
				if (!string.IsNullOrEmpty(_tracksLeaderboardOverlay.SelectedTrackId))
				{
					UpdateLeaderboardForTrack(_tracksLeaderboardOverlay.SelectedTrackId);
				}
			}
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"[RacingUIManager] Exception updating track metadata: {ex.Message}");
		}
	}

	/// <summary>
	/// Enhance track metadata with player's best scores from DataStore
	/// </summary>
	private Task<Dictionary<string, TrackMetadata>> EnhanceTrackMetadataWithScores(Dictionary<string, TrackMetadata> baseMetadata, string playerId)
	{
		var enhancedMetadata = new Dictionary<string, TrackMetadata>();

		// Copy base metadata
		foreach (var kvp in baseMetadata)
		{
			enhancedMetadata[kvp.Key] = new TrackMetadata
			{
				TrackId = kvp.Value.TrackId,
				TrackName = kvp.Value.TrackName,
				DefaultLaps = kvp.Value.DefaultLaps,
				Scene = kvp.Value.Scene,
				Index = kvp.Value.Index,
				IsCurrentTrack = kvp.Value.IsCurrentTrack
			};
		}

		// TODO: Event-sourced - backend handles data persistence
		// For now, return basic metadata (backend will provide enhanced data via events)
		try
		{
			// No score enhancement available yet - backend will aggregate from racing events
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"[RacingUIManager] Exception enhancing track metadata: {ex.Message}");
		}

		return Task.FromResult(enhancedMetadata);
	}

	/// <summary>
	/// Check if tracks & leaderboard overlay is visible
	/// </summary>
	public bool IsTracksLeaderboardVisible => _tracksLeaderboardOverlay?.IsVisible ?? false;

	/// <summary>
	/// Update UI settings from external configuration
	/// </summary>
	/// <param name="timeTrialCreditCost">Credit cost for time trial</param>
	public void UpdateSettings(int timeTrialCreditCost)
	{
		TimeTrialCreditCost = timeTrialCreditCost;

		// Update time trial button text if it exists
		if (_timeTrialButton != null)
		{
			string timeTrialText = "Start Time Trial (3 Laps)";
			if (TimeTrialCreditCost > 0 && !GameHost.ShouldBypassCredits())
			{
				timeTrialText += $" - {TimeTrialCreditCost} Credit{(TimeTrialCreditCost != 1 ? "s" : "")}";
			}
			_timeTrialButton.Text = timeTrialText;
		}
	}

	/// <summary>
	/// Handle credits acquired event to update button states
	/// </summary>
	public void OnCreditsAcquired()
	{
		// Update race complete overlay button states if visible
		if (_raceCompleteOverlay != null && _raceCompleteOverlay.IsVisible)
		{
			// Assume player can now afford replay after purchasing credits
			_raceCompleteOverlay.UpdateButtonStates(true, TimeTrialCreditCost);
		}
	}

	/// <summary>
	/// Format lap time in MM:SS.mmm format
	/// Always pads minutes with leading zero for consistent width and prevent jittering
	/// </summary>
	/// <param name="timeSeconds">Time in seconds</param>
	/// <returns>Formatted time string</returns>
	private string FormatLapTime(float timeSeconds)
	{
		int minutes = (int)(timeSeconds / 60.0f);
		float seconds = timeSeconds % 60.0f;
		// Pad minutes to 2 digits for consistent text width
		return $"{minutes:00}:{seconds:00.000}";
	}
}
}