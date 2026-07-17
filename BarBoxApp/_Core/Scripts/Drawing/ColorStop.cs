using Godot;

namespace BarBox.Core.Drawing;

/// <summary>
/// A gradient stop positioned by normalized arc length along the path, not by point index:
/// PathFlattener chooses the point count adaptively and DashSplitter changes it, so an
/// index-based stop would not survive either stage.
/// </summary>
public readonly struct ColorStop
{
	/// <summary>Normalized arc length in [0, 1]. Stop arrays must be sorted ascending on this.</summary>
	public readonly float T;

	/// <summary>sRGB-encoded, as authored. OkLab handles the linear conversion internally.</summary>
	public readonly Color Color;

	public ColorStop(float t, Color color)
	{
		T = t;
		Color = color;
	}
}
