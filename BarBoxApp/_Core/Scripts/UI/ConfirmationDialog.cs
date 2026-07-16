using System;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Reusable confirmation dialog for user actions requiring confirmation
/// Follows existing modal patterns with async/await support for clean integration
/// </summary>
public partial class ConfirmationDialog : Control
{
	[Signal]
	public delegate void ConfirmedEventHandler();

	[Signal]
	public delegate void CancelledEventHandler();

	private const string DEFAULT_TITLE = "Confirm Action";
	private const string DEFAULT_MESSAGE = "Are you sure?";
	private const string DEFAULT_CONFIRM_TEXT = "Confirm";
	private const string DEFAULT_CANCEL_TEXT = "Cancel";

	private Panel _modalBackground;
	private Panel _modalPanel;
	private VBoxContainer _contentContainer;
	private Label _titleLabel;
	private Label _messageLabel;
	private HBoxContainer _buttonContainer;
	private Button _confirmButton;
	private Button _cancelButton;

	private TaskCompletionSource<bool> _currentTask;

	private string _currentTitle = DEFAULT_TITLE;
	private string _currentMessage = DEFAULT_MESSAGE;
	private string _currentConfirmText = DEFAULT_CONFIRM_TEXT;
	private string _currentCancelText = DEFAULT_CANCEL_TEXT;

	public override void _Ready()
	{
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

		_titleLabel = new Label();
		_titleLabel.Text = _currentTitle;
		_titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_titleLabel.AddThemeColorOverride("font_color", Colors.White);
		_titleLabel.AddThemeFontSizeOverride("font_size", 18);
		_contentContainer.AddChild(_titleLabel);

		_messageLabel = new Label();
		_messageLabel.Text = _currentMessage;
		_messageLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_messageLabel.VerticalAlignment = VerticalAlignment.Center;
		_messageLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_messageLabel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		_messageLabel.AddThemeColorOverride("font_color", Colors.White);
		_messageLabel.AddThemeFontSizeOverride("font_size", 14);
		_contentContainer.AddChild(_messageLabel);

		_buttonContainer = new HBoxContainer();
		_buttonContainer.Alignment = BoxContainer.AlignmentMode.Center;
		_buttonContainer.AddThemeConstantOverride("separation", 20);
		_contentContainer.AddChild(_buttonContainer);

		_cancelButton = new Button();
		_cancelButton.Text = _currentCancelText;
		_cancelButton.CustomMinimumSize = new Vector2(100, 40);
		ApplyButtonStyle(_cancelButton, false);
		_cancelButton.Pressed += OnCancelPressed;
		_buttonContainer.AddChild(_cancelButton);

		_confirmButton = new Button();
		_confirmButton.Text = _currentConfirmText;
		_confirmButton.CustomMinimumSize = new Vector2(100, 40);
		ApplyButtonStyle(_confirmButton, true);
		_confirmButton.Pressed += OnConfirmPressed;
		_buttonContainer.AddChild(_confirmButton);
	}

	private void ApplyButtonStyle(Button button, bool isPrimary)
	{
		var baseColor = isPrimary ? new Color(0.4f, 0.6f, 0.4f, 1.0f) : new Color(0.3f, 0.3f, 0.3f, 1.0f);
		var hoverColor = isPrimary ? new Color(0.5f, 0.7f, 0.5f, 1.0f) : new Color(0.4f, 0.4f, 0.4f, 1.0f);
		var pressedColor = isPrimary ? new Color(0.3f, 0.5f, 0.3f, 1.0f) : new Color(0.2f, 0.2f, 0.2f, 1.0f);

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

		button.AddThemeColorOverride("font_color", Colors.White);
		button.AddThemeFontSizeOverride("font_size", 14);
	}

	private void ConnectSignals()
	{
		Confirmed += OnConfirmed;
		Cancelled += OnCancelled;
	}

	/// <summary>
	/// Returns true for confirmed, false for cancelled
	/// </summary>
	public async Task<bool> ShowAsync(
		string title = DEFAULT_TITLE,
		string message = DEFAULT_MESSAGE,
		string confirmText = DEFAULT_CONFIRM_TEXT,
		string cancelText = DEFAULT_CANCEL_TEXT)
	{
		if (Visible)
		{
			return false;
		}

		_currentTitle = title;
		_currentMessage = message;
		_currentConfirmText = confirmText;
		_currentCancelText = cancelText;

		UpdateDialogContent();

		_currentTask = new TaskCompletionSource<bool>();

		Visible = true;

		_confirmButton.GrabFocus();

		return await _currentTask.Task;
	}

	private void UpdateDialogContent()
	{
		if (_titleLabel != null)
		{
			_titleLabel.Text = _currentTitle;
		}

		if (_messageLabel != null)
		{
			_messageLabel.Text = _currentMessage;
		}

		if (_confirmButton != null)
		{
			_confirmButton.Text = _currentConfirmText;
		}

		if (_cancelButton != null)
		{
			_cancelButton.Text = _currentCancelText;
		}
	}

	public new void Hide()
	{
		if (!Visible)
		{
			return;
		}

		Visible = false;

		if (_currentTask != null && !_currentTask.Task.IsCompleted)
		{
			_currentTask.SetResult(false);
		}

		_currentTask = null;
	}

	private void OnBackgroundInput(InputEvent inputEvent)
	{
		if (inputEvent is InputEventMouseButton mouseButton &&
			mouseButton.ButtonIndex == MouseButton.Left &&
			mouseButton.Pressed)
		{
			OnCancelPressed();
		}
	}

	public override void _Input(InputEvent inputEvent)
	{
		if (!Visible)
		{
			return;
		}

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

		if (_currentTask != null && !_currentTask.Task.IsCompleted)
		{
			_currentTask.SetResult(true);
		}

		_currentTask = null;
	}

	private void OnCancelled()
	{
		Visible = false;

		if (_currentTask != null && !_currentTask.Task.IsCompleted)
		{
			_currentTask.SetResult(false);
		}

		_currentTask = null;
	}

	public override void _ExitTree()
	{
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
