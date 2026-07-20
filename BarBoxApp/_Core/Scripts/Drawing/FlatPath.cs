using System;
using Godot;

namespace BarBox.Core.Drawing;

/// <summary>
/// A flattened polyline plus its normalized arc-length parameterization. Pooled and reused;
/// callers own the instance.
///
/// Closed is a flag, never a repeated vertex: the closing segment is implied. Duplicating
/// point 0 at the end would put a seam where the stroke tessellator expects a normal joint.
/// </summary>
public sealed class FlatPath
{
	public Vector2[] Points;

	/// <summary>Normalized cumulative arc length, filled by FinalizeT. Parallel to Points.</summary>
	public float[] T;

	public int Count;

	public bool Closed;

	public FlatPath(int capacity = 64)
	{
		Points = new Vector2[Mathf.Max(capacity, 1)];
		T = new float[Mathf.Max(capacity, 1)];
	}

	public ReadOnlySpan<Vector2> PointSpan => Points.AsSpan(0, Count);

	public ReadOnlySpan<float> TSpan => T.AsSpan(0, Count);

	public void Clear()
	{
		Count = 0;
		Closed = false;
	}

	public void EnsureCapacity(int total)
	{
		PooledArray.EnsureCapacity(ref Points, total);
		PooledArray.EnsureCapacity(ref T, total);
	}

	/// <summary>
	/// Copies another FlatPath's points, T, and Closed flag. Used when a Shape must own its
	/// contour data rather than alias someone else's pooled buffer (see Shape.FlattenPath).
	/// </summary>
	public void CopyFrom(FlatPath source)
	{
		EnsureCapacity(source.Count);
		Array.Copy(source.Points, Points, source.Count);
		Array.Copy(source.T, T, source.Count);
		Count = source.Count;
		Closed = source.Closed;
	}

	public void Add(Vector2 point)
	{
		EnsureCapacity(Count + 1);
		Points[Count++] = point;
	}

	/// <summary>
	/// Adds a point with an explicit T instead of deriving one via FinalizeT — used when a
	/// contour is windowed out of another, already-parameterized contour (PathTrimmer), so the
	/// copy inherits the source's arc-length parameterization instead of restarting it at 0. Do
	/// not call FinalizeT() afterward; it would overwrite these values.
	/// </summary>
	public void Add(Vector2 point, float t)
	{
		EnsureCapacity(Count + 1);
		Points[Count] = point;
		T[Count] = t;
		Count++;
	}

	/// <summary>
	/// Set Closed before calling. A closed path's total length includes the implied closing
	/// segment, so its last T lands short of 1 — the remainder up to 1 is that segment, which
	/// is what makes a gradient wrap the loop continuously instead of jumping at the seam.
	/// </summary>
	public void FinalizeT()
	{
		if (Count == 0)
		{
			return;
		}

		T[0] = 0f;
		if (Count == 1)
		{
			return;
		}

		float cumulative = 0f;
		for (int i = 1; i < Count; i++)
		{
			cumulative += Points[i].DistanceTo(Points[i - 1]);
			T[i] = cumulative;
		}

		float total = cumulative;
		if (Closed && Count >= 3)
		{
			total += Points[0].DistanceTo(Points[Count - 1]);
		}

		if (total <= 0f)
		{
			for (int i = 0; i < Count; i++)
			{
				T[i] = 0f;
			}

			return;
		}

		for (int i = 1; i < Count; i++)
		{
			T[i] /= total;
		}
	}
}
