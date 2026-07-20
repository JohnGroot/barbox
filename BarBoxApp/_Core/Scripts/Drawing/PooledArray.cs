using System;
using Godot;

namespace BarBox.Core.Drawing;

/// <summary>
/// Shared doubling-growth policy for this module's several pooled arrays (FlatPath's point/T
/// buffers, Shape's contour array and trim scratch, PathBuilder's contour array) — one place to
/// change the growth factor or floor instead of several hand-copies.
/// </summary>
internal static class PooledArray
{
	public static void EnsureCapacity<T>(ref T[] array, int required)
	{
		if (array.Length >= required)
		{
			return;
		}

		int size = Mathf.Max(array.Length, 1);
		while (size < required)
		{
			size *= 2;
		}

		Array.Resize(ref array, size);
	}
}
