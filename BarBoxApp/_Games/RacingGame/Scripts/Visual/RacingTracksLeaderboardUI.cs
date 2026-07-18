using System.Collections.Generic;
using System.Linq;
using BarBox.Core.Drawing;
using BarBox.Core.UI;
using Godot;

namespace BarBox.Games.Racing;

/// <summary>
/// Manages the tracks and leaderboard overlay UI.
/// Displays a list of tracks and their leaderboard data.
/// </summary>
public class RacingTracksLeaderboardUI
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
	public string SelectedTrackId { get; set; } = string.Empty;

	public bool IsVisible => OverlayRoot?.Visible ?? false;

	// Track button references
	private List<Button> _trackButtons = [];
	private Dictionary<string, Button> _trackButtonMap = [];

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
		background.Color = RacingPalette.ScrimLight;
		OverlayRoot.AddChild(background);

		// Main content panel (80% of screen)
		var mainPanel = new Panel();
		mainPanel.AnchorLeft = 0.1f;
		mainPanel.AnchorTop = 0.3f;
		mainPanel.AnchorRight = 0.9f;
		mainPanel.AnchorBottom = 0.7f;
		mainPanel.AddThemeStyleboxOverride("panel", UiTheme.ModalBox());
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

		CreateHeader(vbox);
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
		UiTheme.ApplyOutlineButton(CloseButton, Palette.EdgeGray);
		header.AddChild(CloseButton);
	}

	private void CreateMainContent(Container parent)
	{
		var hbox = new HBoxContainer();
		hbox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		parent.AddChild(hbox);

		CreateTrackListPanel(hbox);

		var separator = new VSeparator();
		hbox.AddChild(separator);

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

		CreateLeaderboardHeader(vbox);
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
		UiTheme.ApplyOutlineButton(LoadTrackButton, Palette.EdgeGray);
		header.AddChild(LoadTrackButton);
	}

	private void CreateLeaderboardTabs(Container parent)
	{
		LeaderboardTabs = new TabContainer();
		LeaderboardTabs.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		parent.AddChild(LeaderboardTabs);

		var raceTimesScroll = new ScrollContainer();
		raceTimesScroll.Name = "Race Times";
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

		if (isCurrent)
		{
			trackButton.AddThemeColorOverride("font_color", RacingPalette.CurrentTrackAccent);
			trackButton.Text += " (Current)";
		}
		else if (hasScores)
		{
			trackButton.AddThemeColorOverride("font_color", RacingPalette.HasScoresAccent);
		}

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

	public void UpdateLeaderboard(List<RacingUIManager.LeaderboardEntry> lapTimes, List<RacingUIManager.LeaderboardEntry> raceTimes)
	{
		UpdateLeaderboardTab("LapTimesContainer", lapTimes);
		UpdateLeaderboardTab("RaceTimesContainer", raceTimes);
	}

	private void UpdateLeaderboardTab(string containerName, List<RacingUIManager.LeaderboardEntry> entries)
	{
		var container = LeaderboardTabs.FindChild(containerName, true, false) as VBoxContainer;
		if (container == null)
		{
			return;
		}

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

		var tableView = new DataTableView();
		tableView.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		tableView.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		tableView.CustomMinimumSize = new Vector2(380, 250);

		tableView.AlternatingRowColors = true;
		tableView.ShowHeaders = true;
		tableView.HeaderBackgroundColor = RacingPalette.TableHeaderBg;
		tableView.HeaderTextColor = Colors.White;
		tableView.RowBackgroundColor1 = RacingPalette.TableRowBg1;
		tableView.RowBackgroundColor2 = RacingPalette.TableRowBg2;
		tableView.RowTextColor = Colors.White;
		tableView.HighlightRowColor = RacingPalette.TableHighlight;

		tableView.AddColumn("Rank", (obj) =>
			{
				var entry = (RacingUIManager.LeaderboardEntry)obj;
				return (entries.IndexOf(entry) + 1).ToString();
			}, 60, HorizontalAlignment.Center)
			.AddColumn(new TableColumnDefinition("Player", (obj) => ((RacingUIManager.LeaderboardEntry)obj).Username, 150, HorizontalAlignment.Left)
			{
				CellStyler = (label, obj) => label.AddThemeColorOverride("font_color", RacingPalette.ColorForUsername(((RacingUIManager.LeaderboardEntry)obj).Username)),
			})
			.AddColumn("Time", (obj) => ((RacingUIManager.LeaderboardEntry)obj).FormattedTime, 100, HorizontalAlignment.Right, true);

		bool hasCurrentPlayerEntries = entries.Any(entry => entry.IsCurrentPlayer);
		if (hasCurrentPlayerEntries)
		{
			tableView.SetHighlightPredicate(obj => ((RacingUIManager.LeaderboardEntry)obj).IsCurrentPlayer);
		}

		var displayEntries = entries.Take(10).Cast<object>().ToList();
		tableView.BindData(displayEntries);

		container.AddChild(tableView);
	}

	public void ShowOverlay()
	{
		if (OverlayRoot != null)
		{
			OverlayRoot.Visible = true;
		}
	}

	public void HideOverlay()
	{
		if (OverlayRoot != null)
		{
			OverlayRoot.Visible = false;
		}
	}
}
