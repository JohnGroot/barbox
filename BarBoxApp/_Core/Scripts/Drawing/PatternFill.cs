using System;
using Godot;

namespace BarBox.Core.Drawing;

/// <summary>
/// Two-color patterned fills — checkerboards and stripes — clipped against an arbitrary polygon.
/// Built entirely on FillTessellator.TessellateClipped, which already handles multi-island output
/// and CW-hole rejection; this file only walks a grid/band and hands each cell to it. Both colors
/// land in the same output buffer via per-vertex color, which is what lets a two-color pattern be
/// one Shape (see Shape.SetMesh) rather than two.
///
/// Output carries no feather — FillTessellator has none by design, and the "bare fill gets an
/// automatic hairline stroke" convenience in Shape.Rebuild never runs for a Mesh-kind shape. v1
/// accepts hard cell edges rather than engineering per-cell AA.
/// </summary>
public static class PatternFill
{
	public static void Checker(
		ReadOnlySpan<Vector2> subject,
		Vector2 origin,
		float cellSize,
		Color colorA,
		Color colorB,
		VertexBuffer output)
	{
		if (output == null)
		{
			return;
		}

		output.Clear();

		if (subject.Length < 3 || !(cellSize > 0f) || !float.IsFinite(cellSize))
		{
			return;
		}

		(Vector2 min, Vector2 max) = Bounds(subject);

		// One cell of margin so an edge cell fully covers any boundary it straddles.
		int ixStart = Mathf.FloorToInt((min.X - origin.X) / cellSize) - 1;
		int ixEnd = Mathf.CeilToInt((max.X - origin.X) / cellSize) + 1;
		int iyStart = Mathf.FloorToInt((min.Y - origin.Y) / cellSize) - 1;
		int iyEnd = Mathf.CeilToInt((max.Y - origin.Y) / cellSize) + 1;

		Vector2[] subjectArray = subject.ToArray();
		var cell = new Vector2[4];

		for (int iy = iyStart; iy <= iyEnd; iy++)
		{
			for (int ix = ixStart; ix <= ixEnd; ix++)
			{
				float x0 = origin.X + (ix * cellSize);
				float y0 = origin.Y + (iy * cellSize);

				cell[0] = new Vector2(x0, y0);
				cell[1] = new Vector2(x0 + cellSize, y0);
				cell[2] = new Vector2(x0 + cellSize, y0 + cellSize);
				cell[3] = new Vector2(x0, y0 + cellSize);

				Color color = ((ix + iy) & 1) == 0 ? colorA : colorB;
				FillTessellator.TessellateClipped(subjectArray, cell, color, output);
			}
		}
	}

	public static void Stripes(
		ReadOnlySpan<Vector2> subject,
		float angleRad,
		float bandWidth,
		Color colorA,
		Color colorB,
		VertexBuffer output)
	{
		if (output == null)
		{
			return;
		}

		output.Clear();

		if (subject.Length < 3 || !(bandWidth > 0f) || !float.IsFinite(bandWidth) || !float.IsFinite(angleRad))
		{
			return;
		}

		(Vector2 min, Vector2 max) = Bounds(subject);
		Vector2 center = (min + max) * 0.5f;

		// Covers the bounding box at any rotation: the farthest corner plus one band of margin.
		float halfExtent = center.DistanceTo(max) + bandWidth;

		Vector2 dir = new(Mathf.Cos(angleRad), Mathf.Sin(angleRad));
		Vector2 normal = dir.Orthogonal();

		Vector2[] subjectArray = subject.ToArray();
		var band = new Vector2[4];

		int bandCount = Mathf.CeilToInt(halfExtent * 2f / bandWidth) + 2;
		int startIndex = -bandCount / 2;

		for (int i = 0; i < bandCount; i++)
		{
			int index = startIndex + i;
			Vector2 bandCenter = center + (normal * (index * bandWidth));
			Vector2 nearEdge = bandCenter - (normal * (bandWidth * 0.5f));
			Vector2 farEdge = bandCenter + (normal * (bandWidth * 0.5f));
			Vector2 along = dir * halfExtent;

			band[0] = nearEdge - along;
			band[1] = nearEdge + along;
			band[2] = farEdge + along;
			band[3] = farEdge - along;

			Color color = (index & 1) == 0 ? colorA : colorB;
			FillTessellator.TessellateClipped(subjectArray, band, color, output);
		}
	}

	private static (Vector2 Min, Vector2 Max) Bounds(ReadOnlySpan<Vector2> polygon)
	{
		Vector2 min = polygon[0];
		Vector2 max = polygon[0];

		for (int i = 1; i < polygon.Length; i++)
		{
			Vector2 p = polygon[i];
			min.X = Mathf.Min(min.X, p.X);
			min.Y = Mathf.Min(min.Y, p.Y);
			max.X = Mathf.Max(max.X, p.X);
			max.Y = Mathf.Max(max.Y, p.Y);
		}

		return (min, max);
	}
}
