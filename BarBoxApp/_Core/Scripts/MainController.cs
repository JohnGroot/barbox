using System.Collections.Generic;
using Godot;

[GlobalClass]
public partial class MainController : Control
{
	[ExportCategory("UI References")]
	[Export]
	public Control GameSelectionPanel { get; set; }

	[Export]
	public VBoxContainer GameList { get; set; }

	[Export]
	public Label UserInfoLabel { get; set; }

	private const string GAME_BUTTON_TEXT_FORMAT = "{0}\nPractice Mode: FREE\nPremium features: {1} credit{2}";
	private const string USER_INFO_FORMAT = "User: {0}\nCredits: {1}";
	private const string NOT_LOGGED_IN_TEXT = "Not logged in\nPractice mode only";

	private SessionManager _sessionManager;
	private GameRegistry _gameRegistry;
	private ApplicationBootstrap _appBootstrap;
	private GameHost _gameHost;
	private UIManager _uiManager;
	private CreditService _creditService;
	private Node _onscreenKeyboard;
	private readonly List<Button> _activeGameButtons = new();

	public override void _Ready()
	{
		var args = OS.GetCmdlineArgs();
		if (System.Array.IndexOf(args, "--run-tests") >= 0)
		{
			// Use CallDeferred to avoid scene tree modification during _Ready()
			GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, "res://Tests/TestRunner.tscn");
			return;
		}

		if (System.Array.IndexOf(args, "--vector-gallery") >= 0)
		{
			GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, "res://_Core/Scenes/Dev/VectorGallery.tscn");
			return;
		}

		_appBootstrap = ApplicationBootstrap.GetAutoload();
		if (_appBootstrap == null)
		{
			GD.PrintErr("[MainController] CRITICAL: ApplicationBootstrap not available");
			return;
		}

		if (_appBootstrap.AreServicesReady())
		{
			InitializeUI();
		}
		else
		{
			_appBootstrap.AllServicesReady += InitializeUI;
		}
	}

	/// <summary>
	/// Initialize UI components after services are guaranteed ready
	/// </summary>
	private void InitializeUI()
	{
		_sessionManager = SessionManager.GetInstance();
		_gameRegistry = GameRegistry.GetAutoload();
		_gameHost = GameHost.GetInstance();
		_creditService = CreditService.GetInstance();

		_onscreenKeyboard = GetNode<Node>("KeyboardLayer/OnscreenKeyboard");

		// Enable keyboard for main menu (disabled by default in scene)
		EnableKeyboardAutoShow();

		_uiManager = UIManager.GetInstance();

		if (_uiManager != null)
		{
			_uiManager.LogoutRequested += OnLogoutRequested;
		}

		ConnectSignals();
		SetupUI();
		UpdateUI();
	}

	public override void _Notification(int what)
	{
		if (what == NotificationExitTree)
		{
			DisconnectSignals();

			if (IsInstanceValid(_appBootstrap))
			{
				_appBootstrap.AllServicesReady -= InitializeUI;
			}

			// Disconnect from UIManager (autoload - don't QueueFree)
			if (IsInstanceValid(_uiManager))
			{
				_uiManager.LogoutRequested -= OnLogoutRequested;
			}

			ClearGameList();

			// Clear references to prevent memory leaks
			_onscreenKeyboard = null;
			_uiManager = null;
		}
	}

	private void ConnectSignals()
	{
		if (_sessionManager != null)
		{
			_sessionManager.UserLoggedIn += OnUserLoggedIn;
			_sessionManager.UserLoggedOut += OnUserLoggedOut;
		}
	}

	private void DisconnectSignals()
	{
		if (IsInstanceValid(_sessionManager))
		{
			_sessionManager.UserLoggedIn -= OnUserLoggedIn;
			_sessionManager.UserLoggedOut -= OnUserLoggedOut;
		}
	}

	private void SetupUI()
	{
		// Always show game selection panel - login is handled by UIManager modal
		if (GameSelectionPanel != null)
		{
			GameSelectionPanel.Visible = true;
		}

		// Always populate game list, regardless of login status
		PopulateGameList();
	}

	private void PopulateGameList()
	{
		if (GameList == null || _gameRegistry == null)
		{
			return;
		}

		ClearGameList();

		var availableGames = _gameRegistry.GetAvailableGames();
		foreach (var gameData in availableGames)
		{
			if (gameData.IsActive)
			{
				CreateGameButton(gameData);
			}
		}
	}

	private void ClearGameList()
	{
		foreach (var button in _activeGameButtons)
		{
			if (IsInstanceValid(button))
			{
				GameList.RemoveChild(button);
				button.QueueFree();
			}
		}

		_activeGameButtons.Clear();
	}

	private void CreateGameButton(GameMetadata gameData)
	{
		var button = new Button();
		var creditPlural = gameData.CreditCost != 1 ? "s" : string.Empty;
		button.Text = string.Format(GAME_BUTTON_TEXT_FORMAT, gameData.DisplayName, gameData.CreditCost, creditPlural);
		button.CustomMinimumSize = new Vector2(250, 100);

		button.Pressed += () => OnGameSelected(gameData.GameId);

		_activeGameButtons.Add(button);
		GameList.AddChild(button);
	}

	private void OnGameSelected(string gameId)
	{
		var gameData = _gameRegistry?.GetGameData(gameId);
		if (gameData == null)
		{
			return;
		}

		// Get primary user session if logged in (single-user main menu context)
		var currentUserSession = _sessionManager?.GetPrimarySession();

		// Load game - it will run in practice mode if no user is logged in
		var gameHost = GameHost.GetInstance();
		if (gameHost != null)
		{
			gameHost.LoadGameOverlay(gameId);
		}
		else
		{
			GD.PrintErr("GameHost autoload not available");
		}
	}

	private async void UpdateUI()
	{
		var currentUserSession = _sessionManager?.GetPrimarySession();

		if (UserInfoLabel != null)
		{
			if (currentUserSession != null)
			{
				// Fetch credits from CreditService (single source of truth)
				int credits = 0;
				if (_creditService != null)
				{
					var balanceResult = await _creditService.GetBalanceAsync(currentUserSession.PlayerId);
					if (balanceResult.IsSuccess(out var balance))
					{
						credits = balance;
					}
				}

				UserInfoLabel.Text = string.Format(USER_INFO_FORMAT, currentUserSession.UserName, credits);
			}
			else
			{
				UserInfoLabel.Text = NOT_LOGGED_IN_TEXT;
			}
		}
	}

	private void OnUserLoggedIn(string phoneNumber)
	{
		// Game selection panel stays visible - no login panel switching needed
		UpdateUI();
	}

	private void OnUserLoggedOut(string phoneNumber)
	{
		// Game selection panel stays visible - login handled by UIManager modal
		UpdateUI();
	}

	private async void OnLogoutRequested()
	{
		try
		{
			if (_sessionManager != null)
			{
				var session = _sessionManager.GetPrimarySession();
				if (session != null)
				{
					await _sessionManager.LogoutUserAsync(session.PhoneNumber);
				}
			}
			else
			{
				GD.PrintErr("[MainController DEBUG] _sessionManager is NULL, cannot logout");
			}

			// UI will be updated via UserLoggedOut signal - no need for immediate UpdateUI()
			// The signal-driven update ensures proper timing after logout completion
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"[MainController] Error during logout: {ex.Message}");
		}
	}

	/// <summary>
	/// Force signal connection to UIManager - used when MainController is loaded dynamically
	/// </summary>
	public void ForceUIManagerConnection()
	{
		if (_uiManager != null)
		{
			if (_uiManager.IsConnected(UIManager.SignalName.LogoutRequested, new Callable(this, nameof(OnLogoutRequested))))
			{
				_uiManager.LogoutRequested -= OnLogoutRequested;
			}

			_uiManager.LogoutRequested += OnLogoutRequested;
		}
		else
		{
			_uiManager = UIManager.GetInstance();

			if (_uiManager != null)
			{
				_uiManager.LogoutRequested += OnLogoutRequested;
			}
		}

		_sessionManager ??= SessionManager.GetInstance();
	}

	/// <summary>
	/// Hide the main UI when a game is loaded - called by GameHost
	/// </summary>
	public void HideMainUI()
	{
		// NOTE: Keyboard auto_show remains enabled during gameplay to support modals (login, etc.)
		// Games that need to disable keyboard can call DisableKeyboardAutoShow() explicitly
		var uiContainer = GetNode<VBoxContainer>("UI");
		if (uiContainer != null)
		{
			uiContainer.Visible = false;
		}
	}

	/// <summary>
	/// Show the main UI when returning from a game - called by GameHost
	/// </summary>
	public void ShowMainUI()
	{
		var uiContainer = GetNode<VBoxContainer>("UI");
		if (uiContainer != null)
		{
			uiContainer.Visible = true;
		}

		EnableKeyboardAutoShow();
	}

	private void DismissKeyboard()
	{
		if (IsInstanceValid(_onscreenKeyboard) && _onscreenKeyboard.HasMethod("hide"))
		{
			_onscreenKeyboard.Call("hide");
		}
	}

	private void DisableKeyboardAutoShow()
	{
		if (IsInstanceValid(_onscreenKeyboard))
		{
			_onscreenKeyboard.Set("auto_show", false);
			_onscreenKeyboard.Call("hide");
		}
	}

	private void EnableKeyboardAutoShow()
	{
		if (IsInstanceValid(_onscreenKeyboard))
		{
			_onscreenKeyboard.Set("auto_show", true);
		}
	}
}
