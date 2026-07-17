using System;
using BarBox.Core.Drawing;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

namespace BarBox.Tests.Unit.Drawing;

public class DashSplitterTests : TestClass
{
	private static readonly Vector2[] Line100 = [new(0f, 0f), new(100f, 0f)];

	private static readonly Vector2[] Square = [new(0f, 0f), new(100f, 0f), new(100f, 100f), new(0f, 100f)];

	public DashSplitterTests(Node testScene)
		: base(testScene)
	{
	}

	[Test]
	public void Split_OnOffPattern_DashLengthsAndGapsSumToPathLength()
	{
		// Arrange
		var result = new DashResult();
		var pattern = new[] { 10f, 10f };

		// Act
		int count = DashSplitter.Split(
			Line100, default, false, pattern, 0f, DashMode.OnOff,
			Colors.White, Colors.Black, CapMode.Butt, false, result);

		// Assert
		count.ShouldBe(5, "A 20-unit period over 100 units yields 5 dashes");
		SumSegmentLengths(result).ShouldBe(50f, 0.01f, "Dashes should cover exactly half the path");
	}

	[Test]
	public void Split_OnOffPattern_SegmentsStayWithinThePath()
	{
		// Arrange
		var result = new DashResult();

		// Act
		DashSplitter.Split(
			Line100, default, false, [10f, 10f], 0f, DashMode.OnOff,
			Colors.White, Colors.Black, CapMode.Butt, false, result);

		// Assert
		for (int i = 0; i < result.PointCount; i++)
		{
			result.Points[i].X.ShouldBeInRange(-0.01f, 100.01f);
			result.Points[i].Y.ShouldBe(0f, 0.01f);
		}
	}

	[Test]
	public void Split_OffsetByFullPeriod_MatchesZeroOffset()
	{
		// Arrange
		var zero = new DashResult();
		var wrapped = new DashResult();
		var pattern = new[] { 10f, 10f };

		// Act
		DashSplitter.Split(Line100, default, false, pattern, 0f, DashMode.OnOff, Colors.White, Colors.Black, CapMode.Butt, false, zero);
		DashSplitter.Split(Line100, default, false, pattern, 20f, DashMode.OnOff, Colors.White, Colors.Black, CapMode.Butt, false, wrapped);

		// Assert
		wrapped.SegmentCount.ShouldBe(zero.SegmentCount);
		SumSegmentLengths(wrapped).ShouldBe(SumSegmentLengths(zero), 0.01f, "A full-period offset should be a no-op");
	}

	[Test]
	public void Split_NegativeOffset_NormalizesToThePositiveEquivalent()
	{
		// Arrange
		var negative = new DashResult();
		var positive = new DashResult();
		var pattern = new[] { 10f, 10f };

		// Act
		DashSplitter.Split(Line100, default, false, pattern, -5f, DashMode.OnOff, Colors.White, Colors.Black, CapMode.Butt, false, negative);
		DashSplitter.Split(Line100, default, false, pattern, 15f, DashMode.OnOff, Colors.White, Colors.Black, CapMode.Butt, false, positive);

		// Assert
		negative.SegmentCount.ShouldBe(positive.SegmentCount);
		SumSegmentLengths(negative).ShouldBe(SumSegmentLengths(positive), 0.01f);
	}

	[Test]
	public void Split_OffsetShiftsDashesAlongThePath()
	{
		// Arrange
		var result = new DashResult();

		// Act
		DashSplitter.Split(
			Line100, default, false, [10f, 10f], 5f, DashMode.OnOff,
			Colors.White, Colors.Black, CapMode.Butt, false, result);

		// Assert - offsetting by 5 leaves a half dash at the start
		result.Points[0].X.ShouldBe(0f, 0.01f);
		result.Points[1].X.ShouldBe(5f, 0.01f, "A positive offset should slide the pattern backwards");
	}

	[Test]
	public void Split_DashLongerThanPath_EmitsOneFullSegment()
	{
		// Arrange
		var result = new DashResult();

		// Act
		int count = DashSplitter.Split(
			Line100, default, false, [500f, 500f], 0f, DashMode.OnOff,
			Colors.White, Colors.Black, CapMode.Butt, false, result);

		// Assert
		count.ShouldBe(1);
		SumSegmentLengths(result).ShouldBe(100f, 0.01f, "A dash longer than the path should cover all of it");
	}

	[Test]
	public void Split_NullOrEmptyOrZeroPattern_EmitsOneSolidSegment()
	{
		// Arrange
		var fromEmpty = new DashResult();
		var fromZero = new DashResult();

		// Act
		int empty = DashSplitter.Split(
			Line100, default, false, [], 0f, DashMode.OnOff,
			Colors.White, Colors.Black, CapMode.Butt, false, fromEmpty);
		int zero = DashSplitter.Split(
			Line100, default, false, [0f, 0f], 0f, DashMode.OnOff,
			Colors.White, Colors.Black, CapMode.Butt, false, fromZero);

		// Assert
		empty.ShouldBe(1, "No pattern should fall back to solid rather than emitting nothing");
		zero.ShouldBe(1, "An all-zero pattern would otherwise loop forever");
		SumSegmentLengths(fromEmpty).ShouldBe(100f, 0.01f);
		SumSegmentLengths(fromZero).ShouldBe(100f, 0.01f);
	}

	[Test]
	public void Split_NegativePatternEntry_FallsBackToSolid()
	{
		// Arrange
		var result = new DashResult();

		// Act
		int count = DashSplitter.Split(
			Line100, default, false, [10f, -5f], 0f, DashMode.OnOff,
			Colors.White, Colors.Black, CapMode.Butt, false, result);

		// Assert
		count.ShouldBe(1, "A negative dash length is not renderable; solid is the safe fallback");
	}

	[Test]
	public void Split_OddLengthPattern_TilesDoubled()
	{
		// Arrange - per SVG, [10] means 10 on, 10 off
		var odd = new DashResult();
		var explicitPattern = new DashResult();

		// Act
		DashSplitter.Split(Line100, default, false, [10f], 0f, DashMode.OnOff, Colors.White, Colors.Black, CapMode.Butt, false, odd);
		DashSplitter.Split(Line100, default, false, [10f, 10f], 0f, DashMode.OnOff, Colors.White, Colors.Black, CapMode.Butt, false, explicitPattern);

		// Assert
		odd.SegmentCount.ShouldBe(explicitPattern.SegmentCount);
		SumSegmentLengths(odd).ShouldBe(SumSegmentLengths(explicitPattern), 0.01f);
	}

	[Test]
	public void Split_ZeroLengthPath_EmitsNoSegments()
	{
		// Arrange
		var result = new DashResult();
		var degenerate = new[] { new Vector2(5f, 5f), new Vector2(5f, 5f) };

		// Act
		int count = DashSplitter.Split(
			degenerate, default, false, [10f, 10f], 0f, DashMode.OnOff,
			Colors.White, Colors.Black, CapMode.Butt, false, result);

		// Assert
		count.ShouldBe(0);
	}

	[Test]
	public void Split_FewerThanTwoPoints_EmitsNoSegments()
	{
		// Arrange
		var result = new DashResult();

		// Act
		int count = DashSplitter.Split(
			[new Vector2(1f, 1f)], default, false, [10f, 10f], 0f, DashMode.OnOff,
			Colors.White, Colors.Black, CapMode.Butt, false, result);

		// Assert
		count.ShouldBe(0);
	}

	[Test]
	public void Split_DashCrossingAVertex_KeepsTheCornerPoint()
	{
		// Arrange - a dash spanning the square's first corner must not cut it
		var result = new DashResult();

		// Act
		DashSplitter.Split(
			Square, default, true, [400f, 0.001f], 0f, DashMode.OnOff,
			Colors.White, Colors.Black, CapMode.Butt, false, result);

		// Assert
		bool hasCorner = false;
		for (int i = 0; i < result.PointCount; i++)
		{
			if (result.Points[i].DistanceTo(new Vector2(100f, 0f)) < 0.01f)
			{
				hasCorner = true;
			}
		}

		hasCorner.ShouldBeTrue("Original vertices inside a dash must survive");
	}

	[Test]
	public void Split_ClosedPath_CoversTheFullLoopLength()
	{
		// Arrange
		var result = new DashResult();

		// Act
		DashSplitter.Split(
			Square, default, true, [20f, 20f], 0f, DashMode.OnOff,
			Colors.White, Colors.Black, CapMode.Butt, false, result);

		// Assert - the loop is 400 units, so dashes cover half
		SumSegmentLengths(result).ShouldBe(200f, 0.5f, "A closed walk should include the closing segment");
	}

	[Test]
	public void Split_ClosedPathFitToLength_ScalesToAWholeNumberOfPeriods()
	{
		// Arrange - a 30-unit period does not divide the square's 400-unit loop
		var result = new DashResult();

		// Act
		DashSplitter.Split(
			Square, default, true, [15f, 15f], 0f, DashMode.OnOff,
			Colors.White, Colors.Black, CapMode.Butt, true, result);

		// Assert
		int periods = Mathf.RoundToInt(400f / 30f);
		result.SegmentCount.ShouldBe(periods, "fitToLength should land a whole number of periods on the loop");
		SumSegmentLengths(result).ShouldBe(200f, 0.5f);
	}

	[Test]
	public void Split_Striped_TilesTheFullLengthWithNoGaps()
	{
		// Arrange
		var result = new DashResult();

		// Act
		int count = DashSplitter.Split(
			Line100, default, false, [10f], 0f, DashMode.Striped,
			Colors.Red, Colors.White, CapMode.Butt, false, result);

		// Assert
		count.ShouldBe(10, "Stripes have no gaps, so every span is emitted");
		SumSegmentLengths(result).ShouldBe(100f, 0.01f, "Stripes should tile the whole path");
	}

	[Test]
	public void Split_Striped_AdjacentStripesShareExactBoundaryPoints()
	{
		// Arrange - any gap here would show as a seam line down a kerb
		var result = new DashResult();

		// Act
		DashSplitter.Split(
			Line100, default, false, [10f], 0f, DashMode.Striped,
			Colors.Red, Colors.White, CapMode.Butt, false, result);

		// Assert
		for (int i = 1; i < result.SegmentCount; i++)
		{
			var previous = result.Segments[i - 1];
			var current = result.Segments[i];
			var previousEnd = result.Points[previous.Start + previous.Count - 1];
			var currentStart = result.Points[current.Start];
			currentStart.DistanceTo(previousEnd)
				.ShouldBeLessThan(0.001f, $"Stripe {i} should start exactly where stripe {i - 1} ended");
		}
	}

	[Test]
	public void Split_Striped_AlternatesColors()
	{
		// Arrange
		var result = new DashResult();

		// Act
		DashSplitter.Split(
			Line100, default, false, [10f], 0f, DashMode.Striped,
			Colors.Red, Colors.White, CapMode.Butt, false, result);

		// Assert
		for (int i = 0; i < result.SegmentCount; i++)
		{
			result.Segments[i].UseColor.ShouldBeTrue("Stripes carry their own color");
			result.Segments[i].Color.ShouldBe(i % 2 == 0 ? Colors.Red : Colors.White, $"Stripe {i} color");
		}
	}

	[Test]
	public void Split_Striped_InternalBoundariesAreButtWithoutFeather()
	{
		// Arrange - a feathered butt on each side of a shared boundary would draw a seam line
		var result = new DashResult();

		// Act
		DashSplitter.Split(
			Line100, default, false, [10f], 0f, DashMode.Striped,
			Colors.Red, Colors.White, CapMode.Round, false, result);

		// Assert
		for (int i = 1; i < result.SegmentCount - 1; i++)
		{
			var segment = result.Segments[i];
			segment.StartCap.ShouldBe(CapMode.Butt, $"Stripe {i} start is an internal boundary");
			segment.EndCap.ShouldBe(CapMode.Butt, $"Stripe {i} end is an internal boundary");
			segment.FeatherStart.ShouldBeFalse();
			segment.FeatherEnd.ShouldBeFalse();
		}
	}

	[Test]
	public void Split_Striped_OpenPathEndsUseTheStyleCapAndFeather()
	{
		// Arrange
		var result = new DashResult();

		// Act
		DashSplitter.Split(
			Line100, default, false, [10f], 0f, DashMode.Striped,
			Colors.Red, Colors.White, CapMode.Round, false, result);

		// Assert
		var first = result.Segments[0];
		var last = result.Segments[result.SegmentCount - 1];
		first.StartCap.ShouldBe(CapMode.Round, "The path's first free end should carry the style cap");
		first.FeatherStart.ShouldBeTrue();
		last.EndCap.ShouldBe(CapMode.Round);
		last.FeatherEnd.ShouldBeTrue();
	}

	[Test]
	public void Split_StripedClosedPath_ButtsEveryBoundaryIncludingTheSeam()
	{
		// Arrange - a closed kerb has no free ends at all
		var result = new DashResult();

		// Act
		DashSplitter.Split(
			Square, default, true, [25f], 0f, DashMode.Striped,
			Colors.Red, Colors.White, CapMode.Round, true, result);

		// Assert
		for (int i = 0; i < result.SegmentCount; i++)
		{
			result.Segments[i].StartCap.ShouldBe(CapMode.Butt, $"Stripe {i} start");
			result.Segments[i].EndCap.ShouldBe(CapMode.Butt, $"Stripe {i} end");
			result.Segments[i].FeatherStart.ShouldBeFalse();
			result.Segments[i].FeatherEnd.ShouldBeFalse();
		}
	}

	[Test]
	public void Split_StripedClosedPathFitToLength_KeepsEveryStripeTheSameLength()
	{
		// Arrange - without fitting, the seam stripe would be a stub
		var result = new DashResult();

		// Act
		DashSplitter.Split(
			Square, default, true, [30f], 0f, DashMode.Striped,
			Colors.Red, Colors.White, CapMode.Butt, true, result);

		// Assert
		float expected = SegmentLength(result, 0);
		for (int i = 1; i < result.SegmentCount; i++)
		{
			SegmentLength(result, i).ShouldBe(expected, 0.1f, $"Stripe {i} should match the others");
		}
	}

	[Test]
	public void Split_TIsMonotonicAcrossAllSegments()
	{
		// Arrange
		var result = new DashResult();

		// Act
		DashSplitter.Split(
			Square, default, true, [20f, 20f], 0f, DashMode.OnOff,
			Colors.White, Colors.Black, CapMode.Butt, false, result);

		// Assert
		for (int i = 0; i < result.SegmentCount; i++)
		{
			var segment = result.Segments[i];
			for (int p = segment.Start + 1; p < segment.Start + segment.Count; p++)
			{
				result.T[p].ShouldBeGreaterThanOrEqualTo(result.T[p - 1] - 0.0001f, $"T should not decrease within segment {i}");
			}

			result.T[segment.Start].ShouldBeInRange(-0.0001f, 1.0001f);
		}
	}

	[Test]
	public void Split_InheritsParentTSoGradientsSurviveTheGaps()
	{
		// Arrange - a dash halfway along the path should carry t near 0.5
		var path = new FlatPath();
		PathFlattener.Polyline(Line100, closed: false, path);
		var result = new DashResult();

		// Act
		DashSplitter.Split(
			path.PointSpan, path.TSpan, false, [10f, 10f], 0f, DashMode.OnOff,
			Colors.White, Colors.Black, CapMode.Butt, false, result);

		// Assert
		for (int i = 0; i < result.PointCount; i++)
		{
			result.T[i].ShouldBe(result.Points[i].X / 100f, 0.001f, "Dash points should keep the parent's parameterization");
		}
	}

	[Test]
	public void Split_Clear_ResetsPreviousResults()
	{
		// Arrange - the caller pools one DashResult across rebuilds
		var result = new DashResult();
		DashSplitter.Split(Line100, default, false, [10f, 10f], 0f, DashMode.OnOff, Colors.White, Colors.Black, CapMode.Butt, false, result);
		int firstRun = result.SegmentCount;

		// Act
		DashSplitter.Split(Line100, default, false, [50f, 50f], 0f, DashMode.OnOff, Colors.White, Colors.Black, CapMode.Butt, false, result);

		// Assert
		firstRun.ShouldBe(5);
		result.SegmentCount.ShouldBe(1, "A reused result should not accumulate the previous run's segments");
	}

	[Test]
	public void Split_ResultFeedsTessellateSegmentIntoOneBuffer()
	{
		// Arrange - the end-to-end shape M2's Shape.Rebuild will drive
		var result = new DashResult();
		var buffer = new VertexBuffer();
		var style = new StrokeStyle
		{
			Width = 8f,
			Color = Colors.White,
			Join = JoinMode.Round,
			Cap = CapMode.Round,
			DashMode = DashMode.Striped,
			DashColorB = Colors.Red,
			TrimEnd = 1f,
		};

		// Act
		int count = DashSplitter.Split(
			Line100, default, false, [10f], 0f, DashMode.Striped,
			Colors.White, Colors.Red, style.Cap, false, result);

		for (int i = 0; i < count; i++)
		{
			var segment = result.Segments[i];
			StrokeTessellator.TessellateSegment(
				result.Points.AsSpan(segment.Start, segment.Count),
				result.T.AsSpan(segment.Start, segment.Count),
				false,
				style,
				segment,
				1f,
				buffer);
		}

		// Assert
		count.ShouldBe(10);
		DrawingTestHelpers.AssertWellFormed(buffer);
		DrawingTestHelpers.IsCovered(buffer, new Vector2(50f, 0f)).ShouldBeTrue("Stripes should leave no gap mid-path");
		DrawingTestHelpers.IsCovered(buffer, new Vector2(5f, 0f)).ShouldBeTrue();
	}

	private static float SumSegmentLengths(DashResult result)
	{
		float total = 0f;
		for (int i = 0; i < result.SegmentCount; i++)
		{
			total += SegmentLength(result, i);
		}

		return total;
	}

	private static float SegmentLength(DashResult result, int index)
	{
		var segment = result.Segments[index];
		float total = 0f;
		for (int p = segment.Start + 1; p < segment.Start + segment.Count; p++)
		{
			total += result.Points[p].DistanceTo(result.Points[p - 1]);
		}

		return total;
	}
}
