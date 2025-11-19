using Godot;
using System;
using System.Threading.Tasks;

/// <summary>
/// Modal dialog for purchasing credit packs
/// Provides credit pack selection and purchase confirmation functionality
/// </summary>
public partial class BuyCreditsModal : Control
{
	[Signal] public delegate void CreditsAcquiredEventHandler(string userId, int amount);
	[Signal] public delegate void ModalClosedEventHandler();

	private const string TITLE_TEXT = "Buy Credits";
	private const string CURRENT_CREDITS_FORMAT = "Current Credits: {0}";
	private const string SELECT_PACK_TEXT = "Select Credit Pack:";
	private const string CLOSE_TEXT = "Close";
	private const string PAYMENT_NOT_AVAILABLE = "Payment service not available";
	private const string USER_INVALID = "Invalid user specified";
	private const string SESSION_NOT_FOUND = "User session not found";
	private const string NO_USER_SPECIFIED = "No user specified";
	private const string PROCESSING_PURCHASE = "Processing purchase...";
	private const string PURCHASE_CONFIRMATION_FORMAT = "Purchase {0}?";
	private const string PURCHASE_SUCCESS_FORMAT = "Purchase successful! {0} credits added.";
	private const string PURCHASE_FAILED_FORMAT = "Purchase failed: {0}";
	private const string ERROR_FORMAT = "Error: {0}";

	private Panel _modalBackground;
	private Panel _modalPanel;
	private VBoxContainer _contentContainer;
	private Label _titleLabel;
	private Label _currentCreditsLabel;
	private GridContainer _creditPackContainer;
	private Button _closeButton;
	private Label _statusLabel;

	private PaymentService _paymentService;
	private SessionManager _sessionManager;
	private string _targetPhoneNumber;

	public override void _Ready()
	{
		_paymentService = PaymentService.GetInstance();
		_sessionManager = SessionManager.GetInstance();

		SetupModalLayout();
		CreateModalUI();
		ConnectSignals();
		Visible = false;
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
		_modalPanel.CustomMinimumSize = new Vector2(500, 450);
		_modalPanel.SetSize(new Vector2(500, 450));

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
		margin.AddThemeConstantOverride("margin_left", 20);
		margin.AddThemeConstantOverride("margin_right", 20);
		margin.AddThemeConstantOverride("margin_top", 20);
		margin.AddThemeConstantOverride("margin_bottom", 20);

		_modalPanel.AddChild(margin);
		margin.AddChild(_contentContainer);

		_titleLabel = new Label();
		_titleLabel.Text = TITLE_TEXT;
		_titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_titleLabel.AddThemeColorOverride("font_color", Colors.White);
		_titleLabel.AddThemeFontSizeOverride("font_size", 24);
		_contentContainer.AddChild(_titleLabel);

		_currentCreditsLabel = new Label();
		_currentCreditsLabel.Text = string.Format(CURRENT_CREDITS_FORMAT, 0);
		_currentCreditsLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_currentCreditsLabel.AddThemeColorOverride("font_color", Colors.LightGray);
		_currentCreditsLabel.AddThemeFontSizeOverride("font_size", 16);
		_contentContainer.AddChild(_currentCreditsLabel);

		var packLabel = new Label();
		packLabel.Text = SELECT_PACK_TEXT;
		packLabel.AddThemeColorOverride("font_color", Colors.White);
		packLabel.AddThemeFontSizeOverride("font_size", 18);
		_contentContainer.AddChild(packLabel);

		_creditPackContainer = new GridContainer();
		_creditPackContainer.Columns = 2;
		_creditPackContainer.AddThemeConstantOverride("h_separation", 10);
		_creditPackContainer.AddThemeConstantOverride("v_separation", 10);
		_contentContainer.AddChild(_creditPackContainer);

		CreateCreditPackButtons();

		var buttonContainer = new HBoxContainer();
		buttonContainer.Alignment = BoxContainer.AlignmentMode.Center;
		_contentContainer.AddChild(buttonContainer);

		_closeButton = new Button();
		_closeButton.Text = CLOSE_TEXT;
		_closeButton.CustomMinimumSize = new Vector2(120, 40);
		TopMenuBar.ApplyStandardButtonStyle(_closeButton);
		buttonContainer.AddChild(_closeButton);

		_statusLabel = new Label();
		_statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_statusLabel.AddThemeColorOverride("font_color", Colors.White);
		_statusLabel.CustomMinimumSize = new Vector2(0, 30);
		_contentContainer.AddChild(_statusLabel);
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
			button.CustomMinimumSize = new Vector2(200, 50);
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
	}

	public void ShowModal(string phoneNumber)
	{
		if (string.IsNullOrEmpty(phoneNumber))
		{
			ShowStatusMessage(USER_INVALID, false);
			return;
		}

		var session = _sessionManager?.GetUserSession(phoneNumber);
		if (session == null)
		{
			ShowStatusMessage(SESSION_NOT_FOUND, false);
			return;
		}

		_targetPhoneNumber = phoneNumber;
		UpdateCurrentCreditsDisplay(session.Credits);

		Visible = true;
		ClearStatusMessage();
	}

	public void HideModal()
	{
		Visible = false;
		ClearStatusMessage();
		EmitSignal(SignalName.ModalClosed);
	}

	private void UpdateCurrentCreditsDisplay(int credits)
	{
		if (_currentCreditsLabel != null)
		{
			_currentCreditsLabel.Text = string.Format(CURRENT_CREDITS_FORMAT, credits);
		}
	}

	private void ShowStatusMessage(string message, bool isSuccess)
	{
		if (_statusLabel != null)
		{
			_statusLabel.Text = message;
			_statusLabel.Modulate = isSuccess ? Colors.Green : Colors.Red;
		}
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
		if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed && mouseEvent.ButtonIndex == MouseButton.Left)
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

		var confirmMessage = string.Format(PURCHASE_CONFIRMATION_FORMAT, creditPack.DisplayName);
		var confirmed = await ShowConfirmationDialog(confirmMessage);

		if (!confirmed)
			return;

		ShowStatusMessage(PROCESSING_PURCHASE, true);

		try
		{
			var playerId = EventService.GetPlayerIdFromPhone(_targetPhoneNumber);
			var result = await _paymentService.PurchaseCreditsAsync(playerId, creditPack);

			if (result.IsSuccess)
			{
				ShowStatusMessage(string.Format(PURCHASE_SUCCESS_FORMAT, creditPack.Credits), true);

				var session = _sessionManager?.GetUserSession(_targetPhoneNumber);
				if (session != null)
				{
					UpdateCurrentCreditsDisplay(session.Credits);
				}

				EmitSignal(SignalName.CreditsAcquired, _targetPhoneNumber, creditPack.Credits);

				await DelayAndClose(2000);
			}
			else
			{
				ShowStatusMessage(string.Format(PURCHASE_FAILED_FORMAT, result.ErrorMessage), false);
			}
		}
		catch (Exception ex)
		{
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
		// Disconnect signals
		if (GodotObject.IsInstanceValid(_closeButton))
			_closeButton.Pressed -= OnClosePressed;

		base._ExitTree();
	}
}