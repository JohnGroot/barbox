using System.Collections.Generic;
using Godot;

namespace BarBox.Core.UI;

/// <summary>
/// High-performance on-screen keyboard using custom rendering
/// Replaces 100+ Control nodes with a single _Draw() call
/// </summary>
[Tool]
public partial class OnScreenKeyboard : Control
{
	#region Constants

	private const string KEYBOARD_FIELD_GROUP = "keyboard_field";
	private const string META_LAYOUT_TYPE = "keyboard_layout_type";
	private const string DEFAULT_LAYOUT = "standard-characters";
	private const float DEFAULT_ANIMATION_DURATION = 0.15f;
	private const float DEFAULT_KEY_SEPARATION = 4f;
	private const float DEFAULT_PADDING = 8f;

	#endregion

	#region Exports

	[ExportCategory("Layout")]
	[Export(PropertyHint.File, "*.json")]
	public string LayoutFilePath { get; set; } = "res://addons/onscreenkeyboard/customize/keyboardLayouts/keyboard_layout_complete.json";

	[Export]
	public bool AutoShow { get; set; } = true;

	[Export]
	public bool Animate { get; set; } = true;

	[Export]
	public float AnimationDuration { get; set; } = DEFAULT_ANIMATION_DURATION;

	[ExportCategory("Sizing")]
	[Export]
	public float KeyboardWidthRatio { get; set; } = 0.8f;

	[Export]
	public float KeyboardHeightRatio { get; set; } = 0.12f;

	[Export]
	public float KeySeparation { get; set; } = DEFAULT_KEY_SEPARATION;

	[Export]
	public float Padding { get; set; } = DEFAULT_PADDING;

	[ExportCategory("Styles")]
	[Export]
	public StyleBoxFlat BackgroundStyle { get; set; }

	[Export]
	public StyleBoxFlat NormalKeyStyle { get; set; }

	[Export]
	public StyleBoxFlat HoverKeyStyle { get; set; }

	[Export]
	public StyleBoxFlat PressedKeyStyle { get; set; }

	[Export]
	public StyleBoxFlat SpecialKeyStyle { get; set; }

	[ExportCategory("Font")]
	[Export]
	public Font KeyFont { get; set; }

	[Export]
	public int FontSize { get; set; } = 20;

	[Export]
	public Color FontColorNormal { get; set; } = Colors.White;

	[Export]
	public Color FontColorHover { get; set; } = Colors.White;

	[Export]
	public Color FontColorPressed { get; set; } = Colors.White;

	#endregion

	#region Signals

	[Signal]
	public delegate void LayoutChangedEventHandler(string layoutName);

	[Signal]
	public delegate void KeyboardShownEventHandler();

	[Signal]
	public delegate void KeyboardHiddenEventHandler();

	#endregion

	#region Private Fields

	// Layout data
	private KeyboardLayout[] _layouts = [];
	private int _activeLayoutIndex;
	private string _previousLayoutName;

	// Cached key rectangles for active layout
	private KeyRect[] _keyRects = [];

	// Input state
	private int _hoveredKeyIndex = -1;
	private int _pressedKeyIndex = -1;
	private bool _uppercase;

	// Field tracking
	private readonly HashSet<Control> _monitoredFields = new();
	private Control _focusedField;

	// Animation
	private Tween _positionTween;
	private Vector2 _hiddenPosition;
	private Vector2 _shownPosition;

	// Cached icons
	private Texture2D _iconDelete;
	private Texture2D _iconShift;
	private Texture2D _iconLeft;
	private Texture2D _iconRight;
	private Texture2D _iconHide;
	private Texture2D _iconEnter;

	// Custom icon cache
	private readonly Dictionary<string, Texture2D> _customIconCache = new();

	#endregion

	#region Lifecycle

	public override void _Ready()
	{
		// Enable input handling for this control
		MouseFilter = MouseFilterEnum.Stop;

		// Create default styles if none provided
		CreateDefaultStyles();

		// Load predefined icons
		LoadPredefinedIcons();

		// Load layouts from file
		LoadLayouts();

		// Calculate initial size and position
		UpdateKeyboardSize();

		// Start hidden
		if (!Engine.IsEditorHint())
		{
			Visible = false;
			Position = _hiddenPosition;
		}

		// Discover fields in keyboard_field group
		DiscoverAndMonitorFields();

		// Connect to tree for new fields
		GetTree().NodeAdded += OnNodeAdded;
		GetTree().NodeRemoved += OnNodeRemoved;

		// Connect to viewport resize
		GetTree().Root.SizeChanged += OnViewportSizeChanged;
	}

	public override void _Notification(int what)
	{
		if (what == NotificationExitTree)
		{
			// Disconnect signals
			if (GetTree() != null)
			{
				GetTree().NodeAdded -= OnNodeAdded;
				GetTree().NodeRemoved -= OnNodeRemoved;
				if (GetTree().Root != null)
				{
					GetTree().Root.SizeChanged -= OnViewportSizeChanged;
				}
			}

			// Clean up field monitors
			foreach (var field in _monitoredFields)
			{
				if (IsInstanceValid(field))
				{
					DisconnectFieldSignals(field);
				}
			}

			_monitoredFields.Clear();
		}
	}

	#endregion

	#region Drawing

	public override void _Draw()
	{
		// Draw background
		if (BackgroundStyle != null)
		{
			DrawStyleBox(BackgroundStyle, new Rect2(Vector2.Zero, Size));
		}

		// Draw all keys
		foreach (var keyRect in _keyRects)
		{
			DrawKey(keyRect);
		}
	}

	private void DrawKey(KeyRect keyRect)
	{
		// Skip spacers - they're empty non-interactive areas
		if (keyRect.Data.Type == KeyType.Spacer)
		{
			return;
		}

		var index = System.Array.IndexOf(_keyRects, keyRect);
		var isHovered = index == _hoveredKeyIndex;
		var isPressed = index == _pressedKeyIndex;

		// Select style
		StyleBoxFlat style;
		Color fontColor;

		if (isPressed)
		{
			style = PressedKeyStyle ?? NormalKeyStyle;
			fontColor = FontColorPressed;
		}
		else if (isHovered)
		{
			style = HoverKeyStyle ?? NormalKeyStyle;
			fontColor = FontColorHover;
		}
		else if (keyRect.IsSpecial)
		{
			style = SpecialKeyStyle ?? NormalKeyStyle;
			fontColor = FontColorNormal;
		}
		else
		{
			style = NormalKeyStyle;
			fontColor = FontColorNormal;
		}

		// Draw key background
		if (style != null)
		{
			DrawStyleBox(style, keyRect.Rect);
		}

		// Draw content (icon or text)
		if (keyRect.Data.HasIcon)
		{
			DrawKeyIcon(keyRect, fontColor);
		}
		else
		{
			DrawKeyText(keyRect, fontColor);
		}
	}

	private void DrawKeyIcon(KeyRect keyRect, Color color)
	{
		var icon = GetIconTexture(keyRect.Data);
		if (icon == null)
		{
			return;
		}

		// Center icon in key rect with padding
		var iconPadding = 8f;
		var availableSize = keyRect.Rect.Size - new Vector2(iconPadding * 2, iconPadding * 2);
		var iconSize = icon.GetSize();

		// Scale to fit while maintaining aspect ratio
		var scale = Mathf.Min(availableSize.X / iconSize.X, availableSize.Y / iconSize.Y);
		var scaledSize = iconSize * scale;

		var iconRect = new Rect2(
			keyRect.Rect.Position + ((keyRect.Rect.Size - scaledSize) / 2),
			scaledSize);

		DrawTextureRect(icon, iconRect, false, color);
	}

	private void DrawKeyText(KeyRect keyRect, Color color)
	{
		var text = keyRect.Data.GetDisplayText(_uppercase);
		if (string.IsNullOrEmpty(text))
		{
			return;
		}

		var font = KeyFont ?? ThemeDB.FallbackFont;
		var fontSize = FontSize;

		// Get text size for centering
		var textSize = font.GetStringSize(text, HorizontalAlignment.Center, -1, fontSize);

		// Center text in key rect
		var textPos = new Vector2(
			keyRect.Rect.Position.X + ((keyRect.Rect.Size.X - textSize.X) / 2),
			keyRect.Rect.Position.Y + ((keyRect.Rect.Size.Y + textSize.Y) / 2) - (textSize.Y * 0.2f));

		DrawString(font, textPos, text, HorizontalAlignment.Left, -1, fontSize, color);
	}

	private Texture2D GetIconTexture(KeyData keyData)
	{
		// Check predefined icons
		var icon = keyData.Icon switch
		{
			PredefinedIcon.Delete => _iconDelete,
			PredefinedIcon.Shift => _iconShift,
			PredefinedIcon.Left => _iconLeft,
			PredefinedIcon.Right => _iconRight,
			PredefinedIcon.Hide => _iconHide,
			PredefinedIcon.Enter => _iconEnter,
			_ => null,
		};

		if (icon != null)
		{
			return icon;
		}

		// Check custom icon path
		if (!string.IsNullOrEmpty(keyData.CustomIconPath))
		{
			if (!_customIconCache.TryGetValue(keyData.CustomIconPath, out icon))
			{
				icon = GD.Load<Texture2D>(keyData.CustomIconPath);
				if (icon != null)
				{
					_customIconCache[keyData.CustomIconPath] = icon;
				}
			}

			return icon;
		}

		return null;
	}

	#endregion

	#region Input Handling

	public override void _GuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mb)
		{
			HandleMouseButton(mb);
		}
		else if (@event is InputEventMouseMotion mm)
		{
			HandleMouseMotion(mm);
		}
	}

	private void HandleMouseButton(InputEventMouseButton mb)
	{
		if (mb.ButtonIndex != MouseButton.Left)
		{
			return;
		}

		var keyIndex = FindKeyAtPosition(mb.Position);

		if (mb.Pressed)
		{
			_pressedKeyIndex = keyIndex;
			QueueRedraw();
		}
		else
		{
			// Release - trigger key if released on same key
			if (_pressedKeyIndex >= 0 && _pressedKeyIndex == keyIndex)
			{
				HandleKeyPress(_keyRects[keyIndex].Data);
			}

			_pressedKeyIndex = -1;
			QueueRedraw();
		}

		AcceptEvent();
	}

	private void HandleMouseMotion(InputEventMouseMotion mm)
	{
		var newHover = FindKeyAtPosition(mm.Position);
		if (newHover != _hoveredKeyIndex)
		{
			_hoveredKeyIndex = newHover;
			QueueRedraw();
		}
	}

	private int FindKeyAtPosition(Vector2 pos)
	{
		for (int i = 0; i < _keyRects.Length; i++)
		{
			// Skip spacers - they're not interactive
			if (_keyRects[i].Data.Type == KeyType.Spacer)
			{
				continue;
			}

			if (_keyRects[i].Rect.HasPoint(pos))
			{
				return i;
			}
		}

		return -1;
	}

	private void HandleKeyPress(KeyData keyData)
	{
		switch (keyData.Type)
		{
			case KeyType.Char:
			case KeyType.Special:
				DispatchKeyEvent(keyData);

				// Disable uppercase after typing a character
				if (keyData.Type == KeyType.Char)
				{
					SetUppercase(false);
				}

				break;

			case KeyType.SpecialShift:
				ToggleUppercase();
				break;

			case KeyType.SwitchLayout:
				SwitchToLayout(keyData.TargetLayout);
				break;

			case KeyType.HideKeyboard:
				HideKeyboard();
				break;
		}
	}

	private void DispatchKeyEvent(KeyData keyData)
	{
		if (string.IsNullOrEmpty(keyData.Output))
		{
			return;
		}

		var inputEvent = new InputEventKey
		{
			ShiftPressed = _uppercase,
			AltPressed = false,
			MetaPressed = false,
			CtrlPressed = false,
			Pressed = true,
		};

		var keyCode = KeyCodeMap.GetKeyCodeWithCase(keyData.Output, _uppercase);
		inputEvent.Keycode = (Key)keyCode;
		inputEvent.Unicode = keyCode;

		Input.ParseInputEvent(inputEvent);
	}

	#endregion

	#region Layout Management

	private void LoadLayouts()
	{
		if (string.IsNullOrEmpty(LayoutFilePath))
		{
			GD.PrintErr("OnScreenKeyboard: No layout file specified");
			return;
		}

		_layouts = OnScreenKeyboardLayoutLoader.LoadFromFile(LayoutFilePath);

		if (_layouts.Length == 0)
		{
			GD.PrintErr($"OnScreenKeyboard: No layouts loaded from {LayoutFilePath}");
			return;
		}

		// Activate first layout
		_activeLayoutIndex = 0;
		RecalculateKeyRects();
	}

	public void SetActiveLayout(string layoutName)
	{
		for (int i = 0; i < _layouts.Length; i++)
		{
			if (_layouts[i].Name == layoutName)
			{
				_previousLayoutName = _layouts[_activeLayoutIndex].Name;
				_activeLayoutIndex = i;
				RecalculateKeyRects();
				EmitSignal(SignalName.LayoutChanged, layoutName);
				QueueRedraw();
				return;
			}
		}

		GD.PrintErr($"OnScreenKeyboard: Layout '{layoutName}' not found");
	}

	private void SwitchToLayout(string layoutName)
	{
		if (layoutName == "PREVIOUS-LAYOUT" && !string.IsNullOrEmpty(_previousLayoutName))
		{
			SetActiveLayout(_previousLayoutName);
		}
		else
		{
			SetActiveLayout(layoutName);
		}
	}

	private void RecalculateKeyRects()
	{
		if (_layouts.Length == 0 || _activeLayoutIndex >= _layouts.Length)
		{
			_keyRects = [];
			return;
		}

		var layout = _layouts[_activeLayoutIndex];
		var keyList = new List<KeyRect>();

		if (layout.Rows == null || layout.Rows.Length == 0)
		{
			_keyRects = [];
			return;
		}

		var contentWidth = Size.X - (Padding * 2);
		var contentHeight = Size.Y - (Padding * 2);
		var rowHeight = (contentHeight - (KeySeparation * (layout.Rows.Length - 1))) / layout.Rows.Length;

		float y = Padding;

		for (int rowIdx = 0; rowIdx < layout.Rows.Length; rowIdx++)
		{
			var row = layout.Rows[rowIdx];
			if (row.Keys == null || row.Keys.Length == 0)
			{
				y += rowHeight + KeySeparation;
				continue;
			}

			// Calculate total stretch for this row
			float totalStretch = 0;
			foreach (var key in row.Keys)
			{
				totalStretch += key.StretchRatio > 0 ? key.StretchRatio : 1f;
			}

			var availableWidth = contentWidth - (KeySeparation * (row.Keys.Length - 1));
			float x = Padding;

			for (int keyIdx = 0; keyIdx < row.Keys.Length; keyIdx++)
			{
				var key = row.Keys[keyIdx];
				var stretch = key.StretchRatio > 0 ? key.StretchRatio : 1f;
				var keyWidth = (stretch / totalStretch) * availableWidth;

				keyList.Add(new KeyRect
				{
					Rect = new Rect2(x, y, keyWidth, rowHeight),
					LayoutIndex = _activeLayoutIndex,
					RowIndex = rowIdx,
					KeyIndex = keyIdx,
					Data = key,
				});

				x += keyWidth + KeySeparation;
			}

			y += rowHeight + KeySeparation;
		}

		_keyRects = [.. keyList];
	}

	#endregion

	#region Uppercase/Shift

	private void SetUppercase(bool value)
	{
		if (_uppercase == value)
		{
			return;
		}

		_uppercase = value;
		QueueRedraw();
	}

	private void ToggleUppercase()
	{
		SetUppercase(!_uppercase);
	}

	#endregion

	#region Show/Hide

	public void ShowKeyboard()
	{
		if (Visible && Position.Y <= _shownPosition.Y)
		{
			return;
		}

		Visible = true;

		if (Animate)
		{
			AnimateToPosition(_shownPosition);
		}
		else
		{
			Position = _shownPosition;
		}

		EmitSignal(SignalName.KeyboardShown);
	}

	public void HideKeyboard()
	{
		if (!Visible)
		{
			return;
		}

		SetUppercase(false);

		if (Animate)
		{
			AnimateToPosition(_hiddenPosition, true);
		}
		else
		{
			Visible = false;
			Position = _hiddenPosition;
		}

		EmitSignal(SignalName.KeyboardHidden);
	}

	private void AnimateToPosition(Vector2 targetPos, bool hideOnComplete = false)
	{
		_positionTween?.Kill();
		_positionTween = CreateTween();

		_positionTween.TweenProperty(this, "position", targetPos, AnimationDuration)
			.SetTrans(Tween.TransitionType.Sine);

		if (hideOnComplete)
		{
			_positionTween.TweenCallback(Callable.From(() => Visible = false));
		}
	}

	#endregion

	#region Sizing

	private void UpdateKeyboardSize()
	{
		var viewportSize = GetViewport()?.GetVisibleRect().Size ?? new Vector2(1920, 1080);

		var keyboardWidth = viewportSize.X * KeyboardWidthRatio;
		var keyboardHeight = viewportSize.Y * KeyboardHeightRatio;

		CustomMinimumSize = new Vector2(keyboardWidth, keyboardHeight);
		Size = new Vector2(keyboardWidth, keyboardHeight);

		// Calculate positions
		var centerX = (viewportSize.X - keyboardWidth) / 2;
		_shownPosition = new Vector2(centerX, viewportSize.Y - keyboardHeight);
		_hiddenPosition = new Vector2(centerX, viewportSize.Y + 10);

		// Update if currently hidden
		if (!Visible)
		{
			Position = _hiddenPosition;
		}

		// Recalculate key rects for new size
		RecalculateKeyRects();
		QueueRedraw();
	}

	private void OnViewportSizeChanged()
	{
		UpdateKeyboardSize();

		// If showing, hide on resize (like original behavior)
		if (AutoShow && Visible)
		{
			HideKeyboard();
		}
	}

	#endregion

	#region Field Integration

	/// <summary>
	/// Manually refresh field discovery (call when fields added dynamically)
	/// </summary>
	public void RefreshFields()
	{
		DiscoverAndMonitorFields();
	}

	private void DiscoverAndMonitorFields()
	{
		var fields = GetTree().GetNodesInGroup(KEYBOARD_FIELD_GROUP);
		foreach (var node in fields)
		{
			if (node is Control field)
			{
				MonitorField(field);
			}
		}
	}

	private void MonitorField(Control field)
	{
		if (_monitoredFields.Contains(field))
		{
			return;
		}

		ConnectFieldSignals(field);
		_monitoredFields.Add(field);
	}

	private void ConnectFieldSignals(Control field)
	{
		if (field is LineEdit lineEdit)
		{
			lineEdit.FocusEntered += () => OnFieldFocusEntered(field);
			lineEdit.FocusExited += () => OnFieldFocusExited(field);
		}
		else if (field is TextEdit textEdit)
		{
			textEdit.FocusEntered += () => OnFieldFocusEntered(field);
			textEdit.FocusExited += () => OnFieldFocusExited(field);
		}
	}

	private void DisconnectFieldSignals(Control field)
	{
		// Note: In Godot 4, we can't easily disconnect lambda-captured delegates
		// The signals will be cleaned up when the field is freed
	}

	private void OnFieldFocusEntered(Control field)
	{
		if (!AutoShow)
		{
			return;
		}

		if (!IsKeyboardTargetField(field))
		{
			return;
		}

		_focusedField = field;

		// Check for field-specific layout
		if (field.HasMeta(META_LAYOUT_TYPE))
		{
			var layoutType = field.GetMeta(META_LAYOUT_TYPE).AsString();
			if (!string.IsNullOrEmpty(layoutType))
			{
				SetActiveLayout(layoutType);
			}
		}
		else
		{
			SetActiveLayout(DEFAULT_LAYOUT);
		}

		ShowKeyboard();
	}

	private async void OnFieldFocusExited(Control field)
	{
		if (!AutoShow)
		{
			return;
		}

		// Wait one frame for new focus owner to be set
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		if (!IsInstanceValid(this))
		{
			return;
		}

		var newFocus = GetViewport()?.GuiGetFocusOwner();
		if (!IsKeyboardTargetField(newFocus))
		{
			_focusedField = null;
			HideKeyboard();
		}
	}

	private void OnNodeAdded(Node node)
	{
		if (node is Control field && node.IsInGroup(KEYBOARD_FIELD_GROUP))
		{
			MonitorField(field);
		}
	}

	private void OnNodeRemoved(Node node)
	{
		if (node is Control field)
		{
			_monitoredFields.Remove(field);
		}
	}

	private static bool IsKeyboardTargetField(Control control)
	{
		return control is LineEdit or TextEdit;
	}

	#endregion

	#region Icon Loading

	private void LoadPredefinedIcons()
	{
		const string iconBasePath = "res://addons/onscreenkeyboard/icons/";

		_iconDelete = LoadIcon(iconBasePath + "delete.png");
		_iconShift = LoadIcon(iconBasePath + "shift.png");
		_iconLeft = LoadIcon(iconBasePath + "left.png");
		_iconRight = LoadIcon(iconBasePath + "right.png");
		_iconHide = LoadIcon(iconBasePath + "hide.png");
		_iconEnter = LoadIcon(iconBasePath + "enter.png");
	}

	private static Texture2D LoadIcon(string path)
	{
		if (!ResourceLoader.Exists(path))
		{
			GD.PrintErr($"OnScreenKeyboard: Icon not found: {path}");
			return null;
		}

		return GD.Load<Texture2D>(path);
	}

	#endregion

	#region Default Styles

	private void CreateDefaultStyles()
	{
		if (BackgroundStyle == null)
		{
			BackgroundStyle = new StyleBoxFlat();
			BackgroundStyle.BgColor = new Color(0.1f, 0.1f, 0.1f, 0.95f);
			BackgroundStyle.SetCornerRadiusAll(8);
		}

		if (NormalKeyStyle == null)
		{
			NormalKeyStyle = new StyleBoxFlat();
			NormalKeyStyle.BgColor = new Color(0.2f, 0.2f, 0.2f, 0.9f);
			NormalKeyStyle.SetCornerRadiusAll(4);
		}

		if (HoverKeyStyle == null)
		{
			HoverKeyStyle = new StyleBoxFlat();
			HoverKeyStyle.BgColor = new Color(0.3f, 0.3f, 0.3f, 0.9f);
			HoverKeyStyle.SetCornerRadiusAll(4);
		}

		if (PressedKeyStyle == null)
		{
			PressedKeyStyle = new StyleBoxFlat();
			PressedKeyStyle.BgColor = new Color(0.4f, 0.4f, 0.5f, 0.9f);
			PressedKeyStyle.SetCornerRadiusAll(4);
		}

		if (SpecialKeyStyle == null)
		{
			SpecialKeyStyle = new StyleBoxFlat();
			SpecialKeyStyle.BgColor = new Color(0.15f, 0.15f, 0.2f, 0.9f);
			SpecialKeyStyle.SetCornerRadiusAll(4);
		}
	}

	#endregion
}
