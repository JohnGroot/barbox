using BarBox.Core.Gameplay;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BarBox.Core.Autoloads;

/// <summary>
/// Simplified game lifecycle management for BarBox ecosystem
/// Focuses on essential game hosting without UI management complexity
/// </summary>
public partial class GameHost : AutoloadBase
{
	private const string FEATURE_EDITOR = "editor";
	private const string FEATURE_STANDALONE = "standalone";

	[Signal] public delegate void GameStartedEventHandler(string gameId);
	[Signal] public delegate void GameEndedEventHandler(string gameId);

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

		// Validate game data (GameRegistry guaranteed to exist). GameRegistry's
		// own boot-time check means an unregistered id here is a caller bug,
		// not a data problem - fail loudly rather than silently no-op.
		var gameData = _gameRegistry.GetGameData(gameId);
		if (gameData == null)
		{
			throw new InvalidOperationException($"[GameHost] Unknown game id: {gameId}");
		}
		if (!gameData.IsActive)
		{
			LogError($"Game {gameId} is inactive");
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
		if (currentScene is MainController mainController)
		{
			_mainController = mainController;
			mainController.HideMainUI();
		}

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
		try
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
		catch (Exception ex)
		{
			LogError($"Error stopping game: {ex.Message}");
		}
	}

	/// <summary>
	/// Games override OnPause() to implement game-specific pause behavior
	/// </summary>
	public void PauseGame()
	{
		if (_currentGame == null) 
			return;

		_currentGame.Pause();
	}

	/// <summary>
	/// Games override OnResume() to implement game-specific resume behavior
	/// </summary>
	public void ResumeGame()
	{
		if (_currentGame == null) 
			return;

		_currentGame.Resume();
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
	/// Called when domain-specific game session starts (e.g., race starts, mining session starts)
	/// Emits platform-level GameStarted signal for platform services
	/// </summary>
	public void NotifyGameStarted()
	{
		LogInfo($"{_currentGameId} started");
		EmitSignal(SignalName.GameStarted, _currentGameId);
	}

	/// <summary>
	/// Called when domain-specific game session ends (e.g., race ends, mining session ends)
	/// Emits platform-level GameEnded signal for platform services
	/// </summary>
	public void NotifyGameEnded()
	{
		LogInfo($"{_currentGameId} ended");
		EmitSignal(SignalName.GameEnded, _currentGameId);
	}

	// Public API for games and other systems
	public string GetCurrentGameId() => _currentGameId;
	public GameController GetCurrentGame() => _currentGame;

	/// <summary>
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
	/// Falls back to primary user if specific playerId not found (single-user game fallback)
	/// </summary>
	public UserSession GetUserSession(string playerId)
	{
		var sessionManager = SessionManager.GetInstance();
		return sessionManager.GetSessionByPhone(playerId) ?? sessionManager.GetPrimarySession();
	}

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
	/// Passes through to UIManager for centralized help system management
	/// </summary>
	public void SetGameHelpContent(HelpContentData helpContent)
	{
		var uiManager = UIManager.GetInstance();
		uiManager.SetGameHelpContent(helpContent);
	}

	/// <summary>
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
		var session = sessionManager?.GetPrimarySession();
		return session?.PhoneNumber ?? "unknown";
	}

	/// <summary>
	/// Returns null if GameHost autoload is not present (development mode)
	/// </summary>
	public static GameHost GetInstance()
	{
		return GetAutoload<GameHost>();
	}

	// ============================================================================
	// Build Context Detection Utilities
	// Delegates to BuildContext for cached values
	// ============================================================================

	/// <summary>
	/// Includes both editor play mode and debug builds launched from editor
	/// </summary>
	public static bool IsLaunchedFromEditor() => BuildContext.IsLaunchedFromEditor;

	/// <summary>
	/// True for all exported builds (debug or release)
	/// </summary>
	public static bool IsExportedBuild() => BuildContext.IsExportedBuild;

	/// <summary>
	/// True during editor tool execution, @tool scripts, editor extensions
	/// </summary>
	public static bool IsEditorToolContext() => BuildContext.IsEditorToolContext;

	/// <summary>
	/// Includes both editor play mode and tool script contexts
	/// </summary>
	public static bool IsDevelopmentContext() => BuildContext.IsDevelopment;

	// ============================================================================
	// Production/Development Context Detection
	// ============================================================================

	/// <summary>
	/// Production context = exported standalone build
	/// </summary>
	public static bool IsProductionContext() => BuildContext.IsProduction;

	/// <summary>
	/// Credits are bypassed in any development context (editor play or tool scripts)
	/// </summary>
	public static bool ShouldBypassCredits() => BuildContext.ShouldBypassCredits;

	/// <summary>
	/// Get a human-readable description of the current build context
	/// </summary>
	public static string GetContextDescription() => BuildContext.GetContextDescription();

	protected override void OnServiceDestroyed()
	{
		// Clean up current game if running
		if (_currentGame != null)
		{
			StopCurrentGame();
		}
	}
}