using BarBox.Core.Autoloads;
using BarBox.Core.Gameplay;
using Godot;
using LightResults;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

[GlobalClass]
public abstract partial class GameController : Node2D
{
	// ==========================================================================
	// PLATFORM ACCESS
	// ==========================================================================

	/// <summary>
	/// Access to platform services. Available after _Ready() begins.
	/// Provides Session, Events, Credits, Location, Host, UI, Input, Registry.
	/// </summary>
	protected GameContext Platform { get; private set; }

	/// <summary>
	/// Game metadata from GameRegistry.
	/// </summary>
	protected GameMetadata _gameMetadata;

	/// <summary>
	/// Public accessor for game ID (for external callers like GameHost).
	/// </summary>
	public string GameId => GetGameId();

	/// <summary>
	/// Whether players can logout during active gameplay.
	/// Override this in your game to control logout behavior (default: true).
	/// </summary>
	public virtual bool CanLogout => true;

	/// <summary>
	/// Indicates whether the game is currently paused.
	/// </summary>
	public bool IsPaused { get; protected set; }

	// ==========================================================================
	// SEALED ORCHESTRATION - Ensures proper initialization flow
	// ==========================================================================

	/// <summary>
	/// Sealed - orchestrates all initialization phases. Cannot be overridden.
	/// Override the individual On* phase methods instead.
	/// </summary>
	public sealed override void _Ready()
	{
		// Phase 0: Create platform context (services always available)
		Platform = GameContext.CreateFromAutoloads();

		// Phase 1: Load game metadata and register with GameHost
		_gameMetadata = Platform.Registry?.GetGameData(GameId);
		Platform.Host?.RegisterCurrentGame(this);

		// Phase 2: Game-specific service discovery (for additional services)
		OnDiscoverServices();

		// Phase 3: Component initialization
		OnInitializeComponents();

		// Phase 4: UI/Context setup (sealed internal)
		SetupGameContext();

		// Phase 5: Async init then activate
		StartAsyncInitialization();
	}

	/// <summary>
	/// Sealed - ensures proper cleanup flow. Cannot be overridden.
	/// Override OnGameTeardown() for game-specific cleanup.
	/// </summary>
	public sealed override void _ExitTree()
	{
		CleanupGameContext();
		DisconnectUserSignals();
		OnGameTeardown();
		CloseBackendSession();
		base._ExitTree();
	}

	// ==========================================================================
	// BACKEND SESSION LIFECYCLE - Shared session create/close for all games
	// ==========================================================================

	/// <summary>
	/// Backend activity-session id, owned by the base class so games no longer
	/// duplicate the field or the close call. Set by StartBackendSessionAsync,
	/// closed automatically during _ExitTree.
	/// </summary>
	private Guid _activitySessionId;

	/// <summary>
	/// Backend game_tag for the activity session. Defaults to the registry game
	/// id; override when the backend tag differs (e.g. Mining's id is
	/// "mining_game" but its tag is "mining").
	/// </summary>
	protected virtual string GetGameTag() => GetGameId();

	/// <summary>
	/// Creates the backend activity session and stores its id for automatic
	/// close on teardown. Call when gameplay actually begins (timing varies per
	/// game). Returns the Result so the caller decides whether failure is fatal.
	/// </summary>
	protected async Task<Result<Guid>> StartBackendSessionAsync(Guid boxId, Guid playerId, List<string> playerIds = null)
	{
		if (Platform.Events == null)
			return Result.Failure<Guid>("EventService not available");

		var result = await Platform.Events.CreateActivitySessionAsync(boxId, playerId, GetGameTag(), playerIds);
		if (result.IsSuccess(out var sessionId))
			_activitySessionId = sessionId;

		return result;
	}

	/// <summary>
	/// Closes the active backend session, if any. Runs in _ExitTree after
	/// OnGameTeardown. IsInstanceValid guard is safe here (cleanup path).
	/// </summary>
	private void CloseBackendSession()
	{
		if (_activitySessionId != Guid.Empty && Platform.Events != null && GodotObject.IsInstanceValid(Platform.Events))
		{
			_ = Platform.Events.CloseActivitySessionAsync(_activitySessionId);
			_activitySessionId = Guid.Empty;
		}
	}

	// ==========================================================================
	// VIRTUAL OVERRIDE POINTS - Games implement these
	// ==========================================================================

	/// <summary>
	/// REQUIRED: Returns the game ID for registry lookup. Must match an entry in GameRegistry.
	/// </summary>
	protected abstract string GetGameId();

	/// <summary>
	/// Override to discover additional game-specific services beyond Platform.
	/// Platform property is already populated when this is called.
	/// </summary>
	protected virtual void OnDiscoverServices() { }

	/// <summary>
	/// Override to create game components (engine, state, UI).
	/// Called after service discovery, before UI integration.
	/// </summary>
	protected virtual void OnInitializeComponents() { }

	/// <summary>
	/// Override for async initialization (backend calls, data loading).
	/// Called after components initialized, before OnActivateGame.
	/// </summary>
	protected virtual Task OnInitializeAsync() => Task.CompletedTask;

	/// <summary>
	/// Override to start gameplay or show initial state.
	/// Called after all initialization (including async) is complete.
	/// </summary>
	protected virtual void OnActivateGame() { }

	/// <summary>
	/// Override to handle user login. Auto-connected to SessionManager.UserLoggedIn signal.
	/// </summary>
	protected virtual void OnUserLoggedIn(UserSession session) { }

	/// <summary>
	/// Override to handle user logout. Auto-connected to SessionManager.UserLoggedOut signal.
	/// </summary>
	protected virtual void OnUserLoggedOut(string phoneNumber) { }

	/// <summary>
	/// Override for game-specific cleanup on exit.
	/// Called during _ExitTree after UI cleanup and signal disconnection.
	/// </summary>
	protected virtual void OnGameTeardown() { }

	/// <summary>
	/// Override to handle async initialization failure.
	/// </summary>
	protected virtual void OnInitializationFailed(Exception ex) { }

	/// <summary>
	/// Override this method to provide help content for your game.
	/// This content will be displayed in the help menu overlay.
	/// </summary>
	protected virtual HelpContentData GetHelpContent()
	{
		return new HelpContentData("Game Help")
			.AddSection("🎮 Basic Controls", "Touch or click to interact with the game.");
	}

	/// <summary>
	/// Gets the display title for the game in the top menu bar.
	/// Override this to provide a custom title, defaults to game metadata display name.
	/// </summary>
	public virtual string GetGameTitle()
	{
		return _gameMetadata?.DisplayName ?? GameId;
	}

	/// <summary>
	/// Gets the context buttons this game wants to display in the top menu bar.
	/// Override this to provide custom buttons for your game (e.g., pause/resume based on domain state).
	/// </summary>
	public virtual ContextButtonData[] GetContextButtons()
	{
		ContextButtonData[] buttons = [
			GameContextButton.CreateReturnToMenuButton(() => {
				Platform.Session?.ResetAllIdleTimers();
				ReturnToMainMenu();
			})
		];

		return buttons;
	}

	// ==========================================================================
	// PAUSE/RESUME LIFECYCLE
	// ==========================================================================

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
	/// </summary>
	protected virtual void OnPause() { }

	/// <summary>
	/// Override this method to implement game-specific resume logic.
	/// </summary>
	protected virtual void OnResume() { }

	// ==========================================================================
	// UI STATE MANAGEMENT
	// ==========================================================================

	/// <summary>
	/// Called to update the game's UI state (e.g., button enabled/disabled states).
	/// Override this to customize UI state updates.
	/// </summary>
	public virtual void UpdateUIState()
	{
		RefreshUI();
	}

	/// <summary>
	/// Refresh the UI context with current game state.
	/// Call this when game state changes that affect UI (pause/resume, etc.)
	/// </summary>
	protected void RefreshUI()
	{
		if (Platform.Host == null)
			return;

		var contextButtons = GetContextButtons();
		Platform.Host.SetTopMenuContext(GetGameTitle(), contextButtons);
	}

	/// <summary>
	/// Return to the main menu with confirmation dialog.
	/// Use OnGameTeardown() for cleanup logic instead of overriding this method.
	/// </summary>
	protected async void ReturnToMainMenu()
	{
		try
		{
			if (Platform.UI != null)
			{
				bool confirmed = await Platform.UI.ShowConfirmationAsync(
					"Return to Menu",
					"Are you sure you want to return to the main menu?\n\nAny unsaved progress will be lost.",
					"Return to Menu",
					"Cancel"
				);

				if (!confirmed)
					return;
			}

			Platform.Host?.ReturnToMainMenu();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[{GameId}] Failed to return to menu: {ex.Message}");
		}
	}

	// ==========================================================================
	// INTERNAL ORCHESTRATION
	// ==========================================================================

	private async void StartAsyncInitialization()
	{
		try
		{
			await OnInitializeAsync();
			OnActivateGame();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[{GameId}] Async initialization failed: {ex.Message}");
			OnInitializationFailed(ex);
		}
	}

	private void SetupGameContext()
	{
		// Connect user signals (Session may be null in dev)
		if (Platform.Session != null)
		{
			Platform.Session.UserLoggedIn += HandleUserLoggedIn;
			Platform.Session.UserLoggedOut += HandleUserLoggedOut;
		}

		// Set up top menu context (Host may be null in dev)
		Platform.Host?.SetTopMenuContext(GetGameTitle(), GetContextButtons());

		// Set up help content
		var helpContent = GetHelpContent();
		Platform.Host?.SetGameHelpContent(helpContent);
		Platform.Host?.ShowGameHelp(true);
	}

	private void CleanupGameContext()
	{
		Platform.Host?.ClearTopMenuContext();
		Platform.Host?.ShowGameHelp(false);
	}

	private void DisconnectUserSignals()
	{
		if (Platform.Session != null && GodotObject.IsInstanceValid(Platform.Session))
		{
			Platform.Session.UserLoggedIn -= HandleUserLoggedIn;
			Platform.Session.UserLoggedOut -= HandleUserLoggedOut;
		}
	}

	private void HandleUserLoggedIn(string phoneNumber)
	{
		var session = Platform.Session?.GetSessionByPhone(phoneNumber);
		OnUserLoggedIn(session);
	}

	private void HandleUserLoggedOut(string phoneNumber) => OnUserLoggedOut(phoneNumber);
}
