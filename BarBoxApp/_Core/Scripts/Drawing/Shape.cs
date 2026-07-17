using System;
using Godot;

namespace BarBox.Core.Drawing;

internal enum ShapeKind
{
	Polyline,
	Polygon,
	Circle,
	Arc,
	RoundedRect,
	CubicBezier,
	QuadBezier,
	Path3,
}

/// <summary>
/// How much of a shape's build pipeline a change invalidated. Monotonic: a higher level implies
/// every lower one, so MarkDirty only ever raises it and a rebuild clears it in one step.
/// </summary>
internal enum DirtyLevel
{
	None = 0,

	/// <summary>Triangles are still valid; the bucket just has to re-concatenate them.</summary>
	Concat = 1,

	/// <summary>Flattened contours are still valid; only the triangles must be rebuilt.</summary>
	Tess = 2,

	/// <summary>The source geometry changed; contours must be re-flattened first.</summary>
	Flatten = 3,
}

/// <summary>
/// One retained drawable: source geometry, style, and the triangles they tessellate to.
///
/// A shape keeps both its source primitive parameters and its flattened contours, which is what
/// lets a style-only change (the animation path) re-tessellate without re-flattening, and a
/// transform-only change skip both. That is the whole point of the DirtyLevel ladder.
///
/// Contours are plural throughout — a wireframe box is 12 disjoint edges and a single-primitive
/// shape is just ContourCount == 1, so there is one code path rather than two.
///
/// Never constructed directly: ShapeCanvas.Build() is the only entry point.
/// </summary>
public sealed class Shape
{
	internal readonly VertexBuffer Buffer = new(64, 128);

	internal ShapeCanvas Canvas;

	/// <summary>The bucket holding this shape, so dirty routing is a field read, not a search.</summary>
	internal ShapeBucket Bucket;

	internal ShapeKind Kind;

	internal Vector2 Center;
	internal float Radius;
	internal float StartRad;
	internal float EndRad;

	internal Rect2 Rect;
	internal float CornerRadius;

	internal Vector2 P0;
	internal Vector2 C0;
	internal Vector2 C1;
	internal Vector2 P1;

	internal Vector2[] SourcePoints;
	internal int SourcePointCount;
	internal bool SourceClosed;

	internal Contour3Set Source3;
	internal Projector Projector;

	/// <summary>Max chord deviation in screen pixels; divided by PixelScale to reach canvas units.</summary>
	internal float TolerancePx = PathFlattener.DefaultTolerance;

	internal FlatPath[] Contours = [];
	internal int ContourCount;

	internal StrokeStyle Stroke;
	internal bool HasStroke;
	internal FillStyle Fill;
	internal bool HasFill;

	internal Transform2D Transform = Transform2D.Identity;
	internal bool HasTransform;

	internal int SortKey;
	internal int CommitSeq;
	internal bool IsDynamic;
	internal ShaderMaterial Material;
	internal bool Visible = true;

	internal DirtyLevel Dirty = DirtyLevel.Flatten;

	/// <summary>Counts full tessellations. A test seam: transform-only changes must not raise it.</summary>
	internal int RebuildCount;

	private ColorStop[] _ownedStops;
	private float[] _ownedWidths;
	private float[] _ownedDashes;

	internal Shape()
	{
	}

	/// <summary>True once removed from its canvas; further mutations are ignored.</summary>
	public bool IsOrphaned => Canvas == null;

	/// <summary>
	/// Copies ColorStops/WidthProfile/DashPattern into shape-owned arrays. This is the single
	/// choke point for the array-ownership rule StrokeStyle documents — ShapeBuilder.Commit
	/// routes through here too, so a caller cannot smuggle a shared preset array into a retained
	/// shape by either path.
	/// </summary>
	public void SetStroke(StrokeStyle style)
	{
		Stroke = style;
		CopyStops(style.ColorStops);
		CopyWidths(style.WidthProfile);
		CopyDashes(style.DashPattern);
		HasStroke = true;
		MarkDirty(DirtyLevel.Tess);
	}

	public void ClearStroke()
	{
		HasStroke = false;
		MarkDirty(DirtyLevel.Tess);
	}

	public void SetFill(FillStyle style)
	{
		Fill = style;
		HasFill = true;
		MarkDirty(DirtyLevel.Tess);
	}

	public void SetFill(Color color)
	{
		SetFill(new FillStyle { Color = color, Material = Fill.Material });
	}

	public void ClearFill()
	{
		HasFill = false;
		MarkDirty(DirtyLevel.Tess);
	}

	public void SetStrokeColor(Color color)
	{
		Stroke.Color = color;
		MarkDirty(DirtyLevel.Tess);
	}

	public void SetStrokeWidth(float width)
	{
		Stroke.Width = width;
		MarkDirty(DirtyLevel.Tess);
	}

	public void SetDashOffset(float offset)
	{
		Stroke.DashOffset = offset;
		MarkDirty(DirtyLevel.Tess);
	}

	public void SetArc(float startRad, float endRad)
	{
		if (Kind != ShapeKind.Arc)
		{
			PushWarning($"Shape.SetArc on a {Kind} shape has no effect.");
			return;
		}

		StartRad = startRad;
		EndRad = endRad;
		MarkDirty(DirtyLevel.Flatten);
	}

	public void SetRadius(float radius)
	{
		if (Kind != ShapeKind.Arc && Kind != ShapeKind.Circle)
		{
			PushWarning($"Shape.SetRadius on a {Kind} shape has no effect.");
			return;
		}

		Radius = radius;
		MarkDirty(DirtyLevel.Flatten);
	}

	public void SetRect(Rect2 rect)
	{
		if (Kind != ShapeKind.RoundedRect)
		{
			PushWarning($"Shape.SetRect on a {Kind} shape has no effect.");
			return;
		}

		Rect = rect;
		MarkDirty(DirtyLevel.Flatten);
	}

	public void SetPoints(ReadOnlySpan<Vector2> points)
	{
		if (Kind != ShapeKind.Polyline && Kind != ShapeKind.Polygon)
		{
			PushWarning($"Shape.SetPoints on a {Kind} shape has no effect.");
			return;
		}

		StorePoints(points);
		MarkDirty(DirtyLevel.Flatten);
	}

	/// <summary>The set is referenced, not copied — the caller must not mutate it in place.</summary>
	public void SetPoints3(Contour3Set contours)
	{
		if (Kind != ShapeKind.Path3)
		{
			PushWarning($"Shape.SetPoints3 on a {Kind} shape has no effect.");
			return;
		}

		Source3 = contours;
		MarkDirty(DirtyLevel.Flatten);
	}

	public void SetProjector(in Projector projector)
	{
		if (Kind != ShapeKind.Path3)
		{
			PushWarning($"Shape.SetProjector on a {Kind} shape has no effect.");
			return;
		}

		Projector = projector;
		MarkDirty(DirtyLevel.Flatten);
	}

	/// <summary>
	/// Rigid transforms only. The transform is baked at concat time, after tessellation, so any
	/// scale in it scales the stroke width and the pixel-accurate feather with it. Content that
	/// moves continuously should move its Node2D instead; this is for occasional layout shifts.
	/// </summary>
	public void SetTransform(Transform2D transform)
	{
		Transform = transform;
		HasTransform = transform != Transform2D.Identity;

		Vector2 scale = transform.Scale;
		if (Mathf.Abs(scale.X - 1f) > 0.01f || Mathf.Abs(scale.Y - 1f) > 0.01f)
		{
			PushWarning("Shape.SetTransform scales the baked stroke width and feather; use a scaled Node2D instead.");
		}

		MarkDirty(DirtyLevel.Concat);
	}

	public void SetVisible(bool visible)
	{
		if (Visible == visible)
		{
			return;
		}

		Visible = visible;
		MarkDirty(DirtyLevel.Concat);
	}

	public void SetSortKey(int key)
	{
		if (SortKey == key)
		{
			return;
		}

		SortKey = key;
		Canvas?.MarkOrderDirty(this);
		MarkDirty(DirtyLevel.Concat);
	}

	internal void StorePoints(ReadOnlySpan<Vector2> points)
	{
		if (SourcePoints == null || SourcePoints.Length < points.Length)
		{
			SourcePoints = new Vector2[Mathf.Max(points.Length, 4)];
		}

		points.CopyTo(SourcePoints);
		SourcePointCount = points.Length;
	}

	/// <summary>
	/// The only tessellation entry point. Honours the M1 contract: a tessellator rejection
	/// leaves the buffer unchanged, and nothing here throws — this runs inside a draw pass.
	/// </summary>
	internal void Rebuild(float pixelScale, DashResult dashScratch)
	{
		if (Dirty >= DirtyLevel.Flatten)
		{
			Flatten(pixelScale);
		}

		Buffer.Clear();

		if (HasFill)
		{
			for (int i = 0; i < ContourCount; i++)
			{
				FlatPath contour = Contours[i];
				if (contour.Closed)
				{
					FillTessellator.Tessellate(contour.PointSpan, Fill.Color, Buffer);
				}
			}
		}

		// A bare fill has no feather of its own, so it gets a hairline stroke of the fill color.
		// At Width == the feather width the tessellator's hairline clamp collapses the core to the
		// band centre with the skirt at +/-f, which is exactly the symmetric alpha ramp the fill
		// edge lacks. It depends on PixelScale, so it cannot be resolved at Commit.
		if (HasFill && !HasStroke)
		{
			var hairline = new StrokeStyle
			{
				Width = StrokeStyle.DefaultFeatherPx / pixelScale,
				Color = Fill.Color,
			};

			for (int i = 0; i < ContourCount; i++)
			{
				StrokeTessellator.Tessellate(Contours[i].PointSpan, default, Contours[i].Closed, hairline, pixelScale, Buffer);
			}
		}

		if (HasStroke)
		{
			for (int i = 0; i < ContourCount; i++)
			{
				StrokeContour(Contours[i], pixelScale, dashScratch);
			}
		}

		RebuildCount++;
	}

	private void StrokeContour(FlatPath contour, float pixelScale, DashResult dashScratch)
	{
		if (Stroke.DashPattern == null || Stroke.DashPattern.Length == 0 || dashScratch == null)
		{
			StrokeTessellator.Tessellate(contour.PointSpan, contour.TSpan, contour.Closed, Stroke, pixelScale, Buffer);
			return;
		}

		// Striped strokes must tile a closed path without a stub at the seam; on/off dashes are
		// left alone so an authored pattern keeps its exact lengths.
		bool fitToLength = contour.Closed && Stroke.DashMode == DashMode.Striped;

		int segments = DashSplitter.Split(
			contour.PointSpan,
			contour.TSpan,
			contour.Closed,
			Stroke.DashPattern,
			Stroke.DashOffset,
			Stroke.DashMode,
			Stroke.Color,
			Stroke.DashColorB,
			Stroke.Cap,
			fitToLength,
			dashScratch);

		for (int i = 0; i < segments; i++)
		{
			DashSegment piece = dashScratch.Segments[i];

			// The tessellator reads a segment only for its colour and per-end cap/feather
			// overrides — it never slices the span itself, so passing the whole buffer would
			// redraw the entire contour once per dash and read as a solid line.
			StrokeTessellator.TessellateSegment(
				dashScratch.Points.AsSpan(piece.Start, piece.Count),
				dashScratch.T.AsSpan(piece.Start, piece.Count),
				false,
				Stroke,
				piece,
				pixelScale,
				Buffer);
		}
	}

	private void Flatten(float pixelScale)
	{
		// Tolerance is authored in screen pixels; PathFlattener wants canvas units, so a shape
		// under a 6x-scaled parent needs a 6x finer tolerance to stay smooth on screen.
		float tolerance = TolerancePx / pixelScale;

		if (Kind == ShapeKind.Path3)
		{
			FlattenPath3(Source3);
			return;
		}

		EnsureContours(1);
		ContourCount = 1;
		FlatPath path = Contours[0];

		switch (Kind)
		{
			case ShapeKind.Polyline:
				PathFlattener.Polyline(SourceSpan, false, path);
				break;
			case ShapeKind.Polygon:
				PathFlattener.Polyline(SourceSpan, SourceClosed, path);
				break;
			case ShapeKind.Circle:
				PathFlattener.Circle(Center, Radius, tolerance, path);
				break;
			case ShapeKind.Arc:
				PathFlattener.Arc(Center, Radius, StartRad, EndRad, tolerance, path);
				break;
			case ShapeKind.RoundedRect:
				PathFlattener.RoundedRect(Rect, CornerRadius, tolerance, path);
				break;
			case ShapeKind.CubicBezier:
				PathFlattener.CubicBezier(P0, C0, C1, P1, tolerance, path);
				break;
			case ShapeKind.QuadBezier:
				PathFlattener.QuadBezier(P0, C0, P1, tolerance, path);
				break;
		}
	}

	private void FlattenPath3(Contour3Set source)
	{
		if (source == null || source.ContourCount == 0)
		{
			ContourCount = 0;
			return;
		}

		EnsureContours(source.ContourCount);
		ContourCount = source.ContourCount;

		for (int i = 0; i < source.ContourCount; i++)
		{
			Projector.ProjectMany(source.Span(i), source.IsClosed(i), Contours[i]);
		}
	}

	private ReadOnlySpan<Vector2> SourceSpan =>
		SourcePoints == null ? default : SourcePoints.AsSpan(0, SourcePointCount);

	private void EnsureContours(int count)
	{
		if (Contours.Length < count)
		{
			int size = Mathf.Max(Contours.Length, 1);
			while (size < count)
			{
				size *= 2;
			}

			Array.Resize(ref Contours, size);
		}

		for (int i = 0; i < count; i++)
		{
			Contours[i] ??= new FlatPath();
		}
	}

	private void MarkDirty(DirtyLevel level)
	{
		Canvas?.MarkDirty(this, level);
	}

	private void CopyStops(ColorStop[] source)
	{
		if (source == null)
		{
			Stroke.ColorStops = null;
			return;
		}

		// Reuse when the length matches so re-styling an animating shape stays allocation-free.
		if (_ownedStops == null || _ownedStops.Length != source.Length)
		{
			_ownedStops = new ColorStop[source.Length];
		}

		Array.Copy(source, _ownedStops, source.Length);
		Stroke.ColorStops = _ownedStops;
	}

	private void CopyWidths(float[] source)
	{
		if (source == null)
		{
			Stroke.WidthProfile = null;
			return;
		}

		if (_ownedWidths == null || _ownedWidths.Length != source.Length)
		{
			_ownedWidths = new float[source.Length];
		}

		Array.Copy(source, _ownedWidths, source.Length);
		Stroke.WidthProfile = _ownedWidths;
	}

	private void CopyDashes(float[] source)
	{
		if (source == null)
		{
			Stroke.DashPattern = null;
			return;
		}

		if (_ownedDashes == null || _ownedDashes.Length != source.Length)
		{
			_ownedDashes = new float[source.Length];
		}

		Array.Copy(source, _ownedDashes, source.Length);
		Stroke.DashPattern = _ownedDashes;
	}

	private static void PushWarning(string message)
	{
		GD.PushWarning(message);
	}
}
