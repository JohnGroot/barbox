using System;
using BarBox.Core.Drawing;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

namespace BarBox.Tests.Unit.Drawing;

/// <summary>
/// Pins the module's central promise: once the pooled buffers have grown, re-tessellating a
/// dirty shape allocates nothing on the managed heap. Every other test here asserts what the
/// triangles look like; these assert what a rebuild costs.
///
/// GC.GetAllocatedBytesForCurrentThread counts bytes ever allocated on this thread, so unlike a
/// heap-size reading it cannot be masked by a collection landing mid-measurement. The flip side
/// is that it counts the harness too: each mutator delegate is built and warmed before the
/// measurement window, because constructing a closure inside it reads as 64 bytes of "drawing"
/// allocation that the drawing code never performed.
///
/// Warmup is not optional either — the first rebuilds grow VertexBuffer/FlatPath/DashResult to
/// their steady-state capacity and JIT the tessellator, and both legitimately allocate.
///
/// Scope: stroke paths only. A fill goes through Geometry2D.TriangulatePolygon, which allocates
/// an int[] on every rebuild by design (§1: "allocates, but only on rebuild"). Fills are for
/// static geometry; if one ever lands in a per-frame path, that is the thing to revisit.
/// </summary>
public class AllocationTests : TestClass
{
	private const int WarmupIterations = 32;
	private const int MeasuredIterations = 50;

	private static readonly Vector2[] Square =
	[
		new Vector2(0f, 0f),
		new Vector2(40f, 0f),
		new Vector2(40f, 40f),
		new Vector2(0f, 40f),
	];

	private ShapeCanvas _canvas;

	public AllocationTests(Node testScene)
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
	public void RestylingAStroke_AllocatesNothingInSteadyState()
	{
		// Arrange
		Shape shape = _canvas.Build().Polygon(Square).Stroke(VectorStyles.EdgeLine).Commit();
		Action<int> mutate = i => shape.SetStrokeColor(i % 2 == 0 ? Palette.Blue : Palette.Red);
		Cycle(mutate, WarmupIterations);

		// Act
		long allocated = Measure(mutate, MeasuredIterations);

		// Assert
		allocated.ShouldBe(0L, $"Re-tessellating a stroke must not allocate; {MeasuredIterations} rebuilds cost {allocated} bytes");
	}

	[Test]
	public void AnimatingADashOffset_AllocatesNothingInSteadyState()
	{
		// Arrange - the HUD/guide animation path, and the reason DashOffset is a plain field.
		// Warmup spans a full pattern cycle: a shifting offset changes how many dashes land on the
		// path, so the buffers only settle once the widest case has been seen.
		Shape shape = _canvas.Build()
			.Polyline([new Vector2(0f, 0f), new Vector2(200f, 0f)])
			.Stroke(VectorStyles.DashedGuide(Palette.Blue))
			.Commit();
		Action<int> mutate = i => shape.SetDashOffset(i * 0.37f);
		Cycle(mutate, WarmupIterations);

		// Act
		long allocated = Measure(mutate, MeasuredIterations);

		// Assert
		allocated.ShouldBe(0L, $"Animating a dash offset must not allocate; {MeasuredIterations} rebuilds cost {allocated} bytes");
	}

	[Test]
	public void FeedingNewPoints_AllocatesNothingInSteadyState()
	{
		// Arrange - M5's tire-trail path: same point count, new positions, every frame.
		var points = new Vector2[32];
		Shape shape = _canvas.Build().Polyline(Wave(points, 0f)).Stroke(VectorStyles.EdgeLine).Commit();
		Action<int> mutate = i => shape.SetPoints(Wave(points, i));
		Cycle(mutate, WarmupIterations);

		// Act
		long allocated = Measure(mutate, MeasuredIterations);

		// Assert
		allocated.ShouldBe(0L, $"Re-flattening a polyline must not allocate; {MeasuredIterations} rebuilds cost {allocated} bytes");
	}

	[Test]
	public void RecoloringAFill_AllocatesNothingInSteadyState()
	{
		// Arrange - the countdown-glow pulse path
		Shape shape = _canvas.Build().Polygon(Square).Fill(Palette.Blue).Commit();
		Action<int> mutate = i => shape.SetFillColor(i % 2 == 0 ? Palette.Blue : Palette.Red);
		Cycle(mutate, WarmupIterations);

		// Act
		long allocated = Measure(mutate, MeasuredIterations);

		// Assert
		allocated.ShouldBe(0L, $"Recoloring a fill must not allocate; {MeasuredIterations} rebuilds cost {allocated} bytes");
	}

	[Test]
	public void TrimmingAStroke_AllocatesNothingInSteadyState()
	{
		// Arrange - the draw-on animation path. Both warmup and measurement cycle the same i % 32
		// fraction set, so the widest trim range (and the per-contour scratch capacity it needs)
		// is already reached during warmup and never grows again during measurement.
		Shape shape = _canvas.Build().Polygon(Square).Stroke(VectorStyles.EdgeLine).Commit();
		Action<int> mutate = i => shape.SetTrim(0f, Mathf.Clamp((i % 32) / 32f, 0.02f, 1f));
		Cycle(mutate, WarmupIterations);

		// Act
		long allocated = Measure(mutate, MeasuredIterations);

		// Assert
		allocated.ShouldBe(0L, $"Animating a trim range must not allocate; {MeasuredIterations} rebuilds cost {allocated} bytes");
	}

	[Test]
	public void MovingAShapeRigidly_AllocatesNothingInSteadyState()
	{
		// Arrange
		Shape shape = _canvas.Build().Polygon(Square).Stroke(VectorStyles.EdgeLine).Commit();
		Action<int> mutate = i => shape.SetTransform(new Transform2D(0f, new Vector2(i, 0f)));
		Cycle(mutate, WarmupIterations);

		// Act
		long allocated = Measure(mutate, MeasuredIterations);

		// Assert
		allocated.ShouldBe(0L, $"Re-concatenating a moved shape must not allocate; {MeasuredIterations} rebuilds cost {allocated} bytes");
	}

	[Test]
	public void AWholeFrameOfDirt_AcrossBothBuckets_AllocatesNothing()
	{
		// Arrange - many shapes across the static and dynamic buckets, mutated together: the shape
		// of a real frame rather than one shape in isolation. This is the test that would catch a
		// per-frame allocation hiding in the bucket walk or the concat rather than in a tessellator.
		var stroked = new Shape[12];
		for (int i = 0; i < stroked.Length; i++)
		{
			stroked[i] = _canvas.Build()
				.Circle(new Vector2(i * 10f, 0f), 20f + i)
				.Stroke(VectorStyles.EdgeLine)
				.Commit();
		}

		Shape dynamic = _canvas.Build()
			.Polyline([new Vector2(0f, 50f), new Vector2(200f, 50f)])
			.Stroke(VectorStyles.DashedGuide(Palette.Orange))
			.Dynamic()
			.Commit();

		Action<int> mutate = i =>
		{
			foreach (Shape s in stroked)
			{
				s.SetStrokeColor(i % 2 == 0 ? Palette.Blue : Palette.Green);
			}

			dynamic.SetDashOffset(i * 0.37f);
		};
		Cycle(mutate, WarmupIterations);

		// Act
		long allocated = Measure(mutate, MeasuredIterations);

		// Assert
		allocated.ShouldBe(0L, $"A whole frame's worth of dirt must not allocate; {MeasuredIterations} rebuilds cost {allocated} bytes");
	}

	private static Vector2[] Wave(Vector2[] points, float phase)
	{
		for (int i = 0; i < points.Length; i++)
		{
			points[i] = new Vector2(i * 4f, Mathf.Sin((i * 0.3f) + phase) * 20f);
		}

		return points;
	}

	/// <summary>Runs the mutator without measuring, to grow the pools and JIT the path.</summary>
	private void Cycle(Action<int> mutate, int iterations)
	{
		for (int i = 0; i < iterations; i++)
		{
			mutate(i);
			_canvas.RebuildBuckets();
		}
	}

	/// <summary>
	/// Takes an already-constructed, already-warmed delegate: building one here would allocate a
	/// closure inside the very window being measured.
	/// </summary>
	private long Measure(Action<int> mutate, int iterations)
	{
		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int i = 0; i < iterations; i++)
		{
			mutate(i);
			_canvas.RebuildBuckets();
		}

		return GC.GetAllocatedBytesForCurrentThread() - before;
	}
}
