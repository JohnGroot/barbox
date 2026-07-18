using System;
using Godot;

namespace BarBox.Core.Drawing;

/// <summary>
/// Walks a path by arc length: point-at-distance sampling and per-vertex T lookup, shared by
/// DashSplitter (dash/stripe span windowing) and PathTrimmer (trim-range windowing).
/// </summary>
internal ref struct PathWalk
{
	private const float Epsilon = 1e-4f;

	public ReadOnlySpan<Vector2> Points;
	public ReadOnlySpan<float> T;
	public int Count;
	public bool Closed;
	public float Total;

	public readonly int SegCount => Closed ? Count : Count - 1;

	public readonly Vector2 Pt(int i)
	{
		return Points[i % Count];
	}

	public readonly float Tt(int i)
	{
		if (T.IsEmpty)
		{
			return Total > 0f ? Mathf.Clamp(CumulativeAt(i) / Total, 0f, 1f) : 0f;
		}

		// The wrap-around index closes the loop at t = 1, which is where FinalizeT leaves
		// the remainder for the implied closing segment.
		return Closed && i >= Count ? 1f : T[i];
	}

	public readonly float SegmentLength(int i)
	{
		return Pt(i).DistanceTo(Pt(i + 1));
	}

	public readonly float MeasureTotal()
	{
		float total = 0f;
		for (int i = 0; i < SegCount; i++)
		{
			total += SegmentLength(i);
		}

		return total;
	}

	public readonly void Sample(float distance, out Vector2 point, out float t)
	{
		float target = Mathf.Clamp(distance, 0f, Total);
		float travelled = 0f;

		for (int i = 0; i < SegCount; i++)
		{
			float segLength = SegmentLength(i);
			if (segLength <= 0f)
			{
				continue;
			}

			if (travelled + segLength >= target - Epsilon)
			{
				float fraction = Mathf.Clamp((target - travelled) / segLength, 0f, 1f);
				point = Pt(i).Lerp(Pt(i + 1), fraction);
				t = Mathf.Lerp(Tt(i), Tt(i + 1), fraction);
				return;
			}

			travelled += segLength;
		}

		point = Pt(SegCount);
		t = Tt(SegCount);
	}

	private readonly float CumulativeAt(int i)
	{
		float total = 0f;
		for (int s = 0; s < i && s < SegCount; s++)
		{
			total += SegmentLength(s);
		}

		return total;
	}
}
