using Godot;
using System;
using System.Collections.Generic;

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
	
	// Status display labels
	private Label _speedLabel;
	private Label _lapLabel;
	private Label _timeLabel;
	
	// Control buttons
	private Button _timeTrialButton;
	private Button[] _trackSelectionButtons;

	private Button _gameOverRestartButton;
	private Button _gameOverPracticeButton;
	
	// Overlays
	private Control _pauseOverlay;
	private Control _gameOverOverlay;
	private Control _countdownOverlay;
	private Label _countdownLabel;
	private Label _finalTimeLabel;

	// Track selection data
	private List<PackedScene> _trackScenes;
	private int _currentTrackIndex = 0;

	// ================================================================
	// PUBLIC PROPERTIES
	// ================================================================

	public bool IsPauseOverlayVisible => _pauseOverlay?.Visible ?? false;
	public bool IsGameOverOverlayVisible => _gameOverOverlay?.Visible ?? false;
	public bool IsCountdownOverlayVisible => _countdownOverlay?.Visible ?? false;

	// ================================================================
	// INITIALIZATION
	// ================================================================

	/// <summary>
	/// Initialize the UI manager
	/// </summary>
	/// <param name="trackScenes">Available track scenes for selection</param>
	/// <param name="currentTrackIndex">Currently selected track index</param>
	/// <param name="timeTrialCreditCost">Credit cost for time trial mode</param>
	public void Initialize(List<PackedScene> trackScenes = null, int currentTrackIndex = 0, int timeTrialCreditCost = 1)
	{
		_trackScenes = trackScenes;
		_currentTrackIndex = currentTrackIndex;
		TimeTrialCreditCost = timeTrialCreditCost;

		SetupUILayer();
		SetupMainUI();
		SetupOverlays();
	}

	/// <summary>
	/// Setup the main UI layer
	/// </summary>
	private void SetupUILayer()
	{
		_uiLayer = new CanvasLayer();
		AddChild(_uiLayer);
	}

	/// <summary>
	/// Setup the main game UI
	/// </summary>
	private void SetupMainUI()
	{
		var mainUI = new Control();
		mainUI.AnchorLeft = 0;
		mainUI.AnchorTop = 0;
		mainUI.AnchorRight = 1;
		mainUI.AnchorBottom = 1;
		_uiLayer.AddChild(mainUI);

		SetupStatusBar(mainUI);
		SetupBottomControls(mainUI);
	}

	/// <summary>
	/// Setup the status bar showing speed, lap, and time information
	/// </summary>
	/// <param name="parent">Parent container</param>
	private void SetupStatusBar(Control parent)
	{
		var statusBar = new HBoxContainer();
		statusBar.AnchorLeft = 0;
		statusBar.AnchorTop = 1;
		statusBar.AnchorRight = 1;
		statusBar.AnchorBottom = 1;
		statusBar.OffsetTop = -120; // Position above bottom controls
		statusBar.OffsetBottom = -80;
		statusBar.Alignment = BoxContainer.AlignmentMode.Center;
		parent.AddChild(statusBar);

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

		// Track selection buttons
		if (_trackScenes != null && _trackScenes.Count > 1)
		{
			bottomBar.AddChild(new VSeparator());
			SetupTrackSelectionButtons(bottomBar);
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
		var restartButton = new Button() { Text = "Restart Practice" };
		restartButton.Pressed += () => { 
			ResetUserIdleTimer();
			EmitSignal(SignalName.RestartRequested);
		};
		parent.AddChild(restartButton);
	}

	/// <summary>
	/// Setup track selection buttons
	/// </summary>
	/// <param name="parent">Parent container</param>
	private void SetupTrackSelectionButtons(Container parent)
	{
		if (_trackScenes == null || _trackScenes.Count <= 1) return;

		_trackSelectionButtons = new Button[_trackScenes.Count];

		for (int i = 0; i < _trackScenes.Count; i++)
		{
			var trackIndex = i; // Capture loop variable
			var trackButton = new Button();
			
			// Get track name from the scene
			var trackScene = _trackScenes[i];
			if (trackScene != null)
			{
				var tempInstance = trackScene.Instantiate();
				if (tempInstance is RacingTrackDefinition trackDef)
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

			trackButton.Pressed += () => {
				ResetUserIdleTimer();
				EmitSignal(SignalName.TrackSwitchRequested, trackIndex);
			};
			_trackSelectionButtons[i] = trackButton;
			parent.AddChild(trackButton);
		}

		UpdateTrackSelectionButtons(false, false);
	}

	/// <summary>
	/// Setup all overlay UIs
	/// </summary>
	private void SetupOverlays()
	{
		SetupCountdownOverlay();
		SetupPauseOverlay();
		SetupGameOverOverlay();
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
	/// Setup game over overlay
	/// </summary>
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

		var vbox = new VBoxContainer();
		vbox.AnchorLeft = 0.5f;
		vbox.AnchorTop = 0.5f;
		vbox.AnchorRight = 0.5f;
		vbox.AnchorBottom = 0.5f;
		vbox.OffsetLeft = -150;
		vbox.OffsetTop = -100;
		vbox.OffsetRight = 150;
		vbox.OffsetBottom = 100;
		vbox.Alignment = BoxContainer.AlignmentMode.Center;
		_gameOverOverlay.AddChild(vbox);

		Label raceCompleteLabel = new Label() { Text = "RACE COMPLETE!", HorizontalAlignment = HorizontalAlignment.Center };
		vbox.AddChild(raceCompleteLabel);

		_finalTimeLabel = new Label() { Text = "Final Time: 0.0s", HorizontalAlignment = HorizontalAlignment.Center };
		vbox.AddChild(_finalTimeLabel);

		_gameOverRestartButton = new Button() { Text = "Race Again" };
		_gameOverRestartButton.Pressed += () => { 
			ResetUserIdleTimer();
			EmitSignal(SignalName.RaceAgainRequested);
		};
		vbox.AddChild(_gameOverRestartButton);

		_gameOverPracticeButton = new Button() { Text = "Practice Mode" };
		_gameOverPracticeButton.Pressed += () => { 
			ResetUserIdleTimer();
			EmitSignal(SignalName.PracticeModeRequested);
		};
		vbox.AddChild(_gameOverPracticeButton);
	}

	// ================================================================
	// UI UPDATE METHODS
	// ================================================================

	/// <summary>
	/// Update status labels with current game state
	/// </summary>
	/// <param name="carSpeed">Current car speed</param>
	/// <param name="gameMode">Current game mode</param>
	/// <param name="currentLap">Current lap number</param>
	/// <param name="targetLaps">Target number of laps</param>
	/// <param name="timeDisplay">Time value to display</param>
	/// <param name="timeLabel">Label for the time display</param>
	public void UpdateStatusLabels(float carSpeed, GameController.GameMode gameMode, int currentLap, int targetLaps, float timeDisplay, string timeLabel)
	{
		// Update speed display
		if (_speedLabel != null)
		{
			_speedLabel.Text = $"Speed: {(int)carSpeed}";
		}

		// Update lap/mode display
		if (_lapLabel != null)
		{
			if (gameMode == GameController.GameMode.Practice)
			{
				_lapLabel.Text = "Practice Mode";
			}
			else
			{
				_lapLabel.Text = $"Lap: {currentLap}/{targetLaps}";
			}
		}

		// Update time display
		if (_timeLabel != null)
		{
			_timeLabel.Text = $"{timeLabel}: {timeDisplay:F1}s";
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
						  state.TargetLaps, state.TimeDisplay, state.TimeLabel);
		
		UpdateButtonStates(state.IsTimeTrialInProgress, state.CanStartTimeTrial, state.IsInCountdown, 
						  state.GameMode == GameController.GameMode.TimeTrial, state.IsUserLoggedIn);

		SetPauseOverlayVisible(state.IsGamePaused);
		SetGameOverOverlayVisible(state.ShowGameOverOverlay, state.FinalTime, state.CanAffordReplay);
	}

	/// <summary>
	/// Update button states based on racing state and authentication
	/// </summary>
	/// <param name="isTimeTrialInProgress">Whether a formal time trial race is in progress</param>
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

		// Update track selection buttons - disable during time trial or countdown
		UpdateTrackSelectionButtons(isTimeTrial && isTimeTrialInProgress, isInCountdown);
	}

	/// <summary>
	/// Update track selection button states
	/// </summary>
	/// <param name="isActiveTimeTrial">Whether time trial is active</param>
	/// <param name="isInCountdown">Whether countdown is active</param>
	public void UpdateTrackSelectionButtons(bool isActiveTimeTrial, bool isInCountdown)
	{
		if (_trackSelectionButtons == null) return;

		// Allow track switching in practice mode, but not during time trial or countdown
		bool shouldDisableAll = isActiveTimeTrial || isInCountdown;

		for (int i = 0; i < _trackSelectionButtons.Length; i++)
		{
			if (_trackSelectionButtons[i] != null)
			{
				_trackSelectionButtons[i].Disabled = (i == _currentTrackIndex) || shouldDisableAll;
			}
		}
	}

	// ================================================================
	// OVERLAY CONTROL
	// ================================================================

	/// <summary>
	/// Show/hide pause overlay
	/// </summary>
	/// <param name="visible">Whether to show the overlay</param>
	public void SetPauseOverlayVisible(bool visible)
	{
		if (_pauseOverlay != null)
		{
			_pauseOverlay.Visible = visible;
		}
	}

	/// <summary>
	/// Show/hide game over overlay with final time
	/// </summary>
	/// <param name="visible">Whether to show the overlay</param>
	/// <param name="finalTime">Final race time to display</param>
	public void SetGameOverOverlayVisible(bool visible, float finalTime, bool canAffordReplay)
	{
		if (_gameOverOverlay != null)
		{
			_gameOverOverlay.Visible = visible;
			
			if (visible)
			{
				_finalTimeLabel.Text = $"Final Time: {finalTime:F2}s";
				
				// Disable restart button if player can't afford replay
				_gameOverRestartButton.Disabled = !canAffordReplay;
			}
		}
	}

	/// <summary>
	/// Show/hide countdown overlay
	/// </summary>
	/// <param name="visible">Whether to show the overlay</param>
	public void SetCountdownOverlayVisible(bool visible)
	{
		if (_countdownOverlay != null)
		{
			_countdownOverlay.Visible = visible;
		}
	}

	/// <summary>
	/// Update countdown text
	/// </summary>
	/// <param name="text">Text to display</param>
	public void UpdateCountdownText(string text)
	{
		if (_countdownLabel != null)
		{
			_countdownLabel.Text = text;
		}
	}

	// ================================================================
	// TRACK MANAGEMENT
	// ================================================================

	/// <summary>
	/// Update current track index
	/// </summary>
	/// <param name="trackIndex">New track index</param>
	public void SetCurrentTrackIndex(int trackIndex)
	{
		_currentTrackIndex = trackIndex;
		UpdateTrackSelectionButtons(false, false);
	}

	/// <summary>
	/// Update track scenes list
	/// </summary>
	/// <param name="trackScenes">New track scenes list</param>
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

	/// <summary>
	/// Reset user idle timer through UserManager
	/// </summary>
	private void ResetUserIdleTimer()
	{
		var userManager = UserManager.GetAutoload();
		if (userManager != null && GodotObject.IsInstanceValid(userManager))
		{
			userManager.ResetUserIdleTimer();
		}
	}

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
}
}