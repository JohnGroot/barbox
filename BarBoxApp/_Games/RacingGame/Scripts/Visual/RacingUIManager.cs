using Godot;
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
	
	// Status display labels (kept for compatibility)
	private Label _speedLabel;
	private Label _lapLabel;
	private Label _timeLabel;

	// Arc-based HUD renderer
	private RacingHUDArcRenderer _hudArcRenderer;
	
	// Control buttons
	private Button _timeTrialButton;
	private Button _restartButton;
	private Button _tracksLeaderboardButton;

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
	
	// Tracks & Leaderboard overlay data
	private TracksLeaderboardOverlay _tracksLeaderboardOverlay;
	private Dictionary<string, TrackMetadata> _trackMetadataCache = new();

	// Countdown state tracking
	private bool _wasCountdownVisible = false;

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
		public float BestLapTime { get; set; } = 0f;
		public float BestRaceTime { get; set; } = 0f;
		public bool HasPlayerScores { get; set; } = false;
		public bool IsCurrentTrack { get; set; } = false;
	}

	/// <summary>
	/// Individual leaderboard entry for display
	/// </summary>
	public class LeaderboardEntry
	{
		public string TrackId { get; set; } = "";
		public string TrackName { get; set; } = "";
		public float Time { get; set; } = 0f;
		public string Type { get; set; } = ""; // "Best Lap" or "Best Race"
		public int Laps { get; set; } = 0;
		public bool IsCurrentPlayer { get; set; } = false;
		public DateTime SetDate { get; set; } = DateTime.UtcNow;
		public string Username { get; set; } = "";

		public string FormattedTime => Time > 0f ? $"{Time:F3}s" : "No time";
		public string CategoryDisplay => Type == "Best Lap" ? "Best Lap" : $"Best Race ({Laps} laps)";
	}

	/// <summary>
	/// Manages the tracks & leaderboard overlay UI
	/// </summary>
	private class TracksLeaderboardOverlay
	{
		// Overlay components
		public Control OverlayRoot { get; private set; }
		public Control TrackListPanel { get; private set; }
		public Control LeaderboardPanel { get; private set; }
		public VBoxContainer TrackButtonContainer { get; private set; }
		public VBoxContainer LeaderboardContainer { get; private set; }
		public Label TrackNameLabel { get; private set; }
		public Button LoadTrackButton { get; private set; }
		public Button CloseButton { get; private set; }
		public TabContainer LeaderboardTabs { get; private set; }
		
		// State
		public string SelectedTrackId { get; set; } = "";
		public bool IsVisible => OverlayRoot?.Visible ?? false;
		
		// Track button references
		private List<Button> _trackButtons = new();
		private Dictionary<string, Button> _trackButtonMap = new();

		/// <summary>
		/// Create the tracks & leaderboard overlay UI structure
		/// </summary>
		public void CreateOverlay(CanvasLayer uiLayer)
		{
			// Root overlay with semi-transparent background
			OverlayRoot = new Control();
			OverlayRoot.AnchorLeft = 0;
			OverlayRoot.AnchorTop = 0;
			OverlayRoot.AnchorRight = 1;
			OverlayRoot.AnchorBottom = 1;
			OverlayRoot.Visible = false;
			uiLayer.AddChild(OverlayRoot);

			// Semi-transparent background
			var background = new ColorRect();
			background.AnchorLeft = 0;
			background.AnchorTop = 0;
			background.AnchorRight = 1;
			background.AnchorBottom = 1;
			background.Color = new Color(0, 0, 0, 0.7f);
			OverlayRoot.AddChild(background);

			// Main content panel (80% of screen)
			var mainPanel = new Panel();
			mainPanel.AnchorLeft = 0.1f;
			mainPanel.AnchorTop = 0.3f;
			mainPanel.AnchorRight = 0.9f;
			mainPanel.AnchorBottom = 0.7f;
			OverlayRoot.AddChild(mainPanel);

			CreateOverlayContent(mainPanel);
		}

		private void CreateOverlayContent(Control parent)
		{
			var vbox = new VBoxContainer();
			vbox.AnchorLeft = 0;
			vbox.AnchorTop = 0;
			vbox.AnchorRight = 1;
			vbox.AnchorBottom = 1;
			parent.AddChild(vbox);

			// Header with title and close button
			CreateHeader(vbox);

			// Main content with two panels
			CreateMainContent(vbox);
		}

		private void CreateHeader(Container parent)
		{
			var header = new HBoxContainer();
			parent.AddChild(header);

			var titleLabel = new Label();
			titleLabel.Text = "Tracks & Leaderboard";
			titleLabel.AddThemeFontSizeOverride("font_size", 24);
			titleLabel.AddThemeColorOverride("font_color", Colors.White);
			titleLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			header.AddChild(titleLabel);

			CloseButton = new Button();
			CloseButton.Text = "✕";
			CloseButton.AddThemeFontSizeOverride("font_size", 20);
			header.AddChild(CloseButton);
		}

		private void CreateMainContent(Container parent)
		{
			var hbox = new HBoxContainer();
			hbox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
			parent.AddChild(hbox);

			// Left panel: Track list (30% width)
			CreateTrackListPanel(hbox);

			// Separator
			var separator = new VSeparator();
			hbox.AddChild(separator);

			// Right panel: Leaderboard (70% width)
			CreateLeaderboardPanel(hbox);
		}

		private void CreateTrackListPanel(Container parent)
		{
			TrackListPanel = new Panel();
			TrackListPanel.CustomMinimumSize = new Vector2(180, 0);
			TrackListPanel.SizeFlagsHorizontal = Control.SizeFlags.Fill;
			parent.AddChild(TrackListPanel);

			var vbox = new VBoxContainer();
			vbox.AnchorLeft = 0;
			vbox.AnchorTop = 0;
			vbox.AnchorRight = 1;
			vbox.AnchorBottom = 1;
			TrackListPanel.AddChild(vbox);

			var trackListTitle = new Label();
			trackListTitle.Text = "Select Track";
			trackListTitle.AddThemeFontSizeOverride("font_size", 18);
			trackListTitle.AddThemeColorOverride("font_color", Colors.White);
			trackListTitle.HorizontalAlignment = HorizontalAlignment.Center;
			vbox.AddChild(trackListTitle);

			// Scrollable track list
			var scrollContainer = new ScrollContainer();
			scrollContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
			vbox.AddChild(scrollContainer);

			TrackButtonContainer = new VBoxContainer();
			scrollContainer.AddChild(TrackButtonContainer);
		}

		private void CreateLeaderboardPanel(Container parent)
		{
			LeaderboardPanel = new Panel();
			LeaderboardPanel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			parent.AddChild(LeaderboardPanel);

			var vbox = new VBoxContainer();
			vbox.AnchorLeft = 0;
			vbox.AnchorTop = 0;
			vbox.AnchorRight = 1;
			vbox.AnchorBottom = 1;
			LeaderboardPanel.AddChild(vbox);

			// Track name and load button
			CreateLeaderboardHeader(vbox);

			// Tabbed leaderboard (Best Laps / Best Races)
			CreateLeaderboardTabs(vbox);
		}

		private void CreateLeaderboardHeader(Container parent)
		{
			var header = new HBoxContainer();
			parent.AddChild(header);

			TrackNameLabel = new Label();
			TrackNameLabel.Text = "Select a track";
			TrackNameLabel.AddThemeFontSizeOverride("font_size", 20);
			TrackNameLabel.AddThemeColorOverride("font_color", Colors.White);
			TrackNameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			header.AddChild(TrackNameLabel);

			LoadTrackButton = new Button();
			LoadTrackButton.Text = "Load Track";
			LoadTrackButton.Disabled = true;
			header.AddChild(LoadTrackButton);
		}

		private void CreateLeaderboardTabs(Container parent)
		{
			LeaderboardTabs = new TabContainer();
			LeaderboardTabs.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
			parent.AddChild(LeaderboardTabs);

			// Best Laps tab
			var lapTimesScroll = new ScrollContainer();
			lapTimesScroll.Name = "Best Laps";
			lapTimesScroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			lapTimesScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
			LeaderboardTabs.AddChild(lapTimesScroll);

			var lapTimesContainer = new VBoxContainer();
			lapTimesContainer.Name = "LapTimesContainer";
			lapTimesContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			lapTimesContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
			lapTimesScroll.AddChild(lapTimesContainer);

			// Best Races tab
			var raceTimesScroll = new ScrollContainer();
			raceTimesScroll.Name = "Best Races";
			raceTimesScroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			raceTimesScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
			LeaderboardTabs.AddChild(raceTimesScroll);

			var raceTimesContainer = new VBoxContainer();
			raceTimesContainer.Name = "RaceTimesContainer";
			raceTimesContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			raceTimesContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
			raceTimesScroll.AddChild(raceTimesContainer);
		}

		public void AddTrackButton(string trackId, string trackName, bool isCurrent, bool hasScores)
		{
			var trackButton = new Button();
			trackButton.Text = trackName;
			trackButton.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			
			// Style button based on state
			if (isCurrent)
			{
				trackButton.AddThemeColorOverride("font_color", Colors.Yellow);
				trackButton.Text += " (Current)";
			}
			else if (hasScores)
			{
				trackButton.AddThemeColorOverride("font_color", Colors.LightBlue);
			}
			
			// Store reference
			_trackButtons.Add(trackButton);
			_trackButtonMap[trackId] = trackButton;
			TrackButtonContainer.AddChild(trackButton);
		}

		public Button GetTrackButton(string trackId)
		{
			return _trackButtonMap.GetValueOrDefault(trackId);
		}

		public void ClearTrackButtons()
		{
			foreach (var button in _trackButtons)
			{
				button?.QueueFree();
			}
			_trackButtons.Clear();
			_trackButtonMap.Clear();
		}

		public void UpdateSelectedTrack(string trackId, string trackName, bool canLoad)
		{
			SelectedTrackId = trackId;
			TrackNameLabel.Text = trackName;
			LoadTrackButton.Disabled = !canLoad;
		}

		public void UpdateLeaderboard(List<LeaderboardEntry> lapTimes, List<LeaderboardEntry> raceTimes)
		{
			UpdateLeaderboardTab("LapTimesContainer", lapTimes);
			UpdateLeaderboardTab("RaceTimesContainer", raceTimes);
		}

		private void UpdateLeaderboardTab(string containerName, List<LeaderboardEntry> entries)
		{
			var container = LeaderboardTabs.FindChild(containerName, true, false) as VBoxContainer;
			if (container == null) return;

			// Clear existing entries
			foreach (Node child in container.GetChildren())
			{
				child.QueueFree();
			}

			if (entries.Count == 0)
			{
				var noDataLabel = new Label();
				noDataLabel.Text = "No times recorded for this track";
				noDataLabel.HorizontalAlignment = HorizontalAlignment.Center;
				noDataLabel.AddThemeColorOverride("font_color", Colors.Gray);
				container.AddChild(noDataLabel);
				return;
			}

			// Create and configure DataTableView
			var tableView = new DataTableView();
			tableView.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			tableView.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
			tableView.CustomMinimumSize = new Vector2(380, 250); // Increased from 320 to utilize extra space from narrower track list

			// Configure table appearance
			tableView.AlternatingRowColors = true;
			tableView.ShowHeaders = true;
			tableView.HeaderBackgroundColor = new Color(0.2f, 0.2f, 0.3f, 1.0f);
			tableView.HeaderTextColor = Colors.White;
			tableView.RowBackgroundColor1 = new Color(0.1f, 0.1f, 0.15f, 0.8f);
			tableView.RowBackgroundColor2 = new Color(0.15f, 0.15f, 0.2f, 0.8f);
			tableView.RowTextColor = Colors.White;
			tableView.HighlightRowColor = new Color(0.9f, 0.7f, 0.0f, 0.5f); // More visible golden yellow

			// Add columns (removed Type column as it's self-evident from tab selection)
			tableView.AddColumn("Rank", (obj) =>
				{
					var entry = (LeaderboardEntry)obj;
					return (entries.IndexOf(entry) + 1).ToString();
				}, 60, HorizontalAlignment.Center)
					 .AddColumn("Player", (obj) => ((LeaderboardEntry)obj).Username, 150, HorizontalAlignment.Left) // Increased to 150px to utilize extra space
					 .AddColumn("Time", (obj) => ((LeaderboardEntry)obj).FormattedTime, 100, HorizontalAlignment.Right, true); // Reduced from 120px to 100px

			// Set highlight predicate only if we have entries that belong to current player
			bool hasCurrentPlayerEntries = entries.Any(entry => entry.IsCurrentPlayer);
			if (hasCurrentPlayerEntries)
			{
				tableView.SetHighlightPredicate(obj => ((LeaderboardEntry)obj).IsCurrentPlayer);
			}

			// Bind data (limit to top 10)
			var displayEntries = entries.Take(10).Cast<object>().ToList();
			tableView.BindData(displayEntries);

			container.AddChild(tableView);
		}

		public void ShowOverlay()
		{
			if (OverlayRoot != null)
				OverlayRoot.Visible = true;
		}

		public void HideOverlay()
		{
			if (OverlayRoot != null)
				OverlayRoot.Visible = false;
		}
	}

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
	/// Setup the status bar with modern arc-based HUD elements
	/// </summary>
	/// <param name="parent">Parent container</param>
	private void SetupStatusBar(Control parent)
	{
		// Create arc-based HUD renderer
		_hudArcRenderer = new RacingHUDArcRenderer();
		_uiLayer.AddChild(_hudArcRenderer);

		// Create invisible status bar elements for compatibility (keep references for code that updates them)
		var statusBar = new HBoxContainer();
		statusBar.Visible = false; // Hide completely - arc renderer provides all visual feedback
		parent.AddChild(statusBar);

		// Create labels but keep them hidden (for compatibility with existing update code)
		_speedLabel = new Label() { Text = "Speed: 0" };
		_lapLabel = new Label() { Text = "Practice Mode" };
		_timeLabel = new Label() { Text = "Gap: 0.0s" };

		// Don't add them to the UI - just keep references
		// statusBar.AddChild(_speedLabel);
		// statusBar.AddChild(_lapLabel);
		// statusBar.AddChild(_timeLabel);
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
	public void UpdateStatusLabels(float carSpeed, GameController.GameMode gameMode, int currentLap, int targetLaps, float timeDisplay, string timeLabel, float maxSpeed = 1800.0f, float lapProgress = 0.0f)
	{
		// Update arc-based HUD (primary display)
		if (_hudArcRenderer != null && IsInstanceValid(_hudArcRenderer))
		{
			var timeText = $"{timeLabel}: {timeDisplay:F1}s";
			var isPracticeMode = (gameMode == GameController.GameMode.Practice);
			_hudArcRenderer.UpdateHUDState(carSpeed, maxSpeed, currentLap, targetLaps, lapProgress, timeText, isPracticeMode);
		}

		// Update traditional labels (fallback/debug display)
		if (_speedLabel != null)
		{
			_speedLabel.Text = $"Speed: {(int)carSpeed}";
		}

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
						  state.TargetLaps, state.TimeDisplay, state.TimeLabel, state.MaxSpeed, state.LapProgress);

		UpdateButtonStates(state.IsTimeTrialInProgress, state.CanStartTimeTrial, state.IsInCountdown,
						  state.GameMode == GameController.GameMode.TimeTrial, state.IsUserLoggedIn);

		SetPauseOverlayVisible(state.IsGamePaused);
		SetGameOverOverlayVisible(state.ShowGameOverOverlay, state.FinalTime, state.CanAffordReplay);

		// Update countdown display in arc renderer
		UpdateCountdownArc(state.IsInCountdown, state.CountdownNumber, state.CountdownProgress);
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
	/// Show/hide countdown overlay (traditional) and update arc renderer
	/// </summary>
	/// <param name="visible">Whether to show the overlay</param>
	public void SetCountdownOverlayVisible(bool visible)
	{
		if (_countdownOverlay != null)
		{
			// Hide traditional overlay completely - arc renderer handles countdown
			_countdownOverlay.Visible = false;
		}

		// Arc renderer handles the primary countdown display
		if (_hudArcRenderer != null && IsInstanceValid(_hudArcRenderer))
		{
			if (!visible)
			{
				_hudArcRenderer.HideCountdown();
			}
		}
	}

	/// <summary>
	/// Update countdown text (traditional) and arc animation
	/// </summary>
	/// <param name="text">Text to display</param>
	public void UpdateCountdownText(string text)
	{
		if (_countdownLabel != null)
		{
			_countdownLabel.Text = text;
		}
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
		UpdateTracksLeaderboardButton(false, false);
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
			_tracksLeaderboardOverlay = new TracksLeaderboardOverlay();
			_tracksLeaderboardOverlay.CreateOverlay(_uiLayer);
			
			// Connect signals
			_tracksLeaderboardOverlay.CloseButton.Pressed += HideTracksLeaderboard;
			_tracksLeaderboardOverlay.LoadTrackButton.Pressed += OnLoadTrackFromOverlay;
		}

		// Update track metadata cache and populate track list
		_trackMetadataCache = trackMetadata;
		PopulateTrackList(currentTrackId);
	}

	/// <summary>
	/// Show the tracks & leaderboard overlay
	/// </summary>
	public void ShowTracksLeaderboard()
	{
		if (_tracksLeaderboardOverlay == null)
			return;

		// Reset idle timer when opening premium UI
		ResetUserIdleTimer();
		
		_tracksLeaderboardOverlay.ShowOverlay();
		EmitSignal(SignalName.TracksLeaderboardRequested);
	}

	/// <summary>
	/// Hide the tracks & leaderboard overlay
	/// </summary>
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

	/// <summary>
	/// Populate track list in the overlay
	/// </summary>
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

	/// <summary>
	/// Handle track selection in overlay
	/// </summary>
	private void OnTrackSelected(string trackId)
	{
		if (!_trackMetadataCache.TryGetValue(trackId, out var track))
			return;

		bool canLoad = !track.IsCurrentTrack;
		_tracksLeaderboardOverlay?.UpdateSelectedTrack(trackId, track.TrackName, canLoad);
		UpdateLeaderboardForTrack(trackId);
	}

	/// <summary>
	/// Handle load track request from overlay
	/// </summary>
	private void OnLoadTrackFromOverlay()
	{
		if (_tracksLeaderboardOverlay == null || string.IsNullOrEmpty(_tracksLeaderboardOverlay.SelectedTrackId))
			return;

		ResetUserIdleTimer();
		EmitSignal(SignalName.TrackLoadFromOverlayRequested, _tracksLeaderboardOverlay.SelectedTrackId);
		HideTracksLeaderboard();
	}

	/// <summary>
	/// Update leaderboard display for a specific track with DataStore integration
	/// </summary>
	private void UpdateLeaderboardForTrack(string trackId)
	{
		if (_tracksLeaderboardOverlay == null || !_trackMetadataCache.TryGetValue(trackId, out var track))
			return;

		// Load leaderboard data asynchronously
		LoadLeaderboardDataAsync(trackId);
	}

	/// <summary>
	/// Load leaderboard data from DataStore for a specific track
	/// </summary>
	private async void LoadLeaderboardDataAsync(string trackId)
	{
		try
		{
			// Get current player's phone number from SessionManager (real backend identifier)
			var playerId = GetCurrentPlayerPhoneNumber();

			// If no user is logged in, show empty leaderboard
			if (playerId == null)
			{
				UpdateLeaderboardFallback(trackId);
				return;
			}

			var dataStore = DataStore.GetInstance();
			if (dataStore == null)
			{
				UpdateLeaderboardFallback(trackId);
				return;
			}

			var globalDataResult = await dataStore.GetGlobalDataAsync(playerId);
			
			// Check if overlay is still valid after await
			if (_tracksLeaderboardOverlay == null || !IsInstanceValid(this))
				return;
				
			if (!globalDataResult.IsSuccess)
			{
				UpdateLeaderboardFallback(trackId);
				return;
			}
			
			var racingData = globalDataResult.Value.Racing;
			var lapEntries = new List<LeaderboardEntry>();
			var raceEntries = new List<LeaderboardEntry>();

			// Extract track-specific times using both new and legacy key formats
			// Pass the username from global data as fallback
			string fallbackUsername = globalDataResult.Value.UserName;
			ExtractTrackLeaderboardEntries(racingData, trackId, playerId, lapEntries, raceEntries, fallbackUsername);
			
			// Update UI on main thread
			var callable = Callable.From(() => UpdateLeaderboardUI(trackId, lapEntries, raceEntries));
			callable.CallDeferred();
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"[RacingUIManager] Exception loading leaderboard: {ex.Message}");
			var fallbackCallable = Callable.From(() => UpdateLeaderboardFallback(trackId));
			fallbackCallable.CallDeferred();
		}
	}

	/// <summary>
	/// Extract leaderboard entries for a specific track from racing data
	/// </summary>
	private void ExtractTrackLeaderboardEntries(RacingGlobalDataStore racingData, string trackId, string currentPlayerId,
		List<LeaderboardEntry> lapEntries, List<LeaderboardEntry> raceEntries, string fallbackUsername = "")
	{
		if (!_trackMetadataCache.TryGetValue(trackId, out var track))
			return;

		// Get current session username as additional fallback
		string sessionUsername = GetCurrentSessionUsername();

		// Get best lap time entry with username
		var bestLapEntry = racingData.GetBestLapTimeEntry(trackId);

		if (bestLapEntry != null && bestLapEntry.Time > 0f)
		{
			// Enhanced username fallback logic
			string displayName = ResolveDisplayName(bestLapEntry.Username, fallbackUsername, sessionUsername, currentPlayerId);

			// Determine if this entry belongs to the current player
			bool isCurrentPlayer = IsEntryForCurrentPlayer(displayName, bestLapEntry.Username, currentPlayerId, fallbackUsername, sessionUsername);

			lapEntries.Add(new LeaderboardEntry
			{
				TrackId = trackId,
				TrackName = track.TrackName,
				Time = bestLapEntry.Time,
				Type = "Best Lap",
				Username = displayName,
				IsCurrentPlayer = isCurrentPlayer
			});
		}

		// Get best race times for different lap counts
		for (int laps = 1; laps <= 10; laps++) // Support up to 10 lap races
		{
			var bestRaceEntry = racingData.GetBestRaceTimeEntry(trackId, laps);

			if (bestRaceEntry != null && bestRaceEntry.Time > 0f)
			{
				// Enhanced username fallback logic
				string displayName = ResolveDisplayName(bestRaceEntry.Username, fallbackUsername, sessionUsername, currentPlayerId);

				// Determine if this entry belongs to the current player
				bool isCurrentPlayer = IsEntryForCurrentPlayer(displayName, bestRaceEntry.Username, currentPlayerId, fallbackUsername, sessionUsername);

				raceEntries.Add(new LeaderboardEntry
				{
					TrackId = trackId,
					TrackName = track.TrackName,
					Time = bestRaceEntry.Time,
					Type = "Best Race",
					Laps = laps,
					Username = displayName,
					IsCurrentPlayer = isCurrentPlayer
				});
			}
		}

		// Sort entries by time (fastest first)
		lapEntries.Sort((a, b) => a.Time.CompareTo(b.Time));
		raceEntries.Sort((a, b) => a.Time.CompareTo(b.Time));
	}

	/// <summary>
	/// Resolve display name with enhanced fallback logic
	/// </summary>
	private string ResolveDisplayName(string storedUsername, string fallbackUsername, string sessionUsername, string currentPlayerId)
	{
		// Priority order:
		// 1. Stored username from the time entry
		// 2. Fallback username from global data
		// 3. Current session username (from UserManager/SessionManager)
		// 4. Player ID (if not default "player1")
		// 5. Empty string if no username available

		if (!string.IsNullOrEmpty(storedUsername))
			return storedUsername;

		if (!string.IsNullOrEmpty(fallbackUsername))
			return fallbackUsername;

		if (!string.IsNullOrEmpty(sessionUsername))
			return sessionUsername;

		if (!string.IsNullOrEmpty(currentPlayerId) && currentPlayerId != "player1")
			return currentPlayerId;

		return string.Empty; // No fallback - display empty if no username available
	}

	/// <summary>
	/// Check if we have a valid logged-in player for highlighting purposes
	/// </summary>
	private bool HasValidCurrentPlayer(string currentPlayerId, string fallbackUsername, string sessionUsername)
	{
		// Consider valid if we have a non-default player ID or any username
		if (!string.IsNullOrEmpty(currentPlayerId) && currentPlayerId != "player1")
			return true;

		if (!string.IsNullOrEmpty(fallbackUsername))
			return true;

		if (!string.IsNullOrEmpty(sessionUsername))
			return true;

		return false;
	}

	/// <summary>
	/// Determine if a leaderboard entry belongs to the current player
	/// </summary>
	private bool IsEntryForCurrentPlayer(string entryUsername, string storedUsername, string currentPlayerId, string fallbackUsername, string sessionUsername)
	{
		// If we don't have a valid current player, no entries belong to current player
		if (!HasValidCurrentPlayer(currentPlayerId, fallbackUsername, sessionUsername))
			return false;

		// Check if the entry username matches any of the current player's identifiers
		if (!string.IsNullOrEmpty(entryUsername))
		{
			// Compare with session username
			if (!string.IsNullOrEmpty(sessionUsername) && entryUsername == sessionUsername)
				return true;

			// Compare with fallback username
			if (!string.IsNullOrEmpty(fallbackUsername) && entryUsername == fallbackUsername)
				return true;

			// Compare with player ID (if not default)
			if (!string.IsNullOrEmpty(currentPlayerId) && currentPlayerId != "player1" && entryUsername == currentPlayerId)
				return true;
		}

		// Also check stored username from the time entry itself
		if (!string.IsNullOrEmpty(storedUsername))
		{
			if (!string.IsNullOrEmpty(sessionUsername) && storedUsername == sessionUsername)
				return true;

			if (!string.IsNullOrEmpty(fallbackUsername) && storedUsername == fallbackUsername)
				return true;

			if (!string.IsNullOrEmpty(currentPlayerId) && currentPlayerId != "player1" && storedUsername == currentPlayerId)
				return true;
		}

		return false;
	}

	/// <summary>
	/// Get current session username from UserManager/SessionManager
	/// </summary>
	private string GetCurrentSessionUsername()
	{
		try
		{
			// Try SessionManager first - get the current user session and extract username
			var sessionManager = SessionManager.GetInstance();
			if (sessionManager != null && GodotObject.IsInstanceValid(sessionManager))
			{
				var currentSession = sessionManager.GetCurrentUserSession();
				if (currentSession != null)
				{
					// First, try to get username from cached GlobalData if available
					if (currentSession.GlobalData != null && !string.IsNullOrEmpty(currentSession.GlobalData.UserName))
					{
						return currentSession.GlobalData.UserName;
					}

					// Privacy-safe fallback - never expose phone numbers in display
					// Use generic player identifier instead
				}
			}
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"[RacingUIManager] Error getting session username: {ex.Message}");
		}

		return "Player"; // Generic fallback that doesn't expose private information
	}

	/// <summary>
	/// Update leaderboard UI on main thread
	/// </summary>
	private void UpdateLeaderboardUI(string trackId, List<LeaderboardEntry> lapEntries, List<LeaderboardEntry> raceEntries)
	{
		// Only update if this track is still selected
		if (_tracksLeaderboardOverlay?.SelectedTrackId == trackId)
		{
			_tracksLeaderboardOverlay.UpdateLeaderboard(lapEntries, raceEntries);
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
	/// Get the current player's phone number from SessionManager (null if not logged in)
	/// </summary>
	private string GetCurrentPlayerPhoneNumber()
	{
		try
		{
			var sessionManager = SessionManager.GetInstance();
			if (sessionManager != null && GodotObject.IsInstanceValid(sessionManager))
			{
				var currentSession = sessionManager.GetCurrentUserSession();
				if (currentSession != null)
				{
					return currentSession.PhoneNumber;
				}
			}
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"[RacingUIManager] Error getting current player phone number: {ex.Message}");
		}

		// Return null if no user is logged in - no fallbacks
		return null;
	}

	/// <summary>
	/// Enhanced track metadata update with DataStore-powered leaderboard data
	/// </summary>
	public async void UpdateTrackMetadataWithLeaderboard(Dictionary<string, TrackMetadata> baseTrackMetadata, string currentTrackId, string playerId)
	{
		_currentPlayerId = playerId;
		
		// Get enhanced metadata with leaderboard data
		var enhancedMetadata = await EnhanceTrackMetadataWithScores(baseTrackMetadata, playerId);
		
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

	/// <summary>
	/// Enhance track metadata with player's best scores from DataStore
	/// </summary>
	private async Task<Dictionary<string, TrackMetadata>> EnhanceTrackMetadataWithScores(Dictionary<string, TrackMetadata> baseMetadata, string playerId)
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

		try
		{
			var dataStore = DataStore.GetInstance();
			if (dataStore == null)
				return enhancedMetadata;

			var globalDataResult = await dataStore.GetGlobalDataAsync(playerId);
			if (!globalDataResult.IsSuccess)
				return enhancedMetadata;

			var racingData = globalDataResult.Value.Racing;
			
			// Enhance each track with score data
			foreach (var track in enhancedMetadata.Values)
			{
				// Get best lap time
				track.BestLapTime = racingData.GetBestLapTime(track.TrackId);
				
				// Get best race time (use default laps)
				track.BestRaceTime = racingData.GetBestRaceTime(track.TrackId, track.DefaultLaps);
				
				// Mark if player has scores
				track.HasPlayerScores = track.BestLapTime > 0f || track.BestRaceTime > 0f;
			}
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"[RacingUIManager] Exception enhancing track metadata: {ex.Message}");
		}

		return enhancedMetadata;
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
}
}