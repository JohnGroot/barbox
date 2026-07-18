using System;
using BarBox.Core.Autoloads;
using Godot;

namespace BarBox.Games.Nines;

/// <summary>
/// Complete UI system for Nines card game.
/// Uses nested helper classes for different UI panels.
/// </summary>
public partial class NinesUI : CanvasLayer
{
	#region References

	private NinesGame _game;
	private NinesGameConfig _config;
	private float _scaleFactor = 1.0f;

	// UI Panels
	private MainMenuPanel _mainMenu;
	private PlayerListPanel _playerList;
	private PredictionPopup _predictionPopup;
	private ResultsModal _resultsModal;
	private GameHUD _gameHUD;
	private ConfirmationPopup _confirmationPopup;

	#endregion

	#region Initialization

	public void Initialize(NinesGame game)
	{
		_game = game;
		_config = game.Config;

		// Calculate scale factor based on viewport
		var viewportSize = GetViewport().GetVisibleRect().Size;
		_scaleFactor = _config.GetScaleFactor(viewportSize);
		GD.Print($"[Nines UI] Scale factor: {_scaleFactor}");

		CreateUI();
	}

	private void CreateUI()
	{
		// Main menu panel
		_mainMenu = new MainMenuPanel(this, _scaleFactor);
		AddChild(_mainMenu);

		// Player list panel (right side)
		_playerList = new PlayerListPanel(this, _scaleFactor);
		AddChild(_playerList);

		// Prediction popup (hidden by default)
		_predictionPopup = new PredictionPopup(this, _scaleFactor);
		_predictionPopup.Visible = false;
		AddChild(_predictionPopup);

		// Results modal (hidden by default)
		_resultsModal = new ResultsModal(this, _scaleFactor);
		_resultsModal.Visible = false;
		AddChild(_resultsModal);

		// Game HUD (hidden by default)
		_gameHUD = new GameHUD(this, _scaleFactor);
		_gameHUD.Visible = false;
		AddChild(_gameHUD);

		// Confirmation popup (hidden by default)
		_confirmationPopup = new ConfirmationPopup(this, _scaleFactor);
		_confirmationPopup.Visible = false;
		AddChild(_confirmationPopup);
	}

	#endregion

	#region Public API

	public void ShowMainMenu()
	{
		_mainMenu.Visible = true;
		_gameHUD.Visible = false;
		_predictionPopup.Visible = false;
		_resultsModal.Visible = false;
	}

	public void HideMainMenu()
	{
		_mainMenu.Visible = false;
		_gameHUD.Visible = true;
	}

	public void ShowPredictionPopup(Vector2 screenPosition)
	{
		_predictionPopup.ShowAt(screenPosition);
	}

	public void HidePredictionPopup()
	{
		_predictionPopup.Visible = false;
	}

	public void ShowResultFeedback(PredictionResult result, Vector2 position)
	{
		_gameHUD.ShowResultFeedback(result, position);
	}

	public void ShowResultsModal(bool won, int jackpotWon)
	{
		_resultsModal.Show(won, jackpotWon);
	}

	public void UpdateJackpotDisplay(int amount)
	{
		_mainMenu.UpdateJackpot(amount);
		_gameHUD.UpdateJackpot(amount);
	}

	public void UpdateCurrentPlayer(string playerName)
	{
		_gameHUD.UpdateCurrentPlayer(playerName);
	}

	public void ShowError(string message)
	{
		GD.PrintErr($"[Nines UI] {message}");
		_game?.ShowNotification(message, NotificationSeverity.Error);
	}

	public void RefreshPlayerList()
	{
		_playerList?.Refresh();
	}

	public void RefreshJackpotButtonState()
	{
		_mainMenu?.UpdateButtonStates();
	}

	public void ShowForfeitConfirmation()
	{
		_confirmationPopup.Show(
			"Forfeit this round?\nThis counts as a loss for all players.",
			() => _game?.ForfeitGame());
	}

	#endregion

	#region Nested Class: MainMenuPanel

	public partial class MainMenuPanel : Control
	{
		private readonly NinesUI _ui;
		private readonly float _scale;
		private Label _titleLabel;
		private Button _playButton;
		private Label _jackpotLabel;
		private Label _entryCostLabel;

		public MainMenuPanel(NinesUI ui, float scaleFactor)
		{
			_ui = ui;
			_scale = scaleFactor;
			SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			BuildUI();
		}

		private void BuildUI()
		{
			var config = _ui._config;
			int cornerRadius = (int)(8 * _scale);

			// Background
			var bg = new ColorRect
			{
				Color = config.BackgroundColor,
				MouseFilter = MouseFilterEnum.Ignore,
			};
			bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			AddChild(bg);

			// Center container
			var containerSize = new Vector2(400, 300) * _scale;
			var container = new VBoxContainer
			{
				CustomMinimumSize = containerSize,
			};
			container.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
			container.Position -= containerSize / 2;
			AddChild(container);

			// Title
			_titleLabel = new Label
			{
				Text = "NINES",
				HorizontalAlignment = HorizontalAlignment.Center,
			};
			_titleLabel.AddThemeFontSizeOverride("font_size", config.GetScaledFontSize(48, _scale));
			container.AddChild(_titleLabel);

			// Spacer
			container.AddChild(new Control { CustomMinimumSize = new Vector2(0, 30 * _scale) });

			// Single Play button
			_playButton = CreateButton("Play", config.ButtonNormalColor, cornerRadius);
			_playButton.Pressed += OnPlayPressed;
			container.AddChild(_playButton);

			// Spacer
			container.AddChild(new Control { CustomMinimumSize = new Vector2(0, 15 * _scale) });

			// Entry cost display
			_entryCostLabel = new Label
			{
				Text = $"Entry: {config.EntryCost:N0} credits per player",
				HorizontalAlignment = HorizontalAlignment.Center,
			};
			_entryCostLabel.AddThemeFontSizeOverride("font_size", config.GetScaledFontSize(18, _scale));
			_entryCostLabel.AddThemeColorOverride("font_color", NinesPalette.EntryCostText);
			container.AddChild(_entryCostLabel);

			// Spacer
			container.AddChild(new Control { CustomMinimumSize = new Vector2(0, 30 * _scale) });

			// Jackpot display
			_jackpotLabel = new Label
			{
				Text = "JACKPOT: 0 CREDITS",
				HorizontalAlignment = HorizontalAlignment.Center,
			};
			_jackpotLabel.AddThemeFontSizeOverride("font_size", config.GetScaledFontSize(28, _scale));
			_jackpotLabel.AddThemeColorOverride("font_color", config.JackpotDisplayColor);
			container.AddChild(_jackpotLabel);

			UpdateButtonStates();
		}

		private Button CreateButton(string text, Color color, int cornerRadius)
		{
			var button = new Button
			{
				Text = text,
				CustomMinimumSize = new Vector2(250, 50) * _scale,
			};
			button.AddThemeFontSizeOverride("font_size", _ui._config.GetScaledFontSize(16, _scale));

			var styleNormal = new StyleBoxFlat
			{
				BgColor = color,
				CornerRadiusTopLeft = cornerRadius,
				CornerRadiusTopRight = cornerRadius,
				CornerRadiusBottomLeft = cornerRadius,
				CornerRadiusBottomRight = cornerRadius,
			};
			button.AddThemeStyleboxOverride("normal", styleNormal);

			var styleHover = new StyleBoxFlat
			{
				BgColor = color.Lightened(0.2f),
				CornerRadiusTopLeft = cornerRadius,
				CornerRadiusTopRight = cornerRadius,
				CornerRadiusBottomLeft = cornerRadius,
				CornerRadiusBottomRight = cornerRadius,
			};
			button.AddThemeStyleboxOverride("hover", styleHover);

			return button;
		}

		public void UpdateJackpot(int amount)
		{
			_jackpotLabel.Text = $"JACKPOT: {amount:N0} CREDITS";
		}

		public void UpdateButtonStates()
		{
			// Play requires at least one logged-in player
			bool canPlay = _ui._game?.CanPlay() ?? false;
			_playButton.Disabled = !canPlay;
			ApplyDisabledStyle(_playButton, !canPlay);
		}

		private void ApplyDisabledStyle(Button button, bool disabled)
		{
			if (disabled)
			{
				int cornerRadius = (int)(8 * _scale);
				var styleDisabled = new StyleBoxFlat
				{
					BgColor = _ui._config.ButtonDisabledColor,
					CornerRadiusTopLeft = cornerRadius,
					CornerRadiusTopRight = cornerRadius,
					CornerRadiusBottomLeft = cornerRadius,
					CornerRadiusBottomRight = cornerRadius,
				};
				button.AddThemeStyleboxOverride("disabled", styleDisabled);
			}
		}

		private async void OnPlayPressed()
		{
			if (_ui._game != null)
			{
				await _ui._game.StartGameAsync();
			}
		}
	}

	#endregion

	#region Nested Class: PredictionPopup

	public partial class PredictionPopup : Control
	{
		private readonly NinesUI _ui;
		private readonly float _scale;
		private Vector2 _popupSize;
		private Button _higherButton;
		private Button _lowerButton;
		private Button _sameButton;
		private Button _cancelButton;

		public PredictionPopup(NinesUI ui, float scaleFactor)
		{
			_ui = ui;
			_scale = scaleFactor;
			BuildUI();
		}

		private void BuildUI()
		{
			var config = _ui._config;
			int cornerRadius = (int)(12 * _scale);
			int btnRadius = (int)(6 * _scale);
			_popupSize = new Vector2(200, 180) * _scale;

			// Panel background
			var panel = new Panel
			{
				CustomMinimumSize = _popupSize,
			};
			var panelStyle = new StyleBoxFlat
			{
				BgColor = config.PanelColor,
				CornerRadiusTopLeft = cornerRadius,
				CornerRadiusTopRight = cornerRadius,
				CornerRadiusBottomLeft = cornerRadius,
				CornerRadiusBottomRight = cornerRadius,
			};
			panel.AddThemeStyleboxOverride("panel", panelStyle);
			AddChild(panel);

			// Button container
			var container = new VBoxContainer
			{
				Position = new Vector2(10, 10) * _scale,
				CustomMinimumSize = new Vector2(180, 160) * _scale,
			};
			panel.AddChild(container);

			// Higher button
			_higherButton = CreatePredictionButton("HIGHER", NinesPalette.PredictionHigher, btnRadius);
			_higherButton.Pressed += () => OnPrediction(PredictionType.Higher);
			container.AddChild(_higherButton);

			// Lower button
			_lowerButton = CreatePredictionButton("LOWER", NinesPalette.PredictionLower, btnRadius);
			_lowerButton.Pressed += () => OnPrediction(PredictionType.Lower);
			container.AddChild(_lowerButton);

			// Same button
			_sameButton = CreatePredictionButton("SAME", NinesPalette.PredictionSame, btnRadius);
			_sameButton.Pressed += () => OnPrediction(PredictionType.Same);
			container.AddChild(_sameButton);

			// Cancel button
			_cancelButton = CreatePredictionButton("X", NinesPalette.PredictionCancel, btnRadius);
			_cancelButton.CustomMinimumSize = new Vector2(40, 30) * _scale;
			_cancelButton.Pressed += OnCancel;
			container.AddChild(_cancelButton);
		}

		private Button CreatePredictionButton(string text, Color color, int cornerRadius)
		{
			var button = new Button
			{
				Text = text,
				CustomMinimumSize = new Vector2(180, 35) * _scale,
			};
			button.AddThemeFontSizeOverride("font_size", _ui._config.GetScaledFontSize(16, _scale));

			var style = new StyleBoxFlat
			{
				BgColor = color,
				CornerRadiusTopLeft = cornerRadius,
				CornerRadiusTopRight = cornerRadius,
				CornerRadiusBottomLeft = cornerRadius,
				CornerRadiusBottomRight = cornerRadius,
			};
			button.AddThemeStyleboxOverride("normal", style);

			var hoverStyle = new StyleBoxFlat
			{
				BgColor = color.Lightened(0.3f),
				CornerRadiusTopLeft = cornerRadius,
				CornerRadiusTopRight = cornerRadius,
				CornerRadiusBottomLeft = cornerRadius,
				CornerRadiusBottomRight = cornerRadius,
			};
			button.AddThemeStyleboxOverride("hover", hoverStyle);

			return button;
		}

		public void ShowAt(Vector2 screenPosition)
		{
			// Position popup near the selected stack, but keep on screen
			var viewportSize = GetViewport().GetVisibleRect().Size;

			var x = Mathf.Clamp(screenPosition.X - (_popupSize.X / 2), 10, viewportSize.X - _popupSize.X - 10);
			var y = Mathf.Clamp(screenPosition.Y - _popupSize.Y - 20, 10, viewportSize.Y - _popupSize.Y - 10);

			Position = new Vector2(x, y);
			Visible = true;
		}

		private void OnPrediction(PredictionType prediction)
		{
			_ui._game?.OnPredictionMade(prediction);
		}

		private void OnCancel()
		{
			_ui._game?.OnPredictionCancelled();
		}
	}

	#endregion

	#region Nested Class: ResultsModal

	public partial class ResultsModal : Control
	{
		private readonly NinesUI _ui;
		private readonly float _scale;
		private Label _resultLabel;
		private Label _jackpotLabel;
		private Button _playAgainButton;
		private Button _menuButton;

		public ResultsModal(NinesUI ui, float scaleFactor)
		{
			_ui = ui;
			_scale = scaleFactor;
			SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			BuildUI();
		}

		private void BuildUI()
		{
			var config = _ui._config;
			int cornerRadius = (int)(16 * _scale);
			int btnRadius = (int)(6 * _scale);

			// Semi-transparent overlay
			var overlay = new ColorRect
			{
				Color = NinesPalette.Scrim,
				MouseFilter = MouseFilterEnum.Stop,
			};
			overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			AddChild(overlay);

			// Modal panel
			var panelSize = new Vector2(350, 250) * _scale;
			var panel = new Panel
			{
				CustomMinimumSize = panelSize,
			};
			panel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
			panel.Position -= panelSize / 2;

			var panelStyle = new StyleBoxFlat
			{
				BgColor = config.PanelColor,
				CornerRadiusTopLeft = cornerRadius,
				CornerRadiusTopRight = cornerRadius,
				CornerRadiusBottomLeft = cornerRadius,
				CornerRadiusBottomRight = cornerRadius,
			};
			panel.AddThemeStyleboxOverride("panel", panelStyle);
			AddChild(panel);

			// Content container
			var container = new VBoxContainer
			{
				Position = new Vector2(25, 25) * _scale,
				CustomMinimumSize = new Vector2(300, 200) * _scale,
			};
			panel.AddChild(container);

			// Result label
			_resultLabel = new Label
			{
				Text = "GAME OVER",
				HorizontalAlignment = HorizontalAlignment.Center,
			};
			_resultLabel.AddThemeFontSizeOverride("font_size", config.GetScaledFontSize(36, _scale));
			container.AddChild(_resultLabel);

			// Jackpot label
			_jackpotLabel = new Label
			{
				Text = string.Empty,
				HorizontalAlignment = HorizontalAlignment.Center,
			};
			_jackpotLabel.AddThemeFontSizeOverride("font_size", config.GetScaledFontSize(24, _scale));
			_jackpotLabel.AddThemeColorOverride("font_color", config.JackpotDisplayColor);
			container.AddChild(_jackpotLabel);

			// Spacer
			container.AddChild(new Control { CustomMinimumSize = new Vector2(0, 30 * _scale) });

			// Button container
			var buttonContainer = new HBoxContainer
			{
				Alignment = BoxContainer.AlignmentMode.Center,
			};
			container.AddChild(buttonContainer);

			// Play Again button
			_playAgainButton = new Button
			{
				Text = "Play Again",
				CustomMinimumSize = new Vector2(120, 40) * _scale,
			};
			_playAgainButton.AddThemeFontSizeOverride("font_size", config.GetScaledFontSize(14, _scale));
			var playAgainStyle = new StyleBoxFlat
			{
				BgColor = config.ButtonNormalColor,
				CornerRadiusTopLeft = btnRadius,
				CornerRadiusTopRight = btnRadius,
				CornerRadiusBottomLeft = btnRadius,
				CornerRadiusBottomRight = btnRadius,
			};
			_playAgainButton.AddThemeStyleboxOverride("normal", playAgainStyle);
			_playAgainButton.Pressed += OnPlayAgain;
			buttonContainer.AddChild(_playAgainButton);

			// Spacer
			buttonContainer.AddChild(new Control { CustomMinimumSize = new Vector2(20 * _scale, 0) });

			// Menu button
			_menuButton = new Button
			{
				Text = "Menu",
				CustomMinimumSize = new Vector2(120, 40) * _scale,
			};
			_menuButton.AddThemeFontSizeOverride("font_size", config.GetScaledFontSize(14, _scale));
			var menuStyle = new StyleBoxFlat
			{
				BgColor = config.ButtonNormalColor,
				CornerRadiusTopLeft = btnRadius,
				CornerRadiusTopRight = btnRadius,
				CornerRadiusBottomLeft = btnRadius,
				CornerRadiusBottomRight = btnRadius,
			};
			_menuButton.AddThemeStyleboxOverride("normal", menuStyle);
			_menuButton.Pressed += OnMenu;
			buttonContainer.AddChild(_menuButton);
		}

		public void Show(bool won, int jackpotWon)
		{
			var config = _ui._config;

			if (won)
			{
				_resultLabel.Text = "YOU WIN!";
				_resultLabel.AddThemeColorOverride("font_color", config.CorrectFeedbackColor);

				if (jackpotWon > 0)
				{
					_jackpotLabel.Text = $"JACKPOT: +{jackpotWon:N0} CREDITS!";
					_jackpotLabel.Visible = true;
				}
				else
				{
					_jackpotLabel.Visible = false;
				}
			}
			else
			{
				_resultLabel.Text = "GAME OVER";
				_resultLabel.AddThemeColorOverride("font_color", config.WrongFeedbackColor);
				_jackpotLabel.Visible = false;
			}

			Visible = true;
		}

		private async void OnPlayAgain()
		{
			Visible = false;
			if (_ui._game != null)
			{
				await _ui._game.StartGameAsync();
			}
		}

		private void OnMenu()
		{
			Visible = false;
			_ui._game?.ReturnToMenu();
		}
	}

	#endregion

	#region Nested Class: GameHUD

	public partial class GameHUD : Control
	{
		private const float TOP_MENU_OFFSET = 160f; // 150px menu + 10px padding

		private readonly NinesUI _ui;
		private readonly float _scale;
		private Label _currentPlayerLabel;
		private Label _jackpotLabel;
		private Label _feedbackLabel;

		public GameHUD(NinesUI ui, float scaleFactor)
		{
			_ui = ui;
			_scale = scaleFactor;
			SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			MouseFilter = MouseFilterEnum.Ignore;
			BuildUI();
		}

		private void BuildUI()
		{
			var config = _ui._config;

			// Current player turn label - left-aligned with offset from screen edge
			_currentPlayerLabel = new Label
			{
				Text = "Player 1's Turn",
				Position = new Vector2(20 * _scale, TOP_MENU_OFFSET * _scale),
			};
			_currentPlayerLabel.AddThemeFontSizeOverride("font_size", config.GetScaledFontSize(24, _scale));
			AddChild(_currentPlayerLabel);

			// Jackpot (bottom center) - properly centered
			_jackpotLabel = new Label
			{
				Text = "JACKPOT: 0",
				HorizontalAlignment = HorizontalAlignment.Center,
				GrowHorizontal = GrowDirection.Both,
			};
			_jackpotLabel.AddThemeFontSizeOverride("font_size", config.GetScaledFontSize(22, _scale));
			_jackpotLabel.AddThemeColorOverride("font_color", config.JackpotDisplayColor);
			_jackpotLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom);
			_jackpotLabel.Position = new Vector2(0, -50 * _scale);
			AddChild(_jackpotLabel);

			// Feedback label (center, hidden by default)
			_feedbackLabel = new Label
			{
				Text = string.Empty,
				HorizontalAlignment = HorizontalAlignment.Center,
				Visible = false,
			};
			_feedbackLabel.AddThemeFontSizeOverride("font_size", config.GetScaledFontSize(48, _scale));
			_feedbackLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
			AddChild(_feedbackLabel);
		}

		public void UpdateCurrentPlayer(string playerName)
		{
			_currentPlayerLabel.Text = $"{playerName}'s Turn";
		}

		public void UpdateJackpot(int amount)
		{
			_jackpotLabel.Text = $"JACKPOT: {amount:N0}";
		}

		public void ShowResultFeedback(PredictionResult result, Vector2 position)
		{
			var config = _ui._config;

			switch (result)
			{
				case PredictionResult.Correct:
					_feedbackLabel.Text = "NICE!";
					_feedbackLabel.AddThemeColorOverride("font_color", config.CorrectFeedbackColor);
					break;

				case PredictionResult.SameCorrect:
					_feedbackLabel.Text = "SAME! BONUS!";
					_feedbackLabel.AddThemeColorOverride("font_color", config.JackpotDisplayColor);
					break;

				case PredictionResult.Wrong:
				case PredictionResult.SameWrong:
					_feedbackLabel.Text = "OOF!";
					_feedbackLabel.AddThemeColorOverride("font_color", config.WrongFeedbackColor);
					break;
			}

			_feedbackLabel.Position = position - new Vector2(50, 30);
			_feedbackLabel.Visible = true;

			// Hide after delay
			var tween = CreateTween();
			tween.TweenInterval(config.ResultFeedbackDuration);
			tween.TweenCallback(Callable.From(() => _feedbackLabel.Visible = false));
		}
	}

	#endregion

	#region Nested Class: PlayerListPanel

	public partial class PlayerListPanel : Control
	{
		private readonly NinesUI _ui;
		private readonly float _scale;
		private Panel _panel;
		private VBoxContainer _playerContainer;
		private Button _titleButton;
		private Button _addPlayerButton;
		private VBoxContainer _collapsibleSection;
		private bool _isCollapsed;

		public PlayerListPanel(NinesUI ui, float scaleFactor)
		{
			_ui = ui;
			_scale = scaleFactor;
			BuildUI();
		}

		private void BuildUI()
		{
			var config = _ui._config;
			int cornerRadius = (int)(8 * _scale);
			int btnRadius = (int)(6 * _scale);
			var panelSize = new Vector2(200, 300) * _scale;

			// Position on right side
			SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);
			Position = new Vector2(-220 * _scale, 20 * _scale);
			CustomMinimumSize = panelSize;

			// Panel background
			_panel = new Panel
			{
				CustomMinimumSize = panelSize,
			};
			var panelStyle = new StyleBoxFlat
			{
				BgColor = config.PanelColor,
				CornerRadiusTopLeft = cornerRadius,
				CornerRadiusTopRight = cornerRadius,
				CornerRadiusBottomLeft = cornerRadius,
				CornerRadiusBottomRight = cornerRadius,
			};
			_panel.AddThemeStyleboxOverride("panel", panelStyle);
			AddChild(_panel);

			// Content container
			var content = new VBoxContainer
			{
				Position = new Vector2(10, 10) * _scale,
				CustomMinimumSize = new Vector2(180, 280) * _scale,
			};
			_panel.AddChild(content);

			// Title button (clickable to collapse/expand)
			_titleButton = new Button
			{
				Text = "PLAYERS  v",
				Flat = true,
				CustomMinimumSize = new Vector2(180, 30) * _scale,
			};
			_titleButton.AddThemeFontSizeOverride("font_size", config.GetScaledFontSize(18, _scale));
			_titleButton.Pressed += OnTitlePressed;
			content.AddChild(_titleButton);

			// Collapsible section containing player list and add button
			_collapsibleSection = new VBoxContainer();
			content.AddChild(_collapsibleSection);

			// Spacer
			_collapsibleSection.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10 * _scale) });

			// Player list container
			_playerContainer = new VBoxContainer();
			_collapsibleSection.AddChild(_playerContainer);

			// Spacer
			_collapsibleSection.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10 * _scale), SizeFlagsVertical = SizeFlags.ExpandFill });

			// Add player button
			_addPlayerButton = new Button
			{
				Text = "+ Add Player",
				CustomMinimumSize = new Vector2(180, 35) * _scale,
			};
			_addPlayerButton.AddThemeFontSizeOverride("font_size", config.GetScaledFontSize(14, _scale));
			var buttonStyle = new StyleBoxFlat
			{
				BgColor = config.ButtonNormalColor,
				CornerRadiusTopLeft = btnRadius,
				CornerRadiusTopRight = btnRadius,
				CornerRadiusBottomLeft = btnRadius,
				CornerRadiusBottomRight = btnRadius,
			};
			_addPlayerButton.AddThemeStyleboxOverride("normal", buttonStyle);
			_addPlayerButton.Pressed += OnAddPlayerPressed;
			_collapsibleSection.AddChild(_addPlayerButton);

			Refresh();
		}

		private void OnTitlePressed()
		{
			_isCollapsed = !_isCollapsed;
			_collapsibleSection.Visible = !_isCollapsed;
			_titleButton.Text = _isCollapsed ? "PLAYERS  >" : "PLAYERS  v";
			UpdatePanelSize();
		}

		private void UpdatePanelSize()
		{
			if (_isCollapsed)
			{
				// Just show title bar height
				var collapsedHeight = 50 * _scale;
				_panel.CustomMinimumSize = new Vector2(200 * _scale, collapsedHeight);
				CustomMinimumSize = new Vector2(200 * _scale, collapsedHeight);
			}
			else
			{
				var panelSize = new Vector2(200, 300) * _scale;
				_panel.CustomMinimumSize = panelSize;
				CustomMinimumSize = panelSize;
			}
		}

		public void Refresh()
		{
			// Clear existing entries
			foreach (var child in _playerContainer.GetChildren())
			{
				child.QueueFree();
			}

			// Add player entries
			var players = _ui._game?.GetPlayers();
			if (players == null || players.Count == 0)
			{
				var noPlayersLabel = new Label
				{
					Text = "No players joined",
					HorizontalAlignment = HorizontalAlignment.Center,
					Modulate = NinesPalette.NoPlayersModulate,
				};
				noPlayersLabel.AddThemeFontSizeOverride("font_size", _ui._config.GetScaledFontSize(14, _scale));
				_playerContainer.AddChild(noPlayersLabel);
				return;
			}

			var config = _ui._config;
			var currentPlayerIndex = _ui._game?.GetCurrentPlayerIndex() ?? -1;
			var playerIndex = 0;
			foreach (var player in players)
			{
				var isCurrentTurn = playerIndex == currentPlayerIndex;
				var entry = new HBoxContainer();

				// Player name
				var nameLabel = new Label
				{
					Text = player.DisplayName,
					SizeFlagsHorizontal = SizeFlags.ExpandFill,
				};
				nameLabel.AddThemeFontSizeOverride("font_size", config.GetScaledFontSize(14, _scale));
				entry.AddChild(nameLabel);

				// Turn indicator (filled circle for current turn, empty for others)
				var statusLabel = new Label
				{
					Text = isCurrentTurn ? "●" : "○",
				};
				statusLabel.AddThemeFontSizeOverride("font_size", config.GetScaledFontSize(14, _scale));
				statusLabel.AddThemeColorOverride(
					"font_color",
					isCurrentTurn ? config.JackpotDisplayColor : config.ButtonDisabledColor);
				entry.AddChild(statusLabel);

				// Credits (if logged in)
				if (player.IsLoggedIn)
				{
					var creditsLabel = new Label
					{
						Text = $"{player.Credits:N0}c",
					};
					creditsLabel.AddThemeFontSizeOverride("font_size", config.GetScaledFontSize(12, _scale));
					creditsLabel.AddThemeColorOverride("font_color", config.JackpotDisplayColor);
					entry.AddChild(creditsLabel);
				}

				_playerContainer.AddChild(entry);
				playerIndex++;
			}

			// Update add button state
			var maxPlayers = _ui._config?.MaxPlayers ?? 8;
			_addPlayerButton.Disabled = players.Count >= maxPlayers;
		}

		private void OnAddPlayerPressed()
		{
			var uiManager = UIManager.GetInstance();
			if (uiManager != null)
			{
				uiManager.ShowLoginModal();
			}
			else
			{
				GD.PrintErr("[Nines] UIManager not available - cannot show login");
			}
		}
	}

	#endregion

	#region Nested Class: ConfirmationPopup

	public partial class ConfirmationPopup : Control
	{
		private readonly NinesUI _ui;
		private readonly float _scale;
		private Label _messageLabel;
		private Action _onConfirm;

		public ConfirmationPopup(NinesUI ui, float scaleFactor)
		{
			_ui = ui;
			_scale = scaleFactor;
			SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			BuildUI();
		}

		private void BuildUI()
		{
			var config = _ui._config;
			int cornerRadius = (int)(12 * _scale);
			int btnRadius = (int)(6 * _scale);

			// Semi-transparent overlay
			var overlay = new ColorRect
			{
				Color = NinesPalette.Scrim,
				MouseFilter = MouseFilterEnum.Stop,
			};
			overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			AddChild(overlay);

			// Modal panel
			var panelSize = new Vector2(300, 180) * _scale;
			var panel = new Panel
			{
				CustomMinimumSize = panelSize,
			};
			panel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
			panel.Position -= panelSize / 2;

			var panelStyle = new StyleBoxFlat
			{
				BgColor = config.PanelColor,
				CornerRadiusTopLeft = cornerRadius,
				CornerRadiusTopRight = cornerRadius,
				CornerRadiusBottomLeft = cornerRadius,
				CornerRadiusBottomRight = cornerRadius,
			};
			panel.AddThemeStyleboxOverride("panel", panelStyle);
			AddChild(panel);

			// Content container
			var container = new VBoxContainer
			{
				Position = new Vector2(20, 20) * _scale,
				CustomMinimumSize = new Vector2(260, 140) * _scale,
			};
			panel.AddChild(container);

			// Message label
			_messageLabel = new Label
			{
				Text = string.Empty,
				HorizontalAlignment = HorizontalAlignment.Center,
				AutowrapMode = TextServer.AutowrapMode.Word,
			};
			_messageLabel.AddThemeFontSizeOverride("font_size", config.GetScaledFontSize(18, _scale));
			container.AddChild(_messageLabel);

			// Spacer
			container.AddChild(new Control { CustomMinimumSize = new Vector2(0, 20 * _scale), SizeFlagsVertical = SizeFlags.ExpandFill });

			// Button container
			var buttonContainer = new HBoxContainer
			{
				Alignment = BoxContainer.AlignmentMode.Center,
			};
			container.AddChild(buttonContainer);

			// Yes button
			var yesButton = new Button
			{
				Text = "Yes",
				CustomMinimumSize = new Vector2(100, 40) * _scale,
			};
			yesButton.AddThemeFontSizeOverride("font_size", config.GetScaledFontSize(14, _scale));
			var yesStyle = new StyleBoxFlat
			{
				BgColor = config.WrongFeedbackColor,
				CornerRadiusTopLeft = btnRadius,
				CornerRadiusTopRight = btnRadius,
				CornerRadiusBottomLeft = btnRadius,
				CornerRadiusBottomRight = btnRadius,
			};
			yesButton.AddThemeStyleboxOverride("normal", yesStyle);
			yesButton.Pressed += OnYesPressed;
			buttonContainer.AddChild(yesButton);

			// Spacer
			buttonContainer.AddChild(new Control { CustomMinimumSize = new Vector2(20 * _scale, 0) });

			// No button
			var noButton = new Button
			{
				Text = "No",
				CustomMinimumSize = new Vector2(100, 40) * _scale,
			};
			noButton.AddThemeFontSizeOverride("font_size", config.GetScaledFontSize(14, _scale));
			var noStyle = new StyleBoxFlat
			{
				BgColor = config.ButtonNormalColor,
				CornerRadiusTopLeft = btnRadius,
				CornerRadiusTopRight = btnRadius,
				CornerRadiusBottomLeft = btnRadius,
				CornerRadiusBottomRight = btnRadius,
			};
			noButton.AddThemeStyleboxOverride("normal", noStyle);
			noButton.Pressed += OnNoPressed;
			buttonContainer.AddChild(noButton);
		}

		public void Show(string message, Action onConfirm)
		{
			_messageLabel.Text = message;
			_onConfirm = onConfirm;
			Visible = true;
		}

		private void OnYesPressed()
		{
			Visible = false;
			_onConfirm?.Invoke();
		}

		private void OnNoPressed()
		{
			Visible = false;
		}
	}

	#endregion
}
