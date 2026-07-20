using Godot;

namespace BarBox.Core.Drawing;

/// <summary>
/// Fluent multi-segment path authoring: MoveTo/LineTo/CubicTo/QuadTo/ArcTo/Close, mirroring an
/// SVG-style path grammar without depending on SVG. Appends directly into pooled FlatPath
/// contours via PathFlattener's internal AppendArc/AppendCubic helpers. A Shape built from this
/// copies the result at Flatten() time (see Shape.SetPath) rather than aliasing it — there is no
/// analytic source to re-derive from, unlike Path3's projector.
///
/// Arc segments use center/radius/start-angle/end-angle, matching every other arc primitive in
/// this module (PathFlattener.Arc, ShapeBuilder.Arc, Shape.SetArc) rather than SVG's
/// endpoint+radii+flags form — that conversion belongs to whatever eventually consumes SVG
/// path data, not to this authoring tool.
///
/// Tolerance is in canvas units, not screen pixels: unlike a Shape, this type has no PixelScale
/// to derive from, so a shape built from it does not get finer on a canvas resize.
/// </summary>
public sealed class PathBuilder
{
	/// <summary>Max chord deviation for CubicTo/QuadTo/ArcTo, in canvas units (see class remarks).</summary>
	public float Tolerance = PathFlattener.DefaultTolerance;

	private FlatPath[] _contours = [];
	private int _contourCount;
	private bool _openContourDirty;
	private Vector2 _lastPoint;

	/// <summary>
	/// Exposes the internal array by reference for a caller to copy from (see Shape.FlattenPath).
	/// Do not retain this reference across further mutation of the builder — its FlatPath
	/// instances are pooled and reused in place by MoveTo.
	/// </summary>
	internal FlatPath[] Contours
	{
		get
		{
			FlushOpenContour();
			return _contours;
		}
	}

	internal int ContourCount
	{
		get
		{
			FlushOpenContour();
			return _contourCount;
		}
	}

	/// <summary>True once MoveTo has opened a contour that hasn't been sealed by Close() yet.</summary>
	private bool HasOpenContour => _contourCount > 0 && !_contours[_contourCount - 1].Closed;

	/// <summary>Starts a new disjoint contour. Finalizes whatever contour was previously open.</summary>
	public PathBuilder MoveTo(Vector2 point)
	{
		FlushOpenContour();

		EnsureContours(_contourCount + 1);
		FlatPath contour = _contours[_contourCount] ??= new FlatPath();
		contour.Clear();
		contour.Add(point);

		_contourCount++;
		_openContourDirty = true;
		_lastPoint = point;
		return this;
	}

	public PathBuilder LineTo(Vector2 point)
	{
		if (!RequireOpenContour(nameof(LineTo)))
		{
			return this;
		}

		CurrentContour.Add(point);
		_lastPoint = point;
		_openContourDirty = true;
		return this;
	}

	public PathBuilder CubicTo(Vector2 c0, Vector2 c1, Vector2 end)
	{
		if (!RequireOpenContour(nameof(CubicTo)))
		{
			return this;
		}

		PathFlattener.AppendCubic(_lastPoint, c0, c1, end, Tolerance, CurrentContour);
		_lastPoint = end;
		_openContourDirty = true;
		return this;
	}

	/// <summary>Degree-elevated to a cubic (exact equivalent), same as PathFlattener.QuadBezier.</summary>
	public PathBuilder QuadTo(Vector2 c, Vector2 end)
	{
		if (!RequireOpenContour(nameof(QuadTo)))
		{
			return this;
		}

		Vector2 p0 = _lastPoint;
		Vector2 c0 = p0 + ((2f / 3f) * (c - p0));
		Vector2 c1 = end + ((2f / 3f) * (c - end));
		return CubicTo(c0, c1, end);
	}

	/// <summary>
	/// Appends an arc. If the arc's own start point doesn't match the contour's current end, an
	/// implicit straight segment connects them (ordinary polyline behavior) — call MoveTo/LineTo
	/// to the arc's start first if that's not intended; a mismatch is warned but not rejected.
	/// </summary>
	public PathBuilder ArcTo(Vector2 center, float radius, float startRad, float endRad)
	{
		if (!RequireOpenContour(nameof(ArcTo)))
		{
			return this;
		}

		var arcStart = center + (new Vector2(Mathf.Cos(startRad), Mathf.Sin(startRad)) * radius);
		if (_lastPoint.DistanceSquaredTo(arcStart) > PolylineMath.Epsilon * PolylineMath.Epsilon)
		{
			GD.PushWarning(
				$"PathBuilder.ArcTo: arc start {arcStart} does not match the current point {_lastPoint}; " +
				"an implicit straight segment will connect them.");
		}

		PathFlattener.AppendArc(center, radius, startRad, endRad, Tolerance, CurrentContour);
		_lastPoint = CurrentContour.Points[CurrentContour.Count - 1];
		_openContourDirty = true;
		return this;
	}

	/// <summary>Seals the current contour as closed. Drops a trailing point that duplicates the
	/// start — Closed is a flag, never a repeated vertex, matching PathFlattener's convention.</summary>
	public PathBuilder Close()
	{
		if (!RequireOpenContour(nameof(Close)))
		{
			return this;
		}

		FlatPath contour = CurrentContour;
		PathFlattener.DropCoincidentClosingPoint(contour);
		contour.Closed = true;

		// Closed changes FinalizeT's total-length math even when no new point was appended, so
		// force a re-finalize regardless of the dirty flag.
		_openContourDirty = true;
		FlushOpenContour();
		return this;
	}

	/// <summary>Resets for reuse. Pooled FlatPath instances are kept and Clear()'d on the next MoveTo.</summary>
	public void Clear()
	{
		_contourCount = 0;
		_openContourDirty = false;
		_lastPoint = Vector2.Zero;
	}

	private FlatPath CurrentContour => _contours[_contourCount - 1];

	private bool RequireOpenContour(string caller)
	{
		if (HasOpenContour)
		{
			return true;
		}

		GD.PushWarning($"PathBuilder.{caller} called with no open contour (call MoveTo first); ignored.");
		return false;
	}

	/// <summary>
	/// FinalizeT is O(n) over its contour, so this must run once per contour close, not once per
	/// appended point — MoveTo/Close call it at well-defined boundaries; a bare read of
	/// Contours/ContourCount covers a contour left open with no trailing Close().
	/// </summary>
	private void FlushOpenContour()
	{
		if (!HasOpenContour || !_openContourDirty)
		{
			return;
		}

		CurrentContour.FinalizeT();
		_openContourDirty = false;
	}

	private void EnsureContours(int count)
	{
		PooledArray.EnsureCapacity(ref _contours, count);
	}
}
