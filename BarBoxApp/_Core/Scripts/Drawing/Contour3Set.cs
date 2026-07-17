using System;
using Godot;

namespace BarBox.Core.Drawing;

/// <summary>
/// A run of points within a parent point array. Closed is a flag, never a repeated vertex,
/// matching FlatPath.
/// </summary>
public readonly struct Contour
{
	public readonly int Start;
	public readonly int Count;
	public readonly bool Closed;

	public Contour(int start, int count, bool closed)
	{
		Start = start;
		Count = count;
		Closed = closed;
	}
}

/// <summary>
/// Several disjoint 3D polylines sharing one pooled point array. Pooled and reused; callers
/// own the instance.
///
/// A wireframe box is 12 edges that cannot be one polyline, so a Shape built from one of these
/// flattens and tessellates each contour in turn into its single VertexBuffer. That is the same
/// shape of work DashSplitter already produces, so the tessellator needs nothing new.
/// </summary>
public sealed class Contour3Set
{
	public Vector3[] Points;
	public Contour[] Contours;

	public int PointCount;
	public int ContourCount;

	public Contour3Set(int pointCapacity = 64, int contourCapacity = 8)
	{
		Points = new Vector3[Mathf.Max(pointCapacity, 1)];
		Contours = new Contour[Mathf.Max(contourCapacity, 1)];
	}

	public void Clear()
	{
		PointCount = 0;
		ContourCount = 0;
	}

	public void EnsurePointCapacity(int total)
	{
		if (total <= Points.Length)
		{
			return;
		}

		int size = Points.Length;
		while (size < total)
		{
			size *= 2;
		}

		Array.Resize(ref Points, size);
	}

	public void EnsureContourCapacity(int total)
	{
		if (total <= Contours.Length)
		{
			return;
		}

		int size = Contours.Length;
		while (size < total)
		{
			size *= 2;
		}

		Array.Resize(ref Contours, size);
	}

	public void AddPoint(Vector3 point)
	{
		EnsurePointCapacity(PointCount + 1);
		Points[PointCount++] = point;
	}

	public void AddContour(int start, int count, bool closed)
	{
		EnsureContourCapacity(ContourCount + 1);
		Contours[ContourCount++] = new Contour(start, count, closed);
	}

	/// <summary>Opens a contour at the current point count; pair with CloseContour.</summary>
	public int BeginContour()
	{
		return PointCount;
	}

	/// <summary>Records everything added since BeginContour as one contour. No-op if empty.</summary>
	public void EndContour(int start, bool closed)
	{
		int count = PointCount - start;
		if (count <= 0)
		{
			return;
		}

		AddContour(start, count, closed);
	}

	public ReadOnlySpan<Vector3> Span(int contourIndex)
	{
		Contour contour = Contours[contourIndex];
		return Points.AsSpan(contour.Start, contour.Count);
	}

	public bool IsClosed(int contourIndex)
	{
		return Contours[contourIndex].Closed;
	}
}
