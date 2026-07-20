using System;
using Godot;

namespace BarBox.Core.Drawing;

/// <summary>
/// Turns analytic primitives into pooled polylines. Tolerance is the maximum chord deviation
/// in canvas units — callers working in pixels divide by the canvas PixelScale first.
///
/// Curve2D.Tessellate would do most of this, but it allocates and cannot target a pooled
/// buffer, which is the whole point of FlatPath.
/// </summary>
public static class PathFlattener
{
	public const float DefaultTolerance = 0.25f;

	private const int MinArcSegments = 1;
	private const int MaxArcSegments = 512;
	private const int MaxBezierDepth = 16;

	public static void Polyline(ReadOnlySpan<Vector2> points, bool closed, FlatPath output)
	{
		output.Clear();
		output.EnsureCapacity(points.Length);
		for (int i = 0; i < points.Length; i++)
		{
			output.Add(points[i]);
		}

		// Closed is a flag here, so an author-supplied closing duplicate would otherwise
		// survive as a zero-length segment across the seam.
		if (closed)
		{
			DropCoincidentClosingPoint(output);
		}

		output.Closed = closed;
		output.FinalizeT();
	}

	/// <summary>
	/// Segments needed to keep an arc's chord sagitta within tolerance. Exposed so the stroke
	/// tessellator can size round joins and caps against the same budget.
	/// </summary>
	public static int ArcSegmentCount(float radius, float sweepRad, float tolerance)
	{
		float sweep = Mathf.Abs(sweepRad);
		if (radius <= 0f || sweep <= 0f)
		{
			return MinArcSegments;
		}

		// Tolerance at or beyond the radius would make the acos argument leave [-1, 1].
		float ratio = Mathf.Clamp(1f - (tolerance / radius), -1f, 1f);
		float maxStep = 2f * Mathf.Acos(ratio);
		if (maxStep <= 0f || !float.IsFinite(maxStep))
		{
			return MaxArcSegments;
		}

		int count = Mathf.CeilToInt(sweep / maxStep);
		return Mathf.Clamp(count, MinArcSegments, MaxArcSegments);
	}

	public static void Arc(
		Vector2 center,
		float radius,
		float startRad,
		float endRad,
		float tolerance,
		FlatPath output)
	{
		output.Clear();
		AppendArc(center, radius, startRad, endRad, tolerance, output);
		output.Closed = false;
		output.FinalizeT();
	}

	public static void Circle(Vector2 center, float radius, float tolerance, FlatPath output)
	{
		output.Clear();

		int segments = Mathf.Max(ArcSegmentCount(radius, Mathf.Tau, tolerance), 3);
		output.EnsureCapacity(segments);

		// One point per segment, not segments + 1: the closing point is implied by Closed.
		for (int i = 0; i < segments; i++)
		{
			float angle = Mathf.Tau * i / segments;
			output.Add(center + (new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius));
		}

		output.Closed = true;
		output.FinalizeT();
	}

	public static void CubicBezier(
		Vector2 p0,
		Vector2 c0,
		Vector2 c1,
		Vector2 p1,
		float tolerance,
		FlatPath output)
	{
		output.Clear();
		output.Add(p0);
		AppendCubic(p0, c0, c1, p1, tolerance, output);
		output.Closed = false;
		output.FinalizeT();
	}

	public static void QuadBezier(Vector2 p0, Vector2 c, Vector2 p1, float tolerance, FlatPath output)
	{
		// Degree elevation: an exact cubic equivalent, so there is only one subdivider to test.
		Vector2 c0 = p0 + ((2f / 3f) * (c - p0));
		Vector2 c1 = p1 + ((2f / 3f) * (c - p1));
		CubicBezier(p0, c0, c1, p1, tolerance, output);
	}

	public static void RoundedRect(Rect2 rect, float radius, float tolerance, FlatPath output)
	{
		output.Clear();

		float maxRadius = Mathf.Min(rect.Size.X, rect.Size.Y) * 0.5f;
		float r = Mathf.Clamp(radius, 0f, maxRadius);

		float left = rect.Position.X;
		float top = rect.Position.Y;
		float right = rect.Position.X + rect.Size.X;
		float bottom = rect.Position.Y + rect.Size.Y;

		if (r <= PolylineMath.Epsilon)
		{
			output.Add(new Vector2(left, top));
			output.Add(new Vector2(right, top));
			output.Add(new Vector2(right, bottom));
			output.Add(new Vector2(left, bottom));
			output.Closed = true;
			output.FinalizeT();
			return;
		}

		// Corner arcs in Y-down order (top-left, top-right, bottom-right, bottom-left). Each
		// arc's endpoints are the adjacent straight runs' endpoints, so continuity needs no
		// extra vertices — AppendArc skips a start point coincident with the previous end.
		AppendArc(new Vector2(left + r, top + r), r, Mathf.Pi, Mathf.Pi * 1.5f, tolerance, output);
		AppendArc(new Vector2(right - r, top + r), r, Mathf.Pi * 1.5f, Mathf.Tau, tolerance, output);
		AppendArc(new Vector2(right - r, bottom - r), r, 0f, Mathf.Pi * 0.5f, tolerance, output);
		AppendArc(new Vector2(left + r, bottom - r), r, Mathf.Pi * 0.5f, Mathf.Pi, tolerance, output);

		DropCoincidentClosingPoint(output);

		output.Closed = true;
		output.FinalizeT();
	}

	/// <summary>
	/// Drops a trailing point that duplicates the first — Closed is a flag, never a repeated
	/// vertex (see FlatPath's own doc comment). Shared by Polyline, RoundedRect, and
	/// PathBuilder.Close.
	/// </summary>
	internal static void DropCoincidentClosingPoint(FlatPath output)
	{
		if (output.Count >= 2 &&
			output.Points[output.Count - 1].DistanceSquaredTo(output.Points[0]) <= PolylineMath.Epsilon * PolylineMath.Epsilon)
		{
			output.Count--;
		}
	}

	/// <summary>Internal: reused by PathBuilder's ArcTo to append a segment mid-contour.</summary>
	internal static void AppendArc(
		Vector2 center,
		float radius,
		float startRad,
		float endRad,
		float tolerance,
		FlatPath output)
	{
		float sweep = endRad - startRad;
		int segments = ArcSegmentCount(radius, sweep, tolerance);
		output.EnsureCapacity(output.Count + segments + 1);

		for (int i = 0; i <= segments; i++)
		{
			// Angles derive from the endpoints rather than accumulating a step, so the last
			// point lands exactly on endRad regardless of segment count.
			float angle = startRad + (sweep * i / segments);
			Vector2 point = center + (new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);

			if (output.Count > 0 &&
				point.DistanceSquaredTo(output.Points[output.Count - 1]) <= PolylineMath.Epsilon * PolylineMath.Epsilon)
			{
				continue;
			}

			output.Add(point);
		}
	}

	/// <summary>
	/// Appends a cubic Bézier's subdivided points to output, including the final endpoint.
	/// Self-contained: the caller's contour must already end with p0 (the curve's start), and
	/// this adds everything after it — the interior subdivision plus `end` — so a caller (e.g.
	/// PathBuilder.CubicTo) doesn't need to know the interior/endpoint split SubdivideCubic itself
	/// uses, or seed its recursion depth.
	/// </summary>
	internal static void AppendCubic(Vector2 p0, Vector2 c0, Vector2 c1, Vector2 end, float tolerance, FlatPath output)
	{
		SubdivideCubic(p0, c0, c1, end, tolerance, 0, output);
		output.Add(end);
	}

	private static void SubdivideCubic(
		Vector2 p0,
		Vector2 c0,
		Vector2 c1,
		Vector2 p1,
		float tolerance,
		int depth,
		FlatPath output)
	{
		if (depth >= MaxBezierDepth || IsFlat(p0, c0, c1, p1, tolerance))
		{
			return;
		}

		// de Casteljau split at t = 0.5.
		Vector2 p01 = (p0 + c0) * 0.5f;
		Vector2 p12 = (c0 + c1) * 0.5f;
		Vector2 p23 = (c1 + p1) * 0.5f;
		Vector2 p012 = (p01 + p12) * 0.5f;
		Vector2 p123 = (p12 + p23) * 0.5f;
		Vector2 mid = (p012 + p123) * 0.5f;

		SubdivideCubic(p0, p01, p012, mid, tolerance, depth + 1, output);
		output.Add(mid);
		SubdivideCubic(mid, p123, p23, p1, tolerance, depth + 1, output);
	}

	private static bool IsFlat(Vector2 p0, Vector2 c0, Vector2 c1, Vector2 p1, float tolerance)
	{
		Vector2 chord = p1 - p0;
		float chordLengthSq = chord.LengthSquared();

		// A degenerate chord makes the perpendicular distance undefined; fall back to measuring
		// the control points against the shared endpoint so an all-points-equal curve terminates.
		if (chordLengthSq <= PolylineMath.Epsilon * PolylineMath.Epsilon)
		{
			return c0.DistanceSquaredTo(p0) <= tolerance * tolerance &&
				c1.DistanceSquaredTo(p0) <= tolerance * tolerance;
		}

		float cross0 = Mathf.Abs(((c0.X - p0.X) * chord.Y) - ((c0.Y - p0.Y) * chord.X));
		float cross1 = Mathf.Abs(((c1.X - p0.X) * chord.Y) - ((c1.Y - p0.Y) * chord.X));
		float maxCross = Mathf.Max(cross0, cross1);

		// cross / |chord| is the perpendicular distance; compare squared to avoid the sqrt.
		return maxCross * maxCross <= tolerance * tolerance * chordLengthSq;
	}
}
