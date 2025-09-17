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

	// UI Components
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
	private string _currentUserId;

	public override void _Ready()
	{
		_paymentService = PaymentService.GetInstance();
		_sessionManager = SessionManager.GetInstance();

		SetupModalLayout();
		CreateModalUI();
		ConnectSignals();

		// Start hidden
		Visible = false;
	}

	private void SetupModalLayout()
	{
		// Fill entire screen - BuyCreditsModal will be added to UIManager which has its own CanvasLayer handling
		SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
	}

	private void CreateModalUI()
	{
		// Semi-transparent background overlay
		_modalBackground = new Panel();
		_modalBackground.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		var backgroundStyle = new StyleBoxFlat();
		backgroundStyle.BgColor = new Color(0.0f, 0.0f, 0.0f, 0.7f); // Semi-transparent black
		_modalBackground.AddThemeStyleboxOverride("panel", backgroundStyle);

		// Click background to close modal
		_modalBackground.GuiInput += OnBackgroundInput;

		AddChild(_modalBackground);

		// Create centering container
		var centerContainer = new CenterContainer();
		centerContainer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		AddChild(centerContainer);

		// Modal panel (centered dialog)
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

		// Content container
		_contentContainer = new VBoxContainer();
		_contentContainer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_contentContainer.AddThemeConstantOverride("separation", 15);

		// Add padding
		var margin = new MarginContainer();
		margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		margin.AddThemeConstantOverride("margin_left", 20);
		margin.AddThemeConstantOverride("margin_right", 20);
		margin.AddThemeConstantOverride("margin_top", 20);
		margin.AddThemeConstantOverride("margin_bottom", 20);

		_modalPanel.AddChild(margin);
		margin.AddChild(_contentContainer);

		// Title
		_titleLabel = new Label();
		_titleLabel.Text = "Buy Credits";
		_titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_titleLabel.AddThemeColorOverride("font_color", Colors.White);
		_titleLabel.AddThemeFontSizeOverride("font_size", 24);
		_contentContainer.AddChild(_titleLabel);

		// Current credits display
		_currentCreditsLabel = new Label();
		_currentCreditsLabel.Text = "Current Credits: 0";
		_currentCreditsLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_currentCreditsLabel.AddThemeColorOverride("font_color", Colors.LightGray);
		_currentCreditsLabel.AddThemeFontSizeOverride("font_size", 16);
		_contentContainer.AddChild(_currentCreditsLabel);

		// Credit pack selection
		var packLabel = new Label();
		packLabel.Text = "Select Credit Pack:";
		packLabel.AddThemeColorOverride("font_color", Colors.White);
		packLabel.AddThemeFontSizeOverride("font_size", 18);
		_contentContainer.AddChild(packLabel);

		// Credit pack grid (2 columns)
		_creditPackContainer = new GridContainer();
		_creditPackContainer.Columns = 2;
		_creditPackContainer.AddThemeConstantOverride("h_separation", 10);
		_creditPackContainer.AddThemeConstantOverride("v_separation", 10);
		_contentContainer.AddChild(_creditPackContainer);

		// Create credit pack buttons
		CreateCreditPackButtons();

		// Close button
		var buttonContainer = new HBoxContainer();
		buttonContainer.Alignment = BoxContainer.AlignmentMode.Center;
		_contentContainer.AddChild(buttonContainer);

		_closeButton = new Button();
		_closeButton.Text = "Close";
		_closeButton.CustomMinimumSize = new Vector2(120, 40);
		TopMenuBar.ApplyStandardButtonStyle(_closeButton);
		buttonContainer.AddChild(_closeButton);

		// Status label
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
			ShowStatusMessage("Payment service not available", false);
			return;
		}

		var creditPacks = _paymentService.GetAvailableCreditPacks();

		foreach (var pack in creditPacks)
		{
			var button = new Button();
			button.Text = pack.DisplayName;
			button.CustomMinimumSize = new Vector2(200, 50);
			TopMenuBar.ApplyStandardButtonStyle(button);

			// Store the pack index in the button's metadata for reference
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

	/// <summary>
	/// Show the buy credits modal
	/// </summary>
	public void ShowModal()
	{
		// Get current user
		var session = _sessionManager?.GetCurrentUserSession();
		if (session == null)
		{
			ShowStatusMessage("Please log in to purchase credits", false);
			return;
		}

		_currentUserId = session.UserId;
		UpdateCurrentCreditsDisplay(session.GlobalData?.GlobalCredits ?? 0);

		Visible = true;
		ClearStatusMessage();
	}

	/// <summary>
	/// Hide the buy credits modal
	/// </summary>
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
			_currentCreditsLabel.Text = $"Current Credits: {credits}";
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

	// Event handlers

	private void OnBackgroundInput(InputEvent @event)
	{
		// Close modal when clicking background
		if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed && mouseEvent.ButtonIndex == MouseButton.Left)
		{
			OnClosePressed();
		}
	}

	private async void OnCreditPackSelected(CreditPack creditPack)
	{
		if (string.IsNullOrEmpty(_currentUserId))
		{
			ShowStatusMessage("No user logged in", false);
			return;
		}

		if (_paymentService == null)
		{
			ShowStatusMessage("Payment service not available", false);
			return;
		}

		// Show confirmation dialog
		var confirmMessage = $"Purchase {creditPack.DisplayName}?";
		var confirmed = await ShowConfirmationDialog(confirmMessage);

		if (!confirmed)
			return;

		ShowStatusMessage("Processing purchase...", true);

		try
		{
			var result = await _paymentService.PurchaseCreditsAsync(_currentUserId, creditPack);

			if (result.IsSuccess)
			{
				ShowStatusMessage($"Purchase successful! {creditPack.Credits} credits added.", true);

				// Update credits display
				var session = _sessionManager?.GetCurrentUserSession();
				if (session?.GlobalData != null)
				{
					UpdateCurrentCreditsDisplay(session.GlobalData.GlobalCredits);
				}

				// Emit signal for UI updates
				EmitSignal(SignalName.CreditsAcquired, _currentUserId, creditPack.Credits);

				// Auto-close after delay
				await DelayAndClose(2000);
			}
			else
			{
				ShowStatusMessage($"Purchase failed: {result.ErrorMessage}", false);
			}
		}
		catch (Exception ex)
		{
			ShowStatusMessage($"Error: {ex.Message}", false);
		}
	}

	private async Task<bool> ShowConfirmationDialog(string message)
	{
		// Simple confirmation - in a more complex UI this could be a separate dialog
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