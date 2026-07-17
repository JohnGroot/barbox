using System;
using Godot;

namespace BarBox.Core.Drawing;

/// <summary>
/// Shared polyline primitives for the tessellators.
///
/// Winding convention, which the rest of the module depends on: `dir.Orthogonal()` is the
/// OUTWARD normal of a closed path whose SignedArea is positive (counter-clockwise in the
/// math convention — equivalently, what Geometry2D.IsPolygonClockwise reports as false).
/// Normalizing a closed path to that winding is what lets StrokeAlign.Outer mean "away from
/// the interior" rather than "whichever way the author happened to author it".
/// </summary>
internal static class PolylineMath
{
	/// <summary>Distance below which two points are treated as coincident.</summary>
	public const float Epsilon = 1e-4f;

	/// <summary>Turn angle below which a joint is treated as straight.</summary>
	public const float CollinearEpsilon = 1e-4f;

	public static bool AllFinite(ReadOnlySpan<Vector2> points)
	{
		for (int i = 0; i < points.Length; i++)
		{
			if (!float.IsFinite(points[i].X) || !float.IsFinite(points[i].Y))
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// Fills map with the indices of points that survive coincident-point removal and returns
	/// how many. Indirecting through a map rather than compacting into a new array keeps the
	/// caller's span (and its parallel T span) usable as the source of truth.
	/// </summary>
	public static int Dedupe(ReadOnlySpan<Vector2> points, bool closed, Span<int> map)
	{
		int count = 0;
		for (int i = 0; i < points.Length; i++)
		{
			if (count > 0 && points[i].DistanceSquaredTo(points[map[count - 1]]) <= Epsilon * Epsilon)
			{
				continue;
			}

			map[count++] = i;
		}

		// A closed path's last point coinciding with its first would put a zero-length segment
		// across the seam, where the tessellator expects a normal joint.
		if (closed && count >= 2 &&
			points[map[count - 1]].DistanceSquaredTo(points[map[0]]) <= Epsilon * Epsilon)
		{
			count--;
		}

		return count;
	}

	/// <summary>Shoelace area. Positive means Orthogonal() points outward. Sign is all that matters here.</summary>
	public static float SignedArea(ReadOnlySpan<Vector2> points, ReadOnlySpan<int> map, int count)
	{
		if (count < 3)
		{
			return 0f;
		}

		float sum = 0f;
		for (int i = 0; i < count; i++)
		{
			Vector2 a = points[map[i]];
			Vector2 b = points[map[(i + 1) % count]];
			sum += (a.X * b.Y) - (b.X * a.Y);
		}

		return sum * 0.5f;
	}

	public static void ReverseMap(Span<int> map, int count)
	{
		for (int i = 0; i < count / 2; i++)
		{
			(map[i], map[count - 1 - i]) = (map[count - 1 - i], map[i]);
		}
	}

	/// <summary>
	/// Signed angle from dirIn to dirOut in (-pi, pi]. Positive means the joint's outer side is
	/// the Orthogonal() side; negative means the opposite side.
	/// </summary>
	public static float SignedTurnAngle(Vector2 dirIn, Vector2 dirOut)
	{
		float cross = (dirIn.X * dirOut.Y) - (dirIn.Y * dirOut.X);
		float dot = dirIn.Dot(dirOut);
		return Mathf.Atan2(cross, dot);
	}

	public static float TotalLength(ReadOnlySpan<Vector2> points, ReadOnlySpan<int> map, int count, bool closed)
	{
		if (count < 2)
		{
			return 0f;
		}

		float total = 0f;
		for (int i = 1; i < count; i++)
		{
			total += points[map[i]].DistanceTo(points[map[i - 1]]);
		}

		if (closed && count >= 3)
		{
			total += points[map[0]].DistanceTo(points[map[count - 1]]);
		}

		return total;
	}

	/// <summary>Intersection of the infinite lines p1+t*d1 and p2+s*d2. False when near-parallel.</summary>
	public static bool LineIntersection(Vector2 p1, Vector2 d1, Vector2 p2, Vector2 d2, out Vector2 hit)
	{
		float denominator = (d1.X * d2.Y) - (d1.Y * d2.X);
		if (Mathf.Abs(denominator) < 1e-6f)
		{
			hit = Vector2.Zero;
			return false;
		}

		Vector2 delta = p2 - p1;
		float t = ((delta.X * d2.Y) - (delta.Y * d2.X)) / denominator;
		hit = p1 + (t * d1);
		return true;
	}

	/// <summary>Resamples a t-parameterized profile (entry i sits at t = i/(n-1)) at normalized t.</summary>
	public static float SampleProfile(float[] profile, float t, float fallback)
	{
		if (profile == null || profile.Length == 0)
		{
			return fallback;
		}

		if (profile.Length == 1)
		{
			return profile[0];
		}

		float scaled = Mathf.Clamp(t, 0f, 1f) * (profile.Length - 1);
		int index = (int)scaled;
		if (index >= profile.Length - 1)
		{
			return profile[profile.Length - 1];
		}

		return Mathf.Lerp(profile[index], profile[index + 1], scaled - index);
	}
}
