using Godot;
using System.Collections.Generic;
using System.Linq;

[GlobalClass]
public partial class HUDOverlay : CanvasLayer
{
	[Signal] public delegate void PauseRequestedEventHandler();
	[Signal] public delegate void ResumeRequestedEventHandler();
	[Signal] public delegate void ReturnToMenuRequestedEventHandler();

	[ExportCategory("HUD Settings")]
	[Export] public bool ShowScores { get; set; } = true;
	[Export] public bool ShowTimer { get; set; } = true;
	[Export] public bool ShowPauseButton { get; set; } = true;

	private Control _mainHUD;
	private HBoxContainer _topBar;
	private Label _scoreLabel;
	private Label _timerLabel;
	private Button _pauseButton;
	private Control _pauseOverlay;
	private Control _gameOverOverlay;
	private Label _finalScoreLabel;

	public override void _Ready()
	{
		CreateHUDElements();
	}

	private void CreateHUDElements()
	{
		// Main HUD container
		_mainHUD = new Control();
		_mainHUD.AnchorLeft = 0;
		_mainHUD.AnchorTop = 0;
		_mainHUD.AnchorRight = 1;
		_mainHUD.AnchorBottom = 1;
		_mainHUD.OffsetLeft = 0;
		_mainHUD.OffsetTop = 0;
		_mainHUD.OffsetRight = 0;
		_mainHUD.OffsetBottom = 0;
		_mainHUD.MouseFilter = Control.MouseFilterEnum.Ignore;
		AddChild(_mainHUD);

		// Top bar
		_topBar = new HBoxContainer();
		_topBar.AnchorLeft = 0;
		_topBar.AnchorTop = 0;
		_topBar.AnchorRight = 1;
		_topBar.AnchorBottom = 0;
		_topBar.OffsetLeft = 0;
		_topBar.OffsetTop = 0;
		_topBar.OffsetRight = 0;
		_topBar.OffsetBottom = 60;
		_topBar.Alignment = BoxContainer.AlignmentMode.Center;
		_mainHUD.AddChild(_topBar);

		if (ShowScores)
		{
			_scoreLabel = new Label();
			_scoreLabel.Text = "Score: 0";
			_scoreLabel.HorizontalAlignment = HorizontalAlignment.Center;
			_topBar.AddChild(_scoreLabel);

			var separator1 = new VSeparator();
			_topBar.AddChild(separator1);
		}

		if (ShowTimer)
		{
			_timerLabel = new Label();
			_timerLabel.Text = "Time: 0.0s";
			_timerLabel.HorizontalAlignment = HorizontalAlignment.Center;
			_topBar.AddChild(_timerLabel);

			var separator2 = new VSeparator();
			_topBar.AddChild(separator2);
		}

		if (ShowPauseButton)
		{
			_pauseButton = new Button();
			_pauseButton.Text = "Pause";
			_pauseButton.Pressed += OnPausePressed;
			_topBar.AddChild(_pauseButton);
		}

		CreatePauseOverlay();
		CreateGameOverOverlay();
	}

	private void CreatePauseOverlay()
	{
		_pauseOverlay = new Control();
		_pauseOverlay.AnchorLeft = 0;
		_pauseOverlay.AnchorTop = 0;
		_pauseOverlay.AnchorRight = 1;
		_pauseOverlay.AnchorBottom = 1;
		_pauseOverlay.OffsetLeft = 0;
		_pauseOverlay.OffsetTop = 0;
		_pauseOverlay.OffsetRight = 0;
		_pauseOverlay.OffsetBottom = 0;
		_pauseOverlay.Visible = false;
		AddChild(_pauseOverlay);

		// Semi-transparent background
		var background = new ColorRect();
		background.AnchorLeft = 0;
		background.AnchorTop = 0;
		background.AnchorRight = 1;
		background.AnchorBottom = 1;
		background.OffsetLeft = 0;
		background.OffsetTop = 0;
		background.OffsetRight = 0;
		background.OffsetBottom = 0;
		background.Color = new Color(0, 0, 0, 0.7f);
		_pauseOverlay.AddChild(background);

		// Pause panel
		var pausePanel = new Panel();
		pausePanel.AnchorLeft = 0.5f;
		pausePanel.AnchorTop = 0.5f;
		pausePanel.AnchorRight = 0.5f;
		pausePanel.AnchorBottom = 0.5f;
		pausePanel.OffsetLeft = -200;
		pausePanel.OffsetTop = -150;
		pausePanel.OffsetRight = 200;
		pausePanel.OffsetBottom = 150;
		_pauseOverlay.AddChild(pausePanel);

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
		pausePanel.AddChild(vbox);

		var pauseLabel = new Label();
		pauseLabel.Text = "PAUSED";
		pauseLabel.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(pauseLabel);

		var resumeButton = new Button();
		resumeButton.Text = "Resume";
		resumeButton.Pressed += OnResumePressed;
		vbox.AddChild(resumeButton);

		var returnToMenuButton = new Button();
		returnToMenuButton.Text = "Return to Menu";
		returnToMenuButton.Pressed += OnReturnToMenuPressed;
		vbox.AddChild(returnToMenuButton);
	}

	private void CreateGameOverOverlay()
	{
		_gameOverOverlay = new Control();
		_gameOverOverlay.AnchorLeft = 0;
		_gameOverOverlay.AnchorTop = 0;
		_gameOverOverlay.AnchorRight = 1;
		_gameOverOverlay.AnchorBottom = 1;
		_gameOverOverlay.OffsetLeft = 0;
		_gameOverOverlay.OffsetTop = 0;
		_gameOverOverlay.OffsetRight = 0;
		_gameOverOverlay.OffsetBottom = 0;
		_gameOverOverlay.Visible = false;
		AddChild(_gameOverOverlay);

		// Semi-transparent background
		var background = new ColorRect();
		background.AnchorLeft = 0;
		background.AnchorTop = 0;
		background.AnchorRight = 1;
		background.AnchorBottom = 1;
		background.OffsetLeft = 0;
		background.OffsetTop = 0;
		background.OffsetRight = 0;
		background.OffsetBottom = 0;
		background.Color = new Color(0, 0, 0, 0.8f);
		_gameOverOverlay.AddChild(background);

		// Game over panel
		var gameOverPanel = new Panel();
		gameOverPanel.AnchorLeft = 0.5f;
		gameOverPanel.AnchorTop = 0.5f;
		gameOverPanel.AnchorRight = 0.5f;
		gameOverPanel.AnchorBottom = 0.5f;
		gameOverPanel.OffsetLeft = -250;
		gameOverPanel.OffsetTop = -150;
		gameOverPanel.OffsetRight = 250;
		gameOverPanel.OffsetBottom = 150;
		_gameOverOverlay.AddChild(gameOverPanel);

		var vbox = new VBoxContainer();
		vbox.AnchorLeft = 0.5f;
		vbox.AnchorTop = 0.5f;
		vbox.AnchorRight = 0.5f;
		vbox.AnchorBottom = 0.5f;
		vbox.OffsetLeft = -125;
		vbox.OffsetTop = -75;
		vbox.OffsetRight = 125;
		vbox.OffsetBottom = 75;
		vbox.Alignment = BoxContainer.AlignmentMode.Center;
		gameOverPanel.AddChild(vbox);

		var gameOverLabel = new Label();
		gameOverLabel.Text = "GAME OVER";
		gameOverLabel.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(gameOverLabel);

		_finalScoreLabel = new Label();
		_finalScoreLabel.Text = "Final Score: 0";
		_finalScoreLabel.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(_finalScoreLabel);

		var playAgainButton = new Button();
		playAgainButton.Text = "Play Again";
		playAgainButton.Pressed += OnPlayAgainPressed;
		vbox.AddChild(playAgainButton);

		var returnToMenuButton2 = new Button();
		returnToMenuButton2.Text = "Return to Menu";
		returnToMenuButton2.Pressed += OnReturnToMenuPressed;
		vbox.AddChild(returnToMenuButton2);
	}

	public void UpdateScores(Dictionary<string, int> scores)
	{
		if (_scoreLabel != null && ShowScores)
		{
			if (scores.Count == 1)
			{
				_scoreLabel.Text = $"Score: {scores.Values.First()}";
			}
			else if (scores.Count > 1)
			{
				var totalScore = scores.Values.Sum();
				_scoreLabel.Text = $"Total Score: {totalScore}";
			}
			else
			{
				_scoreLabel.Text = "Score: 0";
			}
		}
	}

	public void UpdateGameTime(float gameTime)
	{
		if (_timerLabel != null && ShowTimer)
		{
			_timerLabel.Text = $"Time: {gameTime:F1}s";
		}
	}

	public void ShowPauseOverlay()
	{
		if (_pauseOverlay != null)
		{
			_pauseOverlay.Visible = true;
		}
		
		if (_pauseButton != null)
		{
			_pauseButton.Text = "Resume";
		}
	}

	public void HidePauseOverlay()
	{
		if (_pauseOverlay != null)
		{
			_pauseOverlay.Visible = false;
		}
		
		if (_pauseButton != null)
		{
			_pauseButton.Text = "Pause";
		}
	}

	public void ShowGameOverOverlay(Dictionary<string, int> finalScores = null)
	{
		if (_gameOverOverlay != null)
		{
			_gameOverOverlay.Visible = true;
			
			if (_finalScoreLabel != null && finalScores != null)
			{
				if (finalScores.Count == 1)
				{
					_finalScoreLabel.Text = $"Final Score: {finalScores.Values.First()}";
				}
				else if (finalScores.Count > 1)
				{
					var totalScore = finalScores.Values.Sum();
					_finalScoreLabel.Text = $"Total Score: {totalScore}";
				}
			}
		}
	}

	public void HideGameOverOverlay()
	{
		if (_gameOverOverlay != null)
		{
			_gameOverOverlay.Visible = false;
		}
	}

	public void SetHUDVisible(bool visible)
	{
		if (_mainHUD != null)
		{
			_mainHUD.Visible = visible;
		}
	}

	private void OnPausePressed()
	{
		EmitSignal(SignalName.PauseRequested);
	}

	private void OnResumePressed()
	{
		EmitSignal(SignalName.ResumeRequested);
	}

	private void OnReturnToMenuPressed()
	{
		EmitSignal(SignalName.ReturnToMenuRequested);
	}

	private void OnPlayAgainPressed()
	{
		HideGameOverOverlay();
		// Games can listen for this signal to restart
		EmitSignal("PlayAgainRequested");
	}
}