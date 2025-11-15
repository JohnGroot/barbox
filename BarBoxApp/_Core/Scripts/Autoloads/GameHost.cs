using Godot;
using System;
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

	private GameController _currentGame;
	private string _currentGameId = string.Empty;
	private SessionManager _sessionManager;
	private GameRegistry _gameRegistry;
	private SceneManager _sceneManager;
	private MainController _mainController;

	protected override void OnServiceEnterTree()
	{

		// All autoloads guaranteed to exist after _EnterTree phase
		_sessionManager = SessionManager.GetInstance();
		_gameRegistry = GetAutoload<GameRegistry>();
		_sceneManager = GetAutoload<SceneManager>();

		// Production validation - fail fast if required services missing
		if (IsProductionContext())
		{
			if (_gameRegistry == null || _sessionManager == null || _sceneManager == null)
				throw new InvalidOperationException("Required services not configured for GameHost");
		}

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

		// Validate game data (GameRegistry guaranteed to exist)
		var gameData = _gameRegistry.GetGameData(gameId);
		if (gameData == null || !gameData.IsActive)
		{
			LogError($"Game {gameId} not found or inactive");
			return;
		}

		// Load game scene as overlay
		LoadGameScene(gameId, gameData.ScenePath);
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

		_currentGame = scene.Instantiate<GameController>();
		_currentGameId = gameId;

		// Hide main menu UI when game starts
		var currentScene = GetTree().CurrentScene;
		_mainController = currentScene as MainController;
		_mainController?.HideMainUI();

		// Add as child of current scene (overlay pattern)
		// Game's _Ready() will call SetTopMenuContext() during initialization
		currentScene?.AddChild(_currentGame);

		// Show top menu bar AFTER game has initialized and set context
		// This ensures visibility propagation happens with correct child states
		var uiManager = UIManager.GetInstance();
		uiManager?.SetTopMenuVisible(true);

		LogInfo($"Loaded {gameId} as overlay");
	}

	public async void StopCurrentGame()
	{
		if (_currentGame != null)
		{
			// Games are responsible for calling NotifyGameEnded() when they end
			// We just emit the platform signal and clean up
			EmitSignal(SignalName.GameEnded, _currentGameId);
			_currentGame.QueueFree();
			_currentGame = null;
		}

		// Log out all non-primary users when exiting game
		if (_sessionManager != null)
		{
			await _sessionManager.LogoutNonPrimaryUsersAsync();
		}

		// Show main menu UI when returning from game
		_mainController?.ShowMainUI();
		_mainController = null;

		_currentGameId = string.Empty;
	}

	/// <summary>
	/// Attempts to pause the current game by calling game-specific pause methods
	/// Games should implement their own pause logic (PauseRace, etc.)
	/// </summary>
	public void PauseGame()
	{
		// Try common pause method names
		if (_currentGame?.HasMethod("PauseRace") == true)
		{
			_currentGame.Call("PauseRace");
		}
		else if (_currentGame?.HasMethod("PauseGame") == true)
		{
			_currentGame.Call("PauseGame");
		}
	}

	/// <summary>
	/// Attempts to resume the current game by calling game-specific resume methods
	/// Games should implement their own resume logic (ResumeRace, etc.)
	/// </summary>
	public void ResumeGame()
	{
		// Try common resume method names
		if (_currentGame?.HasMethod("ResumeRace") == true)
		{
			_currentGame.Call("ResumeRace");
		}
		else if (_currentGame?.HasMethod("ResumeGame") == true)
		{
			_currentGame.Call("ResumeGame");
		}
	}

	public void ReturnToMainMenu()
	{
		StopCurrentGame();
		_sceneManager.ReturnToMainMenu();
	}


	// ============================================================================
	// Platform Integration - Games call these to emit platform-level signals
	// ============================================================================

	/// <summary>
	/// Games call this when their domain-specific game session starts
	/// (e.g., race starts, mining session starts, round starts)
	/// Emits platform-level GameStarted signal for platform services
	/// </summary>
	public void NotifyGameStarted()
	{
		LogInfo($"{_currentGameId} started");
		EmitSignal(SignalName.GameStarted, _currentGameId);
	}

	/// <summary>
	/// Games call this when their domain-specific game session ends
	/// (e.g., race ends, mining session ends, round ends)
	/// Emits platform-level GameEnded signal for platform services
	/// </summary>
	public void NotifyGameEnded()
	{
		LogInfo($"{_currentGameId} ended");
		EmitSignal(SignalName.GameEnded, _currentGameId);
	}

	/// <summary>
	/// Games call this when their domain-specific game session pauses
	/// (e.g., race pauses)
	/// Emits platform-level GamePaused signal for platform services
	/// </summary>
	public void NotifyGamePaused()
	{
		LogInfo($"{_currentGameId} paused");
		EmitSignal(SignalName.GamePaused, _currentGameId);
	}

	/// <summary>
	/// Games call this when their domain-specific game session resumes
	/// (e.g., race resumes)
	/// Emits platform-level GameResumed signal for platform services
	/// </summary>
	public void NotifyGameResumed()
	{
		LogInfo($"{_currentGameId} resumed");
		EmitSignal(SignalName.GameResumed, _currentGameId);
	}

	// Public API for games and other systems
	public string GetCurrentGameId() => _currentGameId;
	public GameController GetCurrentGame() => _currentGame;

	/// <summary>
	/// Register a game as the current game.
	/// Called by GameController during direct scene loading.
	/// LoadGameOverlay() takes precedence - this is a fallback for development.
	/// </summary>
	public void RegisterCurrentGame(GameController game)
	{
		// Only register if no game is currently loaded via LoadGameOverlay
		if (_currentGame == null)
		{
			_currentGame = game;
			_currentGameId = game.GameId;
			LogInfo($"Game self-registered: {_currentGameId}");
		}
	}

	/// <summary>
	/// Get user session - redirects to SessionManager
	/// Falls back to primary user if specific playerId not found (single-user game fallback)
	/// </summary>
	public UserSession GetUserSession(string playerId)
	{
		var sessionManager = SessionManager.GetInstance();
		return sessionManager.GetUserSession(playerId) ?? sessionManager.GetPrimaryUserSession();
	}

	/// <summary>
	/// UI management methods - connects to UIManager when available
	/// </summary>
	public void SetTopMenuContext(string gameTitle, ContextButtonData[] contextButtons = null)
	{
		var uiManager = UIManager.GetInstance();
		uiManager.SetGameContext(gameTitle, contextButtons);
	}

	public void ClearTopMenuContext()
	{
		var uiManager = UIManager.GetInstance();
		uiManager.ClearGameContext();
	}

	/// <summary>
	/// Sets the help content for the current game
	/// Passes through to UIManager for centralized help system management
	/// </summary>
	public void SetGameHelpContent(HelpContentData helpContent)
	{
		var uiManager = UIManager.GetInstance();
		uiManager.SetGameHelpContent(helpContent);
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
		// Get primary user for single-user game contexts
		var sessionManager = SessionManager.GetInstance();
		var session = sessionManager?.GetPrimaryUserSession();
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
	}
}