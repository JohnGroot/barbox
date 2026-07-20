using System;
using System.Collections.Generic;
using Godot;

namespace BarBox.Core.Drawing;

/// <summary>
/// A set of shapes that upload as one triangle array, plus the concatenated buffer they upload
/// from. Deliberately free of RenderingServer: everything that can be wrong about ordering,
/// dirty propagation, or buffer management is testable here without a GPU.
///
/// Item is the canvas item the bucket uploads to. It is invalid for the static bucket, which
/// draws to the node's own item inside _Draw; dynamic and material buckets own a child item.
/// </summary>
internal sealed class ShapeBucket
{
	/// <summary>
	/// Draw order. Equal sort keys fall back to commit order, which makes List.Sort's
	/// instability irrelevant and keeps the default painter's-algorithm order the plan specifies.
	/// </summary>
	private static readonly Comparison<Shape> ByKeyThenSeq = static (a, b) =>
	{
		int keys = a.SortKey.CompareTo(b.SortKey);
		return keys != 0 ? keys : a.CommitSeq.CompareTo(b.CommitSeq);
	};

	public readonly List<Shape> Shapes = [];
	public readonly VertexBuffer Concat = new(512, 1024);

	public Rid Item;
	public ShaderMaterial Material;
	public bool IsDynamic;

	public bool ConcatDirty = true;
	public bool OrderDirty;

	public bool IsChild => Item.IsValid;

	public void Add(Shape shape)
	{
		Shapes.Add(shape);
		ConcatDirty = true;
		OrderDirty = true;
	}

	public bool Remove(Shape shape)
	{
		if (!Shapes.Remove(shape))
		{
			return false;
		}

		ConcatDirty = true;
		return true;
	}

	/// <summary>
	/// Re-tessellates every shape that needs it, then rebuilds the concatenated buffer. Shapes
	/// dirty only at Concat level skip tessellation entirely and are simply re-appended, which
	/// is what makes a transform or visibility change cheap.
	/// </summary>
	public void Rebuild(float pixelScale, DashResult dashScratch)
	{
		if (OrderDirty)
		{
			Shapes.Sort(ByKeyThenSeq);
			OrderDirty = false;
		}

		foreach (Shape shape in Shapes)
		{
			if (shape.Dirty >= DirtyLevel.Tess)
			{
				shape.Rebuild(pixelScale, dashScratch);
			}
			else if (shape.Dirty == DirtyLevel.Recolor)
			{
				shape.Recolor(pixelScale, dashScratch);
			}

			shape.Dirty = DirtyLevel.None;
		}

		Concat.Clear();

		foreach (Shape shape in Shapes)
		{
			if (!shape.Visible || shape.Buffer.IsEmpty)
			{
				continue;
			}

			if (shape.HasTransform)
			{
				Concat.Append(shape.Buffer, shape.Transform);
			}
			else
			{
				Concat.Append(shape.Buffer);
			}
		}

		ConcatDirty = false;
	}

	/// <summary>
	/// Raises every shape to at least the given level, e.g. after PixelScale changed. Skips
	/// Path-kind shapes for a Flatten-level raise: their curve resolution is fixed at authoring
	/// time (no PixelScale to derive tolerance from — see ShapeKind.Path), so a resize buys them
	/// nothing, and forcing the re-copy would needlessly re-read their source PathBuilder, which
	/// a caller may have reused for a different shape since (SetPath is Path's own, deliberate
	/// dirty-raiser for that).
	/// </summary>
	public void MarkAll(DirtyLevel level)
	{
		foreach (Shape shape in Shapes)
		{
			if (level >= DirtyLevel.Flatten && shape.Kind == ShapeKind.Path)
			{
				continue;
			}

			if (shape.Dirty < level)
			{
				shape.Dirty = level;
			}
		}

		ConcatDirty = true;
	}
}
