using Godot;
using System.Collections.Generic;

/// <summary>
/// Complete self-sufficient game in a single file
/// Includes built-in UI, pause system, and game over screen
/// Can run independently or integrate with GameHost via optional signals
/// Attach this to any Node2D and it becomes a fully playable game
/// </summary>
[GlobalClass]
public partial class SimpleSampleGame : Node2D
{
	// Optional GameHost integration signals - purely informational
	[Signal] public delegate void GameStartedEventHandler();
	[Signal] public delegate void GameEndedEventHandler();
	[Signal] public delegate void ScoreChangedEventHandler(string playerId, int newScore);
	[Signal] public delegate void PlayerAddedEventHandler(string playerId);

	// Game settings
	[ExportCategory("Game Settings")]
	[Export] public int TargetScore { get; set; } = 100;
	[Export] public float SpawnInterval { get; set; } = 2.0f;
	[Export] public float GameDuration { get; set; } = 60.0f;

	// Game state
	private bool _gameActive = false;
	private bool _gamePaused = false;
	private string _playerId = "player1";
	private int _score = 0;
	private float _gameTime = 0.0f;
	private Timer _spawnTimer;
	private List<Node2D> _collectibles = new();
	
	// Context detection
	private GameHost _gameHost;
	private bool _isProductionContext = false;

	// Simple player representation
	private Node2D _player;
	private Vector2 _targetPosition;
	private bool _isDragging = false;

	// Built-in UI elements
	private CanvasLayer _uiLayer;
	private Label _scoreLabel;
	private Label _timerLabel;
	private Button _pauseButton;
	private Control _pauseOverlay;
	private Control _gameOverOverlay;
	private Label _finalScoreLabel;

	public override void _Ready()
	{
		SetupUI();
		SetupGame();
		DetectAndAdaptToContext();
	}

	public override void _Process(double delta)
	{
		if (!_gameActive || _gamePaused) return;

		_gameTime += (float)delta;
		UpdateUI();

		// Check time limit
		if (_gameTime >= GameDuration)
		{
			EndGame();
			return;
		}

		// Update player movement
		if (_player != null && _isDragging)
		{
			_player.GlobalPosition = _player.GlobalPosition.MoveToward(_targetPosition, 300.0f * (float)delta);
		}

		// Check collectible collision
		CheckCollectibleCollisions();
	}

	public override void _Input(InputEvent inputEvent)
	{
		if (!_gameActive || _gamePaused) return;

		// Handle pause key
		if (inputEvent.IsActionPressed("ui_cancel") || 
		   (inputEvent is InputEventKey key && key.Keycode == Key.Escape))
		{
			TogglePause();
			return;
		}

		if (inputEvent is InputEventScreenTouch touch)
		{
			if (touch.Pressed)
			{
				_targetPosition = touch.Position;
				_isDragging = true;
			}
			else
			{
				_isDragging = false;
			}
		}
		else if (inputEvent is InputEventScreenDrag drag)
		{
			if (_isDragging)
			{
				_targetPosition = drag.Position;
			}
		}
		else if (inputEvent is InputEventMouseButton mouse)
		{
			if (mouse.ButtonIndex == MouseButton.Left)
			{
				if (mouse.Pressed)
				{
					_targetPosition = mouse.Position;
					_isDragging = true;
				}
				else
				{
					_isDragging = false;
				}
			}
		}
		else if (inputEvent is InputEventMouseMotion motion && _isDragging)
		{
			_targetPosition = motion.Position;
		}
	}

	// UI setup
	private void SetupUI()
	{
		_uiLayer = new CanvasLayer();
		AddChild(_uiLayer);

		// Main game UI
		var mainUI = new Control();
		mainUI.AnchorLeft = 0;
		mainUI.AnchorTop = 0;
		mainUI.AnchorRight = 1;
		mainUI.AnchorBottom = 1;
		mainUI.MouseFilter = Control.MouseFilterEnum.Ignore;
		_uiLayer.AddChild(mainUI);

		// Top bar with score and timer
		var topBar = new HBoxContainer();
		topBar.AnchorLeft = 0;
		topBar.AnchorTop = 0;
		topBar.AnchorRight = 1;
		topBar.AnchorBottom = 0;
		topBar.OffsetBottom = 50;
		topBar.Alignment = BoxContainer.AlignmentMode.Center;
		mainUI.AddChild(topBar);

		_scoreLabel = new Label();
		_scoreLabel.Text = "Score: 0";
		_scoreLabel.HorizontalAlignment = HorizontalAlignment.Center;
		topBar.AddChild(_scoreLabel);

		var separator = new VSeparator();
		topBar.AddChild(separator);

		_timerLabel = new Label();
		_timerLabel.Text = "Time: 60.0s";
		_timerLabel.HorizontalAlignment = HorizontalAlignment.Center;
		topBar.AddChild(separator);
		topBar.AddChild(_timerLabel);

		var separator2 = new VSeparator();
		topBar.AddChild(separator2);

		_pauseButton = new Button();
		_pauseButton.Text = "Pause";
		_pauseButton.Pressed += TogglePause;
		topBar.AddChild(_pauseButton);

		SetupPauseOverlay();
		SetupGameOverOverlay();
	}

	private void SetupPauseOverlay()
	{
		_pauseOverlay = new Control();
		_pauseOverlay.AnchorLeft = 0;
		_pauseOverlay.AnchorTop = 0;
		_pauseOverlay.AnchorRight = 1;
		_pauseOverlay.AnchorBottom = 1;
		_pauseOverlay.Visible = false;
		_uiLayer.AddChild(_pauseOverlay);

		// Semi-transparent background
		var background = new ColorRect();
		background.AnchorLeft = 0;
		background.AnchorTop = 0;
		background.AnchorRight = 1;
		background.AnchorBottom = 1;
		background.Color = new Color(0, 0, 0, 0.7f);
		_pauseOverlay.AddChild(background);

		// Pause panel
		var pausePanel = new Panel();
		pausePanel.AnchorLeft = 0.5f;
		pausePanel.AnchorTop = 0.5f;
		pausePanel.AnchorRight = 0.5f;
		pausePanel.AnchorBottom = 0.5f;
		pausePanel.OffsetLeft = -150;
		pausePanel.OffsetTop = -100;
		pausePanel.OffsetRight = 150;
		pausePanel.OffsetBottom = 100;
		_pauseOverlay.AddChild(pausePanel);

		var vbox = new VBoxContainer();
		vbox.AnchorLeft = 0.5f;
		vbox.AnchorTop = 0.5f;
		vbox.AnchorRight = 0.5f;
		vbox.AnchorBottom = 0.5f;
		vbox.OffsetLeft = -75;
		vbox.OffsetTop = -50;
		vbox.OffsetRight = 75;
		vbox.OffsetBottom = 50;
		vbox.Alignment = BoxContainer.AlignmentMode.Center;
		pausePanel.AddChild(vbox);

		var pauseLabel = new Label();
		pauseLabel.Text = "PAUSED";
		pauseLabel.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(pauseLabel);

		var resumeButton = new Button();
		resumeButton.Text = "Resume";
		resumeButton.Pressed += TogglePause;
		vbox.AddChild(resumeButton);

		var restartButton = new Button();
		restartButton.Text = "Restart";
		restartButton.Pressed += RestartGame;
		vbox.AddChild(restartButton);
	}

	private void SetupGameOverOverlay()
	{
		_gameOverOverlay = new Control();
		_gameOverOverlay.AnchorLeft = 0;
		_gameOverOverlay.AnchorTop = 0;
		_gameOverOverlay.AnchorRight = 1;
		_gameOverOverlay.AnchorBottom = 1;
		_gameOverOverlay.Visible = false;
		_uiLayer.AddChild(_gameOverOverlay);

		// Semi-transparent background
		var background = new ColorRect();
		background.AnchorLeft = 0;
		background.AnchorTop = 0;
		background.AnchorRight = 1;
		background.AnchorBottom = 1;
		background.Color = new Color(0, 0, 0, 0.8f);
		_gameOverOverlay.AddChild(background);

		// Game over panel
		var gameOverPanel = new Panel();
		gameOverPanel.AnchorLeft = 0.5f;
		gameOverPanel.AnchorTop = 0.5f;
		gameOverPanel.AnchorRight = 0.5f;
		gameOverPanel.AnchorBottom = 0.5f;
		gameOverPanel.OffsetLeft = -200;
		gameOverPanel.OffsetTop = -150;
		gameOverPanel.OffsetRight = 200;
		gameOverPanel.OffsetBottom = 150;
		_gameOverOverlay.AddChild(gameOverPanel);

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
		playAgainButton.Pressed += RestartGame;
		vbox.AddChild(playAgainButton);
	}

	// Game setup
	private void SetupGame()
	{
		// Create simple player
		_player = new Node2D();
		var playerSprite = new ColorRect();
		playerSprite.Size = new Vector2(40, 40);
		playerSprite.Color = Colors.Blue;
		playerSprite.Position = new Vector2(-20, -20);
		_player.AddChild(playerSprite);
		_player.Position = new Vector2(640, 360);
		AddChild(_player);

		// Setup spawn timer
		_spawnTimer = new Timer();
		_spawnTimer.WaitTime = SpawnInterval;
		_spawnTimer.Timeout += SpawnCollectible;
		AddChild(_spawnTimer);

		// Context will determine if we auto-start
		// Auto-start only happens in development context
	}

	// Context detection and adaptation
	private void DetectAndAdaptToContext()
	{
		// Check if GameHost autoload is available
		_gameHost = GameHost.GetInstance();
		
		if (_gameHost != null)
		{
			// Production context: GameHost autoload available
			_isProductionContext = true;
			
			// Get player session from GameHost
			var playerSession = _gameHost.GetPlayerSession("default");
			if (playerSession != null)
			{
				_playerId = playerSession.PlayerId;
			}
			else
			{
				// Fallback to GameHost's current player ID
				_playerId = _gameHost.GetCurrentPlayerId();
			}
			
			GD.Print($"SimpleSampleGame: Production context detected with GameHost autoload");
		}
		else
		{
			// Development context: standalone mode
			_isProductionContext = false;
			_playerId = "dev_player";
			
			// Auto-start for development iteration
			CallDeferred(MethodName.StartGame);
			
			GD.Print($"SimpleSampleGame: Development context detected - auto-starting");
		}
	}

	// Game lifecycle
	public void StartGame()
	{
		if (_gameActive) return;

		_gameActive = true;
		_gamePaused = false;
		_score = 0;
		_gameTime = 0.0f;
		_targetPosition = _player.Position;

		// Hide overlays
		_pauseOverlay.Visible = false;
		_gameOverOverlay.Visible = false;

		// Clear any existing collectibles
		foreach (var collectible in _collectibles)
		{
			if (GodotObject.IsInstanceValid(collectible))
				collectible.QueueFree();
		}
		_collectibles.Clear();

		_spawnTimer.Start();
		UpdateUI();

		// Optional GameHost notification
		EmitSignal(SignalName.PlayerAdded, _playerId);
		EmitSignal(SignalName.GameStarted);

		GD.Print("Simple Sample Game Started!");
	}

	public void EndGame()
	{
		if (!_gameActive) return;

		_gameActive = false;
		_gamePaused = false;
		_spawnTimer.Stop();

		// Show game over screen
		_finalScoreLabel.Text = $"Final Score: {_score}";
		_gameOverOverlay.Visible = true;

		// Optional GameHost notification
		EmitSignal(SignalName.GameEnded);

		GD.Print($"Game Ended! Final Score: {_score}");
	}

	public void PauseGame()
	{
		if (!_gameActive || _gamePaused) return;
		_gamePaused = true;
		_spawnTimer.Paused = true;
		_pauseOverlay.Visible = true;
		_pauseButton.Text = "Resume";
	}

	public void ResumeGame()
	{
		if (!_gameActive || !_gamePaused) return;
		_gamePaused = false;
		_spawnTimer.Paused = false;
		_pauseOverlay.Visible = false;
		_pauseButton.Text = "Pause";
	}

	public void TogglePause()
	{
		if (_gamePaused)
			ResumeGame();
		else
			PauseGame();
	}

	public void RestartGame()
	{
		_gameActive = false;
		_gamePaused = false;
		_spawnTimer.Stop();
		StartGame();
	}

	private void UpdateUI()
	{
		if (_scoreLabel != null)
			_scoreLabel.Text = $"Score: {_score}";
		
		if (_timerLabel != null)
		{
			float timeRemaining = GameDuration - _gameTime;
			_timerLabel.Text = $"Time: {timeRemaining:F1}s";
		}
	}

	// Game mechanics
	private void SpawnCollectible()
	{
		var viewport = GetViewport();
		if (viewport == null) return;

		var screenSize = viewport.GetVisibleRect().Size;
		var randomPos = new Vector2(
			(float)GD.RandRange(50, screenSize.X - 50),
			(float)GD.RandRange(50, screenSize.Y - 50)
		);

		// Create simple collectible
		var collectible = new Node2D();
		var collectibleSprite = new ColorRect();
		collectibleSprite.Size = new Vector2(20, 20);
		collectibleSprite.Color = Colors.Yellow;
		collectibleSprite.Position = new Vector2(-10, -10);
		collectible.AddChild(collectibleSprite);
		collectible.GlobalPosition = randomPos;
		
		// Store reference for collision checking
		collectible.SetMeta("point_value", 10);
		_collectibles.Add(collectible);
		
		AddChild(collectible);

		// Auto-remove after 10 seconds
		var destroyTimer = new Timer();
		destroyTimer.WaitTime = 10.0f;
		destroyTimer.OneShot = true;
		destroyTimer.Timeout += () => RemoveCollectible(collectible);
		collectible.AddChild(destroyTimer);
		destroyTimer.Start();
	}

	private void CheckCollectibleCollisions()
	{
		if (_player == null) return;

		var playerPos = _player.GlobalPosition;
		var collectibleRadius = 30.0f;

		for (int i = _collectibles.Count - 1; i >= 0; i--)
		{
			var collectible = _collectibles[i];
			if (!GodotObject.IsInstanceValid(collectible)) 
			{
				_collectibles.RemoveAt(i);
				continue;
			}

			var distance = playerPos.DistanceTo(collectible.GlobalPosition);
			if (distance < collectibleRadius)
			{
				// Collect the item
				var points = collectible.GetMeta("point_value", 10).AsInt32();
				_score += points;
				
				// Optional GameHost notification
				EmitSignal(SignalName.ScoreChanged, _playerId, _score);
				
				// Remove collectible
				RemoveCollectible(collectible);

				// Check win condition
				if (_score >= TargetScore)
				{
					EndGame();
					return;
				}
			}
		}
	}

	private void RemoveCollectible(Node2D collectible)
	{
		if (_collectibles.Contains(collectible))
		{
			_collectibles.Remove(collectible);
		}
		
		if (GodotObject.IsInstanceValid(collectible))
		{
			collectible.QueueFree();
		}
	}
}