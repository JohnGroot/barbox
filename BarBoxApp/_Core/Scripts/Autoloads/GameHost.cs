using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Simplified game lifecycle management for BarBox ecosystem
/// Focuses on essential game hosting without UI management complexity
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
	private SessionManager _sessionManager;
	private GameRegistry _gameRegistry;
	private SceneManager _sceneManager;
	private MainController _mainController;

	protected override void OnServiceReady()
	{
		Instance = this;
		
		_sessionManager = SessionManager.GetInstance();
		_gameRegistry = GetAutoload<GameRegistry>();
		_sceneManager = GetAutoload<SceneManager>();
		
		LogInfo("GameHost initialized");
	}

	/// <summary>
	/// Loads a game as an overlay on the current scene
	/// Simplified game loading without session management (handled by SessionManager)
	/// </summary>
	public void LoadGameOverlay(string gameId)
	{
		// Stop any current game first
		StopCurrentGame();

		// Validate game data
		var gameData = _gameRegistry?.GetGameData(gameId);
		if (gameData == null || !gameData.IsActive)
		{
			LogError($"Game {gameId} not found or inactive");
			return;
		}

		// Load game scene as overlay
		LoadGameScene(gameId, gameData.ScenePath);
	}

	/// <summary>
	/// Compatibility overload for old LoadGameOverlay signature
	/// </summary>
	public void LoadGameOverlay(string gameId, UserData userData)
	{
		// Just ignore the UserData parameter and call the new version
		// Session management is now handled separately
		LoadGameOverlay(gameId);
	}

	private void LoadGameScene(string gameId, string scenePath)
	{
		if (string.IsNullOrEmpty(scenePath))
		{
			LogError($"No scene path for game {gameId}");
			return;
		}

		// Load and instantiate game scene
		var scene = GD.Load<PackedScene>(scenePath);
		if (scene == null)
		{
			LogError($"Failed to load scene {scenePath}");
			return;
		}

		_currentGame = scene.Instantiate();
		_currentGameId = gameId;

		// Hide main menu UI when game starts
		var currentScene = GetTree().CurrentScene;
		_mainController = currentScene as MainController;
		_mainController?.HideMainUI();

		// Add as child of current scene (overlay pattern)
		currentScene?.AddChild(_currentGame);

		// Connect game signals for optional integration
		ConnectGameSignals(_currentGame);

		LogInfo($"Loaded {gameId} as overlay");
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

		// Show main menu UI when returning from game
		_mainController?.ShowMainUI();
		_mainController = null;

		_currentGameId = string.Empty;
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


	// Signal connection for optional game integration
	private void ConnectGameSignals(Node gameNode)
	{
		// Connect essential game lifecycle signals only
		TryConnectSignal(gameNode, "GameStarted", nameof(OnGameStarted), Callable.From(OnGameStarted));
		TryConnectSignal(gameNode, "GameEnded", nameof(OnGameEnded), Callable.From(OnGameEnded));
		TryConnectSignal(gameNode, "GamePaused", nameof(OnGamePaused), Callable.From(OnGamePaused));
		TryConnectSignal(gameNode, "GameResumed", nameof(OnGameResumed), Callable.From(OnGameResumed));
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

	// Public API for games and other systems
	public string GetCurrentGameId() => _currentGameId;
	public Node GetCurrentGame() => _currentGame;

	/// <summary>
	/// Compatibility method - redirects to SessionManager
	/// </summary>
	public PlayerSession GetPlayerSession(string playerId)
	{
		var sessionManager = SessionManager.GetInstance();
		var userSession = sessionManager?.GetUserSession(playerId) ?? sessionManager?.GetCurrentUserSession();
		
		if (userSession != null)
		{
			// Convert UserSession to PlayerSession for compatibility
			var userData = new UserData(userSession.PhoneNumber);
			if (userSession.GlobalData != null)
			{
				userData.Credits = userSession.GlobalData.GlobalCredits;
			}
			return new PlayerSession(userSession.PhoneNumber, userData);
		}
		
		return null;
	}

	/// <summary>
	/// UI management methods - connects to UIManager when available
	/// </summary>
	public void SetTopMenuContext(string gameTitle, ContextButtonData[] contextButtons = null)
	{
		var uiManager = UIManager.GetInstance();
		if (uiManager != null)
		{
			uiManager.SetGameContext(gameTitle, contextButtons);
			LogInfo($"SetTopMenuContext called for '{gameTitle}' with {contextButtons?.Length ?? 0} buttons");
		}
		else
		{
			LogInfo($"SetTopMenuContext called for '{gameTitle}' but UIManager not available (development mode)");
		}
	}

	public void ClearTopMenuContext()
	{
		var uiManager = UIManager.GetInstance();
		if (uiManager != null)
		{
			uiManager.ClearGameContext();
			LogInfo("ClearTopMenuContext called - game context cleared");
		}
		else
		{
			LogInfo("ClearTopMenuContext called but UIManager not available (development mode)");
		}
	}

	/// <summary>
	/// Sets the help content for the current game
	/// Passes through to UIManager for centralized help system management
	/// </summary>
	public void SetGameHelpContent(HelpContentData helpContent)
	{
		var uiManager = UIManager.GetInstance();
		if (uiManager != null)
		{
			uiManager.SetGameHelpContent(helpContent);
			LogInfo($"Game help content set: {helpContent.Title}");
		}
		else
		{
			LogInfo("SetGameHelpContent called but UIManager not available (development mode)");
		}
	}

	/// <summary>
	/// Shows or hides the game help button
	/// Passes through to UIManager for centralized help system management
	/// </summary>
	public void ShowGameHelp(bool show)
	{
		var uiManager = UIManager.GetInstance();
		if (uiManager != null)
		{
			uiManager.ShowHelpButton(show);
			LogInfo($"Game help button visibility set: {show}");
		}
		else
		{
			LogInfo("ShowGameHelp called but UIManager not available (development mode)");
		}
	}

	public string GetCurrentPlayerId()
	{
		var sessionManager = SessionManager.GetInstance();
		var session = sessionManager?.GetCurrentUserSession();
		return session?.PhoneNumber ?? "unknown";
	}

	/// <summary>
	/// Static method for games to easily check if GameHost is available
	/// Returns null if GameHost autoload is not present (development mode)
	/// </summary>
	public static GameHost GetInstance()
	{
		return GetAutoload<GameHost>();
	}

	// ============================================================================
	// Build Context Detection Utilities
	// ============================================================================

	/// <summary>
	/// Determines if the game was launched from the Godot editor (Play button)
	/// This includes both editor play mode and debug builds launched from editor
	/// </summary>
	/// <returns>True if launched from editor, false otherwise</returns>
	public static bool IsLaunchedFromEditor()
	{
		const string editor = "editor";
		return OS.HasFeature(editor);
	}

	/// <summary>
	/// Determines if the game is running as an exported standalone build
	/// This is true for all exported builds (debug or release)
	/// </summary>
	/// <returns>True if exported build, false otherwise</returns>
	public static bool IsExportedBuild()
	{
		const string standalone = "standalone";
		return OS.HasFeature(standalone);
	}

	/// <summary>
	/// Determines if code is running in editor tool script context
	/// This is true during editor tool execution, @tool scripts, editor extensions
	/// </summary>
	/// <returns>True if in editor tool context, false otherwise</returns>
	public static bool IsEditorToolContext()
	{
		return Engine.IsEditorHint();
	}

	/// <summary>
	/// Determines if the game is running in any development context
	/// Includes both editor play mode and tool script contexts
	/// </summary>
	/// <returns>True if in development context, false for production</returns>
	public static bool IsDevelopmentContext()
	{
		return IsLaunchedFromEditor() || IsEditorToolContext();
	}

	// ============================================================================
	// Production/Development Context Detection
	// ============================================================================

	/// <summary>
	/// Determines if the game is running in production context
	/// Production context = exported standalone build
	/// </summary>
	/// <returns>True if in production context (exported build), false for development</returns>
	public static bool IsProductionContext()
	{
		// Production = exported standalone build
		return IsExportedBuild();
	}

	/// <summary>
	/// Determines if credit costs should be bypassed for the current context
	/// Credits are bypassed in any development context (editor play or tool scripts)
	/// </summary>
	/// <returns>True if credits should be bypassed (development), false if credits are required (production)</returns>
	public static bool ShouldBypassCredits()
	{
		// Bypass credits in any development context
		return IsDevelopmentContext();
	}

	/// <summary>
	/// Gets a description of the current context for debugging purposes
	/// </summary>
	/// <returns>String describing the current context</returns>
	public static string GetContextDescription()
	{
		if (IsEditorToolContext())
		{
			return "Development: Editor tool script context";
		}

		if (IsLaunchedFromEditor())
		{
			return "Development: Launched from Godot editor";
		}

		if (IsExportedBuild())
		{
			return "Production: Exported standalone build";
		}

		return "Unknown: Unable to determine build context";
	}

	protected override void OnServiceDestroyed()
	{
		// Clean up current game if running
		if (_currentGame != null)
		{
			StopCurrentGame();
		}
		
		Instance = null;
	}
}