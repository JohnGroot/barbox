using BarBox.Core.Drawing;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

namespace BarBox.Tests.Unit.Drawing;

public class PolylineOffsetTests : TestClass
{
	public PolylineOffsetTests(Node testScene)
		: base(testScene)
	{
	}

	[Test]
	public void Offset_StraightLine_PreservesDistanceAtEveryPoint()
	{
		// Arrange - which side is "positive" is Godot's Orthogonal() convention, not something
		// this utility documents, so assert magnitude/perpendicularity rather than a literal sign.
		var output = new FlatPath();
		var line = new[] { new Vector2(0f, 0f), new Vector2(100f, 0f), new Vector2(200f, 0f) };

		// Act
		PolylineOffset.Offset(line, closed: false, distance: 10f, output);

		// Assert
		for (int i = 0; i < output.Count; i++)
		{
			Mathf.Abs(output.Points[i].Y).ShouldBe(10f, 0.01f);
			output.Points[i].X.ShouldBe(line[i].X, 0.01f, "Offsetting a horizontal line must not move points along it");
		}
	}

	[Test]
	public void Offset_ClosedSquare_GrowsOrShrinksIntoALargerOrSmallerAxisAlignedSquare()
	{
		// Arrange - each edge of an axis-aligned square offsets by the same distance, so every
		// 90-degree corner's miter lands exactly on the resulting square's own corner. Which sign
		// of distance grows vs. shrinks depends on Godot's Orthogonal() convention, not this
		// utility's contract, so assert the pair of outcomes rather than which sign does which.
		var grown = new FlatPath();
		var shrunk = new FlatPath();
		var square = new[]
		{
			new Vector2(-50f, -50f),
			new Vector2(50f, -50f),
			new Vector2(50f, 50f),
			new Vector2(-50f, 50f),
		};

		// Act
		PolylineOffset.Offset(square, closed: true, distance: 10f, grown);
		PolylineOffset.Offset(square, closed: true, distance: -10f, shrunk);

		// Assert
		grown.Closed.ShouldBeTrue();
		shrunk.Closed.ShouldBeTrue();
		for (int i = 0; i < grown.Count; i++)
		{
			float grownExtent = Mathf.Max(Mathf.Abs(grown.Points[i].X), Mathf.Abs(grown.Points[i].Y));
			float shrunkExtent = Mathf.Max(Mathf.Abs(shrunk.Points[i].X), Mathf.Abs(shrunk.Points[i].Y));
			Mathf.Abs(grownExtent - shrunkExtent).ShouldBe(20f, 0.1f, "One offset direction should grow the square by 10, the other shrink it by 10");
			(Mathf.Abs(grownExtent - 60f) < 0.1f || Mathf.Abs(grownExtent - 40f) < 0.1f).ShouldBeTrue();
		}
	}

	[Test]
	public void Offset_ClosedLoop_SeamIsContinuous()
	{
		// Arrange - the wraparound joint at the seam (last vertex -> first) must use the same
		// joint formula as every interior vertex, not the open-path start/end cap branch. For any
		// convex corner the miter travel from the source vertex is distance / cos(turn/2), which
		// is always in [distance, distance * MaxMiterRatio] before clamping kicks in — a bound
		// that holds for any convex polygon, not just a hand-picked one.
		var output = new FlatPath();
		var triangle = new[] { new Vector2(0f, 0f), new Vector2(100f, 0f), new Vector2(50f, 80f) };
		const float Distance = 5f;

		// Act
		PolylineOffset.Offset(triangle, closed: true, distance: Distance, output);

		// Assert
		output.Count.ShouldBe(3);
		for (int i = 0; i < output.Count; i++)
		{
			output.Points[i].DistanceTo(triangle[i])
				.ShouldBeInRange(Distance - 0.1f, (Distance * 4f) + 0.1f, $"Vertex {i}'s offset should land within the miter clamp bounds");
		}
	}

	[Test]
	public void Offset_NegativeDistance_OffsetsToTheOppositeSide()
	{
		// Arrange
		var outward = new FlatPath();
		var inward = new FlatPath();
		var line = new[] { new Vector2(0f, 0f), new Vector2(100f, 0f) };

		// Act
		PolylineOffset.Offset(line, closed: false, distance: 10f, outward);
		PolylineOffset.Offset(line, closed: false, distance: -10f, inward);

		// Assert
		outward.Points[0].Y.ShouldBe(-inward.Points[0].Y, 0.01f);
	}

	[Test]
	public void Offset_SharpTurn_ClampsRatherThanSpikingToInfinity()
	{
		// Arrange - a near-180-degree reversal makes the true miter point unbounded
		var output = new FlatPath();
		var reversal = new[] { new Vector2(0f, 0f), new Vector2(100f, 0f), new Vector2(0.001f, 0f) };

		// Act
		PolylineOffset.Offset(reversal, closed: false, distance: 5f, output);

		// Assert
		for (int i = 0; i < output.Count; i++)
		{
			float.IsFinite(output.Points[i].X).ShouldBeTrue();
			float.IsFinite(output.Points[i].Y).ShouldBeTrue();
			output.Points[i].DistanceTo(reversal[i]).ShouldBeLessThan(100f, "A clamped corner must not spike far past the source point");
		}
	}

	[Test]
	public void Offset_TwoPointDegenerateInput_ProducesTwoFiniteOffsetPoints()
	{
		// Arrange
		var output = new FlatPath();
		var twoPoints = new[] { new Vector2(0f, 0f), new Vector2(10f, 0f) };

		// Act
		PolylineOffset.Offset(twoPoints, closed: false, distance: 3f, output);

		// Assert
		output.Count.ShouldBe(2);
		float.IsFinite(output.Points[0].X).ShouldBeTrue();
		float.IsFinite(output.Points[1].X).ShouldBeTrue();
	}

	[Test]
	public void Offset_SinglePointInput_EmitsNothing()
	{
		// Arrange
		var output = new FlatPath();

		// Act
		PolylineOffset.Offset([new Vector2(0f, 0f)], closed: false, distance: 5f, output);

		// Assert
		output.Count.ShouldBe(0);
	}

	[Test]
	public void Offset_ClosedInputWithFewerThanThreePoints_FallsBackToOpen()
	{
		// Arrange - a closed 2-point "loop" has no interior; PolylineOffset must not divide by it
		var output = new FlatPath();
		var twoPoints = new[] { new Vector2(0f, 0f), new Vector2(10f, 0f) };

		// Act
		PolylineOffset.Offset(twoPoints, closed: true, distance: 3f, output);

		// Assert
		output.Closed.ShouldBeFalse();
		output.Count.ShouldBe(2);
		float.IsFinite(output.Points[0].X).ShouldBeTrue();
	}

	[Test]
	public void Offset_NonFiniteInput_EmitsNothing()
	{
		// Arrange
		var output = new FlatPath();
		var points = new[] { new Vector2(0f, 0f), new Vector2(float.NaN, 0f), new Vector2(10f, 10f) };

		// Act
		PolylineOffset.Offset(points, closed: false, distance: 5f, output);

		// Assert
		output.Count.ShouldBe(0);
	}
}
