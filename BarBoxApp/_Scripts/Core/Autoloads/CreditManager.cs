using Godot;
using System;
using System.Threading.Tasks;

/// <summary>
/// Manages credit checking, spending, and purchasing flows
/// Provides UI overlays for login prompts, credit purchasing, and spending confirmation
/// </summary>
public partial class CreditManager : AutoloadBase
{
	[Signal] public delegate void CreditsSpentEventHandler(string userId, int amount, string feature);
	[Signal] public delegate void CreditsPurchasedEventHandler(string userId, int amount);
	[Signal] public delegate void CreditCheckCancelledEventHandler(string feature);
	
	public static CreditManager Instance { get; private set; }
	
	private UserManager _userManager;
	private CanvasLayer _uiLayer;
	
	// UI Overlays
	private Control _loginPromptOverlay;
	private Control _buyCreditOverlay;
	private Control _confirmSpendOverlay;
	
	// Current transaction state
	private int _pendingCreditAmount;
	private string _pendingFeatureName;
	private TaskCompletionSource<bool> _currentTransactionTask;
	
	protected override void OnServiceReady()
	{
		Instance = this;
		
		_userManager = GetAutoload<UserManager>();
		if (_userManager == null)
		{
			LogError("UserManager not found - CreditManager requires UserManager");
			return;
		}
		
		// Create UI layer for overlays
		_uiLayer = new CanvasLayer();
		_uiLayer.Layer = 10; // High layer to appear above game content
		AddChild(_uiLayer);
		
		// Create UI overlays
		CreateLoginPromptOverlay();
		CreateBuyCreditOverlay();
		CreateConfirmSpendOverlay();
		
		LogInfo("CreditManager initialized");
	}
	
	/// <summary>
	/// Main entry point for credit checking flow
	/// Returns true if credits were successfully spent, false otherwise
	/// </summary>
	public async Task<bool> CheckAndSpendCredits(int amount, string featureName)
	{
		LogInfo($"Checking credits for {featureName} (cost: {amount})");
		
		// If in development mode and credits should be bypassed
		if (GameHost.ShouldBypassCredits())
		{
			LogInfo($"Development mode - bypassing credit check for {featureName}");
			EmitSignal(SignalName.CreditsSpent, "dev_user", amount, featureName);
			return true;
		}
		
		// Store pending transaction info
		_pendingCreditAmount = amount;
		_pendingFeatureName = featureName;
		_currentTransactionTask = new TaskCompletionSource<bool>();
		
		// Step 1: Check if user is logged in
		var currentUser = _userManager.GetCurrentUser();
		if (currentUser == null)
		{
			LogInfo("No user logged in - showing login prompt");
			ShowLoginPrompt();
			
			// Wait for login result
			await ToSignal(this, "login_prompt_result");
			
			// Check again after login attempt
			currentUser = _userManager.GetCurrentUser();
			if (currentUser == null)
			{
				LogInfo("Login cancelled or failed");
				EmitSignal(SignalName.CreditCheckCancelled, featureName);
				_currentTransactionTask.SetResult(false);
				return false;
			}
		}
		
		// Step 2: Check if user has enough credits
		if (currentUser.Credits < amount)
		{
			LogInfo($"Insufficient credits ({currentUser.Credits}/{amount}) - showing buy credits");
			ShowBuyCreditOverlay(currentUser.Credits, amount);
			
			// Wait for purchase result
			await ToSignal(this, "buy_credits_result");
			
			// Refresh user data
			currentUser = _userManager.GetCurrentUser();
			if (currentUser == null || currentUser.Credits < amount)
			{
				LogInfo("Credit purchase cancelled or insufficient");
				EmitSignal(SignalName.CreditCheckCancelled, featureName);
				_currentTransactionTask.SetResult(false);
				return false;
			}
		}
		
		// Step 3: Confirm spending
		ShowConfirmSpendOverlay(currentUser.Credits, amount, featureName);
		
		// Wait for confirmation
		await ToSignal(this, "confirm_spend_result");
		
		// Get final result from task
		bool result = await _currentTransactionTask.Task;
		
		if (result)
		{
			// Actually spend the credits
			bool spent = _userManager.SpendCredits(currentUser.UserId, amount);
			if (spent)
			{
				LogInfo($"Successfully spent {amount} credits for {featureName}");
				EmitSignal(SignalName.CreditsSpent, currentUser.UserId, amount, featureName);
				return true;
			}
		}
		
		LogInfo($"Credit spending cancelled for {featureName}");
		EmitSignal(SignalName.CreditCheckCancelled, featureName);
		return false;
	}
	
	/// <summary>
	/// Create login prompt overlay UI
	/// </summary>
	private void CreateLoginPromptOverlay()
	{
		_loginPromptOverlay = new Control();
		_loginPromptOverlay.AnchorLeft = 0;
		_loginPromptOverlay.AnchorTop = 0;
		_loginPromptOverlay.AnchorRight = 1;
		_loginPromptOverlay.AnchorBottom = 1;
		_loginPromptOverlay.Visible = false;
		_uiLayer.AddChild(_loginPromptOverlay);
		
		// Background
		var background = new ColorRect();
		background.AnchorLeft = 0;
		background.AnchorTop = 0;
		background.AnchorRight = 1;
		background.AnchorBottom = 1;
		background.Color = new Color(0, 0, 0, 0.8f);
		_loginPromptOverlay.AddChild(background);
		
		// Panel
		var panel = new Panel();
		panel.AnchorLeft = 0.5f;
		panel.AnchorTop = 0.5f;
		panel.AnchorRight = 0.5f;
		panel.AnchorBottom = 0.5f;
		panel.OffsetLeft = -200;
		panel.OffsetTop = -150;
		panel.OffsetRight = 200;
		panel.OffsetBottom = 150;
		_loginPromptOverlay.AddChild(panel);
		
		// Content
		var vbox = new VBoxContainer();
		vbox.AnchorLeft = 0.5f;
		vbox.AnchorTop = 0.5f;
		vbox.AnchorRight = 0.5f;
		vbox.AnchorBottom = 0.5f;
		vbox.OffsetLeft = -150;
		vbox.OffsetTop = -100;
		vbox.OffsetRight = 150;
		vbox.OffsetBottom = 100;
		vbox.Alignment = BoxContainer.AlignmentMode.Center;
		panel.AddChild(vbox);
		
		var titleLabel = new Label();
		titleLabel.Text = "Login Required";
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		titleLabel.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
		vbox.AddChild(titleLabel);
		
		vbox.AddChild(new HSeparator());
		
		var messageLabel = new Label();
		messageLabel.Text = $"Please log in to access\n{_pendingFeatureName}";
		messageLabel.HorizontalAlignment = HorizontalAlignment.Center;
		messageLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		vbox.AddChild(messageLabel);
		
		vbox.AddChild(new HSeparator());
		
		// Input fields
		var userIdLabel = new Label() { Text = "User ID:" };
		vbox.AddChild(userIdLabel);
		
		var userIdInput = new LineEdit();
		userIdInput.PlaceholderText = "Enter User ID";
		vbox.AddChild(userIdInput);
		
		var pinLabel = new Label() { Text = "PIN:" };
		vbox.AddChild(pinLabel);
		
		var pinInput = new LineEdit();
		pinInput.PlaceholderText = "Enter PIN";
		pinInput.Secret = true;
		vbox.AddChild(pinInput);
		
		vbox.AddChild(new HSeparator());
		
		// Buttons
		var buttonContainer = new HBoxContainer();
		buttonContainer.Alignment = BoxContainer.AlignmentMode.Center;
		vbox.AddChild(buttonContainer);
		
		var cancelButton = new Button() { Text = "Cancel" };
		cancelButton.Pressed += () => OnLoginPromptCancel();
		buttonContainer.AddChild(cancelButton);
		
		buttonContainer.AddChild(new VSeparator());
		
		var loginButton = new Button() { Text = "Login" };
		loginButton.Pressed += () => OnLoginPromptLogin(userIdInput.Text, pinInput.Text);
		buttonContainer.AddChild(loginButton);
		
		// Store references for later use
		_loginPromptOverlay.SetMeta("user_id_input", userIdInput);
		_loginPromptOverlay.SetMeta("pin_input", pinInput);
		_loginPromptOverlay.SetMeta("message_label", messageLabel);
	}
	
	/// <summary>
	/// Create buy credits overlay UI
	/// </summary>
	private void CreateBuyCreditOverlay()
	{
		_buyCreditOverlay = new Control();
		_buyCreditOverlay.AnchorLeft = 0;
		_buyCreditOverlay.AnchorTop = 0;
		_buyCreditOverlay.AnchorRight = 1;
		_buyCreditOverlay.AnchorBottom = 1;
		_buyCreditOverlay.Visible = false;
		_uiLayer.AddChild(_buyCreditOverlay);
		
		// Background
		var background = new ColorRect();
		background.AnchorLeft = 0;
		background.AnchorTop = 0;
		background.AnchorRight = 1;
		background.AnchorBottom = 1;
		background.Color = new Color(0, 0, 0, 0.8f);
		_buyCreditOverlay.AddChild(background);
		
		// Panel
		var panel = new Panel();
		panel.AnchorLeft = 0.5f;
		panel.AnchorTop = 0.5f;
		panel.AnchorRight = 0.5f;
		panel.AnchorBottom = 0.5f;
		panel.OffsetLeft = -200;
		panel.OffsetTop = -150;
		panel.OffsetRight = 200;
		panel.OffsetBottom = 150;
		_buyCreditOverlay.AddChild(panel);
		
		// Content
		var vbox = new VBoxContainer();
		vbox.AnchorLeft = 0.5f;
		vbox.AnchorTop = 0.5f;
		vbox.AnchorRight = 0.5f;
		vbox.AnchorBottom = 0.5f;
		vbox.OffsetLeft = -150;
		vbox.OffsetTop = -100;
		vbox.OffsetRight = 150;
		vbox.OffsetBottom = 100;
		vbox.Alignment = BoxContainer.AlignmentMode.Center;
		panel.AddChild(vbox);
		
		var titleLabel = new Label();
		titleLabel.Text = "Buy Credits";
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(titleLabel);
		
		vbox.AddChild(new HSeparator());
		
		var balanceLabel = new Label();
		balanceLabel.Text = "Current Balance: 0 credits";
		balanceLabel.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(balanceLabel);
		
		var needLabel = new Label();
		needLabel.Text = "Need: 0 credits";
		needLabel.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(needLabel);
		
		vbox.AddChild(new HSeparator());
		
		// Credit purchase options (debug)
		var optionsLabel = new Label();
		optionsLabel.Text = "Select amount to purchase:";
		optionsLabel.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(optionsLabel);
		
		var buttonContainer = new HBoxContainer();
		buttonContainer.Alignment = BoxContainer.AlignmentMode.Center;
		vbox.AddChild(buttonContainer);
		
		var buy1Button = new Button() { Text = "1 Credit" };
		buy1Button.Pressed += () => OnBuyCredits(1);
		buttonContainer.AddChild(buy1Button);
		
		var buy5Button = new Button() { Text = "5 Credits" };
		buy5Button.Pressed += () => OnBuyCredits(5);
		buttonContainer.AddChild(buy5Button);
		
		var buy10Button = new Button() { Text = "10 Credits" };
		buy10Button.Pressed += () => OnBuyCredits(10);
		buttonContainer.AddChild(buy10Button);
		
		vbox.AddChild(new HSeparator());
		
		var cancelButton = new Button() { Text = "Cancel" };
		cancelButton.Pressed += () => OnBuyCreditCancel();
		vbox.AddChild(cancelButton);
		
		// Store references
		_buyCreditOverlay.SetMeta("balance_label", balanceLabel);
		_buyCreditOverlay.SetMeta("need_label", needLabel);
	}
	
	/// <summary>
	/// Create confirm spend overlay UI
	/// </summary>
	private void CreateConfirmSpendOverlay()
	{
		_confirmSpendOverlay = new Control();
		_confirmSpendOverlay.AnchorLeft = 0;
		_confirmSpendOverlay.AnchorTop = 0;
		_confirmSpendOverlay.AnchorRight = 1;
		_confirmSpendOverlay.AnchorBottom = 1;
		_confirmSpendOverlay.Visible = false;
		_uiLayer.AddChild(_confirmSpendOverlay);
		
		// Background
		var background = new ColorRect();
		background.AnchorLeft = 0;
		background.AnchorTop = 0;
		background.AnchorRight = 1;
		background.AnchorBottom = 1;
		background.Color = new Color(0, 0, 0, 0.8f);
		_confirmSpendOverlay.AddChild(background);
		
		// Panel
		var panel = new Panel();
		panel.AnchorLeft = 0.5f;
		panel.AnchorTop = 0.5f;
		panel.AnchorRight = 0.5f;
		panel.AnchorBottom = 0.5f;
		panel.OffsetLeft = -200;
		panel.OffsetTop = -100;
		panel.OffsetRight = 200;
		panel.OffsetBottom = 100;
		_confirmSpendOverlay.AddChild(panel);
		
		// Content
		var vbox = new VBoxContainer();
		vbox.AnchorLeft = 0.5f;
		vbox.AnchorTop = 0.5f;
		vbox.AnchorRight = 0.5f;
		vbox.AnchorBottom = 0.5f;
		vbox.OffsetLeft = -150;
		vbox.OffsetTop = -75;
		vbox.OffsetRight = 150;
		vbox.OffsetBottom = 75;
		vbox.Alignment = BoxContainer.AlignmentMode.Center;
		panel.AddChild(vbox);
		
		var titleLabel = new Label();
		titleLabel.Text = "Confirm Purchase";
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(titleLabel);
		
		vbox.AddChild(new HSeparator());
		
		var messageLabel = new Label();
		messageLabel.Text = "Spend X credits for Y?";
		messageLabel.HorizontalAlignment = HorizontalAlignment.Center;
		messageLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		vbox.AddChild(messageLabel);
		
		var balanceLabel = new Label();
		balanceLabel.Text = "Balance after: X credits";
		balanceLabel.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(balanceLabel);
		
		vbox.AddChild(new HSeparator());
		
		var buttonContainer = new HBoxContainer();
		buttonContainer.Alignment = BoxContainer.AlignmentMode.Center;
		vbox.AddChild(buttonContainer);
		
		var cancelButton = new Button() { Text = "Cancel" };
		cancelButton.Pressed += () => OnConfirmSpendCancel();
		buttonContainer.AddChild(cancelButton);
		
		buttonContainer.AddChild(new VSeparator());
		
		var confirmButton = new Button() { Text = "Confirm" };
		confirmButton.Pressed += () => OnConfirmSpend();
		buttonContainer.AddChild(confirmButton);
		
		// Store references
		_confirmSpendOverlay.SetMeta("message_label", messageLabel);
		_confirmSpendOverlay.SetMeta("balance_label", balanceLabel);
	}
	
	// UI Show methods
	
	private void ShowLoginPrompt()
	{
		if (_loginPromptOverlay == null) return;
		
		// Update message
		var messageLabel = _loginPromptOverlay.GetMeta("message_label").AsGodotObject() as Label;
		if (messageLabel != null)
		{
			messageLabel.Text = $"Please log in to access\n{_pendingFeatureName}";
		}
		
		// Clear input fields
		var userIdInput = _loginPromptOverlay.GetMeta("user_id_input").AsGodotObject() as LineEdit;
		var pinInput = _loginPromptOverlay.GetMeta("pin_input").AsGodotObject() as LineEdit;
		if (userIdInput != null) userIdInput.Text = "";
		if (pinInput != null) pinInput.Text = "";
		
		_loginPromptOverlay.Visible = true;
		
		// Focus user ID input
		userIdInput?.GrabFocus();
	}
	
	private void ShowBuyCreditOverlay(int currentBalance, int neededAmount)
	{
		if (_buyCreditOverlay == null) return;
		
		var balanceLabel = _buyCreditOverlay.GetMeta("balance_label").AsGodotObject() as Label;
		var needLabel = _buyCreditOverlay.GetMeta("need_label").AsGodotObject() as Label;
		
		if (balanceLabel != null)
			balanceLabel.Text = $"Current Balance: {currentBalance} credits";
		
		if (needLabel != null)
			needLabel.Text = $"Need: {neededAmount} credits";
		
		_buyCreditOverlay.Visible = true;
	}
	
	private void ShowConfirmSpendOverlay(int currentBalance, int amount, string feature)
	{
		if (_confirmSpendOverlay == null) return;
		
		var messageLabel = _confirmSpendOverlay.GetMeta("message_label").AsGodotObject() as Label;
		var balanceLabel = _confirmSpendOverlay.GetMeta("balance_label").AsGodotObject() as Label;
		
		if (messageLabel != null)
			messageLabel.Text = $"Spend {amount} credit{(amount != 1 ? "s" : "")} for {feature}?";
		
		if (balanceLabel != null)
			balanceLabel.Text = $"Balance after: {currentBalance - amount} credits";
		
		_confirmSpendOverlay.Visible = true;
	}
	
	// UI Event handlers
	
	[Signal] private delegate void login_prompt_resultEventHandler();
	[Signal] private delegate void buy_credits_resultEventHandler();
	[Signal] private delegate void confirm_spend_resultEventHandler();
	
	private void OnLoginPromptCancel()
	{
		_loginPromptOverlay.Visible = false;
		EmitSignal("login_prompt_result");
	}
	
	private void OnLoginPromptLogin(string userId, string pin)
	{
		if (string.IsNullOrEmpty(userId))
		{
			LogWarning("User ID is required");
			return;
		}
		
		bool loginSuccess = false;
		
		// Try development login if no PIN provided in dev mode
		if (GameHost.IsDevelopmentContext() && string.IsNullOrEmpty(pin))
		{
			loginSuccess = _userManager.LoginUserDevelopment(userId);
		}
		else
		{
			loginSuccess = _userManager.LoginUser(userId, pin);
		}
		
		if (loginSuccess)
		{
			_loginPromptOverlay.Visible = false;
			EmitSignal("login_prompt_result");
		}
		else
		{
			LogWarning("Login failed - invalid credentials");
			// Could show error message in UI
		}
	}
	
	private void OnBuyCreditCancel()
	{
		_buyCreditOverlay.Visible = false;
		EmitSignal("buy_credits_result");
	}
	
	private void OnBuyCredits(int amount)
	{
		var currentUser = _userManager.GetCurrentUser();
		if (currentUser != null)
		{
			// Debug implementation - just add credits
			_userManager.AddCredits(currentUser.UserId, amount);
			LogInfo($"Added {amount} debug credits to user {currentUser.UserId}");
			EmitSignal(SignalName.CreditsPurchased, currentUser.UserId, amount);
			
			_buyCreditOverlay.Visible = false;
			EmitSignal("buy_credits_result");
		}
	}
	
	private void OnConfirmSpendCancel()
	{
		_confirmSpendOverlay.Visible = false;
		_currentTransactionTask?.SetResult(false);
		EmitSignal("confirm_spend_result");
	}
	
	private void OnConfirmSpend()
	{
		_confirmSpendOverlay.Visible = false;
		_currentTransactionTask?.SetResult(true);
		EmitSignal("confirm_spend_result");
	}
	
	protected override void OnServiceDestroyed()
	{
		// Clean up UI
		_uiLayer?.QueueFree();
	}
	
	/// <summary>
	/// Get the singleton instance (convenience method)
	/// </summary>
	public static CreditManager GetInstance()
	{
		return GetAutoload<CreditManager>();
	}
}