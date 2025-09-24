using Godot;
using System;

/// <summary>
/// Modal login dialog that appears as an overlay above all content
/// Provides login/logout functionality accessible from anywhere in the application
/// </summary>
public partial class LoginModal : Control
{
	[Signal] public delegate void LoginAttemptEventHandler(string phoneNumber, string pin);
	[Signal] public delegate void CreateUserAttemptEventHandler(string phoneNumber, string pin, string username);
	[Signal] public delegate void ModalClosedEventHandler();

	// UI Components
	private Panel _modalBackground;
	private Panel _modalPanel;
	private TabContainer _tabContainer;

	// Login Tab Components
	private VBoxContainer _loginContainer;
	private Label _loginTitleLabel;
	private LineEdit _loginPhoneInput;
	private LineEdit _loginPinInput;
	private Button _loginButton;
	private Button _loginCancelButton;
	private Label _loginStatusLabel;

	// Create Account Tab Components
	private VBoxContainer _createContainer;
	private Label _createTitleLabel;
	private LineEdit _createPhoneInput;
	private LineEdit _createPinInput;
	private LineEdit _createUsernameInput;
	private Button _createAccountButton;
	private Button _createCancelButton;
	private Label _createStatusLabel;

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

		// Modal panel (centered dialog) - made taller for tabs
		_modalPanel = new Panel();
		_modalPanel.CustomMinimumSize = new Vector2(450, 450);
		_modalPanel.SetSize(new Vector2(450, 450));

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

		// Add padding
		var margin = new MarginContainer();
		margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		margin.AddThemeConstantOverride("margin_left", 20);
		margin.AddThemeConstantOverride("margin_right", 20);
		margin.AddThemeConstantOverride("margin_top", 20);
		margin.AddThemeConstantOverride("margin_bottom", 20);

		_modalPanel.AddChild(margin);

		// Tab container
		_tabContainer = new TabContainer();
		_tabContainer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		margin.AddChild(_tabContainer);

		CreateLoginTab();
		CreateAccountTab();
	}

	private void CreateLoginTab()
	{
		// Login tab container
		_loginContainer = new VBoxContainer();
		_loginContainer.AddThemeConstantOverride("separation", 15);
		_tabContainer.AddChild(_loginContainer);
		_tabContainer.SetTabTitle(0, "Login");

		// Title
		_loginTitleLabel = new Label();
		_loginTitleLabel.Text = "Login to BarBox Arcade";
		_loginTitleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_loginTitleLabel.AddThemeColorOverride("font_color", Colors.White);
		_loginTitleLabel.AddThemeFontSizeOverride("font_size", 18);
		_loginContainer.AddChild(_loginTitleLabel);

		// Phone number input
		_loginPhoneInput = new LineEdit();
		_loginPhoneInput.PlaceholderText = "Phone Number (10 digits)";
		_loginPhoneInput.CustomMinimumSize = new Vector2(0, 40);
		_loginContainer.AddChild(_loginPhoneInput);

		// PIN input
		_loginPinInput = new LineEdit();
		_loginPinInput.PlaceholderText = "PIN (4 digits)";
		_loginPinInput.Secret = true;
		_loginPinInput.CustomMinimumSize = new Vector2(0, 40);
		_loginContainer.AddChild(_loginPinInput);

		// Button container
		var loginButtonContainer = new HBoxContainer();
		loginButtonContainer.Alignment = BoxContainer.AlignmentMode.Center;
		loginButtonContainer.AddThemeConstantOverride("separation", 15);
		_loginContainer.AddChild(loginButtonContainer);

		// Login button
		_loginButton = new Button();
		_loginButton.Text = "Login";
		_loginButton.CustomMinimumSize = new Vector2(120, 40);
		loginButtonContainer.AddChild(_loginButton);

		// Cancel button
		_loginCancelButton = new Button();
		_loginCancelButton.Text = "Cancel";
		_loginCancelButton.CustomMinimumSize = new Vector2(120, 40);
		loginButtonContainer.AddChild(_loginCancelButton);

		// Status label
		_loginStatusLabel = new Label();
		_loginStatusLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_loginStatusLabel.AddThemeColorOverride("font_color", Colors.White);
		_loginStatusLabel.CustomMinimumSize = new Vector2(0, 30);
		_loginContainer.AddChild(_loginStatusLabel);

		// Show development mode indicator
		if (GameHost.IsDevelopmentContext())
		{
			_loginStatusLabel.Text = "Development Mode - PIN validation relaxed";
			_loginStatusLabel.Modulate = Colors.Orange;
		}
	}

	private void CreateAccountTab()
	{
		// Create account tab container
		_createContainer = new VBoxContainer();
		_createContainer.AddThemeConstantOverride("separation", 15);
		_tabContainer.AddChild(_createContainer);
		_tabContainer.SetTabTitle(1, "Create Account");

		// Title
		_createTitleLabel = new Label();
		_createTitleLabel.Text = "Create New Account";
		_createTitleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_createTitleLabel.AddThemeColorOverride("font_color", Colors.White);
		_createTitleLabel.AddThemeFontSizeOverride("font_size", 18);
		_createContainer.AddChild(_createTitleLabel);

		// Phone number input
		_createPhoneInput = new LineEdit();
		_createPhoneInput.PlaceholderText = "Phone Number (10 digits)";
		_createPhoneInput.CustomMinimumSize = new Vector2(0, 40);
		_createContainer.AddChild(_createPhoneInput);

		// PIN input
		_createPinInput = new LineEdit();
		_createPinInput.PlaceholderText = "PIN (4 digits)";
		_createPinInput.Secret = true;
		_createPinInput.CustomMinimumSize = new Vector2(0, 40);
		_createContainer.AddChild(_createPinInput);

		// Username input
		_createUsernameInput = new LineEdit();
		_createUsernameInput.PlaceholderText = "Username (max 7 characters)";
		_createUsernameInput.CustomMinimumSize = new Vector2(0, 40);
		_createContainer.AddChild(_createUsernameInput);

		// Button container
		var createButtonContainer = new HBoxContainer();
		createButtonContainer.Alignment = BoxContainer.AlignmentMode.Center;
		createButtonContainer.AddThemeConstantOverride("separation", 15);
		_createContainer.AddChild(createButtonContainer);

		// Create account button
		_createAccountButton = new Button();
		_createAccountButton.Text = "Create Account";
		_createAccountButton.CustomMinimumSize = new Vector2(140, 40);
		createButtonContainer.AddChild(_createAccountButton);

		// Cancel button
		_createCancelButton = new Button();
		_createCancelButton.Text = "Cancel";
		_createCancelButton.CustomMinimumSize = new Vector2(120, 40);
		createButtonContainer.AddChild(_createCancelButton);

		// Status label
		_createStatusLabel = new Label();
		_createStatusLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_createStatusLabel.AddThemeColorOverride("font_color", Colors.White);
		_createStatusLabel.CustomMinimumSize = new Vector2(0, 30);
		_createContainer.AddChild(_createStatusLabel);
	}

	private void ConnectSignals()
	{
		// Login tab signals
		if (_loginButton != null)
			_loginButton.Pressed += OnLoginPressed;

		if (_loginCancelButton != null)
			_loginCancelButton.Pressed += OnCancelPressed;

		// Create account tab signals
		if (_createAccountButton != null)
			_createAccountButton.Pressed += OnCreateAccountPressed;

		if (_createCancelButton != null)
			_createCancelButton.Pressed += OnCancelPressed;

		// Connect to user manager signals for status updates
		if (_userManager != null)
		{
			_userManager.UserLoggedIn += OnUserLoggedIn;
			_userManager.UserLoggedOut += OnUserLoggedOut;
		}

		// Handle Enter key in login input fields
		if (_loginPhoneInput != null)
			_loginPhoneInput.TextSubmitted += (_) => OnLoginPressed();

		if (_loginPinInput != null)
			_loginPinInput.TextSubmitted += (_) => OnLoginPressed();

		// Handle Enter key in create account input fields
		if (_createPhoneInput != null)
			_createPhoneInput.TextSubmitted += (_) => OnCreateAccountPressed();

		if (_createPinInput != null)
			_createPinInput.TextSubmitted += (_) => OnCreateAccountPressed();

		if (_createUsernameInput != null)
			_createUsernameInput.TextSubmitted += (_) => OnCreateAccountPressed();

		// Real-time input validation and formatting
		SetupInputValidation();
	}

	private void SetupInputValidation()
	{
		// Login tab validation
		if (_loginPhoneInput != null)
		{
			_loginPhoneInput.TextChanged += OnLoginPhoneChanged;
			_loginPhoneInput.FocusExited += OnLoginPhoneFocusExited;
		}

		if (_loginPinInput != null)
		{
			_loginPinInput.TextChanged += OnLoginPinChanged;
		}

		// Create account tab validation
		if (_createPhoneInput != null)
		{
			_createPhoneInput.TextChanged += OnCreatePhoneChanged;
			_createPhoneInput.FocusExited += OnCreatePhoneFocusExited;
		}

		if (_createPinInput != null)
		{
			_createPinInput.TextChanged += OnCreatePinChanged;
		}

		if (_createUsernameInput != null)
		{
			_createUsernameInput.TextChanged += OnCreateUsernameChanged;
		}
	}

	private void OnLoginPhoneChanged(string newText)
	{
		if (_loginPhoneInput == null) return;

		var cleaned = InputValidator.CleanPhoneNumber(newText);

		// Only clean digits during typing, don't format yet
		if (_loginPhoneInput.Text != cleaned)
		{
			var cursorPos = _loginPhoneInput.CaretColumn;
			_loginPhoneInput.Text = cleaned;
			_loginPhoneInput.CaretColumn = Math.Min(cursorPos, cleaned.Length);
		}

		// Update visual feedback
		var validationState = InputValidator.GetPhoneNumberValidationState(cleaned);
		UpdateInputValidationStyle(_loginPhoneInput, validationState);

		// Update login button state
		UpdateLoginButtonState();
	}

	private void OnLoginPhoneFocusExited()
	{
		if (_loginPhoneInput == null) return;

		// Format the phone number when user finishes entering it
		var cleaned = InputValidator.CleanPhoneNumber(_loginPhoneInput.Text);
		var formatted = InputValidator.FormatPhoneNumberDisplay(cleaned);
		_loginPhoneInput.Text = formatted;
	}

	private void OnLoginPinChanged(string newText)
	{
		if (_loginPinInput == null) return;

		var cleaned = InputValidator.CleanPin(newText);

		// Update the text without triggering another change event
		if (_loginPinInput.Text != cleaned)
		{
			var cursorPos = _loginPinInput.CaretColumn;
			_loginPinInput.Text = cleaned;
			_loginPinInput.CaretColumn = Math.Min(cursorPos, cleaned.Length);
		}

		// Update visual feedback
		var validationState = InputValidator.GetPinValidationState(cleaned);
		UpdateInputValidationStyle(_loginPinInput, validationState);

		// Update login button state
		UpdateLoginButtonState();
	}

	private void OnCreatePhoneChanged(string newText)
	{
		if (_createPhoneInput == null) return;

		var cleaned = InputValidator.CleanPhoneNumber(newText);

		// Only clean digits during typing, don't format yet
		if (_createPhoneInput.Text != cleaned)
		{
			var cursorPos = _createPhoneInput.CaretColumn;
			_createPhoneInput.Text = cleaned;
			_createPhoneInput.CaretColumn = Math.Min(cursorPos, cleaned.Length);
		}

		// Update visual feedback
		var validationState = InputValidator.GetPhoneNumberValidationState(cleaned);
		UpdateInputValidationStyle(_createPhoneInput, validationState);
	}

	private void OnCreatePhoneFocusExited()
	{
		if (_createPhoneInput == null) return;

		// Format the phone number when user finishes entering it
		var cleaned = InputValidator.CleanPhoneNumber(_createPhoneInput.Text);
		var formatted = InputValidator.FormatPhoneNumberDisplay(cleaned);
		_createPhoneInput.Text = formatted;
	}

	private void OnCreatePinChanged(string newText)
	{
		if (_createPinInput == null) return;

		var cleaned = InputValidator.CleanPin(newText);

		// Update the text without triggering another change event
		if (_createPinInput.Text != cleaned)
		{
			var cursorPos = _createPinInput.CaretColumn;
			_createPinInput.Text = cleaned;
			_createPinInput.CaretColumn = Math.Min(cursorPos, cleaned.Length);
		}

		// Update visual feedback
		var validationState = InputValidator.GetPinValidationState(cleaned);
		UpdateInputValidationStyle(_createPinInput, validationState);
	}

	private void OnCreateUsernameChanged(string newText)
	{
		if (_createUsernameInput == null) return;

		var cleaned = InputValidator.CleanUsername(newText);

		// Update the text without triggering another change event
		if (_createUsernameInput.Text != cleaned)
		{
			var cursorPos = _createUsernameInput.CaretColumn;
			_createUsernameInput.Text = cleaned;
			_createUsernameInput.CaretColumn = Math.Min(cursorPos, cleaned.Length);
		}

		// Update visual feedback
		var validationState = InputValidator.GetUsernameValidationState(cleaned);
		UpdateInputValidationStyle(_createUsernameInput, validationState);

		// Check username availability (debounced)
		CheckUsernameAvailabilityDebounced(cleaned);
	}

	private void UpdateInputValidationStyle(LineEdit input, InputValidator.ValidationState state)
	{
		if (input == null) return;

		// Create style override based on validation state
		var style = new StyleBoxFlat();

		switch (state)
		{
			case InputValidator.ValidationState.Valid:
				style.BgColor = new Color(0.2f, 0.4f, 0.2f, 1.0f); // Dark green
				style.BorderColor = new Color(0.4f, 0.8f, 0.4f, 1.0f); // Light green border
				break;
			case InputValidator.ValidationState.Incomplete:
				style.BgColor = new Color(0.3f, 0.3f, 0.2f, 1.0f); // Dark yellow
				style.BorderColor = new Color(0.6f, 0.6f, 0.4f, 1.0f); // Light yellow border
				break;
			case InputValidator.ValidationState.Invalid:
				style.BgColor = new Color(0.4f, 0.2f, 0.2f, 1.0f); // Dark red
				style.BorderColor = new Color(0.8f, 0.4f, 0.4f, 1.0f); // Light red border
				break;
			default: // None
				style.BgColor = new Color(0.2f, 0.2f, 0.2f, 1.0f); // Default dark
				style.BorderColor = new Color(0.4f, 0.4f, 0.4f, 1.0f); // Default border
				break;
		}

		style.BorderWidthTop = 2;
		style.BorderWidthBottom = 2;
		style.BorderWidthLeft = 2;
		style.BorderWidthRight = 2;
		style.CornerRadiusTopLeft = 4;
		style.CornerRadiusTopRight = 4;
		style.CornerRadiusBottomLeft = 4;
		style.CornerRadiusBottomRight = 4;

		input.AddThemeStyleboxOverride("normal", style);
	}

	private void ResetInputValidationStyle(LineEdit input)
	{
		if (input == null) return;

		// Remove the theme style override to return to default
		input.RemoveThemeStyleboxOverride("normal");
	}

	private void UpdateLoginButtonState()
	{
		if (_loginButton == null || _loginPhoneInput == null || _loginPinInput == null) return;

		string phoneNumber = _loginPhoneInput.Text.Trim();
		string pin = _loginPinInput.Text.Trim();

		// Validate phone number
		var cleanedPhone = InputValidator.CleanPhoneNumber(phoneNumber);
		bool isPhoneValid = InputValidator.IsValidPhoneNumber(cleanedPhone);

		// Validate PIN (relaxed in development mode)
		var cleanedPin = InputValidator.CleanPin(pin);
		bool isPinValid = GameHost.IsDevelopmentContext() ?
			!string.IsNullOrEmpty(cleanedPin) : // In dev mode, any non-empty PIN is valid
			InputValidator.IsValidPin(cleanedPin); // In production mode, requires 4 digits

		// Enable button only if both inputs are valid
		_loginButton.Disabled = !(isPhoneValid && isPinValid);
	}

	// Username availability checking with debouncing
	private Timer _usernameCheckTimer;
	private string _lastUsernameToCheck = "";

	private void CheckUsernameAvailabilityDebounced(string username)
	{
		if (string.IsNullOrEmpty(username) || !InputValidator.IsValidUsername(username))
		{
			return;
		}

		_lastUsernameToCheck = username;

		// Cancel previous timer
		if (_usernameCheckTimer != null)
		{
			_usernameCheckTimer.Stop();
			_usernameCheckTimer.QueueFree();
		}

		// Create new timer for debouncing
		_usernameCheckTimer = new Timer();
		_usernameCheckTimer.WaitTime = 0.5f; // 500ms debounce
		_usernameCheckTimer.OneShot = true;
		_usernameCheckTimer.Timeout += () => CheckUsernameAvailabilityAsync(_lastUsernameToCheck);
		AddChild(_usernameCheckTimer);
		_usernameCheckTimer.Start();
	}

	private async void CheckUsernameAvailabilityAsync(string username)
	{
		if (_userManager == null || string.IsNullOrEmpty(username)) return;

		try
		{
			var result = await _userManager.IsUsernameAvailableAsync(username);
			if (result.IsSuccess)
			{
				if (result.Value)
				{
					// Username is available
					if (_createStatusLabel != null && _createUsernameInput != null && _createUsernameInput.Text.Trim() == username)
					{
						_createStatusLabel.Text = "✓ Username available";
						_createStatusLabel.Modulate = Colors.Green;
					}
				}
				else
				{
					// Username is taken
					if (_createStatusLabel != null && _createUsernameInput != null && _createUsernameInput.Text.Trim() == username)
					{
						_createStatusLabel.Text = "✗ Username already taken";
						_createStatusLabel.Modulate = Colors.Red;
					}
				}
			}
		}
		catch (System.Exception ex)
		{
			// Silent fail for username availability check
			GD.PrintErr($"Username availability check failed: {ex.Message}");
		}
	}

	/// <summary>
	/// Show the login modal
	/// </summary>
	public void ShowModal()
	{
		Visible = true;

		// Focus first input field on the login tab
		if (_loginPhoneInput != null)
		{
			_loginPhoneInput.GrabFocus();
		}

		ClearForm();

		// Initialize login button state
		UpdateLoginButtonState();

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
		// Clear and reset login tab fields
		if (_loginPhoneInput != null)
		{
			_loginPhoneInput.Text = "";
			ResetInputValidationStyle(_loginPhoneInput);
		}
		if (_loginPinInput != null)
		{
			_loginPinInput.Text = "";
			ResetInputValidationStyle(_loginPinInput);
		}
		if (_loginStatusLabel != null)
		{
			_loginStatusLabel.Text = "";
			_loginStatusLabel.Modulate = Colors.White;
		}

		// Clear and reset create account tab fields
		if (_createPhoneInput != null)
		{
			_createPhoneInput.Text = "";
			ResetInputValidationStyle(_createPhoneInput);
		}
		if (_createPinInput != null)
		{
			_createPinInput.Text = "";
			ResetInputValidationStyle(_createPinInput);
		}
		if (_createUsernameInput != null)
		{
			_createUsernameInput.Text = "";
			ResetInputValidationStyle(_createUsernameInput);
		}
		if (_createStatusLabel != null)
		{
			_createStatusLabel.Text = "";
			_createStatusLabel.Modulate = Colors.White;
		}

		// Update button states after clearing
		UpdateLoginButtonState();
	}

	private void ShowLoginStatusMessage(string message, bool isSuccess)
	{
		if (_loginStatusLabel != null)
		{
			_loginStatusLabel.Text = message;
			_loginStatusLabel.Modulate = isSuccess ? Colors.Green : Colors.Red;
		}
	}

	private void ShowCreateStatusMessage(string message, bool isSuccess)
	{
		if (_createStatusLabel != null)
		{
			_createStatusLabel.Text = message;
			_createStatusLabel.Modulate = isSuccess ? Colors.Green : Colors.Red;
		}
	}

	// Event handlers

	private void OnBackgroundInput(InputEvent @event)
	{
		// Close modal when clicking background
		if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed && mouseEvent.ButtonIndex == MouseButton.Left)
		{
			OnCancelPressed();
		}
	}

	private async void OnLoginPressed()
	{
		if (_loginPhoneInput == null || _loginPinInput == null || _userManager == null)
		{
			ShowLoginStatusMessage("Login system not properly initialized", false);
			return;
		}

		string phoneNumber = _loginPhoneInput.Text.Trim();
		string pin = _loginPinInput.Text.Trim();

		// Validate phone number
		var cleanedPhone = InputValidator.CleanPhoneNumber(phoneNumber);
		if (!InputValidator.IsValidPhoneNumber(cleanedPhone))
		{
			ShowLoginStatusMessage("Please enter a valid 10-digit phone number", false);
			return;
		}

		// Validate PIN (relaxed in development mode)
		var cleanedPin = InputValidator.CleanPin(pin);
		if (!GameHost.IsDevelopmentContext() && !InputValidator.IsValidPin(cleanedPin))
		{
			ShowLoginStatusMessage("Please enter a valid 4-digit PIN", false);
			return;
		}

		try
		{
			ShowLoginStatusMessage("Logging in...", false);

			bool loginSuccess = await _userManager.LoginUserByPhoneAsync(cleanedPhone, cleanedPin);
			if (loginSuccess)
			{
				ShowLoginStatusMessage("Login successful!", true);
				// Modal will be hidden by OnUserLoggedIn signal
			}
			else
			{
				ShowLoginStatusMessage("Invalid phone number or PIN", false);
			}
		}
		catch (System.Exception ex)
		{
			ShowLoginStatusMessage($"Login error: {ex.Message}", false);
		}
	}

	private async void OnCreateAccountPressed()
	{
		if (_createPhoneInput == null || _createPinInput == null || _createUsernameInput == null || _userManager == null)
		{
			ShowCreateStatusMessage("Account creation system not properly initialized", false);
			return;
		}

		string phoneNumber = _createPhoneInput.Text.Trim();
		string pin = _createPinInput.Text.Trim();
		string username = _createUsernameInput.Text.Trim();

		// Validate inputs
		var cleanedPhone = InputValidator.CleanPhoneNumber(phoneNumber);
		if (!InputValidator.IsValidPhoneNumber(cleanedPhone))
		{
			ShowCreateStatusMessage("Please enter a valid 10-digit phone number", false);
			return;
		}

		var cleanedPin = InputValidator.CleanPin(pin);
		if (!InputValidator.IsValidPin(cleanedPin))
		{
			ShowCreateStatusMessage("Please enter a valid 4-digit PIN", false);
			return;
		}

		var cleanedUsername = InputValidator.CleanUsername(username);
		if (!InputValidator.IsValidUsername(cleanedUsername))
		{
			ShowCreateStatusMessage("Username must be 1-7 characters, alphanumeric and underscore only", false);
			return;
		}

		try
		{
			ShowCreateStatusMessage("Creating account...", false);

			var result = await _userManager.CreateUserAccountAsync(cleanedPhone, cleanedPin, cleanedUsername);
			if (result.IsSuccess)
			{
				ShowCreateStatusMessage("Account created successfully! You can now login.", true);

				// Switch to login tab and populate the phone number (formatted)
				_tabContainer.CurrentTab = 0;
				if (_loginPhoneInput != null)
				{
					_loginPhoneInput.Text = InputValidator.FormatPhoneNumberDisplay(cleanedPhone);
				}
			}
			else
			{
				ShowCreateStatusMessage(result.Error, false);
			}
		}
		catch (System.Exception ex)
		{
			ShowCreateStatusMessage($"Account creation error: {ex.Message}", false);
		}
	}

	private void OnCancelPressed()
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
		// Disconnect login tab signals
		if (GodotObject.IsInstanceValid(_loginButton))
			_loginButton.Pressed -= OnLoginPressed;

		if (GodotObject.IsInstanceValid(_loginCancelButton))
			_loginCancelButton.Pressed -= OnCancelPressed;

		// Disconnect create account tab signals
		if (GodotObject.IsInstanceValid(_createAccountButton))
			_createAccountButton.Pressed -= OnCreateAccountPressed;

		if (GodotObject.IsInstanceValid(_createCancelButton))
			_createCancelButton.Pressed -= OnCancelPressed;

		// Disconnect user manager signals
		if (GodotObject.IsInstanceValid(_userManager))
		{
			_userManager.UserLoggedIn -= OnUserLoggedIn;
			_userManager.UserLoggedOut -= OnUserLoggedOut;
		}

		base._ExitTree();
	}
}