using System;
using BarBox.Core.Drawing;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

namespace BarBox.Tests.Unit.Drawing;

public class PathFlattenerTests : TestClass
{
	private const float Tolerance = 0.0001f;

	public PathFlattenerTests(Node testScene)
		: base(testScene)
	{
	}

	[Test]
	public void Arc_Endpoints_LandExactlyOnRequestedAngles()
	{
		// Arrange - angles derive from the endpoints, never from an accumulated step
		var path = new FlatPath();
		var center = new Vector2(10f, 20f);
		const float Radius = 50f;
		const float Start = 0.3f;
		const float End = 2.1f;

		// Act
		PathFlattener.Arc(center, Radius, Start, End, PathFlattener.DefaultTolerance, path);

		// Assert
		var expectedStart = center + (new Vector2(Mathf.Cos(Start), Mathf.Sin(Start)) * Radius);
		var expectedEnd = center + (new Vector2(Mathf.Cos(End), Mathf.Sin(End)) * Radius);
		path.Points[0].X.ShouldBe(expectedStart.X, Tolerance);
		path.Points[0].Y.ShouldBe(expectedStart.Y, Tolerance);
		path.Points[path.Count - 1].X.ShouldBe(expectedEnd.X, Tolerance);
		path.Points[path.Count - 1].Y.ShouldBe(expectedEnd.Y, Tolerance);
	}

	[Test]
	public void Arc_MaxDeviationFromTrueCircle_StaysWithinTolerance()
	{
		// Arrange
		var path = new FlatPath();
		var center = new Vector2(0f, 0f);
		const float Radius = 100f;
		const float ToleranceUnits = 0.25f;

		// Act
		PathFlattener.Arc(center, Radius, 0f, Mathf.Pi, ToleranceUnits, path);

		// Assert - the chord midpoint is the worst case on each segment
		for (int i = 1; i < path.Count; i++)
		{
			var chordMid = (path.Points[i] + path.Points[i - 1]) * 0.5f;
			float deviation = Radius - chordMid.DistanceTo(center);
			deviation.ShouldBeLessThanOrEqualTo(ToleranceUnits + Tolerance, $"Segment {i} sagitta exceeds tolerance");
		}
	}

	[Test]
	public void Arc_PointCount_IsMonotonicInTolerance()
	{
		// Arrange
		var loose = new FlatPath();
		var tight = new FlatPath();

		// Act
		PathFlattener.Arc(Vector2.Zero, 100f, 0f, Mathf.Pi, 2f, loose);
		PathFlattener.Arc(Vector2.Zero, 100f, 0f, Mathf.Pi, 0.05f, tight);

		// Assert
		tight.Count.ShouldBeGreaterThan(loose.Count, "A tighter tolerance should never emit fewer points");
	}

	[Test]
	public void Arc_ToleranceLargerThanRadius_StaysFinite()
	{
		// Arrange - the acos argument would leave [-1, 1] without a clamp
		var path = new FlatPath();

		// Act
		PathFlattener.Arc(Vector2.Zero, 1f, 0f, Mathf.Pi, 100f, path);

		// Assert
		path.Count.ShouldBeGreaterThanOrEqualTo(2);
		for (int i = 0; i < path.Count; i++)
		{
			float.IsFinite(path.Points[i].X).ShouldBeTrue();
			float.IsFinite(path.Points[i].Y).ShouldBeTrue();
		}
	}

	[Test]
	public void Circle_IsClosedWithoutADuplicateClosingPoint()
	{
		// Arrange
		var path = new FlatPath();

		// Act
		PathFlattener.Circle(new Vector2(5f, 5f), 30f, PathFlattener.DefaultTolerance, path);

		// Assert
		path.Closed.ShouldBeTrue();
		path.Count.ShouldBeGreaterThanOrEqualTo(3);
		path.Points[path.Count - 1].DistanceTo(path.Points[0])
			.ShouldBeGreaterThan(PolylineMathEpsilon, "Closed paths imply the closing segment rather than repeating point 0");
	}

	[Test]
	public void Circle_AllPointsLieOnTheRadius()
	{
		// Arrange
		var path = new FlatPath();
		var center = new Vector2(5f, -5f);
		const float Radius = 30f;

		// Act
		PathFlattener.Circle(center, Radius, PathFlattener.DefaultTolerance, path);

		// Assert
		for (int i = 0; i < path.Count; i++)
		{
			path.Points[i].DistanceTo(center).ShouldBe(Radius, 0.001f);
		}
	}

	[Test]
	public void CubicBezier_Endpoints_MatchControlPointsExactly()
	{
		// Arrange
		var path = new FlatPath();
		var p0 = new Vector2(0f, 0f);
		var c0 = new Vector2(0f, 100f);
		var c1 = new Vector2(100f, 100f);
		var p1 = new Vector2(100f, 0f);

		// Act
		PathFlattener.CubicBezier(p0, c0, c1, p1, PathFlattener.DefaultTolerance, path);

		// Assert
		path.Points[0].ShouldBe(p0, "The first point should be the literal start control point");
		path.Points[path.Count - 1].ShouldBe(p1, "The last point should be the literal end control point");
	}

	[Test]
	public void CubicBezier_MaxDeviation_StaysWithinTolerance()
	{
		// Arrange
		var path = new FlatPath();
		var p0 = new Vector2(0f, 0f);
		var c0 = new Vector2(0f, 100f);
		var c1 = new Vector2(100f, 100f);
		var p1 = new Vector2(100f, 0f);
		const float ToleranceUnits = 0.25f;

		// Act
		PathFlattener.CubicBezier(p0, c0, c1, p1, ToleranceUnits, path);

		// Assert - densely sample the true curve and measure to the flattened polyline
		for (int i = 0; i <= 500; i++)
		{
			float t = i / 500f;
			var truePoint = EvaluateCubic(p0, c0, c1, p1, t);
			float nearest = float.MaxValue;
			for (int s = 1; s < path.Count; s++)
			{
				var closest = ClosestPointOnSegment(truePoint, path.Points[s - 1], path.Points[s]);
				nearest = Math.Min(nearest, truePoint.DistanceTo(closest));
			}

			nearest.ShouldBeLessThanOrEqualTo(ToleranceUnits + 0.01f, $"Curve point at t={t} is further than tolerance from the polyline");
		}
	}

	[Test]
	public void CubicBezier_AllControlPointsEqual_TerminatesWithoutExploding()
	{
		// Arrange - the flatness metric is undefined on a zero-length chord
		var path = new FlatPath();
		var p = new Vector2(7f, 7f);

		// Act
		PathFlattener.CubicBezier(p, p, p, p, PathFlattener.DefaultTolerance, path);

		// Assert
		path.Count.ShouldBe(2, "A degenerate curve should collapse to its endpoints, not recurse to the depth cap");
	}

	[Test]
	public void CubicBezier_StraightLineControlPoints_EmitsMinimalPoints()
	{
		// Arrange
		var path = new FlatPath();

		// Act
		PathFlattener.CubicBezier(
			new Vector2(0f, 0f),
			new Vector2(10f, 0f),
			new Vector2(20f, 0f),
			new Vector2(30f, 0f),
			PathFlattener.DefaultTolerance,
			path);

		// Assert
		path.Count.ShouldBe(2, "An already-flat curve needs no subdivision");
	}

	[Test]
	public void QuadBezier_MatchesEquivalentElevatedCubic()
	{
		// Arrange
		var quad = new FlatPath();
		var cubic = new FlatPath();
		var p0 = new Vector2(0f, 0f);
		var c = new Vector2(50f, 100f);
		var p1 = new Vector2(100f, 0f);

		// Act
		PathFlattener.QuadBezier(p0, c, p1, PathFlattener.DefaultTolerance, quad);
		PathFlattener.CubicBezier(
			p0,
			p0 + ((2f / 3f) * (c - p0)),
			p1 + ((2f / 3f) * (c - p1)),
			p1,
			PathFlattener.DefaultTolerance,
			cubic);

		// Assert
		quad.Count.ShouldBe(cubic.Count);
		for (int i = 0; i < quad.Count; i++)
		{
			quad.Points[i].ShouldBe(cubic.Points[i]);
		}
	}

	[Test]
	public void RoundedRect_CornersAreContinuousWithoutDuplicateVertices()
	{
		// Arrange
		var path = new FlatPath();

		// Act
		PathFlattener.RoundedRect(new Rect2(0f, 0f, 200f, 100f), 20f, PathFlattener.DefaultTolerance, path);

		// Assert
		path.Closed.ShouldBeTrue();
		for (int i = 1; i < path.Count; i++)
		{
			path.Points[i].DistanceTo(path.Points[i - 1])
				.ShouldBeGreaterThan(PolylineMathEpsilon, $"Vertex {i} duplicates its predecessor");
		}

		path.Points[path.Count - 1].DistanceTo(path.Points[0])
			.ShouldBeGreaterThan(PolylineMathEpsilon, "The seam should not carry a duplicate closing vertex");
	}

	[Test]
	public void RoundedRect_StaysWithinItsBounds()
	{
		// Arrange
		var rect = new Rect2(10f, 20f, 200f, 100f);
		var path = new FlatPath();

		// Act
		PathFlattener.RoundedRect(rect, 20f, PathFlattener.DefaultTolerance, path);

		// Assert
		for (int i = 0; i < path.Count; i++)
		{
			path.Points[i].X.ShouldBeInRange(rect.Position.X - 0.01f, rect.Position.X + rect.Size.X + 0.01f);
			path.Points[i].Y.ShouldBeInRange(rect.Position.Y - 0.01f, rect.Position.Y + rect.Size.Y + 0.01f);
		}
	}

	[Test]
	public void RoundedRect_RadiusExceedingHalfExtent_ClampsToStadium()
	{
		// Arrange
		var path = new FlatPath();
		var rect = new Rect2(0f, 0f, 200f, 100f);

		// Act - a radius past half the short side would otherwise self-intersect
		PathFlattener.RoundedRect(rect, 500f, PathFlattener.DefaultTolerance, path);

		// Assert
		for (int i = 0; i < path.Count; i++)
		{
			path.Points[i].X.ShouldBeInRange(-0.01f, 200.01f);
			path.Points[i].Y.ShouldBeInRange(-0.01f, 100.01f);
		}

		var center = new Vector2(100f, 50f);
		path.Points[0].DistanceTo(center).ShouldBeGreaterThan(1f, "The stadium should still have extent");
	}

	[Test]
	public void RoundedRect_ZeroRadius_EmitsFourCorners()
	{
		// Arrange
		var path = new FlatPath();

		// Act
		PathFlattener.RoundedRect(new Rect2(0f, 0f, 10f, 20f), 0f, PathFlattener.DefaultTolerance, path);

		// Assert
		path.Count.ShouldBe(4);
		path.Closed.ShouldBeTrue();
	}

	[Test]
	public void Polyline_ClosedWithTrailingDuplicateOfFirst_StripsTheDuplicate()
	{
		// Arrange
		var path = new FlatPath();
		var points = new[]
		{
			new Vector2(0f, 0f),
			new Vector2(10f, 0f),
			new Vector2(10f, 10f),
			new Vector2(0f, 0f),
		};

		// Act
		PathFlattener.Polyline(points, closed: true, path);

		// Assert
		path.Count.ShouldBe(3, "An author-supplied closing duplicate should not survive as a zero-length segment");
		path.Closed.ShouldBeTrue();
	}

	[Test]
	public void Polyline_OpenWithTrailingDuplicateOfFirst_KeepsIt()
	{
		// Arrange - an open path that happens to return to its start is not the same as a closed one
		var path = new FlatPath();
		var points = new[]
		{
			new Vector2(0f, 0f),
			new Vector2(10f, 0f),
			new Vector2(0f, 0f),
		};

		// Act
		PathFlattener.Polyline(points, closed: false, path);

		// Assert
		path.Count.ShouldBe(3);
	}

	[Test]
	public void FinalizeT_OpenPath_RunsFromZeroToOne()
	{
		// Arrange
		var path = new FlatPath();
		var points = new[]
		{
			new Vector2(0f, 0f),
			new Vector2(10f, 0f),
			new Vector2(30f, 0f),
		};

		// Act
		PathFlattener.Polyline(points, closed: false, path);

		// Assert
		path.T[0].ShouldBe(0f, Tolerance);
		path.T[1].ShouldBe(10f / 30f, Tolerance, "T should track arc length, not point index");
		path.T[2].ShouldBe(1f, Tolerance);
	}

	[Test]
	public void FinalizeT_ClosedPath_LeavesRoomForTheClosingSegment()
	{
		// Arrange - a unit square: the last point sits 3/4 of the way around
		var path = new FlatPath();
		var points = new[]
		{
			new Vector2(0f, 0f),
			new Vector2(10f, 0f),
			new Vector2(10f, 10f),
			new Vector2(0f, 10f),
		};

		// Act
		PathFlattener.Polyline(points, closed: true, path);

		// Assert
		path.T[0].ShouldBe(0f, Tolerance);
		path.T[3].ShouldBe(0.75f, Tolerance, "The remainder up to 1 is the implied closing segment");
	}

	[Test]
	public void FinalizeT_ZeroLengthPath_ProducesNoNaN()
	{
		// Arrange
		var path = new FlatPath();
		var points = new[] { new Vector2(5f, 5f), new Vector2(5f, 5f) };

		// Act
		PathFlattener.Polyline(points, closed: false, path);

		// Assert
		for (int i = 0; i < path.Count; i++)
		{
			float.IsFinite(path.T[i]).ShouldBeTrue("A degenerate path must not divide by zero length");
		}
	}

	[Test]
	public void Clear_ResetsClosedFlag()
	{
		// Arrange - a pooled path reused for an open shape must not inherit the previous Closed
		var path = new FlatPath();
		PathFlattener.Circle(Vector2.Zero, 10f, PathFlattener.DefaultTolerance, path);

		// Act
		path.Clear();

		// Assert
		path.Closed.ShouldBeFalse();
		path.Count.ShouldBe(0);
	}

	private const float PolylineMathEpsilon = 1e-4f;

	private static Vector2 EvaluateCubic(Vector2 p0, Vector2 c0, Vector2 c1, Vector2 p1, float t)
	{
		float u = 1f - t;
		return (u * u * u * p0) + (3f * u * u * t * c0) + (3f * u * t * t * c1) + (t * t * t * p1);
	}

	private static Vector2 ClosestPointOnSegment(Vector2 point, Vector2 a, Vector2 b)
	{
		Vector2 ab = b - a;
		float lengthSq = ab.LengthSquared();
		if (lengthSq <= 0f)
		{
			return a;
		}

		float t = Mathf.Clamp((point - a).Dot(ab) / lengthSq, 0f, 1f);
		return a + (t * ab);
	}
}
