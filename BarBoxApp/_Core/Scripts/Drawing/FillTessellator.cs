using System;
using Godot;

namespace BarBox.Core.Drawing;

/// <summary>
/// Triangulates polygons into flat-colored triangles, borrowing Geometry2D rather than
/// hand-rolling ear clipping. Godot's triangulator allocates, but only on rebuild.
///
/// Fills carry no feather. An outlined fill is covered at the edge by its stroke; a bare fill
/// gets an automatic hairline stroke of the fill color, applied a layer up in ShapeBuilder
/// rather than here, so this stays a pure triangulator.
/// </summary>
public static class FillTessellator
{
	private const float AreaEpsilon = 1e-6f;

	/// <summary>
	/// Returns false without emitting anything when the polygon cannot be triangulated
	/// (self-intersecting or degenerate).
	/// </summary>
	public static bool Tessellate(ReadOnlySpan<Vector2> polygon, Color color, VertexBuffer output)
	{
		if (output == null || IsDegenerate(polygon))
		{
			return false;
		}

		// TriangulatePolygon accepts either winding but signals failure with an EMPTY array
		// rather than an error, so an unchecked index into it would throw on bad input.
		int[] indices = Geometry2D.TriangulatePolygon(polygon);
		if (indices == null || indices.Length < 3)
		{
			return false;
		}

		int vertexBase = output.VertexCount;
		output.EnsureCapacity(polygon.Length, indices.Length);

		for (int i = 0; i < polygon.Length; i++)
		{
			output.AddVertex(polygon[i], color);
		}

		for (int i = 0; i + 2 < indices.Length; i += 3)
		{
			output.AddTriangle(vertexBase + indices[i], vertexBase + indices[i + 1], vertexBase + indices[i + 2]);
		}

		return true;
	}

	/// <summary>
	/// Clips bands against subject and triangulates every resulting island. This is how the
	/// checkered start line and the zone tints stay in the vertex-color batch instead of
	/// needing a shader. Returns how many polygons were emitted.
	/// </summary>
	public static int TessellateClipped(Vector2[] subject, Vector2[] bands, Color color, VertexBuffer output)
	{
		if (output == null || subject == null || bands == null || subject.Length < 3 || bands.Length < 3)
		{
			return 0;
		}

		// IntersectPolygons takes arrays, not spans, and can return several disjoint islands.
		Godot.Collections.Array<Vector2[]> pieces = Geometry2D.IntersectPolygons(bands, subject);
		if (pieces == null || pieces.Count == 0)
		{
			return 0;
		}

		int emitted = 0;
		foreach (Vector2[] piece in pieces)
		{
			// A clockwise result is a hole in the clip output, not an island to fill.
			if (piece == null || piece.Length < 3 || Geometry2D.IsPolygonClockwise(piece))
			{
				continue;
			}

			if (Tessellate(piece, color, output))
			{
				emitted++;
			}
		}

		return emitted;
	}

	public static bool IsDegenerate(ReadOnlySpan<Vector2> polygon)
	{
		if (polygon.Length < 3 || !PolylineMath.AllFinite(polygon))
		{
			return true;
		}

		float sum = 0f;
		for (int i = 0; i < polygon.Length; i++)
		{
			Vector2 a = polygon[i];
			Vector2 b = polygon[(i + 1) % polygon.Length];
			sum += (a.X * b.Y) - (b.X * a.Y);
		}

		return Mathf.Abs(sum * 0.5f) <= AreaEpsilon;
	}
}
