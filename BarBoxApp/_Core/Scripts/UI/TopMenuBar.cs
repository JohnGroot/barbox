using Godot;
using System;

/// <summary>
/// Top menu bar UI component displaying game title, user info, and contextual buttons
/// Pure UI component - no service logic
/// </summary>
public partial class TopMenuBar : Control
{
	[Signal] public delegate void LoginRequestedEventHandler();
	[Signal] public delegate void LogoutRequestedEventHandler();
	[Signal] public delegate void BuyCreditsRequestedEventHandler();
	[Signal] public delegate void ReturnToMenuRequestedEventHandler();

	private const float BASE_HEIGHT = 100.0f;
	private const float CONTEXT_SECTION_HEIGHT = 50.0f;
	private const float EXPANDED_HEIGHT = BASE_HEIGHT + CONTEXT_SECTION_HEIGHT;

	// UI Components
	private Panel _backgroundPanel;
	private VBoxContainer _mainContainer;
	private HBoxContainer _topSection;
	private Label _gameTitleLabel;
	private Label _gameStatusLabel;
	private HBoxContainer _userInfoContainer;
	private Label _userInfoLabel;
	private Button _loginButton;
	private Button _buyCreditsButton;
	private Button _logoutButton;
	
	// Context button section
	private Control _contextSection;
	private HBoxContainer _contextButtonContainer;

	public override void _Ready()
	{
		SetupLayout();
		CreateUI();
		UpdateUserInfo(null); // Initialize as logged out
	}

	private void SetupLayout()
	{
		// Set up as full-width top bar with base height
		SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
		// Set minimum height, width handled by TopWide preset
		CustomMinimumSize = new Vector2(0, BASE_HEIGHT);
	}

	private void CreateUI()
	{
		// Background panel
		_backgroundPanel = new Panel();
		_backgroundPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		
		// Create a dark semi-transparent background
		var styleBox = new StyleBoxFlat();
		styleBox.BgColor = new Color(0.1f, 0.1f, 0.1f, 0.9f);
		styleBox.BorderWidthTop = 2;
		styleBox.BorderWidthBottom = 2;
		styleBox.BorderColor = new Color(0.3f, 0.3f, 0.3f, 1.0f);
		_backgroundPanel.AddThemeStyleboxOverride("panel", styleBox);
		
		AddChild(_backgroundPanel);

		// Add top padding with margin container
		var paddingContainer = new MarginContainer();
		paddingContainer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		paddingContainer.AddThemeConstantOverride("margin_top", 10); // Add 10px top padding
		AddChild(paddingContainer);

		// Main vertical container (expandable)
		_mainContainer = new VBoxContainer();
		_mainContainer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		paddingContainer.AddChild(_mainContainer);

		// Top section (platform UI) - 100px height
		_topSection = new HBoxContainer();
		_topSection.CustomMinimumSize = new Vector2(0, BASE_HEIGHT);
		_topSection.AddThemeConstantOverride("separation", 15);
		_topSection.Alignment = BoxContainer.AlignmentMode.Center; // Center all items vertically
		_mainContainer.AddChild(_topSection);

		// Left section: Game title with wrapping container
		var gameTitleContainer = new Control();
		gameTitleContainer.CustomMinimumSize = new Vector2(200, 0);
		gameTitleContainer.SizeFlagsHorizontal = Control.SizeFlags.Fill;
		gameTitleContainer.SizeFlagsStretchRatio = 0.3f; // Take 30% of available space
		gameTitleContainer.ClipContents = true; // Prevent overflow

		_gameTitleLabel = new Label();
		_gameTitleLabel.Text = "BarBox";
		_gameTitleLabel.VerticalAlignment = VerticalAlignment.Center;
		_gameTitleLabel.AddThemeColorOverride("font_color", Colors.White);
		_gameTitleLabel.AddThemeFontSizeOverride("font_size", 24);
		_gameTitleLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_gameTitleLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		gameTitleContainer.AddChild(_gameTitleLabel);
		_topSection.AddChild(gameTitleContainer);

		// Center section: Available for platform-level information
		_gameStatusLabel = new Label();
		_gameStatusLabel.VerticalAlignment = VerticalAlignment.Center;
		_gameStatusLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_gameStatusLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_gameStatusLabel.AddThemeColorOverride("font_color", Colors.White);
		_gameStatusLabel.AddThemeFontSizeOverride("font_size", 16);
		_gameStatusLabel.Text = ""; // Initially empty
		_topSection.AddChild(_gameStatusLabel);

		// Right section: User info and login/logout
		_userInfoContainer = new HBoxContainer();
		_userInfoContainer.Alignment = BoxContainer.AlignmentMode.End;
		_userInfoContainer.AddThemeConstantOverride("separation", 10);
		_topSection.AddChild(_userInfoContainer);

		// User info label
		_userInfoLabel = new Label();
		_userInfoLabel.VerticalAlignment = VerticalAlignment.Center;
		_userInfoLabel.AddThemeColorOverride("font_color", Colors.White);
		_userInfoLabel.AddThemeFontSizeOverride("font_size", 16);
		_userInfoContainer.AddChild(_userInfoLabel);

		// Login button
		_loginButton = new Button();
		_loginButton.Text = "Login";
		ApplyStandardButtonStyle(_loginButton);
		_loginButton.Pressed += OnLoginPressed;
		_userInfoContainer.AddChild(_loginButton);

		// Buy Credits button
		_buyCreditsButton = new Button();
		_buyCreditsButton.Text = "Buy Credits";
		ApplyStandardButtonStyle(_buyCreditsButton);
		_buyCreditsButton.Pressed += OnBuyCreditsPressed;
		_userInfoContainer.AddChild(_buyCreditsButton);

		// Logout button
		_logoutButton = new Button();
		_logoutButton.Text = "Logout";
		ApplyStandardButtonStyle(_logoutButton);
		_logoutButton.Pressed += OnLogoutPressed;
		_userInfoContainer.AddChild(_logoutButton);

		// Context section (game UI) - initially hidden
		_contextSection = new Control();
		_contextSection.CustomMinimumSize = new Vector2(0, CONTEXT_SECTION_HEIGHT);
		_contextSection.Visible = false;
		_mainContainer.AddChild(_contextSection);

		// Context button container - left aligned with margin
		_contextButtonContainer = new HBoxContainer();
		_contextButtonContainer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.LeftWide);
		_contextButtonContainer.Position = new Vector2(20, 0); // Add 20px left margin
		_contextButtonContainer.Alignment = BoxContainer.AlignmentMode.Begin;
		_contextButtonContainer.AddThemeConstantOverride("separation", 15);
		_contextSection.AddChild(_contextButtonContainer);
	}

	/// <summary>
	/// Set the game title displayed in the menu bar
	/// </summary>
	public void SetGameTitle(string title)
	{
		if (_gameTitleLabel != null)
		{
			_gameTitleLabel.Text = title;
		}
	}


	/// <summary>
	/// Set platform-level status text to display in the center section
	/// Note: Games should generally use their own HUD for game-specific status information
	/// </summary>
	public void SetGameStatus(string statusText)
	{
		if (_gameStatusLabel != null)
		{
			_gameStatusLabel.Text = statusText ?? "";
		}
	}

	/// <summary>
	/// Set contextual buttons for the current game using simplified data structures
	/// </summary>
	public void SetContextButtons(ContextButtonData[] buttonData)
	{
		ClearContextButtons();
		
		bool hasButtons = buttonData != null && buttonData.Length > 0;

		if (hasButtons && _contextButtonContainer != null)
		{
			foreach (var data in buttonData)
			{
				var button = CreateContextButton(data);
				_contextButtonContainer.AddChild(button);
			}
		}
		
		// Show context section when buttons are present, hide when no buttons
		if (_contextSection != null)
		{
			_contextSection.Visible = hasButtons;
		}

		// Update TopMenuBar height dynamically
		SetSize(new Vector2(0, hasButtons ? EXPANDED_HEIGHT : BASE_HEIGHT));
	}

	/// <summary>
	/// Clear all contextual buttons
	/// </summary>
	public void ClearContextButtons()
	{
		if (_contextButtonContainer != null)
		{
			foreach (Node child in _contextButtonContainer.GetChildren())
			{
				child.QueueFree();
			}
		}
		
		// Hide context section when no buttons
		if (_contextSection != null)
		{
			_contextSection.Visible = false;
		}
		
		// Collapse TopMenuBar to base height
		SetSize(new Vector2(0, BASE_HEIGHT));
	}

	/// <summary>
	/// Create a Godot Button from ContextButtonData with consistent styling
	/// </summary>
	private Button CreateContextButton(ContextButtonData data)
	{
		var button = new Button();
		button.Text = data.GetDisplayText();
		button.Disabled = !data.Enabled;
		
		if (!string.IsNullOrEmpty(data.Tooltip))
		{
			button.TooltipText = data.Tooltip;
		}

		// Apply standard button styling
		ApplyStandardButtonStyle(button);

		// Connect the action with error handling
		if (data.OnPressed != null)
		{
			button.Pressed += () => {
				try
				{
					data.OnPressed.Invoke();
				}
				catch (Exception ex)
				{
					GD.PrintErr($"[TopMenuBar] Error executing button action for '{data.Text}': {ex.Message}");
				}
			};
		}

		return button;
	}

	/// <summary>
	/// Update user information display using session system
	/// </summary>
	public void UpdateUserInfo(UserSession session)
	{
		bool isLoggedIn = session != null;

		if (_userInfoLabel != null)
		{
			if (isLoggedIn && session!= null)
			{
				string displayName = !string.IsNullOrEmpty(session.UserName)
					? session.UserName
					: "Player"; // Privacy-safe fallback - never expose phone numbers

				_userInfoLabel.Text = $"{displayName}\n{session.Credits} Credits";
			}
			else
			{
				_userInfoLabel.Text = "Not logged in";
			}
		}

		if (_loginButton != null)
		{
			_loginButton.Visible = !isLoggedIn;
		}

		if (_buyCreditsButton != null)
		{
			_buyCreditsButton.Visible = isLoggedIn;
		}

		if (_logoutButton != null)
		{
			_logoutButton.Visible = isLoggedIn;
		}
	}

	/// <summary>
	/// Enable or disable the logout button (used to prevent logout during active games)
	/// </summary>
	public void SetLogoutButtonEnabled(bool enabled)
	{
		if (_logoutButton != null)
		{
			_logoutButton.Disabled = !enabled;

			if (!enabled)
			{
				_logoutButton.TooltipText = "Cannot logout during active game. Exit to main menu first.";
			}
			else
			{
				_logoutButton.TooltipText = "";
			}
		}
	}

	/// <summary>
	/// Apply consistent styling to all top menu bar buttons
	/// </summary>
	internal static void ApplyStandardButtonStyle(Button button)
	{
		// Standard button size for all top menu buttons
		button.CustomMinimumSize = new Vector2(120, 45);
		
		// Normal state styling
		var normalStyleBox = new StyleBoxFlat();
		normalStyleBox.BgColor = new Color(0.3f, 0.3f, 0.3f, 1.0f);
		normalStyleBox.BorderWidthTop = 1;
		normalStyleBox.BorderWidthBottom = 1;
		normalStyleBox.BorderWidthLeft = 1;
		normalStyleBox.BorderWidthRight = 1;
		normalStyleBox.BorderColor = new Color(0.6f, 0.6f, 0.6f, 1.0f);
		normalStyleBox.CornerRadiusTopLeft = 4;
		normalStyleBox.CornerRadiusTopRight = 4;
		normalStyleBox.CornerRadiusBottomLeft = 4;
		normalStyleBox.CornerRadiusBottomRight = 4;
		button.AddThemeStyleboxOverride("normal", normalStyleBox);
		
		// Hover state styling
		var hoverStyleBox = new StyleBoxFlat();
		hoverStyleBox.BgColor = new Color(0.4f, 0.4f, 0.4f, 1.0f);
		hoverStyleBox.BorderWidthTop = 1;
		hoverStyleBox.BorderWidthBottom = 1;
		hoverStyleBox.BorderWidthLeft = 1;
		hoverStyleBox.BorderWidthRight = 1;
		hoverStyleBox.BorderColor = new Color(0.8f, 0.8f, 0.8f, 1.0f);
		hoverStyleBox.CornerRadiusTopLeft = 4;
		hoverStyleBox.CornerRadiusTopRight = 4;
		hoverStyleBox.CornerRadiusBottomLeft = 4;
		hoverStyleBox.CornerRadiusBottomRight = 4;
		button.AddThemeStyleboxOverride("hover", hoverStyleBox);
		
		// Pressed state styling
		var pressedStyleBox = new StyleBoxFlat();
		pressedStyleBox.BgColor = new Color(0.2f, 0.2f, 0.2f, 1.0f);
		pressedStyleBox.BorderWidthTop = 1;
		pressedStyleBox.BorderWidthBottom = 1;
		pressedStyleBox.BorderWidthLeft = 1;
		pressedStyleBox.BorderWidthRight = 1;
		pressedStyleBox.BorderColor = new Color(0.8f, 0.8f, 0.8f, 1.0f);
		pressedStyleBox.CornerRadiusTopLeft = 4;
		pressedStyleBox.CornerRadiusTopRight = 4;
		pressedStyleBox.CornerRadiusBottomLeft = 4;
		pressedStyleBox.CornerRadiusBottomRight = 4;
		button.AddThemeStyleboxOverride("pressed", pressedStyleBox);
		
		// Text styling
		button.AddThemeColorOverride("font_color", Colors.White);
		button.AddThemeFontSizeOverride("font_size", 14);
	}

	/// <summary>
	/// Get the base height of the TopMenuBar (without context buttons)
	/// </summary>
	public static float GetBaseHeight()
	{
		return BASE_HEIGHT;
	}

	/// <summary>
	/// Get the expanded height of the TopMenuBar (with context buttons)
	/// </summary>
	public static float GetExpandedHeight()
	{
		return EXPANDED_HEIGHT;
	}

	/// <summary>
	/// Get the height of the context button section
	/// </summary>
	public static float GetContextSectionHeight()
	{
		return CONTEXT_SECTION_HEIGHT;
	}

	// Signal handlers

	private void OnLoginPressed()
	{
		EmitSignal(SignalName.LoginRequested);
	}

	private void OnBuyCreditsPressed()
	{
		EmitSignal(SignalName.BuyCreditsRequested);
	}

	private void OnLogoutPressed()
	{
		EmitSignal(SignalName.LogoutRequested);
	}

	public override void _ExitTree()
	{
		// Disconnect any remaining signals
		if (GodotObject.IsInstanceValid(_loginButton))
		{
			_loginButton.Pressed -= OnLoginPressed;
		}
		if (GodotObject.IsInstanceValid(_buyCreditsButton))
		{
			_buyCreditsButton.Pressed -= OnBuyCreditsPressed;
		}
		if (GodotObject.IsInstanceValid(_logoutButton))
		{
			_logoutButton.Pressed -= OnLogoutPressed;
		}
		base._ExitTree();
	}
}