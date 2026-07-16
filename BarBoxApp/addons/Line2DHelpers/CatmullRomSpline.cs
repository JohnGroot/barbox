#if TOOLS
using System.Collections.Generic;
using Godot;

/// <summary>
/// Utility class for Catmull-Rom spline interpolation
/// </summary>
public static class CatmullRomSpline
{
	/// <summary>
	/// Interpolate a point on the Catmull-Rom spline at parameter t (0 to 1)
	/// between points p1 and p2, using p0 and p3 as tangent control points.
	/// </summary>
	/// <param name="p0">Control point before the segment start</param>
	/// <param name="p1">Segment start point</param>
	/// <param name="p2">Segment end point</param>
	/// <param name="p3">Control point after the segment end</param>
	/// <param name="t">Interpolation parameter (0 to 1)</param>
	/// <returns>Interpolated point on the spline</returns>
	public static Vector2 Interpolate(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
	{
		float t2 = t * t;
		float t3 = t2 * t;

		return 0.5f * (
			(2.0f * p1) +
			((-p0 + p2) * t) +
			(((2.0f * p0) - (5.0f * p1) + (4.0f * p2) - p3) * t2) +
			((-p0 + (3.0f * p1) - (3.0f * p2) + p3) * t3));
	}

	/// <summary>
	/// Generate smoothed points between startIndex and endIndex on a Line2D using Catmull-Rom interpolation.
	/// </summary>
	/// <param name="originalPoints">The original point array</param>
	/// <param name="startIndex">Index of the first point in the range to smooth</param>
	/// <param name="endIndex">Index of the last point in the range to smooth</param>
	/// <param name="pointsPerUnit">Number of interpolated points per unit of distance</param>
	/// <param name="bulgeFactor">Controls curve tightness (0 = straight, 1 = normal, 2 = exaggerated)</param>
	/// <param name="isClosedLoop">When true, wraps control points for smooth closed curve</param>
	/// <returns>Array of smoothed points including start and end points</returns>
	public static Vector2[] GenerateSmoothPoints(
		Vector2[] originalPoints,
		int startIndex,
		int endIndex,
		float pointsPerUnit,
		float bulgeFactor = 1.0f,
		bool isClosedLoop = false)
	{
		if (originalPoints == null || originalPoints.Length < 2)
		{
			return originalPoints;
		}

		if (startIndex < 0 || endIndex >= originalPoints.Length || startIndex >= endIndex)
		{
			return originalPoints;
		}

		var result = new List<Vector2>();

		// Process each segment in the range
		for (int i = startIndex; i < endIndex; i++)
		{
			Vector2 p1 = originalPoints[i];
			Vector2 p2 = originalPoints[i + 1];

			// Get p0 (before p1) - wrap or extrapolate at boundaries
			Vector2 p0;
			if (i > 0)
			{
				p0 = originalPoints[i - 1];
			}
			else if (isClosedLoop)
			{
				// Wrap to second-to-last point for smooth loop
				p0 = originalPoints[originalPoints.Length - 2];
			}
			else
			{
				// Reflect p2 around p1 to extrapolate
				p0 = p1 + (p1 - p2);
			}

			// Get p3 (after p2) - wrap or extrapolate at boundaries
			Vector2 p3;
			if (i + 2 < originalPoints.Length)
			{
				p3 = originalPoints[i + 2];
			}
			else if (isClosedLoop)
			{
				// Wrap to second point for smooth loop
				p3 = originalPoints[1];
			}
			else
			{
				// Reflect p1 around p2 to extrapolate
				p3 = p2 + (p2 - p1);
			}

			// Preserve boundary tangents: adjust control points so entry/exit aligns with adjacent segments
			// First segment: adjust p0 so entry tangent aligns with incoming direction
			if (i == startIndex && startIndex > 0)
			{
				// Entry tangent at p1 is 0.5*(p2-p0), want it parallel to (p1 - prevPoint)
				Vector2 prevPoint = originalPoints[startIndex - 1];
				p0 = p2 - (p1 - prevPoint);
			}

			// Last segment: adjust p3 so exit tangent aligns with outgoing direction
			if (i == endIndex - 1 && endIndex + 1 < originalPoints.Length)
			{
				// Exit tangent at p2 is 0.5*(p3-p1), want it parallel to (nextPoint - p2)
				Vector2 nextPoint = originalPoints[endIndex + 1];
				p3 = p1 + (nextPoint - p2);
			}

			// Apply bulge factor to control curve tightness
			// Bulge affects how much the tangent points influence the curve
			Vector2 adjustedP0 = p1 + ((p0 - p1) * bulgeFactor);
			Vector2 adjustedP3 = p2 + ((p3 - p2) * bulgeFactor);

			// Calculate number of points based on segment distance
			float segmentLength = (p2 - p1).Length();
			int numPoints = Mathf.Max(2, Mathf.CeilToInt(segmentLength * pointsPerUnit));

			// Generate interpolated points (skip the last point to avoid duplicates between segments)
			for (int j = 0; j < numPoints; j++)
			{
				float t = (float)j / numPoints;
				result.Add(Interpolate(adjustedP0, p1, p2, adjustedP3, t));
			}
		}

		// Add the final endpoint
		result.Add(originalPoints[endIndex]);

		return [.. result];
	}

	/// <summary>
	/// Generate smoothed points for a wrap-around range on a closed loop.
	/// Use when endIndex &lt; startIndex to smooth across the loop boundary.
	/// </summary>
	/// <param name="originalPoints">The original point array (closed loop)</param>
	/// <param name="startIndex">Index of the first point in the range</param>
	/// <param name="endIndex">Index of the last point (may be less than startIndex for wrap-around)</param>
	/// <param name="pointsPerUnit">Number of interpolated points per unit of distance</param>
	/// <param name="bulgeFactor">Controls curve tightness (0 = straight, 1 = normal, 2 = exaggerated)</param>
	/// <returns>Array of smoothed points from start wrapping around to end</returns>
	public static Vector2[] GenerateSmoothPointsWithWrap(
		Vector2[] originalPoints,
		int startIndex,
		int endIndex,
		float pointsPerUnit,
		float bulgeFactor = 1.0f)
	{
		if (originalPoints == null || originalPoints.Length < 2)
		{
			return originalPoints;
		}

		var result = new List<Vector2>();
		int n = originalPoints.Length;

		// Build list of segment pairs (from, to) with wrapping
		var segments = new List<(int from, int to)>();

		if (startIndex == endIndex)
		{
			// Full loop: all segments from start back to start
			for (int i = 0; i < n; i++)
			{
				int from = (startIndex + i) % n;
				int to = (startIndex + i + 1) % n;
				segments.Add((from, to));
			}
		}
		else if (endIndex < startIndex)
		{
			// Wrap-around: start→end_of_array, then 0→end
			for (int i = startIndex; i < n; i++)
			{
				segments.Add((i, (i + 1) % n));
			}

			for (int i = 0; i < endIndex; i++)
			{
				segments.Add((i, i + 1));
			}
		}
		else
		{
			// Normal range (shouldn't typically call this method for normal range, but handle it)
			for (int i = startIndex; i < endIndex; i++)
			{
				segments.Add((i, i + 1));
			}
		}

		// Determine first and last segment indices for boundary tangent preservation
		// (Only applies when NOT a full loop - full loops should flow smoothly)
		bool isFullLoop = startIndex == endIndex;
		int firstFromIdx = segments.Count > 0 ? segments[0].from : -1;
		int lastToIdx = segments.Count > 0 ? segments[^1].to : -1;

		// Process each segment
		foreach (var (fromIdx, toIdx) in segments)
		{
			Vector2 p1 = originalPoints[fromIdx];
			Vector2 p2 = originalPoints[toIdx];

			// Get control points with wrap-around
			int prevIdx = (fromIdx - 1 + n) % n;
			int nextIdx = (toIdx + 1) % n;
			Vector2 p0 = originalPoints[prevIdx];
			Vector2 p3 = originalPoints[nextIdx];

			// Preserve boundary tangents for non-full-loop wrap-arounds
			if (!isFullLoop)
			{
				// First segment: adjust p0 so entry tangent aligns with incoming direction
				if (fromIdx == firstFromIdx)
				{
					Vector2 prevPoint = originalPoints[prevIdx];
					p0 = p2 - (p1 - prevPoint);
				}

				// Last segment: adjust p3 so exit tangent aligns with outgoing direction
				if (toIdx == lastToIdx)
				{
					Vector2 nextPoint = originalPoints[nextIdx];
					p3 = p1 + (nextPoint - p2);
				}
			}

			// Apply bulge factor
			Vector2 adjustedP0 = p1 + ((p0 - p1) * bulgeFactor);
			Vector2 adjustedP3 = p2 + ((p3 - p2) * bulgeFactor);

			// Calculate number of points based on segment distance
			float segmentLength = (p2 - p1).Length();
			int numPoints = Mathf.Max(2, Mathf.CeilToInt(segmentLength * pointsPerUnit));

			// Generate interpolated points (skip the last point to avoid duplicates between segments)
			for (int j = 0; j < numPoints; j++)
			{
				float t = (float)j / numPoints;
				result.Add(Interpolate(adjustedP0, p1, p2, adjustedP3, t));
			}
		}

		// Add the final endpoint
		result.Add(originalPoints[endIndex]);

		return [.. result];
	}

	/// <summary>
	/// Redistribute points evenly along the curve by arc length
	/// </summary>
	public static Vector2[] RedistributeEvenly(Vector2[] points, int targetPointCount)
	{
		if (points == null || points.Length < 2 || targetPointCount < 2)
		{
			return points;
		}

		// Calculate cumulative arc lengths
		float[] arcLengths = new float[points.Length];
		arcLengths[0] = 0;
		for (int i = 1; i < points.Length; i++)
		{
			arcLengths[i] = arcLengths[i - 1] + points[i].DistanceTo(points[i - 1]);
		}

		float totalLength = arcLengths[^1];

		if (totalLength < 0.0001f)
		{
			return points;
		}

		// Resample at equal arc-length intervals
		var result = new Vector2[targetPointCount];
		result[0] = points[0];
		result[^1] = points[^1];

		for (int i = 1; i < targetPointCount - 1; i++)
		{
			float targetArcLength = totalLength * i / (targetPointCount - 1);

			// Find segment containing this arc length
			int segmentIdx = 0;
			for (int j = 1; j < arcLengths.Length; j++)
			{
				if (arcLengths[j] >= targetArcLength)
				{
					segmentIdx = j - 1;
					break;
				}
			}

			// Interpolate within segment
			float segmentStart = arcLengths[segmentIdx];
			float segmentEnd = arcLengths[segmentIdx + 1];
			float t = (targetArcLength - segmentStart) / (segmentEnd - segmentStart);

			result[i] = points[segmentIdx].Lerp(points[segmentIdx + 1], t);
		}

		return result;
	}
}
#endif
