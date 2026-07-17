using System;
using System.Buffers;
using Godot;

namespace BarBox.Core.Drawing;

/// <summary>
/// Perpendicular offset of a polyline by a fixed distance — e.g. deriving a track's inner/outer
/// boundary from its centerline. StrokeAlign cannot do this: it offsets a stroke's own two edges
/// by that stroke's own Width (see StrokeTessellator.ComputeGeometry), not an arbitrary distance
/// from the input points. Per-vertex normal offset with miter joining: a simple, good-enough
/// offset for smooth loops, not full self-intersection-safe polygon offsetting.
/// </summary>
public static class PolylineOffset
{
	/// <summary>Matches StrokeStyle.DefaultMiterLimit: past this ratio a corner clamps rather than spikes.</summary>
	private const float MaxMiterRatio = 4f;

	private const int StackallocLimit = 256;

	public static void Offset(ReadOnlySpan<Vector2> points, bool closed, float distance, FlatPath output)
	{
		output.Clear();

		if (points.Length < 2 || !PolylineMath.AllFinite(points))
		{
			return;
		}

		int[] rentedMap = null;

		try
		{
			Span<int> map = points.Length <= StackallocLimit
				? stackalloc int[points.Length]
				: (rentedMap = ArrayPool<int>.Shared.Rent(points.Length)).AsSpan(0, points.Length);

			int count = PolylineMath.Dedupe(points, closed, map);
			if (count < 2)
			{
				return;
			}

			if (closed && count < 3)
			{
				closed = false;
			}

			output.EnsureCapacity(count);
			for (int i = 0; i < count; i++)
			{
				output.Add(OffsetVertex(points, map, count, closed, i, distance));
			}

			output.Closed = closed;
			output.FinalizeT();
		}
		finally
		{
			if (rentedMap != null)
			{
				ArrayPool<int>.Shared.Return(rentedMap);
			}
		}
	}

	private static Vector2 OffsetVertex(
		ReadOnlySpan<Vector2> points,
		ReadOnlySpan<int> map,
		int count,
		bool closed,
		int i,
		float distance)
	{
		Vector2 p = points[map[i]];

		if (!closed && i == 0)
		{
			return p + ((points[map[1]] - p).Normalized().Orthogonal() * distance);
		}

		if (!closed && i == count - 1)
		{
			return p + ((p - points[map[i - 1]]).Normalized().Orthogonal() * distance);
		}

		int prev = (i - 1 + count) % count;
		int next = (i + 1) % count;

		Vector2 dirIn = (p - points[map[prev]]).Normalized();
		Vector2 dirOut = (points[map[next]] - p).Normalized();
		Vector2 nIn = dirIn.Orthogonal();
		Vector2 nOut = dirOut.Orthogonal();

		float theta = PolylineMath.SignedTurnAngle(dirIn, dirOut);
		if (Mathf.Abs(theta) < PolylineMath.CollinearEpsilon)
		{
			return p + (nIn * distance);
		}

		Vector2 fallback = p + (nIn * distance);
		if (!PolylineMath.LineIntersection(p + (nIn * distance), dirIn, p + (nOut * distance), dirOut, out Vector2 hit))
		{
			return fallback;
		}

		// Clamping rather than splitting into two offset points keeps every vertex to one output
		// point, at the cost of a bounded rounding-off on the inside of sharp turns — the same
		// trade-off StrokeTessellator.InnerPoint makes for its own inner joints.
		Vector2 delta = hit - p;
		float travel = delta.Length();
		float maxTravel = Mathf.Abs(distance) * MaxMiterRatio;
		if (travel > maxTravel && travel > 0f)
		{
			return p + (delta / travel * maxTravel);
		}

		return hit;
	}
}
