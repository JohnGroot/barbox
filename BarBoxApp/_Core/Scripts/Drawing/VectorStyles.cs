using Godot;

namespace BarBox.Core.Drawing;

/// <summary>
/// Stroke presets. Start from one of these rather than `default(StrokeStyle)`, which has Width 0
/// and is rejected by the tessellator.
///
/// Presets deliberately hand out shared array references (GuidePattern) rather than copying per
/// read: Shape.SetStroke copies at Commit, so no retained Shape can ever hold one. The rule that
/// makes this safe is the one StrokeStyle already states — a style array is immutable once
/// authored. Mutating GuidePattern in place would bypass every dirty flag.
///
/// Join and Cap are omitted where Round is wanted, since Round is the zero value.
/// </summary>
public static class VectorStyles
{
	internal static readonly float[] GuidePattern = [8f, 6f];

	/// <summary>Thinnest readable line; butt-capped so it tiles into rules and ticks.</summary>
	public static readonly StrokeStyle HairLine = new()
	{
		Width = 1f,
		Color = Palette.EdgeGray,
		Cap = CapMode.Butt,
	};

	/// <summary>The OP-1 accent outline: track edges, panel borders.</summary>
	public static readonly StrokeStyle EdgeLine = new()
	{
		Width = 2.5f,
		Color = Palette.Blue,
	};

	/// <summary>Fat round-capped arc for HUD gauges. Pair with `with { ColorStops = ... }`.</summary>
	public static readonly StrokeStyle GaugeArc = new()
	{
		Width = 12f,
		Color = Palette.HudNeedle,
	};

	/// <summary>Miter joins keep faux-3D box corners sharp instead of rounding them off.</summary>
	public static StrokeStyle Wireframe(Color color)
	{
		return new StrokeStyle
		{
			Width = 1.5f,
			Color = color,
			Join = JoinMode.Miter,
			Cap = CapMode.Butt,
		};
	}

	public static StrokeStyle DashedGuide(Color color)
	{
		return new StrokeStyle
		{
			Width = 2f,
			Color = color,
			DashPattern = GuidePattern,
			Cap = CapMode.Butt,
		};
	}

	public static StrokeStyle ButtonOutline(Color color)
	{
		return new StrokeStyle
		{
			Width = 2f,
			Color = color,
		};
	}
}
