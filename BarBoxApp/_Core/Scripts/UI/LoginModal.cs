using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Modal login dialog that appears as an overlay above all content
/// Provides login/logout functionality accessible from anywhere in the application
/// </summary>
public partial class LoginModal : Control
{
	[Signal] public delegate void LoginAttemptEventHandler(string phoneNumber, string pin);
	[Signal] public delegate void CreateUserAttemptEventHandler(string phoneNumber, string pin, string username);
	[Signal] public delegate void ModalClosedEventHandler();

	private const string LOGIN_TITLE = "Login to BarBox";
	private const string CREATE_TITLE = "Create New Account";
	private const string PHONE_PLACEHOLDER = "Phone Number (10 digits)";
	private const string PIN_PLACEHOLDER = "PIN (4 digits)";
	private const string USERNAME_PLACEHOLDER = "Username (max 7 characters)";
	private const string LOGIN_TEXT = "Login";
	private const string CREATE_ACCOUNT_TEXT = "Create Account";
	private const string CANCEL_TEXT = "Cancel";
	private const string DEV_MODE_TEXT = "Development Mode - PIN validation relaxed";
	private const string NOT_LOGGED_IN_TEXT = "Not logged in";
	private const string USERNAME_AVAILABLE = "✓ Username available";
	private const string USERNAME_TAKEN = "✗ Username already taken";
	private const string PHONE_INVALID = "Please enter a valid 10-digit phone number";
	private const string PIN_INVALID = "Please enter a valid 4-digit PIN";
	private const string USERNAME_INVALID = "Username must be 1-7 characters, alphanumeric and underscore only";
	private const string LOGIN_SYSTEM_ERROR = "Login system not properly initialized";
	private const string LOGGING_IN = "Logging in...";
	private const string LOGIN_SUCCESS = "Login successful!";
	private const string CREATE_SYSTEM_ERROR = "Account creation system not properly initialized";
	private const string VALIDATING = "Validating account information...";
	private const string CREATING_ACCOUNT = "Creating account...";
	private const string ACCOUNT_CREATED = "Account created successfully! You can now login.";

	private Panel _modalBackground;
	private Panel _modalPanel;
	private TabContainer _tabContainer;

	private VBoxContainer _loginContainer;
	private Label _loginTitleLabel;
	private LineEdit _loginPhoneInput;
	private LineEdit _loginPinInput;
	private Button _loginButton;
	private Button _loginCancelButton;
	private Label _loginStatusLabel;

	private VBoxContainer _createContainer;
	private Label _createTitleLabel;
	private LineEdit _createPhoneInput;
	private LineEdit _createPinInput;
	private LineEdit _createUsernameInput;
	private Button _createAccountButton;
	private Button _createCancelButton;
	private Label _createStatusLabel;

	private Node _onscreenKeyboard;
	private SessionManager _sessionManager;

	private bool _isLoginInProgress = false;
	private bool _isCreateAccountInProgress = false;

	private StyleBoxFlat _validStyle;
	private StyleBoxFlat _invalidStyle;
	private StyleBoxFlat _incompleteStyle;
	private StyleBoxFlat _noneStyle;

	public override void _Ready()
	{
		_sessionManager = SessionManager.GetInstance();

		SetupModalLayout();
		CreateModalUI();
		InitializeValidationStyles();
		ConnectSignals();
		Visible = false;
	}

	private void SetupModalLayout()
	{
		SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
	}

	private void CreateModalUI()
	{
		_modalBackground = new Panel();
		_modalBackground.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		var backgroundStyle = new StyleBoxFlat();
		backgroundStyle.BgColor = new Color(0.0f, 0.0f, 0.0f, 0.7f);
		_modalBackground.AddThemeStyleboxOverride("panel", backgroundStyle);
		_modalBackground.GuiInput += OnBackgroundInput;

		AddChild(_modalBackground);

		var centerContainer = new CenterContainer();
		centerContainer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		AddChild(centerContainer);

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

		var margin = new MarginContainer();
		margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		margin.AddThemeConstantOverride("margin_left", 20);
		margin.AddThemeConstantOverride("margin_right", 20);
		margin.AddThemeConstantOverride("margin_top", 20);
		margin.AddThemeConstantOverride("margin_bottom", 20);

		_modalPanel.AddChild(margin);

		_tabContainer = new TabContainer();
		_tabContainer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		margin.AddChild(_tabContainer);

		_tabContainer.TabChanged += OnTabChanged;

		CreateLoginTab();
		CreateAccountTab();
	}

	private void CreateLoginTab()
	{
		_loginContainer = new VBoxContainer();
		_loginContainer.AddThemeConstantOverride("separation", 15);
		_tabContainer.AddChild(_loginContainer);
		_tabContainer.SetTabTitle(0, "Login");

		_loginTitleLabel = new Label();
		_loginTitleLabel.Text = LOGIN_TITLE;
		_loginTitleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_loginTitleLabel.AddThemeColorOverride("font_color", Colors.White);
		_loginTitleLabel.AddThemeFontSizeOverride("font_size", 18);
		_loginContainer.AddChild(_loginTitleLabel);

		_loginPhoneInput = new LineEdit();
		_loginPhoneInput.PlaceholderText = PHONE_PLACEHOLDER;
		_loginPhoneInput.CustomMinimumSize = new Vector2(0, 40);
		_loginPhoneInput.SetMeta("keyboard_layout_type", "phone");
		_loginPhoneInput.AddToGroup("keyboard_field");
		_loginContainer.AddChild(_loginPhoneInput);

		_loginPinInput = new LineEdit();
		_loginPinInput.PlaceholderText = PIN_PLACEHOLDER;
		_loginPinInput.Secret = true;
		_loginPinInput.CustomMinimumSize = new Vector2(0, 40);
		_loginPinInput.SetMeta("keyboard_layout_type", "pin");
		_loginPinInput.AddToGroup("keyboard_field");
		_loginContainer.AddChild(_loginPinInput);

		var loginButtonContainer = new HBoxContainer();
		loginButtonContainer.Alignment = BoxContainer.AlignmentMode.Center;
		loginButtonContainer.AddThemeConstantOverride("separation", 15);
		_loginContainer.AddChild(loginButtonContainer);

		_loginButton = new Button();
		_loginButton.Text = LOGIN_TEXT;
		_loginButton.CustomMinimumSize = new Vector2(120, 40);
		loginButtonContainer.AddChild(_loginButton);

		_loginCancelButton = new Button();
		_loginCancelButton.Text = CANCEL_TEXT;
		_loginCancelButton.CustomMinimumSize = new Vector2(120, 40);
		loginButtonContainer.AddChild(_loginCancelButton);

		_loginStatusLabel = new Label();
		_loginStatusLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_loginStatusLabel.AddThemeColorOverride("font_color", Colors.White);
		_loginStatusLabel.CustomMinimumSize = new Vector2(0, 30);
		_loginStatusLabel.AutowrapMode = TextServer.AutowrapMode.Word;
		_loginStatusLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_loginContainer.AddChild(_loginStatusLabel);

		if (GameHost.IsDevelopmentContext())
		{
			_loginStatusLabel.Text = DEV_MODE_TEXT;
			_loginStatusLabel.Modulate = Colors.Orange;
		}
	}

	private void CreateAccountTab()
	{
		_createContainer = new VBoxContainer();
		_createContainer.AddThemeConstantOverride("separation", 15);
		_tabContainer.AddChild(_createContainer);
		_tabContainer.SetTabTitle(1, "Create Account");

		_createTitleLabel = new Label();
		_createTitleLabel.Text = CREATE_TITLE;
		_createTitleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_createTitleLabel.AddThemeColorOverride("font_color", Colors.White);
		_createTitleLabel.AddThemeFontSizeOverride("font_size", 18);
		_createContainer.AddChild(_createTitleLabel);

		_createPhoneInput = new LineEdit();
		_createPhoneInput.PlaceholderText = PHONE_PLACEHOLDER;
		_createPhoneInput.CustomMinimumSize = new Vector2(0, 40);
		_createPhoneInput.SetMeta("keyboard_layout_type", "phone");
		_createPhoneInput.AddToGroup("keyboard_field");
		_createContainer.AddChild(_createPhoneInput);

		_createPinInput = new LineEdit();
		_createPinInput.PlaceholderText = PIN_PLACEHOLDER;
		_createPinInput.Secret = true;
		_createPinInput.CustomMinimumSize = new Vector2(0, 40);
		_createPinInput.SetMeta("keyboard_layout_type", "pin");
		_createPinInput.AddToGroup("keyboard_field");
		_createContainer.AddChild(_createPinInput);

		_createUsernameInput = new LineEdit();
		_createUsernameInput.PlaceholderText = USERNAME_PLACEHOLDER;
		_createUsernameInput.CustomMinimumSize = new Vector2(0, 40);
		_createUsernameInput.SetMeta("keyboard_layout_type", "standard-characters");
		_createUsernameInput.AddToGroup("keyboard_field");
		_createContainer.AddChild(_createUsernameInput);

		var createButtonContainer = new HBoxContainer();
		createButtonContainer.Alignment = BoxContainer.AlignmentMode.Center;
		createButtonContainer.AddThemeConstantOverride("separation", 15);
		_createContainer.AddChild(createButtonContainer);

		_createAccountButton = new Button();
		_createAccountButton.Text = CREATE_ACCOUNT_TEXT;
		_createAccountButton.CustomMinimumSize = new Vector2(140, 40);
		createButtonContainer.AddChild(_createAccountButton);

		_createCancelButton = new Button();
		_createCancelButton.Text = CANCEL_TEXT;
		_createCancelButton.CustomMinimumSize = new Vector2(120, 40);
		createButtonContainer.AddChild(_createCancelButton);

		_createStatusLabel = new Label();
		_createStatusLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_createStatusLabel.AddThemeColorOverride("font_color", Colors.White);
		_createStatusLabel.CustomMinimumSize = new Vector2(0, 30);
		_createStatusLabel.AutowrapMode = TextServer.AutowrapMode.Word;
		_createStatusLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_createContainer.AddChild(_createStatusLabel);

		_usernameCheckTimer = new Timer();
		_usernameCheckTimer.WaitTime = 0.5f;
		_usernameCheckTimer.OneShot = true;
		_usernameCheckTimer.Timeout += OnUsernameCheckTimeout;
		AddChild(_usernameCheckTimer);
	}

	private void ConnectSignals()
	{
		if (_loginButton != null)
			_loginButton.Pressed += OnLoginPressed;

		if (_loginCancelButton != null)
			_loginCancelButton.Pressed += OnCancelPressed;

		if (_createAccountButton != null)
			_createAccountButton.Pressed += OnCreateAccountPressed;

		if (_createCancelButton != null)
			_createCancelButton.Pressed += OnCancelPressed;

		if (_sessionManager != null)
		{
			_sessionManager.UserLoggedIn += OnUserLoggedIn;
			_sessionManager.UserLoggedOut += OnUserLoggedOut;
		}

		if (_loginPhoneInput != null)
			_loginPhoneInput.TextSubmitted += (_) => OnLoginPressed();

		if (_loginPinInput != null)
			_loginPinInput.TextSubmitted += (_) => OnLoginPressed();

		if (_createPhoneInput != null)
			_createPhoneInput.TextSubmitted += (_) => OnCreateAccountPressed();

		if (_createPinInput != null)
			_createPinInput.TextSubmitted += (_) => OnCreateAccountPressed();

		if (_createUsernameInput != null)
			_createUsernameInput.TextSubmitted += (_) => OnCreateAccountPressed();

		SetupInputValidation();
	}

	private void SetupInputValidation()
	{
		if (_loginPhoneInput != null)
		{
			_loginPhoneInput.TextChanged += OnLoginPhoneChanged;
			_loginPhoneInput.FocusExited += OnLoginPhoneFocusExited;
		}

		if (_loginPinInput != null)
		{
			_loginPinInput.TextChanged += OnLoginPinChanged;
		}

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

		if (_loginPhoneInput.Text != cleaned)
		{
			var cursorPos = _loginPhoneInput.CaretColumn;
			_loginPhoneInput.Text = cleaned;
			_loginPhoneInput.CaretColumn = Math.Min(cursorPos, cleaned.Length);
		}

		var validationState = InputValidator.GetPhoneNumberValidationState(cleaned);
		UpdateInputValidationStyle(_loginPhoneInput, validationState);

		UpdateLoginButtonState();
	}

	private void OnLoginPhoneFocusExited()
	{
		if (_loginPhoneInput == null) return;

		var cleaned = InputValidator.CleanPhoneNumber(_loginPhoneInput.Text);
		var formatted = InputValidator.FormatPhoneNumberDisplay(cleaned);
		_loginPhoneInput.Text = formatted;
	}

	private void OnLoginPinChanged(string newText)
	{
		if (_loginPinInput == null) return;

		var cleaned = InputValidator.CleanPin(newText);

		if (_loginPinInput.Text != cleaned)
		{
			var cursorPos = _loginPinInput.CaretColumn;
			_loginPinInput.Text = cleaned;
			_loginPinInput.CaretColumn = Math.Min(cursorPos, cleaned.Length);
		}

		var validationState = InputValidator.GetPinValidationState(cleaned);
		UpdateInputValidationStyle(_loginPinInput, validationState);

		UpdateLoginButtonState();
	}

	private void OnCreatePhoneChanged(string newText)
	{
		if (_createPhoneInput == null) return;

		var cleaned = InputValidator.CleanPhoneNumber(newText);

		if (_createPhoneInput.Text != cleaned)
		{
			var cursorPos = _createPhoneInput.CaretColumn;
			_createPhoneInput.Text = cleaned;
			_createPhoneInput.CaretColumn = Math.Min(cursorPos, cleaned.Length);
		}

		var validationState = InputValidator.GetPhoneNumberValidationState(cleaned);
		UpdateInputValidationStyle(_createPhoneInput, validationState);

		UpdateCreateAccountButtonState();
	}

	private void OnCreatePhoneFocusExited()
	{
		if (_createPhoneInput == null) return;

		var cleaned = InputValidator.CleanPhoneNumber(_createPhoneInput.Text);
		var formatted = InputValidator.FormatPhoneNumberDisplay(cleaned);
		_createPhoneInput.Text = formatted;
	}

	private void OnCreatePinChanged(string newText)
	{
		if (_createPinInput == null) return;

		var cleaned = InputValidator.CleanPin(newText);

		if (_createPinInput.Text != cleaned)
		{
			var cursorPos = _createPinInput.CaretColumn;
			_createPinInput.Text = cleaned;
			_createPinInput.CaretColumn = Math.Min(cursorPos, cleaned.Length);
		}

		var validationState = InputValidator.GetPinValidationState(cleaned);
		UpdateInputValidationStyle(_createPinInput, validationState);

		UpdateCreateAccountButtonState();
	}

	private void OnCreateUsernameChanged(string newText)
	{
		if (_createUsernameInput == null) return;

		var cleaned = InputValidator.CleanUsername(newText);

		if (_createUsernameInput.Text != cleaned)
		{
			var cursorPos = _createUsernameInput.CaretColumn;
			_createUsernameInput.Text = cleaned;
			_createUsernameInput.CaretColumn = Math.Min(cursorPos, cleaned.Length);
		}

		var validationState = InputValidator.GetUsernameValidationState(cleaned);
		UpdateInputValidationStyle(_createUsernameInput, validationState);

		UpdateCreateAccountButtonState();

		CheckUsernameAvailabilityDebounced(cleaned);
	}

	private void UpdateInputValidationStyle(LineEdit input, InputValidator.ValidationState state)
	{
		if (input == null) return;

		StyleBoxFlat style = state switch
		{
			InputValidator.ValidationState.Valid => _validStyle,
			InputValidator.ValidationState.Incomplete => _incompleteStyle,
			InputValidator.ValidationState.Invalid => _invalidStyle,
			_ => _noneStyle
		};

		input.AddThemeStyleboxOverride("normal", style);
	}

	private void ResetInputValidationStyle(LineEdit input)
	{
		if (input == null) return;

		input.RemoveThemeStyleboxOverride("normal");
	}

	private void InitializeValidationStyles()
	{
		_validStyle = CreateStyleBox(
			new Color(0.2f, 0.4f, 0.2f, 1.0f),
			new Color(0.4f, 0.8f, 0.4f, 1.0f)
		);

		_incompleteStyle = CreateStyleBox(
			new Color(0.3f, 0.3f, 0.2f, 1.0f),
			new Color(0.6f, 0.6f, 0.4f, 1.0f)
		);

		_invalidStyle = CreateStyleBox(
			new Color(0.4f, 0.2f, 0.2f, 1.0f),
			new Color(0.8f, 0.4f, 0.4f, 1.0f)
		);

		_noneStyle = CreateStyleBox(
			new Color(0.2f, 0.2f, 0.2f, 1.0f),
			new Color(0.4f, 0.4f, 0.4f, 1.0f)
		);
	}

	private StyleBoxFlat CreateStyleBox(Color bgColor, Color borderColor)
	{
		var style = new StyleBoxFlat();
		style.BgColor = bgColor;
		style.BorderColor = borderColor;
		style.BorderWidthTop = 2;
		style.BorderWidthBottom = 2;
		style.BorderWidthLeft = 2;
		style.BorderWidthRight = 2;
		style.CornerRadiusTopLeft = 4;
		style.CornerRadiusTopRight = 4;
		style.CornerRadiusBottomLeft = 4;
		style.CornerRadiusBottomRight = 4;
		return style;
	}

	private void SetLoginUIEnabled(bool enabled)
	{
		if (_loginPhoneInput != null)
		{
			_loginPhoneInput.Editable = enabled;
			_loginPhoneInput.Modulate = enabled ? Colors.White : new Color(0.7f, 0.7f, 0.7f);
		}

		if (_loginPinInput != null)
		{
			_loginPinInput.Editable = enabled;
			_loginPinInput.Modulate = enabled ? Colors.White : new Color(0.7f, 0.7f, 0.7f);
		}

		if (_loginButton != null)
		{
			_loginButton.Disabled = !enabled;
			_loginButton.Modulate = enabled ? Colors.White : new Color(0.5f, 0.5f, 0.5f);
		}

		if (_loginCancelButton != null)
		{
			_loginCancelButton.Disabled = !enabled;
		}

		if (_tabContainer != null)
		{
			_tabContainer.TabsVisible = enabled;
		}
	}

	private void SetCreateAccountUIEnabled(bool enabled)
	{
		if (_createPhoneInput != null)
		{
			_createPhoneInput.Editable = enabled;
			_createPhoneInput.Modulate = enabled ? Colors.White : new Color(0.7f, 0.7f, 0.7f);
		}

		if (_createPinInput != null)
		{
			_createPinInput.Editable = enabled;
			_createPinInput.Modulate = enabled ? Colors.White : new Color(0.7f, 0.7f, 0.7f);
		}

		if (_createUsernameInput != null)
		{
			_createUsernameInput.Editable = enabled;
			_createUsernameInput.Modulate = enabled ? Colors.White : new Color(0.7f, 0.7f, 0.7f);
		}

		if (_createAccountButton != null)
		{
			_createAccountButton.Disabled = !enabled;
			_createAccountButton.Modulate = enabled ? Colors.White : new Color(0.5f, 0.5f, 0.5f);
		}

		if (_createCancelButton != null)
		{
			_createCancelButton.Disabled = !enabled;
		}

		if (_tabContainer != null)
		{
			_tabContainer.TabsVisible = enabled;
		}
	}

	private void UpdateLoginButtonState()
	{
		if (_loginButton == null || _loginPhoneInput == null || _loginPinInput == null) return;

		string phoneNumber = _loginPhoneInput.Text.Trim();
		string pin = _loginPinInput.Text.Trim();

		var cleanedPhone = InputValidator.CleanPhoneNumber(phoneNumber);
		bool isPhoneValid = InputValidator.IsValidPhoneNumber(cleanedPhone);

		var cleanedPin = InputValidator.CleanPin(pin);
		bool isPinValid = InputValidator.IsValidPin(cleanedPin);

		_loginButton.Disabled = !isPhoneValid || !isPinValid || _isLoginInProgress;
	}

	private void UpdateCreateAccountButtonState()
	{
		if (_createAccountButton == null || _createPhoneInput == null ||
		    _createPinInput == null || _createUsernameInput == null) return;

		string phoneNumber = _createPhoneInput.Text.Trim();
		string pin = _createPinInput.Text.Trim();
		string username = _createUsernameInput.Text.Trim();

		var cleanedPhone = InputValidator.CleanPhoneNumber(phoneNumber);
		bool isPhoneValid = InputValidator.IsValidPhoneNumber(cleanedPhone);

		var cleanedPin = InputValidator.CleanPin(pin);
		bool isPinValid = InputValidator.IsValidPin(cleanedPin);

		var cleanedUsername = InputValidator.CleanUsername(username);
		bool isUsernameValid = InputValidator.IsValidUsername(cleanedUsername);

		_createAccountButton.Disabled = !isPhoneValid || !isPinValid || !isUsernameValid || _isCreateAccountInProgress;
	}

	private Timer _usernameCheckTimer;
	private string _lastUsernameToCheck = "";

	private void CheckUsernameAvailabilityDebounced(string username)
	{
		if (string.IsNullOrEmpty(username) || !InputValidator.IsValidUsername(username))
		{
			if (_usernameCheckTimer != null && !_usernameCheckTimer.IsStopped())
			{
				_usernameCheckTimer.Stop();
			}
			return;
		}

		_lastUsernameToCheck = username;

		if (_usernameCheckTimer != null)
		{
			_usernameCheckTimer.Stop();
			_usernameCheckTimer.Start();
		}
	}

	private void OnUsernameCheckTimeout()
	{
		CheckUsernameAvailabilityAsync(_lastUsernameToCheck);
	}

	private async void CheckUsernameAvailabilityAsync(string username)
	{
		if (_sessionManager == null || string.IsNullOrEmpty(username)) return;

		try
		{
			var result = await _sessionManager.IsUsernameAvailableAsync(username);
			if (result.IsSuccess(out var isAvailable))
			{
				if (isAvailable)
				{
					if (_createStatusLabel != null && _createUsernameInput != null && _createUsernameInput.Text.Trim() == username)
					{
						_createStatusLabel.Text = USERNAME_AVAILABLE;
						_createStatusLabel.Modulate = Colors.Green;
					}
				}
				else
				{
					if (_createStatusLabel != null && _createUsernameInput != null && _createUsernameInput.Text.Trim() == username)
					{
						_createStatusLabel.Text = USERNAME_TAKEN;
						_createStatusLabel.Modulate = Colors.Red;
					}
				}
			}
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"Username availability check failed: {ex.Message}");
		}
	}

	public void ShowModal()
	{
		Visible = true;

		if (_loginPhoneInput != null)
		{
			_loginPhoneInput.GrabFocus();
		}

		ClearForm();
		UpdateLoginButtonState();
	}

	public void HideModal()
	{
		Visible = false;
		ClearForm();
		EmitSignal(SignalName.ModalClosed);
	}

	private void ClearForm()
	{
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

	private void OnBackgroundInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed && mouseEvent.ButtonIndex == MouseButton.Left)
		{
			OnCancelPressed();
		}
	}

	private async void OnLoginPressed()
	{
		if (_isLoginInProgress) return;

		if (_loginPhoneInput == null || _loginPinInput == null || _sessionManager == null)
		{
			ShowLoginStatusMessage(LOGIN_SYSTEM_ERROR, false);
			return;
		}

		try
		{
			string phoneNumber = _loginPhoneInput.Text.Trim();
			string pin = _loginPinInput.Text.Trim();

			var cleanedPhone = InputValidator.CleanPhoneNumber(phoneNumber);
			if (!InputValidator.IsValidPhoneNumber(cleanedPhone))
			{
				ShowLoginStatusMessage(PHONE_INVALID, false);
				return;
			}

			var cleanedPin = InputValidator.CleanPin(pin);
			if (!InputValidator.IsValidPin(cleanedPin))
			{
				ShowLoginStatusMessage(PIN_INVALID, false);
				return;
			}

			_isLoginInProgress = true;
			SetLoginUIEnabled(false);
			UpdateLoginButtonState();

			ShowLoginStatusMessage(LOGGING_IN, false);

			var loginResult = await _sessionManager.LoginUserByPhoneAsync(cleanedPhone, cleanedPin);
			if (loginResult.IsSuccess(out var _))
			{
				ShowLoginStatusMessage(LOGIN_SUCCESS, true);

				await AutoloadBase.StaticDelayAsync(0.5f);

				HideModal();
			}
			else if (loginResult.IsFailure(out var loginError))
			{
				ShowLoginStatusMessage(loginError.Message, false);
			}
		}
		catch (System.Exception ex)
		{
			ShowLoginStatusMessage($"Login error: {ex.Message}", false);
		}
		finally
		{
			_isLoginInProgress = false;
			SetLoginUIEnabled(true);
			UpdateLoginButtonState();
		}
	}

	private async void OnCreateAccountPressed()
	{
		if (_isCreateAccountInProgress) return;

		if (_createPhoneInput == null || _createPinInput == null || _createUsernameInput == null || _sessionManager == null)
		{
			ShowCreateStatusMessage(CREATE_SYSTEM_ERROR, false);
			return;
		}

		try
		{
			string phoneNumber = _createPhoneInput.Text.Trim();
			string pin = _createPinInput.Text.Trim();
			string username = _createUsernameInput.Text.Trim();

			var cleanedPhone = InputValidator.CleanPhoneNumber(phoneNumber);
			if (!InputValidator.IsValidPhoneNumber(cleanedPhone))
			{
				ShowCreateStatusMessage(PHONE_INVALID, false);
				return;
			}

			var cleanedPin = InputValidator.CleanPin(pin);
			if (!InputValidator.IsValidPin(cleanedPin))
			{
				ShowCreateStatusMessage(PIN_INVALID, false);
				return;
			}

			var cleanedUsername = InputValidator.CleanUsername(username);
			if (!InputValidator.IsValidUsername(cleanedUsername))
			{
				ShowCreateStatusMessage(USERNAME_INVALID, false);
				return;
			}

			_isCreateAccountInProgress = true;
			SetCreateAccountUIEnabled(false);
			UpdateCreateAccountButtonState();

			ShowCreateStatusMessage(VALIDATING, false);

			var validationResult = await _sessionManager.ValidatePlayerCreationAsync(cleanedPhone, cleanedPin, cleanedUsername);
			if (validationResult.IsFailure(out var valError))
			{
				var userFriendlyError = BarBox.Core.Utils.ErrorMessageHelper.GetUserFriendlyError(valError.Message);
				ShowCreateStatusMessage(userFriendlyError, false);
				return;
			}

			if (validationResult.IsSuccess(out var validationResponse) && !validationResponse.Valid)
			{
				List<string> errorMessages = [];
				foreach (var error in validationResponse.Errors)
				{
					errorMessages.Add(MapValidationError(error));
				}

				var combinedMessage = string.Join("\n", errorMessages);
				ShowCreateStatusMessage(combinedMessage, false);
				return;
			}

			ShowCreateStatusMessage(CREATING_ACCOUNT, false);

			var result = await _sessionManager.CreateUserAccountAsync(cleanedPhone, cleanedPin, cleanedUsername);
			if (result.IsSuccess(out var _))
			{
				ShowCreateStatusMessage(ACCOUNT_CREATED, true);

				await AutoloadBase.StaticDelayAsync(0.7f);

				_tabContainer.CurrentTab = 0;
				if (_loginPhoneInput != null)
				{
					_loginPhoneInput.Text = InputValidator.FormatPhoneNumberDisplay(cleanedPhone);
					_loginPhoneInput.GrabFocus();
				}
			}
			else if (result.IsFailure(out var createError))
			{
				ShowCreateStatusMessage(createError.Message, false);
			}
		}
		catch (System.Exception ex)
		{
			var userFriendlyError = BarBox.Core.Utils.ErrorMessageHelper.GetUserFriendlyError(ex.Message);
		ShowCreateStatusMessage(userFriendlyError, false);
		}
		finally
		{
			_isCreateAccountInProgress = false;
			SetCreateAccountUIEnabled(true);
			UpdateCreateAccountButtonState();
		}
	}

	private string MapValidationError(ValidationErrorDetail error)
	{
		return BarBox.Core.Utils.ErrorMessageHelper.FormatValidationError(
			error.Field,
			error.Message,
			error.Value?.ToString());
	}


	private void OnTabChanged(long tabIndex)
	{
		// Get keyboard reference if not cached
		if (_onscreenKeyboard == null)
		{
			_onscreenKeyboard = GetTree().Root.GetNode<Node>("Main/KeyboardLayer/OnscreenKeyboard");
		}

		// Notify keyboard to update position if it's showing
		if (IsInstanceValid(_onscreenKeyboard) && _onscreenKeyboard.Get("visible").AsBool())
		{
			// Call keyboard's update position method
			// This ensures keyboard repositions based on new focused field
			if (_onscreenKeyboard.HasMethod("_update_keyboard_position_for_focus"))
			{
				_onscreenKeyboard.Call("_update_keyboard_position_for_focus");
			}
		}
	}

	private void OnCancelPressed()
	{
		HideModal();
	}

	private void OnUserLoggedIn(string phoneNumber)
	{
		var session = _sessionManager?.GetSessionByPhone(phoneNumber);
		var userName = session?.UserName ?? string.Empty;

		GD.Print($"[LoginModal] User logged in: {userName} ({phoneNumber})");
	}

	private void OnUserLoggedOut(string phoneNumber)
	{
		ClearForm();
	}

	public override void _ExitTree()
	{
		if (GodotObject.IsInstanceValid(_tabContainer))
		{
			_tabContainer.TabChanged -= OnTabChanged;
		}

		if (GodotObject.IsInstanceValid(_loginButton))
			_loginButton.Pressed -= OnLoginPressed;

		if (GodotObject.IsInstanceValid(_loginCancelButton))
			_loginCancelButton.Pressed -= OnCancelPressed;

		if (GodotObject.IsInstanceValid(_loginPhoneInput))
		{
			_loginPhoneInput.TextChanged -= OnLoginPhoneChanged;
			_loginPhoneInput.FocusExited -= OnLoginPhoneFocusExited;
		}

		if (GodotObject.IsInstanceValid(_loginPinInput))
		{
			_loginPinInput.TextChanged -= OnLoginPinChanged;
		}

		if (GodotObject.IsInstanceValid(_createAccountButton))
			_createAccountButton.Pressed -= OnCreateAccountPressed;

		if (GodotObject.IsInstanceValid(_createCancelButton))
			_createCancelButton.Pressed -= OnCancelPressed;

		if (GodotObject.IsInstanceValid(_createPhoneInput))
		{
			_createPhoneInput.TextChanged -= OnCreatePhoneChanged;
			_createPhoneInput.FocusExited -= OnCreatePhoneFocusExited;
		}

		if (GodotObject.IsInstanceValid(_createPinInput))
		{
			_createPinInput.TextChanged -= OnCreatePinChanged;
		}

		if (GodotObject.IsInstanceValid(_createUsernameInput))
		{
			_createUsernameInput.TextChanged -= OnCreateUsernameChanged;
		}

		if (GodotObject.IsInstanceValid(_sessionManager))
		{
			_sessionManager.UserLoggedIn -= OnUserLoggedIn;
			_sessionManager.UserLoggedOut -= OnUserLoggedOut;
		}

		if (GodotObject.IsInstanceValid(_usernameCheckTimer))
		{
			_usernameCheckTimer.Timeout -= OnUsernameCheckTimeout;
			_usernameCheckTimer.QueueFree();
		}

		base._ExitTree();
	}
}