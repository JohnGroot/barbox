using Godot;
using BarBox.Core.Autoloads;
using System;
using System.Threading.Tasks;

/// <summary>
/// Modal dialog for purchasing credit packs
/// Provides credit pack selection and purchase confirmation functionality
/// Supports QR code display for Stripe mobile payments
/// </summary>
public partial class BuyCreditsModal : Control
{
	[Signal] public delegate void CreditsAcquiredEventHandler(string userId, int amount);
	[Signal] public delegate void ModalClosedEventHandler();

	private const string TITLE_TEXT = "Buy Credits";
	private const string CURRENT_CREDITS_FORMAT = "Current Credits: {0:N0}";
	private const string SELECT_PACK_TEXT = "Select Credit Pack:";
	private const string CLOSE_TEXT = "Close";
	private const string CANCEL_TEXT = "Cancel";
	private const string PAYMENT_NOT_AVAILABLE = "Payment service not available";
	private const string USER_INVALID = "Invalid user specified";
	private const string SESSION_NOT_FOUND = "User session not found";
	private const string NO_USER_SPECIFIED = "No user specified";
	private const string PROCESSING_PURCHASE = "Processing purchase...";
	private const string PURCHASE_CONFIRMATION_FORMAT = "Purchase {0}?";
	private const string PURCHASE_SUCCESS_FORMAT = "Purchase successful! {0:N0} credits added.";
	private const string PURCHASE_FAILED_FORMAT = "Purchase failed: {0}";
	private const string ERROR_FORMAT = "Error: {0}";
	private const string QR_SCAN_TEXT = "Scan to pay ${0:F2}";
	private const string USER_INFO_FORMAT = "{0} - {1:N0} credits";
	private const string PAYMENT_TIMEOUT_TEXT = "Payment timed out";
	private const float POLL_TIMEOUT = 120.0f;

	/// <summary>QR code display size in pixels (80% larger than original 250x250)</summary>
	public const float QR_CODE_SIZE = 450.0f;

	private Panel _modalBackground;
	private Panel _modalPanel;
	private VBoxContainer _contentContainer;
	private Label _titleLabel;
	private Label _currentCreditsLabel;
	private GridContainer _creditPackContainer;
	private Button _closeButton;
	private Label _statusLabel;

	// QR code view elements
	private VBoxContainer _qrContainer;
	private TextureRect _qrCodeTexture;
	private Label _qrInstructionLabel;
	private Label _qrUserInfoLabel;
	private ProgressBar _timeoutProgressBar;
	private Label _timeoutMessageLabel;
	private Button _qrCancelButton;
	private bool _showingQRCode;
	private ulong _qrViewStartTime;
	private string _currentUsername;
	private int _currentBalance;

	private PaymentService _paymentService;
	private SessionManager _sessionManager;
	private CreditService _creditService;
	private string _targetPhoneNumber;
	private CreditPack _pendingPurchasePack;

	public override void _Ready()
	{
		_paymentService = PaymentService.GetInstance();
		_sessionManager = SessionManager.GetInstance();
		_creditService = CreditService.GetInstance();

		SetupModalLayout();
		CreateModalUI();

		// Defer signal connections to ensure all services are fully initialized
		CallDeferred(nameof(ConnectSignals));

		Visible = false;
	}

	public override void _Process(double delta)
	{
		if (!_showingQRCode || _timeoutProgressBar == null)
			return;

		var elapsedSeconds = (Time.GetTicksMsec() - _qrViewStartTime) / 1000.0f;
		var remainingSeconds = POLL_TIMEOUT - elapsedSeconds;
		_timeoutProgressBar.Value = Math.Max(0, remainingSeconds);
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
		_modalPanel.CustomMinimumSize = new Vector2(900, 810);
		_modalPanel.SetSize(new Vector2(900, 810));

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

		_contentContainer = new VBoxContainer();
		_contentContainer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_contentContainer.AddThemeConstantOverride("separation", 15);

		var margin = new MarginContainer();
		margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		margin.AddThemeConstantOverride("margin_left", 36);
		margin.AddThemeConstantOverride("margin_right", 36);
		margin.AddThemeConstantOverride("margin_top", 36);
		margin.AddThemeConstantOverride("margin_bottom", 36);

		_modalPanel.AddChild(margin);
		margin.AddChild(_contentContainer);

		_titleLabel = new Label();
		_titleLabel.Text = TITLE_TEXT;
		_titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_titleLabel.AddThemeColorOverride("font_color", Colors.White);
		_titleLabel.AddThemeFontSizeOverride("font_size", 43);
		_contentContainer.AddChild(_titleLabel);

		_currentCreditsLabel = new Label();
		_currentCreditsLabel.Text = string.Format(CURRENT_CREDITS_FORMAT, 0);
		_currentCreditsLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_currentCreditsLabel.AddThemeColorOverride("font_color", Colors.LightGray);
		_currentCreditsLabel.AddThemeFontSizeOverride("font_size", 29);
		_contentContainer.AddChild(_currentCreditsLabel);

		var packLabel = new Label();
		packLabel.Text = SELECT_PACK_TEXT;
		packLabel.AddThemeColorOverride("font_color", Colors.White);
		packLabel.AddThemeFontSizeOverride("font_size", 32);
		_contentContainer.AddChild(packLabel);

		_creditPackContainer = new GridContainer();
		_creditPackContainer.Columns = 2;
		_creditPackContainer.AddThemeConstantOverride("h_separation", 10);
		_creditPackContainer.AddThemeConstantOverride("v_separation", 10);
		_contentContainer.AddChild(_creditPackContainer);

		var buttonContainer = new HBoxContainer();
		buttonContainer.Alignment = BoxContainer.AlignmentMode.Center;
		_contentContainer.AddChild(buttonContainer);

		_closeButton = new Button();
		_closeButton.Text = CLOSE_TEXT;
		_closeButton.CustomMinimumSize = new Vector2(216, 72);
		TopMenuBar.ApplyStandardButtonStyle(_closeButton);
		buttonContainer.AddChild(_closeButton);

		_statusLabel = new Label();
		_statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_statusLabel.AddThemeColorOverride("font_color", Colors.White);
		_statusLabel.CustomMinimumSize = new Vector2(0, 30);
		_contentContainer.AddChild(_statusLabel);

		// Create QR code view (initially hidden)
		CreateQRCodeView();
	}

	private void CreateQRCodeView()
	{
		_qrContainer = new VBoxContainer();
		_qrContainer.Visible = false;
		_qrContainer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_qrContainer.AddThemeConstantOverride("separation", 15);

		var qrMargin = new MarginContainer();
		qrMargin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		qrMargin.MouseFilter = Control.MouseFilterEnum.Ignore;
		qrMargin.AddThemeConstantOverride("margin_left", 36);
		qrMargin.AddThemeConstantOverride("margin_right", 36);
		qrMargin.AddThemeConstantOverride("margin_top", 36);
		qrMargin.AddThemeConstantOverride("margin_bottom", 36);
		_modalPanel.AddChild(qrMargin);
		qrMargin.AddChild(_qrContainer);

		// Instruction label
		_qrInstructionLabel = new Label();
		_qrInstructionLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_qrInstructionLabel.AddThemeColorOverride("font_color", Colors.White);
		_qrInstructionLabel.AddThemeFontSizeOverride("font_size", 36);
		_qrContainer.AddChild(_qrInstructionLabel);

		// User info label (username - credits)
		_qrUserInfoLabel = new Label();
		_qrUserInfoLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_qrUserInfoLabel.AddThemeColorOverride("font_color", Colors.LightGray);
		_qrUserInfoLabel.AddThemeFontSizeOverride("font_size", 24);
		_qrContainer.AddChild(_qrUserInfoLabel);

		// QR code texture
		var qrCenter = new CenterContainer();
		_qrCodeTexture = new TextureRect();
		_qrCodeTexture.CustomMinimumSize = new Vector2(QR_CODE_SIZE, QR_CODE_SIZE);
		_qrCodeTexture.ExpandMode = TextureRect.ExpandModeEnum.FitWidthProportional;
		_qrCodeTexture.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		qrCenter.AddChild(_qrCodeTexture);
		_qrContainer.AddChild(qrCenter);

		// Cancel button
		var qrButtonContainer = new HBoxContainer();
		qrButtonContainer.Alignment = BoxContainer.AlignmentMode.Center;
		_qrContainer.AddChild(qrButtonContainer);

		_qrCancelButton = new Button();
		_qrCancelButton.Text = CANCEL_TEXT;
		_qrCancelButton.CustomMinimumSize = new Vector2(216, 72);
		TopMenuBar.ApplyStandardButtonStyle(_qrCancelButton);
		_qrCancelButton.Pressed += OnQRCancelPressed;
		qrButtonContainer.AddChild(_qrCancelButton);

		// Progress bar container at bottom
		var progressBarContainer = new VBoxContainer();
		progressBarContainer.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;

		_timeoutMessageLabel = new Label();
		_timeoutMessageLabel.Text = "";
		_timeoutMessageLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_timeoutMessageLabel.AddThemeColorOverride("font_color", Colors.Red);
		_timeoutMessageLabel.AddThemeFontSizeOverride("font_size", 24);
		_timeoutMessageLabel.Visible = false;
		progressBarContainer.AddChild(_timeoutMessageLabel);

		_timeoutProgressBar = new ProgressBar();
		_timeoutProgressBar.MinValue = 0;
		_timeoutProgressBar.MaxValue = POLL_TIMEOUT;
		_timeoutProgressBar.Value = POLL_TIMEOUT;
		_timeoutProgressBar.CustomMinimumSize = new Vector2(0, 8);
		_timeoutProgressBar.ShowPercentage = false;

		// Style the progress bar fill (green draining bar)
		var fillStyle = new StyleBoxFlat();
		fillStyle.BgColor = new Color(0.3f, 0.7f, 0.3f, 1.0f);
		fillStyle.SetCornerRadiusAll(4);
		_timeoutProgressBar.AddThemeStyleboxOverride("fill", fillStyle);

		// Style the progress bar background
		var bgStyle = new StyleBoxFlat();
		bgStyle.BgColor = new Color(0.3f, 0.3f, 0.3f, 1.0f);
		bgStyle.SetCornerRadiusAll(4);
		_timeoutProgressBar.AddThemeStyleboxOverride("background", bgStyle);

		progressBarContainer.AddChild(_timeoutProgressBar);
		_qrContainer.AddChild(progressBarContainer);
	}

	private void CreateCreditPackButtons()
	{
		if (_paymentService == null)
		{
			ShowStatusMessage(PAYMENT_NOT_AVAILABLE, false);
			return;
		}

		var creditPacks = _paymentService.GetAvailableCreditPacks();

		foreach (var pack in creditPacks)
		{
			var button = new Button();
			button.Text = pack.DisplayName;
			button.CustomMinimumSize = new Vector2(360, 90);
			TopMenuBar.ApplyStandardButtonStyle(button);
			button.SetMeta("pack_index", Array.IndexOf(creditPacks, pack));
			button.Pressed += () => OnCreditPackSelected(pack);

			_creditPackContainer.AddChild(button);
		}
	}

	private void ConnectSignals()
	{
		if (_closeButton != null)
			_closeButton.Pressed += OnClosePressed;

		// Connect to Stripe events if available
		ConnectStripeEvents();
	}

	private void ConnectStripeEvents()
	{
		if (_paymentService?.StripeService == null)
			return;

		_paymentService.StripeService.OnQRCodeReady += OnStripeQRCodeReady;
		_paymentService.StripeService.OnPaymentTimeout += OnStripePaymentTimeout;
	}

	private void DisconnectStripeEvents()
	{
		if (_paymentService?.StripeService == null)
			return;

		_paymentService.StripeService.OnQRCodeReady -= OnStripeQRCodeReady;
		_paymentService.StripeService.OnPaymentTimeout -= OnStripePaymentTimeout;
	}

	private void OnStripeQRCodeReady(StripePaymentData data)
	{
		CallDeferred(nameof(ShowQRCodeViewDeferred), data.QRCodeTexture, (float)_pendingPurchasePack.Price);
	}

	private void OnStripePaymentTimeout()
	{
		CallDeferred(nameof(OnPaymentTimedOutDeferred));
	}

	// Deferred method wrappers for Godot's CallDeferred (which only accepts Variant params)
	private void ShowQRCodeViewDeferred(ImageTexture qrTexture, float price)
	{
		ShowQRCodeView(qrTexture, (decimal)price);
	}

	private void OnPaymentTimedOutDeferred()
	{
		OnPaymentTimedOut();
	}

	private void ShowQRCodeView(ImageTexture qrTexture, decimal price)
	{
		_showingQRCode = true;
		_qrViewStartTime = Time.GetTicksMsec();
		_contentContainer.Visible = false;
		_qrContainer.Visible = true;

		_qrCodeTexture.Texture = qrTexture;
		_qrInstructionLabel.Text = string.Format(QR_SCAN_TEXT, price);

		// Show user info in QR view
		if (_qrUserInfoLabel != null && !string.IsNullOrEmpty(_currentUsername))
		{
			_qrUserInfoLabel.Text = string.Format(USER_INFO_FORMAT, _currentUsername, _currentBalance);
		}

		// Reset progress bar
		if (_timeoutProgressBar != null)
		{
			_timeoutProgressBar.Value = POLL_TIMEOUT;
		}
		if (_timeoutMessageLabel != null)
		{
			_timeoutMessageLabel.Visible = false;
		}
	}

	private void HideQRCodeView()
	{
		_showingQRCode = false;
		_qrContainer.Visible = false;
		_contentContainer.Visible = true;

		_qrCodeTexture.Texture = null;

		// Reset progress bar state
		if (_timeoutProgressBar != null)
		{
			_timeoutProgressBar.Value = POLL_TIMEOUT;
		}
		if (_timeoutMessageLabel != null)
		{
			_timeoutMessageLabel.Visible = false;
		}
	}

	private async void OnPaymentTimedOut()
	{
		// Show timeout message above progress bar
		if (_timeoutMessageLabel != null)
		{
			_timeoutMessageLabel.Text = PAYMENT_TIMEOUT_TEXT;
			_timeoutMessageLabel.Visible = true;
		}

		// Wait 2 seconds, then auto-close
		await Task.Delay(2000);

		if (IsInsideTree())
		{
			HideQRCodeView();
			HideModal();
		}
	}

	private void OnQRCancelPressed()
	{
		_paymentService?.StripeService?.CancelPayment();
		HideQRCodeView();
		ClearStatusMessage();
	}

	public async void ShowModal(string phoneNumber)
	{
		if (string.IsNullOrEmpty(phoneNumber))
		{
			ShowStatusMessage(USER_INVALID, false);
			return;
		}

		var session = _sessionManager?.GetSessionByPhone(phoneNumber);
		if (session == null)
		{
			ShowStatusMessage(SESSION_NOT_FOUND, false);
			return;
		}

		_targetPhoneNumber = phoneNumber;
		_currentUsername = session.UserName;

		// Fetch credits from CreditService (single source of truth)
		_currentBalance = 0;
		if (_creditService != null)
		{
			var balanceResult = await _creditService.GetBalanceAsync(session.PlayerId);
			if (balanceResult.IsSuccess(out var balance))
			{
				_currentBalance = balance;
			}
		}
		UpdateCurrentCreditsDisplay(_currentBalance);

		// Clear and recreate credit pack buttons to ensure fresh data
		if (_creditPackContainer != null)
		{
			foreach (Node child in _creditPackContainer.GetChildren())
			{
				child.QueueFree();
			}
		}
		CreateCreditPackButtons();

		Visible = true;
		ClearStatusMessage();
	}

	public void HideModal()
	{
		Visible = false;
		ClearStatusMessage();

		// Cancel payment and hide QR view if showing
		if (_showingQRCode)
		{
			_paymentService?.StripeService?.CancelPayment();
			HideQRCodeView();
		}

		EmitSignal(SignalName.ModalClosed);
	}

	private void UpdateCurrentCreditsDisplay(int credits)
	{
		if (_currentCreditsLabel != null)
		{
			if (!string.IsNullOrEmpty(_currentUsername))
			{
				_currentCreditsLabel.Text = string.Format(USER_INFO_FORMAT, _currentUsername, credits);
			}
			else
			{
				_currentCreditsLabel.Text = string.Format(CURRENT_CREDITS_FORMAT, credits);
			}
		}
	}

	private void ShowStatusMessage(string message, bool isSuccess)
	{
		if (_statusLabel == null)
			return;

		_statusLabel.Text = message;
		_statusLabel.Modulate = isSuccess ? Colors.Green : Colors.Red;
	}

	private void ClearStatusMessage()
	{
		if (_statusLabel != null)
		{
			_statusLabel.Text = "";
		}
	}

	private void OnBackgroundInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
		{
			OnClosePressed();
		}
	}

	private async void OnCreditPackSelected(CreditPack creditPack)
	{
		if (string.IsNullOrEmpty(_targetPhoneNumber))
		{
			ShowStatusMessage(NO_USER_SPECIFIED, false);
			return;
		}

		if (_paymentService == null)
		{
			ShowStatusMessage(PAYMENT_NOT_AVAILABLE, false);
			return;
		}

		// Store pending pack for Stripe QR code events
		_pendingPurchasePack = creditPack;

		var confirmMessage = string.Format(PURCHASE_CONFIRMATION_FORMAT, creditPack.DisplayName);
		var confirmed = await ShowConfirmationDialog(confirmMessage);

		if (!confirmed)
			return;

		// Ensure Stripe events connected just-in-time (PaymentService fully initialized by now)
		if (_paymentService.IsUsingStripe)
		{
			DisconnectStripeEvents();
			ConnectStripeEvents();
		}

		// For Stripe: QR code view will be shown via OnStripeQRCodeReady event
		// For Debug: Show processing message immediately
		if (!_paymentService.IsUsingStripe)
		{
			ShowStatusMessage(PROCESSING_PURCHASE, true);
		}

		try
		{
			var playerId = EventService.GetPlayerIdFromPhone(_targetPhoneNumber);
			var result = await _paymentService.PurchaseCreditsAsync(playerId, creditPack);

			// Hide QR view (if showing) before showing result
			if (_showingQRCode)
			{
				HideQRCodeView();
			}

			if (result.IsSuccess)
			{
				ShowStatusMessage(string.Format(PURCHASE_SUCCESS_FORMAT, creditPack.TotalCredits), true);

				// Fetch updated credits from CreditService (single source of truth)
				if (_creditService != null)
				{
					var balanceResult = await _creditService.GetBalanceAsync(playerId, forceRefresh: true);
					if (balanceResult.IsSuccess(out var balance))
					{
						UpdateCurrentCreditsDisplay(balance);
					}
				}

				EmitSignal(SignalName.CreditsAcquired, _targetPhoneNumber, creditPack.TotalCredits);

				await DelayAndClose(2000);
			}
			else
			{
				ShowStatusMessage(string.Format(PURCHASE_FAILED_FORMAT, result.ErrorMessage), false);
			}
		}
		catch (Exception ex)
		{
			// Hide QR view on error
			if (_showingQRCode)
			{
				HideQRCodeView();
			}
			ShowStatusMessage(string.Format(ERROR_FORMAT, ex.Message), false);
		}
	}

	private async Task<bool> ShowConfirmationDialog(string message)
	{
		var confirmDialog = new AcceptDialog();
		confirmDialog.DialogText = message;
		confirmDialog.GetOkButton().Text = "Purchase";
		confirmDialog.AddCancelButton("Cancel");

		GetTree().Root.AddChild(confirmDialog);
		confirmDialog.PopupCentered();

		var result = await ToSignal(confirmDialog, AcceptDialog.SignalName.Confirmed);
		confirmDialog.QueueFree();

		return result != null;
	}

	private async Task DelayAndClose(int milliseconds)
	{
		await Task.Delay(milliseconds);
		if (IsInsideTree())
		{
			HideModal();
		}
	}

	private void OnClosePressed()
	{
		HideModal();
	}

	public override void _ExitTree()
	{
		// Disconnect Stripe events
		DisconnectStripeEvents();

		// Disconnect UI signals
		if (IsInstanceValid(_closeButton))
			_closeButton.Pressed -= OnClosePressed;

		if (IsInstanceValid(_qrCancelButton))
			_qrCancelButton.Pressed -= OnQRCancelPressed;

		base._ExitTree();
	}
}