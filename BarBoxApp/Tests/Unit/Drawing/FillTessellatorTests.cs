using System;
using BarBox.Core.Drawing;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

namespace BarBox.Tests.Unit.Drawing;

public class FillTessellatorTests : TestClass
{
	public FillTessellatorTests(Node testScene)
		: base(testScene)
	{
	}

	[Test]
	public void Tessellate_ConvexSquare_PreservesArea()
	{
		// Arrange
		var buffer = new VertexBuffer();
		var square = new[]
		{
			new Vector2(0f, 0f),
			new Vector2(100f, 0f),
			new Vector2(100f, 100f),
			new Vector2(0f, 100f),
		};

		// Act
		bool ok = FillTessellator.Tessellate(square, Colors.Blue, buffer);

		// Assert
		ok.ShouldBeTrue();
		DrawingTestHelpers.AssertWellFormed(buffer);
		DrawingTestHelpers.TotalArea(buffer).ShouldBe(10000f, 0.1f);
	}

	[Test]
	public void Tessellate_ConcaveLShape_TriangulatesFullyWithoutOverfilling()
	{
		// Arrange
		var buffer = new VertexBuffer();
		var lShape = new[]
		{
			new Vector2(0f, 0f),
			new Vector2(100f, 0f),
			new Vector2(100f, 50f),
			new Vector2(50f, 50f),
			new Vector2(50f, 100f),
			new Vector2(0f, 100f),
		};

		// Act
		bool ok = FillTessellator.Tessellate(lShape, Colors.Blue, buffer);

		// Assert
		ok.ShouldBeTrue();
		DrawingTestHelpers.AssertWellFormed(buffer);
		DrawingTestHelpers.TotalArea(buffer)
			.ShouldBe(DrawingTestHelpers.ShoelaceArea(lShape), 0.1f, "A concave fill should match its shoelace area exactly");
		DrawingTestHelpers.IsCovered(buffer, new Vector2(25f, 25f)).ShouldBeTrue("The solid corner should be filled");
		DrawingTestHelpers.IsCovered(buffer, new Vector2(75f, 75f)).ShouldBeFalse("The notch should stay empty");
	}

	[Test]
	public void Tessellate_EitherWinding_ProducesTheSameArea()
	{
		// Arrange
		var clockwise = new VertexBuffer();
		var counterClockwise = new VertexBuffer();
		var ccw = new[] { new Vector2(0f, 0f), new Vector2(100f, 0f), new Vector2(100f, 100f) };
		var cw = new[] { new Vector2(100f, 100f), new Vector2(100f, 0f), new Vector2(0f, 0f) };

		// Act
		bool ccwOk = FillTessellator.Tessellate(ccw, Colors.Blue, counterClockwise);
		bool cwOk = FillTessellator.Tessellate(cw, Colors.Blue, clockwise);

		// Assert
		ccwOk.ShouldBeTrue();
		cwOk.ShouldBeTrue("TriangulatePolygon accepts either winding");
		DrawingTestHelpers.TotalArea(clockwise).ShouldBe(DrawingTestHelpers.TotalArea(counterClockwise), 0.1f);
	}

	[Test]
	public void Tessellate_UsesTheGivenColorForEveryVertex()
	{
		// Arrange
		var buffer = new VertexBuffer();
		var triangle = new[] { new Vector2(0f, 0f), new Vector2(10f, 0f), new Vector2(0f, 10f) };

		// Act
		FillTessellator.Tessellate(triangle, Colors.Orange, buffer);

		// Assert
		for (int i = 0; i < buffer.VertexCount; i++)
		{
			buffer.Colors[i].ShouldBe(Colors.Orange);
		}
	}

	[Test]
	public void Tessellate_SelfIntersectingPolygon_DoesNotThrowAndEmitsNothing()
	{
		// Arrange - a figure-eight; TriangulatePolygon signals this with an empty array
		var buffer = new VertexBuffer();
		var figureEight = new[]
		{
			new Vector2(0f, 0f),
			new Vector2(100f, 100f),
			new Vector2(100f, 0f),
			new Vector2(0f, 100f),
		};

		// Act
		bool ok = FillTessellator.Tessellate(figureEight, Colors.Blue, buffer);

		// Assert - either it triangulates or it declines, but it must never throw or half-emit
		if (!ok)
		{
			buffer.VertexCount.ShouldBe(0, "A declined fill must not leave partial geometry");
			buffer.IndexCount.ShouldBe(0);
		}
		else
		{
			DrawingTestHelpers.AssertWellFormed(buffer);
		}
	}

	[Test]
	public void Tessellate_DegenerateInput_IsRejected()
	{
		// Arrange
		var buffer = new VertexBuffer();
		var collinear = new[] { new Vector2(0f, 0f), new Vector2(50f, 0f), new Vector2(100f, 0f) };
		var twoPoints = new[] { new Vector2(0f, 0f), new Vector2(50f, 0f) };

		// Act
		bool collinearOk = FillTessellator.Tessellate(collinear, Colors.Blue, buffer);
		bool twoPointsOk = FillTessellator.Tessellate(twoPoints, Colors.Blue, buffer);
		bool emptyOk = FillTessellator.Tessellate([], Colors.Blue, buffer);

		// Assert
		collinearOk.ShouldBeFalse("A zero-area polygon has nothing to fill");
		twoPointsOk.ShouldBeFalse();
		emptyOk.ShouldBeFalse();
		buffer.VertexCount.ShouldBe(0);
	}

	[Test]
	public void Tessellate_NonFinitePoint_IsRejected()
	{
		// Arrange
		var buffer = new VertexBuffer();
		var polygon = new[] { new Vector2(0f, 0f), new Vector2(float.NaN, 0f), new Vector2(0f, 100f) };

		// Act
		bool ok = FillTessellator.Tessellate(polygon, Colors.Blue, buffer);

		// Assert
		ok.ShouldBeFalse();
		buffer.VertexCount.ShouldBe(0);
	}

	[Test]
	public void Tessellate_AppendsWithoutDisturbingExistingBufferContents()
	{
		// Arrange
		var buffer = new VertexBuffer();
		buffer.AddVertex(new Vector2(-1f, -1f), Colors.Green);
		buffer.AddVertex(new Vector2(-2f, -2f), Colors.Green);
		buffer.AddVertex(new Vector2(-3f, -3f), Colors.Green);
		buffer.AddTriangle(0, 1, 2);
		int vertexBase = buffer.VertexCount;

		// Act
		FillTessellator.Tessellate(
			[new Vector2(0f, 0f), new Vector2(10f, 0f), new Vector2(0f, 10f)],
			Colors.Blue,
			buffer);

		// Assert
		buffer.Points[0].ShouldBe(new Vector2(-1f, -1f));
		buffer.Indices[0].ShouldBe(0, "Pre-existing indices should be untouched");
		for (int i = 3; i < buffer.IndexCount; i++)
		{
			buffer.Indices[i].ShouldBeGreaterThanOrEqualTo(vertexBase, "Appended indices should be rebased");
		}

		DrawingTestHelpers.AssertWellFormed(buffer);
	}

	[Test]
	public void TessellateClipped_BandInsideSubject_ClipsToTheOverlap()
	{
		// Arrange
		var buffer = new VertexBuffer();
		var subject = new[]
		{
			new Vector2(0f, 0f),
			new Vector2(100f, 0f),
			new Vector2(100f, 100f),
			new Vector2(0f, 100f),
		};
		var band = new[]
		{
			new Vector2(-50f, 20f),
			new Vector2(150f, 20f),
			new Vector2(150f, 40f),
			new Vector2(-50f, 40f),
		};

		// Act
		int emitted = FillTessellator.TessellateClipped(subject, band, Colors.Yellow, buffer);

		// Assert
		emitted.ShouldBe(1);
		DrawingTestHelpers.AssertWellFormed(buffer);
		DrawingTestHelpers.TotalArea(buffer).ShouldBe(2000f, 1f, "The band should clip to 100 x 20 inside the subject");
	}

	[Test]
	public void TessellateClipped_BandCrossingAConcaveSubject_TriangulatesEveryIsland()
	{
		// Arrange - a C shape split by a horizontal band yields two disjoint pieces
		var buffer = new VertexBuffer();
		var cShape = new[]
		{
			new Vector2(0f, 0f),
			new Vector2(100f, 0f),
			new Vector2(100f, 20f),
			new Vector2(20f, 20f),
			new Vector2(20f, 80f),
			new Vector2(100f, 80f),
			new Vector2(100f, 100f),
			new Vector2(0f, 100f),
		};
		var band = new[]
		{
			new Vector2(-10f, 5f),
			new Vector2(110f, 5f),
			new Vector2(110f, 95f),
			new Vector2(-10f, 95f),
		};

		// Act
		int emitted = FillTessellator.TessellateClipped(cShape, band, Colors.Yellow, buffer);

		// Assert
		emitted.ShouldBeGreaterThanOrEqualTo(1, "The clip should produce at least one island");
		DrawingTestHelpers.AssertWellFormed(buffer);
		DrawingTestHelpers.IsCovered(buffer, new Vector2(50f, 10f)).ShouldBeTrue("The top arm should be filled");
		DrawingTestHelpers.IsCovered(buffer, new Vector2(50f, 90f)).ShouldBeTrue("The bottom arm should be filled");
		DrawingTestHelpers.IsCovered(buffer, new Vector2(60f, 50f)).ShouldBeFalse("The C's mouth should stay empty");
	}

	[Test]
	public void TessellateClipped_CheckerBands_StayInsideTheSubject()
	{
		// Arrange - the shape of the checkered start line
		var buffer = new VertexBuffer();
		var subject = new[]
		{
			new Vector2(0f, 0f),
			new Vector2(80f, 0f),
			new Vector2(80f, 20f),
			new Vector2(0f, 20f),
		};
		var cell = new[]
		{
			new Vector2(-10f, -10f),
			new Vector2(20f, -10f),
			new Vector2(20f, 30f),
			new Vector2(-10f, 30f),
		};

		// Act
		int emitted = FillTessellator.TessellateClipped(subject, cell, Colors.White, buffer);

		// Assert
		emitted.ShouldBe(1);
		for (int i = 0; i < buffer.VertexCount; i++)
		{
			buffer.Points[i].X.ShouldBeInRange(-0.01f, 80.01f, "Clipped cells must not escape the subject");
			buffer.Points[i].Y.ShouldBeInRange(-0.01f, 20.01f);
		}
	}

	[Test]
	public void TessellateClipped_NonOverlappingBand_EmitsNothing()
	{
		// Arrange
		var buffer = new VertexBuffer();
		var subject = new[] { new Vector2(0f, 0f), new Vector2(10f, 0f), new Vector2(10f, 10f) };
		var band = new[] { new Vector2(500f, 500f), new Vector2(510f, 500f), new Vector2(510f, 510f) };

		// Act
		int emitted = FillTessellator.TessellateClipped(subject, band, Colors.Yellow, buffer);

		// Assert
		emitted.ShouldBe(0);
		buffer.VertexCount.ShouldBe(0);
	}

	[Test]
	public void TessellateClipped_NullOrDegenerateInput_EmitsNothing()
	{
		// Arrange
		var buffer = new VertexBuffer();
		var valid = new[] { new Vector2(0f, 0f), new Vector2(10f, 0f), new Vector2(10f, 10f) };

		// Act
		int nullSubject = FillTessellator.TessellateClipped(null, valid, Colors.Yellow, buffer);
		int nullBands = FillTessellator.TessellateClipped(valid, null, Colors.Yellow, buffer);
		int tooFewPoints = FillTessellator.TessellateClipped(valid, [new Vector2(0f, 0f)], Colors.Yellow, buffer);

		// Assert
		nullSubject.ShouldBe(0);
		nullBands.ShouldBe(0);
		tooFewPoints.ShouldBe(0);
		buffer.VertexCount.ShouldBe(0);
	}

	[Test]
	public void IsDegenerate_ClassifiesZeroAreaAndTooFewPoints()
	{
		// Arrange
		var real = new[] { new Vector2(0f, 0f), new Vector2(10f, 0f), new Vector2(0f, 10f) };
		var collinear = new[] { new Vector2(0f, 0f), new Vector2(5f, 0f), new Vector2(10f, 0f) };

		// Act & Assert
		FillTessellator.IsDegenerate(real).ShouldBeFalse();
		FillTessellator.IsDegenerate(collinear).ShouldBeTrue();
		FillTessellator.IsDegenerate([]).ShouldBeTrue();
	}
}
