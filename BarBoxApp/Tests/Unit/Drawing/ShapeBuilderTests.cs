using BarBox.Core.Drawing;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

namespace BarBox.Tests.Unit.Drawing;

public class ShapeBuilderTests : TestClass
{
	private static readonly Vector2[] Square =
	[
		new Vector2(0f, 0f),
		new Vector2(40f, 0f),
		new Vector2(40f, 40f),
		new Vector2(0f, 40f),
	];

	private ShapeCanvas _canvas;

	public ShapeBuilderTests(Node testScene)
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
	public void Commit_CopiesTheDashPattern()
	{
		// Arrange
		float[] pattern = [8f, 6f];
		Shape shape = _canvas.Build()
			.Polygon(Square)
			.Stroke(new StrokeStyle { Width = 2f, Color = Palette.Blue, DashPattern = pattern })
			.Commit();

		// Act
		pattern[0] = 999f;

		// Assert
		shape.Stroke.DashPattern.ShouldNotBeSameAs(pattern, "Commit must copy, or a shared preset array would leak in");
		shape.Stroke.DashPattern[0].ShouldBe(8f, "Mutating the source array after Commit must not reach the shape");
	}

	[Test]
	public void Commit_CopiesTheColorStops()
	{
		// Arrange
		ColorStop[] stops = [new ColorStop(0f, Palette.Blue), new ColorStop(1f, Palette.Red)];
		Shape shape = _canvas.Build()
			.Polygon(Square)
			.Stroke(VectorStyles.GaugeArc with { ColorStops = stops })
			.Commit();

		// Act
		stops[0] = new ColorStop(0f, Palette.Green);

		// Assert
		shape.Stroke.ColorStops.ShouldNotBeSameAs(stops);
		shape.Stroke.ColorStops[0].Color.ShouldBe(Palette.Blue);
	}

	[Test]
	public void Commit_CopiesTheWidthProfile()
	{
		// Arrange
		float[] widths = [1f, 10f];
		Shape shape = _canvas.Build()
			.Polygon(Square)
			.Stroke(new StrokeStyle { Width = 4f, Color = Palette.Blue, WidthProfile = widths })
			.Commit();

		// Act
		widths[1] = 999f;

		// Assert
		shape.Stroke.WidthProfile.ShouldNotBeSameAs(widths);
		shape.Stroke.WidthProfile[1].ShouldBe(10f);
	}

	[Test]
	public void Commit_DoesNotCaptureAPresetsSharedArray()
	{
		// Arrange & Act
		Shape shape = _canvas.Build()
			.Polygon(Square)
			.Stroke(VectorStyles.DashedGuide(Palette.Blue))
			.Commit();

		// Assert
		shape.Stroke.DashPattern.ShouldNotBeSameAs(
			VectorStyles.GuidePattern,
			"A retained shape must never hold the preset's array");
	}

	[Test]
	public void Commit_WithoutGeometry_ReturnsNullAndRegistersNothing()
	{
		// Arrange & Act
		Shape shape = _canvas.Build().Stroke(VectorStyles.EdgeLine).Commit();

		// Assert
		shape.ShouldBeNull();
		_canvas.ShapeCount.ShouldBe(0);
	}

	[Test]
	public void Commit_WithoutStrokeOrFill_ReturnsNullAndRegistersNothing()
	{
		// Arrange & Act
		Shape shape = _canvas.Build().Polygon(Square).Commit();

		// Assert
		shape.ShouldBeNull();
		_canvas.ShapeCount.ShouldBe(0);
	}

	[Test]
	public void Commit_PathBuilderPopulatedAfterPathCall_StillCommits()
	{
		// Arrange - .Path() is called before the builder is finished being populated; _hasGeometry
		// must not be frozen false/partial at that earlier moment (a mutable-reference footgun the
		// other geometry methods don't have, since they take their geometry by value)
		var pathBuilder = new PathBuilder();
		ShapeBuilder builder = _canvas.Build().Path(pathBuilder);
		pathBuilder.MoveTo(new Vector2(0f, 0f)).LineTo(new Vector2(10f, 0f));

		// Act
		Shape shape = builder.Stroke(VectorStyles.EdgeLine).Commit();
		_canvas.RebuildBuckets();

		// Assert
		shape.ShouldNotBeNull();
		shape.ContourCount.ShouldBe(1);
	}

	[Test]
	public void Commit_PathBuilderNeverPopulated_ReturnsNullAndRegistersNothing()
	{
		// Arrange
		var pathBuilder = new PathBuilder();

		// Act
		Shape shape = _canvas.Build().Path(pathBuilder).Stroke(VectorStyles.EdgeLine).Commit();

		// Assert
		shape.ShouldBeNull();
		_canvas.ShapeCount.ShouldBe(0);
	}

	[Test]
	public void Build_DoesNotInheritThePreviousChainsStyle()
	{
		// Arrange
		_canvas.Build()
			.Polygon(Square)
			.Stroke(VectorStyles.EdgeLine)
			.Fill(Palette.Red)
			.Dynamic()
			.SortKey(7)
			.Commit();

		// Act
		Shape second = _canvas.Build()
			.Polygon(Square)
			.Stroke(VectorStyles.HairLine)
			.Commit();

		// Assert
		second.HasFill.ShouldBeFalse("The pooled builder must not carry the previous fill over");
		second.IsDynamic.ShouldBeFalse();
		second.SortKey.ShouldBe(0);
		second.Stroke.Color.ShouldBe(Palette.EdgeGray);
	}

	[Test]
	public void Build_DoesNotInheritThePreviousChainsGeometry()
	{
		// Arrange
		_canvas.Build().Circle(new Vector2(5f, 5f), 30f).Stroke(VectorStyles.EdgeLine).Commit();

		// Act
		Shape second = _canvas.Build()
			.Polyline([new Vector2(0f, 0f), new Vector2(10f, 0f)])
			.Stroke(VectorStyles.EdgeLine)
			.Commit();

		// Assert
		second.Kind.ShouldBe(ShapeKind.Polyline);
		second.Radius.ShouldBe(0f, "A stale radius from the previous chain would silently change geometry");
		second.SourcePointCount.ShouldBe(2);
	}

	[Test]
	public void Dynamic_RoutesToTheChildBucket()
	{
		// Arrange & Act
		Shape shape = _canvas.Build()
			.Polygon(Square)
			.Stroke(VectorStyles.EdgeLine)
			.Dynamic()
			.Commit();

		// Assert
		shape.Bucket.ShouldBeSameAs(_canvas.DynamicBucket);
		shape.Bucket.IsChild.ShouldBeTrue("A dynamic bucket needs its own canvas item to be flushed separately");
		_canvas.StaticBucket.Shapes.ShouldBeEmpty();
	}

	[Test]
	public void WithMaterial_SharesOneBucketPerMaterialInstance()
	{
		// Arrange
		var shared = new ShaderMaterial();
		var other = new ShaderMaterial();

		// Act
		_canvas.Build().Polygon(Square).Fill(Palette.Blue).WithMaterial(shared).Commit();
		_canvas.Build().Polygon(Square).Fill(Palette.Red).WithMaterial(shared).Commit();
		_canvas.Build().Polygon(Square).Fill(Palette.Green).WithMaterial(other).Commit();

		// Assert
		_canvas.MaterialBuckets.Count.ShouldBe(2, "Shapes sharing a material batch together");
		_canvas.MaterialBuckets[0].Shapes.Count.ShouldBe(2);
		_canvas.MaterialBuckets[1].Shapes.Count.ShouldBe(1);
	}

	[Test]
	public void StripedStroke_ProducesAStripedDash()
	{
		// Arrange & Act
		Shape shape = _canvas.Build()
			.Polyline([new Vector2(0f, 0f), new Vector2(100f, 0f)])
			.StripedStroke(50f, 10f, Palette.Red, Palette.White)
			.Commit();

		// Assert
		shape.Stroke.DashMode.ShouldBe(DashMode.Striped);
		shape.Stroke.Color.ShouldBe(Palette.Red);
		shape.Stroke.DashColorB.ShouldBe(Palette.White);
		shape.Stroke.DashPattern.ShouldBe([10f]);
	}

	[Test]
	public void Rect_IsARoundedRectWithoutCorners()
	{
		// Arrange & Act
		Shape shape = _canvas.Build()
			.Rect(new Rect2(0f, 0f, 30f, 20f))
			.Fill(Palette.Panel)
			.Commit();
		_canvas.RebuildBuckets();

		// Assert
		shape.Kind.ShouldBe(ShapeKind.RoundedRect);
		shape.CornerRadius.ShouldBe(0f);
		shape.Contours[0].Count.ShouldBe(4, "A square-cornered rect flattens to exactly its four corners");
	}

	[Test]
	public void Hidden_CommitsInvisible()
	{
		// Arrange & Act
		Shape shape = _canvas.Build()
			.Polygon(Square)
			.Stroke(VectorStyles.EdgeLine)
			.Hidden()
			.Commit();
		_canvas.RebuildBuckets();

		// Assert
		shape.Visible.ShouldBeFalse();
		_canvas.StaticBucket.Concat.IsEmpty.ShouldBeTrue();
	}
}
