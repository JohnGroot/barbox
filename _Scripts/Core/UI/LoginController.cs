using Godot;

[GlobalClass]
public partial class LoginController : Control
{
	[ExportCategory("UI References")]
	[Export] public LineEdit UserIdInput { get; set; }
	[Export] public LineEdit PinInput { get; set; }
	[Export] public Button LoginButton { get; set; }
	[Export] public Button CreateUserButton { get; set; }
	[Export] public Label StatusLabel { get; set; }
	[Export] public Control CreateUserPanel { get; set; }

	[ExportCategory("Create User UI")]
	[Export] public LineEdit NewUserIdInput { get; set; }
	[Export] public LineEdit NewPinInput { get; set; }
	[Export] public LineEdit ConfirmPinInput { get; set; }
	[Export] public Button ConfirmCreateButton { get; set; }
	[Export] public Button CancelCreateButton { get; set; }

	private UserManager _userManager;

	public override void _Ready()
	{
		_userManager = UserManager.GetAutoload();
		ConnectSignals();
		SetupUI();
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
		if (LoginButton != null)
			LoginButton.Pressed += OnLoginPressed;
		
		if (CreateUserButton != null)
			CreateUserButton.Pressed += OnCreateUserPressed;
		
		if (ConfirmCreateButton != null)
			ConfirmCreateButton.Pressed += OnConfirmCreatePressed;
		
		if (CancelCreateButton != null)
			CancelCreateButton.Pressed += OnCancelCreatePressed;

		// Enter key handling
		if (UserIdInput != null)
			UserIdInput.TextSubmitted += OnLoginSubmitted;
		
		if (PinInput != null)
			PinInput.TextSubmitted += OnLoginSubmitted;
	}

	private void DisconnectSignals()
	{
		if (GodotObject.IsInstanceValid(LoginButton))
			LoginButton.Pressed -= OnLoginPressed;
		
		if (GodotObject.IsInstanceValid(CreateUserButton))
			CreateUserButton.Pressed -= OnCreateUserPressed;
		
		if (GodotObject.IsInstanceValid(ConfirmCreateButton))
			ConfirmCreateButton.Pressed -= OnConfirmCreatePressed;
		
		if (GodotObject.IsInstanceValid(CancelCreateButton))
			CancelCreateButton.Pressed -= OnCancelCreatePressed;

		if (GodotObject.IsInstanceValid(UserIdInput))
			UserIdInput.TextSubmitted -= OnLoginSubmitted;
		
		if (GodotObject.IsInstanceValid(PinInput))
			PinInput.TextSubmitted -= OnLoginSubmitted;
	}

	private void SetupUI()
	{
		if (CreateUserPanel != null)
			CreateUserPanel.Visible = false;
		
		// Setup input field properties
		if (UserIdInput != null)
		{
			UserIdInput.MaxLength = 5;
			UserIdInput.PlaceholderText = "User ID (max 5 chars)";
		}
		
		if (PinInput != null)
		{
			PinInput.MaxLength = 5;
			PinInput.Secret = true;
			PinInput.PlaceholderText = "PIN (max 5 chars)";
		}
		
		if (NewUserIdInput != null)
		{
			NewUserIdInput.MaxLength = 5;
			NewUserIdInput.PlaceholderText = "User ID (max 5 chars)";
		}
		
		if (NewPinInput != null)
		{
			NewPinInput.MaxLength = 5;
			NewPinInput.Secret = true;
			NewPinInput.PlaceholderText = "PIN (max 5 chars)";
		}
		
		if (ConfirmPinInput != null)
		{
			ConfirmPinInput.MaxLength = 5;
			ConfirmPinInput.Secret = true;
			ConfirmPinInput.PlaceholderText = "Confirm PIN";
		}

		ClearStatusMessage();
	}

	private void OnLoginPressed()
	{
		AttemptLogin();
	}

	private void OnLoginSubmitted(string text)
	{
		AttemptLogin();
	}

	private void AttemptLogin()
	{
		if (_userManager == null) return;

		string userId = UserIdInput?.Text?.Trim() ?? string.Empty;
		string pin = PinInput?.Text?.Trim() ?? string.Empty;

		if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(pin))
		{
			ShowStatusMessage("Please enter both User ID and PIN", false);
			return;
		}

		bool loginSuccess = _userManager.LoginUser(userId, pin);
		
		if (loginSuccess)
		{
			ClearInputs();
			ClearStatusMessage();
		}
		else
		{
			ShowStatusMessage("Invalid User ID or PIN", false);
		}
	}

	private void OnCreateUserPressed()
	{
		if (CreateUserPanel != null)
			CreateUserPanel.Visible = true;
		
		ClearCreateUserInputs();
		ClearStatusMessage();
	}

	private void OnConfirmCreatePressed()
	{
		if (_userManager == null) return;

		string userId = NewUserIdInput?.Text?.Trim() ?? string.Empty;
		string pin = NewPinInput?.Text?.Trim() ?? string.Empty;
		string confirmPin = ConfirmPinInput?.Text?.Trim() ?? string.Empty;

		if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(pin))
		{
			ShowStatusMessage("Please fill in all fields", false);
			return;
		}

		if (pin != confirmPin)
		{
			ShowStatusMessage("PINs do not match", false);
			return;
		}

		var newUser = _userManager.CreateUser(userId, pin);
		
		if (newUser != null)
		{
			ShowStatusMessage("User created successfully! You can now log in.", true);
			OnCancelCreatePressed();
			
			// Auto-fill login form
			if (UserIdInput != null)
				UserIdInput.Text = userId;
		}
		else
		{
			ShowStatusMessage("User already exists or invalid input", false);
		}
	}

	private void OnCancelCreatePressed()
	{
		if (CreateUserPanel != null)
			CreateUserPanel.Visible = false;
		
		ClearCreateUserInputs();
		ClearStatusMessage();
	}

	private void ClearInputs()
	{
		if (UserIdInput != null)
			UserIdInput.Text = string.Empty;
		
		if (PinInput != null)
			PinInput.Text = string.Empty;
	}

	private void ClearCreateUserInputs()
	{
		if (NewUserIdInput != null)
			NewUserIdInput.Text = string.Empty;
		
		if (NewPinInput != null)
			NewPinInput.Text = string.Empty;
		
		if (ConfirmPinInput != null)
			ConfirmPinInput.Text = string.Empty;
	}

	private void ShowStatusMessage(string message, bool isSuccess)
	{
		if (StatusLabel != null)
		{
			StatusLabel.Text = message;
			StatusLabel.Modulate = isSuccess ? Colors.Green : Colors.Red;
		}
	}

	private void ClearStatusMessage()
	{
		if (StatusLabel != null)
		{
			StatusLabel.Text = string.Empty;
		}
	}
}