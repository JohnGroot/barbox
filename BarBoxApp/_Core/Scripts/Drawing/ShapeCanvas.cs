using System;
using System.Collections.Generic;
using Godot;

namespace BarBox.Core.Drawing;

/// <summary>
/// Retained canvas for tessellated vector shapes. Commit shapes through Build(); the canvas
/// re-tessellates only what changed and uploads each bucket as a single triangle array.
///
/// Bucket model, and why it is split this way: canvas_item_clear frees a command's GPU vertex
/// and index buffers, so re-issuing an item re-creates them. A shape that dirties every frame
/// must therefore not share an item with heavy static geometry. Static shapes live on the node's
/// own canvas item and rebuild through _Draw; Dynamic() shapes and material shapes each live on
/// a child item that is cleared and re-uploaded from _Process, leaving the node's item — and so
/// the static GPU buffers — untouched.
///
/// The corollary is that only static dirt may call QueueRedraw: a redraw clears the node's item,
/// which forces the static bucket back onto the GPU. Routing per-frame dirt through QueueRedraw
/// would make the bucket split buy nothing.
///
/// Child items inherit the node's transform and always draw above its own commands, so SortKey
/// orders shapes within a bucket only. Anything that must interleave with external nodes gets
/// its own ShapeCanvas and ZIndex.
/// </summary>
[GlobalClass]
public partial class ShapeCanvas : Node2D
{
	private readonly ShapeBucket _static = new();
	private readonly List<ShapeBucket> _materialBuckets = [];
	private readonly DashResult _dashScratch = new();

	private ShapeBucket _dynamic;
	private ShapeBuilder _builder;

	private float _pixelScale = 1f;
	private int _nextSeq;
	private bool _childFlushPending;
	private bool _sizeChangedConnected;
	private bool _warnedNonUniform;

	/// <summary>
	/// Canvas units to screen pixels, which is what makes the feather land at a fixed pixel
	/// width. Derived from GetScreenTransform (parent scales plus the canvas_items stretch)
	/// unless set explicitly, which pins it and disables auto-derivation.
	/// </summary>
	public float PixelScale
	{
		get => _pixelScale;
		set
		{
			// Only a value the canvas can actually honour pins auto-derivation off. Rejecting the
			// value but disabling auto anyway would strand the canvas at a stale scale for good.
			if (float.IsFinite(value) && value > 0f)
			{
				AutoPixelScale = false;
			}

			ApplyPixelScale(value);
		}
	}

	public bool AutoPixelScale { get; set; } = true;

	public int ShapeCount
	{
		get
		{
			int count = _static.Shapes.Count + (_dynamic?.Shapes.Count ?? 0);
			foreach (ShapeBucket bucket in _materialBuckets)
			{
				count += bucket.Shapes.Count;
			}

			return count;
		}
	}

	public int TriangleCount
	{
		get
		{
			int count = _static.Concat.IndexCount / 3;
			count += (_dynamic?.Concat.IndexCount ?? 0) / 3;
			foreach (ShapeBucket bucket in _materialBuckets)
			{
				count += bucket.Concat.IndexCount / 3;
			}

			return count;
		}
	}

	/// <summary>Canvas items in use: the node's own plus one per child bucket.</summary>
	public int CanvasItemCount => 1 + (_dynamic == null ? 0 : 1) + _materialBuckets.Count;

	public ShapeBuilder Build()
	{
		_builder ??= new ShapeBuilder(this);
		_builder.Begin();
		return _builder;
	}

	/// <summary>
	/// Commits pre-tessellated triangles directly, bypassing the fluent builder — for content
	/// whose per-vertex color a single StrokeStyle/FillStyle can't express (see CheckerFill).
	/// The source buffer is copied, not referenced, so the caller's scratch buffer is free to
	/// reuse immediately.
	/// </summary>
	public Shape CommitMesh(VertexBuffer source, int sortKey = 0)
	{
		var shape = new Shape
		{
			Kind = ShapeKind.Mesh,
			SortKey = sortKey,
		};

		Register(shape);
		shape.SetMesh(source);
		return shape;
	}

	/// <summary>Checkerboard fill clipped to subject — the checkered start line. See PatternFill.</summary>
	public Shape CheckerFill(
		ReadOnlySpan<Vector2> subject,
		Vector2 origin,
		float cellSize,
		Color colorA,
		Color colorB,
		int sortKey = 0)
	{
		var scratch = new VertexBuffer();
		PatternFill.Checker(subject, origin, cellSize, colorA, colorB, scratch);
		return CommitMesh(scratch, sortKey);
	}

	/// <summary>Angled two-color band fill clipped to subject — kerb-style striping. See PatternFill.</summary>
	public Shape StripesFill(
		ReadOnlySpan<Vector2> subject,
		float angleRad,
		float bandWidth,
		Color colorA,
		Color colorB,
		int sortKey = 0)
	{
		var scratch = new VertexBuffer();
		PatternFill.Stripes(subject, angleRad, bandWidth, colorA, colorB, scratch);
		return CommitMesh(scratch, sortKey);
	}

	public void Remove(Shape shape)
	{
		if (shape == null || shape.Canvas != this)
		{
			return;
		}

		ShapeBucket bucket = shape.Bucket;
		bucket?.Remove(shape);
		shape.Canvas = null;
		shape.Bucket = null;

		// Routed like MarkDirty rather than doing both: redrawing for a removal from a child bucket
		// would re-upload the static geometry, which is the cost the buckets exist to avoid.
		if (bucket != null && bucket.IsChild)
		{
			RequestChildFlush();
		}
		else
		{
			QueueRedraw();
		}
	}

	public void Clear()
	{
		foreach (Shape shape in _static.Shapes)
		{
			shape.Canvas = null;
			shape.Bucket = null;
		}

		_static.Shapes.Clear();
		_static.ConcatDirty = true;

		if (_dynamic != null)
		{
			foreach (Shape shape in _dynamic.Shapes)
			{
				shape.Canvas = null;
				shape.Bucket = null;
			}

			_dynamic.Shapes.Clear();
			_dynamic.ConcatDirty = true;
		}

		foreach (ShapeBucket bucket in _materialBuckets)
		{
			foreach (Shape shape in bucket.Shapes)
			{
				shape.Canvas = null;
				shape.Bucket = null;
			}

			bucket.Shapes.Clear();
			bucket.ConcatDirty = true;
		}

		QueueRedraw();
		RequestChildFlush();
	}

	public override void _Ready()
	{
		// Parent scale is not known until the node is in the tree, and the racing renderers are
		// added at runtime under roots scaled 6x, so the feather would be sized wrong without this.
		SetNotifyTransform(true);

		// Idle by default, but never cancel a flush already queued by shapes committed before the
		// canvas entered the tree — those would otherwise never reach their child item.
		SetProcess(_childFlushPending);

		RefreshPixelScale();
		ConnectSizeChanged();
	}

	public override void _EnterTree()
	{
		ConnectSizeChanged();
	}

	public override void _ExitTree()
	{
		DisconnectSizeChanged();
	}

	public override void _Draw()
	{
		if (_static.ConcatDirty)
		{
			_static.Rebuild(_pixelScale, _dashScratch);
		}

		// Unconditional: the engine cleared this item before calling us, and _Draw also fires on
		// entering the canvas and on visibility changes, not only on our own QueueRedraw.
		Upload(GetCanvasItem(), _static.Concat);
	}

	public override void _Process(double delta)
	{
		// Processing stays enabled once a child bucket exists, and this early-out is the whole
		// idle cost. Toggling SetProcess off after each flush halves the update rate instead of
		// saving anything: the re-enable cannot take effect until the following frame, so a
		// per-frame dirty shape would only reach the GPU every other frame.
		if (!_childFlushPending)
		{
			return;
		}

		FlushChild(_dynamic);
		foreach (ShapeBucket bucket in _materialBuckets)
		{
			FlushChild(bucket);
		}

		_childFlushPending = false;
	}

	public override void _Notification(int what)
	{
		switch ((long)what)
		{
			case NotificationTransformChanged:
				RefreshPixelScale();
				break;
			case NotificationPredelete:
				FreeChildItems();
				break;
		}
	}

	internal void Register(Shape shape)
	{
		shape.Canvas = this;
		shape.CommitSeq = _nextSeq++;

		ShapeBucket bucket = ResolveBucket(shape);
		shape.Bucket = bucket;
		bucket.Add(shape);

		MarkDirty(shape, DirtyLevel.Flatten);
	}

	/// <summary>
	/// The one place dirt turns into work. Static dirt redraws the node's item; child-bucket dirt
	/// must not, or the static geometry re-uploads with it.
	/// </summary>
	internal void MarkDirty(Shape shape, DirtyLevel level)
	{
		if (level > shape.Dirty)
		{
			shape.Dirty = level;
		}

		ShapeBucket bucket = shape.Bucket;
		if (bucket == null)
		{
			return;
		}

		bucket.ConcatDirty = true;

		if (bucket.IsChild)
		{
			RequestChildFlush();
		}
		else
		{
			QueueRedraw();
		}
	}

	internal void MarkOrderDirty(Shape shape)
	{
		if (shape.Bucket != null)
		{
			shape.Bucket.OrderDirty = true;
		}
	}

	internal ShapeBucket StaticBucket => _static;

	internal ShapeBucket DynamicBucket => _dynamic;

	internal IReadOnlyList<ShapeBucket> MaterialBuckets => _materialBuckets;

	/// <summary>
	/// Rebuilds every dirty bucket without touching RenderingServer. Tests drive this instead of
	/// waiting on _Draw, whose timing under the headless driver is a dependency with no payoff.
	/// </summary>
	internal void RebuildBuckets()
	{
		if (_static.ConcatDirty)
		{
			_static.Rebuild(_pixelScale, _dashScratch);
		}

		if (_dynamic != null && _dynamic.ConcatDirty)
		{
			_dynamic.Rebuild(_pixelScale, _dashScratch);
		}

		foreach (ShapeBucket bucket in _materialBuckets)
		{
			if (bucket.ConcatDirty)
			{
				bucket.Rebuild(_pixelScale, _dashScratch);
			}
		}
	}

	internal void FreeChildItemsForTest()
	{
		FreeChildItems();
	}

	/// <summary>
	/// Wakes _Process only when there is a child item to flush, so a canvas of purely static
	/// shapes never pays for a per-frame callback.
	/// </summary>
	private void RequestChildFlush()
	{
		if (_dynamic == null && _materialBuckets.Count == 0)
		{
			return;
		}

		_childFlushPending = true;
		SetProcess(true);
	}

	private ShapeBucket ResolveBucket(Shape shape)
	{
		if (shape.Material != null)
		{
			foreach (ShapeBucket existing in _materialBuckets)
			{
				if (ReferenceEquals(existing.Material, shape.Material))
				{
					return existing;
				}
			}

			var bucket = new ShapeBucket { Material = shape.Material, Item = CreateChildItem(shape.Material) };
			_materialBuckets.Add(bucket);
			return bucket;
		}

		if (shape.IsDynamic)
		{
			_dynamic ??= new ShapeBucket { IsDynamic = true, Item = CreateChildItem(null) };
			return _dynamic;
		}

		return _static;
	}

	private Rid CreateChildItem(ShaderMaterial material)
	{
		Rid item = RenderingServer.CanvasItemCreate();
		RenderingServer.CanvasItemSetParent(item, GetCanvasItem());

		if (material != null)
		{
			RenderingServer.CanvasItemSetMaterial(item, material.GetRid());
		}

		return item;
	}

	private void FlushChild(ShapeBucket bucket)
	{
		if (bucket == null || !bucket.ConcatDirty || !bucket.Item.IsValid)
		{
			return;
		}

		bucket.Rebuild(_pixelScale, _dashScratch);
		RenderingServer.CanvasItemClear(bucket.Item);
		Upload(bucket.Item, bucket.Concat);
	}

	private static void Upload(Rid item, VertexBuffer buffer)
	{
		if (buffer.IsEmpty)
		{
			return;
		}

		// Colors is exactly as long as Points by construction, satisfying the engine's "1 or N"
		// rule, and IndexCount is a multiple of 3 because only AddTriangle ever writes it.
		//
		// Every trailing argument is passed explicitly because the ReadOnlySpan overload declares
		// no defaults: omitting them binds to the Array overload instead, which would copy each
		// buffer onto the heap on every upload and undo the whole pooling design.
		RenderingServer.CanvasItemAddTriangleArray(
			item,
			buffer.IndexSpan,
			buffer.PointSpan,
			buffer.ColorSpan,
			default,
			default,
			default,
			default,
			-1);
	}

	private void RefreshPixelScale()
	{
		if (!AutoPixelScale || !IsInsideTree())
		{
			return;
		}

		Vector2 scale = GetScreenTransform().Scale;
		float max = Mathf.Max(Mathf.Abs(scale.X), Mathf.Abs(scale.Y));

		if (!_warnedNonUniform && max > 0f && Mathf.Abs(Mathf.Abs(scale.X) - Mathf.Abs(scale.Y)) > 0.01f * max)
		{
			_warnedNonUniform = true;
			GD.PushWarning($"ShapeCanvas '{Name}': non-uniform scale {scale}; feather width will be approximate.");
		}

		ApplyPixelScale(Mathf.Abs(scale.X));
	}

	private void ApplyPixelScale(float value)
	{
		if (!(value > 0f) || !float.IsFinite(value) || Mathf.IsEqualApprox(value, _pixelScale))
		{
			return;
		}

		_pixelScale = value;

		// Flatten, not merely Tess: both the feather width AND the flattening tolerance are derived
		// from PixelScale (Shape.Flatten divides by it), so re-tessellating alone would leave every
		// curve at its old segment density and a resize would turn circles into visible polygons.
		// Only an actual scale change reaches here — a canvas that merely moves compares equal.
		_static.MarkAll(DirtyLevel.Flatten);
		_dynamic?.MarkAll(DirtyLevel.Flatten);
		foreach (ShapeBucket bucket in _materialBuckets)
		{
			bucket.MarkAll(DirtyLevel.Flatten);
		}

		QueueRedraw();
		RequestChildFlush();
	}

	private void ConnectSizeChanged()
	{
		if (_sizeChangedConnected || !IsInsideTree())
		{
			return;
		}

		GetTree().Root.SizeChanged += RefreshPixelScale;
		_sizeChangedConnected = true;
	}

	private void DisconnectSizeChanged()
	{
		if (!_sizeChangedConnected)
		{
			return;
		}

		// Root outlives this node, so a connection left behind would keep it referenced. Gate on
		// the tree being reachable rather than on IsInsideTree, which is exactly what is changing.
		SceneTree tree = GetTree();
		if (tree != null)
		{
			tree.Root.SizeChanged -= RefreshPixelScale;
		}

		_sizeChangedConnected = false;
	}

	/// <summary>
	/// Freed at predelete rather than _ExitTree: a node can leave and re-enter the tree, and
	/// freeing there would leave the buckets pointing at dead RIDs.
	/// </summary>
	private void FreeChildItems()
	{
		if (_dynamic != null && _dynamic.Item.IsValid)
		{
			RenderingServer.FreeRid(_dynamic.Item);
			_dynamic.Item = default;
		}

		foreach (ShapeBucket bucket in _materialBuckets)
		{
			if (bucket.Item.IsValid)
			{
				RenderingServer.FreeRid(bucket.Item);
				bucket.Item = default;
			}
		}
	}
}
