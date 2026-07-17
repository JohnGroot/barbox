using BarBox.Core.Drawing;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

namespace BarBox.Tests.Unit.Drawing;

public class ShapeCanvasTests : TestClass
{
	private static readonly Vector2[] Square =
	[
		new Vector2(0f, 0f),
		new Vector2(40f, 0f),
		new Vector2(40f, 40f),
		new Vector2(0f, 40f),
	];

	private ShapeCanvas _canvas;

	public ShapeCanvasTests(Node testScene)
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
	public void DirtyingADynamicShape_LeavesTheStaticBucketAlone()
	{
		// Arrange - the whole point of the bucket split: per-frame dirt must not re-upload
		// static geometry, which is what would happen if dynamic dirt called QueueRedraw.
		Shape stat = _canvas.Build().Polygon(Square).Stroke(VectorStyles.EdgeLine).Commit();
		Shape dyn = _canvas.Build()
			.Polyline([new Vector2(0f, 60f), new Vector2(100f, 60f)])
			.Stroke(VectorStyles.DashedGuide(Palette.Blue))
			.Dynamic()
			.Commit();
		_canvas.RebuildBuckets();
		int staticRebuilds = stat.RebuildCount;

		// Act
		dyn.SetDashOffset(4f);

		// Assert
		_canvas.StaticBucket.ConcatDirty.ShouldBeFalse("Dynamic dirt must never dirty the static bucket");
		_canvas.DynamicBucket.ConcatDirty.ShouldBeTrue();

		_canvas.RebuildBuckets();
		stat.RebuildCount.ShouldBe(staticRebuilds, "The static shape must not re-tessellate for a dynamic change");
		dyn.RebuildCount.ShouldBe(2);
	}

	[Test]
	public void ConcatOrder_FollowsCommitOrderByDefault()
	{
		// Arrange
		Shape first = Commit(Palette.Blue);
		Shape second = Commit(Palette.Red);

		// Act
		_canvas.RebuildBuckets();

		// Assert
		_canvas.StaticBucket.Shapes[0].ShouldBeSameAs(first);
		_canvas.StaticBucket.Shapes[1].ShouldBeSameAs(second);
		_canvas.StaticBucket.Concat.Colors[0].ShouldBe(Palette.Blue, "Painter's order: first committed is laid down first");
	}

	[Test]
	public void SortKey_ReordersWithinTheBucket()
	{
		// Arrange
		Shape first = Commit(Palette.Blue);
		Shape second = Commit(Palette.Red);

		// Act
		first.SetSortKey(10);
		_canvas.RebuildBuckets();

		// Assert
		_canvas.StaticBucket.Shapes[0].ShouldBeSameAs(second, "A higher sort key draws later");
		_canvas.StaticBucket.Shapes[1].ShouldBeSameAs(first);
	}

	[Test]
	public void EqualSortKeys_PreserveCommitOrder()
	{
		// Arrange - List.Sort is unstable, so the comparison must fall back to commit sequence.
		Shape[] shapes = new Shape[8];
		for (int i = 0; i < shapes.Length; i++)
		{
			shapes[i] = Commit(Palette.Blue);
			shapes[i].SetSortKey(3);
		}

		// Act
		_canvas.RebuildBuckets();

		// Assert
		for (int i = 0; i < shapes.Length; i++)
		{
			_canvas.StaticBucket.Shapes[i].ShouldBeSameAs(shapes[i], "Ties must resolve to commit order deterministically");
		}
	}

	[Test]
	public void Remove_ExcludesTheShapeFromTheConcat()
	{
		// Arrange
		Shape shape = Commit(Palette.Blue);
		_canvas.RebuildBuckets();

		// Act
		_canvas.Remove(shape);
		_canvas.RebuildBuckets();

		// Assert
		_canvas.ShapeCount.ShouldBe(0);
		_canvas.StaticBucket.Concat.IsEmpty.ShouldBeTrue();
		shape.IsOrphaned.ShouldBeTrue();
	}

	[Test]
	public void Remove_IsIdempotent()
	{
		// Arrange
		Shape shape = Commit(Palette.Blue);
		_canvas.Remove(shape);

		// Act
		_canvas.Remove(shape);

		// Assert
		_canvas.ShapeCount.ShouldBe(0);
	}

	[Test]
	public void MutatingAnOrphanedShape_IsInert()
	{
		// Arrange
		Shape shape = Commit(Palette.Blue);
		_canvas.RebuildBuckets();
		_canvas.Remove(shape);

		// Act
		shape.SetStrokeColor(Palette.Red);

		// Assert
		_canvas.StaticBucket.Shapes.ShouldBeEmpty();
	}

	[Test]
	public void Clear_OrphansEveryShape()
	{
		// Arrange
		Shape stat = Commit(Palette.Blue);
		Shape dyn = _canvas.Build().Polygon(Square).Stroke(VectorStyles.EdgeLine).Dynamic().Commit();

		// Act
		_canvas.Clear();
		_canvas.RebuildBuckets();

		// Assert
		_canvas.ShapeCount.ShouldBe(0);
		stat.IsOrphaned.ShouldBeTrue();
		dyn.IsOrphaned.ShouldBeTrue();
		_canvas.StaticBucket.Concat.IsEmpty.ShouldBeTrue();
	}

	[Test]
	public void SettingPixelScale_DisablesAutoDerivationAndRebuilds()
	{
		// Arrange
		Shape shape = Commit(Palette.Blue);
		_canvas.RebuildBuckets();
		int before = shape.RebuildCount;

		// Act
		_canvas.PixelScale = 3f;

		// Assert
		_canvas.AutoPixelScale.ShouldBeFalse();
		shape.Dirty.ShouldBe(
			DirtyLevel.Flatten,
			"PixelScale feeds both the feather width and the flattening tolerance, so contours are stale too");

		_canvas.RebuildBuckets();
		shape.RebuildCount.ShouldBe(before + 1);
	}

	[Test]
	public void SettingPixelScale_ToTheSameValue_DoesNotRetessellate()
	{
		// Arrange
		Shape shape = Commit(Palette.Blue);
		_canvas.RebuildBuckets();
		int before = shape.RebuildCount;

		// Act
		_canvas.PixelScale = 1f;
		_canvas.RebuildBuckets();

		// Assert
		shape.RebuildCount.ShouldBe(before, "A no-op scale change must not invalidate anything");
	}

	[Test]
	public void SettingPixelScale_ToAnInvalidValue_IsIgnoredAndLeavesAutoOn()
	{
		// Arrange - a canvas the Setup has not already pinned, so AutoPixelScale is still on.
		var fresh = new ShapeCanvas();
		TestScene.AddChild(fresh);
		fresh.AutoPixelScale.ShouldBeTrue();
		float before = fresh.PixelScale;

		// Act
		fresh.PixelScale = 0f;

		// Assert
		fresh.PixelScale.ShouldBe(before, "A zero scale would make the tessellator reject every shape");
		fresh.AutoPixelScale.ShouldBeTrue(
			"A rejected value must not pin auto-derivation off, which would strand a stale scale");

		// Cleanup
		fresh.QueueFree();
	}

	[Test]
	public void RemovingADynamicShape_DoesNotDirtyTheStaticBucket()
	{
		// Arrange
		Commit(Palette.Blue);
		Shape dyn = _canvas.Build().Polygon(Square).Stroke(VectorStyles.EdgeLine).Dynamic().Commit();
		_canvas.RebuildBuckets();

		// Act
		_canvas.Remove(dyn);

		// Assert
		_canvas.StaticBucket.ConcatDirty.ShouldBeFalse(
			"Removing from a child bucket must not force the static geometry back onto the GPU");
		_canvas.DynamicBucket.ConcatDirty.ShouldBeTrue();
	}

	[Test]
	public void PixelScale_ScalesTheHairlineInCanvasUnits()
	{
		// Arrange - a bare fill's hairline is a fixed screen width, so a bigger PixelScale must
		// make it narrower in canvas units.
		Shape coarse = _canvas.Build().Polygon(Square).Fill(Palette.Blue).Commit();
		_canvas.RebuildBuckets();
		float coarseArea = DrawingTestHelpers.TotalArea(coarse.Buffer);

		// Act
		_canvas.PixelScale = 8f;
		_canvas.RebuildBuckets();

		// Assert
		DrawingTestHelpers.TotalArea(coarse.Buffer)
			.ShouldBeLessThan(coarseArea, "A higher pixel scale means a thinner hairline in canvas units");
	}

	[Test]
	public void CanvasItemCount_StaysSmall()
	{
		// Arrange
		for (int i = 0; i < 20; i++)
		{
			Commit(Palette.Blue);
		}

		_canvas.Build().Polygon(Square).Stroke(VectorStyles.EdgeLine).Dynamic().Commit();

		// Act
		_canvas.RebuildBuckets();

		// Assert
		_canvas.CanvasItemCount.ShouldBe(2, "Twenty static shapes plus a dynamic one is the node's item plus one child");
	}

	[Test]
	public void TriangleCount_SumsEveryBucket()
	{
		// Arrange
		Commit(Palette.Blue);
		_canvas.Build().Polygon(Square).Stroke(VectorStyles.EdgeLine).Dynamic().Commit();

		// Act
		_canvas.RebuildBuckets();

		// Assert
		int expected = (_canvas.StaticBucket.Concat.IndexCount + _canvas.DynamicBucket.Concat.IndexCount) / 3;
		_canvas.TriangleCount.ShouldBe(expected);
	}

	[Test]
	public void CommittingADynamicShapeBeforeEnteringTheTree_StillFlushes()
	{
		// Arrange - _Ready leaves processing idle for a purely static canvas, but a shape committed
		// before the node enters the tree has already queued a flush that must survive that.
		var early = new ShapeCanvas();
		early.PixelScale = 1f;
		early.Build().Polygon(Square).Stroke(VectorStyles.EdgeLine).Dynamic().Commit();

		// Act
		TestScene.AddChild(early);

		// Assert
		early.IsProcessing().ShouldBeTrue("A flush queued before _Ready must not be cancelled by it");

		// Cleanup
		early.QueueFree();
	}

	[Test]
	public void AStaticOnlyCanvas_DoesNotProcessEveryFrame()
	{
		// Arrange & Act
		Commit(Palette.Blue);

		// Assert
		_canvas.IsProcessing().ShouldBeFalse("A canvas with no child bucket has nothing to flush");
	}

	[Test]
	public void FreeChildItems_LeavesNoValidRids()
	{
		// Arrange
		_canvas.Build().Polygon(Square).Stroke(VectorStyles.EdgeLine).Dynamic().Commit();
		_canvas.DynamicBucket.Item.IsValid.ShouldBeTrue();

		// Act
		_canvas.FreeChildItemsForTest();

		// Assert
		_canvas.DynamicBucket.Item.IsValid.ShouldBeFalse("Predelete must release every child item RID");
	}

	[Test]
	public void SettingPixelScale_ReflattensCurvesAtTheNewTolerance()
	{
		// Arrange - flattening tolerance is TolerancePx / PixelScale, so a scale change invalidates
		// the contours themselves, not just the feather. Re-tessellating alone would leave a curve
		// at its old segment density and it would read as a polygon after a resize.
		Shape circle = _canvas.Build().Circle(Vector2.Zero, 50f).Stroke(VectorStyles.EdgeLine).Commit();
		_canvas.RebuildBuckets();
		int coarse = circle.Contours[0].Count;

		// Act
		_canvas.PixelScale = 8f;
		_canvas.RebuildBuckets();

		// Assert
		circle.Contours[0].Count.ShouldBeGreaterThan(
			coarse,
			"A finer pixel scale must re-flatten the curve, not just re-tessellate it");
	}

	[Test]
	public void CommitMesh_RegistersIntoTheStaticBucketAndFollowsSortKeyOrder()
	{
		// Arrange
		Shape polygonShape = Commit(Palette.Blue);
		var source = new VertexBuffer();
		source.AddVertex(new Vector2(0f, 0f), Palette.Red);
		source.AddVertex(new Vector2(10f, 0f), Palette.White);
		source.AddVertex(new Vector2(0f, 10f), Palette.Red);
		source.AddTriangle(0, 1, 2);

		// Act
		Shape meshShape = _canvas.CommitMesh(source, sortKey: -1);
		_canvas.RebuildBuckets();

		// Assert
		_canvas.StaticBucket.Shapes.ShouldContain(meshShape);
		_canvas.ShapeCount.ShouldBe(2);
		_canvas.StaticBucket.Shapes[0].ShouldBeSameAs(meshShape, "A lower sort key draws first, same as any other shape");
		_canvas.StaticBucket.Shapes[1].ShouldBeSameAs(polygonShape);
	}

	private Shape Commit(Color color)
	{
		return _canvas.Build()
			.Polygon(Square)
			.Stroke(VectorStyles.EdgeLine with { Color = color })
			.Commit();
	}
}
