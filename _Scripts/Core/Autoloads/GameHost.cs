using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// AutoLoad singleton for game orchestration and framework integration
/// Provides optional services for games - always available but never required
/// Games work perfectly without GameHost, but get enhanced features when it's present
/// </summary>
public partial class GameHost : AutoloadBase
{
	[Signal] public delegate void GameStartedEventHandler(string gameId);
	[Signal] public delegate void GameEndedEventHandler(string gameId);
	[Signal] public delegate void GamePausedEventHandler(string gameId);
	[Signal] public delegate void GameResumedEventHandler(string gameId);

	public static GameHost Instance { get; private set; }

	private Node _currentGame;
	private string _currentGameId = string.Empty;
	private Dictionary<string, PlayerSession> _playerSessions = new();
	private UserManager _userManager;
	private GameRegistry _gameRegistry;
	private SceneManager _sceneManager;

	protected override void OnServiceReady()
	{
		Instance = this;
		
		_userManager = GetAutoload<UserManager>();
		_gameRegistry = GetAutoload<GameRegistry>();
		_sceneManager = GetAutoload<SceneManager>();
	}

	/// <summary>
	/// Loads a game as an overlay on the current scene
	/// Handles user context, credit deduction, and PlayerSession creation
	/// </summary>
	public void LoadGameOverlay(string gameId, UserData userData)
	{
		// Stop any current game first
		StopCurrentGame();

		// Validate game data
		var gameData = _gameRegistry?.GetGameData(gameId);
		if (gameData == null || !gameData.IsActive)
		{
			GD.PrintErr($"GameHost: Game {gameId} not found or inactive");
			return;
		}

		// Handle credit deduction
		if (_userManager != null && userData != null)
		{
			var currentUser = _userManager.GetCurrentUser();
			if (currentUser != null && currentUser.Credits >= gameData.CreditCost)
			{
				_userManager.SpendCredits(gameData.CreditCost);
				GD.Print($"GameHost: Deducted {gameData.CreditCost} credits for {gameId}");
			}
			else
			{
				GD.PrintErr("GameHost: Insufficient credits or no user logged in");
				return;
			}
		}

		// Create player session
		if (userData != null)
		{
			AddPlayerSession(userData.UserId, userData);
		}

		// Load game scene as overlay
		LoadGameScene(gameId, gameData.ScenePath);
	}

	private void LoadGameScene(string gameId, string scenePath)
	{
		if (string.IsNullOrEmpty(scenePath))
		{
			GD.PrintErr($"GameHost: No scene path for game {gameId}");
			return;
		}

		// Load and instantiate game scene
		var scene = GD.Load<PackedScene>(scenePath);
		if (scene == null)
		{
			GD.PrintErr($"GameHost: Failed to load scene {scenePath}");
			return;
		}

		_currentGame = scene.Instantiate();
		_currentGameId = gameId;

		// Add as child of current scene (overlay pattern)
		var currentScene = GetTree().CurrentScene;
		currentScene?.AddChild(_currentGame);

		// Connect game signals for optional integration
		ConnectGameSignals(_currentGame);

		GD.Print($"GameHost: Loaded {gameId} as overlay");
	}

	public void StopCurrentGame()
	{
		if (_currentGame != null)
		{
			// Try to call EndGame on the hosted game
			if (_currentGame.HasMethod("EndGame"))
			{
				_currentGame.Call("EndGame");
			}

			EmitSignal(SignalName.GameEnded, _currentGameId);
			_currentGame.QueueFree();
			_currentGame = null;
		}

		_currentGameId = string.Empty;
		_playerSessions.Clear();
	}

	public void PauseGame()
	{
		if (_currentGame?.HasMethod("PauseGame") == true)
		{
			_currentGame.Call("PauseGame");
		}
	}

	public void ResumeGame()
	{
		if (_currentGame?.HasMethod("ResumeGame") == true)
		{
			_currentGame.Call("ResumeGame");
		}
	}

	public void ReturnToMainMenu()
	{
		StopCurrentGame();
		_sceneManager?.ReturnToMainMenu();
	}

	// PlayerSession management
	public void AddPlayerSession(string playerId, UserData userData = null)
	{
		if (!_playerSessions.ContainsKey(playerId))
		{
			var session = new PlayerSession(playerId, userData);
			_playerSessions[playerId] = session;
		}
	}

	public void RemovePlayerSession(string playerId)
	{
		_playerSessions.Remove(playerId);
	}

	// Signal connection for optional game integration
	private void ConnectGameSignals(Node gameNode)
	{
		// Connect signals using nameof for compile-time method validation
		TryConnectSignal(gameNode, "GameStarted", nameof(OnGameStarted), Callable.From(OnGameStarted));
		TryConnectSignal(gameNode, "GameEnded", nameof(OnGameEnded), Callable.From(OnGameEnded));
		TryConnectSignal(gameNode, "GamePaused", nameof(OnGamePaused), Callable.From(OnGamePaused));
		TryConnectSignal(gameNode, "GameResumed", nameof(OnGameResumed), Callable.From(OnGameResumed));
		TryConnectSignal(gameNode, "ScoreChanged", nameof(OnScoreChanged), Callable.From<string, int>(OnScoreChanged));
		TryConnectSignal(gameNode, "PlayerAdded", nameof(OnPlayerAdded), Callable.From<string>(OnPlayerAdded));
		TryConnectSignal(gameNode, "PlayerRemoved", nameof(OnPlayerRemoved), Callable.From<string>(OnPlayerRemoved));
	}

	// Signal handlers
	private void OnGameStarted()
	{
		LogInfo($"{_currentGameId} started");
		EmitSignal(SignalName.GameStarted, _currentGameId);
	}

	private void OnGameEnded()
	{
		LogInfo($"{_currentGameId} ended");
		SaveScores();
		EmitSignal(SignalName.GameEnded, _currentGameId);
	}

	private void OnGamePaused()
	{
		LogInfo($"{_currentGameId} paused");
		EmitSignal(SignalName.GamePaused, _currentGameId);
	}

	private void OnGameResumed()
	{
		LogInfo($"{_currentGameId} resumed");
		EmitSignal(SignalName.GameResumed, _currentGameId);
	}

	private void OnScoreChanged(string playerId, int newScore)
	{
		LogInfo($"Player {playerId} score: {newScore}");
	}

	private void OnPlayerAdded(string playerId)
	{
		if (!_playerSessions.ContainsKey(playerId))
		{
			AddPlayerSession(playerId);
		}
	}

	private void OnPlayerRemoved(string playerId)
	{
		RemovePlayerSession(playerId);
	}

	private void SaveScores()
	{
		// Optional: integrate with UserManager for global high scores
		if (_userManager != null && !string.IsNullOrEmpty(_currentGameId))
		{
			LogInfo($"Could save scores for {_currentGameId}");
		}
	}

	// Public API for games and other systems
	public string GetCurrentGameId() => _currentGameId;
	public Node GetCurrentGame() => _currentGame;
	
	public PlayerSession GetPlayerSession(string playerId) 
	{
		// Support "default" lookup for single-player games
		if (playerId == "default" && _playerSessions.Count == 1)
		{
			return _playerSessions.Values.First();
		}
		return _playerSessions.GetValueOrDefault(playerId);
	}

	public string GetCurrentPlayerId()
	{
		return _playerSessions.Count == 1 ? _playerSessions.Keys.First() : "unknown";
	}

	public Dictionary<string, PlayerSession> GetAllPlayerSessions() => new Dictionary<string, PlayerSession>(_playerSessions);

	/// <summary>
	/// Static method for games to easily check if GameHost is available
	/// Returns null if GameHost autoload is not present (development mode)
	/// </summary>
	public static GameHost GetInstance()
	{
		return GetAutoload<GameHost>();
	}
}