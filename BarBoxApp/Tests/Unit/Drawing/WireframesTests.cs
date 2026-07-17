using System.Collections.Generic;
using BarBox.Core.Drawing;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

namespace BarBox.Tests.Unit.Drawing;

public class WireframesTests : TestClass
{
	public WireframesTests(Node testScene)
		: base(testScene)
	{
	}

	[Test]
	public void Box_EmitsSixContoursCoveringTwelveEdges()
	{
		// Arrange
		var set = new Contour3Set();

		// Act
		Wireframes.Box(new Vector3(2f, 2f, 2f), set);

		// Assert
		set.ContourCount.ShouldBe(6, "Two closed faces plus four struts");
		CountEdges(set).ShouldBe(12, "A box has twelve edges however the contours are split");
		CountCorners(set).ShouldBe(8);
	}

	[Test]
	public void Box_FacesAreClosedAndStrutsAreNot()
	{
		// Arrange
		var set = new Contour3Set();

		// Act
		Wireframes.Box(Vector3.One, set);

		// Assert
		set.IsClosed(0).ShouldBeTrue();
		set.IsClosed(1).ShouldBeTrue();
		for (int i = 2; i < set.ContourCount; i++)
		{
			set.IsClosed(i).ShouldBeFalse("Struts are open two-point contours");
			set.Span(i).Length.ShouldBe(2);
		}
	}

	[Test]
	public void Box_PointsAreFiniteAndWithinExtents()
	{
		// Arrange
		var set = new Contour3Set();
		var size = new Vector3(4f, 6f, 8f);

		// Act
		Wireframes.Box(size, set);

		// Assert
		for (int i = 0; i < set.PointCount; i++)
		{
			Vector3 p = set.Points[i];
			p.IsFinite().ShouldBeTrue();
			Mathf.Abs(p.X).ShouldBeLessThanOrEqualTo((size.X * 0.5f) + 1e-4f);
			Mathf.Abs(p.Y).ShouldBeLessThanOrEqualTo((size.Y * 0.5f) + 1e-4f);
			Mathf.Abs(p.Z).ShouldBeLessThanOrEqualTo((size.Z * 0.5f) + 1e-4f);
		}
	}

	[Test]
	public void Grid_EmitsOneContourPerLine()
	{
		// Arrange
		var set = new Contour3Set();

		// Act
		Wireframes.Grid(10f, 20f, 4, 2, set);

		// Assert
		set.ContourCount.ShouldBe(4 + 2 + 2, "cellsX + cellsZ + 2 bounding lines");
		set.PointCount.ShouldBe(set.ContourCount * 2);
	}

	[Test]
	public void Grid_SpansTheRequestedExtents()
	{
		// Arrange
		var set = new Contour3Set();

		// Act
		Wireframes.Grid(10f, 20f, 2, 2, set);

		// Assert
		float minX = float.MaxValue;
		float maxX = float.MinValue;
		for (int i = 0; i < set.PointCount; i++)
		{
			minX = Mathf.Min(minX, set.Points[i].X);
			maxX = Mathf.Max(maxX, set.Points[i].X);
		}

		minX.ShouldBe(-5f, 1e-4f);
		maxX.ShouldBe(5f, 1e-4f);
	}

	[Test]
	public void Grid_WithZeroCells_StillEmitsBoundingLines()
	{
		// Arrange
		var set = new Contour3Set();

		// Act
		Wireframes.Grid(10f, 10f, 0, 0, set);

		// Assert
		set.ContourCount.ShouldBe(2, "Zero cells still yields one line per axis");
	}

	[Test]
	public void Grid_WithNegativeCells_EmitsNothingAndDoesNotThrow()
	{
		// Arrange
		var set = new Contour3Set();

		// Act
		Wireframes.Grid(10f, 10f, -1, 2, set);

		// Assert
		set.ContourCount.ShouldBe(0);
	}

	[Test]
	public void Contour3Set_ClearRetainsArrays()
	{
		// Arrange
		var set = new Contour3Set(4, 2);
		for (int i = 0; i < 40; i++)
		{
			set.AddPoint(new Vector3(i, i, i));
		}

		Vector3[] grown = set.Points;

		// Act
		set.Clear();

		// Assert
		set.PointCount.ShouldBe(0);
		set.Points.ShouldBeSameAs(grown, "Clear must retain the grown array; that is what makes reuse free");
	}

	[Test]
	public void Contour3Set_SpanReturnsOnlyItsOwnContour()
	{
		// Arrange
		var set = new Contour3Set();
		set.AddPoint(Vector3.Zero);
		set.AddPoint(Vector3.One);
		set.AddContour(0, 2, false);
		int start = set.BeginContour();
		set.AddPoint(new Vector3(5f, 5f, 5f));
		set.AddPoint(new Vector3(6f, 6f, 6f));
		set.AddPoint(new Vector3(7f, 7f, 7f));
		set.EndContour(start, true);

		// Act
		System.ReadOnlySpan<Vector3> second = set.Span(1);

		// Assert
		set.ContourCount.ShouldBe(2);
		second.Length.ShouldBe(3);
		second[0].ShouldBe(new Vector3(5f, 5f, 5f));
		set.IsClosed(1).ShouldBeTrue();
	}

	[Test]
	public void Contour3Set_EndContourWithNoPoints_AddsNothing()
	{
		// Arrange
		var set = new Contour3Set();
		int start = set.BeginContour();

		// Act
		set.EndContour(start, false);

		// Assert
		set.ContourCount.ShouldBe(0);
	}

	private static int CountEdges(Contour3Set set)
	{
		int edges = 0;
		for (int i = 0; i < set.ContourCount; i++)
		{
			int count = set.Span(i).Length;
			edges += set.IsClosed(i) ? count : count - 1;
		}

		return edges;
	}

	private static int CountCorners(Contour3Set set)
	{
		var unique = new HashSet<Vector3>();
		for (int i = 0; i < set.PointCount; i++)
		{
			unique.Add(set.Points[i]);
		}

		return unique.Count;
	}
}
