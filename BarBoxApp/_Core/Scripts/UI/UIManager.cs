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
	private TopMenuBar _topMenuBar;
	private LoginModal _loginModal;
	private BuyCreditsModal _buyCreditsModal;
	private SessionManager _sessionManager;
	
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
		
		// Create modal layer (higher than top menu)
		_modalLayer = new CanvasLayer();
		_modalLayer.Layer = 110; // Higher layer to appear above top menu
		AddChild(_modalLayer);
		
		// Create top menu bar (now includes context buttons)
		_topMenuBar = new TopMenuBar();
		_topMenuLayer.AddChild(_topMenuBar);
		
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
}