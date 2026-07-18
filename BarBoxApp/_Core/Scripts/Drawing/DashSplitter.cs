using System;
using Godot;

namespace BarBox.Core.Drawing;

/// <summary>
/// Walks a path by arc length and carves it into dash or stripe sub-paths.
///
/// Pure function: it allocates nothing and owns nothing. The caller supplies a pooled
/// DashResult and then loops SegmentCount, feeding each range to
/// StrokeTessellator.TessellateSegment. Every sub-path is open — a dashed closed loop is a
/// set of open dashes.
/// </summary>
public static class DashSplitter
{
	private const float Epsilon = 1e-4f;
	private const int MaxCycleEntries = 64;

	/// <summary>
	/// Returns the number of segments written to output.
	/// </summary>
	/// <param name="t">Normalized arc length parallel to points; empty derives it from distance.</param>
	/// <param name="fitToLength">
	/// Scales the pattern so a whole number of periods spans a closed path, which is what stops
	/// a striped kerb ending in a stub at the seam. Note it silently adjusts the requested dash
	/// length by up to half a period.
	/// </param>
	public static int Split(
		ReadOnlySpan<Vector2> points,
		ReadOnlySpan<float> t,
		bool closed,
		ReadOnlySpan<float> pattern,
		float offset,
		DashMode mode,
		Color colorA,
		Color colorB,
		CapMode endCap,
		bool fitToLength,
		DashResult output)
	{
		output.Clear();

		int count = points.Length;
		if (count < 2 || output == null)
		{
			return 0;
		}

		if (closed && count < 3)
		{
			closed = false;
		}

		var walk = new PathWalk
		{
			Points = points,
			T = t,
			Count = count,
			Closed = closed,
		};

		walk.Total = walk.MeasureTotal();
		if (walk.Total <= Epsilon)
		{
			return 0;
		}

		Span<float> cycle = stackalloc float[MaxCycleEntries];
		int cycleLength = BuildCycle(pattern, mode, cycle, out float period);

		if (cycleLength == 0 || period <= Epsilon || period > walk.Total * 1e6f)
		{
			// No usable pattern, or a dash longer than the path: one solid run.
			EmitSpan(ref walk, 0f, walk.Total, colorA, mode == DashMode.Striped, endCap, output, wrappedStart: closed, wrappedEnd: closed);
			return output.SegmentCount;
		}

		if (fitToLength)
		{
			int periods = Mathf.Max(Mathf.RoundToInt(walk.Total / period), 1);
			float scale = walk.Total / (periods * period);
			for (int i = 0; i < cycleLength; i++)
			{
				cycle[i] *= scale;
			}

			period *= scale;
		}

		bool striped = mode == DashMode.Striped;

		// SVG dash-offset semantics: a positive offset slides the pattern backwards along the path.
		float phase = ((offset % period) + period) % period;
		float cursor = -phase;
		int index = 0;

		while (cursor < walk.Total - Epsilon)
		{
			float spanLength = cycle[index % cycleLength];
			float rawStart = cursor;
			float rawEnd = cursor + spanLength;
			cursor = rawEnd;

			bool isOn = striped || index % 2 == 0;
			index++;

			if (!isOn)
			{
				continue;
			}

			float start = Mathf.Max(rawStart, 0f);
			float end = Mathf.Min(rawEnd, walk.Total);
			if (end <= start + Epsilon)
			{
				continue;
			}

			Color color = striped && (index - 1) % 2 != 0 ? colorB : colorA;

			// A span the pattern clipped at either end of a closed path is one physical dash
			// crossing the seam, so both halves butt together instead of growing caps.
			bool wrappedStart = closed && rawStart < -Epsilon;
			bool wrappedEnd = closed && rawEnd > walk.Total + Epsilon;

			// Stripes abut everywhere, so only an open path's two free ends get a cap. A closed
			// kerb has none at all — including at the seam, where the first and last stripes meet.
			if (striped)
			{
				wrappedStart |= closed || start > Epsilon;
				wrappedEnd |= closed || end < walk.Total - Epsilon;
			}

			EmitSpan(ref walk, start, end, color, striped, endCap, output, wrappedStart, wrappedEnd);
		}

		return output.SegmentCount;
	}

	/// <summary>
	/// Resolves the on/off lengths for one period. Returns 0 when the pattern cannot produce dashes.
	/// </summary>
	private static int BuildCycle(ReadOnlySpan<float> pattern, DashMode mode, Span<float> cycle, out float period)
	{
		period = 0f;

		if (pattern.Length == 0)
		{
			return 0;
		}

		float sum = 0f;
		for (int i = 0; i < pattern.Length; i++)
		{
			if (!float.IsFinite(pattern[i]) || pattern[i] < 0f)
			{
				return 0;
			}

			sum += pattern[i];
		}

		if (sum <= Epsilon)
		{
			return 0;
		}

		if (mode == DashMode.Striped)
		{
			// Stripes have no gaps: one stripe length, alternating color by span parity.
			float stripe = pattern.Length == 1 ? pattern[0] : sum;
			if (stripe <= Epsilon)
			{
				return 0;
			}

			cycle[0] = stripe;
			cycle[1] = stripe;
			period = stripe * 2f;
			return 2;
		}

		// An odd-length pattern tiles doubled so on/off parity stays consistent, per SVG.
		int length = pattern.Length % 2 == 0 ? pattern.Length : pattern.Length * 2;
		if (length > MaxCycleEntries)
		{
			return 0;
		}

		for (int i = 0; i < length; i++)
		{
			cycle[i] = pattern[i % pattern.Length];
			period += cycle[i];
		}

		return length;
	}

	private static void EmitSpan(
		ref PathWalk walk,
		float start,
		float end,
		Color color,
		bool useColor,
		CapMode endCap,
		DashResult output,
		bool wrappedStart,
		bool wrappedEnd)
	{
		int first = output.PointCount;

		walk.Sample(start, out Vector2 startPoint, out float startT);
		output.AddPoint(startPoint, startT);

		// Every original vertex strictly inside the span keeps the path's shape.
		float travelled = 0f;
		for (int i = 0; i < walk.SegCount; i++)
		{
			float segLength = walk.SegmentLength(i);
			float vertexDistance = travelled + segLength;
			travelled = vertexDistance;

			if (vertexDistance <= start + Epsilon)
			{
				continue;
			}

			if (vertexDistance >= end - Epsilon)
			{
				break;
			}

			output.AddPoint(walk.Pt(i + 1), walk.Tt(i + 1));
		}

		walk.Sample(end, out Vector2 endPoint, out float endT);
		output.AddPoint(endPoint, endT);

		int pointCount = output.PointCount - first;
		if (pointCount < 2)
		{
			output.PointCount = first;
			return;
		}

		output.AddSegment(new DashSegment(
			first,
			pointCount,
			color,
			useColor,
			wrappedStart ? CapMode.Butt : endCap,
			wrappedEnd ? CapMode.Butt : endCap,
			!wrappedStart,
			!wrappedEnd));
	}
}
