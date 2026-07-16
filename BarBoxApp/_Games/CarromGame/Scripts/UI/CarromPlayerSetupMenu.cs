using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BarBox.Core.Autoloads;
using BarBox.Games.Carrom;
using Godot;

/// <summary>
/// Interstitial player setup menu for Carrom competitive mode
/// Allows players to log in, transfer credits to the table, and start the game
/// </summary>
public partial class CarromPlayerSetupMenu : CanvasLayer
{
	[Signal]
	public delegate void GameStartRequestedEventHandler();

	[Signal]
	public delegate void MenuCancelledEventHandler();

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
	private CreditService _creditService;

	// Modals
	private CreditTransferModal _creditTransferModal;

	// Player slot tracking
	private Dictionary<int, string> _slotToPhoneNumber = [];
	private Dictionary<string, int> _creditsTransferredByPlayer = [];
	private int _pendingLoginSlot = -1;

	private partial class PlayerSlotUI : VBoxContainer
	{
		private static readonly Vector2 SlotSize = new(250, 200);

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

			CustomMinimumSize = SlotSize;
			AddThemeConstantOverride("separation", 10);

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

			SlotNumberLabel = new Label();
			SlotNumberLabel.Text = $"Player {slotIndex + 1}";
			SlotNumberLabel.HorizontalAlignment = HorizontalAlignment.Center;
			SlotNumberLabel.AddThemeColorOverride("font_color", Colors.White);
			SlotNumberLabel.AddThemeFontSizeOverride("font_size", 16);
			AddChild(SlotNumberLabel);

			// Piece color label (will be set when showing menu based on player count)
			PieceColorLabel = new Label();
			PieceColorLabel.Text = string.Empty;
			PieceColorLabel.HorizontalAlignment = HorizontalAlignment.Center;
			PieceColorLabel.AddThemeColorOverride("font_color", Colors.LightGray);
			PieceColorLabel.AddThemeFontSizeOverride("font_size", 12);
			AddChild(PieceColorLabel);

			PlayerInfoLabel = new Label();
			PlayerInfoLabel.Text = "Empty Slot";
			PlayerInfoLabel.HorizontalAlignment = HorizontalAlignment.Center;
			PlayerInfoLabel.AddThemeColorOverride("font_color", Colors.Gray);
			PlayerInfoLabel.AddThemeFontSizeOverride("font_size", 14);
			PlayerInfoLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			AddChild(PlayerInfoLabel);

			CreditsLabel = new Label();
			CreditsLabel.Text = string.Empty;
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

			LogoutButton = new Button();
			LogoutButton.Text = "Logout";
			LogoutButton.CustomMinimumSize = new Vector2(70, 30);
			TopMenuBar.ApplyStandardButtonStyle(LogoutButton);
			_actionButtonsContainer.AddChild(LogoutButton);

			BuyCreditsButton = new Button();
			BuyCreditsButton.Text = "Buy";
			BuyCreditsButton.CustomMinimumSize = new Vector2(60, 30);
			TopMenuBar.ApplyStandardButtonStyle(BuyCreditsButton);
			_actionButtonsContainer.AddChild(BuyCreditsButton);

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
			CreditsLabel.Text = string.Empty;
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
		private int _selectedAmount = 1000;

		// Constants
		private const int MIN_TRANSFER_AMOUNT = 1000;
		private const int MAX_TRANSFER_PER_TRANSACTION = 20000;
		private static readonly Vector2 ModalSize = new(400, 350);

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

			var centerContainer = new CenterContainer();
			centerContainer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
			AddChild(centerContainer);

			_modalPanel = new Panel();
			_modalPanel.CustomMinimumSize = ModalSize;
			_modalPanel.SetSize(ModalSize);

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

			_titleLabel = new Label();
			_titleLabel.Text = "Transfer Credits";
			_titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
			_titleLabel.AddThemeColorOverride("font_color", Colors.White);
			_titleLabel.AddThemeFontSizeOverride("font_size", 20);
			contentContainer.AddChild(_titleLabel);

			_playerNameLabel = new Label();
			_playerNameLabel.Text = "Player: ";
			_playerNameLabel.HorizontalAlignment = HorizontalAlignment.Center;
			_playerNameLabel.AddThemeColorOverride("font_color", Colors.LightGray);
			_playerNameLabel.AddThemeFontSizeOverride("font_size", 16);
			contentContainer.AddChild(_playerNameLabel);

			_availableCreditsLabel = new Label();
			_availableCreditsLabel.Text = "Available: 0 credits";
			_availableCreditsLabel.HorizontalAlignment = HorizontalAlignment.Center;
			_availableCreditsLabel.AddThemeColorOverride("font_color", Colors.LightGreen);
			_availableCreditsLabel.AddThemeFontSizeOverride("font_size", 14);
			contentContainer.AddChild(_availableCreditsLabel);

			var separator1 = new HSeparator();
			contentContainer.AddChild(separator1);

			var amountContainer = new HBoxContainer();
			amountContainer.Alignment = BoxContainer.AlignmentMode.Center;
			amountContainer.AddThemeConstantOverride("separation", 20);
			contentContainer.AddChild(amountContainer);

			_decrementButton = new Button();
			_decrementButton.Text = "−";
			_decrementButton.CustomMinimumSize = new Vector2(50, 50);
			_decrementButton.AddThemeFontSizeOverride("font_size", 24);
			TopMenuBar.ApplyStandardButtonStyle(_decrementButton);
			_decrementButton.Pressed += OnDecrementPressed;
			amountContainer.AddChild(_decrementButton);

			_amountLabel = new Label();
			_amountLabel.Text = "1";
			_amountLabel.HorizontalAlignment = HorizontalAlignment.Center;
			_amountLabel.CustomMinimumSize = new Vector2(80, 50);
			_amountLabel.AddThemeColorOverride("font_color", Colors.Yellow);
			_amountLabel.AddThemeFontSizeOverride("font_size", 28);
			amountContainer.AddChild(_amountLabel);

			_incrementButton = new Button();
			_incrementButton.Text = "+";
			_incrementButton.CustomMinimumSize = new Vector2(50, 50);
			_incrementButton.AddThemeFontSizeOverride("font_size", 24);
			TopMenuBar.ApplyStandardButtonStyle(_incrementButton);
			_incrementButton.Pressed += OnIncrementPressed;
			amountContainer.AddChild(_incrementButton);

			_statusLabel = new Label();
			_statusLabel.Text = string.Empty;
			_statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
			_statusLabel.AddThemeColorOverride("font_color", Colors.Orange);
			_statusLabel.AddThemeFontSizeOverride("font_size", 12);
			_statusLabel.CustomMinimumSize = new Vector2(0, 20);
			contentContainer.AddChild(_statusLabel);

			var separator2 = new HSeparator();
			contentContainer.AddChild(separator2);

			var buttonContainer = new HBoxContainer();
			buttonContainer.Alignment = BoxContainer.AlignmentMode.Center;
			buttonContainer.AddThemeConstantOverride("separation", 15);
			contentContainer.AddChild(buttonContainer);

			_cancelButton = new Button();
			_cancelButton.Text = "Cancel";
			_cancelButton.CustomMinimumSize = new Vector2(120, 40);
			TopMenuBar.ApplyStandardButtonStyle(_cancelButton);
			_cancelButton.Pressed += OnCancelPressed;
			buttonContainer.AddChild(_cancelButton);

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
			_targetPhoneNumber = phoneNumber;
			_targetSlotIndex = slotIndex;
			_playerName = playerName;
			_availableCredits = availableCredits;
			_selectedAmount = MIN_TRANSFER_AMOUNT;

			_playerNameLabel.Text = $"Player: {_playerName}";
			_availableCreditsLabel.Text = $"Available: {_availableCredits:N0} Credits";
			_amountLabel.Text = _selectedAmount.ToString("N0");
			_statusLabel.Text = string.Empty;

			UpdateButtonStates();

			_completionSource = new TaskCompletionSource<int?>();

			Visible = true;

			return await _completionSource.Task;
		}

		private void HideModal()
		{
			Visible = false;
		}

		private void UpdateButtonStates()
		{
			_amountLabel.Text = _selectedAmount.ToString("N0");

			_decrementButton.Disabled = _selectedAmount <= MIN_TRANSFER_AMOUNT;

			// Increment button: disabled at maximum (min of MAX_TRANSFER or available credits)
			int maxAmount = Math.Min(MAX_TRANSFER_PER_TRANSACTION, _availableCredits);
			_incrementButton.Disabled = _selectedAmount >= maxAmount;

			_confirmButton.Text = $"Transfer {_selectedAmount:N0} Credits";
			_confirmButton.Disabled = _selectedAmount > _availableCredits || _selectedAmount < MIN_TRANSFER_AMOUNT;

			if (_selectedAmount >= MAX_TRANSFER_PER_TRANSACTION && _availableCredits > MAX_TRANSFER_PER_TRANSACTION)
			{
				_statusLabel.Text = $"Maximum {MAX_TRANSFER_PER_TRANSACTION:N0} credits per transfer";
				_statusLabel.Modulate = Colors.Orange;
			}
			else if (_selectedAmount >= _availableCredits && _availableCredits < MAX_TRANSFER_PER_TRANSACTION)
			{
				_statusLabel.Text = "All available credits selected";
				_statusLabel.Modulate = Colors.Yellow;
			}
			else
			{
				_statusLabel.Text = string.Empty;
			}
		}

		private void OnDecrementPressed()
		{
			if (_selectedAmount > MIN_TRANSFER_AMOUNT)
			{
				_selectedAmount -= 1000;
				UpdateButtonStates();
			}
		}

		private void OnIncrementPressed()
		{
			int maxAmount = Math.Min(MAX_TRANSFER_PER_TRANSACTION, _availableCredits);
			if (_selectedAmount + 1000 <= maxAmount)
			{
				_selectedAmount += 1000;
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

			_completionSource?.SetResult(_selectedAmount);
			HideModal();
		}

		private void OnCancelPressed()
		{
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
		_creditService = CreditService.GetInstance();

		_creditTransferModal = new CreditTransferModal();
		AddChild(_creditTransferModal);

		// Connect to SessionManager signals for real-time updates
		if (_sessionManager != null)
		{
			_sessionManager.UserLoggedIn += OnUserLoggedIn;
			_sessionManager.UserLoggedOut += OnUserLoggedOut;
		}

		// Connect to CreditService for credit change notifications
		if (_creditService != null)
		{
			_creditService.CreditsChanged += OnCreditsChanged;
		}

		Visible = false;
	}

	public void ShowMenu(int playerCount, int costPerGame)
	{
		_playerCount = playerCount;
		_costPerGame = costPerGame;

		// DON'T clear state - preserve between popup open/close cycles
		if (_modalPanel == null)
		{
			CreateModalUI();
		}

		UpdateUI();

		RestoreMachineCreditsFromBackendAsync();

		RestoreOccupiedSlots();

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
		{
			return [];
		}

		var playerIds = new List<System.Guid>();

		for (int i = 0; i < _playerCount; i++)
		{
			if (_slotToPhoneNumber.TryGetValue(i, out var phoneNumber))
			{
				var session = _sessionManager.GetSessionByPhone(phoneNumber);
				if (session != null)
				{
					// Generate player ID from phone number (deterministic UUID)
					var playerId = SessionManager.GetPlayerIdFromPhone(phoneNumber);
					playerIds.Add(playerId);
				}
			}
		}

		return [.. playerIds];
	}

	private void CreateModalUI()
	{
		_modalBackground = new Panel();
		_modalBackground.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		var backgroundStyle = new StyleBoxFlat();
		backgroundStyle.BgColor = new Color(0.0f, 0.0f, 0.0f, 0.7f);
		_modalBackground.AddThemeStyleboxOverride("panel", backgroundStyle);

		// Click background to cancel (optional - can be removed if you want to force player interaction)
		_modalBackground.GuiInput += OnBackgroundInput;

		AddChild(_modalBackground);

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

		_titleLabel = new Label();
		_titleLabel.Text = "Setup Competitive Match";
		_titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_titleLabel.AddThemeColorOverride("font_color", Colors.White);
		_titleLabel.AddThemeFontSizeOverride("font_size", 22);
		_contentContainer.AddChild(_titleLabel);

		CreatePlayerSlotsSection();

		CreateTableCreditsSection();

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

		_exitButton = new Button();
		_exitButton.Text = "Exit";
		_exitButton.CustomMinimumSize = new Vector2(120, 45);
		TopMenuBar.ApplyStandardButtonStyle(_exitButton);
		_exitButton.Pressed += OnExitPressed;
		_actionButtonsContainer.AddChild(_exitButton);

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
		_titleLabel.Text = $"Setup {_playerCount}-Player Match";

		// Update modal panel height based on player count
		// 2-player: 500px (1x2 grid) | 4-player: 650px (2x2 grid needs more space)
		float panelHeight = _playerCount == 2 ? 500f : 650f;
		if (_modalPanel != null)
		{
			_modalPanel.CustomMinimumSize = new Vector2(600, panelHeight);
			_modalPanel.SetSize(new Vector2(600, panelHeight));
		}

		_costLabel.Text = $"Cost: {_costPerGame} Credit{(_costPerGame > 1 ? "s" : string.Empty)}";

		foreach (var slot in _playerSlots)
		{
			slot.QueueFree();
		}

		_playerSlots.Clear();

		for (int i = 0; i < _playerCount; i++)
		{
			var slot = new PlayerSlotUI(i);

			var pieceColor = GetPieceColorForSlot(i, _playerCount);
			string colorText = pieceColor == PieceType.White ? "⚪ White Pieces" : "⚫ Black Pieces";
			slot.PieceColorLabel.Text = colorText;

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
		else
		{
			return (slotIndex == 0 || slotIndex == 2) ? PieceType.White : PieceType.Black;
		}
	}

	private async void AutoPopulateFirstSlot()
	{
		if (_sessionManager == null)
		{
			return;
		}

		if (_slotToPhoneNumber.ContainsKey(0))
		{
			return;
		}

		// Auto-populate first slot with primary user (UI convenience for local multiplayer)
		var currentSession = _sessionManager.GetPrimarySession();
		if (currentSession != null)
		{
			var phoneNumber = currentSession.PhoneNumber;

			int existingSlot = -1;
			foreach (var kvp in _slotToPhoneNumber)
			{
				if (kvp.Value == phoneNumber)
				{
					existingSlot = kvp.Key;
					break;
				}
			}

			if (existingSlot >= 0 && existingSlot != 0)
			{
				_slotToPhoneNumber.Remove(existingSlot);

				if (existingSlot < _playerSlots.Count)
				{
					_playerSlots[existingSlot].SetEmpty();
				}

				GD.Print($"[CarromPlayerSetupMenu] Moved primary user from slot {existingSlot} to slot 0");
			}

			_slotToPhoneNumber[0] = phoneNumber;

			// Only initialize contribution to 0 if not already set (respect loaded data)
			if (!_creditsTransferredByPlayer.ContainsKey(currentSession.PhoneNumber))
			{
				_creditsTransferredByPlayer[currentSession.PhoneNumber] = 0;
			}

			// Fetch credits from CreditService (single source of truth)
			int credits = 0;
			if (_creditService != null)
			{
				var balanceResult = await _creditService.GetBalanceAsync(currentSession.PlayerId);
				if (balanceResult.IsSuccess(out var balance))
				{
					credits = balance;
				}
			}

			var slot = _playerSlots[0];
			slot.SetOccupied(currentSession.UserName, credits);
		}
	}

	/// <summary>
	/// Restore occupied slot UI from persisted slot assignments
	/// Clears slots for users who are no longer logged in
	/// </summary>
	private async void RestoreOccupiedSlots()
	{
		if (_sessionManager == null)
		{
			return;
		}

		var slotsToRemove = new List<(int slotIndex, string phoneNumber)>();

		foreach (var kvp in _slotToPhoneNumber)
		{
			int slotIndex = kvp.Key;
			string phoneNumber = kvp.Value;

			if (slotIndex < 0 || slotIndex >= _playerSlots.Count)
			{
				continue;
			}

			var session = _sessionManager.GetSessionByPhone(phoneNumber);
			if (session != null)
			{
				// Fetch credits from CreditService (single source of truth)
				int credits = 0;
				if (_creditService != null)
				{
					var balanceResult = await _creditService.GetBalanceAsync(session.PlayerId);
					if (balanceResult.IsSuccess(out var balance))
					{
						credits = balance;
					}
				}

				var slot = _playerSlots[slotIndex];
				slot.SetOccupied(session.UserName, credits);
			}
			else
			{
				slotsToRemove.Add((slotIndex, phoneNumber));
			}
		}

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
		_tableCreditsAmountLabel.Text = $"{_tableCredits} Credit{(_tableCredits != 1 ? "s" : string.Empty)}";

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
		if (_slotToPhoneNumber.Count != _playerCount)
		{
			return false;
		}

		foreach (var kvp in _slotToPhoneNumber)
		{
			var session = _sessionManager?.GetSessionByPhone(kvp.Value);
			if (session == null)
			{
				return false;
			}
		}

		return true;
	}

	private void UpdateStartGameButton()
	{
		bool hasCredits = _tableCredits >= _costPerGame;

		bool slotsFilledCorrectly = ValidatePlayerSlots();

		bool canStart = hasCredits && slotsFilledCorrectly;
		_startGameButton.Disabled = !canStart;

		if (!hasCredits)
		{
			_startGameButton.Text = $"Need {_costPerGame - _tableCredits} More Credits";
		}
		else if (!slotsFilledCorrectly)
		{
			int needed = _playerCount - _slotToPhoneNumber.Count;
			_startGameButton.Text = $"Need {needed} More Player{(needed > 1 ? "s" : string.Empty)}";
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
		if (_uiManager == null)
		{
			return;
		}

		_pendingLoginSlot = slotIndex;

		_uiManager.ShowLoginModal();
	}

	/// <summary>
	/// Handle SessionManager's UserLoggedIn signal
	/// Falls back to the first empty slot if _pendingLoginSlot is invalid
	/// </summary>
	private async void OnUserLoggedIn(string phoneNumber)
	{
		if (_sessionManager == null)
		{
			return;
		}

		var session = _sessionManager.GetSessionByPhone(phoneNumber);
		if (session == null)
		{
			return;
		}

		if (_slotToPhoneNumber.Values.Contains(phoneNumber))
		{
			GD.Print($"User {phoneNumber} already in a slot");
			_pendingLoginSlot = -1;
			return;
		}

		int slotIndex = _pendingLoginSlot;

		if (slotIndex < 0 || slotIndex >= _playerSlots.Count)
		{
			GD.Print($"Invalid pending login slot: {_pendingLoginSlot}, searching for empty slot");

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

			if (slotIndex < 0)
			{
				GD.PrintErr($"No empty slots available for {phoneNumber}");
				_pendingLoginSlot = -1;
				return;
			}
		}

		_slotToPhoneNumber[slotIndex] = phoneNumber;
		_creditsTransferredByPlayer[phoneNumber] = 0;

		if (slotIndex >= 0 && slotIndex < _playerSlots.Count)
		{
			// Fetch credits from CreditService (single source of truth)
			int credits = 0;
			if (_creditService != null)
			{
				var balanceResult = await _creditService.GetBalanceAsync(session.PlayerId);
				if (balanceResult.IsSuccess(out var balance))
				{
					credits = balance;
				}
			}

			var slot = _playerSlots[slotIndex];
			slot.SetOccupied(session.UserName, credits);
			GD.Print($"Player {session.UserName} assigned to slot {slotIndex}");
		}

		_pendingLoginSlot = -1;

		UpdateStartGameButton();
	}

	/// <summary>
	/// Handle user logout - clear their slot immediately
	/// </summary>
	private void OnUserLoggedOut(string phoneNumber)
	{
		int slotIndex = -1;
		foreach (var kvp in _slotToPhoneNumber)
		{
			if (kvp.Value == phoneNumber)
			{
				slotIndex = kvp.Key;
				break;
			}
		}

		if (slotIndex >= 0 && slotIndex < _playerSlots.Count)
		{
			_slotToPhoneNumber.Remove(slotIndex);
			_creditsTransferredByPlayer.Remove(phoneNumber);

			var slot = _playerSlots[slotIndex];
			slot.SetEmpty();

			GD.Print($"[CarromPlayerSetupMenu] User {phoneNumber} logged out - cleared slot {slotIndex}");

			UpdateStartGameButton();
		}
	}

	private void OnCreditsChanged(string playerId, int newBalance)
	{
		// playerId is a GUID string, not a phone number
		if (!Guid.TryParse(playerId, out var playerGuid))
		{
			return;
		}

		foreach (var kvp in _slotToPhoneNumber)
		{
			var slotPhoneNumber = kvp.Value;
			var slotPlayerId = SessionManager.GetPlayerIdFromPhone(slotPhoneNumber);
			if (slotPlayerId == playerGuid)
			{
				UpdatePlayerSlotCreditsAsync(kvp.Key, newBalance);
				break;
			}
		}
	}

	private void UpdatePlayerSlotCreditsAsync(int slotIndex, int newBalance)
	{
		if (slotIndex >= 0 && slotIndex < _playerSlots.Count)
		{
			_playerSlots[slotIndex].UpdateCreditsDisplay(newBalance);
		}
	}

	private async void UpdatePlayerSlotCredits(string phoneNumber)
	{
		foreach (var kvp in _slotToPhoneNumber)
		{
			if (kvp.Value == phoneNumber)
			{
				if (kvp.Key >= _playerSlots.Count)
				{
					break;
				}

				// Fetch credits from CreditService (single source of truth)
				var playerId = SessionManager.GetPlayerIdFromPhone(phoneNumber);
				if (_creditService != null)
				{
					var balanceResult = await _creditService.GetBalanceAsync(playerId);
					if (balanceResult.IsSuccess(out var balance))
					{
						_playerSlots[kvp.Key].UpdateCreditsDisplay(balance);
					}
				}

				break;
			}
		}
	}

	private async void OnLogoutButtonPressed(int slotIndex)
	{
		if (_sessionManager == null)
		{
			return;
		}

		if (!_slotToPhoneNumber.TryGetValue(slotIndex, out var phoneNumber))
		{
			return;
		}

		if (_creditsTransferredByPlayer.TryGetValue(phoneNumber, out var transferredAmount))
		{
			if (transferredAmount > 0)
			{
				var session = _sessionManager.GetSessionByPhone(phoneNumber);
				if (session != null && _creditService != null)
				{
					var addResult = await _creditService.AddAsync(session.PlayerId, transferredAmount, "Returned from table");
					if (addResult.IsSuccess(out var newBalance))
					{
						_tableCredits -= transferredAmount;

						// Event-sourced - backend handles machine credits via events
					}
				}
			}
		}

		_slotToPhoneNumber.Remove(slotIndex);
		_creditsTransferredByPlayer.Remove(phoneNumber);

		var slot = _playerSlots[slotIndex];
		slot.SetEmpty();

		var currentSession = _sessionManager.GetPrimarySession();
		if (currentSession != null && currentSession.PhoneNumber == phoneNumber)
		{
			await _sessionManager.LogoutUserAsync(phoneNumber);
		}

		UpdateTableCreditsDisplay();
		UpdateStartGameButton();
	}

	private void OnBuyCreditsButtonPressed(int slotIndex)
	{
		if (_uiManager == null)
		{
			return;
		}

		if (!_slotToPhoneNumber.TryGetValue(slotIndex, out var phoneNumber))
		{
			return;
		}

		// Show buy credits modal for this specific user (explicit user targeting)
		_uiManager.ShowBuyCreditsModal(phoneNumber);

		// Wait for purchase completion and update display
		RefreshSlotCreditsDisplay(slotIndex);
	}

	private async void RefreshSlotCreditsDisplay(int slotIndex)
	{
		if (_sessionManager == null)
		{
			return;
		}

		if (!_slotToPhoneNumber.TryGetValue(slotIndex, out var phoneNumber))
		{
			return;
		}

		// Wait a moment for purchase to process
		await ToSignal(GetTree().CreateTimer(1.0f), Timer.SignalName.Timeout);

		// Refresh credits display from CreditService (single source of truth)
		var session = _sessionManager.GetSessionByPhone(phoneNumber);
		if (session != null && _creditService != null)
		{
			var balanceResult = await _creditService.GetBalanceAsync(session.PlayerId, forceRefresh: true);
			if (balanceResult.IsSuccess(out var balance))
			{
				var slot = _playerSlots[slotIndex];
				slot.UpdateCreditsDisplay(balance);
			}
		}
	}

	private async void OnTransferCreditButtonPressed(int slotIndex)
	{
		try
		{
			if (_sessionManager == null)
			{
				return;
			}

			if (!_slotToPhoneNumber.TryGetValue(slotIndex, out var phoneNumber))
			{
				return;
			}

			var session = _sessionManager.GetSessionByPhone(phoneNumber);
			if (session == null)
			{
				return;
			}

			// Fetch credits from CreditService (single source of truth)
			int availableCredits = 0;
			if (_creditService != null)
			{
				var balanceResult = await _creditService.GetBalanceAsync(session.PlayerId);
				if (balanceResult.IsSuccess(out var balance))
				{
					availableCredits = balance;
				}
			}

			if (availableCredits < 1)
			{
				GD.Print($"Player {phoneNumber} has insufficient credits");
				return;
			}

			var selectedAmount = await _creditTransferModal.ShowAsync(
				phoneNumber,
				slotIndex,
				session.UserName,
				availableCredits);

			if (!selectedAmount.HasValue || selectedAmount.Value < 1)
			{
				return;
			}

			// Transfer credits: player credits → machine credits (via CreditService)
			if (_creditService == null)
			{
				GD.PrintErr("Required services not available for credit transfer");
				return;
			}

			// Spend from player account and deposit to the machine pot in one
			// step, rolling back the spend if the deposit fails partway through.
			if (session.LobbySessionId != Guid.Empty)
			{
				var locationManager = LocationManager.GetAutoload();
				if (locationManager == null)
				{
					GD.PrintErr("LocationManager not available for credit transfer");
					return;
				}

				var boxId = locationManager.BoxId;
				var transferResult = await _creditService.TransferToMachineAsync(
					"carrom",  // Use game tag (not game ID) to match backend queries
					boxId,
					session.PlayerId,
					selectedAmount.Value,
					session.LobbySessionId,
					"Transfer to Carrom table");

				// Validate scene still exists after async operation
				if (!IsInstanceValid(this))
				{
					return;
				}

				if (transferResult.IsFailure(out var transferError))
				{
					GD.PrintErr($"Credit transfer failed: {transferError.Message}");
					return;
				}
			}
			else
			{
				// No lobby session (e.g. standalone/dev) - nothing to deposit into, spend only
				var spendResult = await _creditService.SpendAsync(
					session.PlayerId,
					selectedAmount.Value,
					"Transfer to Carrom table");

				if (!IsInstanceValid(this))
				{
					return;
				}

				if (spendResult.IsFailure(out var spendError))
				{
					GD.PrintErr($"Failed to spend credits: {spendError.Message}");
					return;
				}
			}

			// Event-sourced - backend tracks machine credits
			_tableCredits += selectedAmount.Value;

			_creditsTransferredByPlayer.TryAdd(phoneNumber, 0);
			_creditsTransferredByPlayer[phoneNumber] += selectedAmount.Value;

			// Update displays - fetch from CreditService (single source of truth)
			// CreditsChanged signal will also fire, but explicit update ensures immediate UI feedback
			if (_creditService != null)
			{
				var balanceResult = await _creditService.GetBalanceAsync(session.PlayerId);
				if (balanceResult.IsSuccess(out var balance))
				{
					var slot = _playerSlots[slotIndex];
					slot.UpdateCreditsDisplay(balance);
				}
			}

			UpdateTableCreditsDisplay();
			UpdateStartGameButton();
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"Credit transfer failed: {ex.Message}");
		}
	}

	private void OnExitPressed()
	{
		HideMenu();
		EmitSignal(SignalName.MenuCancelled);
	}

	private async void OnStartGamePressed()
	{
		if (_tableCredits < _costPerGame)
		{
			return;
		}

		if (!ValidatePlayerSlots())
		{
			int needed = _playerCount - _slotToPhoneNumber.Count;
			GD.PrintErr($"[CarromPlayerSetupMenu] Cannot start - need {needed} more player(s) to fill all slots");
			return;
		}

		// Consume credits from machine pot (backend tracking)
		var locationManager = LocationManager.GetAutoload();

		if (_creditService != null && locationManager != null)
		{
			var boxId = locationManager.BoxId;

			// TODO: Get game session ID from CarromGame when starting
			// For now, use a temporary session ID - will be replaced with actual game session
			var tempGameSessionId = Guid.NewGuid();

			var consumeResult = await _creditService.ConsumeMachineCreditsAsync(
				"carrom",  // Use game tag to match backend queries
				boxId,
				_costPerGame,
				tempGameSessionId);

			if (consumeResult.IsSuccess(out var machineState))
			{
				_tableCredits = machineState.Balance;

				HideMenu();
				EmitSignal(SignalName.GameStartRequested);

				// Preserve slot assignments for quick rematches
				// Signal handlers can read _slotToPhoneNumber for session setup
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

			// Preserve slot assignments for quick rematches
			// Signal handlers can read _slotToPhoneNumber for session setup
		}
	}

	private async Task ReturnAllCreditsToPlayers()
	{
		if (_sessionManager == null)
		{
			return;
		}

		int totalReturned = 0;

		foreach (var kvp in _creditsTransferredByPlayer)
		{
			var phoneNumber = kvp.Key;
			var amount = kvp.Value;

			if (amount > 0)
			{
				var session = _sessionManager?.GetSessionByPhone(phoneNumber);
				if (session != null && _creditService != null)
				{
					var addResult = await _creditService.AddAsync(session.PlayerId, amount, "Returned from table");
					if (addResult.IsSuccess(out var newBalance))
					{
						totalReturned += amount;
					}
				}
			}
		}

		// Event-sourced - backend tracks machine credits
		_tableCredits = 0;
		_creditsTransferredByPlayer.Clear();

		await SavePlayerContributionsToMachineData();
	}

	/// <summary>
	/// Cleanup all player sessions when exiting the game
	/// Logs out secondary players (non-primary sessions) without returning credits
	/// Credits transferred to the table are permanent and remain in machine pool
	/// </summary>
	private async Task CleanupAllPlayerSessions()
	{
		if (_sessionManager == null)
		{
			return;
		}

		// Get primary session to avoid logging it out (primary user manages their own session lifecycle)
		var primarySession = _sessionManager.GetPrimarySession();
		string primaryPhoneNumber = primarySession?.PhoneNumber;

		// Process each slot - use ToList to avoid modification during iteration
		foreach (var kvp in _slotToPhoneNumber.ToList())
		{
			string phoneNumber = kvp.Value;

			// Credits transferred to table are PERMANENT and are NOT returned
			if (!string.IsNullOrEmpty(phoneNumber) && phoneNumber != primaryPhoneNumber)
			{
				await _sessionManager.LogoutUserAsync(phoneNumber);
				GD.Print($"[CarromPlayerSetupMenu] Logged out secondary player: {phoneNumber}");
			}
		}

		// Credits remain in machine pool permanently
		_slotToPhoneNumber.Clear();
		_creditsTransferredByPlayer.Clear();

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
			if (_creditService == null)
			{
				GD.Print("[CarromPlayerSetupMenu] CreditService not available - using local state only");
				return;
			}

			var locationManager = LocationManager.GetAutoload();
			if (locationManager == null)
			{
				GD.Print("[CarromPlayerSetupMenu] LocationManager not available");
				return;
			}

			var boxId = locationManager.BoxId;
			var result = await _creditService.GetMachineCreditsAsync("carrom", boxId);

			if (result.IsSuccess(out var machineCredits))
			{
				_tableCredits = machineCredits.Balance;

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
							var activePhones = sessionManager.GetActivePhoneNumbers();
							foreach (var phone in activePhones)
							{
								var session = sessionManager.GetSessionByPhone(phone);
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

	private async Task SavePlayerContributionsToMachineData()
	{
		// No-op: deposit is handled directly in OnTransferCreditButtonPressed; kept so existing call sites still compile
		await Task.CompletedTask;
		return;
	}

	public override void _ExitTree()
	{
		// Only clear local state to prevent stale references
		_slotToPhoneNumber.Clear();
		_creditsTransferredByPlayer.Clear();

		if (GodotObject.IsInstanceValid(_sessionManager))
		{
			_sessionManager.UserLoggedIn -= OnUserLoggedIn;
			_sessionManager.UserLoggedOut -= OnUserLoggedOut;
		}

		if (GodotObject.IsInstanceValid(_creditService))
		{
			_creditService.CreditsChanged -= OnCreditsChanged;
		}

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
