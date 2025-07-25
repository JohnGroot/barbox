using Godot;
using System.Collections.Generic;
using System.Linq;

[GlobalClass]
public abstract partial class GameController : Node2D, IGameUIIntegration
{
	[Signal] public delegate void GameStartedEventHandler();
	[Signal] public delegate void GameEndedEventHandler();
	[Signal] public delegate void GamePausedEventHandler();
	[Signal] public delegate void GameResumedEventHandler();
	[Signal] public delegate void ScoreChangedEventHandler(string playerId, float score);
	[Signal] public delegate void PlayerAddedEventHandler(string playerId);

	[ExportCategory("Game Settings")]
	[Export] public string GameId { get; set; } = string.Empty;
	[Export] public int TimeLimit { get; set; } = 0; // 0 = no time limit
	[Export] public bool CanPause { get; set; } = true;

	// Game modes for racing/sports games
	public enum GameMode { Practice, TimeTrial, Tournament }
	
	protected GameMetadata _gameMetadata;
	protected List<BasePlayer> _players = new();
	protected Dictionary<string, float> _playerScores = new(); // Changed to string keys and float scores for time-based games
	protected bool _isGameActive = false;
	protected bool _isGamePaused = false;
	protected GameMode _currentGameMode = GameMode.Practice;
	protected float _gameTime = 0.0f;
	protected Timer _gameTimer;
	private GameHost _gameHost;

	public override void _Ready()
	{
		_gameMetadata = GameRegistry.GetAutoload()?.GetGameData(GameId);
		_gameHost = GameHost.GetInstance();
		
		SetupGameTimer();
		InitializeGame();
		SetupUI();
	}

	public override void _Process(double delta)
	{
		if (_isGameActive && !_isGamePaused)
		{
			_gameTime += (float)delta;
			UpdateGame((float)delta);
		}
	}

	protected virtual void InitializeGame()
	{
	}

	protected virtual void UpdateGame(float delta)
	{
	}

	protected virtual void SetupGameTimer()
	{
		if (TimeLimit > 0)
		{
			_gameTimer = new Timer();
			_gameTimer.WaitTime = TimeLimit;
			_gameTimer.OneShot = true;
			_gameTimer.Timeout += OnTimeUp;
			AddChild(_gameTimer);
		}
	}

	public virtual void StartGame()
	{
		if (!_isGameActive)
		{
			_isGameActive = true;
			_isGamePaused = false;
			_gameTime = 0.0f;
			_playerScores.Clear();
			
			// Initialize scores for all players
			foreach (var player in _players)
			{
				_playerScores[player.PlayerId] = 0.0f;
			}
			
			if (_gameTimer != null)
			{
				_gameTimer.Start();
			}
			
			OnGameStarted();
			EmitSignal(SignalName.GameStarted);
		}
	}

	public virtual void EndGame()
	{
		if (_isGameActive)
		{
			_isGameActive = false;
			_isGamePaused = false;
			
			if (_gameTimer != null)
			{
				_gameTimer.Stop();
			}
			
			SaveScores();
			OnGameEnded();
			EmitSignal(SignalName.GameEnded);
		}
	}

	public virtual void PauseGame()
	{
		if (_isGameActive && CanPause && !_isGamePaused)
		{
			_isGamePaused = true;
			
			if (_gameTimer != null)
			{
				_gameTimer.Paused = true;
			}
			
			OnGamePaused();
			EmitSignal(SignalName.GamePaused);
		}
	}

	public virtual void ResumeGame()
	{
		if (_isGameActive && _isGamePaused)
		{
			_isGamePaused = false;
			
			if (_gameTimer != null)
			{
				_gameTimer.Paused = false;
			}
			
			OnGameResumed();
			EmitSignal(SignalName.GameResumed);
		}
	}

	protected virtual void OnGameStarted()
	{
	}

	protected virtual void OnGameEnded()
	{
	}

	protected virtual void OnGamePaused()
	{
	}

	protected virtual void OnGameResumed()
	{
	}

	protected virtual void OnTimeUp()
	{
		EndGame();
	}

	public virtual void UpdateScore(string playerId, float score)
	{
		if (_playerScores.ContainsKey(playerId))
		{
			_playerScores[playerId] = score;
			EmitSignal(SignalName.ScoreChanged, playerId, score);
		}
	}

	public virtual void AddScore(string playerId, float points)
	{
		if (_isGameActive && _playerScores.ContainsKey(playerId))
		{
			_playerScores[playerId] += points;
			EmitSignal(SignalName.ScoreChanged, playerId, _playerScores[playerId]);
		}
	}

	public virtual void AddPlayer(BasePlayer player)
	{
		if (!_players.Contains(player))
		{
			_players.Add(player);
			_playerScores[player.PlayerId] = 0.0f;
			EmitSignal(SignalName.PlayerAdded, player.PlayerId);
		}
	}

	public virtual void RemovePlayer(BasePlayer player)
	{
		if (_players.Contains(player))
		{
			_players.Remove(player);
			_playerScores.Remove(player.PlayerId);
		}
	}

	private void SaveScores()
	{
		var userManager = UserManager.GetAutoload();
		if (userManager != null && !string.IsNullOrEmpty(GameId))
		{
			foreach (var player in _players)
			{
				if (GodotObject.IsInstanceValid(player))
				{
					var userData = player.GetUserData();
					if (userData != null && _playerScores.ContainsKey(player.PlayerId))
					{
						// Convert float score to int for UserManager compatibility
						int intScore = (int)_playerScores[player.PlayerId];
						userManager.UpdateHighScore(GameId, intScore);
					}
				}
			}
		}
	}

	public float GetPlayerScore(string playerId) => _playerScores.GetValueOrDefault(playerId, 0.0f);
	public Dictionary<string, float> GetAllScores() => new Dictionary<string, float>(_playerScores);
	public List<BasePlayer> GetPlayers() => new List<BasePlayer>(_players);
	public int GetPlayerCount() => _players.Count;
	public float GetGameTime() => _gameTime;
	public bool IsGameActive() => _isGameActive;
	public bool IsGamePaused() => _isGamePaused;
	public GameMode GetGameMode() => _currentGameMode;
	public void SetGameMode(GameMode mode) => _currentGameMode = mode;
	public float GetTimeRemaining() => (float)(_gameTimer?.TimeLeft ?? 0.0f);

	// ============================================================================
	// UI Integration
	// ============================================================================

	/// <summary>
	/// Set up UI integration when the game starts
	/// </summary>
	private void SetupUI()
	{
		if (_gameHost != null)
		{
			// Set up the game context in the top menu
			var contextButtons = GetContextButtons();
			_gameHost.SetTopMenuContext(GetGameTitle(), contextButtons);
			OnUIContextSetup();
			
			GD.Print($"[GameController] UI context set up for '{GetGameTitle()}' with {contextButtons?.Length ?? 0} buttons");
		}
		else
		{
			GD.Print("[GameController] GameHost not available - UI integration skipped (development mode)");
		}
	}

	/// <summary>
	/// Clean up UI integration when the game ends
	/// </summary>
	private void CleanupUI()
	{
		if (_gameHost != null)
		{
			_gameHost.ClearTopMenuContext();
			OnUIContextTeardown();
			GD.Print("[GameController] UI context cleaned up");
		}
	}

	public override void _ExitTree()
	{
		// Disconnect timer signal if it exists
		if (GodotObject.IsInstanceValid(_gameTimer))
		{
			_gameTimer.Timeout -= OnTimeUp;
		}
		
		CleanupUI();
		base._ExitTree();
	}

	// ============================================================================
	// IGameUIIntegration Implementation
	// ============================================================================

	/// <summary>
	/// Gets the display title for the game in the top menu bar
	/// Override this to provide a custom title, defaults to game metadata display name
	/// </summary>
	public virtual string GetGameTitle()
	{
		return _gameMetadata?.DisplayName ?? GameId;
	}

	/// <summary>
	/// Gets the context buttons this game wants to display in the top menu bar
	/// Override this to provide custom buttons for your game
	/// </summary>
	public virtual ContextButtonData[] GetContextButtons()
	{
		var buttons = new List<ContextButtonData>();

		// Standard "Return to Menu" button
		buttons.Add(GameContextButton.CreateReturnToMenuButton(() => {
			var userManager = UserManager.GetAutoload();
			userManager?.ResetUserIdleTimer();
			ReturnToMainMenu();
		}));

		// Add pause/resume button if the game supports pausing
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
	/// Called when the game's UI context is being set up
	/// Override this to perform any additional UI initialization
	/// </summary>
	public virtual void OnUIContextSetup()
	{
	}

	/// <summary>
	/// Called when the game's UI context is being torn down
	/// Override this to perform any UI cleanup
	/// </summary>
	public virtual void OnUIContextTeardown()
	{
	}

	/// <summary>
	/// Called to update the game's UI state (e.g., button enabled/disabled states)
	/// Override this to customize UI state updates
	/// </summary>
	public virtual void UpdateUIState()
	{
		// Refresh the context buttons with updated state
		RefreshUI();
	}

	/// <summary>
	/// Refresh the UI context with current game state
	/// Call this when game state changes that affect UI (pause/resume, etc.)
	/// </summary>
	protected void RefreshUI()
	{
		if (_gameHost != null)
		{
			var contextButtons = GetContextButtons();
			_gameHost.SetTopMenuContext(GetGameTitle(), contextButtons);
		}
	}

	/// <summary>
	/// Return to the main menu - can be overridden for custom behavior
	/// </summary>
	protected virtual void ReturnToMainMenu()
	{
		_gameHost?.ReturnToMainMenu();
	}
}