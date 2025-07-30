using Godot;

/// <summary>
/// UI management service for global UI components
/// Manages top menu bar, overlays, and global UI state
/// Instantiated by GameHost, not an autoload
/// </summary>
public partial class UIManager : Control
{
	[Signal] public delegate void LoginRequestedEventHandler();
	[Signal] public delegate void LogoutRequestedEventHandler();
	[Signal] public delegate void ReturnToMenuRequestedEventHandler();

	private CanvasLayer _topMenuLayer;
	private CanvasLayer _modalLayer;
	private TopMenuBar _topMenuBar;
	private LoginModal _loginModal;
	private UserManager _userManager;
	
	// Context tracking
	private string _currentGameTitle = "BarBox Arcade";
	private ContextButtonData[] _currentContextButtons = null;

	public override void _Ready()
	{
		SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		
		// Get service references
		_userManager = UserManager.GetAutoload();
		
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
		
		// Connect signals
		_topMenuBar.LoginRequested += OnLoginRequested;
		_topMenuBar.LogoutRequested += OnLogoutRequested;
		_topMenuBar.ReturnToMenuRequested += OnReturnToMenuRequested;
		
		// Connect login modal signals
		_loginModal.ModalClosed += OnLoginModalClosed;
		
		// Connect to user manager signals
		if (_userManager != null)
		{
			_userManager.UserLoggedIn += OnUserLoggedIn;
			_userManager.UserLoggedOut += OnUserLoggedOut;
			_userManager.UserDataUpdated += OnUserDataUpdated;
		}

		// Initialize with current user state
		UpdateUserDisplay();
		
		GD.Print("[UIManager] Initialized with top menu bar and login modal on separate layers");
	}

	public override void _ExitTree()
	{
		DisconnectSignals();
		base._ExitTree();
	}

	private void DisconnectSignals()
	{
		if (GodotObject.IsInstanceValid(_topMenuBar))
		{
			_topMenuBar.LoginRequested -= OnLoginRequested;
			_topMenuBar.LogoutRequested -= OnLogoutRequested;
			_topMenuBar.ReturnToMenuRequested -= OnReturnToMenuRequested;
		}
		
		if (GodotObject.IsInstanceValid(_loginModal))
		{
			_loginModal.ModalClosed -= OnLoginModalClosed;
		}

		if (GodotObject.IsInstanceValid(_userManager))
		{
			_userManager.UserLoggedIn -= OnUserLoggedIn;
			_userManager.UserLoggedOut -= OnUserLoggedOut;
			_userManager.UserDataUpdated -= OnUserDataUpdated;
		}
	}

	/// <summary>
	/// Set the game context in the top menu bar using simplified ContextButtonData structures
	/// </summary>
	public void SetGameContext(string gameTitle, ContextButtonData[] contextButtons = null)
	{
		_currentGameTitle = gameTitle ?? "BarBox Arcade";
		_currentContextButtons = contextButtons;
		
		RefreshTopMenuContext();
		
		GD.Print($"[UIManager] Game context set: '{_currentGameTitle}' with {contextButtons?.Length ?? 0} buttons");
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
		
		GD.Print("[UIManager] Game context cleared, returned to main menu state");
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
			GD.Print($"[UIManager] Context button states updated for '{_currentGameTitle}'");
		}
	}

	/// <summary>
	/// Update user display based on current login state
	/// </summary>
	public void UpdateUserDisplay()
	{
		var currentUser = _userManager?.GetCurrentUser();
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

	private void OnLogoutRequested()
	{
		EmitSignal(SignalName.LogoutRequested);
	}

	private void OnReturnToMenuRequested()
	{
		EmitSignal(SignalName.ReturnToMenuRequested);
	}

	private void OnUserLoggedIn(UserData userData)
	{
		UpdateUserDisplay();
	}

	private void OnUserLoggedOut()
	{
		UpdateUserDisplay();
	}

	private void OnUserDataUpdated(UserData userData)
	{
		UpdateUserDisplay();
	}

	private void OnLoginModalClosed()
	{
		// Modal was closed, no additional action needed
		GD.Print("[UIManager] Login modal closed");
	}
}