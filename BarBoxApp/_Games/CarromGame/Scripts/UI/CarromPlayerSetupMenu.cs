using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BarBox.Games.Carrom;

/// <summary>
/// Interstitial player setup menu for Carrom competitive mode
/// Allows players to log in, transfer credits to the table, and start the game
/// </summary>
public partial class CarromPlayerSetupMenu : CanvasLayer
{
	[Signal] public delegate void GameStartRequestedEventHandler();
	[Signal] public delegate void MenuCancelledEventHandler();

	// Configuration
	private int _playerCount = 2;
	private int _costPerGame = 1;
	private int _tableCredits = 0;

	// UI Components - Main structure
	private Panel _modalBackground;
	private Panel _modalPanel;
	private VBoxContainer _contentContainer;
	private Label _titleLabel;

	// Player Slots Section
	private GridContainer _playerSlotsContainer;
	private List<PlayerSlotUI> _playerSlots = [];

	// Table Credits Section
	private VBoxContainer _tableCreditsSection;
	private Label _tableCreditsTitleLabel;
	private Label _tableCreditsAmountLabel;
	private Label _costLabel;

	// Action Buttons Section
	private HBoxContainer _actionButtonsContainer;
	private Button _exitButton;
	private Button _startGameButton;

	// Services
	private SessionManager _sessionManager;
	private UIManager _uiManager;

	// Modals
	private CreditTransferModal _creditTransferModal;

	// Player slot tracking
	private Dictionary<int, string> _slotToPhoneNumber = [];
	private Dictionary<string, int> _creditsTransferredByPlayer = [];
	private int _pendingLoginSlot = -1; // Track which slot is waiting for login

	/// <summary>
	/// Individual player slot UI component
	/// </summary>
	private partial class PlayerSlotUI : VBoxContainer
	{
		public Panel BackgroundPanel { get; private set; }
		public Label SlotNumberLabel { get; private set; }
		public Label PieceColorLabel { get; private set; }
		public Label PlayerInfoLabel { get; private set; }
		public Label CreditsLabel { get; private set; }

		// Buttons
		public Button LoginButton { get; private set; }
		public Button LogoutButton { get; private set; }
		public Button BuyCreditsButton { get; private set; }
		public Button TransferCreditButton { get; private set; }

		private HBoxContainer _actionButtonsContainer;

		public int SlotIndex { get; private set; }
		public bool IsOccupied { get; private set; }

		public PlayerSlotUI(int slotIndex)
		{
			SlotIndex = slotIndex;
			IsOccupied = false;

			CustomMinimumSize = new Vector2(250, 200);
			AddThemeConstantOverride("separation", 10);

			// Background panel
			BackgroundPanel = new Panel();
			BackgroundPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
			var bgStyle = new StyleBoxFlat();
			bgStyle.BgColor = new Color(0.15f, 0.15f, 0.15f, 0.9f);
			bgStyle.BorderColor = new Color(0.3f, 0.3f, 0.3f, 1.0f);
			bgStyle.BorderWidthTop = 2;
			bgStyle.BorderWidthBottom = 2;
			bgStyle.BorderWidthLeft = 2;
			bgStyle.BorderWidthRight = 2;
			bgStyle.CornerRadiusTopLeft = 8;
			bgStyle.CornerRadiusTopRight = 8;
			bgStyle.CornerRadiusBottomLeft = 8;
			bgStyle.CornerRadiusBottomRight = 8;
			BackgroundPanel.AddThemeStyleboxOverride("panel", bgStyle);
			AddChild(BackgroundPanel);

			// Slot number label
			SlotNumberLabel = new Label();
			SlotNumberLabel.Text = $"Player {slotIndex + 1}";
			SlotNumberLabel.HorizontalAlignment = HorizontalAlignment.Center;
			SlotNumberLabel.AddThemeColorOverride("font_color", Colors.White);
			SlotNumberLabel.AddThemeFontSizeOverride("font_size", 16);
			AddChild(SlotNumberLabel);

			// Piece color label (will be set when showing menu based on player count)
			PieceColorLabel = new Label();
			PieceColorLabel.Text = "";
			PieceColorLabel.HorizontalAlignment = HorizontalAlignment.Center;
			PieceColorLabel.AddThemeColorOverride("font_color", Colors.LightGray);
			PieceColorLabel.AddThemeFontSizeOverride("font_size", 12);
			AddChild(PieceColorLabel);

			// Player info label
			PlayerInfoLabel = new Label();
			PlayerInfoLabel.Text = "Empty Slot";
			PlayerInfoLabel.HorizontalAlignment = HorizontalAlignment.Center;
			PlayerInfoLabel.AddThemeColorOverride("font_color", Colors.Gray);
			PlayerInfoLabel.AddThemeFontSizeOverride("font_size", 14);
			PlayerInfoLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			AddChild(PlayerInfoLabel);

			// Credits label
			CreditsLabel = new Label();
			CreditsLabel.Text = "";
			CreditsLabel.HorizontalAlignment = HorizontalAlignment.Center;
			CreditsLabel.AddThemeColorOverride("font_color", Colors.LightGreen);
			CreditsLabel.AddThemeFontSizeOverride("font_size", 12);
			CreditsLabel.Visible = false;
			AddChild(CreditsLabel);

			// Login button (shown when slot is empty)
			LoginButton = new Button();
			LoginButton.Text = "Login";
			LoginButton.CustomMinimumSize = new Vector2(100, 35);
			TopMenuBar.ApplyStandardButtonStyle(LoginButton);
			AddChild(LoginButton);

			// Action buttons container (shown when slot is occupied)
			_actionButtonsContainer = new HBoxContainer();
			_actionButtonsContainer.Alignment = BoxContainer.AlignmentMode.Center;
			_actionButtonsContainer.AddThemeConstantOverride("separation", 5);
			_actionButtonsContainer.Visible = false;
			AddChild(_actionButtonsContainer);

			// Logout button
			LogoutButton = new Button();
			LogoutButton.Text = "Logout";
			LogoutButton.CustomMinimumSize = new Vector2(70, 30);
			TopMenuBar.ApplyStandardButtonStyle(LogoutButton);
			_actionButtonsContainer.AddChild(LogoutButton);

			// Buy credits button
			BuyCreditsButton = new Button();
			BuyCreditsButton.Text = "Buy";
			BuyCreditsButton.CustomMinimumSize = new Vector2(60, 30);
			TopMenuBar.ApplyStandardButtonStyle(BuyCreditsButton);
			_actionButtonsContainer.AddChild(BuyCreditsButton);

			// Transfer credit button
			TransferCreditButton = new Button();
			TransferCreditButton.Text = "Transfer";
			TransferCreditButton.CustomMinimumSize = new Vector2(70, 30);
			TopMenuBar.ApplyStandardButtonStyle(TransferCreditButton);
			_actionButtonsContainer.AddChild(TransferCreditButton);

			UpdateSlotDisplay();
		}

		public void SetOccupied(string userName, int accountCredits)
		{
			IsOccupied = true;
			PlayerInfoLabel.Text = userName;
			PlayerInfoLabel.Modulate = Colors.White;
			CreditsLabel.Text = $"Credits: {accountCredits}";
			CreditsLabel.Visible = true;

			// Update background to show occupied state
			if (BackgroundPanel.GetThemeStylebox("panel") is StyleBoxFlat bgStyle)
			{
				bgStyle.BgColor = new Color(0.2f, 0.3f, 0.25f, 0.9f); // Slight green tint
				bgStyle.BorderColor = new Color(0.4f, 0.6f, 0.4f, 1.0f);
			}

			UpdateSlotDisplay();
		}

		public void SetEmpty()
		{
			IsOccupied = false;
			PlayerInfoLabel.Text = "Empty Slot";
			PlayerInfoLabel.Modulate = Colors.Gray;
			CreditsLabel.Text = "";
			CreditsLabel.Visible = false;

			// Reset background to empty state
			if (BackgroundPanel.GetThemeStylebox("panel") is StyleBoxFlat bgStyle)
			{
				bgStyle.BgColor = new Color(0.15f, 0.15f, 0.15f, 0.9f);
				bgStyle.BorderColor = new Color(0.3f, 0.3f, 0.3f, 1.0f);
			}

			UpdateSlotDisplay();
		}

		public void UpdateCreditsDisplay(int accountCredits)
		{
			if (IsOccupied)
			{
				CreditsLabel.Text = $"Credits: {accountCredits}";
			}
		}

		private void UpdateSlotDisplay()
		{
			LoginButton.Visible = !IsOccupied;
			_actionButtonsContainer.Visible = IsOccupied;
		}
	}

	/// <summary>
	/// Credit transfer confirmation modal with amount selection
	/// Renders at Layer 109 (above game modals, below system modals)
	/// </summary>
	private partial class CreditTransferModal : CanvasLayer
	{
		// Transfer state
		private string _targetPhoneNumber;
		private int _targetSlotIndex;
		private string _playerName;
		private int _availableCredits;
		private int _selectedAmount = 1;

		// Constants
		private const int MIN_TRANSFER_AMOUNT = 1;
		private const int MAX_TRANSFER_PER_TRANSACTION = 20;

		// Async completion
		private TaskCompletionSource<int?> _completionSource;

		// UI Components
		private Panel _backgroundPanel;
		private Panel _modalPanel;
		private Label _titleLabel;
		private Label _playerNameLabel;
		private Label _availableCreditsLabel;
		private Button _decrementButton;
		private Label _amountLabel;
		private Button _incrementButton;
		private Label _statusLabel;
		private Button _confirmButton;
		private Button _cancelButton;

		public CreditTransferModal()
		{
			// Set layer to 109 (between game modals at 108 and system modals at 110)
			Layer = 109;

			CreateModalUI();
			Visible = false;
		}

		private void CreateModalUI()
		{
			// CanvasLayer automatically fills the viewport, no anchor setup needed

			// Semi-transparent background
			_backgroundPanel = new Panel();
			_backgroundPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
			var backgroundStyle = new StyleBoxFlat();
			backgroundStyle.BgColor = new Color(0.0f, 0.0f, 0.0f, 0.7f);
			_backgroundPanel.AddThemeStyleboxOverride("panel", backgroundStyle);
			AddChild(_backgroundPanel);

			// Click background to cancel
			_backgroundPanel.GuiInput += (InputEvent @event) =>
			{
				if (@event is InputEventMouseButton mouseButton &&
				    mouseButton.Pressed &&
				    mouseButton.ButtonIndex == MouseButton.Left)
				{
					OnCancelPressed();
				}
			};

			// Centering container
			var centerContainer = new CenterContainer();
			centerContainer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
			AddChild(centerContainer);

			// Modal panel
			_modalPanel = new Panel();
			_modalPanel.CustomMinimumSize = new Vector2(400, 350);
			_modalPanel.SetSize(new Vector2(400, 350));

			var modalStyle = new StyleBoxFlat();
			modalStyle.BgColor = new Color(0.1f, 0.1f, 0.12f, 1.0f);
			modalStyle.BorderWidthTop = 3;
			modalStyle.BorderWidthBottom = 3;
			modalStyle.BorderWidthLeft = 3;
			modalStyle.BorderWidthRight = 3;
			modalStyle.BorderColor = new Color(0.4f, 0.4f, 0.5f, 1.0f);
			modalStyle.CornerRadiusTopLeft = 10;
			modalStyle.CornerRadiusTopRight = 10;
			modalStyle.CornerRadiusBottomLeft = 10;
			modalStyle.CornerRadiusBottomRight = 10;
			_modalPanel.AddThemeStyleboxOverride("panel", modalStyle);

			centerContainer.AddChild(_modalPanel);

			// Content container with padding
			var marginContainer = new MarginContainer();
			marginContainer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
			marginContainer.AddThemeConstantOverride("margin_left", 20);
			marginContainer.AddThemeConstantOverride("margin_right", 20);
			marginContainer.AddThemeConstantOverride("margin_top", 20);
			marginContainer.AddThemeConstantOverride("margin_bottom", 20);
			_modalPanel.AddChild(marginContainer);

			var contentContainer = new VBoxContainer();
			contentContainer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
			contentContainer.AddThemeConstantOverride("separation", 15);
			marginContainer.AddChild(contentContainer);

			// Title
			_titleLabel = new Label();
			_titleLabel.Text = "Transfer Credits";
			_titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
			_titleLabel.AddThemeColorOverride("font_color", Colors.White);
			_titleLabel.AddThemeFontSizeOverride("font_size", 20);
			contentContainer.AddChild(_titleLabel);

			// Player name
			_playerNameLabel = new Label();
			_playerNameLabel.Text = "Player: ";
			_playerNameLabel.HorizontalAlignment = HorizontalAlignment.Center;
			_playerNameLabel.AddThemeColorOverride("font_color", Colors.LightGray);
			_playerNameLabel.AddThemeFontSizeOverride("font_size", 16);
			contentContainer.AddChild(_playerNameLabel);

			// Available credits
			_availableCreditsLabel = new Label();
			_availableCreditsLabel.Text = "Available: 0 credits";
			_availableCreditsLabel.HorizontalAlignment = HorizontalAlignment.Center;
			_availableCreditsLabel.AddThemeColorOverride("font_color", Colors.LightGreen);
			_availableCreditsLabel.AddThemeFontSizeOverride("font_size", 14);
			contentContainer.AddChild(_availableCreditsLabel);

			// Separator
			var separator1 = new HSeparator();
			contentContainer.AddChild(separator1);

			// Amount selection container
			var amountContainer = new HBoxContainer();
			amountContainer.Alignment = BoxContainer.AlignmentMode.Center;
			amountContainer.AddThemeConstantOverride("separation", 20);
			contentContainer.AddChild(amountContainer);

			// Decrement button
			_decrementButton = new Button();
			_decrementButton.Text = "−";
			_decrementButton.CustomMinimumSize = new Vector2(50, 50);
			_decrementButton.AddThemeFontSizeOverride("font_size", 24);
			TopMenuBar.ApplyStandardButtonStyle(_decrementButton);
			_decrementButton.Pressed += OnDecrementPressed;
			amountContainer.AddChild(_decrementButton);

			// Amount label
			_amountLabel = new Label();
			_amountLabel.Text = "1";
			_amountLabel.HorizontalAlignment = HorizontalAlignment.Center;
			_amountLabel.CustomMinimumSize = new Vector2(80, 50);
			_amountLabel.AddThemeColorOverride("font_color", Colors.Yellow);
			_amountLabel.AddThemeFontSizeOverride("font_size", 28);
			amountContainer.AddChild(_amountLabel);

			// Increment button
			_incrementButton = new Button();
			_incrementButton.Text = "+";
			_incrementButton.CustomMinimumSize = new Vector2(50, 50);
			_incrementButton.AddThemeFontSizeOverride("font_size", 24);
			TopMenuBar.ApplyStandardButtonStyle(_incrementButton);
			_incrementButton.Pressed += OnIncrementPressed;
			amountContainer.AddChild(_incrementButton);

			// Status label
			_statusLabel = new Label();
			_statusLabel.Text = "";
			_statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
			_statusLabel.AddThemeColorOverride("font_color", Colors.Orange);
			_statusLabel.AddThemeFontSizeOverride("font_size", 12);
			_statusLabel.CustomMinimumSize = new Vector2(0, 20);
			contentContainer.AddChild(_statusLabel);

			// Separator
			var separator2 = new HSeparator();
			contentContainer.AddChild(separator2);

			// Button container
			var buttonContainer = new HBoxContainer();
			buttonContainer.Alignment = BoxContainer.AlignmentMode.Center;
			buttonContainer.AddThemeConstantOverride("separation", 15);
			contentContainer.AddChild(buttonContainer);

			// Cancel button
			_cancelButton = new Button();
			_cancelButton.Text = "Cancel";
			_cancelButton.CustomMinimumSize = new Vector2(120, 40);
			TopMenuBar.ApplyStandardButtonStyle(_cancelButton);
			_cancelButton.Pressed += OnCancelPressed;
			buttonContainer.AddChild(_cancelButton);

			// Confirm button (primary style)
			_confirmButton = new Button();
			_confirmButton.Text = "Transfer 1 Credit";
			_confirmButton.CustomMinimumSize = new Vector2(150, 40);
			ApplyPrimaryButtonStyle(_confirmButton);
			_confirmButton.Pressed += OnConfirmPressed;
			buttonContainer.AddChild(_confirmButton);
		}

		private void ApplyPrimaryButtonStyle(Button button)
		{
			var normalStyle = new StyleBoxFlat();
			normalStyle.BgColor = new Color(0.4f, 0.6f, 0.4f, 1.0f);
			normalStyle.BorderWidthTop = 1;
			normalStyle.BorderWidthBottom = 1;
			normalStyle.BorderWidthLeft = 1;
			normalStyle.BorderWidthRight = 1;
			normalStyle.BorderColor = new Color(0.6f, 0.8f, 0.6f, 1.0f);
			normalStyle.CornerRadiusTopLeft = 4;
			normalStyle.CornerRadiusTopRight = 4;
			normalStyle.CornerRadiusBottomLeft = 4;
			normalStyle.CornerRadiusBottomRight = 4;
			button.AddThemeStyleboxOverride("normal", normalStyle);

			var hoverStyle = new StyleBoxFlat();
			hoverStyle.BgColor = new Color(0.5f, 0.7f, 0.5f, 1.0f);
			hoverStyle.BorderWidthTop = 1;
			hoverStyle.BorderWidthBottom = 1;
			hoverStyle.BorderWidthLeft = 1;
			hoverStyle.BorderWidthRight = 1;
			hoverStyle.BorderColor = new Color(0.7f, 0.9f, 0.7f, 1.0f);
			hoverStyle.CornerRadiusTopLeft = 4;
			hoverStyle.CornerRadiusTopRight = 4;
			hoverStyle.CornerRadiusBottomLeft = 4;
			hoverStyle.CornerRadiusBottomRight = 4;
			button.AddThemeStyleboxOverride("hover", hoverStyle);

			button.AddThemeColorOverride("font_color", Colors.White);
			button.AddThemeFontSizeOverride("font_size", 14);
		}

		public async Task<int?> ShowAsync(string phoneNumber, int slotIndex, string playerName, int availableCredits)
		{
			// Set state
			_targetPhoneNumber = phoneNumber;
			_targetSlotIndex = slotIndex;
			_playerName = playerName;
			_availableCredits = availableCredits;
			_selectedAmount = 1;

			// Update UI
			_playerNameLabel.Text = $"Player: {_playerName}";
			_availableCreditsLabel.Text = $"Available: {_availableCredits} credit{(_availableCredits != 1 ? "s" : "")}";
			_amountLabel.Text = _selectedAmount.ToString();
			_statusLabel.Text = "";

			UpdateButtonStates();

			// Create completion source
			_completionSource = new TaskCompletionSource<int?>();

			// Show modal
			Visible = true;

			return await _completionSource.Task;
		}

		private void HideModal()
		{
			Visible = false;
		}

		private void UpdateButtonStates()
		{
			// Update amount label
			_amountLabel.Text = _selectedAmount.ToString();

			// Decrement button: disabled at minimum
			_decrementButton.Disabled = _selectedAmount <= MIN_TRANSFER_AMOUNT;

			// Increment button: disabled at maximum (min of MAX_TRANSFER or available credits)
			int maxAmount = Math.Min(MAX_TRANSFER_PER_TRANSACTION, _availableCredits);
			_incrementButton.Disabled = _selectedAmount >= maxAmount;

			// Confirm button text and state
			string creditText = _selectedAmount != 1 ? "Credits" : "Credit";
			_confirmButton.Text = $"Transfer {_selectedAmount} {creditText}";
			_confirmButton.Disabled = _selectedAmount > _availableCredits || _selectedAmount < MIN_TRANSFER_AMOUNT;

			// Status message
			if (_selectedAmount >= MAX_TRANSFER_PER_TRANSACTION && _availableCredits > MAX_TRANSFER_PER_TRANSACTION)
			{
				_statusLabel.Text = $"Maximum {MAX_TRANSFER_PER_TRANSACTION} credits per transfer";
				_statusLabel.Modulate = Colors.Orange;
			}
			else if (_selectedAmount >= _availableCredits && _availableCredits < MAX_TRANSFER_PER_TRANSACTION)
			{
				_statusLabel.Text = "All available credits selected";
				_statusLabel.Modulate = Colors.Yellow;
			}
			else
			{
				_statusLabel.Text = "";
			}
		}

		private void OnDecrementPressed()
		{
			if (_selectedAmount > MIN_TRANSFER_AMOUNT)
			{
				_selectedAmount--;
				UpdateButtonStates();
			}
		}

		private void OnIncrementPressed()
		{
			int maxAmount = Math.Min(MAX_TRANSFER_PER_TRANSACTION, _availableCredits);
			if (_selectedAmount < maxAmount)
			{
				_selectedAmount++;
				UpdateButtonStates();
			}
		}

		private void OnConfirmPressed()
		{
			// Final validation
			if (_selectedAmount < MIN_TRANSFER_AMOUNT || _selectedAmount > _availableCredits)
			{
				_statusLabel.Text = "Invalid amount selected";
				_statusLabel.Modulate = Colors.Red;
				return;
			}

			// Complete the task with the selected amount
			_completionSource?.SetResult(_selectedAmount);
			HideModal();
		}

		private void OnCancelPressed()
		{
			// Complete the task with null (cancelled)
			_completionSource?.SetResult(null);
			HideModal();
		}
	}

	public override void _Ready()
	{
		// Set layer for game modals (below system modals like login/buy credits)
		Layer = 108;

		_sessionManager = SessionManager.GetInstance();
		_uiManager = UIManager.GetInstance();

		// Create credit transfer modal
		_creditTransferModal = new CreditTransferModal();
		AddChild(_creditTransferModal);

		// Connect to SessionManager signals for real-time updates
		if (_sessionManager != null)
		{
			_sessionManager.UserLoggedIn += OnUserLoggedIn;
		}

		// Start hidden
		Visible = false;
	}

	public void ShowMenu(int playerCount, int costPerGame)
	{
		_playerCount = playerCount;
		_costPerGame = costPerGame;

		// DON'T clear state - preserve between popup open/close cycles
		// _slotToPhoneNumber.Clear();  // ❌ Removed - keeps player slots
		// _tableCredits = 0;            // ❌ Removed - keeps table credits
		// _creditsTransferredByPlayer.Clear();  // ❌ Removed - keeps contributions

		// Create UI if not already created
		if (_modalPanel == null)
		{
			CreateModalUI();
		}

		// Update UI with current configuration
		UpdateUI();

		// Restore machine credits from backend (async)
		RestoreMachineCreditsFromBackendAsync();

		// Restore occupied slots from persisted data
		RestoreOccupiedSlots();

		// Auto-populate first slot if user is logged in
		AutoPopulateFirstSlot();

		Visible = true;
	}

	public void HideMenu()
	{
		Visible = false;
	}

	/// <summary>
	/// Get list of player IDs (Guids) for all logged-in players
	/// Used for backend multiplayer session creation
	/// </summary>
	public System.Guid[] GetLoggedInPlayerIds()
	{
		if (_sessionManager == null)
			return System.Array.Empty<System.Guid>();

		var playerIds = new List<System.Guid>();

		// Iterate through slots in order
		for (int i = 0; i < _playerCount; i++)
		{
			if (_slotToPhoneNumber.TryGetValue(i, out var phoneNumber))
			{
				// Get session to retrieve player ID
				var session = _sessionManager.GetUserSession(phoneNumber);
				if (session != null)
				{
					// Generate player ID from phone number (deterministic UUID)
					var playerId = EventService.GetPlayerIdFromPhone(phoneNumber);
					playerIds.Add(playerId);
				}
			}
		}

		return playerIds.ToArray();
	}

	private void CreateModalUI()
	{
		// Semi-transparent background overlay
		_modalBackground = new Panel();
		_modalBackground.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		var backgroundStyle = new StyleBoxFlat();
		backgroundStyle.BgColor = new Color(0.0f, 0.0f, 0.0f, 0.7f);
		_modalBackground.AddThemeStyleboxOverride("panel", backgroundStyle);

		// Click background to cancel (optional - can be removed if you want to force player interaction)
		_modalBackground.GuiInput += OnBackgroundInput;

		AddChild(_modalBackground);

		// Create centering container
		var centerContainer = new CenterContainer();
		centerContainer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		AddChild(centerContainer);

		// Modal panel (size will be set dynamically in UpdateUI based on player count)
		_modalPanel = new Panel();
		_modalPanel.CustomMinimumSize = new Vector2(600, 500);

		var modalStyle = new StyleBoxFlat();
		modalStyle.BgColor = new Color(0.1f, 0.1f, 0.12f, 1.0f);
		modalStyle.BorderWidthTop = 3;
		modalStyle.BorderWidthBottom = 3;
		modalStyle.BorderWidthLeft = 3;
		modalStyle.BorderWidthRight = 3;
		modalStyle.BorderColor = new Color(0.4f, 0.4f, 0.5f, 1.0f);
		modalStyle.CornerRadiusTopLeft = 10;
		modalStyle.CornerRadiusTopRight = 10;
		modalStyle.CornerRadiusBottomLeft = 10;
		modalStyle.CornerRadiusBottomRight = 10;
		_modalPanel.AddThemeStyleboxOverride("panel", modalStyle);

		centerContainer.AddChild(_modalPanel);

		// Content container with padding
		var marginContainer = new MarginContainer();
		marginContainer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		marginContainer.AddThemeConstantOverride("margin_left", 25);
		marginContainer.AddThemeConstantOverride("margin_right", 25);
		marginContainer.AddThemeConstantOverride("margin_top", 25);
		marginContainer.AddThemeConstantOverride("margin_bottom", 25);
		_modalPanel.AddChild(marginContainer);

		_contentContainer = new VBoxContainer();
		_contentContainer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_contentContainer.AddThemeConstantOverride("separation", 20);
		marginContainer.AddChild(_contentContainer);

		// Title
		_titleLabel = new Label();
		_titleLabel.Text = "Setup Competitive Match";
		_titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_titleLabel.AddThemeColorOverride("font_color", Colors.White);
		_titleLabel.AddThemeFontSizeOverride("font_size", 22);
		_contentContainer.AddChild(_titleLabel);

		// Player slots section
		CreatePlayerSlotsSection();

		// Table credits section
		CreateTableCreditsSection();

		// Action buttons section
		CreateActionButtonsSection();
	}

	private void CreatePlayerSlotsSection()
	{
		var sectionLabel = new Label();
		sectionLabel.Text = "Players";
		sectionLabel.HorizontalAlignment = HorizontalAlignment.Center;
		sectionLabel.AddThemeColorOverride("font_color", Colors.LightGray);
		sectionLabel.AddThemeFontSizeOverride("font_size", 16);
		_contentContainer.AddChild(sectionLabel);

		// Player slots grid (1x2 or 2x2 depending on player count)
		_playerSlotsContainer = new GridContainer();
		_playerSlotsContainer.Columns = 2;
		_playerSlotsContainer.AddThemeConstantOverride("h_separation", 15);
		_playerSlotsContainer.AddThemeConstantOverride("v_separation", 15);
		_contentContainer.AddChild(_playerSlotsContainer);

		// Will be populated in UpdateUI() based on player count
	}

	private void CreateTableCreditsSection()
	{
		_tableCreditsSection = new VBoxContainer();
		_tableCreditsSection.AddThemeConstantOverride("separation", 5);
		_contentContainer.AddChild(_tableCreditsSection);

		_tableCreditsTitleLabel = new Label();
		_tableCreditsTitleLabel.Text = "Table Credits";
		_tableCreditsTitleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_tableCreditsTitleLabel.AddThemeColorOverride("font_color", Colors.LightGray);
		_tableCreditsTitleLabel.AddThemeFontSizeOverride("font_size", 16);
		_tableCreditsSection.AddChild(_tableCreditsTitleLabel);

		_tableCreditsAmountLabel = new Label();
		_tableCreditsAmountLabel.Text = "0 Credits";
		_tableCreditsAmountLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_tableCreditsAmountLabel.AddThemeColorOverride("font_color", Colors.Yellow);
		_tableCreditsAmountLabel.AddThemeFontSizeOverride("font_size", 18);
		_tableCreditsSection.AddChild(_tableCreditsAmountLabel);

		_costLabel = new Label();
		_costLabel.Text = "Cost: 1 Credit";
		_costLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_costLabel.AddThemeColorOverride("font_color", Colors.LightGray);
		_costLabel.AddThemeFontSizeOverride("font_size", 14);
		_tableCreditsSection.AddChild(_costLabel);
	}

	private void CreateActionButtonsSection()
	{
		_actionButtonsContainer = new HBoxContainer();
		_actionButtonsContainer.Alignment = BoxContainer.AlignmentMode.Center;
		_actionButtonsContainer.AddThemeConstantOverride("separation", 20);
		_contentContainer.AddChild(_actionButtonsContainer);

		// Exit button
		_exitButton = new Button();
		_exitButton.Text = "Exit";
		_exitButton.CustomMinimumSize = new Vector2(120, 45);
		TopMenuBar.ApplyStandardButtonStyle(_exitButton);
		_exitButton.Pressed += OnExitPressed;
		_actionButtonsContainer.AddChild(_exitButton);

		// Start game button
		_startGameButton = new Button();
		_startGameButton.Text = "Start Game";
		_startGameButton.CustomMinimumSize = new Vector2(150, 45);
		ApplyPrimaryButtonStyle(_startGameButton);
		_startGameButton.Pressed += OnStartGamePressed;
		_actionButtonsContainer.AddChild(_startGameButton);
	}

	private void ApplyPrimaryButtonStyle(Button button)
	{
		// Similar to ConfirmationDialog's primary button
		var normalStyle = new StyleBoxFlat();
		normalStyle.BgColor = new Color(0.4f, 0.6f, 0.4f, 1.0f);
		normalStyle.BorderWidthTop = 1;
		normalStyle.BorderWidthBottom = 1;
		normalStyle.BorderWidthLeft = 1;
		normalStyle.BorderWidthRight = 1;
		normalStyle.BorderColor = new Color(0.6f, 0.8f, 0.6f, 1.0f);
		normalStyle.CornerRadiusTopLeft = 4;
		normalStyle.CornerRadiusTopRight = 4;
		normalStyle.CornerRadiusBottomLeft = 4;
		normalStyle.CornerRadiusBottomRight = 4;
		button.AddThemeStyleboxOverride("normal", normalStyle);

		var hoverStyle = new StyleBoxFlat();
		hoverStyle.BgColor = new Color(0.5f, 0.7f, 0.5f, 1.0f);
		hoverStyle.BorderWidthTop = 1;
		hoverStyle.BorderWidthBottom = 1;
		hoverStyle.BorderWidthLeft = 1;
		hoverStyle.BorderWidthRight = 1;
		hoverStyle.BorderColor = new Color(0.7f, 0.9f, 0.7f, 1.0f);
		hoverStyle.CornerRadiusTopLeft = 4;
		hoverStyle.CornerRadiusTopRight = 4;
		hoverStyle.CornerRadiusBottomLeft = 4;
		hoverStyle.CornerRadiusBottomRight = 4;
		button.AddThemeStyleboxOverride("hover", hoverStyle);

		button.AddThemeColorOverride("font_color", Colors.White);
		button.AddThemeFontSizeOverride("font_size", 16);
	}

	private void UpdateUI()
	{
		// Update title based on player count
		_titleLabel.Text = $"Setup {_playerCount}-Player Match";

		// Update modal panel height based on player count
		// 2-player: 500px (1x2 grid) | 4-player: 650px (2x2 grid needs more space)
		float panelHeight = _playerCount == 2 ? 500f : 650f;
		if (_modalPanel != null)
		{
			_modalPanel.CustomMinimumSize = new Vector2(600, panelHeight);
			_modalPanel.SetSize(new Vector2(600, panelHeight));
		}

		// Update cost label
		_costLabel.Text = $"Cost: {_costPerGame} Credit{(_costPerGame > 1 ? "s" : "")}";

		// Clear existing player slots
		foreach (var slot in _playerSlots)
		{
			slot.QueueFree();
		}
		_playerSlots.Clear();

		// Create player slots based on player count
		for (int i = 0; i < _playerCount; i++)
		{
			var slot = new PlayerSlotUI(i);

			// Set piece color for this slot
			var pieceColor = GetPieceColorForSlot(i, _playerCount);
			string colorText = pieceColor == PieceType.White ? "⚪ White Pieces" : "⚫ Black Pieces";
			slot.PieceColorLabel.Text = colorText;

			// Connect slot button signals
			int slotIndex = i; // Capture for lambda
			slot.LoginButton.Pressed += () => OnLoginButtonPressed(slotIndex);
			slot.LogoutButton.Pressed += () => OnLogoutButtonPressed(slotIndex);
			slot.BuyCreditsButton.Pressed += () => OnBuyCreditsButtonPressed(slotIndex);
			slot.TransferCreditButton.Pressed += () => OnTransferCreditButtonPressed(slotIndex);

			_playerSlotsContainer.AddChild(slot);
			_playerSlots.Add(slot);
		}

		UpdateTableCreditsDisplay();
		UpdateStartGameButton();
	}

	/// <summary>
	/// Determine piece color assignment for a slot based on player count
	/// 2-player: Slot 0 = White, Slot 1 = Black
	/// 4-player: Slots 0,2 = White team, Slots 1,3 = Black team
	/// </summary>
	private PieceType GetPieceColorForSlot(int slotIndex, int playerCount)
	{
		if (playerCount == 2)
		{
			return slotIndex == 0 ? PieceType.White : PieceType.Black;
		}
		else // 4-player
		{
			return (slotIndex == 0 || slotIndex == 2) ? PieceType.White : PieceType.Black;
		}
	}

	private void AutoPopulateFirstSlot()
	{
		if (_sessionManager == null) return;

		// Only auto-populate if slot 0 is not already occupied
		if (_slotToPhoneNumber.ContainsKey(0))
			return;

		// Auto-populate first slot with primary user (UI convenience for local multiplayer)
		var currentSession = _sessionManager.GetPrimaryUserSession();
		if (currentSession != null && currentSession!= null)
		{
			// Add primary user to first slot
			_slotToPhoneNumber[0] = currentSession.PhoneNumber;

			// Only initialize contribution to 0 if not already set (respect loaded data)
			if (!_creditsTransferredByPlayer.ContainsKey(currentSession.PhoneNumber))
			{
				_creditsTransferredByPlayer[currentSession.PhoneNumber] = 0;
			}

			var slot = _playerSlots[0];
			slot.SetOccupied(currentSession.UserName, currentSession.Credits);
		}
	}

	/// <summary>
	/// Restore occupied slot UI from persisted slot assignments
	/// Clears slots for users who are no longer logged in
	/// </summary>
	private void RestoreOccupiedSlots()
	{
		if (_sessionManager == null) return;

		// Track slots and phone numbers to clear if user is no longer logged in
		var slotsToRemove = new List<(int slotIndex, string phoneNumber)>();

		foreach (var kvp in _slotToPhoneNumber)
		{
			int slotIndex = kvp.Key;
			string phoneNumber = kvp.Value;

			// Validate slot index
			if (slotIndex < 0 || slotIndex >= _playerSlots.Count)
				continue;

			// Get user session
			var session = _sessionManager.GetUserSession(phoneNumber);
			if (session != null && session != null)
			{
				// User is logged in - restore occupied state
				var slot = _playerSlots[slotIndex];
				slot.SetOccupied(session.UserName, session.Credits);
			}
			else
			{
				// User is no longer logged in - mark for removal
				slotsToRemove.Add((slotIndex, phoneNumber));
			}
		}

		// Clear slots for logged out users
		foreach (var (slotIndex, phoneNumber) in slotsToRemove)
		{
			_slotToPhoneNumber.Remove(slotIndex);
			_creditsTransferredByPlayer.Remove(phoneNumber);

			var slot = _playerSlots[slotIndex];
			slot.SetEmpty();
		}
	}

	private void UpdateTableCreditsDisplay()
	{
		_tableCreditsAmountLabel.Text = $"{_tableCredits} Credit{(_tableCredits != 1 ? "s" : "")}";

		// Color based on sufficiency
		if (_tableCredits >= _costPerGame)
		{
			_tableCreditsAmountLabel.Modulate = Colors.LightGreen;
		}
		else
		{
			_tableCreditsAmountLabel.Modulate = Colors.Yellow;
		}
	}

	private bool ValidatePlayerSlots()
	{
		// Check correct number of slots filled
		if (_slotToPhoneNumber.Count != _playerCount)
			return false;

		// Validate each player has valid session
		foreach (var kvp in _slotToPhoneNumber)
		{
			var session = _sessionManager?.GetUserSession(kvp.Value);
			if (session == null)
				return false;
		}

		return true;
	}

	private void UpdateStartGameButton()
	{
		// Validate sufficient credits
		bool hasCredits = _tableCredits >= _costPerGame;

		// Validate all required player slots are filled
		bool slotsFilledCorrectly = ValidatePlayerSlots();

		// Enable button only if BOTH conditions met
		bool canStart = hasCredits && slotsFilledCorrectly;
		_startGameButton.Disabled = !canStart;

		// Update button text to provide feedback
		if (!hasCredits)
		{
			_startGameButton.Text = $"Need {_costPerGame - _tableCredits} More Credits";
		}
		else if (!slotsFilledCorrectly)
		{
			int needed = _playerCount - _slotToPhoneNumber.Count;
			_startGameButton.Text = $"Need {needed} More Player{(needed > 1 ? "s" : "")}";
		}
		else
		{
			_startGameButton.Text = "Start Game";
		}
	}

	// Event handlers

	private void OnBackgroundInput(InputEvent inputEvent)
	{
		if (inputEvent is InputEventMouseButton mouseButton &&
		    mouseButton.ButtonIndex == MouseButton.Left &&
		    mouseButton.Pressed)
		{
			OnExitPressed();
		}
	}

	private void OnLoginButtonPressed(int slotIndex)
	{
		if (_uiManager == null) return;

		// Store which slot is waiting for login
		_pendingLoginSlot = slotIndex;

		// Show login modal
		_uiManager.ShowLoginModal();
	}

	/// <summary>
	/// Handle SessionManager's UserLoggedIn signal
	/// Comprehensive fix: If _pendingLoginSlot is invalid, find first empty slot automatically
	/// </summary>
	private void OnUserLoggedIn(string phoneNumber)
	{
		if (_sessionManager == null)
			return;

		// Get the user session
		var session = _sessionManager.GetUserSession(phoneNumber);
		if (session == null)
			return;

		// Check if this user is already in another slot
		if (_slotToPhoneNumber.Values.Contains(phoneNumber))
		{
			GD.Print($"User {phoneNumber} already in a slot");
			_pendingLoginSlot = -1;
			return;
		}

		// Determine which slot to assign this player to
		int slotIndex = _pendingLoginSlot;

		// If _pendingLoginSlot is invalid, find first empty slot as fallback
		if (slotIndex < 0 || slotIndex >= _playerSlots.Count)
		{
			GD.Print($"Invalid pending login slot: {_pendingLoginSlot}, searching for empty slot");

			// Find first slot without a phone number assigned
			slotIndex = -1;
			for (int i = 0; i < _playerSlots.Count; i++)
			{
				if (!_slotToPhoneNumber.ContainsKey(i))
				{
					slotIndex = i;
					GD.Print($"Found empty slot at index {i} for {phoneNumber}");
					break;
				}
			}

			// If still no valid slot, bail out
			if (slotIndex < 0)
			{
				GD.PrintErr($"No empty slots available for {phoneNumber}");
				_pendingLoginSlot = -1;
				return;
			}
		}

		// Assign user to the determined slot
		_slotToPhoneNumber[slotIndex] = phoneNumber;
		_creditsTransferredByPlayer[phoneNumber] = 0;

		// Update slot UI
		if (slotIndex >= 0 && slotIndex < _playerSlots.Count)
		{
			var slot = _playerSlots[slotIndex];
			slot.SetOccupied(session.UserName, session.Credits);
			GD.Print($"Player {session.UserName} assigned to slot {slotIndex}");
		}

		// Clear pending slot
		_pendingLoginSlot = -1;

		// Update game start button state
		UpdateStartGameButton();
	}

	private async void OnLogoutButtonPressed(int slotIndex)
	{
		if (_sessionManager == null) return;
		if (!_slotToPhoneNumber.TryGetValue(slotIndex, out var phoneNumber))
			return;

		// Return transferred credits to player
		if (_creditsTransferredByPlayer.TryGetValue(phoneNumber, out var transferredAmount))
		{
			if (transferredAmount > 0)
			{
				// Return credits to player's account
				var addResult = await _sessionManager.AddGlobalCreditsAsync(phoneNumber, transferredAmount, "Returned from table");
				if (addResult)
				{
					_tableCredits -= transferredAmount;

					// Decrement machine credits to reflect return
					// TODO: Event-sourced - backend handles machine credits via events
				}
			}
		}

		// Clear slot
		_slotToPhoneNumber.Remove(slotIndex);
		_creditsTransferredByPlayer.Remove(phoneNumber);

		var slot = _playerSlots[slotIndex];
		slot.SetEmpty();

		// Logout user if they're the primary session
		var currentSession = _sessionManager.GetPrimaryUserSession();
		if (currentSession != null && currentSession.PhoneNumber == phoneNumber)
		{
			await _sessionManager.LogoutUserAsync(phoneNumber);
		}

		UpdateTableCreditsDisplay();
		UpdateStartGameButton();
	}

	private void OnBuyCreditsButtonPressed(int slotIndex)
	{
		if (_uiManager == null) return;
		if (!_slotToPhoneNumber.TryGetValue(slotIndex, out var phoneNumber))
			return;

		// Show buy credits modal for this specific user (explicit user targeting)
		_uiManager.ShowBuyCreditsModal(phoneNumber);

		// Wait for purchase completion and update display
		RefreshSlotCreditsDisplay(slotIndex);
	}

	private async void RefreshSlotCreditsDisplay(int slotIndex)
	{
		if (_sessionManager == null) return;
		if (!_slotToPhoneNumber.TryGetValue(slotIndex, out var phoneNumber))
			return;

		// Wait a moment for purchase to process
		await ToSignal(GetTree().CreateTimer(1.0f), Timer.SignalName.Timeout);

		// Refresh credits display
		var session = _sessionManager.GetUserSession(phoneNumber);
		if (session != null && session != null)
		{
			var slot = _playerSlots[slotIndex];
			slot.UpdateCreditsDisplay(session.Credits);
		}
	}

	private async void OnTransferCreditButtonPressed(int slotIndex)
	{
		if (_sessionManager == null) // Event-sourced - no DataStore needed
			return;
		if (!_slotToPhoneNumber.TryGetValue(slotIndex, out var phoneNumber))
			return;

		var session = _sessionManager.GetUserSession(phoneNumber);
		if (session == null || false)
			return;

		// Check if player has credits
		int availableCredits = session.Credits;
		if (availableCredits < 1)
		{
			GD.Print($"Player {phoneNumber} has insufficient credits");
			return;
		}

		// Show modal and get selected amount
		var selectedAmount = await _creditTransferModal.ShowAsync(
			phoneNumber,
			slotIndex,
			session.UserName,
			availableCredits
		);

		// If cancelled or invalid, return
		if (!selectedAmount.HasValue || selectedAmount.Value < 1)
			return;

		// Transfer credits atomically: player credits → machine credits (via SessionManager)
		var transferSuccess = await _sessionManager.TransferCreditsToMachineAsync(
			phoneNumber,
			"carrom",  // Use game tag (not game ID) to match backend queries
			selectedAmount.Value,
			"Transfer to Carrom table"
		);

		if (transferSuccess)
		{
			// Event-sourced - backend tracks machine credits
			// Update local table credits cache
			_tableCredits += selectedAmount.Value;

			// Track how many credits this player has transferred
			if (!_creditsTransferredByPlayer.ContainsKey(phoneNumber))
			{
				_creditsTransferredByPlayer[phoneNumber] = 0;
			}
			_creditsTransferredByPlayer[phoneNumber] += selectedAmount.Value;

			// Persist player contributions to machine data
			await SavePlayerContributionsToMachineData();

			// Update displays (session cache already updated by SessionManager)
			if (session != null && session != null)
			{
				var slot = _playerSlots[slotIndex];
				slot.UpdateCreditsDisplay(session.Credits);
			}

			UpdateTableCreditsDisplay();
			UpdateStartGameButton();
		}
	}

	private void OnExitPressed()
	{
		HideMenu();
		EmitSignal(SignalName.MenuCancelled);
	}

	private async void OnStartGamePressed()
	{
		// Validate credits (existing check)
		if (_tableCredits < _costPerGame)
			return;

		// Validate player slots
		if (!ValidatePlayerSlots())
		{
			int needed = _playerCount - _slotToPhoneNumber.Count;
			GD.PrintErr($"[CarromPlayerSetupMenu] Cannot start - need {needed} more player(s) to fill all slots");
			return;
		}

		// Consume credits from machine pot (backend tracking)
		var eventService = EventService.GetInstance();
		var locationManager = LocationManager.GetAutoload();

		if (eventService != null && locationManager != null)
		{
			var boxId = locationManager.BoxId;

			// TODO: Get game session ID from CarromGame when starting
			// For now, use a temporary session ID - will be replaced with actual game session
			var tempGameSessionId = Guid.NewGuid();

			var consumeResult = await eventService.ConsumeMachineCreditsAsync(
				"carrom",  // Use game tag to match backend queries
				boxId,
				_costPerGame,
				tempGameSessionId
			);

			if (consumeResult.IsSuccess(out var machineState))
			{
				// Backend consumption successful - update local state
				_tableCredits = machineState.Balance;

				HideMenu();
				EmitSignal(SignalName.GameStartRequested);

				// IMPORTANT: Clear local state AFTER signal emission
				// Signal handlers need to read _slotToPhoneNumber first!
				_slotToPhoneNumber.Clear();
				_creditsTransferredByPlayer.Clear();
			}
			else if (consumeResult.IsFailure(out var error))
			{
				GD.PrintErr($"[CarromPlayerSetupMenu] Failed to consume machine credits: {error.Message}");
			}
		}
		else
		{
			// Fallback for development mode - just consume locally
			_tableCredits -= _costPerGame;

			HideMenu();
			EmitSignal(SignalName.GameStartRequested);

			// IMPORTANT: Clear local state AFTER signal emission
			// Signal handlers need to read _slotToPhoneNumber first!
			_slotToPhoneNumber.Clear();
			_creditsTransferredByPlayer.Clear();
		}
	}

	private async Task ReturnAllCreditsToPlayers()
	{
		if (_sessionManager == null) return;

		int totalReturned = 0;

		// Return transferred credits to each player
		foreach (var kvp in _creditsTransferredByPlayer)
		{
			var phoneNumber = kvp.Key;
			var amount = kvp.Value;

			if (amount > 0)
			{
				var addResult = await _sessionManager.AddGlobalCreditsAsync(phoneNumber, amount, "Returned from table");
				if (addResult)
				{
					totalReturned += amount;
				}
			}
		}

		// Decrement machine credits to reflect all returns
		// TODO: Event-sourced - backend handles machine credits via events
		if (false && totalReturned > 0)
		{
			// Machine credit handling moved to backend
		}

		_tableCredits = 0;
		_creditsTransferredByPlayer.Clear();

		// Clear persisted contribution tracking
		await SavePlayerContributionsToMachineData();
	}

	/// <summary>
	/// Cleanup all player sessions when exiting the game
	/// Logs out secondary players (non-primary sessions) without returning credits
	/// Credits transferred to the table are permanent and remain in machine pool
	/// </summary>
	private async Task CleanupAllPlayerSessions()
	{
		if (_sessionManager == null) return;

		// Get primary session to avoid logging it out (primary user manages their own session lifecycle)
		var primarySession = _sessionManager.GetPrimaryUserSession();
		string primaryPhoneNumber = primarySession?.PhoneNumber;

		// Process each slot - use ToList to avoid modification during iteration
		foreach (var kvp in _slotToPhoneNumber.ToList())
		{
			string phoneNumber = kvp.Value;

			// Logout secondary players only (non-primary sessions)
			// Primary user manages their own session lifecycle
			// NOTE: Credits transferred to table are PERMANENT and are NOT returned
			if (!string.IsNullOrEmpty(phoneNumber) && phoneNumber != primaryPhoneNumber)
			{
				await _sessionManager.LogoutUserAsync(phoneNumber);
				GD.Print($"[CarromPlayerSetupMenu] Logged out secondary player: {phoneNumber}");
			}
		}

		// Clear slot tracking (credits remain in machine pool permanently)
		_slotToPhoneNumber.Clear();
		_creditsTransferredByPlayer.Clear();

		// Clear persisted slot assignments (but machine credits remain untouched)
		await SavePlayerContributionsToMachineData();
	}

	/// <summary>
	/// Async void wrapper for session cleanup - safe to call from synchronous _ExitTree
	/// </summary>
	private async void CleanupAllPlayerSessionsAsync()
	{
		try
		{
			await CleanupAllPlayerSessions();
		}
		catch (System.Exception ex)
		{
			// Thread-safe error logging via CallDeferred
			CallDeferred(MethodName.LogCleanupError, ex.Message);
		}
	}

	private void LogCleanupError(string message)
	{
		GD.PrintErr($"[CarromPlayerSetupMenu] Session cleanup failed: {message}");
	}

	private async void RestoreMachineCreditsFromBackendAsync()
	{
		try
		{
			var eventService = EventService.GetInstance();
			if (eventService == null)
			{
				GD.Print("[CarromPlayerSetupMenu] EventService not available - using local state only");
				return;
			}

			var locationManager = LocationManager.GetAutoload();
			if (locationManager == null)
			{
				GD.Print("[CarromPlayerSetupMenu] LocationManager not available");
				return;
			}

			var boxId = locationManager.BoxId;
			var result = await eventService.GetMachineCreditsAsync("carrom", boxId);

			if (result.IsSuccess(out var machineCredits))
			{
				_tableCredits = machineCredits.Balance;

				// Restore player contributions
				_creditsTransferredByPlayer.Clear();
				foreach (var contribution in machineCredits.Contributions)
				{
					if (Guid.TryParse(contribution.PlayerId, out var playerId))
					{
						// Convert playerId to phone number by checking all active sessions
						// Match against secondary index in SessionManager
						var sessionManager = SessionManager.GetInstance();
						if (sessionManager != null)
						{
							// Check all active sessions to find matching player ID
							var activePhones = sessionManager.GetActivePhoneNumbers();
							foreach (var phone in activePhones)
							{
								var session = sessionManager.GetUserSession(phone);
								if (session != null && session.PlayerId == playerId)
								{
									_creditsTransferredByPlayer[session.PhoneNumber] = contribution.Amount;
									break;
								}
							}
						}
					}
				}

				// Update only what changed from backend response (don't recreate slots!)
				UpdateTableCreditsDisplay();
				UpdateStartGameButton();
				GD.Print($"[CarromPlayerSetupMenu] Restored {_tableCredits} table credits with {_creditsTransferredByPlayer.Count} contributions");
			}
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"[CarromPlayerSetupMenu] Failed to restore machine credits: {ex.Message}");
		}
	}

	/// <summary>
	/// Persist player contributions and slot assignments to MachineGameData.GameState
	/// This is called after each credit transfer to update the backend
	/// </summary>
	private async Task SavePlayerContributionsToMachineData()
	{
		// Backend deposit is now handled directly in OnTransferCreditButtonPressed
		// This method kept for compatibility but may be removed in future refactoring
		await Task.CompletedTask;
		return;
	}


	public override void _ExitTree()
	{
		// Cleanup all player sessions (return credits and logout secondary players)
		CleanupAllPlayerSessionsAsync();

		// Cleanup SessionManager signals
		if (GodotObject.IsInstanceValid(_sessionManager))
		{
			_sessionManager.UserLoggedIn -= OnUserLoggedIn;
		}

		// Cleanup signals
		if (GodotObject.IsInstanceValid(_modalBackground))
		{
			_modalBackground.GuiInput -= OnBackgroundInput;
		}

		if (GodotObject.IsInstanceValid(_exitButton))
		{
			_exitButton.Pressed -= OnExitPressed;
		}

		if (GodotObject.IsInstanceValid(_startGameButton))
		{
			_startGameButton.Pressed -= OnStartGamePressed;
		}

		base._ExitTree();
	}
}