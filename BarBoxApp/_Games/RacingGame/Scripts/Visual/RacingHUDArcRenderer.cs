using Godot;
using System;

namespace BarBox.Games.Racing
{
	/// <summary>
	/// Renders colorful, arc-based HUD elements for the racing game including speedometer, countdown timer, and lap progress
	/// Inspired by teenage.engineering design aesthetic with vibrant colors and smooth animations
	/// </summary>
	[GlobalClass]
	public partial class RacingHUDArcRenderer : Node2D
	{
		// ================================================================
		// EXPORT PROPERTIES - HUD STYLING
		// ================================================================

		[ExportCategory("Speedometer Arc")]
		[Export] public float SpeedometerRadius { get; set; } = 40.0f; // 50% smaller
		[Export] public float SpeedometerThickness { get; set; } = 12.0f;
		[Export] public Color SpeedLowColor { get; set; } = new Color(0.0f, 1.0f, 0.53f); // Bright green #00FF88
		[Export] public Color SpeedMidColor { get; set; } = new Color(1.0f, 0.84f, 0.0f); // Golden #FFD700
		[Export] public Color SpeedHighColor { get; set; } = new Color(1.0f, 0.27f, 0.0f); // Orange-red #FF4500
		[Export] public Color SpeedMaxColor { get; set; } = new Color(1.0f, 0.0f, 0.0f); // Bright red #FF0000

		[ExportCategory("Countdown Arc")]
		[Export] public float CountdownRadius { get; set; } = 120.0f;
		[Export] public float CountdownThickness { get; set; } = 8.0f;
		[Export] public Color CountdownColor { get; set; } = new Color(0.0f, 1.0f, 1.0f); // Cyan #00FFFF
		[Export] public Color CountdownGlowColor { get; set; } = new Color(0.0f, 0.8f, 1.0f, 0.5f); // Blue glow

		[ExportCategory("Lap Progress Arc")]
		[Export] public float LapRadius { get; set; } = 45.0f; // 25% smaller than 60
		[Export] public float LapThickness { get; set; } = 10.0f;
		[Export] public Color LapStartColor { get; set; } = new Color(0.61f, 0.35f, 0.71f); // Purple #9B59B6
		[Export] public Color LapEndColor { get; set; } = new Color(0.91f, 0.30f, 0.24f); // Red #E74C3C

		[ExportCategory("HUD Layout")]
		[Export] public Vector2 SpeedometerPosition { get; set; } = new Vector2(120, -100);
		[Export] public Vector2 LapProgressPosition { get; set; } = new Vector2(-120, -100);
		[Export] public Vector2 CountdownPosition { get; set; } = Vector2.Zero;

		// ================================================================
		// PRIVATE FIELDS - STATE TRACKING
		// ================================================================

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

		// Game state
		private float _currentSpeed = 0.0f;
		private float _maxSpeed = 100.0f;
		private int _currentLap = 1;
		private int _targetLaps = 3;
		private string _timeText = "0.0s";
	private bool _isPracticeMode = false;

		// ================================================================
		// PUBLIC PROPERTIES
		// ================================================================

		public bool ShouldRender { get; set; } = true;

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
			_timeText = timeText;
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
			if (!_isCountdownVisible) return;

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
			_glowIntensity = 0.8f + 0.2f * Mathf.Sin(_pulseTime * 3.0f);

			// Queue redraw for smooth animations
			if (ShouldRender)
			{
				QueueRedraw();
			}
		}

		// ================================================================
		// RENDERING METHODS
		// ================================================================

		/// <summary>
		/// Main drawing method
		/// </summary>
		public override void _Draw()
		{
			if (!ShouldRender) return;

			var viewport = GetViewport();
			if (viewport == null) return;

			var screenSize = viewport.GetVisibleRect().Size;

			// Position HUD elements relative to screen corners
			var speedometerPos = new Vector2(SpeedometerPosition.X, screenSize.Y + SpeedometerPosition.Y);
			var lapProgressPos = new Vector2(screenSize.X + LapProgressPosition.X, screenSize.Y + LapProgressPosition.Y);
			var countdownPos = screenSize * 0.5f + CountdownPosition;

			// Draw speedometer arc
			DrawSpeedometer(speedometerPos);

			// Draw lap progress arc
			// Draw lap progress arc (only in time trial mode)
		if (!_isPracticeMode)
		{
			DrawLapProgress(lapProgressPos);
		}

			// Draw countdown arc if visible
			if (_isCountdownVisible)
			{
				DrawCountdown(countdownPos);
			}
		}

		/// <summary>
		/// Draw the speedometer arc with gradient colors
		/// </summary>
		private void DrawSpeedometer(Vector2 position)
		{
			// Background arc (dark)
			var bgColor = new Color(0.2f, 0.2f, 0.3f, 0.6f);
			var bgStartAngle = Mathf.Pi; // 180° (left)
			var bgEndAngle = Mathf.Tau; // 360° (right, equivalent to 0°)
			DrawArc(position, SpeedometerRadius, bgStartAngle, bgEndAngle, 64, bgColor, SpeedometerThickness);

			// Calculate arc angle based on speed (180 degrees total)
			// Follow the same path as background: from 180° to 360°
			var arcStart = Mathf.Pi; // 180° (left)
			var arcEnd = Mathf.Tau; // 360° (right)
			var totalRange = arcEnd - arcStart; // Total 180° range
			var endAngle = arcStart + (totalRange * _currentSpeedPercent);

			if (_currentSpeedPercent > 0.01f)
			{
				// Get color based on speed percentage
				var startAngle = arcStart;
				var speedColor = GetSpeedColor(_currentSpeedPercent);

				// Add glow effect
				var glowColor = speedColor * new Color(1, 1, 1, _glowIntensity * 0.3f);
				DrawArc(position, SpeedometerRadius + 3, startAngle, endAngle, 64, glowColor, SpeedometerThickness + 4);

				// Main arc
				DrawArc(position, SpeedometerRadius, startAngle, endAngle, 64, speedColor, SpeedometerThickness);
			}

			// Speed needle (visual indicator pointing to current speed)
			var needleAngle = endAngle; // Needle points to the end of the progress arc
			var needleInnerRadius = SpeedometerRadius - SpeedometerThickness * 1.5f;
			var needleOuterRadius = SpeedometerRadius + SpeedometerThickness * 1.5f;
			var needleStart = position + Vector2.FromAngle(needleAngle) * needleInnerRadius;
			var needleEnd = position + Vector2.FromAngle(needleAngle) * needleOuterRadius;

			// Draw needle with glow for better visibility
			DrawLine(needleStart, needleEnd, Colors.Black, 5.0f); // Shadow/outline
			DrawLine(needleStart, needleEnd, Colors.White, 3.0f); // Main needle
			
			// Speed text in center
			var speedInt = (int)(_currentSpeed * 0.1f);
			var font = ThemeDB.FallbackFont;
			var fontSize = 24;
			var speedText = speedInt.ToString();
			var textSize = font.GetStringSize(speedText, HorizontalAlignment.Center, -1, fontSize);
			var textPos = position + (Vector2.Left * textSize * 0.5f);

			// Main text
			DrawString(font, textPos, speedText, HorizontalAlignment.Center, -1, fontSize, Colors.White);
		}

		/// <summary>
		/// Draw lap progress arc
		/// </summary>
		private void DrawLapProgress(Vector2 position)
		{
			// Don't show lap counts if we're counting down
			if (_isCountdownVisible)
				return;
			
			// Background arc
			var bgColor = new Color(0.2f, 0.2f, 0.3f, 0.6f);
			DrawArc(position, LapRadius, 0, Mathf.Tau, 64, bgColor, LapThickness); // Full circle background

			if (_currentLapProgress > 0.01f)
			{
				// Progress arc with gradient
				// Progress ring starts from bottom (-90°) and fills clockwise
				var startAngle = -Mathf.Pi * 0.5f; // Start from bottom
				var sweepAngle = Mathf.Tau * _currentLapProgress; // Full circle sweep
				var endAngle = startAngle + sweepAngle;
				var lapColor = LapStartColor.Lerp(LapEndColor, _currentLapProgress);

				// Glow effect
				var glowColor = lapColor * new Color(1, 1, 1, _glowIntensity * 0.4f);
				DrawArc(position, LapRadius + 2, startAngle, endAngle, 64, glowColor, LapThickness + 3);

				// Main arc
				DrawArc(position, LapRadius, startAngle, endAngle, 64, lapColor, LapThickness);
			}

			// Lap text - draw as separate lines for proper newline handling
			var font = ThemeDB.FallbackFont;
			var fontSize = 18;
			var lapLabel = "LAP";
			var lapValue = $"{_currentLap}/{_targetLaps}";

			var lapLabelSize = font.GetStringSize(lapLabel, HorizontalAlignment.Center, -1, fontSize);
			var lapValueSize = font.GetStringSize(lapValue, HorizontalAlignment.Center, -1, fontSize);
			var lineHeight = lapLabelSize.Y;
			var totalHeight = lineHeight * 2;

			// Center the text block accounting for baseline positioning
			var startY = position.Y - totalHeight * 0.5f + lineHeight * 0.75f; // Adjust baseline like countdown
			var lapLabelPos = new Vector2(position.X - lapLabelSize.X * 0.5f, startY);
			var lapValuePos = new Vector2(position.X - lapValueSize.X * 0.5f, startY + lineHeight);

			// Draw both lines
			DrawString(font, lapLabelPos, lapLabel, HorizontalAlignment.Left, -1, fontSize, Colors.White);
			DrawString(font, lapValuePos, lapValue, HorizontalAlignment.Left, -1, fontSize, Colors.White);
		}

		/// <summary>
		/// Draw countdown arc and number
		/// </summary>
		private void DrawCountdown(Vector2 position)
		{
			// Outer glow ring
			var outerGlow = CountdownGlowColor * new Color(1, 1, 1, _glowIntensity * 0.3f);
			// Multiple outer decorative rings
			for (int i = 1; i <= 3; i++)
			{
				var ringRadius = CountdownRadius + 12 + (i * 8);
				var ringAlpha = _glowIntensity * (0.15f / i);
				var ringPulse = 1.0f + 0.2f * Mathf.Sin((_countdownAnimTime + i * 0.5f) * 4.0f);
				var ringColor = CountdownGlowColor * new Color(1, 1, 1, ringAlpha * ringPulse);
				DrawCircle(position, ringRadius, ringColor);
			}

			// Background circle
			var bgColor = new Color(0.1f, 0.1f, 0.2f, 0.8f);
			DrawCircle(position, CountdownRadius, bgColor);

			// Progress arc (decreases as countdown progresses)
			var progressAngle = Mathf.Tau * (1.0f - _countdownProgress);
			if (progressAngle > 0.01f) // Show arc even when very small
			{
				// Animated glow
				var glowPulse = 1.0f + 0.3f * Mathf.Sin(_countdownAnimTime * 8.0f);
				var glowColor = CountdownColor * new Color(1, 1, 1, glowPulse * 0.5f);
				DrawArc(position, CountdownRadius + 4, -Mathf.Pi * 0.5f, -Mathf.Pi * 0.5f + progressAngle, 64, glowColor, CountdownThickness + 6);

				// Main countdown arc
				DrawArc(position, CountdownRadius, -Mathf.Pi * 0.5f, -Mathf.Pi * 0.5f + progressAngle, 64, CountdownColor, CountdownThickness);
			}

			// Countdown number with pulse scale
			if (_countdownNumber > 0)
			{
				var font = ThemeDB.FallbackFont;
				var fontSize = (int)(72 * _countdownPulseScale);
				var numberText = _countdownNumber.ToString();
				var textSize = font.GetStringSize(numberText, HorizontalAlignment.Center, -1, fontSize);
				var textPos = position - textSize * 0.5f;
				textPos.Y += textSize.Y * 0.75f; // Adjust for baseline positioning

				// Outer glow
				var textGlowColor = CountdownColor * new Color(1, 1, 1, 0.8f);
				for (int i = 1; i <= 3; i++)
				{
					var offset = Vector2.One * i * 2;
					DrawString(font, textPos + offset, numberText, HorizontalAlignment.Center, -1, fontSize, textGlowColor * (0.3f / i));
				}

				// Main text
				DrawString(font, textPos, numberText, HorizontalAlignment.Center, -1, fontSize, Colors.White);
			}
			else
			{
				// "GO!" text
				var font = ThemeDB.FallbackFont;
				var fontSize = (int)(60 * _countdownPulseScale);
				var goText = "GO!";
				var textSize = font.GetStringSize(goText, HorizontalAlignment.Center, -1, fontSize);
				var textPos = position - textSize * 0.5f;
				textPos.Y += textSize.Y * 0.75f; // Adjust for baseline positioning

				// Rainbow effect for GO!
				var rainbowColor = new Color(
					0.5f + 0.5f * Mathf.Sin(_countdownAnimTime * 6.0f),
					0.5f + 0.5f * Mathf.Sin(_countdownAnimTime * 6.0f + 2.0f),
					0.5f + 0.5f * Mathf.Sin(_countdownAnimTime * 6.0f + 4.0f)
				);

				// Glow effect
				for (int i = 1; i <= 4; i++)
				{
					var offset = Vector2.One * i * 3;
					DrawString(font, textPos + offset, goText, HorizontalAlignment.Center, -1, fontSize, rainbowColor * (0.4f / i));
				}

				// Main text
				DrawString(font, textPos, goText, HorizontalAlignment.Center, -1, fontSize, Colors.White);
			}
		}

		/// <summary>
		/// Get gradient color based on speed percentage
		/// </summary>
		private Color GetSpeedColor(float speedPercent)
		{
			if (speedPercent <= 0.3f)
			{
				// Green to yellow (0-30%)
				var t = speedPercent / 0.3f;
				return SpeedLowColor.Lerp(SpeedMidColor, t);
			}
			else if (speedPercent <= 0.7f)
			{
				// Yellow to orange (30-70%)
				var t = (speedPercent - 0.3f) / 0.4f;
				return SpeedMidColor.Lerp(SpeedHighColor, t);
			}
			else
			{
				// Orange to red (70-100%)
				var t = (speedPercent - 0.7f) / 0.3f;
				return SpeedHighColor.Lerp(SpeedMaxColor, t);
			}
		}

		// ================================================================
		// UTILITY METHODS
		// ================================================================

		/// <summary>
		/// Check if the renderer should be visible based on viewport
		/// </summary>
		public new bool IsVisible()
		{
			var viewport = GetViewport();
			return viewport != null && ShouldRender;
		}

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
}