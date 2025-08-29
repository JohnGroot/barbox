using Godot;

[GlobalClass]
public partial class MainController : Control
{
	[ExportCategory("UI References")]
	[Export] public Control GameSelectionPanel { get; set; }
	[Export] public VBoxContainer GameList { get; set; }
	[Export] public Label UserInfoLabel { get; set; }

	private UserManager _userManager;
	private GameRegistry _gameRegistry;
	private SceneManager _sceneManager;
	private GameHost _gameHost;
	private UIManager _uiManager;
	private Node _onscreenKeyboard;
	
	public override void _Ready()
	{
		// MainController _Ready() starting
		
		// Get SceneManager reference to check if services are ready
		_sceneManager = SceneManager.GetAutoload();
		if (_sceneManager == null)
		{
			GD.PrintErr("[MainController] CRITICAL: SceneManager not available");
			return;
		}
		
		// Check if services are already initialized
		if (_sceneManager.AreServicesReady())
		{
			// Services already ready, initializing UI
			InitializeUI();
		}
		else
		{
			// Waiting for services to be ready
			_sceneManager.AllServicesReady += InitializeUI;
		}
	}

	/// <summary>
	/// Initialize UI components after services are guaranteed ready
	/// </summary>
	private void InitializeUI()
	{
		// Services ready, initializing UI
		
		// Now safe to get service references
		_userManager = UserManager.GetAutoload();
		_gameRegistry = GameRegistry.GetAutoload();
		_gameHost = GameHost.Instance;
		
		// Services loaded and initialized
		
		// Get reference to onscreen keyboard
		_onscreenKeyboard = GetNode<Node>("OnscreenKeyboard");
		
		// Enable keyboard for main menu (disabled by default in scene)
		EnableKeyboardAutoShow();
		
		// Get UIManager reference - it's now an autoload service
		_uiManager = UIManager.GetInstance();
		
		if (_uiManager != null)
		{
			_uiManager.LogoutRequested += OnLogoutRequested;
		}
		
		ConnectSignals();
		SetupUI();
		UpdateUI();
		
		// UI initialization completed
	}

	public override void _Notification(int what)
	{
		if (what == NotificationExitTree)
		{
			DisconnectSignals();
			
			// Disconnect from SceneManager signal
			if (GodotObject.IsInstanceValid(_sceneManager))
			{
				_sceneManager.AllServicesReady -= InitializeUI;
			}
			
			// Disconnect from UIManager (it's an autoload now, don't QueueFree)
			if (GodotObject.IsInstanceValid(_uiManager))
			{
				_uiManager.LogoutRequested -= OnLogoutRequested;
			}
			
			// Clear references to prevent memory leaks
			_onscreenKeyboard = null;
			_uiManager = null;
		}
	}

	private void ConnectSignals()
	{
		if (_userManager != null)
		{
			_userManager.UserLoggedIn += OnUserLoggedIn;
			_userManager.UserLoggedOut += OnUserLoggedOut;
			_userManager.UserDataUpdated += OnUserDataUpdated;
		}
	}

	private void DisconnectSignals()
	{
		if (GodotObject.IsInstanceValid(_userManager))
		{
			_userManager.UserLoggedIn -= OnUserLoggedIn;
			_userManager.UserLoggedOut -= OnUserLoggedOut;
			_userManager.UserDataUpdated -= OnUserDataUpdated;
		}
	}


	private void SetupUI()
	{
		// SetupUI() starting
		
		// Always show game selection panel - login is handled by UIManager modal
		if (GameSelectionPanel != null)
			GameSelectionPanel.Visible = true; // Always show game selection

		// Always populate game list, regardless of login status
		PopulateGameList();
		
		// SetupUI() completed
	}

	private void PopulateGameList()
	{
		if (GameList == null || _gameRegistry == null) return;

		// Clear existing game buttons
		foreach (Node child in GameList.GetChildren())
		{
			child.QueueFree();
		}

		// Add game buttons
		var availableGames = _gameRegistry.GetAvailableGames();
		foreach (var gameData in availableGames)
		{
			if (gameData.IsActive)
			{
				CreateGameButton(gameData);
			}
		}
	}

	private void CreateGameButton(GameMetadata gameData)
	{
		var button = new Button();
		// Show that practice mode is free
		button.Text = $"{gameData.DisplayName}\nPractice Mode: FREE\nPremium features: {gameData.CreditCost} credit{(gameData.CreditCost != 1 ? "s" : "")}";
		button.CustomMinimumSize = new Vector2(250, 100);
		
		button.Pressed += () => OnGameSelected(gameData.GameId);
		
		GameList.AddChild(button);
	}

	private void OnGameSelected(string gameId)
	{
		var gameData = _gameRegistry?.GetGameData(gameId);
		if (gameData == null) return;

		// Get current user if logged in (can be null)
		var currentUser = _userManager?.GetCurrentUser();

		// Load game - it will run in practice mode if no user is logged in
		var gameHost = GameHost.Instance;
		if (gameHost != null)
		{
			gameHost.LoadGameOverlay(gameId, currentUser);
		}
		else
		{
			GD.PrintErr("GameHost autoload not available");
		}
	}

	private void ShowInsufficientCreditsMessage()
	{
		// Show popup or notification about insufficient credits
		// Insufficient credits
	}

	private void UpdateUI()
	{
		var currentUser = _userManager?.GetCurrentUser();
		
		if (UserInfoLabel != null)
		{
			if (currentUser != null)
			{
				UserInfoLabel.Text = $"User: {currentUser.UserId}\nCredits: {currentUser.Credits}";
			}
			else
			{
				UserInfoLabel.Text = "Not logged in\nPractice mode only";
			}
		}
	}

	private void OnUserLoggedIn(UserData userData)
	{
		// Game selection panel stays visible - no login panel switching needed
		UpdateUI();
	}

	private void OnUserLoggedOut()
	{
		// Game selection panel stays visible - login handled by UIManager modal
		UpdateUI();
	}

	private void OnUserDataUpdated(UserData userData)
	{
		UpdateUI();
	}

	private async void OnLogoutRequested()
	{
		try
		{
			// Logout requested
			
			// Handle logout request from UIManager
			if (_userManager != null)
			{
				// UserManager exists, calling LogoutUserAsync()
				await _userManager.LogoutUserAsync();
				// LogoutUserAsync() completed
			}
			else
			{
				GD.PrintErr("[MainController DEBUG] _userManager is NULL, cannot logout");
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
			// Disconnect any existing connection first
			if (_uiManager.IsConnected(UIManager.SignalName.LogoutRequested, new Callable(this, nameof(OnLogoutRequested))))
			{
				_uiManager.LogoutRequested -= OnLogoutRequested;
			}
			
			// Reconnect to ensure signal works
			_uiManager.LogoutRequested += OnLogoutRequested;
		}
		else
		{
			// Try to get UIManager reference if not available
			_uiManager = UIManager.GetInstance();
			
			if (_uiManager != null)
			{
				_uiManager.LogoutRequested += OnLogoutRequested;
			}
		}
		
		// Ensure UserManager reference is available for logout functionality
		if (_userManager == null)
		{
			// UserManager is null, obtaining reference
			_userManager = UserManager.GetAutoload();
			// UserManager reference obtained
		}
	}


	/// <summary>
	/// Hide the main UI when a game is loaded - called by GameHost
	/// </summary>
	public void HideMainUI()
	{
		// Disable keyboard auto_show during game
		DisableKeyboardAutoShow();
		
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

		// Re-enable keyboard auto_show when returning to main menu
		EnableKeyboardAutoShow();
	}

	/// <summary>
	/// Dismiss the onscreen keyboard
	/// </summary>
	private void DismissKeyboard()
	{
		if (GodotObject.IsInstanceValid(_onscreenKeyboard) && _onscreenKeyboard.HasMethod("hide"))
		{
			_onscreenKeyboard.Call("hide");
		}
	}

	/// <summary>
	/// Disable keyboard completely to prevent it from appearing during games
	/// </summary>
	private void DisableKeyboardAutoShow()
	{
		if (GodotObject.IsInstanceValid(_onscreenKeyboard))
		{
			_onscreenKeyboard.Set("auto_show", false);
			_onscreenKeyboard.Call("hide");
		}
	}

	/// <summary>
	/// Re-enable keyboard when returning to main menu
	/// </summary>
	private void EnableKeyboardAutoShow()
	{
		if (GodotObject.IsInstanceValid(_onscreenKeyboard))
		{
			_onscreenKeyboard.Set("auto_show", true);
		}
	}
	
}