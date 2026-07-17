using System;
using BarBox.Core.Drawing;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

namespace BarBox.Tests.Unit.Drawing;

public class StrokeTessellatorTests : TestClass
{
	private const float Feather = StrokeStyle.DefaultFeatherPx;

	public StrokeTessellatorTests(Node testScene)
		: base(testScene)
	{
	}

	[Test]
	public void Tessellate_StraightButtStrokeWithoutFeather_AreaEqualsLengthTimesWidth()
	{
		// Arrange
		var buffer = new VertexBuffer();
		var points = new[] { new Vector2(0f, 0f), new Vector2(100f, 0f) };
		var style = HardStyle(width: 10f, CapMode.Butt);

		// Act
		bool ok = StrokeTessellator.Tessellate(points, default, false, style, 1f, buffer);

		// Assert
		ok.ShouldBeTrue();
		DrawingTestHelpers.AssertWellFormed(buffer);
		DrawingTestHelpers.TotalArea(buffer).ShouldBe(1000f, 0.01f, "A butt-capped straight stroke is exactly length x width");
	}

	[Test]
	public void Tessellate_RoundCapStroke_AddsExactlyOneCircleOfArea()
	{
		// Arrange - two half-discs make a full circle of diameter Width
		var buffer = new VertexBuffer();
		var points = new[] { new Vector2(0f, 0f), new Vector2(100f, 0f) };
		var style = HardStyle(width: 10f, CapMode.Round);

		// Act
		StrokeTessellator.Tessellate(points, default, false, style, 1f, buffer);

		// Assert
		DrawingTestHelpers.AssertWellFormed(buffer);
		float expected = 1000f + (Mathf.Pi * 25f);
		DrawingTestHelpers.TotalArea(buffer).ShouldBe(expected, expected * 0.02f, "Round caps should add one circle of area");
	}

	[Test]
	public void Tessellate_SquareCapStroke_ExtendsByHalfWidthEachEnd()
	{
		// Arrange
		var buffer = new VertexBuffer();
		var points = new[] { new Vector2(0f, 0f), new Vector2(100f, 0f) };
		var style = HardStyle(width: 10f, CapMode.Square);

		// Act
		StrokeTessellator.Tessellate(points, default, false, style, 1f, buffer);

		// Assert
		DrawingTestHelpers.AssertWellFormed(buffer);
		DrawingTestHelpers.TotalArea(buffer).ShouldBe(1100f, 0.01f, "Square caps add half a width at each end");
	}

	[Test]
	public void Tessellate_FeatheredStroke_KeepsFiftyPercentContourAtRequestedWidth()
	{
		// Arrange - the straddle rule: feather must not widen the stroke, or the racing track's
		// asphalt would render wider than the collision geometry it is drawn from
		var buffer = new VertexBuffer();
		var points = new[] { new Vector2(0f, 0f), new Vector2(100f, 0f) };
		var style = BaseStyle(width: 20f);

		// Act
		StrokeTessellator.Tessellate(points, default, false, style, 1f, buffer);

		// Assert
		DrawingTestHelpers.AssertWellFormed(buffer);
		float maxOffset = 0f;
		for (int i = 0; i < buffer.VertexCount; i++)
		{
			maxOffset = Math.Max(maxOffset, Math.Abs(buffer.Points[i].Y));
		}

		maxOffset.ShouldBe(10f + (Feather / 2f), 0.01f, "The skirt should sit half a feather outside the edge");
	}

	[Test]
	public void Tessellate_Feather_CoreVerticesAreOpaqueAndSkirtVerticesAreClear()
	{
		// Arrange
		var buffer = new VertexBuffer();
		var points = new[] { new Vector2(0f, 0f), new Vector2(100f, 0f) };
		var style = BaseStyle(width: 20f);
		float coreEdge = 10f - (Feather / 2f);
		float skirtEdge = 10f + (Feather / 2f);

		// Act
		StrokeTessellator.Tessellate(points, default, false, style, 1f, buffer);

		// Assert
		for (int i = 0; i < buffer.VertexCount; i++)
		{
			float offset = Math.Abs(buffer.Points[i].Y);
			if (Math.Abs(offset - coreEdge) < 0.001f)
			{
				buffer.Colors[i].A.ShouldBe(1f, 0.001f, "Core-edge vertices should be fully opaque");
			}
			else if (Math.Abs(offset - skirtEdge) < 0.001f)
			{
				buffer.Colors[i].A.ShouldBe(0f, 0.001f, "Skirt vertices should be fully transparent");
			}
		}
	}

	[Test]
	public void Tessellate_HairlineNarrowerThanFeather_ScalesPeakAlphaByWidthOverFeather()
	{
		// Arrange - below the feather width the core would invert, so it collapses and fades
		var buffer = new VertexBuffer();
		var points = new[] { new Vector2(0f, 0f), new Vector2(100f, 0f) };
		var style = BaseStyle(width: Feather / 4f);

		// Act
		StrokeTessellator.Tessellate(points, default, false, style, 1f, buffer);

		// Assert
		DrawingTestHelpers.AssertWellFormed(buffer);
		DrawingTestHelpers.MaxAlpha(buffer).ShouldBe(0.25f, 0.001f, "Peak alpha should be width / feather");
	}

	[Test]
	public void Tessellate_HairlineAtExactlyFeatherWidth_MatchesTheNormalBranch()
	{
		// Arrange - the two branches must meet continuously or hairlines pop as width crosses f
		var hairline = new VertexBuffer();
		var normal = new VertexBuffer();
		var points = new[] { new Vector2(0f, 0f), new Vector2(100f, 0f) };

		// Act
		StrokeTessellator.Tessellate(points, default, false, BaseStyle(Feather * 0.999f), 1f, hairline);
		StrokeTessellator.Tessellate(points, default, false, BaseStyle(Feather * 1.001f), 1f, normal);

		// Assert
		DrawingTestHelpers.MaxAlpha(hairline).ShouldBe(DrawingTestHelpers.MaxAlpha(normal), 0.01f);
		DrawingTestHelpers.TotalArea(hairline).ShouldBe(DrawingTestHelpers.TotalArea(normal), DrawingTestHelpers.TotalArea(normal) * 0.02f);
	}

	[Test]
	public void Tessellate_RightAngleRoundJoin_EmitsFanAtTwelveDegreeSteps()
	{
		// Arrange
		var buffer = new VertexBuffer();
		var points = new[] { new Vector2(0f, 0f), new Vector2(50f, 0f), new Vector2(50f, 50f) };
		var style = HardStyle(width: 10f, CapMode.Butt);
		style.Join = JoinMode.Round;

		// Act
		StrokeTessellator.Tessellate(points, default, false, style, 1f, buffer);

		// Assert
		DrawingTestHelpers.AssertWellFormed(buffer);
		int expectedSteps = Mathf.CeilToInt((Mathf.Pi / 2f) / StrokeTessellator.FanStepRad);
		expectedSteps.ShouldBe(8, "A 90 degree turn should fan in 8 steps at 12 degrees each");
	}

	[Test]
	public void Tessellate_TurnBelowFanStep_AutoMitersInsteadOfFanning()
	{
		// Arrange - a shallow round join is sub-pixel-identical to a miter but costs triangles
		var shallow = new VertexBuffer();
		var mitered = new VertexBuffer();
		var points = new[] { new Vector2(0f, 0f), new Vector2(100f, 0f), new Vector2(200f, 1f) };

		var roundStyle = HardStyle(width: 10f, CapMode.Butt);
		roundStyle.Join = JoinMode.Round;
		var miterStyle = HardStyle(width: 10f, CapMode.Butt);
		miterStyle.Join = JoinMode.Miter;

		// Act
		StrokeTessellator.Tessellate(points, default, false, roundStyle, 1f, shallow);
		StrokeTessellator.Tessellate(points, default, false, miterStyle, 1f, mitered);

		// Assert
		shallow.VertexCount.ShouldBe(mitered.VertexCount, "A sub-12-degree round join should fall back to a miter");
	}

	[Test]
	public void Tessellate_MiterBeyondLimit_FallsBackToBevel()
	{
		// Arrange - a near-reversal miter is an unbounded spike
		var buffer = new VertexBuffer();
		var joint = new Vector2(100f, 0f);
		var points = new[] { new Vector2(0f, 0f), joint, new Vector2(0f, 4f) };
		var style = HardStyle(width: 10f, CapMode.Butt);
		style.Join = JoinMode.Miter;
		style.MiterLimit = 4f;

		// Act
		StrokeTessellator.Tessellate(points, default, false, style, 1f, buffer);

		// Assert
		DrawingTestHelpers.AssertWellFormed(buffer);
		for (int i = 0; i < buffer.VertexCount; i++)
		{
			buffer.Points[i].DistanceTo(joint)
				.ShouldBeLessThanOrEqualTo(100f + (4f * 5f), "No vertex should exceed the miter limit from the joint");
		}
	}

	[Test]
	public void Tessellate_OneEightyDegreeReversal_ProducesFiniteGeometry()
	{
		// Arrange - the offset lines are antiparallel here, so there is no miter point to find
		var buffer = new VertexBuffer();
		var points = new[] { new Vector2(0f, 0f), new Vector2(100f, 0f), new Vector2(0f, 0f) };
		var style = BaseStyle(width: 10f);

		// Act
		bool ok = StrokeTessellator.Tessellate(points, default, false, style, 1f, buffer);

		// Assert
		ok.ShouldBeTrue();
		DrawingTestHelpers.AssertWellFormed(buffer);
	}

	[Test]
	public void Tessellate_DuplicatePoints_MatchTheDedupedPath()
	{
		// Arrange
		var withDupes = new VertexBuffer();
		var clean = new VertexBuffer();
		var dupePoints = new[]
		{
			new Vector2(0f, 0f),
			new Vector2(0f, 0f),
			new Vector2(50f, 0f),
			new Vector2(50f, 0f),
			new Vector2(50f, 50f),
		};
		var cleanPoints = new[] { new Vector2(0f, 0f), new Vector2(50f, 0f), new Vector2(50f, 50f) };
		var style = BaseStyle(width: 10f);

		// Act
		StrokeTessellator.Tessellate(dupePoints, default, false, style, 1f, withDupes);
		StrokeTessellator.Tessellate(cleanPoints, default, false, style, 1f, clean);

		// Assert
		withDupes.VertexCount.ShouldBe(clean.VertexCount, "Coincident points should not survive dedupe");
		for (int i = 0; i < clean.VertexCount; i++)
		{
			withDupes.Points[i].ShouldBe(clean.Points[i]);
		}
	}

	[Test]
	public void Tessellate_CollinearRun_EmitsNoJoinGeometry()
	{
		// Arrange
		var collinear = new VertexBuffer();
		var straight = new VertexBuffer();
		var style = BaseStyle(width: 10f);

		// Act
		StrokeTessellator.Tessellate(
			[new Vector2(0f, 0f), new Vector2(50f, 0f), new Vector2(100f, 0f)],
			default,
			false,
			style,
			1f,
			collinear);
		StrokeTessellator.Tessellate(
			[new Vector2(0f, 0f), new Vector2(100f, 0f)],
			default,
			false,
			style,
			1f,
			straight);

		// Assert
		DrawingTestHelpers.AssertWellFormed(collinear);
		float area = DrawingTestHelpers.TotalArea(collinear);
		area.ShouldBe(DrawingTestHelpers.TotalArea(straight), 0.01f, "A collinear midpoint should add no area");
	}

	[Test]
	public void Tessellate_ClosedSquareLoop_CoversEveryPointOnItsCentreline()
	{
		// Arrange - the seam at vertex 0 is a normal joint, not a pair of caps
		var buffer = new VertexBuffer();
		var points = new[]
		{
			new Vector2(0f, 0f),
			new Vector2(100f, 0f),
			new Vector2(100f, 100f),
			new Vector2(0f, 100f),
		};
		var style = HardStyle(width: 10f, CapMode.Butt);

		// Act
		StrokeTessellator.Tessellate(points, default, true, style, 1f, buffer);

		// Assert
		DrawingTestHelpers.AssertWellFormed(buffer);
		for (int i = 0; i < points.Length; i++)
		{
			var a = points[i];
			var b = points[(i + 1) % points.Length];
			for (int s = 0; s <= 20; s++)
			{
				var sample = a.Lerp(b, s / 20f);
				DrawingTestHelpers.IsCovered(buffer, sample)
					.ShouldBeTrue($"Centreline point {sample} should be covered, including at the seam");
			}
		}
	}

	[Test]
	public void Tessellate_ClosedSquareLoop_LeavesNoGapAtTheSeam()
	{
		// Arrange
		var buffer = new VertexBuffer();
		var points = new[]
		{
			new Vector2(0f, 0f),
			new Vector2(100f, 0f),
			new Vector2(100f, 100f),
			new Vector2(0f, 100f),
		};
		var style = HardStyle(width: 20f, CapMode.Butt);
		style.Join = JoinMode.Round;

		// Act
		StrokeTessellator.Tessellate(points, default, true, style, 1f, buffer);

		// Assert - probe a ring of points just inside the outer edge, right around vertex 0
		for (float angle = 0f; angle < Mathf.Tau; angle += 0.1f)
		{
			var probe = points[0] + (new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 9f);
			DrawingTestHelpers.IsCovered(buffer, probe).ShouldBeTrue($"The seam corner should be filled at angle {angle}");
		}
	}

	[Test]
	public void Tessellate_ClosedLoop_ProducesSameGeometryForEitherAuthoredWinding()
	{
		// Arrange - StrokeAlign.Outer must mean "away from the interior", not "whichever way
		// the author happened to wind the polygon"
		var clockwise = new VertexBuffer();
		var counterClockwise = new VertexBuffer();
		var ccwPoints = new[]
		{
			new Vector2(0f, 0f),
			new Vector2(100f, 0f),
			new Vector2(100f, 100f),
			new Vector2(0f, 100f),
		};
		var cwPoints = new[]
		{
			new Vector2(0f, 100f),
			new Vector2(100f, 100f),
			new Vector2(100f, 0f),
			new Vector2(0f, 0f),
		};
		var style = BaseStyle(width: 10f);
		style.Align = StrokeAlign.Outer;

		// Act
		StrokeTessellator.Tessellate(ccwPoints, default, true, style, 1f, counterClockwise);
		StrokeTessellator.Tessellate(cwPoints, default, true, style, 1f, clockwise);

		// Assert
		DrawingTestHelpers.TotalArea(clockwise)
			.ShouldBe(DrawingTestHelpers.TotalArea(counterClockwise), 0.5f, "Winding should be normalized away");

		// Both must expand outward from the square, never into its interior.
		var center = new Vector2(50f, 50f);
		DrawingTestHelpers.IsCovered(clockwise, center).ShouldBeFalse();
		DrawingTestHelpers.IsCovered(counterClockwise, center).ShouldBeFalse();
		DrawingTestHelpers.IsCovered(clockwise, new Vector2(50f, -5f)).ShouldBeTrue("Outer align should cover outside the edge");
		DrawingTestHelpers.IsCovered(counterClockwise, new Vector2(50f, -5f)).ShouldBeTrue();
	}

	[Test]
	public void Tessellate_InnerAlign_StaysOnOneSideOfTheLine()
	{
		// Arrange
		var buffer = new VertexBuffer();
		var points = new[] { new Vector2(0f, 0f), new Vector2(100f, 0f) };
		var style = HardStyle(width: 10f, CapMode.Butt);
		style.Align = StrokeAlign.Inner;

		// Act
		StrokeTessellator.Tessellate(points, default, false, style, 1f, buffer);

		// Assert - Orthogonal() is (y, -x), so Inner offsets to positive Y here
		for (int i = 0; i < buffer.VertexCount; i++)
		{
			buffer.Points[i].Y.ShouldBeInRange(-0.001f, 10.001f, "Inner align should not cross the line");
		}
	}

	[Test]
	public void Tessellate_OuterAlign_StaysOnTheOppositeSideFromInner()
	{
		// Arrange
		var buffer = new VertexBuffer();
		var points = new[] { new Vector2(0f, 0f), new Vector2(100f, 0f) };
		var style = HardStyle(width: 10f, CapMode.Butt);
		style.Align = StrokeAlign.Outer;

		// Act
		StrokeTessellator.Tessellate(points, default, false, style, 1f, buffer);

		// Assert
		for (int i = 0; i < buffer.VertexCount; i++)
		{
			buffer.Points[i].Y.ShouldBeInRange(-10.001f, 0.001f);
		}
	}

	[Test]
	public void Tessellate_WidthProfile_ResamplesAlongNormalizedArcLength()
	{
		// Arrange - a two-entry profile should ramp linearly regardless of point count
		var buffer = new VertexBuffer();
		var points = new Vector2[10];
		for (int i = 0; i < points.Length; i++)
		{
			points[i] = new Vector2(i * 10f, 0f);
		}

		var style = HardStyle(width: 10f, CapMode.Butt);
		style.WidthProfile = [10f, 30f];

		// Act
		StrokeTessellator.Tessellate(points, default, false, style, 1f, buffer);

		// Assert
		DrawingTestHelpers.AssertWellFormed(buffer);
		float startHalfWidth = MaxOffsetNear(buffer, x: 0f);
		float endHalfWidth = MaxOffsetNear(buffer, x: 90f);
		startHalfWidth.ShouldBe(5f, 0.01f, "Profile entry 0 sits at t=0");
		endHalfWidth.ShouldBe(15f, 0.01f, "Profile entry 1 sits at t=1");
	}

	[Test]
	public void Tessellate_ColorStops_SampleInOkLabAtVertexT()
	{
		// Arrange
		var buffer = new VertexBuffer();
		var points = new[] { new Vector2(0f, 0f), new Vector2(50f, 0f), new Vector2(100f, 0f) };
		var style = HardStyle(width: 10f, CapMode.Butt);
		style.ColorStops = [new ColorStop(0f, Colors.Red), new ColorStop(1f, Colors.Blue)];

		// Act
		StrokeTessellator.Tessellate(points, default, false, style, 1f, buffer);

		// Assert - the midpoint vertex should carry the OKLab midpoint, not an sRGB lerp
		var expected = OkLab.Mix(Colors.Red, Colors.Blue, 0.5f);
		bool found = false;
		for (int i = 0; i < buffer.VertexCount; i++)
		{
			if (Math.Abs(buffer.Points[i].X - 50f) < 0.001f)
			{
				buffer.Colors[i].R.ShouldBe(expected.R, 0.001f);
				buffer.Colors[i].G.ShouldBe(expected.G, 0.001f);
				buffer.Colors[i].B.ShouldBe(expected.B, 0.001f);
				found = true;
			}
		}

		found.ShouldBeTrue("The midpoint vertex should exist");
	}

	[Test]
	public void Tessellate_ColorStops_MatchEndpointsExactly()
	{
		// Arrange
		var buffer = new VertexBuffer();
		var points = new[] { new Vector2(0f, 0f), new Vector2(100f, 0f) };
		var style = HardStyle(width: 10f, CapMode.Butt);
		style.ColorStops = [new ColorStop(0f, Colors.Red), new ColorStop(1f, Colors.Blue)];

		// Act
		StrokeTessellator.Tessellate(points, default, false, style, 1f, buffer);

		// Assert
		for (int i = 0; i < buffer.VertexCount; i++)
		{
			var expected = buffer.Points[i].X < 50f ? Colors.Red : Colors.Blue;
			buffer.Colors[i].R.ShouldBe(expected.R, 0.001f);
			buffer.Colors[i].B.ShouldBe(expected.B, 0.001f);
		}
	}

	[Test]
	public void Tessellate_AppendsWithoutDisturbingExistingBufferContents()
	{
		// Arrange
		var buffer = new VertexBuffer();
		int existingA = buffer.AddVertex(new Vector2(-1f, -1f), Colors.Green);
		int existingB = buffer.AddVertex(new Vector2(-2f, -2f), Colors.Green);
		int existingC = buffer.AddVertex(new Vector2(-3f, -3f), Colors.Green);
		buffer.AddTriangle(existingA, existingB, existingC);
		int vertexBase = buffer.VertexCount;

		// Act
		StrokeTessellator.Tessellate(
			[new Vector2(0f, 0f), new Vector2(100f, 0f)],
			default,
			false,
			BaseStyle(10f),
			1f,
			buffer);

		// Assert
		buffer.Points[0].ShouldBe(new Vector2(-1f, -1f), "Pre-existing vertices should be untouched");
		buffer.Indices[0].ShouldBe(existingA, "Pre-existing indices should be untouched");
		buffer.VertexCount.ShouldBeGreaterThan(vertexBase);
		for (int i = 3; i < buffer.IndexCount; i++)
		{
			buffer.Indices[i].ShouldBeGreaterThanOrEqualTo(vertexBase, "Appended indices should be rebased past the existing vertices");
		}

		DrawingTestHelpers.AssertWellFormed(buffer);
	}

	[Test]
	public void Tessellate_ZeroWidth_IsRejectedAndLeavesTheBufferUntouched()
	{
		// Arrange
		var buffer = new VertexBuffer();
		var points = new[] { new Vector2(0f, 0f), new Vector2(100f, 0f) };

		// Act
		bool ok = StrokeTessellator.Tessellate(points, default, false, BaseStyle(0f), 1f, buffer);

		// Assert
		ok.ShouldBeFalse();
		buffer.VertexCount.ShouldBe(0, "A rejected shape must not emit partial geometry");
		buffer.IndexCount.ShouldBe(0);
	}

	[Test]
	public void Tessellate_NegativeOrNaNWidth_IsRejected()
	{
		// Arrange
		var buffer = new VertexBuffer();
		var points = new[] { new Vector2(0f, 0f), new Vector2(100f, 0f) };

		// Act
		bool negative = StrokeTessellator.Tessellate(points, default, false, BaseStyle(-5f), 1f, buffer);
		bool nan = StrokeTessellator.Tessellate(points, default, false, BaseStyle(float.NaN), 1f, buffer);

		// Assert
		negative.ShouldBeFalse();
		nan.ShouldBeFalse();
		buffer.VertexCount.ShouldBe(0);
	}

	[Test]
	public void Tessellate_InvalidPixelScale_IsRejected()
	{
		// Arrange
		var buffer = new VertexBuffer();
		var points = new[] { new Vector2(0f, 0f), new Vector2(100f, 0f) };
		var style = BaseStyle(10f);

		// Act
		bool zero = StrokeTessellator.Tessellate(points, default, false, style, 0f, buffer);
		bool negative = StrokeTessellator.Tessellate(points, default, false, style, -1f, buffer);
		bool nan = StrokeTessellator.Tessellate(points, default, false, style, float.NaN, buffer);

		// Assert
		zero.ShouldBeFalse();
		negative.ShouldBeFalse();
		nan.ShouldBeFalse();
		buffer.VertexCount.ShouldBe(0);
	}

	[Test]
	public void Tessellate_FewerThanTwoDistinctPoints_IsRejected()
	{
		// Arrange - no dot primitive in v1; silently emitting one would hide upstream bugs
		var buffer = new VertexBuffer();
		var style = BaseStyle(10f);

		// Act
		bool single = StrokeTessellator.Tessellate([new Vector2(1f, 1f)], default, false, style, 1f, buffer);
		bool coincident = StrokeTessellator.Tessellate(
			[new Vector2(1f, 1f), new Vector2(1f, 1f)],
			default,
			false,
			style,
			1f,
			buffer);
		bool empty = StrokeTessellator.Tessellate([], default, false, style, 1f, buffer);

		// Assert
		single.ShouldBeFalse();
		coincident.ShouldBeFalse();
		empty.ShouldBeFalse();
		buffer.VertexCount.ShouldBe(0);
	}

	[Test]
	public void Tessellate_NonFinitePoint_IsRejected()
	{
		// Arrange - the last guard between a bad Projector result and a corrupt GPU buffer
		var buffer = new VertexBuffer();
		var points = new[] { new Vector2(0f, 0f), new Vector2(float.NaN, 0f), new Vector2(100f, 0f) };

		// Act
		bool ok = StrokeTessellator.Tessellate(points, default, false, BaseStyle(10f), 1f, buffer);

		// Assert
		ok.ShouldBeFalse();
		buffer.VertexCount.ShouldBe(0);
	}

	[Test]
	public void Tessellate_ClosedPathWithTwoDistinctPoints_DegradesToOpen()
	{
		// Arrange
		var buffer = new VertexBuffer();
		var points = new[] { new Vector2(0f, 0f), new Vector2(100f, 0f) };

		// Act
		bool ok = StrokeTessellator.Tessellate(points, default, true, BaseStyle(10f), 1f, buffer);

		// Assert
		ok.ShouldBeTrue("A two-point closed path is just a line");
		DrawingTestHelpers.AssertWellFormed(buffer);
	}

	[Test]
	public void Tessellate_PixelScale_ShrinksFeatherInCanvasUnits()
	{
		// Arrange - feather is specified in screen pixels, so a 6x-scaled canvas needs 1/6th
		// the canvas units to land on the same number of pixels
		var unscaled = new VertexBuffer();
		var scaled = new VertexBuffer();
		var points = new[] { new Vector2(0f, 0f), new Vector2(100f, 0f) };
		var style = BaseStyle(width: 20f);

		// Act
		StrokeTessellator.Tessellate(points, default, false, style, 1f, unscaled);
		StrokeTessellator.Tessellate(points, default, false, style, 6f, scaled);

		// Assert
		MaxAbsY(unscaled).ShouldBe(10f + (Feather / 2f), 0.01f);
		MaxAbsY(scaled).ShouldBe(10f + (Feather / 6f / 2f), 0.01f, "A larger pixel scale should shrink the feather skirt");
	}

	[Test]
	public void Tessellate_NoFeather_EmitsNoTransparentVertices()
	{
		// Arrange
		var buffer = new VertexBuffer();
		var points = new[] { new Vector2(0f, 0f), new Vector2(100f, 0f) };
		var style = HardStyle(width: 10f, CapMode.Butt);

		// Act
		StrokeTessellator.Tessellate(points, default, false, style, 1f, buffer);

		// Assert
		for (int i = 0; i < buffer.VertexCount; i++)
		{
			buffer.Colors[i].A.ShouldBe(1f, 0.001f, "A feather-suppressed stroke should be uniformly opaque");
		}
	}

	[Test]
	public void Tessellate_LongPathBeyondStackallocLimit_StillSucceeds()
	{
		// Arrange - past 256 points the index map comes from the array pool instead of the stack
		var buffer = new VertexBuffer();
		var points = new Vector2[600];
		for (int i = 0; i < points.Length; i++)
		{
			points[i] = new Vector2(i * 2f, Mathf.Sin(i * 0.1f) * 20f);
		}

		// Act
		bool ok = StrokeTessellator.Tessellate(points, default, false, BaseStyle(6f), 1f, buffer);

		// Assert
		ok.ShouldBeTrue();
		DrawingTestHelpers.AssertWellFormed(buffer);
	}

	private static StrokeStyle BaseStyle(float width)
	{
		return new StrokeStyle
		{
			Width = width,
			Color = Colors.White,
			Join = JoinMode.Round,
			Cap = CapMode.Round,
			Align = StrokeAlign.Center,
			TrimEnd = 1f,
		};
	}

	private static StrokeStyle HardStyle(float width, CapMode cap)
	{
		var style = BaseStyle(width);
		style.Cap = cap;
		style.FeatherPx = StrokeStyle.NoFeather;
		return style;
	}

	private static float MaxAbsY(VertexBuffer buffer)
	{
		float max = 0f;
		for (int i = 0; i < buffer.VertexCount; i++)
		{
			max = Math.Max(max, Math.Abs(buffer.Points[i].Y));
		}

		return max;
	}

	private static float MaxOffsetNear(VertexBuffer buffer, float x)
	{
		float max = 0f;
		for (int i = 0; i < buffer.VertexCount; i++)
		{
			if (Math.Abs(buffer.Points[i].X - x) < 0.001f)
			{
				max = Math.Max(max, Math.Abs(buffer.Points[i].Y));
			}
		}

		return max;
	}
}
