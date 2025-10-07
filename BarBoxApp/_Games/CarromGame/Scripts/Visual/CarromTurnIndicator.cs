using Godot;

/// <summary>
/// Turn indicator UI for Carrom competitive mode
/// Displays current player's turn below the top menu bar
/// </summary>
[GlobalClass]
public partial class CarromTurnIndicator : CanvasLayer
{
	// UI Components
	private Panel _mainPanel;
	private Label _turnLabel;

	// Styling constants
	private const float PANEL_HEIGHT = 50.0f;
	private const float PANEL_TOP_OFFSET = 205.0f; // Position below both menu bars
	private const float PANEL_MARGIN = 10.0f;
	private readonly Color PANEL_BACKGROUND_COLOR = new Color(0.05f, 0.05f, 0.1f, 0.9f); // Dark blue-black
	private readonly Color WHITE_PLAYER_COLOR = new Color(0.2f, 0.9f, 0.8f, 1.0f); // Cyan tint
	private readonly Color BLACK_PLAYER_COLOR = new Color(0.6f, 0.4f, 0.9f, 1.0f); // Purple tint

	// Animation state
	private Tween _currentTween;

	public override void _Ready()
	{
		// Set layer to appear below top menu but above game content
		Layer = 75;

		SetupUI();
	}

	/// <summary>
	/// Setup the UI components
	/// </summary>
	private void SetupUI()
	{
		// Create main panel - horizontal bar below top menu
		_mainPanel = new Panel();

		// Anchor to top of screen, full width
		_mainPanel.AnchorLeft = 0.0f;
		_mainPanel.AnchorRight = 1.0f;
		_mainPanel.AnchorTop = 0.0f;
		_mainPanel.AnchorBottom = 0.0f;

		// Set size and position
		_mainPanel.OffsetLeft = PANEL_MARGIN;
		_mainPanel.OffsetRight = -PANEL_MARGIN;
		_mainPanel.OffsetTop = PANEL_TOP_OFFSET;
		_mainPanel.OffsetBottom = PANEL_TOP_OFFSET + PANEL_HEIGHT;

		// Style the main panel
		var styleBox = new StyleBoxFlat();
		styleBox.BgColor = PANEL_BACKGROUND_COLOR;
		styleBox.BorderWidthTop = 2;
		styleBox.BorderWidthBottom = 2;
		styleBox.BorderWidthLeft = 0;
		styleBox.BorderWidthRight = 0;
		styleBox.BorderColor = new Color(0.3f, 0.3f, 0.4f, 1.0f);
		styleBox.CornerRadiusTopLeft = 8;
		styleBox.CornerRadiusTopRight = 8;
		styleBox.CornerRadiusBottomLeft = 8;
		styleBox.CornerRadiusBottomRight = 8;
		_mainPanel.AddThemeStyleboxOverride("panel", styleBox);

		AddChild(_mainPanel);

		// Create turn label (centered)
		_turnLabel = new Label();
		_turnLabel.Text = "";
		_turnLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_turnLabel.VerticalAlignment = VerticalAlignment.Center;
		_turnLabel.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
		_turnLabel.AddThemeFontSizeOverride("font_size", 16);

		// Anchor to fill entire panel
		_turnLabel.AnchorLeft = 0.0f;
		_turnLabel.AnchorRight = 1.0f;
		_turnLabel.AnchorTop = 0.0f;
		_turnLabel.AnchorBottom = 1.0f;

		// Explicitly clear offsets to ensure proper centering
		_turnLabel.OffsetLeft = 0;
		_turnLabel.OffsetRight = 0;
		_turnLabel.OffsetTop = 0;
		_turnLabel.OffsetBottom = 0;

		_mainPanel.AddChild(_turnLabel);

		// Start hidden
		_mainPanel.Modulate = new Color(1, 1, 1, 0);
	}

	/// <summary>
	/// Update the turn display with current player info
	/// </summary>
	public void UpdateTurnDisplay(string playerId, PieceType pieceType, int turnNumber)
	{
		// Get piece icon based on type
		string pieceIcon = pieceType == PieceType.White ? "⚪" : "⚫";

		// Build turn message
		string turnMessage = $"🎯 {playerId.ToUpper()}'S TURN {pieceIcon} - Turn {turnNumber}";

		_turnLabel.Text = turnMessage;

		// Set color based on piece type
		_turnLabel.Modulate = pieceType == PieceType.White ? WHITE_PLAYER_COLOR : BLACK_PLAYER_COLOR;

		// Trigger pulse animation to draw attention
		AnimateTurnChange();
	}

	/// <summary>
	/// Show the turn indicator with fade-in animation
	/// </summary>
	public new void Show()
	{
		Visible = true;

		// Stop any existing animation
		_currentTween?.Kill();

		// Fade in
		_currentTween = CreateTween();
		_currentTween.TweenProperty(_mainPanel, TweenConstants.ModulateAlpha, 1.0f, 0.3f)
			.SetTrans(Tween.TransitionType.Cubic)
			.SetEase(Tween.EaseType.Out);

		// Slide down slightly for emphasis
		var startOffset = _mainPanel.OffsetTop - 10.0f;
		_mainPanel.OffsetTop = startOffset;
		_currentTween.Parallel().TweenProperty(_mainPanel, "offset_top", PANEL_TOP_OFFSET, 0.3f)
			.SetTrans(Tween.TransitionType.Back)
			.SetEase(Tween.EaseType.Out);
	}

	/// <summary>
	/// Hide the turn indicator with fade-out animation
	/// </summary>
	public new void Hide()
	{
		// Stop any existing animation
		_currentTween?.Kill();

		// Fade out
		_currentTween = CreateTween();
		_currentTween.TweenProperty(_mainPanel, TweenConstants.ModulateAlpha, 0.0f, 0.3f)
			.SetTrans(Tween.TransitionType.Cubic)
			.SetEase(Tween.EaseType.In);

		_currentTween.Finished += () => Visible = false;
	}

	/// <summary>
	/// Animate turn change with subtle pulse effect
	/// </summary>
	private void AnimateTurnChange()
	{
		if (_turnLabel == null || !IsInstanceValid(_turnLabel)) return;

		// Stop any existing label animation
		var existingTweens = _turnLabel.GetTree()?.GetProcessedTweens();
		if (existingTweens != null)
		{
			foreach (var tween in existingTweens)
			{
				if (tween.IsValid())
					tween.Kill();
			}
		}

		// Pulse animation - scale up and back
		var pulseTween = CreateTween();
		pulseTween.TweenProperty(_turnLabel, TweenConstants.Scale, Vector2.One * 1.15f, 0.2f)
			.SetTrans(Tween.TransitionType.Back)
			.SetEase(Tween.EaseType.Out);
		pulseTween.TweenProperty(_turnLabel, TweenConstants.Scale, Vector2.One, 0.2f)
			.SetTrans(Tween.TransitionType.Cubic)
			.SetEase(Tween.EaseType.Out);

		// Add subtle alpha pulse loop for continuous attention
		var alphaPulseTween = CreateTween();
		alphaPulseTween.SetLoops(3); // Pulse 3 times then stop
		alphaPulseTween.TweenProperty(_turnLabel, TweenConstants.ModulateAlpha, 0.7f, 0.5f);
		alphaPulseTween.TweenProperty(_turnLabel, TweenConstants.ModulateAlpha, 1.0f, 0.5f);
	}

	public override void _ExitTree()
	{
		base._ExitTree();

		// Clean up animation
		_currentTween?.Kill();
	}
}
