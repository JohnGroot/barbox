using Godot;
using System;
using System.Threading.Tasks;

/// <summary>
/// Reusable confirmation dialog for user actions requiring confirmation
/// Follows existing modal patterns with async/await support for clean integration
/// </summary>
public partial class ConfirmationDialog : Control
{
	[Signal] public delegate void ConfirmedEventHandler();
	[Signal] public delegate void CancelledEventHandler();

	// UI Components
	private Panel _modalBackground;
	private Panel _modalPanel;
	private VBoxContainer _contentContainer;
	private Label _titleLabel;
	private Label _messageLabel;
	private HBoxContainer _buttonContainer;
	private Button _confirmButton;
	private Button _cancelButton;

	// Task completion source for async operation
	private TaskCompletionSource<bool> _currentTask;

	// Dialog configuration
	private string _currentTitle = "Confirm Action";
	private string _currentMessage = "Are you sure?";
	private string _currentConfirmText = "Confirm";
	private string _currentCancelText = "Cancel";

	public override void _Ready()
	{
		SetupModalLayout();
		CreateModalUI();
		ConnectSignals();

		// Start hidden
		Visible = false;
	}

	private void SetupModalLayout()
	{
		// Fill entire screen for modal overlay
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

		// Click background to cancel (ESC-like behavior)
		_modalBackground.GuiInput += OnBackgroundInput;

		AddChild(_modalBackground);

		// Create centering container
		var centerContainer = new CenterContainer();
		centerContainer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		AddChild(centerContainer);

		// Modal panel (centered dialog)
		_modalPanel = new Panel();
		_modalPanel.CustomMinimumSize = new Vector2(400, 200);
		_modalPanel.SetSize(new Vector2(400, 200));

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

		// Content container with padding
		var marginContainer = new MarginContainer();
		marginContainer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		marginContainer.AddThemeConstantOverride("margin_left", 20);
		marginContainer.AddThemeConstantOverride("margin_right", 20);
		marginContainer.AddThemeConstantOverride("margin_top", 20);
		marginContainer.AddThemeConstantOverride("margin_bottom", 20);
		_modalPanel.AddChild(marginContainer);

		_contentContainer = new VBoxContainer();
		_contentContainer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_contentContainer.AddThemeConstantOverride("separation", 15);
		marginContainer.AddChild(_contentContainer);

		// Title label
		_titleLabel = new Label();
		_titleLabel.Text = _currentTitle;
		_titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_titleLabel.AddThemeColorOverride("font_color", Colors.White);
		_titleLabel.AddThemeFontSizeOverride("font_size", 18);
		_contentContainer.AddChild(_titleLabel);

		// Message label
		_messageLabel = new Label();
		_messageLabel.Text = _currentMessage;
		_messageLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_messageLabel.VerticalAlignment = VerticalAlignment.Center;
		_messageLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_messageLabel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		_messageLabel.AddThemeColorOverride("font_color", Colors.White);
		_messageLabel.AddThemeFontSizeOverride("font_size", 14);
		_contentContainer.AddChild(_messageLabel);

		// Button container
		_buttonContainer = new HBoxContainer();
		_buttonContainer.Alignment = BoxContainer.AlignmentMode.Center;
		_buttonContainer.AddThemeConstantOverride("separation", 20);
		_contentContainer.AddChild(_buttonContainer);

		// Cancel button
		_cancelButton = new Button();
		_cancelButton.Text = _currentCancelText;
		_cancelButton.CustomMinimumSize = new Vector2(100, 40);
		ApplyButtonStyle(_cancelButton, false);
		_cancelButton.Pressed += OnCancelPressed;
		_buttonContainer.AddChild(_cancelButton);

		// Confirm button
		_confirmButton = new Button();
		_confirmButton.Text = _currentConfirmText;
		_confirmButton.CustomMinimumSize = new Vector2(100, 40);
		ApplyButtonStyle(_confirmButton, true);
		_confirmButton.Pressed += OnConfirmPressed;
		_buttonContainer.AddChild(_confirmButton);
	}

	/// <summary>
	/// Apply consistent button styling matching TopMenuBar pattern
	/// </summary>
	private void ApplyButtonStyle(Button button, bool isPrimary)
	{
		// Primary button (confirm) gets emphasized styling
		var baseColor = isPrimary ? new Color(0.4f, 0.6f, 0.4f, 1.0f) : new Color(0.3f, 0.3f, 0.3f, 1.0f);
		var hoverColor = isPrimary ? new Color(0.5f, 0.7f, 0.5f, 1.0f) : new Color(0.4f, 0.4f, 0.4f, 1.0f);
		var pressedColor = isPrimary ? new Color(0.3f, 0.5f, 0.3f, 1.0f) : new Color(0.2f, 0.2f, 0.2f, 1.0f);

		// Normal state styling
		var normalStyleBox = new StyleBoxFlat();
		normalStyleBox.BgColor = baseColor;
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
		hoverStyleBox.BgColor = hoverColor;
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
		pressedStyleBox.BgColor = pressedColor;
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

	private void ConnectSignals()
	{
		// Connect internal signals
		Confirmed += OnConfirmed;
		Cancelled += OnCancelled;
	}

	/// <summary>
	/// Show confirmation dialog with customizable content
	/// Returns Task<bool> - true for confirmed, false for cancelled
	/// </summary>
	public async Task<bool> ShowAsync(
		string title = "Confirm Action",
		string message = "Are you sure?",
		string confirmText = "Confirm",
		string cancelText = "Cancel")
	{
		// Don't show if already visible
		if (Visible)
			return false;

		// Store current configuration
		_currentTitle = title;
		_currentMessage = message;
		_currentConfirmText = confirmText;
		_currentCancelText = cancelText;

		// Update UI with new content
		UpdateDialogContent();

		// Create task completion source for async operation
		_currentTask = new TaskCompletionSource<bool>();

		// Show the dialog
		Visible = true;

		// Focus the confirm button for keyboard navigation
		_confirmButton.GrabFocus();

		return await _currentTask.Task;
	}

	/// <summary>
	/// Update dialog content with current configuration
	/// </summary>
	private void UpdateDialogContent()
	{
		if (_titleLabel != null)
			_titleLabel.Text = _currentTitle;

		if (_messageLabel != null)
			_messageLabel.Text = _currentMessage;

		if (_confirmButton != null)
			_confirmButton.Text = _currentConfirmText;

		if (_cancelButton != null)
			_cancelButton.Text = _currentCancelText;
	}

	/// <summary>
	/// Hide the dialog without completing the task
	/// </summary>
	public new void Hide()
	{
		if (!Visible)
			return;

		Visible = false;

		// Complete task with cancellation if still pending
		if (_currentTask != null && !_currentTask.Task.IsCompleted)
		{
			_currentTask.SetResult(false);
		}

		_currentTask = null;
	}

	/// <summary>
	/// Handle background click to cancel
	/// </summary>
	private void OnBackgroundInput(InputEvent inputEvent)
	{
		if (inputEvent is InputEventMouseButton mouseButton &&
		    mouseButton.ButtonIndex == MouseButton.Left &&
		    mouseButton.Pressed)
		{
			OnCancelPressed();
		}
	}

	/// <summary>
	/// Handle input events for keyboard shortcuts
	/// </summary>
	public override void _Input(InputEvent inputEvent)
	{
		if (!Visible)
			return;

		if (inputEvent is InputEventKey keyEvent && keyEvent.Pressed)
		{
			switch (keyEvent.Keycode)
			{
				case Key.Escape:
					OnCancelPressed();
					GetViewport().SetInputAsHandled();
					break;
				case Key.Enter:
				case Key.KpEnter:
					OnConfirmPressed();
					GetViewport().SetInputAsHandled();
					break;
			}
		}
	}

	// Event handlers
	private void OnConfirmPressed()
	{
		EmitSignal(SignalName.Confirmed);
	}

	private void OnCancelPressed()
	{
		EmitSignal(SignalName.Cancelled);
	}

	private void OnConfirmed()
	{
		Visible = false;

		// Complete task with confirmation
		if (_currentTask != null && !_currentTask.Task.IsCompleted)
		{
			_currentTask.SetResult(true);
		}

		_currentTask = null;
	}

	private void OnCancelled()
	{
		Visible = false;

		// Complete task with cancellation
		if (_currentTask != null && !_currentTask.Task.IsCompleted)
		{
			_currentTask.SetResult(false);
		}

		_currentTask = null;
	}

	public override void _ExitTree()
	{
		// Disconnect signals
		if (GodotObject.IsInstanceValid(_confirmButton))
		{
			_confirmButton.Pressed -= OnConfirmPressed;
		}
		if (GodotObject.IsInstanceValid(_cancelButton))
		{
			_cancelButton.Pressed -= OnCancelPressed;
		}
		if (GodotObject.IsInstanceValid(_modalBackground))
		{
			_modalBackground.GuiInput -= OnBackgroundInput;
		}

		Confirmed -= OnConfirmed;
		Cancelled -= OnCancelled;

		base._ExitTree();
	}
}