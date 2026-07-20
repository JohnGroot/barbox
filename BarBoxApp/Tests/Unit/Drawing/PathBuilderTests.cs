using BarBox.Core.Drawing;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

namespace BarBox.Tests.Unit.Drawing;

public class PathBuilderTests : TestClass
{
	public PathBuilderTests(Node testScene)
		: base(testScene)
	{
	}

	[Test]
	public void MoveTo_LineTo_ProducesOneOpenContourWithExactPoints()
	{
		// Arrange
		var builder = new PathBuilder();

		// Act
		builder.MoveTo(new Vector2(0f, 0f)).LineTo(new Vector2(10f, 0f)).LineTo(new Vector2(10f, 10f));

		// Assert
		builder.ContourCount.ShouldBe(1);
		FlatPath contour = builder.Contours[0];
		contour.Closed.ShouldBeFalse();
		contour.Count.ShouldBe(3);
		contour.Points[0].ShouldBe(new Vector2(0f, 0f));
		contour.Points[1].ShouldBe(new Vector2(10f, 0f));
		contour.Points[2].ShouldBe(new Vector2(10f, 10f));
	}

	[Test]
	public void Close_DropsCoincidentDuplicateAndMarksClosed()
	{
		// Arrange - the author closes the loop explicitly, matching PathFlattener's own convention
		// that Closed is a flag, never a repeated vertex
		var builder = new PathBuilder();
		builder.MoveTo(new Vector2(0f, 0f))
			.LineTo(new Vector2(10f, 0f))
			.LineTo(new Vector2(10f, 10f))
			.LineTo(new Vector2(0f, 0f));

		// Act
		builder.Close();

		// Assert
		FlatPath contour = builder.Contours[0];
		contour.Closed.ShouldBeTrue();
		contour.Count.ShouldBe(3, "The author-supplied closing duplicate should not survive as a zero-length segment");
	}

	[Test]
	public void SecondMoveTo_StartsANewDisjointContour_FirstContourUnaffected()
	{
		// Arrange - the "wireframe box is 12 disjoint edges" model, applied in 2D
		var builder = new PathBuilder();
		builder.MoveTo(new Vector2(0f, 0f)).LineTo(new Vector2(10f, 0f));

		// Act
		builder.MoveTo(new Vector2(100f, 100f)).LineTo(new Vector2(110f, 100f));

		// Assert
		builder.ContourCount.ShouldBe(2);
		builder.Contours[0].Count.ShouldBe(2);
		builder.Contours[0].Points[0].ShouldBe(new Vector2(0f, 0f));
		builder.Contours[1].Count.ShouldBe(2);
		builder.Contours[1].Points[0].ShouldBe(new Vector2(100f, 100f));
	}

	[Test]
	public void CubicTo_MatchesPathFlattenerCubicBezier_ForTheSameEndpoints()
	{
		// Arrange
		var p0 = new Vector2(0f, 0f);
		var c0 = new Vector2(0f, 100f);
		var c1 = new Vector2(100f, 100f);
		var p1 = new Vector2(100f, 0f);

		var expected = new FlatPath();
		PathFlattener.CubicBezier(p0, c0, c1, p1, PathFlattener.DefaultTolerance, expected);

		var builder = new PathBuilder();

		// Act
		builder.MoveTo(p0).CubicTo(c0, c1, p1);

		// Assert - no divergent reimplementation of the subdivision math
		FlatPath actual = builder.Contours[0];
		actual.Count.ShouldBe(expected.Count);
		for (int i = 0; i < actual.Count; i++)
		{
			actual.Points[i].ShouldBe(expected.Points[i]);
		}
	}

	[Test]
	public void ArcTo_MatchesPathFlattenerArc_ForTheSameParameters()
	{
		// Arrange
		var center = new Vector2(10f, 20f);
		const float Radius = 50f;
		const float Start = 0.3f;
		const float End = 2.1f;

		var expected = new FlatPath();
		PathFlattener.Arc(center, Radius, Start, End, PathFlattener.DefaultTolerance, expected);

		var builder = new PathBuilder();

		// Act - MoveTo to the arc's own start so ArcTo appends with no coincident-point handling in play
		builder.MoveTo(expected.Points[0]).ArcTo(center, Radius, Start, End);

		// Assert
		FlatPath actual = builder.Contours[0];
		actual.Count.ShouldBe(expected.Count);
		for (int i = 0; i < actual.Count; i++)
		{
			actual.Points[i].ShouldBe(expected.Points[i]);
		}
	}

	[Test]
	public void ArcTo_StartDoesNotMatchLastPoint_StillAppendsArcPointsWithAnImplicitConnectingSegment()
	{
		// Arrange - a mismatched start must degrade to an ordinary polyline connection, not throw
		// or drop the arc
		var center = new Vector2(10f, 20f);
		const float Radius = 50f;
		const float Start = 0.3f;
		const float End = 2.1f;

		var expectedArc = new FlatPath();
		PathFlattener.Arc(center, Radius, Start, End, PathFlattener.DefaultTolerance, expectedArc);

		var builder = new PathBuilder();
		var mismatchedPoint = new Vector2(-1000f, -1000f);

		// Act
		builder.MoveTo(mismatchedPoint).ArcTo(center, Radius, Start, End);

		// Assert - the mismatched point survives as an ordinary vertex, followed by every one of
		// the arc's own points (no dedup fires since nothing coincides with mismatchedPoint)
		FlatPath actual = builder.Contours[0];
		actual.Count.ShouldBe(expectedArc.Count + 1);
		actual.Points[0].ShouldBe(mismatchedPoint);
		for (int i = 0; i < expectedArc.Count; i++)
		{
			actual.Points[i + 1].ShouldBe(expectedArc.Points[i]);
		}
	}

	[Test]
	public void QuadTo_DegreeElevatesLikePathFlattenerQuadBezier()
	{
		// Arrange
		var p0 = new Vector2(0f, 0f);
		var c = new Vector2(50f, 100f);
		var p1 = new Vector2(100f, 0f);

		var expected = new FlatPath();
		PathFlattener.QuadBezier(p0, c, p1, PathFlattener.DefaultTolerance, expected);

		var builder = new PathBuilder();

		// Act
		builder.MoveTo(p0).QuadTo(c, p1);

		// Assert
		FlatPath actual = builder.Contours[0];
		actual.Count.ShouldBe(expected.Count);
		for (int i = 0; i < actual.Count; i++)
		{
			actual.Points[i].ShouldBe(expected.Points[i]);
		}
	}

	[Test]
	public void LineTo_WithoutMoveTo_NoOps()
	{
		// Arrange
		var builder = new PathBuilder();

		// Act
		builder.LineTo(new Vector2(10f, 10f));

		// Assert
		builder.ContourCount.ShouldBe(0, "LineTo before any MoveTo has no open contour to append to");
	}

	[Test]
	public void CubicTo_WithoutMoveTo_NoOps()
	{
		// Arrange
		var builder = new PathBuilder();

		// Act
		builder.CubicTo(new Vector2(1f, 1f), new Vector2(2f, 2f), new Vector2(3f, 3f));

		// Assert
		builder.ContourCount.ShouldBe(0);
	}

	[Test]
	public void ArcTo_WithoutMoveTo_NoOps()
	{
		// Arrange
		var builder = new PathBuilder();

		// Act
		builder.ArcTo(Vector2.Zero, 10f, 0f, Mathf.Pi);

		// Assert
		builder.ContourCount.ShouldBe(0);
	}

	[Test]
	public void Close_WithoutMoveTo_NoOps()
	{
		// Arrange
		var builder = new PathBuilder();

		// Act
		builder.Close();

		// Assert
		builder.ContourCount.ShouldBe(0);
	}

	[Test]
	public void Clear_ResetsForReuse_SecondChainInheritsNothingFromTheFirst()
	{
		// Arrange
		var builder = new PathBuilder();
		builder.MoveTo(new Vector2(0f, 0f)).LineTo(new Vector2(10f, 0f)).Close();

		// Act
		builder.Clear();
		builder.MoveTo(new Vector2(5f, 5f)).LineTo(new Vector2(6f, 6f));

		// Assert
		builder.ContourCount.ShouldBe(1);
		FlatPath contour = builder.Contours[0];
		contour.Closed.ShouldBeFalse("The prior chain's Close() must not leak into the reused instance");
		contour.Count.ShouldBe(2);
		contour.Points[0].ShouldBe(new Vector2(5f, 5f));
	}

	[Test]
	public void FinalizeT_RunsOnceEvenAfterAReadFollowedByMoreAppends()
	{
		// Arrange - reading ContourCount/Contours mid-construction must not corrupt later appends
		var builder = new PathBuilder();
		builder.MoveTo(new Vector2(0f, 0f)).LineTo(new Vector2(10f, 0f));
		int countBeforeMore = builder.ContourCount;

		// Act
		builder.LineTo(new Vector2(10f, 10f));

		// Assert
		countBeforeMore.ShouldBe(1);
		builder.Contours[0].Count.ShouldBe(3);
		builder.Contours[0].T[2].ShouldBeGreaterThan(builder.Contours[0].T[1]);
	}
}
