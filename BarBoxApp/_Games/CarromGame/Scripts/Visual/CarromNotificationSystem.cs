using Godot;
using System.Collections.Generic;
using System.Linq;

namespace BarBox.Games.Carrom;

/// <summary>
/// Notification types for game events
/// </summary>
public enum NotificationType
{
	TurnStart,        // Sticky until striker hit
	PiecePocketed,    // Timed 2s
	Foul,             // Timed 3s
	QueenPocketed,    // Timed 3s
	QueenCovered,     // Timed 3s
	QueenReturned,    // Timed 2s
	BreakingAttempt,  // Timed 2s
	BreakingSuccess,  // Timed 2s
	BreakingFailed    // Timed 3s
}

/// <summary>
/// Individual notification data
/// </summary>
public class GameNotification
{
	public NotificationType Type { get; set; }
	public string Message { get; set; }
	public bool IsSticky { get; set; }
	public float Duration { get; set; }
	public Color Color { get; set; }

	public GameNotification(NotificationType type, string message, bool isSticky = false, float duration = 2.0f, Color? color = null)
	{
		Type = type;
		Message = message;
		IsSticky = isSticky;
		Duration = duration;
		Color = color ?? Colors.White;
	}
}

/// <summary>
/// Unified notification system for Carrom game events
/// Displays notifications as a vertical stack below the menu bar
/// Oldest notifications at top, newest at bottom
/// Supports both timed (auto-fade) and sticky (wait for striker hit) modes
/// </summary>
[GlobalClass]
public partial class CarromNotificationSystem : CanvasLayer
{
	/// <summary>
	/// Individual notification entry in the stack
	/// Self-manages its lifecycle with fade animations and timer
	/// </summary>
	private partial class NotificationEntry : Panel
	{
		[Signal] public delegate void ReadyToRemoveEventHandler(NotificationEntry entry);

		public GameNotification Data { get; private set; }

		private Label _label;
		private Timer _dismissTimer;
		private Tween _fadeTween;
		private bool _isRemoving = false;

		public NotificationEntry(GameNotification notification)
		{
			Data = notification;
			SetupEntry();
		}

		private void SetupEntry()
		{
			// Set panel size
			CustomMinimumSize = new Vector2(0, ENTRY_HEIGHT);

			// Style the panel
			var styleBox = new StyleBoxFlat();
			styleBox.BgColor = PANEL_BACKGROUND_COLOR;
			styleBox.BorderWidthTop = 1;
			styleBox.BorderWidthBottom = 1;
			styleBox.BorderColor = new Color(0.3f, 0.3f, 0.4f, 1.0f);
			styleBox.CornerRadiusTopLeft = 6;
			styleBox.CornerRadiusTopRight = 6;
			styleBox.CornerRadiusBottomLeft = 6;
			styleBox.CornerRadiusBottomRight = 6;
			AddThemeStyleboxOverride("panel", styleBox);

			// Create label
			_label = new Label();
			_label.Text = Data.Message;
			_label.Modulate = Data.Color;
			_label.HorizontalAlignment = HorizontalAlignment.Center;
			_label.VerticalAlignment = VerticalAlignment.Center;
			_label.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
			_label.AddThemeFontSizeOverride("font_size", 14);

			// Anchor label to fill panel
			_label.AnchorLeft = 0.0f;
			_label.AnchorRight = 1.0f;
			_label.AnchorTop = 0.0f;
			_label.AnchorBottom = 1.0f;
			_label.OffsetLeft = 10;
			_label.OffsetRight = -10;
			_label.OffsetTop = 0;
			_label.OffsetBottom = 0;

			AddChild(_label);

			// Setup timer for timed notifications
			if (!Data.IsSticky)
			{
				_dismissTimer = new Timer();
				_dismissTimer.WaitTime = Data.Duration;
				_dismissTimer.OneShot = true;
				_dismissTimer.Timeout += OnDismissTimeout;
				AddChild(_dismissTimer);
			}

			// Start with zero alpha for fade in
			Modulate = new Color(1, 1, 1, 0);
		}

		public void FadeIn()
		{
			if (_isRemoving) return;

			// Stop any existing fade tween
			_fadeTween?.Kill();

			// Validate this notification entry before creating tween
			if (!GodotObject.IsInstanceValid(this))
			{
				GD.PrintErr("[CarromNotificationSystem] NotificationEntry invalid - cannot create fade tween");
				return;
			}

			// Create fade in tween
			_fadeTween = GetTree().CreateTween();
			if (_fadeTween == null)
			{
				GD.PrintErr("[CarromNotificationSystem] Failed to create fade tween");
				return;
			}

			// Add tweeners first, then configure parallel mode
			var tweener1 = _fadeTween.TweenProperty(this, TweenConstants.ModulateAlpha, 1.0f, 0.3f)
				.SetTrans(Tween.TransitionType.Cubic)
				.SetEase(Tween.EaseType.Out);

			// Slight upward slide for emphasis
			var startY = Position.Y + 10.0f;
			var targetY = Position.Y;
			Position = new Vector2(Position.X, startY);
			var tweener2 = _fadeTween.TweenProperty(this, TweenConstants.Position + ":y", targetY, 0.3f)
				.SetTrans(Tween.TransitionType.Back)
				.SetEase(Tween.EaseType.Out);

			// Only set parallel mode if both tweeners were successfully created
			if (tweener1 != null && tweener2 != null)
			{
				_fadeTween.SetParallel(true);

				// Start timer after fade in completes (for timed notifications)
				if (!Data.IsSticky && _dismissTimer != null)
				{
					_fadeTween.Chain().TweenCallback(Callable.From(() => _dismissTimer?.Start()));
				}
			}
			else
			{
				GD.PrintErr("[CarromNotificationSystem] Failed to create fade tweeners - killing tween");
				_fadeTween.Kill();
				_fadeTween = null;
			}
		}

		public void FadeOut()
		{
			if (_isRemoving) return;
			_isRemoving = true;

			// Stop timer if active
			_dismissTimer?.Stop();

			// Stop any existing fade tween
			_fadeTween?.Kill();

			// Create fade out tween
			_fadeTween = GetTree().CreateTween();
			_fadeTween.TweenProperty(this, TweenConstants.ModulateAlpha, 0.0f, 0.3f)
				.SetTrans(Tween.TransitionType.Cubic)
				.SetEase(Tween.EaseType.In);

			_fadeTween.Finished += OnFadeOutFinished;
		}

		private void OnFadeOutFinished()
		{
			if (IsInstanceValid(this))
			{
				EmitSignal(SignalName.ReadyToRemove, this);
			}
		}

		public void ForceCleanup()
		{
			_isRemoving = true;

			// Stop all active animations and timers
			_fadeTween?.Kill();
			_dismissTimer?.Stop();

			// Emit removal signal
			if (IsInstanceValid(this))
			{
				EmitSignal(SignalName.ReadyToRemove, this);
			}
		}

		private void OnDismissTimeout()
		{
			FadeOut();
		}

		public override void _ExitTree()
		{
			base._ExitTree();

			// Clean up animations and timers
			_fadeTween?.Kill();
			_dismissTimer?.Stop();
		}
	}

	// Styling constants
	private const float ENTRY_HEIGHT = 45.0f;
	private const float ENTRY_SEPARATION = 5.0f;
	private const float PANEL_TOP_OFFSET = 205.0f;
	private const float PANEL_MARGIN = 10.0f;
	private const int MAX_VISIBLE_ENTRIES = 4;

	// Notification type colors
	private readonly static Color COLOR_TURN_START = new Color(0.2f, 0.9f, 0.8f, 1.0f);     // Cyan
	private readonly static Color COLOR_GOOD = new Color(0.2f, 0.9f, 0.3f, 1.0f);            // Green
	private readonly static Color COLOR_FOUL = new Color(0.9f, 0.3f, 0.2f, 1.0f);            // Red
	private readonly static Color COLOR_QUEEN = new Color(1.0f, 0.84f, 0.0f, 1.0f);          // Gold
	private readonly static Color COLOR_BREAKING = new Color(0.6f, 0.4f, 0.9f, 1.0f);        // Purple
	private readonly static Color COLOR_NEUTRAL = new Color(0.7f, 0.7f, 0.7f, 1.0f);         // Gray
	private readonly static Color PANEL_BACKGROUND_COLOR = new Color(0.05f, 0.05f, 0.1f, 0.9f); // Dark blue-black

	// UI Components
	private VBoxContainer _notificationContainer;
	private List<NotificationEntry> _activeEntries = [];

	// Position animation
	private Tween _positionTween;

	public override void _Ready()
	{
		// Set layer to appear below top menu but above game content
		Layer = 75;

		SetupUI();
	}

	private void SetupUI()
	{
		// Create vertical container for notification stack
		_notificationContainer = new VBoxContainer();

		// Set separation between entries
		_notificationContainer.AddThemeConstantOverride("separation", (int)ENTRY_SEPARATION);

		// Anchor to top of screen, full width
		_notificationContainer.AnchorLeft = 0.0f;
		_notificationContainer.AnchorRight = 1.0f;
		_notificationContainer.AnchorTop = 0.0f;
		_notificationContainer.AnchorBottom = 0.0f;

		// Set position
		_notificationContainer.OffsetLeft = PANEL_MARGIN;
		_notificationContainer.OffsetRight = -PANEL_MARGIN;
		_notificationContainer.OffsetTop = PANEL_TOP_OFFSET;

		AddChild(_notificationContainer);
	}

	public void ShowNotification(NotificationType type, string message, bool? isSticky = null, float? duration = null)
	{
		// Create notification data
		var notification = CreateNotification(type, message, isSticky, duration);

		// If stack is full, remove oldest entry
		if (_activeEntries.Count >= MAX_VISIBLE_ENTRIES)
		{
			RemoveOldestEntry();
		}

		// Create new entry
		var entry = new NotificationEntry(notification);
		entry.ReadyToRemove += OnEntryReadyToRemove;

		// Add to container (bottom of stack)
		_notificationContainer.AddChild(entry);
		_activeEntries.Add(entry);

		// Fade in the new entry
		entry.FadeIn();

		// Make container visible if hidden
		if (!Visible)
		{
			Visible = true;
		}
	}

	/// <summary>
	/// Create a notification with default properties based on type
	/// </summary>
	private GameNotification CreateNotification(NotificationType type, string message, bool? isSticky, float? duration)
	{
		// Default properties based on type
		bool defaultSticky = false;
		float defaultDuration = 2.0f;
		Color color = COLOR_NEUTRAL;

		switch (type)
		{
			case NotificationType.TurnStart:
				defaultSticky = true;
				color = COLOR_TURN_START;
				break;

			case NotificationType.PiecePocketed:
				defaultDuration = 2.0f;
				color = COLOR_GOOD;
				break;

			case NotificationType.Foul:
				defaultDuration = 3.0f;
				color = COLOR_FOUL;
				break;

			case NotificationType.QueenPocketed:
			case NotificationType.QueenCovered:
				defaultDuration = 3.0f;
				color = COLOR_QUEEN;
				break;

			case NotificationType.QueenReturned:
				defaultDuration = 2.0f;
				color = COLOR_QUEEN;
				break;

			case NotificationType.BreakingAttempt:
			case NotificationType.BreakingSuccess:
				defaultDuration = 2.0f;
				color = COLOR_BREAKING;
				break;

			case NotificationType.BreakingFailed:
				defaultDuration = 3.0f;
				color = COLOR_BREAKING;
				break;
		}

		return new GameNotification(
			type,
			message,
			isSticky ?? defaultSticky,
			duration ?? defaultDuration,
			color
		);
	}

	private void RemoveOldestEntry()
	{
		if (_activeEntries.Count == 0) return;

		var oldestEntry = _activeEntries[0];
		if (IsInstanceValid(oldestEntry))
		{
			// Force cleanup to immediately remove
			oldestEntry.ForceCleanup();
		}
	}

	private void OnEntryReadyToRemove(NotificationEntry entry)
	{
		// Check if entry is still valid before operating
		if (!IsInstanceValid(entry))
		{
			_activeEntries.Remove(entry);
			return;
		}

		// Capture current positions of all OTHER entries before removal
		var remainingEntries = _activeEntries.Where(e => e != entry && IsInstanceValid(e)).ToList();
		var oldPositions = new Dictionary<NotificationEntry, float>();

		foreach (var e in remainingEntries)
		{
			oldPositions[e] = e.Position.Y;
		}

		// Remove entry from tracking and container
		_activeEntries.Remove(entry);

		// Disconnect signal before freeing to prevent double-call
		if (IsInstanceValid(entry))
		{
			entry.ReadyToRemove -= OnEntryReadyToRemove;
			_notificationContainer.RemoveChild(entry);
			entry.QueueFree();
		}

		// VBoxContainer has now reflowed - animate remaining entries to new positions
		AnimateStackMovement(remainingEntries, oldPositions);

		// Hide container if no entries left
		if (_activeEntries.Count == 0)
		{
			Visible = false;
		}
	}

	private void AnimateStackMovement(List<NotificationEntry> entries, Dictionary<NotificationEntry, float> oldPositions)
	{
		if (entries.Count == 0) return;

		// Stop any existing position tween
		_positionTween?.Kill();

		// Create new position tween
		_positionTween = GetTree().CreateTween();
		if (_positionTween == null)
		{
			GD.PrintErr("[CarromNotificationSystem] Failed to create position tween");
			return;
		}

		bool anyTweenersAdded = false;

		foreach (var entry in entries)
		{
			if (!IsInstanceValid(entry)) continue;

			// Get old and new Y positions
			float oldY = oldPositions[entry];
			float newY = entry.Position.Y;

			// Only tween if position changed
			if (Mathf.Abs(oldY - newY) > 0.1f)
			{
				// Temporarily set to old position
				entry.Position = new Vector2(entry.Position.X, oldY);

				// Tween to new position
				var tweener = _positionTween.TweenProperty(entry, TweenConstants.Position + ":y", newY, 0.25f)
					.SetTrans(Tween.TransitionType.Cubic)
					.SetEase(Tween.EaseType.Out);

				if (tweener != null)
				{
					anyTweenersAdded = true;
				}
			}
		}

		// Only set parallel mode if at least one tweener was added
		if (anyTweenersAdded)
		{
			_positionTween.SetParallel(true);
		}
		else
		{
			// No valid entries to animate - kill the empty tween
			_positionTween.Kill();
			_positionTween = null;
		}
	}

	/// <summary>
	/// Clear sticky notifications (called when striker is hit)
	/// </summary>
	public void ClearStickyNotification()
	{
		// Find all sticky entries (create copy to avoid modification during iteration)
		var stickyEntries = _activeEntries.Where(e => IsInstanceValid(e) && e.Data.IsSticky).ToList();

		foreach (var entry in stickyEntries)
		{
			if (IsInstanceValid(entry))
			{
				entry.FadeOut();
			}
		}
	}

	public void ClearAllNotifications()
	{
		// Stop position animations
		_positionTween?.Kill();

		// Create copy of entries to avoid modification during iteration
		var entriesToClear = _activeEntries.ToList();

		foreach (var entry in entriesToClear)
		{
			if (IsInstanceValid(entry))
			{
				entry.ForceCleanup();
			}
		}

		// Clear the list
		_activeEntries.Clear();

		// Hide container
		Visible = false;
	}

	public new void Show()
	{
		Visible = true;
	}

	public new void Hide()
	{
		// Clear all notifications when hiding
		ClearAllNotifications();
		Visible = false;
	}

	public override void _ExitTree()
	{
		base._ExitTree();

		// Clean up animations
		_positionTween?.Kill();

		// Clean up all entries
		foreach (var entry in _activeEntries)
		{
			if (IsInstanceValid(entry))
			{
				entry.QueueFree();
			}
		}
		_activeEntries.Clear();
	}
}
