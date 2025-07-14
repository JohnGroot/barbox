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
	
	public override void _Ready()
	{
		_userManager = UserManager.GetAutoload();
		_gameRegistry = GameRegistry.GetAutoload();
		_sceneManager = SceneManager.GetAutoload();
		
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
		button.Text = $"{gameData.DisplayName}\nCredits: {gameData.CreditCost}";
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

		if (currentUser.Credits >= gameData.CreditCost)
		{
			// Direct GameHost autoload call - no scene switching needed
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
		else
		{
			ShowInsufficientCreditsMessage();
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
		_userManager?.LogoutUser();
	}

	private void OnLoginPressed()
	{
		if (UserIdInput == null || PinInput == null || _userManager == null)
		{
			ShowStatusMessage("Login system not properly initialized", false);
			return;
		}

		string userId = UserIdInput.Text.Trim();
		string pin = PinInput.Text.Trim();

		if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(pin))
		{
			ShowStatusMessage("Please enter both User ID and PIN", false);
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
}