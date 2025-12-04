using Godot;
using System.Collections.Generic;
using System.Linq;
using BarBox.Core.UI;

namespace BarBox.Games.Racing;

/// <summary>
/// Manages the race complete overlay UI with high scores and action buttons.
/// Displayed when a player completes a race, showing their time, position, and leaderboard.
/// </summary>
public class RacingRaceCompleteUI
{
	// Overlay components
	public Control OverlayRoot { get; private set; }
	public VBoxContainer ContentContainer { get; private set; }
	public Label RaceCompleteLabel { get; private set; }
	public Label FinalTimeLabel { get; private set; }
	public Label PositionLabel { get; private set; }
	public Button TryAgainButton { get; private set; }
	public Button AddCreditsButton { get; private set; }
	public Button ReturnToPracticeButton { get; private set; }
	public DataTableView HighScoresTable { get; private set; }

	// State
	public bool IsVisible => OverlayRoot?.Visible ?? false;

	public void CreateOverlay(CanvasLayer uiLayer)
	{
		OverlayRoot = new Control();
		OverlayRoot.AnchorLeft = 0;
		OverlayRoot.AnchorTop = 0;
		OverlayRoot.AnchorRight = 1;
		OverlayRoot.AnchorBottom = 1;
		OverlayRoot.Visible = false;
		uiLayer.AddChild(OverlayRoot);

		var background = new ColorRect();
		background.AnchorLeft = 0;
		background.AnchorTop = 0;
		background.AnchorRight = 1;
		background.AnchorBottom = 1;
		background.Color = new Color(0, 0, 0, 0.8f);
		OverlayRoot.AddChild(background);

		var mainPanel = new Panel();
		mainPanel.AnchorLeft = 0.5f;
		mainPanel.AnchorTop = 0.5f;
		mainPanel.AnchorRight = 0.5f;
		mainPanel.AnchorBottom = 0.5f;
		mainPanel.OffsetLeft = -300;
		mainPanel.OffsetTop = -250;
		mainPanel.OffsetRight = 300;
		mainPanel.OffsetBottom = 250;
		OverlayRoot.AddChild(mainPanel);

		CreateOverlayContent(mainPanel);
	}

	private void CreateOverlayContent(Control parent)
	{
		ContentContainer = new VBoxContainer();
		ContentContainer.AnchorLeft = 0;
		ContentContainer.AnchorTop = 0;
		ContentContainer.AnchorRight = 1;
		ContentContainer.AnchorBottom = 1;
		ContentContainer.AddThemeConstantOverride("separation", 15);
		parent.AddChild(ContentContainer);

		CreateHeader();
		CreateHighScoresSection();
		CreateButtonSection();
	}

	private void CreateHeader()
	{
		RaceCompleteLabel = new Label();
		RaceCompleteLabel.Text = "RACE COMPLETE!";
		RaceCompleteLabel.HorizontalAlignment = HorizontalAlignment.Center;
		RaceCompleteLabel.AddThemeFontSizeOverride("font_size", 28);
		RaceCompleteLabel.AddThemeColorOverride("font_color", Colors.Gold);
		ContentContainer.AddChild(RaceCompleteLabel);

		FinalTimeLabel = new Label();
		FinalTimeLabel.Text = "Final Time: 0.0s";
		FinalTimeLabel.HorizontalAlignment = HorizontalAlignment.Center;
		FinalTimeLabel.AddThemeFontSizeOverride("font_size", 20);
		FinalTimeLabel.AddThemeColorOverride("font_color", Colors.White);
		ContentContainer.AddChild(FinalTimeLabel);

		PositionLabel = new Label();
		PositionLabel.Text = "Position: --";
		PositionLabel.HorizontalAlignment = HorizontalAlignment.Center;
		PositionLabel.AddThemeFontSizeOverride("font_size", 18);
		PositionLabel.AddThemeColorOverride("font_color", Colors.LightBlue);
		ContentContainer.AddChild(PositionLabel);
	}

	private void CreateHighScoresSection()
	{
		var highScoresTitle = new Label();
		highScoresTitle.Text = "Track High Scores";
		highScoresTitle.HorizontalAlignment = HorizontalAlignment.Center;
		highScoresTitle.AddThemeFontSizeOverride("font_size", 16);
		highScoresTitle.AddThemeColorOverride("font_color", Colors.Yellow);
		ContentContainer.AddChild(highScoresTitle);

		HighScoresTable = new DataTableView();
		HighScoresTable.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		HighScoresTable.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		HighScoresTable.CustomMinimumSize = new Vector2(550, 200);

		HighScoresTable.AlternatingRowColors = true;
		HighScoresTable.ShowHeaders = true;
		HighScoresTable.HeaderBackgroundColor = new Color(0.2f, 0.2f, 0.3f, 1.0f);
		HighScoresTable.HeaderTextColor = Colors.White;
		HighScoresTable.RowBackgroundColor1 = new Color(0.1f, 0.1f, 0.15f, 0.8f);
		HighScoresTable.RowBackgroundColor2 = new Color(0.15f, 0.15f, 0.2f, 0.8f);
		HighScoresTable.RowTextColor = Colors.White;
		HighScoresTable.HighlightRowColor = new Color(0.9f, 0.7f, 0.0f, 0.6f);

		HighScoresTable.AddColumn("Rank", (obj) =>
		{
			var entry = (RacingUIManager.LeaderboardEntry)obj;
			return entry.DisplayRank;
		}, 60, HorizontalAlignment.Center)
		.AddColumn("Player", (obj) => ((RacingUIManager.LeaderboardEntry)obj).Username, 200, HorizontalAlignment.Left)
		.AddColumn("Time", (obj) => ((RacingUIManager.LeaderboardEntry)obj).FormattedTime, 120, HorizontalAlignment.Right, true);

		ContentContainer.AddChild(HighScoresTable);
	}

	private void CreateButtonSection()
	{
		var buttonContainer = new HBoxContainer();
		buttonContainer.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		buttonContainer.AddThemeConstantOverride("separation", 20);
		ContentContainer.AddChild(buttonContainer);

		TryAgainButton = new Button();
		TryAgainButton.Text = "Try Again - 1 Credit";
		TryAgainButton.CustomMinimumSize = new Vector2(150, 40);
		buttonContainer.AddChild(TryAgainButton);

		AddCreditsButton = new Button();
		AddCreditsButton.Text = "Add Credits";
		AddCreditsButton.CustomMinimumSize = new Vector2(120, 40);
		buttonContainer.AddChild(AddCreditsButton);

		ReturnToPracticeButton = new Button();
		ReturnToPracticeButton.Text = "Return to Practice";
		ReturnToPracticeButton.CustomMinimumSize = new Vector2(150, 40);
		buttonContainer.AddChild(ReturnToPracticeButton);
	}

	public void UpdateRaceResults(float finalTime, string trackName, int position, bool isNewHighScore)
	{
		if (FinalTimeLabel != null)
		{
			FinalTimeLabel.Text = $"Final Time: {finalTime:F2}s";
		}

		if (PositionLabel != null)
		{
			if (position > 0)
			{
				var positionSuffix = GetPositionSuffix(position);
				PositionLabel.Text = $"Position: {position}{positionSuffix}";

				if (isNewHighScore)
				{
					PositionLabel.Text += " NEW HIGH SCORE!";
					PositionLabel.AddThemeColorOverride("font_color", Colors.Gold);
				}
			}
			else
			{
				PositionLabel.Text = "Position: First recorded time!";
				PositionLabel.AddThemeColorOverride("font_color", Colors.Gold);
			}
		}

		var highScoresTitle = ContentContainer.GetChild(3) as Label;
		if (highScoresTitle != null)
		{
			highScoresTitle.Text = $"{trackName} - High Scores";
		}
	}

	private static string GetPositionSuffix(int position)
	{
		return position switch
		{
			1 => "st",
			2 => "nd",
			3 => "rd",
			_ => "th"
		};
	}

	public void UpdateHighScores(List<RacingUIManager.LeaderboardEntry> highScores, string currentPlayerPhoneNumber)
	{
		if (HighScoresTable == null || highScores == null)
			return;

		for (int i = 0; i < highScores.Count; i++)
		{
			var entry = highScores[i];
			entry.DisplayRank = (i + 1).ToString();

			entry.IsCurrentPlayer = !string.IsNullOrEmpty(currentPlayerPhoneNumber) &&
				entry.Username == currentPlayerPhoneNumber;
		}

		HighScoresTable.SetHighlightPredicate(obj => ((RacingUIManager.LeaderboardEntry)obj).IsCurrentPlayer);

		var displayEntries = highScores.Take(8).Cast<object>().ToList();
		HighScoresTable.BindData(displayEntries);
	}

	public void UpdateButtonStates(bool canAffordTryAgain, int creditCost)
	{
		if (TryAgainButton != null)
		{
			TryAgainButton.Disabled = !canAffordTryAgain;
			if (creditCost > 0)
			{
				TryAgainButton.Text = $"Try Again - {creditCost} Credit{(creditCost != 1 ? "s" : "")}";
			}
			else
			{
				TryAgainButton.Text = "Try Again";
			}
		}
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
