using System.Collections.Generic;
using System.Linq;
using Godot;

namespace BarBox.Core.Autoloads;

/// <summary>
/// Default severity presets for <see cref="NotificationService.Show"/> - each
/// carries a default color/sticky/duration a caller can still override.
/// </summary>
public enum NotificationSeverity
{
	Info,
	Success,
	Warning,
	Error,
}

/// <summary>
/// Global, stacking, self-dismissing notification overlay. Promoted from
/// Carrom's CarromNotificationSystem so every game shares one implementation
/// instead of hand-rolling error/status UI. Displays as a vertical stack
/// below the top menu bar; oldest at top, newest at bottom. Supports both
/// timed (auto-fade) and sticky (dismissed explicitly via <see cref="ClearSticky"/>) entries.
/// </summary>
public partial class NotificationService : AutoloadBase
{
	/// <summary>
	/// Individual notification entry in the stack. Self-manages its lifecycle
	/// with fade animations and an optional dismiss timer.
	/// </summary>
	private partial class NotificationEntry : Panel
	{
		[Signal]
		public delegate void ReadyToRemoveEventHandler(NotificationEntry entry);

		public bool IsSticky { get; }

		private readonly string _message;
		private readonly float _duration;
		private readonly Color _color;

		private Label _label;
		private Timer _dismissTimer;
		private Tween _fadeTween;
		private bool _isRemoving = false;

		public NotificationEntry(string message, Color color, bool isSticky, float duration)
		{
			_message = message;
			_color = color;
			IsSticky = isSticky;
			_duration = duration;
			SetupEntry();
		}

		private void SetupEntry()
		{
			CustomMinimumSize = new Vector2(0, ENTRY_HEIGHT);

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

			_label = new Label();
			_label.Text = _message;
			_label.Modulate = _color;
			_label.HorizontalAlignment = HorizontalAlignment.Center;
			_label.VerticalAlignment = VerticalAlignment.Center;
			_label.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
			_label.AddThemeFontSizeOverride("font_size", 14);

			_label.AnchorLeft = 0.0f;
			_label.AnchorRight = 1.0f;
			_label.AnchorTop = 0.0f;
			_label.AnchorBottom = 1.0f;
			_label.OffsetLeft = 10;
			_label.OffsetRight = -10;
			_label.OffsetTop = 0;
			_label.OffsetBottom = 0;

			AddChild(_label);

			if (!IsSticky)
			{
				_dismissTimer = new Timer();
				_dismissTimer.WaitTime = _duration;
				_dismissTimer.OneShot = true;
				_dismissTimer.Timeout += OnDismissTimeout;
				AddChild(_dismissTimer);
			}

			Modulate = new Color(1, 1, 1, 0);
		}

		public void FadeIn()
		{
			if (_isRemoving)
			{
				return;
			}

			_fadeTween?.Kill();

			if (!GodotObject.IsInstanceValid(this))
			{
				GD.PrintErr("[NotificationService] NotificationEntry invalid - cannot create fade tween");
				return;
			}

			_fadeTween = GetTree().CreateTween();
			if (_fadeTween == null)
			{
				GD.PrintErr("[NotificationService] Failed to create fade tween");
				return;
			}

			var tweener1 = _fadeTween.TweenProperty(this, TweenConstants.ModulateAlpha, 1.0f, 0.3f)
				.SetTrans(Tween.TransitionType.Cubic)
				.SetEase(Tween.EaseType.Out);

			var startY = Position.Y + 10.0f;
			var targetY = Position.Y;
			Position = new Vector2(Position.X, startY);
			var tweener2 = _fadeTween.TweenProperty(this, TweenConstants.Position + ":y", targetY, 0.3f)
				.SetTrans(Tween.TransitionType.Back)
				.SetEase(Tween.EaseType.Out);

			if (tweener1 != null && tweener2 != null)
			{
				_fadeTween.SetParallel(true);

				if (!IsSticky && _dismissTimer != null)
				{
					_fadeTween.Chain().TweenCallback(Callable.From(() => _dismissTimer?.Start()));
				}
			}
			else
			{
				GD.PrintErr("[NotificationService] Failed to create fade tweeners - killing tween");
				_fadeTween.Kill();
				_fadeTween = null;
			}
		}

		public void FadeOut()
		{
			if (_isRemoving)
			{
				return;
			}

			_isRemoving = true;

			_dismissTimer?.Stop();
			_fadeTween?.Kill();

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

			_fadeTween?.Kill();
			_dismissTimer?.Stop();

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

	// Below UIManager's LAYER_GAME_UI (100, top menu bar) so notifications sit
	// under the menu bar; above default game content (layer 0).
	private const int LAYER = 90;

	private static readonly Color COLOR_INFO = new Color(0.7f, 0.7f, 0.7f, 1.0f);
	private static readonly Color COLOR_SUCCESS = new Color(0.2f, 0.9f, 0.3f, 1.0f);
	private static readonly Color COLOR_WARNING = new Color(1.0f, 0.7f, 0.2f, 1.0f);
	private static readonly Color COLOR_ERROR = new Color(0.9f, 0.3f, 0.2f, 1.0f);
	private static readonly Color PANEL_BACKGROUND_COLOR = new Color(0.05f, 0.05f, 0.1f, 0.9f);

	private CanvasLayer _layer;
	private VBoxContainer _notificationContainer;
	private readonly List<NotificationEntry> _activeEntries = [];
	private Tween _positionTween;

	public static NotificationService GetInstance() => GetAutoload<NotificationService>();

	protected override void OnServiceEnterTree()
	{
		_layer = new CanvasLayer { Layer = LAYER };
		AddChild(_layer);

		_notificationContainer = new VBoxContainer();
		_notificationContainer.AddThemeConstantOverride("separation", (int)ENTRY_SEPARATION);

		_notificationContainer.AnchorLeft = 0.0f;
		_notificationContainer.AnchorRight = 1.0f;
		_notificationContainer.AnchorTop = 0.0f;
		_notificationContainer.AnchorBottom = 0.0f;

		_notificationContainer.OffsetLeft = PANEL_MARGIN;
		_notificationContainer.OffsetRight = -PANEL_MARGIN;
		_notificationContainer.OffsetTop = PANEL_TOP_OFFSET;

		_layer.AddChild(_notificationContainer);
	}

	/// <summary>
	/// Show a notification. <paramref name="severity"/> supplies a default
	/// color/sticky/duration; pass <paramref name="color"/>/<paramref name="sticky"/>/<paramref name="duration"/>
	/// to override any of them (used by games with their own severity taxonomy).
	/// </summary>
	public void Show(string message, NotificationSeverity severity = NotificationSeverity.Info, Color? color = null, bool? sticky = null, float? duration = null)
	{
		var (defaultColor, defaultSticky, defaultDuration) = severity switch
		{
			NotificationSeverity.Success => (COLOR_SUCCESS, false, 2.0f),
			NotificationSeverity.Warning => (COLOR_WARNING, false, 3.0f),
			NotificationSeverity.Error => (COLOR_ERROR, true, 3.0f),
			_ => (COLOR_INFO, false, 2.0f),
		};

		if (_activeEntries.Count >= MAX_VISIBLE_ENTRIES)
		{
			RemoveOldestEntry();
		}

		var entry = new NotificationEntry(message, color ?? defaultColor, sticky ?? defaultSticky, duration ?? defaultDuration);
		entry.ReadyToRemove += OnEntryReadyToRemove;

		_notificationContainer.AddChild(entry);
		_activeEntries.Add(entry);

		entry.FadeIn();
	}

	private void RemoveOldestEntry()
	{
		if (_activeEntries.Count == 0)
		{
			return;
		}

		var oldestEntry = _activeEntries[0];
		if (IsInstanceValid(oldestEntry))
		{
			oldestEntry.ForceCleanup();
		}
	}

	private void OnEntryReadyToRemove(NotificationEntry entry)
	{
		if (!IsInstanceValid(entry))
		{
			_activeEntries.Remove(entry);
			return;
		}

		var remainingEntries = _activeEntries.Where(e => e != entry && IsInstanceValid(e)).ToList();
		var oldPositions = new Dictionary<NotificationEntry, float>();

		foreach (var e in remainingEntries)
		{
			oldPositions[e] = e.Position.Y;
		}

		_activeEntries.Remove(entry);

		if (IsInstanceValid(entry))
		{
			entry.ReadyToRemove -= OnEntryReadyToRemove;
			_notificationContainer.RemoveChild(entry);
			entry.QueueFree();
		}

		AnimateStackMovement(remainingEntries, oldPositions);
	}

	private void AnimateStackMovement(List<NotificationEntry> entries, Dictionary<NotificationEntry, float> oldPositions)
	{
		if (entries.Count == 0)
		{
			return;
		}

		_positionTween?.Kill();

		_positionTween = GetTree().CreateTween();
		if (_positionTween == null)
		{
			GD.PrintErr("[NotificationService] Failed to create position tween");
			return;
		}

		bool anyTweenersAdded = false;

		foreach (var entry in entries)
		{
			if (!IsInstanceValid(entry))
			{
				continue;
			}

			float oldY = oldPositions[entry];
			float newY = entry.Position.Y;

			if (Mathf.Abs(oldY - newY) > 0.1f)
			{
				entry.Position = new Vector2(entry.Position.X, oldY);

				var tweener = _positionTween.TweenProperty(entry, TweenConstants.Position + ":y", newY, 0.25f)
					.SetTrans(Tween.TransitionType.Cubic)
					.SetEase(Tween.EaseType.Out);

				if (tweener != null)
				{
					anyTweenersAdded = true;
				}
			}
		}

		if (anyTweenersAdded)
		{
			_positionTween.SetParallel(true);
		}
		else
		{
			_positionTween.Kill();
			_positionTween = null;
		}
	}

	/// <summary>
	/// Dismiss all sticky notifications (e.g. a turn-start prompt cleared once the player acts).
	/// </summary>
	public void ClearSticky()
	{
		var stickyEntries = _activeEntries.Where(e => IsInstanceValid(e) && e.IsSticky).ToList();

		foreach (var entry in stickyEntries)
		{
			if (IsInstanceValid(entry))
			{
				entry.FadeOut();
			}
		}
	}

	public void ClearAll()
	{
		_positionTween?.Kill();

		var entriesToClear = _activeEntries.ToList();

		foreach (var entry in entriesToClear)
		{
			if (IsInstanceValid(entry))
			{
				entry.ForceCleanup();
			}
		}

		_activeEntries.Clear();
	}

	protected override void OnServiceDestroyed()
	{
		_positionTween?.Kill();

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
