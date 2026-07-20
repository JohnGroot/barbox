using Godot;

namespace BarBox.Core.Drawing;

/// <summary>
/// Stateless periodic-motion math for continuous cues (glow pulses, color cycles). Godot's Tween
/// already covers one-shot and looping animation toward a fixed end value (draw-on sweeps,
/// fades) via TweenMethod/TweenProperty against a Shape's plain setters — this is for the
/// different shape of problem Tween doesn't cover: an unbounded oscillation driven by a
/// caller-owned clock.
/// </summary>
public static class Oscillator
{
	/// <summary>bias + amplitude * sin(time * frequency + phaseRad). Pure; no per-instance state.</summary>
	public static float Pulse(float time, float frequency, float bias, float amplitude, float phaseRad = 0f)
	{
		return bias + (amplitude * Mathf.Sin((time * frequency) + phaseRad));
	}
}
