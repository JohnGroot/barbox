using System;
using Godot;

namespace BarBox.Core.Drawing;

/// <summary>
/// Grow-only pooled triangle soup. Arrays are oversized; only [0, VertexCount) and
/// [0, IndexCount) are valid, and the spans below are what let ShapeCanvas upload via
/// RenderingServer's ReadOnlySpan overloads without a slice copy or a managed allocation.
///
/// Buffers are relocatable: each Shape owns one whose indices are self-relative, and the
/// canvas rebuilds a bucket with Clear() + Append(each). Nothing tracks a shape's position
/// inside a shared array, so a shape growing or shrinking cannot corrupt its neighbours.
///
/// The tessellator contract: an append records `vertexBase = VertexCount` on entry, emits
/// absolute indices, never touches vertices below vertexBase, and on rejection leaves both
/// counts unchanged.
/// </summary>
public sealed class VertexBuffer
{
	public Vector2[] Points;

	/// <summary>Parallel to Points. RenderingServer requires length 1 or exactly the point count.</summary>
	public Color[] Colors;

	public int[] Indices;

	public int VertexCount;
	public int IndexCount;

	public VertexBuffer(int vertexCapacity = 256, int indexCapacity = 512)
	{
		Points = new Vector2[Mathf.Max(vertexCapacity, 1)];
		Colors = new Color[Mathf.Max(vertexCapacity, 1)];
		Indices = new int[Mathf.Max(indexCapacity, 1)];
	}

	public ReadOnlySpan<Vector2> PointSpan => Points.AsSpan(0, VertexCount);

	public ReadOnlySpan<Color> ColorSpan => Colors.AsSpan(0, VertexCount);

	public ReadOnlySpan<int> IndexSpan => Indices.AsSpan(0, IndexCount);

	public bool IsEmpty => IndexCount == 0;

	/// <summary>Retains the arrays, which is what keeps steady-state rebuilds allocation-free.</summary>
	public void Clear()
	{
		VertexCount = 0;
		IndexCount = 0;
	}

	public void EnsureCapacity(int extraVertices, int extraIndices)
	{
		int neededVertices = VertexCount + extraVertices;
		if (neededVertices > Points.Length)
		{
			int size = Points.Length;
			while (size < neededVertices)
			{
				size *= 2;
			}

			Array.Resize(ref Points, size);
			Array.Resize(ref Colors, size);
		}

		int neededIndices = IndexCount + extraIndices;
		if (neededIndices > Indices.Length)
		{
			int size = Indices.Length;
			while (size < neededIndices)
			{
				size *= 2;
			}

			Array.Resize(ref Indices, size);
		}
	}

	public int AddVertex(Vector2 point, Color color)
	{
		EnsureCapacity(1, 0);
		Points[VertexCount] = point;
		Colors[VertexCount] = color;
		return VertexCount++;
	}

	public void AddTriangle(int a, int b, int c)
	{
		EnsureCapacity(0, 3);
		Indices[IndexCount++] = a;
		Indices[IndexCount++] = b;
		Indices[IndexCount++] = c;
	}

	public void AddQuad(int a, int b, int c, int d)
	{
		AddTriangle(a, b, c);
		AddTriangle(a, c, d);
	}

	/// <summary>Concatenates src, rebasing its self-relative indices onto the vertices already here.</summary>
	public void Append(VertexBuffer src)
	{
		if (src == null || src.VertexCount == 0)
		{
			return;
		}

		int vertexBase = VertexCount;
		EnsureCapacity(src.VertexCount, src.IndexCount);

		Array.Copy(src.Points, 0, Points, vertexBase, src.VertexCount);
		Array.Copy(src.Colors, 0, Colors, vertexBase, src.VertexCount);
		VertexCount += src.VertexCount;

		for (int i = 0; i < src.IndexCount; i++)
		{
			Indices[IndexCount + i] = src.Indices[i] + vertexBase;
		}

		IndexCount += src.IndexCount;
	}

	/// <summary>
	/// Append with positions baked through a transform. Colors pass through untouched — the
	/// feather alpha they carry is already resolved and must not be re-derived here.
	/// </summary>
	public void Append(VertexBuffer src, Transform2D xform)
	{
		if (src == null || src.VertexCount == 0)
		{
			return;
		}

		int vertexBase = VertexCount;
		EnsureCapacity(src.VertexCount, src.IndexCount);

		for (int i = 0; i < src.VertexCount; i++)
		{
			Points[vertexBase + i] = xform * src.Points[i];
			Colors[vertexBase + i] = src.Colors[i];
		}

		VertexCount += src.VertexCount;

		for (int i = 0; i < src.IndexCount; i++)
		{
			Indices[IndexCount + i] = src.Indices[i] + vertexBase;
		}

		IndexCount += src.IndexCount;
	}
}
