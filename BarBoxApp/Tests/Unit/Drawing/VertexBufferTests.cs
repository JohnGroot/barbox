using BarBox.Core.Drawing;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

namespace BarBox.Tests.Unit.Drawing;

public class VertexBufferTests : TestClass
{
	public VertexBufferTests(Node testScene)
		: base(testScene)
	{
	}

	[Test]
	public void AddVertex_ReturnsAbsoluteIndex()
	{
		// Arrange
		var buffer = new VertexBuffer();

		// Act
		int first = buffer.AddVertex(Vector2.Zero, Colors.Red);
		int second = buffer.AddVertex(Vector2.One, Colors.Blue);

		// Assert
		first.ShouldBe(0);
		second.ShouldBe(1);
		buffer.VertexCount.ShouldBe(2);
	}

	[Test]
	public void Append_RebasesIndicesOntoExistingVertices()
	{
		// Arrange
		var target = MakeTriangle(new Vector2(0f, 0f), Colors.Red);
		var source = MakeTriangle(new Vector2(10f, 10f), Colors.Blue);

		// Act
		target.Append(source);

		// Assert
		target.VertexCount.ShouldBe(6);
		target.IndexCount.ShouldBe(6);
		target.Indices[3].ShouldBe(3, "The appended triangle's indices should shift by the vertex base");
		target.Indices[4].ShouldBe(4);
		target.Indices[5].ShouldBe(5);
		target.Points[3].ShouldBe(new Vector2(10f, 10f));
	}

	[Test]
	public void Append_ThreeBuffersInOrder_AllIndicesResolveToCorrectPoints()
	{
		// Arrange
		var a = MakeTriangle(new Vector2(0f, 0f), Colors.Red);
		var b = MakeTriangle(new Vector2(100f, 0f), Colors.Green);
		var c = MakeTriangle(new Vector2(200f, 0f), Colors.Blue);
		var target = new VertexBuffer();

		// Act
		target.Append(a);
		target.Append(b);
		target.Append(c);

		// Assert
		var expected = new[] { a, b, c };
		for (int tri = 0; tri < 3; tri++)
		{
			for (int corner = 0; corner < 3; corner++)
			{
				int index = target.Indices[(tri * 3) + corner];
				target.Points[index].ShouldBe(
					expected[tri].Points[expected[tri].Indices[corner]],
					$"Triangle {tri} corner {corner} should resolve to its source point");
			}
		}
	}

	[Test]
	public void Append_MiddleSourceGrows_OtherSourcesSurviveRebuild()
	{
		// Arrange
		var a = MakeTriangle(new Vector2(0f, 0f), Colors.Red);
		var b = MakeTriangle(new Vector2(100f, 0f), Colors.Green);
		var c = MakeTriangle(new Vector2(200f, 0f), Colors.Blue);
		var target = new VertexBuffer();
		target.Append(a);
		target.Append(b);
		target.Append(c);

		// Act - the middle shape grows, then the bucket rebuilds from scratch
		AddTriangleAt(b, new Vector2(150f, 50f), Colors.Green);
		target.Clear();
		target.Append(a);
		target.Append(b);
		target.Append(c);

		// Assert
		target.VertexCount.ShouldBe(12, "3 + 6 + 3 vertices after b grew");
		target.IndexCount.ShouldBe(12);
		target.Points[0].ShouldBe(new Vector2(0f, 0f), "Shape a should be unaffected by b growing");
		target.Points[9].ShouldBe(new Vector2(200f, 0f), "Shape c should relocate intact behind the grown b");
		for (int i = 0; i < target.IndexCount; i++)
		{
			target.Indices[i].ShouldBeInRange(0, target.VertexCount - 1);
		}
	}

	[Test]
	public void Append_MiddleSourceShrinks_OtherSourcesSurviveRebuild()
	{
		// Arrange
		var a = MakeTriangle(new Vector2(0f, 0f), Colors.Red);
		var b = MakeTriangle(new Vector2(100f, 0f), Colors.Green);
		AddTriangleAt(b, new Vector2(150f, 50f), Colors.Green);
		var c = MakeTriangle(new Vector2(200f, 0f), Colors.Blue);
		var target = new VertexBuffer();
		target.Append(a);
		target.Append(b);
		target.Append(c);

		// Act - b shrinks back to a single triangle, then the bucket rebuilds
		b.Clear();
		AddTriangleAt(b, new Vector2(100f, 0f), Colors.Green);
		target.Clear();
		target.Append(a);
		target.Append(b);
		target.Append(c);

		// Assert
		target.VertexCount.ShouldBe(9);
		target.Points[0].ShouldBe(new Vector2(0f, 0f), "Shape a should be unaffected by b shrinking");
		target.Points[6].ShouldBe(new Vector2(200f, 0f), "Shape c should relocate intact after the shrunk b");
		for (int i = 0; i < target.IndexCount; i++)
		{
			target.Indices[i].ShouldBeInRange(0, target.VertexCount - 1);
		}
	}

	[Test]
	public void EnsureCapacity_GrowsWithoutLosingContents()
	{
		// Arrange
		var buffer = new VertexBuffer(vertexCapacity: 2, indexCapacity: 3);
		buffer.AddVertex(new Vector2(1f, 2f), Colors.Red);
		buffer.AddVertex(new Vector2(3f, 4f), Colors.Green);

		// Act
		buffer.AddVertex(new Vector2(5f, 6f), Colors.Blue);

		// Assert
		buffer.Points.Length.ShouldBeGreaterThanOrEqualTo(3);
		buffer.Points[0].ShouldBe(new Vector2(1f, 2f), "Existing points should survive the resize");
		buffer.Points[1].ShouldBe(new Vector2(3f, 4f));
		buffer.Points[2].ShouldBe(new Vector2(5f, 6f));
		buffer.Colors[0].ShouldBe(Colors.Red);
	}

	[Test]
	public void EnsureCapacity_KeepsPointsAndColorsSameLength()
	{
		// Arrange - RenderingServer requires colors length 1 or exactly the point count
		var buffer = new VertexBuffer(vertexCapacity: 2, indexCapacity: 3);

		// Act
		for (int i = 0; i < 50; i++)
		{
			buffer.AddVertex(new Vector2(i, i), Colors.White);
		}

		// Assert
		buffer.Colors.Length.ShouldBe(buffer.Points.Length);
		buffer.ColorSpan.Length.ShouldBe(buffer.PointSpan.Length);
	}

	[Test]
	public void Clear_ResetsCountsButRetainsArrays()
	{
		// Arrange - the zero-steady-state-allocation invariant
		var buffer = new VertexBuffer(vertexCapacity: 2, indexCapacity: 3);
		for (int i = 0; i < 100; i++)
		{
			buffer.AddVertex(new Vector2(i, i), Colors.White);
		}

		var grownArray = buffer.Points;
		int grownLength = buffer.Points.Length;

		// Act
		buffer.Clear();
		for (int i = 0; i < 100; i++)
		{
			buffer.AddVertex(new Vector2(i, i), Colors.White);
		}

		// Assert
		buffer.Points.Length.ShouldBe(grownLength, "Refilling to the same size should not reallocate");
		ReferenceEquals(buffer.Points, grownArray).ShouldBeTrue("Clear should retain the array instance");
		buffer.VertexCount.ShouldBe(100);
	}

	[Test]
	public void Clear_EmptiesSpans()
	{
		// Arrange
		var buffer = MakeTriangle(Vector2.Zero, Colors.Red);

		// Act
		buffer.Clear();

		// Assert
		buffer.PointSpan.Length.ShouldBe(0);
		buffer.IndexSpan.Length.ShouldBe(0);
		buffer.IsEmpty.ShouldBeTrue();
	}

	[Test]
	public void Append_WithTransform_BakesPositionsAndPreservesColors()
	{
		// Arrange
		var source = MakeTriangle(new Vector2(1f, 0f), Colors.Red);
		var target = new VertexBuffer();
		var xform = new Transform2D(0f, new Vector2(10f, 20f));

		// Act
		target.Append(source, xform);

		// Assert
		target.Points[0].ShouldBe(new Vector2(11f, 20f), "Translation should be baked into the position");
		target.Colors[0].ShouldBe(Colors.Red, "Colors carry resolved feather alpha and must pass through");
		target.Indices[0].ShouldBe(0);
	}

	[Test]
	public void Append_EmptyOrNullSource_IsNoOp()
	{
		// Arrange
		var target = MakeTriangle(Vector2.Zero, Colors.Red);
		int before = target.VertexCount;

		// Act
		target.Append(null);
		target.Append(new VertexBuffer());

		// Assert
		target.VertexCount.ShouldBe(before);
		target.IndexCount.ShouldBe(3);
	}

	private static VertexBuffer MakeTriangle(Vector2 origin, Color color)
	{
		var buffer = new VertexBuffer();
		AddTriangleAt(buffer, origin, color);
		return buffer;
	}

	private static void AddTriangleAt(VertexBuffer buffer, Vector2 origin, Color color)
	{
		int a = buffer.AddVertex(origin, color);
		int b = buffer.AddVertex(origin + new Vector2(1f, 0f), color);
		int c = buffer.AddVertex(origin + new Vector2(0f, 1f), color);
		buffer.AddTriangle(a, b, c);
	}
}
