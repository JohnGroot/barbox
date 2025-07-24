using Godot;

[GlobalClass]
public partial class MainController : Control
{
	[ExportCategory("UI References")]
	[Export] public Control LoginPanel { get; set; }
	[Export] public Control GameSelectionPanel { get; set; }
	[Export] public VBoxContainer GameList { get; set; }
	[Export] public Label UserInfoLabel { get; set; }
	[Export] public Button LogoutButton { get; set; }
	
	[ExportCategory("Login UI")]
	[Export] public LineEdit UserIdInput { get; set; }
	[Export] public LineEdit PinInput { get; set; }
	[Export] public Button LoginButton { get; set; }
	[Export] public Button CreateUserButton { get; set; }
	[Export] public Label StatusLabel { get; set; }

	private UserManager _userManager;
	private GameRegistry _gameRegistry;
	private SceneManager _sceneManager;
	private Node _onscreenKeyboard;
	
	public override void _Ready()
	{
		_userManager = UserManager.GetAutoload();
		_gameRegistry = GameRegistry.GetAutoload();
		_sceneManager = SceneManager.GetAutoload();
		
		// Get reference to onscreen keyboard
		_onscreenKeyboard = GetNode<Node>("OnscreenKeyboard");
		
		// Enable keyboard for main menu (disabled by default in scene)
		EnableKeyboardAutoShow();
		
		ConnectSignals();
		SetupUI();
		UpdateUI();
	}

	public override void _Notification(int what)
	{
		if (what == NotificationExitTree)
		{
			DisconnectSignals();
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

		if (LogoutButton != null)
		{
			LogoutButton.Pressed += OnLogoutPressed;
		}

		if (LoginButton != null)
		{
			LoginButton.Pressed += OnLoginPressed;
		}

		if (CreateUserButton != null)
		{
			CreateUserButton.Pressed += OnCreateUserPressed;
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

		if (GodotObject.IsInstanceValid(LogoutButton))
		{
			LogoutButton.Pressed -= OnLogoutPressed;
		}

		if (GodotObject.IsInstanceValid(LoginButton))
		{
			LoginButton.Pressed -= OnLoginPressed;
		}

		if (GodotObject.IsInstanceValid(CreateUserButton))
		{
			CreateUserButton.Pressed -= OnCreateUserPressed;
		}
	}

	private void SetupUI()
	{
		// Show login panel initially if no user is logged in
		bool userLoggedIn = _userManager?.IsUserLoggedIn() ?? false;
		
		if (LoginPanel != null)
			LoginPanel.Visible = !userLoggedIn;
		
		if (GameSelectionPanel != null)
			GameSelectionPanel.Visible = userLoggedIn;

		// Show development mode indicator
		if (GameHost.IsDevelopmentContext() && StatusLabel != null)
		{
			StatusLabel.Text = "Development Mode - PIN optional for debug accounts";
			StatusLabel.Modulate = Colors.Orange;
		}

		if (userLoggedIn)
		{
			PopulateGameList();
		}
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
		button.Text = $"{gameData.DisplayName}\nPremium features: {gameData.CreditCost} credit{(gameData.CreditCost != 1 ? "s" : "")}";
		button.CustomMinimumSize = new Vector2(200, 80);
		
		button.Pressed += () => OnGameSelected(gameData.GameId);
		
		GameList.AddChild(button);
	}

	private void OnGameSelected(string gameId)
	{
		var currentUser = _userManager?.GetCurrentUser();
		if (currentUser == null) return;

		var gameData = _gameRegistry?.GetGameData(gameId);
		if (gameData == null) return;

		// Load game directly - games handle their own credit economy
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
		GD.Print("Insufficient credits!");
	}

	private void UpdateUI()
	{
		var currentUser = _userManager?.GetCurrentUser();
		
		if (UserInfoLabel != null && currentUser != null)
		{
			UserInfoLabel.Text = $"User: {currentUser.UserId}\nCredits: {currentUser.Credits}";
		}
	}

	private void OnUserLoggedIn(UserData userData)
	{
		if (LoginPanel != null)
			LoginPanel.Visible = false;
		
		if (GameSelectionPanel != null)
			GameSelectionPanel.Visible = true;
		
		PopulateGameList();
		UpdateUI();
	}

	private void OnUserLoggedOut()
	{
		if (LoginPanel != null)
			LoginPanel.Visible = true;
		
		if (GameSelectionPanel != null)
			GameSelectionPanel.Visible = false;
		
		UpdateUI();
	}

	private void OnUserDataUpdated(UserData userData)
	{
		UpdateUI();
	}

	private void OnLogoutPressed()
	{
		// Dismiss keyboard before logout
		DismissKeyboard();
		_userManager?.LogoutUser();
	}

	private void OnLoginPressed()
	{
		// Dismiss keyboard when login button is pressed
		DismissKeyboard();
		
		if (UserIdInput == null || PinInput == null || _userManager == null)
		{
			ShowStatusMessage("Login system not properly initialized", false);
			return;
		}

		string userId = UserIdInput.Text.Trim();
		string pin = PinInput.Text.Trim();

		// Development context: allow empty PIN for debug account
		if (GameHost.IsDevelopmentContext() && !string.IsNullOrEmpty(userId) && string.IsNullOrEmpty(pin))
		{
			GD.Print($"Development mode: Attempting debug login for user '{userId}'");
			bool debugLoginSuccess = _userManager.LoginUserDevelopment(userId);
			if (debugLoginSuccess)
			{
				ShowStatusMessage("Debug login successful! (Development Mode)", true);
				ClearLoginFields();
			}
			else
			{
				ShowStatusMessage("Debug login failed", false);
			}
			return;
		}

		// Production context: require both User ID and PIN
		if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(pin))
		{
			if (GameHost.IsDevelopmentContext())
			{
				ShowStatusMessage("Enter User ID (PIN optional in dev mode)", false);
			}
			else
			{
				ShowStatusMessage("Please enter both User ID and PIN", false);
			}
			return;
		}

		bool loginSuccess = _userManager.LoginUser(userId, pin);
		if (loginSuccess)
		{
			ShowStatusMessage("Login successful!", true);
			ClearLoginFields();
		}
		else
		{
			ShowStatusMessage("Invalid credentials", false);
		}
	}

	private void OnCreateUserPressed()
	{
		// Dismiss keyboard when create user button is pressed
		DismissKeyboard();
		
		if (UserIdInput == null || PinInput == null || _userManager == null)
		{
			ShowStatusMessage("User creation system not properly initialized", false);
			return;
		}

		string userId = UserIdInput.Text.Trim();
		string pin = PinInput.Text.Trim();

		if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(pin))
		{
			ShowStatusMessage("Please enter both User ID and PIN", false);
			return;
		}

		var newUser = _userManager.CreateUser(userId, pin);
		if (newUser != null)
		{
			ShowStatusMessage("User created successfully!", true);
			ClearLoginFields();
		}
		else
		{
			ShowStatusMessage("User creation failed - ID may already exist", false);
		}
	}

	private void ShowStatusMessage(string message, bool isSuccess)
	{
		if (StatusLabel != null)
		{
			StatusLabel.Text = message;
			StatusLabel.Modulate = isSuccess ? Colors.Green : Colors.Red;
		}
	}

	private void ClearLoginFields()
	{
		if (UserIdInput != null) UserIdInput.Text = "";
		if (PinInput != null) PinInput.Text = "";
		if (StatusLabel != null) StatusLabel.Text = "";
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
		if (_onscreenKeyboard != null && _onscreenKeyboard.HasMethod("hide"))
		{
			_onscreenKeyboard.Call("hide");
		}
	}

	/// <summary>
	/// Disable keyboard completely to prevent it from appearing during games
	/// </summary>
	private void DisableKeyboardAutoShow()
	{
		if (_onscreenKeyboard != null)
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
		if (_onscreenKeyboard != null)
		{
			_onscreenKeyboard.Set("auto_show", true);
		}
	}
}