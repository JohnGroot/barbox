using System;
using BarBox.Core.Drawing;
using Godot;

namespace BarBox.Games.Racing;

/// <summary>
/// Renders colorful, arc-based HUD elements for the racing game including speedometer, countdown timer, and lap progress
/// Inspired by teenage.engineering design aesthetic with vibrant colors and smooth animations
/// </summary>
[GlobalClass]
public partial class RacingHUDArcRenderer : ShapeCanvas
{
	// ================================================================
	// EXPORT PROPERTIES - HUD STYLING
	// ================================================================
	[ExportCategory("Speedometer Arc")]
	[Export]
	public float SpeedometerRadius { get; set; } = 40.0f; // 50% smaller

	[Export]
	public float SpeedometerThickness { get; set; } = 12.0f;

	[ExportCategory("Countdown Arc")]
	[Export]
	public float CountdownRadius { get; set; } = 120.0f;

	[Export]
	public float CountdownThickness { get; set; } = 8.0f;

	[Export]
	public Color CountdownColor { get; set; } = Palette.Cyan;

	[Export]
	public Color CountdownGlowColor { get; set; } = RacingPalette.CountdownGlow;

	[ExportCategory("Lap Progress Arc")]
	[Export]
	public float LapRadius { get; set; } = 45.0f; // 25% smaller than 60

	[Export]
	public float LapThickness { get; set; } = 10.0f;

	[Export]
	public Color LapStartColor { get; set; } = Palette.Purple;

	[Export]
	public Color LapEndColor { get; set; } = Palette.Red;

	[ExportCategory("HUD Layout")]
	[Export]
	public Vector2 SpeedometerPosition { get; set; } = new Vector2(120, -100);

	[Export]
	public Vector2 LapProgressPosition { get; set; } = new Vector2(-120, -100);

	[Export]
	public Vector2 CountdownPosition { get; set; } = Vector2.Zero;

	// ================================================================
	// PRIVATE FIELDS - STATE TRACKING
	// ================================================================
	private const int CountdownGlowRingCount = 3;

	// Current values for smooth interpolation
	private float _currentSpeedPercent = 0.0f;
	private float _targetSpeedPercent = 0.0f;
	private float _currentLapProgress = 0.0f;
	private float _targetLapProgress = 0.0f;

	// Countdown animation state
	private float _countdownProgress = 0.0f; // 0.0 = full, 1.0 = empty
	private float _countdownPulseScale = 1.0f;
	private bool _isCountdownVisible = false;
	private int _countdownNumber = 0;
	private float _countdownAnimTime = 0.0f;

	// Animation timing
	private float _pulseTime = 0.0f;
	private float _glowIntensity = 1.0f;

	// Cached values for performance optimization
	private Vector2 _cachedScreenSize;
	private bool _screenSizeDirty = true;
	private Font _cachedFont;
	private Vector2 _cachedSpeedometerPos;
	private Vector2 _cachedLapProgressPos;
	private Vector2 _cachedCountdownPos;

	// Speed text + measured size cache - recomputed only when the displayed integer speed changes
	private int _cachedSpeedInt = int.MinValue;
	private string _cachedSpeedText;
	private Vector2 _cachedSpeedTextSize;

	// GetStringSize caches, keyed by the values that determine the measured text
	private Vector2? _cachedLapLabelSize;
	private int _cachedLapValueLap = int.MinValue;
	private int _cachedLapValueTarget = int.MinValue;
	private string _cachedLapValueText;
	private Vector2 _cachedLapValueSize;
	private int _cachedCountdownNumberValue = int.MinValue;
	private int _cachedCountdownNumberFontSize = int.MinValue;
	private string _cachedCountdownNumberText;
	private Vector2 _cachedCountdownNumberSize;
	private int _cachedGoTextFontSize = int.MinValue;
	private Vector2 _cachedGoTextSize;

	// Dirty-flag tracking for QueueRedraw - mirrors the RacingUIState.StateEquals
	// diff pattern: only redraw when the values that feed _Draw actually changed.
	private bool _hasDrawnOnce = false;
	private float _lastDrawnSpeed;
	private float _lastDrawnSpeedPercent;
	private float _lastDrawnLapProgress;
	private float _lastDrawnGlowIntensity;
	private int _lastDrawnCurrentLap;
	private int _lastDrawnTargetLaps;
	private bool _lastDrawnIsPracticeMode;
	private bool _lastDrawnIsCountdownVisible;
	private int _lastDrawnCountdownNumber;
	private float _lastDrawnCountdownProgress;
	private float _lastDrawnCountdownPulseScale;
	private float _lastDrawnCountdownAnimTime;

	// Game state
	private float _currentSpeed = 0.0f;
	private float _maxSpeed = 100.0f;
	private int _currentLap = 1;
	private int _targetLaps = 3;
	private bool _isPracticeMode = false;

	// Retained shapes, committed once and mutated per push. All static-bucket (no Dynamic()): the
	// Lerp-eased speed/lap percentages converge below HasVisualStateChanged's epsilons within a
	// few frames of steady state, so redraws genuinely stop — Dynamic() would not improve on that,
	// and there is no unrelated heavy static geometry in this canvas for the bucket split to
	// protect. Each gauge's shapes are committed at a local Center/points around Vector2.Zero and
	// positioned on screen via SetTransform, so a screen resize only needs a transform update, not
	// re-committing geometry.
	private Shape _speedoBgShape;
	private Shape _speedoProgressShape;
	private Shape _needleShadowShape;
	private Shape _needleShape;
	private Shape _lapBgShape;
	private Shape _lapProgressShape;
	private Shape _countdownBgShape;
	private Shape[] _countdownGlowShapes;
	private Shape _countdownProgressShape;

	// ================================================================
	// PUBLIC PROPERTIES
	// ================================================================
	public bool ShouldRender { get; set; } = true;

	// ================================================================
	// LIFECYCLE
	// ================================================================

	/// <summary>
	/// ShapeCanvas._Ready() calls SetProcess(_childFlushPending), which is false until a Dynamic()
	/// shape is registered — this canvas has none, so without this override _Process would never
	/// run at all and no animation/HUD state would ever be pushed.
	/// </summary>
	public override void _Ready()
	{
		base._Ready();
		SetProcess(true);
	}

	public override void _Notification(int what)
	{
		base._Notification(what);
		if (what == NotificationWMSizeChanged)
		{
			_screenSizeDirty = true;
		}
	}

	private void UpdateCachedScreenData()
	{
		if (!_screenSizeDirty)
		{
			return;
		}

		var viewport = GetViewport();
		if (viewport == null)
		{
			return;
		}

		_cachedScreenSize = viewport.GetVisibleRect().Size;
		_cachedSpeedometerPos = new Vector2(SpeedometerPosition.X, _cachedScreenSize.Y + SpeedometerPosition.Y);
		_cachedLapProgressPos = new Vector2(_cachedScreenSize.X + LapProgressPosition.X, _cachedScreenSize.Y + LapProgressPosition.Y);
		_cachedCountdownPos = (_cachedScreenSize * 0.5f) + CountdownPosition;
		_screenSizeDirty = false;
	}

	// ================================================================
	// SHAPE SETUP
	// ================================================================

	/// <summary>
	/// Simplifications from the original per-gauge pulsing glow halos: every ring background here
	/// shares one HudTrack-derived backdrop color (the originals used two close-but-distinct
	/// literals), and only the countdown ring keeps decorative glow shapes (its three outer
	/// pulsing rings) — the speedo/lap/countdown progress arcs no longer have a separate glow pass
	/// behind them.
	/// </summary>
	private void EnsureShapesCommitted()
	{
		if (_speedoBgShape != null)
		{
			return;
		}

		_speedoBgShape = Build().Arc(Vector2.Zero, SpeedometerRadius, Mathf.Pi, Mathf.Tau)
			.Stroke(HudRingStyle(SpeedometerThickness))
			.Commit();

		_speedoProgressShape = Build().Arc(Vector2.Zero, SpeedometerRadius, Mathf.Pi, Mathf.Pi).Hidden()
			.Stroke(VectorStyles.GaugeArc with { Width = SpeedometerThickness, ColorStops = RacingPalette.SpeedGradient, Cap = CapMode.Butt })
			.Commit();

		// Unlike the progress arcs/rings, the needle has no visibility condition — it renders
		// unconditionally whenever the speedometer does, so it must not be committed Hidden().
		_needleShadowShape = Build().Polyline([Vector2.Zero, Vector2.Zero])
			.Stroke(new StrokeStyle { Width = 5f, Color = Palette.Ink })
			.Commit();

		_needleShape = Build().Polyline([Vector2.Zero, Vector2.Zero])
			.Stroke(new StrokeStyle { Width = 3f, Color = Palette.White })
			.Commit();

		_lapBgShape = Build().Arc(Vector2.Zero, LapRadius, 0f, Mathf.Tau)
			.Stroke(HudRingStyle(LapThickness))
			.Commit();

		_lapProgressShape = Build().Arc(Vector2.Zero, LapRadius, 0f, 0f).Hidden()
			.Stroke(new StrokeStyle { Width = LapThickness, Color = LapStartColor, Cap = CapMode.Butt })
			.Commit();

		_countdownBgShape = Build().Circle(Vector2.Zero, CountdownRadius).Hidden()
			.Fill(RacingPalette.HudTrack * new Color(1, 1, 1, 0.8f))
			.Commit();

		_countdownGlowShapes = new Shape[CountdownGlowRingCount];
		for (int i = 1; i <= CountdownGlowRingCount; i++)
		{
			var ringRadius = CountdownRadius + 12f + (i * 8f);
			_countdownGlowShapes[i - 1] = Build().Circle(Vector2.Zero, ringRadius).Hidden()
				.Fill(CountdownGlowColor)
				.Commit();
		}

		_countdownProgressShape = Build().Arc(Vector2.Zero, CountdownRadius, -Mathf.Pi * 0.5f, -Mathf.Pi * 0.5f).Hidden()
			.Stroke(new StrokeStyle { Width = CountdownThickness, Color = CountdownColor, Cap = CapMode.Butt })
			.Commit();
	}

	/// <summary>Dark HUD gauge background ring stroke; width varies per gauge (speedo/lap).</summary>
	private static StrokeStyle HudRingStyle(float width)
	{
		return new StrokeStyle
		{
			Width = width,
			Color = RacingPalette.HudTrack * new Color(1, 1, 1, 0.6f),
			Cap = CapMode.Butt,
		};
	}

	// ================================================================
	// UPDATE METHODS
	// ================================================================

	/// <summary>
	/// Update HUD state from racing game data
	/// </summary>
	public void UpdateHUDState(float currentSpeed, float maxSpeed, int currentLap, int targetLaps, float lapProgress, string timeText, bool isPracticeMode = false)
	{
		_currentSpeed = currentSpeed;
		_maxSpeed = maxSpeed;
		_currentLap = currentLap;
		_targetLaps = targetLaps;
		_isPracticeMode = isPracticeMode;

		// Update target values for smooth interpolation
		_targetSpeedPercent = Mathf.Clamp(currentSpeed / maxSpeed, 0.0f, 1.0f);
		_targetLapProgress = Mathf.Clamp(lapProgress, 0.0f, 1.0f);
	}

	/// <summary>
	/// Start countdown animation
	/// </summary>
	public void StartCountdown(int countdownNumber, float countdownProgress)
	{
		_isCountdownVisible = true;
		_countdownNumber = countdownNumber;
		_countdownProgress = countdownProgress;
		_countdownPulseScale = 1.2f; // Start with pulse
		_countdownAnimTime = 0.0f;
	}

	/// <summary>
	/// Update countdown animation
	/// </summary>
	public void UpdateCountdown(int countdownNumber, float countdownProgress)
	{
		if (!_isCountdownVisible)
		{
			return;
		}

		// Trigger pulse when number changes
		if (_countdownNumber != countdownNumber)
		{
			_countdownNumber = countdownNumber;
			_countdownPulseScale = 1.3f;
		}

		_countdownProgress = countdownProgress;
	}

	/// <summary>
	/// Hide countdown display
	/// </summary>
	public void HideCountdown()
	{
		_isCountdownVisible = false;
		_countdownProgress = 0.0f;
	}

	/// <summary>
	/// Update animations and smooth transitions
	/// </summary>
	public override void _Process(double delta)
	{
		base._Process(delta);

		float deltaF = (float)delta;
		_pulseTime += deltaF;
		_countdownAnimTime += deltaF;

		// Smooth interpolation for speedometer
		_currentSpeedPercent = Mathf.Lerp(_currentSpeedPercent, _targetSpeedPercent, deltaF * 8.0f);

		// Smooth interpolation for lap progress
		_currentLapProgress = Mathf.Lerp(_currentLapProgress, _targetLapProgress, deltaF * 6.0f);

		// Countdown pulse animation
		if (_isCountdownVisible)
		{
			_countdownPulseScale = Mathf.Lerp(_countdownPulseScale, 1.0f, deltaF * 6.0f);
		}

		// Global glow pulse for visual interest
		_glowIntensity = 0.8f + (0.2f * Mathf.Sin(_pulseTime * 3.0f));

		// Push shape state, and with it a QueueRedraw for the text pass, only when something
		// visible actually changed.
		if (ShouldRender && HasVisualStateChanged())
		{
			UpdateCachedScreenData();
			if (_cachedScreenSize != Vector2.Zero)
			{
				EnsureShapesCommitted();
				PushShapeState();
			}
		}
	}

	/// <summary>
	/// Compare current animated/state values against what was last drawn.
	/// Follows the same diff-based philosophy as RacingUIState.StateEquals.
	/// </summary>
	private bool HasVisualStateChanged()
	{
		if (!_hasDrawnOnce)
		{
			return true;
		}

		return Math.Abs(_currentSpeed - _lastDrawnSpeed) > 0.01f
			|| Math.Abs(_currentSpeedPercent - _lastDrawnSpeedPercent) > 0.0005f
			|| Math.Abs(_currentLapProgress - _lastDrawnLapProgress) > 0.0005f

			// _glowIntensity only feeds the countdown ring's glow rings (PushCountdownShapes) — gating
			// it here, like the _countdownAnimTime check below, is what lets redraws actually stop
			// once the Lerp-eased values settle outside a countdown; ungated, its continuous sine wave
			// would keep this predicate true almost every frame regardless of anything else changing.
			|| (_isCountdownVisible && Math.Abs(_glowIntensity - _lastDrawnGlowIntensity) > 0.001f)
			|| _currentLap != _lastDrawnCurrentLap
			|| _targetLaps != _lastDrawnTargetLaps
			|| _isPracticeMode != _lastDrawnIsPracticeMode
			|| _isCountdownVisible != _lastDrawnIsCountdownVisible
			|| _countdownNumber != _lastDrawnCountdownNumber
			|| Math.Abs(_countdownProgress - _lastDrawnCountdownProgress) > 0.0005f
			|| Math.Abs(_countdownPulseScale - _lastDrawnCountdownPulseScale) > 0.0005f
			|| (_isCountdownVisible && Math.Abs(_countdownAnimTime - _lastDrawnCountdownAnimTime) > 0.0001f);
	}

	/// <summary>
	/// Snapshot the values that drove this draw call, for the next dirty check.
	/// </summary>
	private void RecordDrawnState()
	{
		_hasDrawnOnce = true;
		_lastDrawnSpeed = _currentSpeed;
		_lastDrawnSpeedPercent = _currentSpeedPercent;
		_lastDrawnLapProgress = _currentLapProgress;
		_lastDrawnGlowIntensity = _glowIntensity;
		_lastDrawnCurrentLap = _currentLap;
		_lastDrawnTargetLaps = _targetLaps;
		_lastDrawnIsPracticeMode = _isPracticeMode;
		_lastDrawnIsCountdownVisible = _isCountdownVisible;
		_lastDrawnCountdownNumber = _countdownNumber;
		_lastDrawnCountdownProgress = _countdownProgress;
		_lastDrawnCountdownPulseScale = _countdownPulseScale;
		_lastDrawnCountdownAnimTime = _countdownAnimTime;
	}

	// ================================================================
	// SHAPE PUSHES
	// ================================================================
	private void PushShapeState()
	{
		// Shape.SetTransform has its own equality guard, so this only actually dirties anything
		// on a screen resize (when the cached positions change).
		var speedoTransform = new Transform2D(0f, _cachedSpeedometerPos);
		_speedoBgShape.SetTransform(speedoTransform);
		_speedoProgressShape.SetTransform(speedoTransform);
		_needleShadowShape.SetTransform(speedoTransform);
		_needleShape.SetTransform(speedoTransform);

		var lapTransform = new Transform2D(0f, _cachedLapProgressPos);
		_lapBgShape.SetTransform(lapTransform);
		_lapProgressShape.SetTransform(lapTransform);

		var countdownTransform = new Transform2D(0f, _cachedCountdownPos);
		_countdownBgShape.SetTransform(countdownTransform);
		foreach (Shape glow in _countdownGlowShapes)
		{
			glow.SetTransform(countdownTransform);
		}

		_countdownProgressShape.SetTransform(countdownTransform);

		PushSpeedometerShapes();

		// Don't show lap counts if we're in practice mode or counting down.
		bool lapVisible = !_isPracticeMode && !_isCountdownVisible;
		_lapBgShape.SetVisible(lapVisible);
		if (lapVisible)
		{
			PushLapShapes();
		}
		else
		{
			_lapProgressShape.SetVisible(false);
		}

		_countdownBgShape.SetVisible(_isCountdownVisible);
		foreach (Shape glow in _countdownGlowShapes)
		{
			glow.SetVisible(_isCountdownVisible);
		}

		if (_isCountdownVisible)
		{
			PushCountdownShapes();
		}
		else
		{
			_countdownProgressShape.SetVisible(false);
		}
	}

	private void PushSpeedometerShapes()
	{
		// Follow the same path as the background: from 180deg to 360deg.
		var arcStart = Mathf.Pi;
		var arcEnd = Mathf.Tau;
		var totalRange = arcEnd - arcStart;
		var endAngle = arcStart + (totalRange * _currentSpeedPercent);

		bool progressVisible = _currentSpeedPercent > 0.01f;
		_speedoProgressShape.SetVisible(progressVisible);
		if (progressVisible)
		{
			_speedoProgressShape.SetArc(arcStart, endAngle);
		}

		// Needle points to the end of the progress arc.
		var needleAngle = endAngle;
		var needleInnerRadius = SpeedometerRadius - (SpeedometerThickness * 1.5f);
		var needleOuterRadius = SpeedometerRadius + (SpeedometerThickness * 1.5f);
		var needleDirection = Vector2.FromAngle(needleAngle);
		var needleStart = needleDirection * needleInnerRadius;
		var needleEnd = needleDirection * needleOuterRadius;

		_needleShadowShape.SetPoints([needleStart, needleEnd]);
		_needleShape.SetPoints([needleStart, needleEnd]);
	}

	private void PushLapShapes()
	{
		bool progressVisible = _currentLapProgress > 0.01f;
		_lapProgressShape.SetVisible(progressVisible);
		if (progressVisible)
		{
			// Progress ring starts from bottom (-90deg) and fills clockwise.
			var startAngle = -Mathf.Pi * 0.5f;
			var sweepAngle = Mathf.Tau * _currentLapProgress;
			var endAngle = startAngle + sweepAngle;
			var lapColor = LapStartColor.Lerp(LapEndColor, _currentLapProgress);

			_lapProgressShape.SetArc(startAngle, endAngle);
			_lapProgressShape.SetStrokeColor(lapColor);
		}
	}

	private void PushCountdownShapes()
	{
		for (int i = 1; i <= CountdownGlowRingCount; i++)
		{
			var ringAlpha = _glowIntensity * (0.15f / i);
			var ringPulse = 1.0f + (0.2f * Mathf.Sin((_countdownAnimTime + (i * 0.5f)) * 4.0f));
			_countdownGlowShapes[i - 1].SetFillColor(CountdownGlowColor * new Color(1, 1, 1, ringAlpha * ringPulse));
		}

		// Progress arc decreases as the countdown progresses.
		var progressAngle = Mathf.Tau * (1.0f - _countdownProgress);
		bool progressVisible = progressAngle > 0.01f;
		_countdownProgressShape.SetVisible(progressVisible);
		if (progressVisible)
		{
			_countdownProgressShape.SetArc(-Mathf.Pi * 0.5f, (-Mathf.Pi * 0.5f) + progressAngle);
		}
	}

	// ================================================================
	// TEXT RENDERING
	// ================================================================

	/// <summary>
	/// Draws HUD text. ShapeCanvas has no text primitive, so this is the one place immediate-mode
	/// drawing and retained shapes coexist on this node — base._Draw() must run first to rebuild
	/// and upload the static shape bucket the rest of this override draws text on top of.
	/// </summary>
	public override void _Draw()
	{
		base._Draw();

		if (!ShouldRender)
		{
			return;
		}

		UpdateCachedScreenData();

		if (_cachedScreenSize == Vector2.Zero)
		{
			return;
		}

		_cachedFont ??= LoadInterFont();

		DrawSpeedText(_cachedSpeedometerPos);

		if (!_isPracticeMode && !_isCountdownVisible)
		{
			DrawLapText(_cachedLapProgressPos);
		}

		if (_isCountdownVisible)
		{
			DrawCountdownText(_cachedCountdownPos);
		}

		RecordDrawnState();
	}

	private static Font LoadInterFont()
	{
		var fontFile = GD.Load<FontFile>("res://_Core/Fonts/Inter/InterDisplay-SemiBold.ttf");
		var fontVariation = new FontVariation();
		fontVariation.SetBaseFont(fontFile);
		return fontVariation;
	}

	private void DrawSpeedText(Vector2 position)
	{
		var speedInt = (int)(_currentSpeed * 0.1f);
		var fontSize = 24;
		if (speedInt != _cachedSpeedInt)
		{
			_cachedSpeedInt = speedInt;
			_cachedSpeedText = speedInt.ToString();
			_cachedSpeedTextSize = _cachedFont.GetStringSize(_cachedSpeedText, HorizontalAlignment.Center, -1, fontSize);
		}

		var speedText = _cachedSpeedText;
		var textSize = _cachedSpeedTextSize;
		var textPos = position + (Vector2.Left * textSize * 0.5f);

		DrawString(_cachedFont, textPos, speedText, HorizontalAlignment.Center, -1, fontSize, Palette.White);
	}

	private void DrawLapText(Vector2 position)
	{
		var fontSize = 18;
		var lapLabel = "LAP";

		// "LAP" and its font size never change, so this is measured once and reused
		_cachedLapLabelSize ??= _cachedFont.GetStringSize(lapLabel, HorizontalAlignment.Center, -1, fontSize);
		var lapLabelSize = _cachedLapLabelSize.Value;

		if (_currentLap != _cachedLapValueLap || _targetLaps != _cachedLapValueTarget)
		{
			_cachedLapValueLap = _currentLap;
			_cachedLapValueTarget = _targetLaps;
			_cachedLapValueText = $"{_currentLap}/{_targetLaps}";
			_cachedLapValueSize = _cachedFont.GetStringSize(_cachedLapValueText, HorizontalAlignment.Center, -1, fontSize);
		}

		var lapValue = _cachedLapValueText;
		var lapValueSize = _cachedLapValueSize;
		var lineHeight = lapLabelSize.Y;
		var totalHeight = lineHeight * 2;

		// Center the text block accounting for baseline positioning
		var startY = position.Y - (totalHeight * 0.5f) + (lineHeight * 0.75f); // Adjust baseline like countdown
		var lapLabelPos = new Vector2(position.X - (lapLabelSize.X * 0.5f), startY);
		var lapValuePos = new Vector2(position.X - (lapValueSize.X * 0.5f), startY + lineHeight);

		DrawString(_cachedFont, lapLabelPos, lapLabel, HorizontalAlignment.Left, -1, fontSize, Palette.White);
		DrawString(_cachedFont, lapValuePos, lapValue, HorizontalAlignment.Left, -1, fontSize, Palette.White);
	}

	private void DrawCountdownText(Vector2 position)
	{
		if (_countdownNumber > 0)
		{
			var fontSize = (int)(72 * _countdownPulseScale);
			if (_countdownNumber != _cachedCountdownNumberValue || fontSize != _cachedCountdownNumberFontSize)
			{
				_cachedCountdownNumberValue = _countdownNumber;
				_cachedCountdownNumberFontSize = fontSize;
				_cachedCountdownNumberText = _countdownNumber.ToString();
				_cachedCountdownNumberSize = _cachedFont.GetStringSize(_cachedCountdownNumberText, HorizontalAlignment.Center, -1, fontSize);
			}

			var numberText = _cachedCountdownNumberText;
			var textSize = _cachedCountdownNumberSize;
			var textPos = position - (textSize * 0.5f);
			textPos.Y += textSize.Y * 0.75f; // Adjust for baseline positioning

			// Outer glow
			var textGlowColor = CountdownColor * new Color(1, 1, 1, 0.8f);
			for (int i = 1; i <= 3; i++)
			{
				var offset = Vector2.One * i * 2;
				DrawString(_cachedFont, textPos + offset, numberText, HorizontalAlignment.Center, -1, fontSize, textGlowColor * (0.3f / i));
			}

			// Main text
			DrawString(_cachedFont, textPos, numberText, HorizontalAlignment.Center, -1, fontSize, Palette.White);
		}
		else
		{
			// "GO!" text - use cached font and measured size ("GO!" itself never changes)
			var fontSize = (int)(60 * _countdownPulseScale);
			var goText = "GO!";
			if (fontSize != _cachedGoTextFontSize)
			{
				_cachedGoTextFontSize = fontSize;
				_cachedGoTextSize = _cachedFont.GetStringSize(goText, HorizontalAlignment.Center, -1, fontSize);
			}

			var textSize = _cachedGoTextSize;
			var textPos = position - (textSize * 0.5f);
			textPos.Y += textSize.Y * 0.75f; // Adjust for baseline positioning

			// Rainbow effect for GO!
			var rainbowColor = new Color(
				0.5f + (0.5f * Mathf.Sin(_countdownAnimTime * 6.0f)),
				0.5f + (0.5f * Mathf.Sin((_countdownAnimTime * 6.0f) + 2.0f)),
				0.5f + (0.5f * Mathf.Sin((_countdownAnimTime * 6.0f) + 4.0f)));

			// Glow effect
			for (int i = 1; i <= 4; i++)
			{
				var offset = Vector2.One * i * 3;
				DrawString(_cachedFont, textPos + offset, goText, HorizontalAlignment.Center, -1, fontSize, rainbowColor * (0.4f / i));
			}

			// Main text
			DrawString(_cachedFont, textPos, goText, HorizontalAlignment.Center, -1, fontSize, Palette.White);
		}
	}

	// ================================================================
	// UTILITY METHODS
	// ================================================================

	/// <summary>
	/// Reset all animation states
	/// </summary>
	public void ResetAnimations()
	{
		_currentSpeedPercent = 0.0f;
		_targetSpeedPercent = 0.0f;
		_currentLapProgress = 0.0f;
		_targetLapProgress = 0.0f;
		_countdownProgress = 0.0f;
		_countdownPulseScale = 1.0f;
		_isCountdownVisible = false;
		_pulseTime = 0.0f;
	}
}
