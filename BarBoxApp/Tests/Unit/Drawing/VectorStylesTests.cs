using BarBox.Core.Drawing;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

namespace BarBox.Tests.Unit.Drawing;

/// <summary>
/// Presets are the sanctioned starting point for a StrokeStyle, so a typo in one surfaces far
/// away as an invisible shape. These pin the trap StrokeStyle documents: Width 0 is rejected.
/// </summary>
public class VectorStylesTests : TestClass
{
	private static readonly Vector2[] Line = [new Vector2(0f, 0f), new Vector2(40f, 0f)];

	public VectorStylesTests(Node testScene)
		: base(testScene)
	{
	}

	[Test]
	public void AllPresets_HaveUsableWidth()
	{
		// Arrange
		StrokeStyle[] presets =
		[
			VectorStyles.HairLine,
			VectorStyles.EdgeLine,
			VectorStyles.GaugeArc,
			VectorStyles.Wireframe(Palette.Grid),
			VectorStyles.DashedGuide(Palette.Blue),
			VectorStyles.ButtonOutline(Palette.Orange),
		];

		// Act & Assert
		foreach (StrokeStyle style in presets)
		{
			style.Width.ShouldBeGreaterThan(0f, "A preset with Width 0 is rejected by the tessellator");
			float.IsFinite(style.Width).ShouldBeTrue();
		}
	}

	[Test]
	public void AllPresets_TessellateAStraightLine()
	{
		// Arrange
		StrokeStyle[] presets =
		[
			VectorStyles.HairLine,
			VectorStyles.EdgeLine,
			VectorStyles.GaugeArc,
			VectorStyles.Wireframe(Palette.Grid),
			VectorStyles.DashedGuide(Palette.Blue),
			VectorStyles.ButtonOutline(Palette.Orange),
		];

		// Act & Assert
		foreach (StrokeStyle style in presets)
		{
			var buffer = new VertexBuffer();
			bool ok = StrokeTessellator.Tessellate(Line, default, false, style, 1f, buffer);

			ok.ShouldBeTrue("Every preset should tessellate a simple two-point line");
			buffer.IsEmpty.ShouldBeFalse();
			DrawingTestHelpers.AssertWellFormed(buffer);
		}
	}

	[Test]
	public void GaugeArc_AcceptsGradientStopsViaWith()
	{
		// Arrange
		StrokeStyle style = VectorStyles.GaugeArc with { ColorStops = Palette.SpeedGradient };
		var buffer = new VertexBuffer();

		// Act
		bool ok = StrokeTessellator.Tessellate(Line, default, false, style, 1f, buffer);

		// Assert
		ok.ShouldBeTrue();
		DrawingTestHelpers.AssertWellFormed(buffer);
		VectorStyles.GaugeArc.ColorStops.ShouldBeNull("`with` must copy the struct, never mutate the preset");
	}

	[Test]
	public void DashedGuide_SharesItsPatternArray()
	{
		// Arrange & Act
		StrokeStyle first = VectorStyles.DashedGuide(Palette.Blue);
		StrokeStyle second = VectorStyles.DashedGuide(Palette.Orange);

		// Assert
		first.DashPattern.ShouldBeSameAs(
			second.DashPattern,
			"Presets share arrays by design; Shape.SetStroke is the copy choke point");
		first.DashPattern.Length.ShouldBe(2);
	}
}
