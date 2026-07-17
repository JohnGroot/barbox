using System;
using Godot;

namespace BarBox.Core.Drawing;

/// <summary>
/// One dash or stripe: a range into DashResult's flat point array plus the per-end treatment.
///
/// Per-end cap and feather flags exist for striped strokes. Where two stripes abut, a feathered
/// end on each side would meet as two alpha ramps at 50%, drawing a visible seam line, and a
/// round cap there would bulge. Suppressing both at internal boundaries lets adjacent stripes
/// share exact vertex positions and read as one hard color edge, which is what a kerb is.
/// </summary>
public readonly struct DashSegment
{
	public readonly int Start;
	public readonly int Count;

	/// <summary>Only meaningful when UseColor is set.</summary>
	public readonly Color Color;

	/// <summary>When false the tessellator falls back to the style's Color or ColorStops.</summary>
	public readonly bool UseColor;

	public readonly CapMode StartCap;
	public readonly CapMode EndCap;
	public readonly bool FeatherStart;
	public readonly bool FeatherEnd;

	public DashSegment(
		int start,
		int count,
		Color color,
		bool useColor,
		CapMode startCap,
		CapMode endCap,
		bool featherStart,
		bool featherEnd)
	{
		Start = start;
		Count = count;
		Color = color;
		UseColor = useColor;
		StartCap = startCap;
		EndCap = endCap;
		FeatherStart = featherStart;
		FeatherEnd = featherEnd;
	}

	public static DashSegment Solid(int start, int count, CapMode cap)
	{
		return new DashSegment(start, count, default, false, cap, cap, true, true);
	}
}

/// <summary>
/// Pooled output for DashSplitter. Sub-paths are ranges into one flat point array rather than
/// separate arrays, so a dashed stroke still tessellates into a single shape buffer.
/// Callers own the instance; DashSplitter allocates nothing.
/// </summary>
public sealed class DashResult
{
	public Vector2[] Points;

	/// <summary>Normalized arc length inherited from the parent path, so gradients survive the gaps.</summary>
	public float[] T;

	public DashSegment[] Segments;
	public int PointCount;
	public int SegmentCount;

	public DashResult(int pointCapacity = 128, int segmentCapacity = 32)
	{
		Points = new Vector2[Mathf.Max(pointCapacity, 1)];
		T = new float[Mathf.Max(pointCapacity, 1)];
		Segments = new DashSegment[Mathf.Max(segmentCapacity, 1)];
	}

	public void Clear()
	{
		PointCount = 0;
		SegmentCount = 0;
	}

	public void EnsurePointCapacity(int total)
	{
		if (total <= Points.Length)
		{
			return;
		}

		int size = Points.Length;
		while (size < total)
		{
			size *= 2;
		}

		Array.Resize(ref Points, size);
		Array.Resize(ref T, size);
	}

	public void EnsureSegmentCapacity(int total)
	{
		if (total <= Segments.Length)
		{
			return;
		}

		int size = Segments.Length;
		while (size < total)
		{
			size *= 2;
		}

		Array.Resize(ref Segments, size);
	}

	public void AddPoint(Vector2 point, float t)
	{
		EnsurePointCapacity(PointCount + 1);
		Points[PointCount] = point;
		T[PointCount] = t;
		PointCount++;
	}

	public void AddSegment(in DashSegment segment)
	{
		EnsureSegmentCapacity(SegmentCount + 1);
		Segments[SegmentCount++] = segment;
	}
}
