using BarBox.Core.Drawing;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

namespace BarBox.Tests.Unit.Drawing;

public class ShapeTests : TestClass
{
	private static readonly Vector2[] Square =
	[
		new Vector2(0f, 0f),
		new Vector2(40f, 0f),
		new Vector2(40f, 40f),
		new Vector2(0f, 40f),
	];

	private ShapeCanvas _canvas;

	public ShapeTests(Node testScene)
		: base(testScene)
	{
	}

	[Setup]
	public void Setup()
	{
		_canvas = new ShapeCanvas();
		TestScene.AddChild(_canvas);
		_canvas.PixelScale = 1f;
	}

	[Cleanup]
	public void Cleanup()
	{
		_canvas.QueueFree();
		_canvas = null;
	}

	[Test]
	public void SetStroke_RaisesTessNotFlatten()
	{
		// Arrange
		Shape shape = Stroked();
		_canvas.RebuildBuckets();

		// Act
		shape.SetStrokeColor(Palette.Orange);

		// Assert
		shape.Dirty.ShouldBe(DirtyLevel.Tess, "Restyling must not invalidate the flattened contours");
	}

	[Test]
	public void SetArc_RaisesFlatten()
	{
		// Arrange
		Shape shape = _canvas.Build()
			.Arc(Vector2.Zero, 20f, 0f, Mathf.Pi)
			.Stroke(VectorStyles.GaugeArc)
			.Commit();
		_canvas.RebuildBuckets();

		// Act
		shape.SetArc(0f, Mathf.Pi * 0.5f);

		// Assert
		shape.Dirty.ShouldBe(DirtyLevel.Flatten);
	}

	[Test]
	public void SetTransform_RaisesOnlyConcat()
	{
		// Arrange
		Shape shape = Stroked();
		_canvas.RebuildBuckets();

		// Act
		shape.SetTransform(new Transform2D(Mathf.Pi * 0.5f, Vector2.Zero));

		// Assert
		shape.Dirty.ShouldBe(DirtyLevel.Concat, "A rigid move must not re-tessellate");
	}

	[Test]
	public void SetTransform_BakesIntoTheConcatButNotTheShapeBuffer()
	{
		// Arrange
		Shape shape = Stroked();
		_canvas.RebuildBuckets();
		Vector2 ownBefore = shape.Buffer.Points[0];
		int rebuildsBefore = shape.RebuildCount;
		var offset = new Transform2D(0f, new Vector2(100f, 0f));

		// Act
		shape.SetTransform(offset);
		_canvas.RebuildBuckets();

		// Assert
		shape.RebuildCount.ShouldBe(rebuildsBefore, "Rigid motion must not re-tessellate");
		shape.Buffer.Points[0].ShouldBe(ownBefore, "The shape's own buffer stays in local space");
		_canvas.StaticBucket.Concat.Points[0].ShouldBe(offset * ownBefore, "The concat carries the baked transform");
	}

	[Test]
	public void SetVisible_False_ExcludesFromConcat()
	{
		// Arrange
		Shape shape = Stroked();
		_canvas.RebuildBuckets();
		_canvas.StaticBucket.Concat.IsEmpty.ShouldBeFalse();

		// Act
		shape.SetVisible(false);
		_canvas.RebuildBuckets();

		// Assert
		_canvas.StaticBucket.Concat.IsEmpty.ShouldBeTrue();
		shape.Buffer.IsEmpty.ShouldBeFalse("Hiding keeps the triangles cached for a cheap unhide");
	}

	[Test]
	public void Rebuild_EmitsFillBeforeStroke()
	{
		// Arrange
		Shape shape = _canvas.Build()
			.Polygon(Square)
			.Fill(Palette.Blue)
			.Stroke(VectorStyles.EdgeLine with { Color = Palette.Orange })
			.Commit();

		// Act
		_canvas.RebuildBuckets();

		// Assert
		shape.Buffer.Colors[0].ShouldBe(Palette.Blue, "The fill must be laid down first for the stroke to cover its edge");
		DrawingTestHelpers.AssertWellFormed(shape.Buffer);
	}

	[Test]
	public void BareFill_SynthesizesAHairlineAtTheBoundary()
	{
		// Arrange
		Shape shape = _canvas.Build().Polygon(Square).Fill(Palette.Blue).Commit();

		// Act
		_canvas.RebuildBuckets();

		// Assert
		DrawingTestHelpers.AssertWellFormed(shape.Buffer);
		DrawingTestHelpers.IsCovered(shape.Buffer, new Vector2(20f, -0.4f))
			.ShouldBeTrue("A bare fill gets a hairline stroke so its edge is antialiased");
	}

	[Test]
	public void OutlinedFill_DoesNotSynthesizeAHairline()
	{
		// Arrange - triangle counts cannot tell the two apart: the synthesized hairline and an
		// explicit stroke share joins and fan step, so only their area differs. An outlined fill
		// must come to exactly its polygon plus its one stroke, with no second stroke stacked on.
		Shape strokeOnly = _canvas.Build().Polygon(Square).Stroke(VectorStyles.HairLine).Commit();
		Shape outlined = _canvas.Build()
			.Polygon(Square)
			.Fill(Palette.Blue)
			.Stroke(VectorStyles.HairLine)
			.Commit();

		// Act
		_canvas.RebuildBuckets();

		// Assert
		float polygonArea = DrawingTestHelpers.ShoelaceArea(Square);
		float expected = polygonArea + DrawingTestHelpers.TotalArea(strokeOnly.Buffer);

		DrawingTestHelpers.TotalArea(outlined.Buffer).ShouldBe(
			expected,
			0.01f,
			"An explicit stroke replaces the synthesized hairline rather than adding to it");
		DrawingTestHelpers.AssertWellFormed(outlined.Buffer);
	}

	[Test]
	public void Path3_WithTwoContours_TessellatesBothIntoOneShape()
	{
		// Arrange
		var set = new Contour3Set();
		set.AddPoint(new Vector3(-10f, 0f, 0f));
		set.AddPoint(new Vector3(-2f, 0f, 0f));
		set.AddContour(0, 2, false);
		set.AddPoint(new Vector3(2f, 0f, 0f));
		set.AddPoint(new Vector3(10f, 0f, 0f));
		set.AddContour(2, 2, false);

		Shape shape = _canvas.Build()
			.Path3(set, Projector.Isometric(1f))
			.Stroke(VectorStyles.Wireframe(Palette.Grid))
			.Commit();

		// Act
		_canvas.RebuildBuckets();

		// Assert
		shape.ContourCount.ShouldBe(2, "Disjoint edges live in one shape, not one shape each");
		DrawingTestHelpers.AssertWellFormed(shape.Buffer);
		_canvas.ShapeCount.ShouldBe(1);
	}

	[Test]
	public void DashedStroke_LeavesGapsBetweenDashes()
	{
		// Arrange - a triangle count cannot tell dashes apart from N overlapping full-length
		// strokes, so this asserts on coverage: ink where a dash is, none where a gap is.
		// Pattern is 8 on / 6 off from x = 0, so x in (8, 14) is the first gap.
		Shape dashed = _canvas.Build()
			.Polyline([new Vector2(0f, 0f), new Vector2(100f, 0f)])
			.Stroke(VectorStyles.DashedGuide(Palette.Blue))
			.Commit();

		// Act
		_canvas.RebuildBuckets();

		// Assert
		DrawingTestHelpers.AssertWellFormed(dashed.Buffer);
		DrawingTestHelpers.IsCovered(dashed.Buffer, new Vector2(4f, 0f)).ShouldBeTrue("The first dash should be inked");
		DrawingTestHelpers.IsCovered(dashed.Buffer, new Vector2(11f, 0f)).ShouldBeFalse("The first gap must be empty");
		DrawingTestHelpers.IsCovered(dashed.Buffer, new Vector2(18f, 0f)).ShouldBeTrue("The second dash should be inked");
	}

	[Test]
	public void DashedStroke_TotalInkIsLessThanSolid()
	{
		// Arrange
		Shape solid = _canvas.Build()
			.Polyline([new Vector2(0f, 0f), new Vector2(100f, 0f)])
			.Stroke(VectorStyles.DashedGuide(Palette.Blue) with { DashPattern = null })
			.Commit();
		Shape dashed = _canvas.Build()
			.Polyline([new Vector2(0f, 20f), new Vector2(100f, 20f)])
			.Stroke(VectorStyles.DashedGuide(Palette.Blue))
			.Commit();

		// Act
		_canvas.RebuildBuckets();

		// Assert
		DrawingTestHelpers.TotalArea(dashed.Buffer).ShouldBeLessThan(
			DrawingTestHelpers.TotalArea(solid.Buffer),
			"Dashes remove ink; more triangles must not mean more coverage");
	}

	[Test]
	public void StripedStroke_AlternatesBothColorsWithNoGaps()
	{
		// Arrange - the kerb: red/white stripes tiling the whole run.
		Shape shape = _canvas.Build()
			.Polyline([new Vector2(0f, 0f), new Vector2(100f, 0f)])
			.StripedStroke(20f, 10f, Palette.Red, Palette.White)
			.Commit();

		// Act
		_canvas.RebuildBuckets();

		// Assert
		DrawingTestHelpers.AssertWellFormed(shape.Buffer);
		HasColor(shape.Buffer, Palette.Red).ShouldBeTrue("Stripe colour A must appear");
		HasColor(shape.Buffer, Palette.White).ShouldBeTrue("Stripe colour B must appear; a single-colour run is not striped");

		// Striped means zero off-length, so every point along the run carries ink.
		for (float x = 2f; x < 98f; x += 4f)
		{
			DrawingTestHelpers.IsCovered(shape.Buffer, new Vector2(x, 0f))
				.ShouldBeTrue($"Stripes must tile without gaps, but x={x} was empty");
		}
	}

	private static bool HasColor(VertexBuffer buffer, Color color)
	{
		for (int i = 0; i < buffer.VertexCount; i++)
		{
			Color c = buffer.Colors[i];
			if (Mathf.IsEqualApprox(c.R, color.R) && Mathf.IsEqualApprox(c.G, color.G) && Mathf.IsEqualApprox(c.B, color.B))
			{
				return true;
			}
		}

		return false;
	}

	[Test]
	public void SetPoints_OnAnArc_WarnsAndLeavesGeometryAlone()
	{
		// Arrange
		Shape shape = _canvas.Build()
			.Arc(Vector2.Zero, 20f, 0f, Mathf.Pi)
			.Stroke(VectorStyles.GaugeArc)
			.Commit();
		_canvas.RebuildBuckets();
		int before = shape.Buffer.IndexCount;

		// Act
		shape.SetPoints(Square);
		_canvas.RebuildBuckets();

		// Assert
		shape.Buffer.IndexCount.ShouldBe(before, "A mismatched setter must be inert, not corrupting");
	}

	[Test]
	public void CommitMesh_PopulatesBufferFromTheSourceVerbatim()
	{
		// Arrange
		VertexBuffer source = MeshSource();

		// Act
		Shape shape = _canvas.CommitMesh(source);
		_canvas.RebuildBuckets();

		// Assert
		shape.Buffer.VertexCount.ShouldBe(3);
		shape.Buffer.IndexCount.ShouldBe(3);
		shape.Buffer.Colors[0].ShouldBe(Palette.Red);
		shape.Buffer.Colors[1].ShouldBe(Palette.White);
		DrawingTestHelpers.AssertWellFormed(shape.Buffer);
	}

	[Test]
	public void CommitMesh_IsImmuneToABucketWideFlattenInvalidation()
	{
		// Arrange - a PixelScale change marks every shape Flatten; a Mesh shape has no source
		// primitive to re-derive from, so its RebuildCount must never move and its content must
		// survive untouched.
		Shape shape = _canvas.CommitMesh(MeshSource());
		_canvas.RebuildBuckets();
		int rebuildsBefore = shape.RebuildCount;
		Vector2 pointBefore = shape.Buffer.Points[0];

		// Act
		_canvas.PixelScale = 2f;
		_canvas.RebuildBuckets();

		// Assert
		shape.RebuildCount.ShouldBe(rebuildsBefore, "A Mesh shape has no tessellation to redo");
		shape.Buffer.Points[0].ShouldBe(pointBefore, "A PixelScale change must not touch mesh content");
	}

	[Test]
	public void CommitMesh_SetTransformBakesIntoTheConcatLikeAnyOtherShape()
	{
		// Arrange
		Shape shape = _canvas.CommitMesh(MeshSource());
		_canvas.RebuildBuckets();
		Vector2 ownBefore = shape.Buffer.Points[0];

		// Act
		shape.SetTransform(new Transform2D(0f, new Vector2(100f, 0f)));
		_canvas.RebuildBuckets();

		// Assert
		shape.Buffer.Points[0].ShouldBe(ownBefore, "The shape's own buffer stays in local space");
		_canvas.StaticBucket.Concat.Points[0].ShouldBe(ownBefore + new Vector2(100f, 0f));
	}

	private static VertexBuffer MeshSource()
	{
		var source = new VertexBuffer();
		source.AddVertex(new Vector2(0f, 0f), Palette.Red);
		source.AddVertex(new Vector2(10f, 0f), Palette.White);
		source.AddVertex(new Vector2(0f, 10f), Palette.Red);
		source.AddTriangle(0, 1, 2);
		return source;
	}

	[Test]
	public void Rebuild_IsIdempotentWhenNothingIsDirty()
	{
		// Arrange
		Stroked();
		_canvas.RebuildBuckets();
		int vertices = _canvas.StaticBucket.Concat.VertexCount;
		int indices = _canvas.StaticBucket.Concat.IndexCount;

		// Act
		_canvas.RebuildBuckets();

		// Assert
		_canvas.StaticBucket.Concat.VertexCount.ShouldBe(vertices, "A clean rebuild must not double-append");
		_canvas.StaticBucket.Concat.IndexCount.ShouldBe(indices);
	}

	private Shape Stroked()
	{
		return _canvas.Build().Polygon(Square).Stroke(VectorStyles.EdgeLine).Commit();
	}
}
