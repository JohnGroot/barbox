using Godot;

/// <summary>
/// UI management service for global UI components
/// Manages top menu bar, overlays, and global UI state
/// Now an autoload service for persistence across all scenes
/// </summary>
public partial class UIManager : AutoloadBase
{
	[Signal] public delegate void LoginRequestedEventHandler();
	[Signal] public delegate void LogoutRequestedEventHandler();
	[Signal] public delegate void ReturnToMenuRequestedEventHandler();

	public static UIManager Instance { get; private set; }

	public static UIManager GetInstance()
	{
		return GetAutoload<UIManager>();
	}

	private CanvasLayer _topMenuLayer;
	private CanvasLayer _modalLayer;
	private CanvasLayer _helpLayer;
	private TopMenuBar _topMenuBar;
	private LoginModal _loginModal;
	private BuyCreditsModal _buyCreditsModal;
	private SessionManager _sessionManager;

	// Help system components
	private Control _helpOverlay;
	private Button _helpToggleButton;
	private ScrollContainer _helpContentScroll;
	private VBoxContainer _helpContentContainer;
	private Label _helpTitleLabel;
	private HelpContentData _currentHelpContent;
	private bool _isHelpVisible = false;
	
	// Context tracking
	private string _currentGameTitle = "BarBox Arcade";
	private ContextButtonData[] _currentContextButtons = null;

	protected override void OnServiceReady()
	{
		Instance = this;
		// Minimal setup only - actual initialization happens in OnServiceInitialize
	}

	protected override void OnServiceInitialize()
	{
		// Get service references - now guaranteed to be initialized
		_sessionManager = SessionManager.GetInstance();
		
		// Create top menu layer
		_topMenuLayer = new CanvasLayer();
		_topMenuLayer.Layer = 100; // High layer to appear above everything
		AddChild(_topMenuLayer);
		
		// Create help layer (between games and top menu)
		_helpLayer = new CanvasLayer();
		_helpLayer.Layer = 105; // Above games but below top menu
		AddChild(_helpLayer);

		// Create modal layer (higher than top menu)
		_modalLayer = new CanvasLayer();
		_modalLayer.Layer = 110; // Higher layer to appear above top menu
		AddChild(_modalLayer);

		// Create top menu bar (now includes context buttons)
		_topMenuBar = new TopMenuBar();
		_topMenuLayer.AddChild(_topMenuBar);

		// Set up help system
		SetupHelpSystem();
		
		// Create login modal
		_loginModal = new LoginModal();
		_modalLayer.AddChild(_loginModal);

		// Create buy credits modal
		_buyCreditsModal = new BuyCreditsModal();
		_modalLayer.AddChild(_buyCreditsModal);
		
		// Connect signals
		_topMenuBar.LoginRequested += OnLoginRequested;
		_topMenuBar.LogoutRequested += OnLogoutRequested;
		_topMenuBar.BuyCreditsRequested += OnBuyCreditsRequested;
		_topMenuBar.ReturnToMenuRequested += OnReturnToMenuRequested;

		// Connect login modal signals
		_loginModal.ModalClosed += OnLoginModalClosed;

		// Connect buy credits modal signals
		_buyCreditsModal.ModalClosed += OnBuyCreditsModalClosed;
		_buyCreditsModal.CreditsAcquired += OnCreditsAcquired;
		
		// Connect to session manager signals
		if (_sessionManager != null)
		{
			_sessionManager.UserLoggedIn += OnUserLoggedIn;
			_sessionManager.UserLoggedOut += OnUserLoggedOut;
			_sessionManager.CreditsEarned += OnCreditsEarned;
		}

		// Initialize with current user state
		UpdateUserDisplay();
		
		// Apply any context that was set before initialization
		RefreshTopMenuContext();
		
		LogInfo("UIManager initialized with top menu bar and login modal");
	}

	protected override void OnServiceDestroyed()
	{
		DisconnectSignals();
		CleanupHelpSystem();
		Instance = null;
	}

	private void DisconnectSignals()
	{
		if (GodotObject.IsInstanceValid(_topMenuBar))
		{
			_topMenuBar.LoginRequested -= OnLoginRequested;
			_topMenuBar.LogoutRequested -= OnLogoutRequested;
			_topMenuBar.BuyCreditsRequested -= OnBuyCreditsRequested;
			_topMenuBar.ReturnToMenuRequested -= OnReturnToMenuRequested;
		}
		
		if (GodotObject.IsInstanceValid(_loginModal))
		{
			_loginModal.ModalClosed -= OnLoginModalClosed;
		}

		if (GodotObject.IsInstanceValid(_buyCreditsModal))
		{
			_buyCreditsModal.ModalClosed -= OnBuyCreditsModalClosed;
			_buyCreditsModal.CreditsAcquired -= OnCreditsAcquired;
		}

		if (GodotObject.IsInstanceValid(_sessionManager))
		{
			_sessionManager.UserLoggedIn -= OnUserLoggedIn;
			_sessionManager.UserLoggedOut -= OnUserLoggedOut;
			_sessionManager.CreditsEarned -= OnCreditsEarned;
		}
	}

	/// <summary>
	/// Set the game context in the top menu bar using simplified ContextButtonData structures
	/// </summary>
	public void SetGameContext(string gameTitle, ContextButtonData[] contextButtons = null)
	{
		_currentGameTitle = gameTitle ?? "BarBox Arcade";
		_currentContextButtons = contextButtons;
		
		// If not yet initialized, the context will be applied when OnServiceInitialize runs
		if (_topMenuBar != null)
		{
			RefreshTopMenuContext();
		}
		else
		{
			// Game context queued (UI not ready)
		}
		
		// Game context set
	}


	/// <summary>
	/// Clear game context and return to main menu state
	/// </summary>
	public void ClearGameContext()
	{
		_currentGameTitle = "BarBox Arcade";
		_currentContextButtons = null;
		
		if (_topMenuBar != null)
		{
			_topMenuBar.SetGameTitle(_currentGameTitle);
			_topMenuBar.ClearContextButtons();
		}
		
		// Game context cleared, returned to main menu state
	}

	/// <summary>
	/// Refresh the top menu context with current tracked buttons
	/// Useful when button states need to be updated
	/// </summary>
	public void RefreshTopMenuContext()
	{
		if (_topMenuBar != null)
		{
			_topMenuBar.SetGameTitle(_currentGameTitle);
			
			if (_currentContextButtons != null)
			{
				_topMenuBar.SetContextButtons(_currentContextButtons);
			}
			else
			{
				_topMenuBar.ClearContextButtons();
			}
		}
	}

	/// <summary>
	/// Update the state of context buttons without recreating them
	/// Call this when game state changes affect button availability
	/// </summary>
	public void UpdateContextButtonStates()
	{
		if (_currentContextButtons != null)
		{
			// Trigger UI update for games implementing IGameUIIntegration
			RefreshTopMenuContext();
			// Context button states updated
		}
	}

	/// <summary>
	/// Update user display based on current login state
	/// </summary>
	public void UpdateUserDisplay()
	{
		UserData currentUser = null;
		
		// Get current user from SessionManager
		var session = _sessionManager?.GetCurrentUserSession();
		if (session != null)
		{
			currentUser = new UserData(session.UserId);
			if (session.GlobalData != null)
			{
				currentUser.Credits = session.GlobalData.GlobalCredits;
			}
		}
		
		if (_topMenuBar != null)
		{
			_topMenuBar.UpdateUserInfo(currentUser);
		}
	}

	/// <summary>
	/// Show or hide the top menu bar
	/// </summary>
	public void SetTopMenuVisible(bool visible)
	{
		if (_topMenuLayer != null)
		{
			_topMenuLayer.Visible = visible;
		}
	}

	/// <summary>
	/// Show the login modal
	/// </summary>
	public void ShowLoginModal()
	{
		if (_loginModal != null)
		{
			_loginModal.ShowModal();
		}
	}

	/// <summary>
	/// Hide the login modal
	/// </summary>
	public void HideLoginModal()
	{
		if (_loginModal != null)
		{
			_loginModal.HideModal();
		}
	}

	/// <summary>
	/// Show the buy credits modal
	/// </summary>
	public void ShowBuyCreditsModal()
	{
		if (_buyCreditsModal != null)
		{
			_buyCreditsModal.ShowModal();
		}
	}

	/// <summary>
	/// Hide the buy credits modal
	/// </summary>
	public void HideBuyCreditsModal()
	{
		if (_buyCreditsModal != null)
		{
			_buyCreditsModal.HideModal();
		}
	}

	/// <summary>
	/// Set game status text to display in the top menu bar center section
	/// Status is shown when no context buttons are present
	/// </summary>
	public void SetGameStatus(string statusText)
	{
		if (_topMenuBar != null)
		{
			_topMenuBar.SetGameStatus(statusText);
		}
	}

	// Signal handlers

	private void OnLoginRequested()
	{
		// Show login modal directly instead of emitting signal
		ShowLoginModal();
	}

	private void OnBuyCreditsRequested()
	{
		// Show buy credits modal directly
		ShowBuyCreditsModal();
	}

	private void OnLogoutRequested()
	{
		EmitSignal(SignalName.LogoutRequested);
	}

	private void OnReturnToMenuRequested()
	{
		EmitSignal(SignalName.ReturnToMenuRequested);
	}

	private void OnUserLoggedIn(string userId)
	{
		UpdateUserDisplay();
	}

	private void OnUserLoggedOut(string userId)
	{
		UpdateUserDisplay();
	}

	private void OnLoginModalClosed()
	{
		// Modal was closed, no additional action needed
		// Login modal closed
	}

	private void OnBuyCreditsModalClosed()
	{
		// Modal was closed, no additional action needed
		// Buy credits modal closed
	}

	private void OnCreditsAcquired(string userId, int amount)
	{
		// Credits were successfully purchased, update user display
		UpdateUserDisplay();
	}

	private void OnCreditsEarned(string userId, int amount, string reason)
	{
		// Credits were earned (from any source), update user display
		UpdateUserDisplay();
	}

	// ============================================================================
	// Help System Management
	// ============================================================================

	/// <summary>
	/// Sets up the help system UI components on the help layer
	/// </summary>
	private void SetupHelpSystem()
	{
		CreateHelpToggleButton();
		CreateHelpOverlay();
	}

	/// <summary>
	/// Creates the help toggle button in the bottom-left corner
	/// </summary>
	private void CreateHelpToggleButton()
	{
		_helpToggleButton = new Button();
		_helpToggleButton.Text = "❓";
		_helpToggleButton.Size = new Vector2(60, 60);
		_helpToggleButton.CustomMinimumSize = new Vector2(60, 60);
		_helpToggleButton.ExpandIcon = false; // Prevent icon expansion to maintain square aspect

		// Position in bottom-left corner with margin, moved up from edge
		_helpToggleButton.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
		_helpToggleButton.Position = new Vector2(-60, 20); // Adjusted for 60px button width

		// Style the button as a circle
		_helpToggleButton.AddThemeFontSizeOverride("font_size", 24); // Reduced font size for smaller button

		// Create circular style
		var circularStyle = new StyleBoxFlat();
		circularStyle.BgColor = new Color(0.2f, 0.4f, 0.8f, 0.6f); // Blue background with lighter alpha
		circularStyle.BorderWidthTop = 2;
		circularStyle.BorderWidthBottom = 2;
		circularStyle.BorderWidthLeft = 2;
		circularStyle.BorderWidthRight = 2;
		circularStyle.BorderColor = new Color(0.6f, 0.8f, 1.0f, 1.0f); // Light blue border
		circularStyle.CornerRadiusTopLeft = 30; // Half of button size (60/2)
		circularStyle.CornerRadiusTopRight = 30;
		circularStyle.CornerRadiusBottomLeft = 30;
		circularStyle.CornerRadiusBottomRight = 30;
		_helpToggleButton.AddThemeStyleboxOverride("normal", circularStyle);

		// Hover state
		var hoverStyle = new StyleBoxFlat();
		hoverStyle.BgColor = new Color(0.3f, 0.5f, 0.9f, 0.95f); // Brighter blue on hover
		hoverStyle.BorderWidthTop = 2;
		hoverStyle.BorderWidthBottom = 2;
		hoverStyle.BorderWidthLeft = 2;
		hoverStyle.BorderWidthRight = 2;
		hoverStyle.BorderColor = new Color(0.8f, 0.9f, 1.0f, 1.0f);
		hoverStyle.CornerRadiusTopLeft = 30;
		hoverStyle.CornerRadiusTopRight = 30;
		hoverStyle.CornerRadiusBottomLeft = 30;
		hoverStyle.CornerRadiusBottomRight = 30;
		_helpToggleButton.AddThemeStyleboxOverride("hover", hoverStyle);

		// Pressed state
		var pressedStyle = new StyleBoxFlat();
		pressedStyle.BgColor = new Color(0.1f, 0.3f, 0.7f, 1.0f); // Darker blue when pressed
		pressedStyle.BorderWidthTop = 2;
		pressedStyle.BorderWidthBottom = 2;
		pressedStyle.BorderWidthLeft = 2;
		pressedStyle.BorderWidthRight = 2;
		pressedStyle.BorderColor = new Color(0.4f, 0.6f, 0.8f, 1.0f);
		pressedStyle.CornerRadiusTopLeft = 30;
		pressedStyle.CornerRadiusTopRight = 30;
		pressedStyle.CornerRadiusBottomLeft = 30;
		pressedStyle.CornerRadiusBottomRight = 30;
		_helpToggleButton.AddThemeStyleboxOverride("pressed", pressedStyle);

		// Connect the pressed signal
		_helpToggleButton.Pressed += ToggleHelpMenu;

		// Initially hidden until game provides content
		_helpToggleButton.Visible = false;

		// Add to help layer
		_helpLayer.AddChild(_helpToggleButton);
	}

	/// <summary>
	/// Creates the help overlay panel that displays help content
	/// </summary>
	private void CreateHelpOverlay()
	{
		// Main overlay background - covers entire screen
		_helpOverlay = new Control();
		_helpOverlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_helpOverlay.Visible = false;
		_helpOverlay.MouseFilter = Control.MouseFilterEnum.Stop; // Block input to game

		// Semi-transparent background - clicking it closes the help menu
		var background = new ColorRect();
		background.Color = new Color(0, 0, 0, 0.7f);
		background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		background.GuiInput += (InputEvent @event) => {
			if (@event is InputEventMouseButton mouseButton && mouseButton.Pressed && mouseButton.ButtonIndex == MouseButton.Left)
			{
				SetHelpMenuVisible(false);
			}
		};
		_helpOverlay.AddChild(background);

		// Content panel - centered with smaller margins for more text space
		var contentPanel = new PanelContainer();
		contentPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		contentPanel.OffsetLeft = 50;
		contentPanel.OffsetRight = -50;
		contentPanel.OffsetTop = 80;
		contentPanel.OffsetBottom = -80;

		// Style the content panel
		var panelStyle = new StyleBoxFlat();
		panelStyle.BgColor = new Color(0.1f, 0.1f, 0.15f, 0.95f);
		panelStyle.CornerRadiusTopLeft = 10;
		panelStyle.CornerRadiusTopRight = 10;
		panelStyle.CornerRadiusBottomLeft = 10;
		panelStyle.CornerRadiusBottomRight = 10;
		panelStyle.BorderWidthTop = 2;
		panelStyle.BorderWidthBottom = 2;
		panelStyle.BorderWidthLeft = 2;
		panelStyle.BorderWidthRight = 2;
		panelStyle.BorderColor = new Color(0.4f, 0.2f, 0.8f);
		contentPanel.AddThemeStyleboxOverride("panel", panelStyle);

		_helpOverlay.AddChild(contentPanel);

		// Header with title and close button
		var headerContainer = new HBoxContainer();
		headerContainer.AddThemeConstantOverride("separation", 10);

		_helpTitleLabel = new Label();
		_helpTitleLabel.Text = "Game Help";
		_helpTitleLabel.AddThemeFontSizeOverride("font_size", 24);
		_helpTitleLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.95f, 0.8f));
		_helpTitleLabel.HorizontalAlignment = HorizontalAlignment.Center; // Center align the title text
		_helpTitleLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		headerContainer.AddChild(_helpTitleLabel);

		var closeButton = new Button();
		closeButton.Text = "✕";
		closeButton.AddThemeFontSizeOverride("font_size", 20);
		closeButton.Size = new Vector2(40, 40);
		closeButton.Pressed += () => SetHelpMenuVisible(false);
		headerContainer.AddChild(closeButton);

		// Scroll container for content
		_helpContentScroll = new ScrollContainer();
		_helpContentScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

		_helpContentContainer = new VBoxContainer();
		_helpContentContainer.AddThemeConstantOverride("separation", 15);
		_helpContentContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_helpContentContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		_helpContentScroll.AddChild(_helpContentContainer);

		// Main content container
		var mainContentContainer = new VBoxContainer();
		mainContentContainer.AddThemeConstantOverride("separation", 20);
		mainContentContainer.AddChild(headerContainer);
		mainContentContainer.AddChild(_helpContentScroll);

		// Add margins to main content - smaller side margins for more text space
		var marginContainer = new MarginContainer();
		marginContainer.AddThemeConstantOverride("margin_top", 20);
		marginContainer.AddThemeConstantOverride("margin_bottom", 20);
		marginContainer.AddThemeConstantOverride("margin_left", 30);
		marginContainer.AddThemeConstantOverride("margin_right", 30);
		marginContainer.AddChild(mainContentContainer);

		contentPanel.AddChild(marginContainer);

		// Add to help layer
		_helpLayer.AddChild(_helpOverlay);
	}

	/// <summary>
	/// Sets the help content for the current game
	/// Called by GameHost when a game is loaded
	/// </summary>
	public void SetGameHelpContent(HelpContentData helpContent)
	{
		_currentHelpContent = helpContent;

		if (_helpTitleLabel != null && _helpContentContainer != null)
		{
			PopulateHelpContent();
		}
	}

	/// <summary>
	/// Shows or hides the help button
	/// Called by GameHost when game starts/ends
	/// </summary>
	public void ShowHelpButton(bool show)
	{
		if (_helpToggleButton != null)
		{
			_helpToggleButton.Visible = show;
		}
	}

	/// <summary>
	/// Populates the help content from the stored help data
	/// </summary>
	private void PopulateHelpContent()
	{
		if (_helpTitleLabel == null || _helpContentContainer == null || _currentHelpContent.Sections == null)
		{
			return;
		}

		// Update title
		_helpTitleLabel.Text = _currentHelpContent.Title;

		// Clear existing content
		foreach (Node child in _helpContentContainer.GetChildren())
		{
			child.QueueFree();
		}

		// Add each section
		foreach (var section in _currentHelpContent.Sections)
		{
			AddHelpSection(section);
		}
	}

	/// <summary>
	/// Adds a help section to the content container
	/// </summary>
	private void AddHelpSection(HelpSection section)
	{
		// Section header
		var headerLabel = new Label();
		headerLabel.Text = section.Header;
		headerLabel.AddThemeFontSizeOverride("font_size", 18);
		headerLabel.AddThemeColorOverride("font_color", new Color(0.4f, 0.8f, 1.0f));
		headerLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		headerLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		headerLabel.CustomMinimumSize = new Vector2(400, 0); // Ensure minimum width
		_helpContentContainer.AddChild(headerLabel);

		// Section content
		foreach (var content in section.Content)
		{
			var contentLabel = new Label();
			contentLabel.Text = section.IsList ? $"• {content}" : content;
			contentLabel.AddThemeFontSizeOverride("font_size", 14);
			contentLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
			contentLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			contentLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			contentLabel.CustomMinimumSize = new Vector2(400, 0); // Ensure minimum width
			contentLabel.AddThemeConstantOverride("line_spacing", 2);

			// Add left margin for list items
			if (section.IsList)
			{
				var marginContainer = new MarginContainer();
				marginContainer.AddThemeConstantOverride("margin_left", 20);
				marginContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
				marginContainer.AddChild(contentLabel);
				_helpContentContainer.AddChild(marginContainer);
			}
			else
			{
				_helpContentContainer.AddChild(contentLabel);
			}
		}
	}

	/// <summary>
	/// Toggles the help menu visibility
	/// </summary>
	private void ToggleHelpMenu()
	{
		SetHelpMenuVisible(!_isHelpVisible);
	}

	/// <summary>
	/// Sets the help menu visibility state
	/// </summary>
	private void SetHelpMenuVisible(bool visible)
	{
		_isHelpVisible = visible;
		if (_helpOverlay != null)
		{
			_helpOverlay.Visible = visible;
		}
	}

	/// <summary>
	/// Cleans up help system components
	/// </summary>
	private void CleanupHelpSystem()
	{
		if (GodotObject.IsInstanceValid(_helpToggleButton))
		{
			_helpToggleButton.Pressed -= ToggleHelpMenu;
		}

		// CanvasLayer cleanup will handle child nodes
		_helpToggleButton = null;
		_helpOverlay = null;
		_helpContentScroll = null;
		_helpContentContainer = null;
		_helpTitleLabel = null;
		_isHelpVisible = false;
		_currentHelpContent = default;
	}
}