using Godot;

namespace BarBox.Core.Drawing;

/// <summary>
/// Windows a flattened contour to a fractional arc-length range — the draw-on animation
/// primitive (gauge sweeps, lap-ring reveals). Pure function, no allocation: the caller supplies
/// a pooled FlatPath and this fills it.
/// </summary>
internal static class PathTrimmer
{
	private const float Epsilon = 1e-4f;
	private const int MaxWarnings = 8;

	private static int _warningCount;

	/// <summary>
	/// Windows [trimStart, trimEnd] (fractions of source's arc length) of source into output as
	/// an open sub-path. T values are copied verbatim from source, never renormalized — a
	/// trimmed stroke's gradient stays anchored to the untrimmed path rather than restretching
	/// into the visible sliver. Returns false (output left empty) on a degenerate range or a
	/// source too short to trim; callers must not tessellate a stroke for a false return.
	/// </summary>
	internal static bool Trim(FlatPath source, float trimStart, float trimEnd, FlatPath output)
	{
		output.Clear();

		if (source == null || output == null || source.Count < 2)
		{
			return false;
		}

		if (!float.IsFinite(trimStart) || !float.IsFinite(trimEnd) || trimStart >= trimEnd)
		{
			Warn($"PathTrimmer: TrimStart ({trimStart}) must be less than TrimEnd ({trimEnd}); stroke skipped.");
			return false;
		}

		trimStart = Mathf.Clamp(trimStart, 0f, 1f);
		trimEnd = Mathf.Clamp(trimEnd, 0f, 1f);
		if (trimStart >= trimEnd)
		{
			Warn("PathTrimmer: trim range collapsed to zero length after clamping to [0,1]; stroke skipped.");
			return false;
		}

		var walk = new PathWalk
		{
			Points = source.PointSpan,
			T = source.TSpan,
			Count = source.Count,
			Closed = source.Closed,
		};
		walk.Total = walk.MeasureTotal();
		if (walk.Total <= Epsilon)
		{
			return false;
		}

		float start = trimStart * walk.Total;
		float end = trimEnd * walk.Total;

		walk.Sample(start, out Vector2 startPoint, out float startT);
		output.Add(startPoint, startT);

		float travelled = 0f;
		for (int i = 0; i < walk.SegCount; i++)
		{
			travelled += walk.SegmentLength(i);

			if (travelled <= start + Epsilon)
			{
				continue;
			}

			if (travelled >= end - Epsilon)
			{
				break;
			}

			output.Add(walk.Pt(i + 1), walk.Tt(i + 1));
		}

		walk.Sample(end, out Vector2 endPoint, out float endT);
		output.Add(endPoint, endT);

		if (output.Count < 2)
		{
			output.Clear();
			return false;
		}

		output.Closed = false;
		return true;
	}

	private static void Warn(string message)
	{
		// Rate-limited: this runs inside _Draw, where a per-frame invalid shape would otherwise
		// flood the log until the editor stalls.
		if (_warningCount >= MaxWarnings)
		{
			return;
		}

		_warningCount++;
		GD.PushWarning(message);
	}
}
