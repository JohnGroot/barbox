using System;
using BarBox.Core.Drawing;
using Godot;
using Shouldly;

namespace BarBox.Tests.Unit.Drawing;

/// <summary>
/// Geometry assertions for triangle soup, without a GPU. The coverage sampler is the load
/// bearing one: it turns "does the round join actually fill the wedge" into something
/// assertable, which pure vertex-count checks cannot do.
/// </summary>
public static class DrawingTestHelpers
{
	/// <summary>How many triangles cover a point. Overlap is legitimate on the inside of joins.</summary>
	public static int CountCovering(VertexBuffer buffer, Vector2 point)
	{
		int hits = 0;
		for (int i = 0; i < buffer.IndexCount; i += 3)
		{
			var a = buffer.Points[buffer.Indices[i]];
			var b = buffer.Points[buffer.Indices[i + 1]];
			var c = buffer.Points[buffer.Indices[i + 2]];
			if (PointInTriangle(point, a, b, c))
			{
				hits++;
			}
		}

		return hits;
	}

	public static bool IsCovered(VertexBuffer buffer, Vector2 point)
	{
		return CountCovering(buffer, point) > 0;
	}

	/// <summary>Summed unsigned triangle area. Overlapping triangles are counted twice by design.</summary>
	public static float TotalArea(VertexBuffer buffer)
	{
		float total = 0f;
		for (int i = 0; i < buffer.IndexCount; i += 3)
		{
			var a = buffer.Points[buffer.Indices[i]];
			var b = buffer.Points[buffer.Indices[i + 1]];
			var c = buffer.Points[buffer.Indices[i + 2]];
			total += Mathf.Abs(((b - a).X * (c - a).Y) - ((b - a).Y * (c - a).X)) * 0.5f;
		}

		return total;
	}

	public static float ShoelaceArea(ReadOnlySpan<Vector2> polygon)
	{
		float sum = 0f;
		for (int i = 0; i < polygon.Length; i++)
		{
			var a = polygon[i];
			var b = polygon[(i + 1) % polygon.Length];
			sum += (a.X * b.Y) - (b.X * a.Y);
		}

		return Mathf.Abs(sum) * 0.5f;
	}

	/// <summary>
	/// Structural invariants every tessellator result must hold. This is where the "no NaN or
	/// degenerate output" requirement actually lives, so call it from every tessellation test.
	/// </summary>
	public static void AssertWellFormed(VertexBuffer buffer)
	{
		(buffer.IndexCount % 3).ShouldBe(0, "Index count should be a whole number of triangles");

		for (int i = 0; i < buffer.IndexCount; i++)
		{
			buffer.Indices[i].ShouldBeInRange(0, buffer.VertexCount - 1, $"Index {i} points outside the vertex range");
		}

		for (int i = 0; i < buffer.VertexCount; i++)
		{
			float.IsFinite(buffer.Points[i].X).ShouldBeTrue($"Vertex {i} X is not finite");
			float.IsFinite(buffer.Points[i].Y).ShouldBeTrue($"Vertex {i} Y is not finite");

			var color = buffer.Colors[i];
			float.IsFinite(color.R).ShouldBeTrue($"Vertex {i} color R is not finite");
			float.IsFinite(color.G).ShouldBeTrue($"Vertex {i} color G is not finite");
			float.IsFinite(color.B).ShouldBeTrue($"Vertex {i} color B is not finite");
			float.IsFinite(color.A).ShouldBeTrue($"Vertex {i} color A is not finite");
			color.A.ShouldBeInRange(0f, 1f, $"Vertex {i} alpha is out of range");
		}
	}

	public static float DistanceToPolyline(Vector2 point, ReadOnlySpan<Vector2> points, bool closed)
	{
		float nearest = float.MaxValue;
		for (int i = 1; i < points.Length; i++)
		{
			nearest = Math.Min(nearest, point.DistanceTo(ClosestPointOnSegment(point, points[i - 1], points[i])));
		}

		if (closed && points.Length >= 3)
		{
			nearest = Math.Min(nearest, point.DistanceTo(ClosestPointOnSegment(point, points[^1], points[0])));
		}

		return nearest;
	}

	public static Vector2 ClosestPointOnSegment(Vector2 point, Vector2 a, Vector2 b)
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

	/// <summary>Peak alpha across all vertices — the hairline clamp's observable output.</summary>
	public static float MaxAlpha(VertexBuffer buffer)
	{
		float max = 0f;
		for (int i = 0; i < buffer.VertexCount; i++)
		{
			max = Math.Max(max, buffer.Colors[i].A);
		}

		return max;
	}

	private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
	{
		float d1 = Sign(p, a, b);
		float d2 = Sign(p, b, c);
		float d3 = Sign(p, c, a);

		bool anyNegative = d1 < 0f || d2 < 0f || d3 < 0f;
		bool anyPositive = d1 > 0f || d2 > 0f || d3 > 0f;

		// Consistent sign on all three edges means inside, for either winding.
		return !(anyNegative && anyPositive);
	}

	private static float Sign(Vector2 p, Vector2 a, Vector2 b)
	{
		return ((p.X - b.X) * (a.Y - b.Y)) - ((a.X - b.X) * (p.Y - b.Y));
	}
}
