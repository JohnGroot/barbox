using BarBox.Core.Drawing;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

namespace BarBox.Tests.Unit.Drawing;

public class PatternFillTests : TestClass
{
	public PatternFillTests(Node testScene)
		: base(testScene)
	{
	}

	[Test]
	public void Checker_TilesARectangleFully_TotalAreaMatchesTheSubject()
	{
		// Arrange
		var buffer = new VertexBuffer();
		var subject = new[]
		{
			new Vector2(0f, 0f),
			new Vector2(80f, 0f),
			new Vector2(80f, 40f),
			new Vector2(0f, 40f),
		};

		// Act
		PatternFill.Checker(subject, Vector2.Zero, 20f, Colors.Red, Colors.White, buffer);

		// Assert
		DrawingTestHelpers.AssertWellFormed(buffer);
		DrawingTestHelpers.TotalArea(buffer).ShouldBe(DrawingTestHelpers.ShoelaceArea(subject), 0.1f);
	}

	[Test]
	public void Checker_KnownSmallGrid_AlternatesColorsAtCellCenters()
	{
		// Arrange - a 2x2 grid over a 40x40 square, cellSize 20: (ix+iy) parity picks the color
		var buffer = new VertexBuffer();
		var subject = new[]
		{
			new Vector2(0f, 0f),
			new Vector2(40f, 0f),
			new Vector2(40f, 40f),
			new Vector2(0f, 40f),
		};

		// Act
		PatternFill.Checker(subject, Vector2.Zero, 20f, Colors.Red, Colors.White, buffer);

		// Assert
		Color? topLeft = DrawingTestHelpers.ColorAt(buffer, new Vector2(10f, 10f));
		Color? topRight = DrawingTestHelpers.ColorAt(buffer, new Vector2(30f, 10f));
		Color? bottomLeft = DrawingTestHelpers.ColorAt(buffer, new Vector2(10f, 30f));
		Color? bottomRight = DrawingTestHelpers.ColorAt(buffer, new Vector2(30f, 30f));

		topLeft.ShouldNotBeNull();
		topRight.ShouldNotBeNull();
		bottomLeft.ShouldNotBeNull();
		bottomRight.ShouldNotBeNull();

		topLeft.Value.ShouldBe(Colors.Red);
		topRight.Value.ShouldBe(Colors.White);
		bottomLeft.Value.ShouldBe(Colors.White);
		bottomRight.Value.ShouldBe(Colors.Red);
	}

	[Test]
	public void Checker_ConcaveSubject_CoversTheShapeButNotItsNotch()
	{
		// Arrange - the same C shape FillTessellatorTests uses for its island coverage
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

		// Act
		PatternFill.Checker(cShape, Vector2.Zero, 10f, Colors.Red, Colors.White, buffer);

		// Assert
		DrawingTestHelpers.AssertWellFormed(buffer);
		DrawingTestHelpers.IsCovered(buffer, new Vector2(50f, 10f)).ShouldBeTrue("The top arm should be filled");
		DrawingTestHelpers.IsCovered(buffer, new Vector2(50f, 90f)).ShouldBeTrue("The bottom arm should be filled");
		DrawingTestHelpers.IsCovered(buffer, new Vector2(60f, 50f)).ShouldBeFalse("The C's mouth should stay empty");
	}

	[Test]
	public void Checker_DegenerateOrInvalidInput_EmitsNothing()
	{
		// Arrange
		var buffer = new VertexBuffer();
		var valid = new[] { new Vector2(0f, 0f), new Vector2(10f, 0f), new Vector2(10f, 10f), new Vector2(0f, 10f) };
		var tooFewPoints = new[] { new Vector2(0f, 0f), new Vector2(10f, 0f) };

		// Act
		PatternFill.Checker(tooFewPoints, Vector2.Zero, 5f, Colors.Red, Colors.White, buffer);
		int afterBadSubject = buffer.VertexCount;

		PatternFill.Checker(valid, Vector2.Zero, 0f, Colors.Red, Colors.White, buffer);
		int afterZeroCellSize = buffer.VertexCount;

		PatternFill.Checker(valid, Vector2.Zero, float.NaN, Colors.Red, Colors.White, buffer);
		int afterNonFiniteCellSize = buffer.VertexCount;

		// Assert
		afterBadSubject.ShouldBe(0);
		afterZeroCellSize.ShouldBe(0);
		afterNonFiniteCellSize.ShouldBe(0);
	}

	[Test]
	public void Checker_ReplacesRatherThanAppendsToExistingBufferContent()
	{
		// Arrange - Checker/Stripes regenerate a whole pattern per call, unlike FillTessellator's
		// append-only Tessellate; a reused scratch buffer must not accumulate stale geometry.
		var buffer = new VertexBuffer();
		buffer.AddVertex(new Vector2(-1f, -1f), Colors.Green);
		buffer.AddVertex(new Vector2(-2f, -2f), Colors.Green);
		buffer.AddVertex(new Vector2(-3f, -3f), Colors.Green);
		buffer.AddTriangle(0, 1, 2);
		var subject = new[] { new Vector2(0f, 0f), new Vector2(20f, 0f), new Vector2(20f, 20f), new Vector2(0f, 20f) };

		// Act
		PatternFill.Checker(subject, Vector2.Zero, 10f, Colors.Red, Colors.White, buffer);

		// Assert
		for (int i = 0; i < buffer.VertexCount; i++)
		{
			buffer.Points[i].X.ShouldBeGreaterThanOrEqualTo(-0.01f, "Stale geometry from before the call must be gone");
		}
	}

	[Test]
	public void Stripes_CoverageSumsToTheSubjectArea_AtAnyAngle()
	{
		// Arrange
		var axisAligned = new VertexBuffer();
		var rotated = new VertexBuffer();
		var subject = new[]
		{
			new Vector2(0f, 0f),
			new Vector2(100f, 0f),
			new Vector2(100f, 100f),
			new Vector2(0f, 100f),
		};

		// Act
		PatternFill.Stripes(subject, 0f, 15f, Colors.Red, Colors.White, axisAligned);
		PatternFill.Stripes(subject, Mathf.Pi / 6f, 15f, Colors.Red, Colors.White, rotated);

		// Assert
		DrawingTestHelpers.AssertWellFormed(axisAligned);
		DrawingTestHelpers.AssertWellFormed(rotated);
		DrawingTestHelpers.TotalArea(axisAligned).ShouldBe(10000f, 1f);
		DrawingTestHelpers.TotalArea(rotated).ShouldBe(10000f, 1f, "Rotating the stripe angle must not leave gaps or overlap");
	}

	[Test]
	public void Stripes_AlternatesBothColorsAcrossTheSubject()
	{
		// Arrange
		var buffer = new VertexBuffer();
		var subject = new[] { new Vector2(0f, 0f), new Vector2(100f, 0f), new Vector2(100f, 20f), new Vector2(0f, 20f) };

		// Act
		PatternFill.Stripes(subject, 0f, 10f, Colors.Red, Colors.White, buffer);

		// Assert
		bool sawRed = false;
		bool sawWhite = false;
		for (int i = 0; i < buffer.VertexCount; i++)
		{
			if (buffer.Colors[i] == Colors.Red)
			{
				sawRed = true;
			}

			if (buffer.Colors[i] == Colors.White)
			{
				sawWhite = true;
			}
		}

		sawRed.ShouldBeTrue();
		sawWhite.ShouldBeTrue();
	}

	[Test]
	public void Stripes_DegenerateOrInvalidInput_EmitsNothing()
	{
		// Arrange
		var buffer = new VertexBuffer();
		var valid = new[] { new Vector2(0f, 0f), new Vector2(10f, 0f), new Vector2(10f, 10f), new Vector2(0f, 10f) };
		var tooFewPoints = new[] { new Vector2(0f, 0f), new Vector2(10f, 0f) };

		// Act
		PatternFill.Stripes(tooFewPoints, 0f, 5f, Colors.Red, Colors.White, buffer);
		int afterBadSubject = buffer.VertexCount;

		PatternFill.Stripes(valid, 0f, 0f, Colors.Red, Colors.White, buffer);
		int afterZeroBandWidth = buffer.VertexCount;

		// Assert
		afterBadSubject.ShouldBe(0);
		afterZeroBandWidth.ShouldBe(0);
	}
}
