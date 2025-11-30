using Godot;
using System.Collections.Generic;

[GlobalClass]
public abstract partial class GameController : Node2D
{
	/// <summary>
	/// Returns the game ID for registry lookup. Must match an entry in GameRegistry.
	/// </summary>
	protected abstract string GetGameId();

	/// <summary>
	/// Public accessor for game ID (for external callers like GameHost)
	/// </summary>
	public string GameId => GetGameId();

	/// <summary>
	/// Whether players can logout during active gameplay.
	/// Override this in your game to allow logout during play (default: false).
	/// Games should check their domain-specific state (IsRaceActive, IsPlayerLoggedIn, etc.)
	/// </summary>
	public virtual bool CanLogout => true;

	protected GameMetadata _gameMetadata;
	protected GameHost _gameHost;

	/// <summary>
	/// Godot lifecycle method - orchestrates all initialization phases synchronously.
	/// Games can override this but should call base._Ready() first.
	/// </summary>
	public override void _Ready()
	{
		// Phase 1: Service Discovery
		DiscoverServices();

		// Phase 2: Core Setup
		SetupCore();

		// Phase 3: Game Context Setup
		SetupGameContext();

		// Phase 4: Activation Decision
		ActivateGame();
	}

	/// <summary>
	/// PHASE 1: Service Discovery
	/// Discovers platform services and loads game metadata.
	/// Override to discover additional game-specific services.
	/// Called first in initialization sequence - no components are created yet.
	///
	/// POST-GUARANTEES:
	/// - _gameMetadata is not null (throws if missing)
	/// - _gameHost is not null in production context
	/// - All autoloads are guaranteed available (initialized in _EnterTree)
	/// </summary>
	protected virtual void DiscoverServices()
	{
		// Services guaranteed available - autoloads initialized in _EnterTree
		_gameMetadata = GameRegistry.GetAutoload().GetGameData(GameId);
		_gameHost = GameHost.GetInstance();

		// Register with GameHost for direct scene loading support
		_gameHost?.RegisterCurrentGame(this);
	}

	/// <summary>
	/// PHASE 2: Core Setup
	/// Initializes game components.
	/// Override InitializeComponents() rather than this method.
	/// </summary>
	private void SetupCore()
	{
		InitializeComponents();
	}

	/// <summary>
	/// PHASE 2 Extension Point: Component Initialization
	/// Override this to create game-specific components (engine, state, UI).
	/// Called after service discovery but before UI integration.
	///
	/// POST-GUARANTEES:
	/// - All game components exist and are valid
	/// - Configuration loaded and validated
	/// - Components ready for gameplay
	/// </summary>
	protected virtual void InitializeComponents() { }

	/// <summary>
	/// PHASE 4: Activation Decision
	/// Determines if game should auto-start or wait for user input.
	/// Override to implement game-specific activation logic.
	/// All components are guaranteed to exist when this is called.
	///
	/// Note: Games are responsible for their own lifecycle management.
	/// Implement domain-specific state checks (IsRaceActive, IsPlayerLoggedIn, etc.)
	/// and lifecycle methods (StartRace, EndRace, StartMiningSession, etc.)
	/// </summary>
	protected virtual void ActivateGame() { }

	/// <summary>
	/// Override this method to provide help content for your game
	/// This content will be displayed in the help menu overlay
	/// </summary>
	protected virtual HelpContentData GetHelpContent()
	{
		return new HelpContentData("Game Help")
			.AddSection("🎮 Basic Controls", "Touch or click to interact with the game.");
	}

	/// <summary>
	/// Indicates whether the game is currently paused.
	/// </summary>
	public bool IsPaused { get; protected set; }

	/// <summary>
	/// Pauses the game. Sets IsPaused to true and calls OnPause() for derived classes.
	/// Override OnPause() to implement game-specific pause behavior.
	/// </summary>
	public void Pause()
	{
		if (IsPaused) 
			return;

		IsPaused = true;
		OnPause();
	}

	/// <summary>
	/// Resumes the game. Sets IsPaused to false and calls OnResume() for derived classes.
	/// Override OnResume() to implement game-specific resume behavior.
	/// </summary>
	public void Resume()
	{
		if (!IsPaused)
			return;

		IsPaused = false;
		OnResume();
	}

	/// <summary>
	/// Override this method to implement game-specific pause logic.
	/// Base implementation does nothing.
	/// </summary>
	protected virtual void OnPause() { }

	/// <summary>
	/// Override this method to implement game-specific resume logic.
	/// Base implementation does nothing.
	/// </summary>
	protected virtual void OnResume() { }

	/// <summary>
	/// PHASE 3: Game Context Setup
	/// Integrates game with platform UI and triggers game-specific setup lifecycle.
	/// Override OnGameSetup() to connect event handlers (SessionManager signals, etc.).
	/// Do NOT call StartGame() in OnGameSetup() - use ActivateGame() instead.
	///
	/// POST-GUARANTEES:
	/// - Top menu context set with game title and buttons
	/// - Help content registered
	/// - UI integration complete
	/// - OnGameSetup() lifecycle called
	/// </summary>
	private void SetupGameContext()
	{
		var contextButtons = GetContextButtons();
		_gameHost.SetTopMenuContext(GetGameTitle(), contextButtons);

		// Set up help content through GameHost (when available)
		SetupGameHelp();

		// Call game-specific setup after UI integration is complete
		OnGameSetup();
	}

	/// <summary>
	/// Clean up game context when the game ends
	/// </summary>
	private void CleanupGameContext()
	{
		_gameHost?.ClearTopMenuContext();

		// Clean up help content
		CleanupGameHelp();
	}

	public override void _ExitTree()
	{
		CleanupGameContext();

		OnGameTeardown();

		base._ExitTree();
	}

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
	/// Override this to provide custom buttons for your game (e.g., pause/resume based on domain state)
	/// </summary>
	public virtual ContextButtonData[] GetContextButtons()
	{
		ContextButtonData[] buttons = [
			GameContextButton.CreateReturnToMenuButton(() => {
				var sessionManager = SessionManager.GetInstance();
				sessionManager?.ResetAllIdleTimers();
				ReturnToMainMenu();
			})
		];

		return buttons;
	}

	/// <summary>
	/// Called when the game is being set up
	/// Override this to connect external event handlers and perform initialization
	/// </summary>
	public virtual void OnGameSetup() { }

	/// <summary>
	/// Called when the game is being torn down
	/// Override this to disconnect external event handlers and perform cleanup
	/// </summary>
	public virtual void OnGameTeardown() { }

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
		if (_gameHost == null)
			return;

		var contextButtons = GetContextButtons();
		_gameHost.SetTopMenuContext(GetGameTitle(), contextButtons);
	}

	/// <summary>
	/// Return to the main menu with confirmation dialog.
	/// Use OnGameTeardown() for cleanup logic instead of overriding this method.
	/// </summary>
	protected async void ReturnToMainMenu()
	{
		try
		{
			// Get UIManager for confirmation dialog
			var uiManager = UIManager.GetInstance();

			// Show confirmation dialog
			bool confirmed = await uiManager.ShowConfirmationAsync(
				"Return to Menu",
				"Are you sure you want to return to the main menu?\n\nAny unsaved progress will be lost.",
				"Return to Menu",
				"Cancel"
			);

			if (!confirmed)
			{
				return; // User cancelled
			}

			// User confirmed or no UIManager available - proceed with return to menu
			_gameHost.ReturnToMainMenu();
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"[GameController] Failed to return to menu: {ex.Message}");
		}
	}

	/// <summary>
	/// Sets up help content through the GameHost/UIManager system
	/// </summary>
	private void SetupGameHelp()
	{
		var helpContent = GetHelpContent();
		_gameHost.SetGameHelpContent(helpContent);
		_gameHost.ShowGameHelp(true);
	}

	/// <summary>
	/// Cleans up help content when game ends
	/// </summary>
	private void CleanupGameHelp()
	{
		_gameHost.ShowGameHelp(false);
	}
}