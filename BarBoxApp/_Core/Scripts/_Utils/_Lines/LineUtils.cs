using Godot;

/// <summary>
/// Utility class for working with Line2D nodes and converting them to other formats
/// </summary>
public static class LineUtils
{
	/// <summary>
	/// Convert a Line2D to a Curve2D for use with Path2D and other curve-based systems
	/// </summary>
	/// <param name="line">The Line2D to convert</param>
	/// <returns>A new Curve2D with points from the Line2D, or null if invalid</returns>
	public static Curve2D Line2DToCurve2D(Line2D line)
	{
		if (line == null || line.GetPointCount() < 2)
			return null;

		var curve = new Curve2D();
		
		for (int i = 0; i < line.GetPointCount(); i++)
		{
			var point = line.GetPointPosition(i);
			curve.AddPoint(point);
		}

		return curve;
	}

	/// <summary>
	/// Check if a point is within the Line2D's width distance from the line
	/// </summary>
	/// <param name="point">The point to check</param>
	/// <param name="line">The Line2D to check against</param>
	/// <returns>True if the point is within the line's width distance</returns>
	public static bool IsPointNearLine2D(Vector2 point, Line2D line)
	{
		if (line == null || line.GetPointCount() < 2)
			return false;

		var halfWidth = line.Width / 2.0f;
		var halfWidthSq = halfWidth * halfWidth;

		// Inline segment iteration with early exit (avoids full O(N) scan when on track)
		for (int i = 0; i < line.GetPointCount() - 1; i++)
		{
			var closest = GetClosestPointOnSegment(point,
				line.GetPointPosition(i), line.GetPointPosition(i + 1));
			if (point.DistanceSquaredTo(closest) <= halfWidthSq)
				return true;
		}

		if (line.Closed && line.GetPointCount() >= 3)
		{
			var closest = GetClosestPointOnSegment(point,
				line.GetPointPosition(line.GetPointCount() - 1), line.GetPointPosition(0));
			if (point.DistanceSquaredTo(closest) <= halfWidthSq)
				return true;
		}

		return false;
	}

	/// <summary>
	/// Check if a point is within half-width distance of a line defined by cached points.
	/// Same algorithm as IsPointNearLine2D but avoids C#-to-native interop calls.
	/// </summary>
	public static bool IsPointNearCachedLine(Vector2 point, Vector2[] points, float halfWidthSq, bool closed)
	{
		if (points == null || points.Length < 2)
			return false;

		for (int i = 0; i < points.Length - 1; i++)
		{
			var closest = GetClosestPointOnSegment(point, points[i], points[i + 1]);
			if (point.DistanceSquaredTo(closest) <= halfWidthSq)
				return true;
		}

		if (closed && points.Length >= 3)
		{
			var closest = GetClosestPointOnSegment(point, points[points.Length - 1], points[0]);
			if (point.DistanceSquaredTo(closest) <= halfWidthSq)
				return true;
		}

		return false;
	}

	/// <summary>
	/// Find the closest point on a Line2D to a given point
	/// </summary>
	/// <param name="point">The point to find the closest position to</param>
	/// <param name="line">The Line2D to search on</param>
	/// <returns>The closest point on the line</returns>
	public static Vector2 GetClosestPointOnLine2D(Vector2 point, Line2D line)
	{
		if (line == null || line.GetPointCount() < 2)
			return point;

		var closestPoint = line.GetPointPosition(0);
		var closestDistance = point.DistanceTo(closestPoint);

		// Check each line segment
		for (int i = 0; i < line.GetPointCount() - 1; i++)
		{
			var segmentStart = line.GetPointPosition(i);
			var segmentEnd = line.GetPointPosition(i + 1);
			
			var segmentClosest = GetClosestPointOnSegment(point, segmentStart, segmentEnd);
			var segmentDistance = point.DistanceTo(segmentClosest);

			if (segmentDistance < closestDistance)
			{
				closestDistance = segmentDistance;
				closestPoint = segmentClosest;
			}
		}

		// Check closing segment if the line is closed
		if (line.Closed && line.GetPointCount() >= 3)
		{
			var lastPoint = line.GetPointPosition(line.GetPointCount() - 1);
			var firstPoint = line.GetPointPosition(0);
			
			var closingSegmentClosest = GetClosestPointOnSegment(point, lastPoint, firstPoint);
			var closingSegmentDistance = point.DistanceTo(closingSegmentClosest);
			
			if (closingSegmentDistance < closestDistance)
			{
				closestDistance = closingSegmentDistance;
				closestPoint = closingSegmentClosest;
			}
		}

		return closestPoint;
	}

	/// <summary>
	/// Find the closest point on a line segment to a given point
	/// </summary>
	/// <param name="point">The point to find the closest position to</param>
	/// <param name="segmentStart">Start of the line segment</param>
	/// <param name="segmentEnd">End of the line segment</param>
	/// <returns>The closest point on the segment</returns>
	internal static Vector2 GetClosestPointOnSegment(Vector2 point, Vector2 segmentStart, Vector2 segmentEnd)
	{
		var segmentVector = segmentEnd - segmentStart;
		var pointVector = point - segmentStart;
		
		var segmentLengthSquared = segmentVector.LengthSquared();
		
		// If the segment has no length, return the start point
		if (segmentLengthSquared == 0)
			return segmentStart;
		
		// Project the point onto the segment
		var t = pointVector.Dot(segmentVector) / segmentLengthSquared;
		
		// Clamp t to the segment bounds [0, 1]
		t = Mathf.Clamp(t, 0.0f, 1.0f);
		
		// Return the point on the segment
		return segmentStart + t * segmentVector;
	}
}