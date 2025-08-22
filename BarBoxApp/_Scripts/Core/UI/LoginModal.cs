using Godot;

/// <summary>
/// Modal login dialog that appears as an overlay above all content
/// Provides login/logout functionality accessible from anywhere in the application
/// </summary>
public partial class LoginModal : Control
{
	[Signal] public delegate void LoginAttemptEventHandler(string userId, string pin);
	[Signal] public delegate void CreateUserAttemptEventHandler(string userId, string pin);
	[Signal] public delegate void ModalClosedEventHandler();

	// UI Components
	private Panel _modalBackground;
	private Panel _modalPanel;
	private VBoxContainer _contentContainer;
	private Label _titleLabel;
	private LineEdit _userIdInput;
	private LineEdit _pinInput;
	private Button _loginButton;
	private Button _createUserButton;
	private Button _closeButton;
	private Label _statusLabel;

	private UserManager _userManager;

	public override void _Ready()
	{
		_userManager = UserManager.GetAutoload();
		
		SetupModalLayout();
		CreateModalUI();
		ConnectSignals();
		
		// Start hidden
		Visible = false;
		
		// LoginModal created and ready
	}

	private void SetupModalLayout()
	{
		// Fill entire screen - LoginModal will be added to UIManager which has its own CanvasLayer handling
		SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
	}

	private void CreateModalUI()
	{
		// Semi-transparent background overlay
		_modalBackground = new Panel();
		_modalBackground.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		
		var backgroundStyle = new StyleBoxFlat();
		backgroundStyle.BgColor = new Color(0.0f, 0.0f, 0.0f, 0.7f); // Semi-transparent black
		_modalBackground.AddThemeStyleboxOverride("panel", backgroundStyle);
		
		// Click background to close modal
		_modalBackground.GuiInput += OnBackgroundInput;
		
		AddChild(_modalBackground);

		// Create centering container
		var centerContainer = new CenterContainer();
		centerContainer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		AddChild(centerContainer);

		// Modal panel (centered dialog)
		_modalPanel = new Panel();
		_modalPanel.CustomMinimumSize = new Vector2(400, 350);
		_modalPanel.SetSize(new Vector2(400, 350));
		
		var modalStyle = new StyleBoxFlat();
		modalStyle.BgColor = new Color(0.2f, 0.2f, 0.2f, 1.0f);
		modalStyle.BorderWidthTop = 2;
		modalStyle.BorderWidthBottom = 2;
		modalStyle.BorderWidthLeft = 2;
		modalStyle.BorderWidthRight = 2;
		modalStyle.BorderColor = new Color(0.4f, 0.4f, 0.4f, 1.0f);
		modalStyle.CornerRadiusTopLeft = 8;
		modalStyle.CornerRadiusTopRight = 8;
		modalStyle.CornerRadiusBottomLeft = 8;
		modalStyle.CornerRadiusBottomRight = 8;
		_modalPanel.AddThemeStyleboxOverride("panel", modalStyle);
		
		centerContainer.AddChild(_modalPanel);

		// Content container
		_contentContainer = new VBoxContainer();
		_contentContainer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_contentContainer.AddThemeConstantOverride("separation", 15);
		
		// Add padding
		var margin = new MarginContainer();
		margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		margin.AddThemeConstantOverride("margin_left", 20);
		margin.AddThemeConstantOverride("margin_right", 20);
		margin.AddThemeConstantOverride("margin_top", 20);
		margin.AddThemeConstantOverride("margin_bottom", 20);
		
		_modalPanel.AddChild(margin);
		margin.AddChild(_contentContainer);

		// Title
		_titleLabel = new Label();
		_titleLabel.Text = "Login to BarBox Arcade";
		_titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_titleLabel.AddThemeColorOverride("font_color", Colors.White);
		_titleLabel.AddThemeFontSizeOverride("font_size", 20);
		_contentContainer.AddChild(_titleLabel);

		// User ID input
		_userIdInput = new LineEdit();
		_userIdInput.PlaceholderText = "User ID";
		_userIdInput.CustomMinimumSize = new Vector2(0, 40);
		_contentContainer.AddChild(_userIdInput);

		// PIN input
		_pinInput = new LineEdit();
		_pinInput.PlaceholderText = "PIN";
		_pinInput.Secret = true;
		_pinInput.CustomMinimumSize = new Vector2(0, 40);
		_contentContainer.AddChild(_pinInput);

		// Button container
		var buttonContainer = new HBoxContainer();
		buttonContainer.Alignment = BoxContainer.AlignmentMode.Center;
		buttonContainer.AddThemeConstantOverride("separation", 10);
		_contentContainer.AddChild(buttonContainer);

		// Login button
		_loginButton = new Button();
		_loginButton.Text = "Login";
		_loginButton.CustomMinimumSize = new Vector2(100, 40);
		buttonContainer.AddChild(_loginButton);

		// Create user button
		_createUserButton = new Button();
		_createUserButton.Text = "Create User";
		_createUserButton.CustomMinimumSize = new Vector2(100, 40);
		buttonContainer.AddChild(_createUserButton);

		// Close button
		_closeButton = new Button();
		_closeButton.Text = "Close";
		_closeButton.CustomMinimumSize = new Vector2(100, 40);
		buttonContainer.AddChild(_closeButton);

		// Status label
		_statusLabel = new Label();
		_statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_statusLabel.AddThemeColorOverride("font_color", Colors.White);
		_statusLabel.CustomMinimumSize = new Vector2(0, 30);
		_contentContainer.AddChild(_statusLabel);

		// Show development mode indicator
		if (GameHost.IsDevelopmentContext())
		{
			_statusLabel.Text = "Development Mode - PIN optional for debug accounts";
			_statusLabel.Modulate = Colors.Orange;
		}
	}

	private void ConnectSignals()
	{
		if (_loginButton != null)
			_loginButton.Pressed += OnLoginPressed;
		
		if (_createUserButton != null)
			_createUserButton.Pressed += OnCreateUserPressed;
		
		if (_closeButton != null)
			_closeButton.Pressed += OnClosePressed;

		// Connect to user manager signals for status updates
		if (_userManager != null)
		{
			_userManager.UserLoggedIn += OnUserLoggedIn;
			_userManager.UserLoggedOut += OnUserLoggedOut;
		}

		// Handle Enter key in input fields
		if (_userIdInput != null)
			_userIdInput.TextSubmitted += (_) => OnLoginPressed();
		
		if (_pinInput != null)
			_pinInput.TextSubmitted += (_) => OnLoginPressed();
	}

	/// <summary>
	/// Show the login modal
	/// </summary>
	public void ShowModal()
	{
		Visible = true;
		
		// Focus first input field
		if (_userIdInput != null)
		{
			_userIdInput.GrabFocus();
		}
		
		ClearForm();
		// Modal shown
	}

	/// <summary>
	/// Hide the login modal
	/// </summary>
	public void HideModal()
	{
		Visible = false;
		ClearForm();
		EmitSignal(SignalName.ModalClosed);
		// Modal hidden
	}

	private void ClearForm()
	{
		if (_userIdInput != null) _userIdInput.Text = "";
		if (_pinInput != null) _pinInput.Text = "";
		if (_statusLabel != null && !GameHost.IsDevelopmentContext()) _statusLabel.Text = "";
	}

	private void ShowStatusMessage(string message, bool isSuccess)
	{
		if (_statusLabel != null)
		{
			_statusLabel.Text = message;
			_statusLabel.Modulate = isSuccess ? Colors.Green : Colors.Red;
		}
	}

	// Event handlers

	private void OnBackgroundInput(InputEvent @event)
	{
		// Close modal when clicking background
		if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed && mouseEvent.ButtonIndex == MouseButton.Left)
		{
			OnClosePressed();
		}
	}

	private void OnLoginPressed()
	{
		if (_userIdInput == null || _pinInput == null || _userManager == null)
		{
			ShowStatusMessage("Login system not properly initialized", false);
			return;
		}

		string userId = _userIdInput.Text.Trim();
		string pin = _pinInput.Text.Trim();

		// Development context: allow empty PIN for debug account
		if (GameHost.IsDevelopmentContext() && !string.IsNullOrEmpty(userId) && string.IsNullOrEmpty(pin))
		{
			bool debugLoginSuccess = _userManager.LoginUserDevelopment(userId);
			if (debugLoginSuccess)
			{
				ShowStatusMessage("Debug login successful! (Development Mode)", true);
				// Modal will be hidden by OnUserLoggedIn signal
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
			// Modal will be hidden by OnUserLoggedIn signal
		}
		else
		{
			ShowStatusMessage("Invalid credentials", false);
		}
	}

	private void OnCreateUserPressed()
	{
		if (_userIdInput == null || _pinInput == null || _userManager == null)
		{
			ShowStatusMessage("User creation system not properly initialized", false);
			return;
		}

		string userId = _userIdInput.Text.Trim();
		string pin = _pinInput.Text.Trim();

		if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(pin))
		{
			ShowStatusMessage("Please enter both User ID and PIN", false);
			return;
		}

		var newUser = _userManager.CreateUser(userId, pin);
		if (newUser != null)
		{
			ShowStatusMessage("User created successfully!", true);
			// Modal will be hidden by OnUserLoggedIn signal
		}
		else
		{
			ShowStatusMessage("User creation failed - ID may already exist", false);
		}
	}

	private void OnClosePressed()
	{
		HideModal();
	}

	private void OnUserLoggedIn(UserData userData)
	{
		// Auto-hide modal on successful login
		HideModal();
	}

	private void OnUserLoggedOut()
	{
		// Clear form when user logs out
		ClearForm();
	}

	public override void _ExitTree()
	{
		// Disconnect signals
		if (GodotObject.IsInstanceValid(_loginButton))
			_loginButton.Pressed -= OnLoginPressed;
		
		if (GodotObject.IsInstanceValid(_createUserButton))
			_createUserButton.Pressed -= OnCreateUserPressed;
		
		if (GodotObject.IsInstanceValid(_closeButton))
			_closeButton.Pressed -= OnClosePressed;

		if (GodotObject.IsInstanceValid(_userManager))
		{
			_userManager.UserLoggedIn -= OnUserLoggedIn;
			_userManager.UserLoggedOut -= OnUserLoggedOut;
		}

		base._ExitTree();
	}
}