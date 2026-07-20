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

	/// <summary>
	/// Pre-flattened via PathBuilder. Flatten() copies the builder's contours rather than
	/// dispatching through PathFlattener — there is no analytic source to re-derive them from,
	/// so unlike every other kind this one does not get finer on a canvas resize.
	/// </summary>
	Path,

	/// <summary>
	/// Pre-tessellated triangle soup supplied directly via SetMesh, bypassing Flatten/Tess
	/// entirely. Used for content — like a checkerboard fill — whose per-vertex color can't be
	/// expressed by a single FillStyle.
	/// </summary>
	Mesh,
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

	/// <summary>
	/// Geometry and triangle counts are still valid; only the baked vertex colors must be
	/// rewritten in place. Cheaper than Tess: it skips the tessellator's join/cap/triangulation
	/// work, though the bucket's re-concat walk still runs regardless of level (see ShapeBucket).
	/// </summary>
	Recolor = 2,

	/// <summary>Flattened contours are still valid; only the triangles must be rebuilt.</summary>
	Tess = 3,

	/// <summary>The source geometry changed; contours must be re-flattened first.</summary>
	Flatten = 4,
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

	/// <summary>Referenced, not copied — see SetPath.</summary>
	internal PathBuilder SourcePath;

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
	private FlatPath[] _trimScratch = [];

	/// <summary>Alpha baked into the fill/hairline range at the last full Tess. Recolor's ratio denominator.</summary>
	private float _bakedFillAlpha;

	/// <summary>Alpha baked into the stroke range at the last full Tess. Recolor's ratio denominator.</summary>
	private float _bakedStrokeAlpha;

	/// <summary>
	/// Vertex index boundary between the fill pass's output and everything after it (the bare-fill
	/// hairline or the real stroke) within Buffer. Correct whether or not HasFill is true.
	/// </summary>
	private int _fillVertexEnd;

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

	/// <summary>
	/// Rewrites the stroke color without re-tessellating (Recolor tier): the per-vertex alpha
	/// ratio baked at the last full Tess is preserved, so feather/skirt vertices stay
	/// transparent. Falls back to a full Tess rebuild for gradients, striped dashes, or a
	/// previously-zero baked alpha — see Shape.Recolor.
	/// </summary>
	public void SetStrokeColor(Color color)
	{
		if (Stroke.Color == color)
		{
			return;
		}

		Stroke.Color = color;
		MarkDirty(DirtyLevel.Recolor);
	}

	/// <summary>Fill counterpart to SetStrokeColor — same Recolor fast path and fallback rules.</summary>
	public void SetFillColor(Color color)
	{
		if (Fill.Color == color)
		{
			return;
		}

		Fill.Color = color;
		MarkDirty(DirtyLevel.Recolor);
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

	/// <summary>
	/// Fractional arc-length window of the flattened contour that gets stroked — the draw-on
	/// animation primitive (gauge sweeps, lap-ring reveals). Operates on the already-flattened
	/// contour, so this raises only Tess, never Flatten. TrimEnd == 0 resolves to 1 (see
	/// StrokeStyle.ResolveTrimEnd); represent "nothing drawn yet" with SetVisible(false) or a
	/// tiny epsilon end, not a literal 0. A range that collapses (TrimStart >= TrimEnd, after
	/// resolving) skips this shape's stroke for that pass and rate-limits a warning — it never
	/// throws, per the module's validation convention.
	/// </summary>
	public void SetTrim(float start, float end)
	{
		Stroke.TrimStart = start;
		Stroke.TrimEnd = end;
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

	/// <summary>
	/// The builder reference is kept only to know what to copy from on the next Flatten() pass —
	/// its contours are copied into this Shape's own buffers, not aliased (see FlattenPath).
	/// Mutating the builder afterward has no effect until SetPath (or another dirty-raiser) is
	/// called again.
	/// </summary>
	public void SetPath(PathBuilder path)
	{
		if (Kind != ShapeKind.Path)
		{
			PushWarning($"Shape.SetPath on a {Kind} shape has no effect.");
			return;
		}

		SourcePath = path;
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
	/// Replaces Buffer's contents wholesale with pre-tessellated triangles — the source IS the
	/// final content, so this marks only Concat, never Flatten/Tess (there is nothing to
	/// re-derive them from).
	/// </summary>
	public void SetMesh(VertexBuffer source)
	{
		if (Kind != ShapeKind.Mesh)
		{
			PushWarning($"Shape.SetMesh on a {Kind} shape has no effect.");
			return;
		}

		Buffer.Clear();
		Buffer.Append(source);
		MarkDirty(DirtyLevel.Concat);
	}

	/// <summary>
	/// Rigid transforms only. The transform is baked at concat time, after tessellation, so any
	/// scale in it scales the stroke width and the pixel-accurate feather with it. Content that
	/// moves continuously should move its Node2D instead; this is for occasional layout shifts.
	/// </summary>
	public void SetTransform(Transform2D transform)
	{
		if (Transform == transform)
		{
			return;
		}

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
		// A Mesh shape's Buffer IS its content — SetMesh already populated it eagerly, and there
		// is no source primitive to re-flatten or re-tessellate from. This makes it immune to a
		// bucket-wide MarkAll(Flatten) (e.g. a PixelScale change): correctly so, since the mesh
		// carries no feather and no pixel-scale-dependent tolerance to regenerate. RebuildCount
		// stays untouched too — it counts full tessellations, and none ever happens here.
		if (Kind == ShapeKind.Mesh)
		{
			return;
		}

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

		_fillVertexEnd = Buffer.VertexCount;

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
				StrokeContour(Contours[i], i, pixelScale, dashScratch);
			}
		}

		_bakedFillAlpha = Fill.Color.A;
		_bakedStrokeAlpha = Stroke.Color.A;
		RebuildCount++;
	}

	/// <summary>
	/// Rewrites already-baked vertex colors in place instead of re-tessellating — the fast path
	/// for SetFillColor/SetStrokeColor. Falls back to a full Rebuild whenever the ratio math
	/// below can't apply (a gradient, a per-span color override, or an unrecoverable zero-alpha
	/// baseline). Skipped entirely for a Mesh shape's Buffer (no fill/stroke ranges to speak of),
	/// same as Rebuild.
	/// </summary>
	internal void Recolor(float pixelScale, DashResult dashScratch)
	{
		if (Kind == ShapeKind.Mesh || NeedsFullRebuildForRecolor())
		{
			Rebuild(pixelScale, dashScratch);
			return;
		}

		if (HasFill)
		{
			RecolorRange(0, _fillVertexEnd, ref _bakedFillAlpha, Fill.Color);
		}

		if (HasStroke)
		{
			RecolorRange(_fillVertexEnd, Buffer.VertexCount, ref _bakedStrokeAlpha, Stroke.Color);
		}
		else if (HasFill)
		{
			// Bare-fill hairline: synthesized in Rebuild with Style.Color = Fill.Color, so it
			// recolors off the same baseline as the fill interior, not the (unused) Stroke field.
			RecolorRange(_fillVertexEnd, Buffer.VertexCount, ref _bakedFillAlpha, Fill.Color);
		}
	}

	private bool NeedsFullRebuildForRecolor()
	{
		if (HasStroke && Stroke.ColorStops != null && Stroke.ColorStops.Length >= 2)
		{
			// A gradient stroke isn't "one color" to begin with.
			return true;
		}

		if (HasStroke && Stroke.DashMode == DashMode.Striped)
		{
			// Striped segments resolve color from a per-span override, not Style.Color.
			return true;
		}

		if (HasFill && _bakedFillAlpha <= 0f)
		{
			// The ratio denominator would be 0/0.
			return true;
		}

		if (HasStroke && _bakedStrokeAlpha <= 0f)
		{
			return true;
		}

		return false;
	}

	/// <summary>
	/// A core vertex's baked alpha is Style.Color.A * alphaScale (alphaScale usually 1, reduced
	/// only by the hairline clamp on a sub-feather-width stroke); a skirt vertex's is always
	/// exactly 0. Dividing by the alpha baked at the last full Tess recovers that per-vertex
	/// factor exactly, so multiplying the new color's alpha by it reproduces the same feather
	/// ramp under the new color with no re-tessellation.
	/// </summary>
	private void RecolorRange(int startVertex, int endVertex, ref float bakedAlpha, Color newColor)
	{
		float oldAlpha = bakedAlpha;
		for (int i = startVertex; i < endVertex; i++)
		{
			float ratio = Buffer.Colors[i].A / oldAlpha;
			Buffer.Colors[i] = new Color(newColor.R, newColor.G, newColor.B, ratio * newColor.A);
		}

		bakedAlpha = newColor.A;
	}

	private void StrokeContour(FlatPath contour, int contourIndex, float pixelScale, DashResult dashScratch)
	{
		float trimStart = Stroke.TrimStart;
		float trimEnd = Stroke.ResolveTrimEnd();
		FlatPath source = contour;

		if (trimStart > 0f || trimEnd < 1f)
		{
			FlatPath scratch = GetTrimScratch(contourIndex);
			if (!PathTrimmer.Trim(contour, trimStart, trimEnd, scratch))
			{
				// No stroke geometry for this contour this pass; any fill is unaffected.
				return;
			}

			source = scratch;
		}

		if (Stroke.DashPattern == null || Stroke.DashPattern.Length == 0 || dashScratch == null)
		{
			StrokeTessellator.Tessellate(source.PointSpan, source.TSpan, source.Closed, Stroke, pixelScale, Buffer);
			return;
		}

		// Striped strokes must tile a closed path without a stub at the seam; on/off dashes are
		// left alone so an authored pattern keeps its exact lengths. A trimmed contour is always
		// open (see PathTrimmer), so this correctly degrades to false without extra branching.
		bool fitToLength = source.Closed && Stroke.DashMode == DashMode.Striped;

		int segments = DashSplitter.Split(
			source.PointSpan,
			source.TSpan,
			source.Closed,
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

		if (Kind == ShapeKind.Path)
		{
			FlattenPath(SourcePath);
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

	/// <summary>
	/// Copies the builder's contours into this Shape's own pooled buffers. Not zero-copy —
	/// unlike FlattenPath3's aliasing-free projection, aliasing PathBuilder's contours directly
	/// would let a later reuse of the same builder (Clear() + MoveTo() on its pooled FlatPath
	/// instances) silently corrupt this Shape's geometry with no dirty-mark. The copy only runs
	/// on DirtyLevel.Flatten (rare, not per-frame), so the O(n) cost is acceptable.
	/// </summary>
	private void FlattenPath(PathBuilder source)
	{
		int count = source?.ContourCount ?? 0;
		if (count == 0)
		{
			ContourCount = 0;
			return;
		}

		EnsureContours(count);
		ContourCount = count;

		FlatPath[] sourceContours = source.Contours;
		for (int i = 0; i < count; i++)
		{
			Contours[i].CopyFrom(sourceContours[i]);
		}
	}

	private ReadOnlySpan<Vector2> SourceSpan =>
		SourcePoints == null ? default : SourcePoints.AsSpan(0, SourcePointCount);

	private void EnsureContours(int count)
	{
		PooledArray.EnsureCapacity(ref Contours, count);

		for (int i = 0; i < count; i++)
		{
			Contours[i] ??= new FlatPath();
		}
	}

	/// <summary>
	/// Per-contour scratch for PathTrimmer's windowed output. Lazily allocated and grown in
	/// lockstep with Contours, so a shape that never calls SetTrim never pays for this.
	/// </summary>
	private FlatPath GetTrimScratch(int index)
	{
		PooledArray.EnsureCapacity(ref _trimScratch, index + 1);
		return _trimScratch[index] ??= new FlatPath();
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
