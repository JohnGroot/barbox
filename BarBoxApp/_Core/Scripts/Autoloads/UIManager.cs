using System.Threading.Tasks;
using Godot;

namespace BarBox.Core.Autoloads;

/// <summary>
/// UI management service for global UI components
/// Manages top menu bar, overlays, and global UI state
/// </summary>
public partial class UIManager : AutoloadBase
{
	[Signal]
	public delegate void LoginRequestedEventHandler();

	[Signal]
	public delegate void LogoutRequestedEventHandler();

	[Signal]
	public delegate void ReturnToMenuRequestedEventHandler();

	public static UIManager Instance { get; private set; }

	public static UIManager GetInstance()
	{
		return GetAutoload<UIManager>();
	}

	/// <summary>
	/// UI Layer Hierarchy
	/// Defines the z-ordering of UI elements to prevent layering conflicts
	/// Lower numbers render first (behind), higher numbers render last (in front)
	/// </summary>
	private const int LAYER_GAME_UI = 100;        // Game HUDs, TopMenuBar, score displays
	private const int LAYER_HELP = 105;           // Help overlays and information panels
	private const int LAYER_GAME_MODAL = 108;     // Game-specific setup/configuration modals
	private const int LAYER_SYSTEM_MODAL = 110;   // System action modals (login, buy credits, confirmations)
	private const int LAYER_CRITICAL = 115;       // Critical alerts and error dialogs (future use)

	private CanvasLayer _topMenuLayer;
	private CanvasLayer _modalLayer;
	private CanvasLayer _helpLayer;
	private TopMenuBar _topMenuBar;
	private LoginModal _loginModal;
	private BuyCreditsModal _buyCreditsModal;
	private ConfirmationDialog _confirmationDialog;
	private SessionManager _sessionManager;
	private CreditService _creditService;

	// Help system components
	private Control _helpOverlay;
	private Button _helpToggleButton;
	private ScrollContainer _helpContentScroll;
	private VBoxContainer _helpContentContainer;
	private Label _helpTitleLabel;
	private HelpContentData _currentHelpContent;
	private bool _isHelpVisible = false;

	// Context tracking
	private string _currentGameTitle = "BarBox";
	private ContextButtonData[] _currentContextButtons = null;

	// UI ready state and queuing
	private bool _isUIReady = false;
	private string _queuedGameTitle = null;
	private ContextButtonData[] _queuedContextButtons = null;

	protected override void OnServiceEnterTree()
	{
		Instance = this;

		// Get service references - all autoloads guaranteed to exist
		_sessionManager = SessionManager.GetInstance();
		_creditService = CreditService.GetInstance();

		_topMenuLayer = new() { Layer = LAYER_GAME_UI };
		AddChild(_topMenuLayer);

		_helpLayer = new() { Layer = LAYER_HELP };
		AddChild(_helpLayer);

		_modalLayer = new() { Layer = LAYER_SYSTEM_MODAL };
		AddChild(_modalLayer);

		_topMenuBar = new TopMenuBar();
		_topMenuLayer.AddChild(_topMenuBar);

		SetupHelpSystem();

		_loginModal = new LoginModal();
		_modalLayer.AddChild(_loginModal);

		_buyCreditsModal = new BuyCreditsModal();
		_modalLayer.AddChild(_buyCreditsModal);

		_confirmationDialog = new ConfirmationDialog();
		_modalLayer.AddChild(_confirmationDialog);
	}

	protected override void OnServiceReady()
	{
		_topMenuBar.LoginRequested += OnLoginRequested;
		_topMenuBar.LogoutRequested += OnLogoutRequested;
		_topMenuBar.BuyCreditsRequested += OnBuyCreditsRequested;
		_topMenuBar.ReturnToMenuRequested += OnReturnToMenuRequested;

		_loginModal.ModalClosed += OnLoginModalClosed;

		_buyCreditsModal.ModalClosed += OnBuyCreditsModalClosed;
		_buyCreditsModal.CreditsAcquired += OnCreditsAcquired;

		if (_sessionManager != null)
		{
			_sessionManager.UserLoggedIn += OnUserLoggedIn;
			_sessionManager.UserLoggedOut += OnUserLoggedOut;
		}

		if (_creditService != null)
		{
			_creditService.CreditsChanged += OnCreditsChanged;
		}

		var gameHost = GameHost.GetInstance();
		if (gameHost != null)
		{
			gameHost.GameStarted += OnGameStarted;
			gameHost.GameEnded += OnGameEnded;
		}

		UpdateUserDisplay();
		RefreshTopMenuContext();

		_isUIReady = true;

		if (_queuedGameTitle != null)
		{
			SetGameContext(_queuedGameTitle, _queuedContextButtons);
			_queuedGameTitle = null;
			_queuedContextButtons = null;
		}

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
		if (IsInstanceValid(_topMenuBar))
		{
			_topMenuBar.LoginRequested -= OnLoginRequested;
			_topMenuBar.LogoutRequested -= OnLogoutRequested;
			_topMenuBar.BuyCreditsRequested -= OnBuyCreditsRequested;
			_topMenuBar.ReturnToMenuRequested -= OnReturnToMenuRequested;
		}

		if (IsInstanceValid(_loginModal))
		{
			_loginModal.ModalClosed -= OnLoginModalClosed;
		}

		if (IsInstanceValid(_buyCreditsModal))
		{
			_buyCreditsModal.ModalClosed -= OnBuyCreditsModalClosed;
			_buyCreditsModal.CreditsAcquired -= OnCreditsAcquired;
		}

		if (IsInstanceValid(_sessionManager))
		{
			_sessionManager.UserLoggedIn -= OnUserLoggedIn;
			_sessionManager.UserLoggedOut -= OnUserLoggedOut;
		}

		if (IsInstanceValid(_creditService))
		{
			_creditService.CreditsChanged -= OnCreditsChanged;
		}

		var gameHost = GameHost.GetInstance();
		if (IsInstanceValid(gameHost))
		{
			gameHost.GameStarted -= OnGameStarted;
			gameHost.GameEnded -= OnGameEnded;
		}
	}

	public void SetGameContext(string gameTitle, ContextButtonData[] contextButtons = null)
	{
		// If UI not ready yet, queue the context to apply later
		if (!_isUIReady)
		{
			_queuedGameTitle = gameTitle ?? "BarBox";
			_queuedContextButtons = contextButtons;
			LogInfo($"UI not ready - queued game context: '{_queuedGameTitle}' with {contextButtons?.Length ?? 0} buttons");
			return;
		}

		// UI is ready, apply context immediately
		_currentGameTitle = gameTitle ?? "BarBox";
		_currentContextButtons = contextButtons;

		if (_topMenuBar != null)
		{
			RefreshTopMenuContext();
		}

		LogInfo($"Game context set: '{_currentGameTitle}' with {contextButtons?.Length ?? 0} buttons");
	}

	/// <summary>
	/// Clear game context and return to main menu state
	/// </summary>
	public void ClearGameContext()
	{
		_currentGameTitle = "BarBox";
		_currentContextButtons = null;

		if (_topMenuBar != null)
		{
			_topMenuBar.SetGameTitle(_currentGameTitle);
			_topMenuBar.ClearContextButtons();
		}
	}

	/// <summary>
	/// Refresh the top menu context with current tracked buttons
	/// Useful when button states need to be updated
	/// </summary>
	public void RefreshTopMenuContext()
	{
		if (_topMenuBar == null)
		{
			return;
		}

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

	/// <summary>
	/// Update the state of context buttons without recreating them
	/// Call this when game state changes affect button availability
	/// </summary>
	public void UpdateContextButtonStates()
	{
		if (_currentContextButtons != null)
		{
			// Trigger UI update for current game
			RefreshTopMenuContext();
		}
	}

	/// <summary>
	/// Update user display based on primary user login state
	/// </summary>
	public void UpdateUserDisplay()
	{
		// Get primary user session for UI display (TopMenuBar shows primary user)
		var session = _sessionManager?.GetPrimarySession();

		if (_topMenuBar != null)
		{
			// Fetch credits from CreditService (single source of truth)
			int credits = 0;
			if (session != null && _creditService != null)
			{
				// Use cached balance - GetBalanceAsync will emit CreditsChanged if cache is stale
				var balanceResult = _creditService.GetBalanceAsync(session.PlayerId, forceRefresh: false);
				if (balanceResult.IsCompleted && balanceResult.Result.IsSuccess(out var balance))
				{
					credits = balance;
				}
			}

			_topMenuBar.UpdateUserInfo(session, credits);
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
		_loginModal?.ShowModal();
	}

	/// <summary>
	/// Hide the login modal
	/// </summary>
	public void HideLoginModal()
	{
		_loginModal?.HideModal();
	}

	/// <summary>
	/// Show the buy credits modal for a specific user
	/// </summary>
	/// <param name="phoneNumber">Phone number of the user purchasing credits</param>
	public void ShowBuyCreditsModal(string phoneNumber)
	{
		_buyCreditsModal?.ShowModal(phoneNumber);
	}

	/// <summary>
	/// Hide the buy credits modal
	/// </summary>
	public void HideBuyCreditsModal()
	{
		_buyCreditsModal?.HideModal();
	}

	/// <summary>
	/// Show confirmation dialog with customizable content
	/// Returns Task<bool> - true for confirmed, false for cancelled
	/// </summary>
	public async Task<bool> ShowConfirmationAsync(
		string title = "Confirm Action",
		string message = "Are you sure?",
		string confirmText = "Confirm",
		string cancelText = "Cancel")
	{
		if (_confirmationDialog != null)
		{
			return await _confirmationDialog.ShowAsync(title, message, confirmText, cancelText);
		}

		// Fallback: assume confirmation if dialog unavailable
		return true;
	}

	/// <summary>
	/// Set game status text to display in the top menu bar center section
	/// Status is shown when no context buttons are present
	/// </summary>
	public void SetGameStatus(string statusText)
	{
		_topMenuBar?.SetGameStatus(statusText);
	}

	// Signal handlers
	private void OnLoginRequested()
	{
		// Show login modal directly instead of emitting signal
		ShowLoginModal();
	}

	private void OnBuyCreditsRequested()
	{
		// Show buy credits modal for primary user (UI convenience - TopMenuBar button)
		var session = _sessionManager?.GetPrimarySession();
		if (session != null)
		{
			ShowBuyCreditsModal(session.PhoneNumber);
		}
	}

	private void OnLogoutRequested()
	{
		EmitSignal(SignalName.LogoutRequested);
	}

	private void OnReturnToMenuRequested()
	{
		EmitSignal(SignalName.ReturnToMenuRequested);
	}

	private async void OnUserLoggedIn(string userId)
	{
		UpdateUserDisplay(); // Shows cached/0 initially

		// Trigger async credit fetch - CreditsChanged signal will update UI with correct value
		try
		{
			var session = _sessionManager?.GetPrimarySession();
			if (session != null && _creditService != null)
			{
				await _creditService.GetBalanceAsync(session.PlayerId, forceRefresh: true);

				// CreditsChanged signal emitted by CreditService → OnCreditsChanged updates UI
			}
		}
		catch (System.Exception ex)
		{
			LogError($"Failed to fetch credits after login: {ex.Message}");
		}
	}

	private void OnUserLoggedOut(string userId)
	{
		UpdateUserDisplay();
	}

	private void OnLoginModalClosed()
	{
	}

	private void OnBuyCreditsModalClosed()
	{
	}

	private void OnCreditsAcquired(string userId, int amount)
	{
		// Credits were successfully purchased, update user display
		UpdateUserDisplay();
	}

	private void OnCreditsChanged(string playerId, int newBalance)
	{
		// Check if this is the primary user's credits
		var session = _sessionManager?.GetPrimarySession();
		if (session != null && session.PlayerId.ToString() == playerId && _topMenuBar != null)
		{
			// Update UI directly with the balance we received (avoid re-fetching)
			_topMenuBar.UpdateUserInfo(session, newBalance);
		}
	}

	private void OnGameStarted(string gameId)
	{
		_topMenuBar.SetLogoutButtonEnabled(GameHost.GetInstance().GetCurrentGame().CanLogout);
	}

	private void OnGameEnded(string gameId)
	{
		// Re-enable logout button when game ends
		_topMenuBar?.SetLogoutButtonEnabled(true);
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
		_helpToggleButton.ExpandIcon = false;

		_helpToggleButton.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
		_helpToggleButton.Position = new Vector2(-60, 20);
		_helpToggleButton.AddThemeFontSizeOverride("font_size", 24);

		_helpToggleButton.AddThemeStyleboxOverride(
			"normal",
			CreateCircularButtonStyle(new Color(0.2f, 0.4f, 0.8f, 0.6f), new Color(0.6f, 0.8f, 1.0f, 1.0f), 30));
		_helpToggleButton.AddThemeStyleboxOverride(
			"hover",
			CreateCircularButtonStyle(new Color(0.3f, 0.5f, 0.9f, 0.95f), new Color(0.8f, 0.9f, 1.0f, 1.0f), 30));
		_helpToggleButton.AddThemeStyleboxOverride(
			"pressed",
			CreateCircularButtonStyle(new Color(0.1f, 0.3f, 0.7f, 1.0f), new Color(0.4f, 0.6f, 0.8f, 1.0f), 30));

		_helpToggleButton.Pressed += ToggleHelpMenu;
		_helpToggleButton.Visible = false;
		_helpLayer.AddChild(_helpToggleButton);
	}

	private static StyleBoxFlat CreateCircularButtonStyle(Color bgColor, Color borderColor, int cornerRadius)
	{
		return new StyleBoxFlat
		{
			BgColor = bgColor,
			BorderWidthTop = 2,
			BorderWidthBottom = 2,
			BorderWidthLeft = 2,
			BorderWidthRight = 2,
			BorderColor = borderColor,
			CornerRadiusTopLeft = cornerRadius,
			CornerRadiusTopRight = cornerRadius,
			CornerRadiusBottomLeft = cornerRadius,
			CornerRadiusBottomRight = cornerRadius,
		};
	}

	private void CreateHelpOverlay()
	{
		_helpOverlay = new Control();
		_helpOverlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_helpOverlay.Visible = false;
		_helpOverlay.MouseFilter = Control.MouseFilterEnum.Stop;

		// Clicking background closes help menu
		var background = new ColorRect();
		background.Color = new Color(0, 0, 0, 0.7f);
		background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		background.GuiInput += (InputEvent @event) =>
		{
			if (@event is InputEventMouseButton mouseButton && mouseButton.Pressed && mouseButton.ButtonIndex == MouseButton.Left)
			{
				SetHelpMenuVisible(false);
			}
		};
		_helpOverlay.AddChild(background);

		var contentPanel = new PanelContainer();
		contentPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		contentPanel.OffsetLeft = 50;
		contentPanel.OffsetRight = -50;
		contentPanel.OffsetTop = 80;
		contentPanel.OffsetBottom = -80;

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
		if (IsInstanceValid(_helpToggleButton))
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
