using System;
using Godot;

namespace BarBox.Core.Drawing;

/// <summary>
/// Fluent front end for committing shapes to a canvas. One instance per ShapeCanvas, reset and
/// handed back by Build(), so a build chain allocates nothing but the Shape itself.
///
/// Because it is pooled, every field must be cleared by Reset — a second Build() inheriting the
/// first's style is the failure mode this design trades allocation for.
/// </summary>
public sealed class ShapeBuilder
{
	private readonly ShapeCanvas _canvas;

	private bool _open;
	private bool _hasGeometry;

	private ShapeKind _kind;
	private Vector2 _center;
	private float _radius;
	private float _startRad;
	private float _endRad;
	private Rect2 _rect;
	private float _cornerRadius;
	private Vector2 _p0;
	private Vector2 _c0;
	private Vector2 _c1;
	private Vector2 _p1;
	private Vector2[] _points;
	private int _pointCount;
	private bool _closed;
	private Contour3Set _contours3;
	private Projector _projector;

	private StrokeStyle _stroke;
	private bool _hasStroke;
	private FillStyle _fill;
	private bool _hasFill;

	private bool _dynamic;
	private int _sortKey;
	private float _tolerancePx = PathFlattener.DefaultTolerance;
	private bool _hidden;
	private ShaderMaterial _material;

	internal ShapeBuilder(ShapeCanvas canvas)
	{
		_canvas = canvas;
	}

	internal void Begin()
	{
		if (_open)
		{
			GD.PushWarning("ShapeBuilder: a previous Build() was never committed; discarding it.");
		}

		Reset();
		_open = true;
	}

	public ShapeBuilder Polyline(ReadOnlySpan<Vector2> points)
	{
		_kind = ShapeKind.Polyline;
		StorePoints(points);
		_closed = false;
		_hasGeometry = true;
		return this;
	}

	public ShapeBuilder Polygon(ReadOnlySpan<Vector2> points, bool closed = true)
	{
		_kind = ShapeKind.Polygon;
		StorePoints(points);
		_closed = closed;
		_hasGeometry = true;
		return this;
	}

	public ShapeBuilder Circle(Vector2 center, float radius)
	{
		_kind = ShapeKind.Circle;
		_center = center;
		_radius = radius;
		_hasGeometry = true;
		return this;
	}

	public ShapeBuilder Arc(Vector2 center, float radius, float startRad, float endRad)
	{
		_kind = ShapeKind.Arc;
		_center = center;
		_radius = radius;
		_startRad = startRad;
		_endRad = endRad;
		_hasGeometry = true;
		return this;
	}

	public ShapeBuilder RoundedRect(Rect2 rect, float radius)
	{
		_kind = ShapeKind.RoundedRect;
		_rect = rect;
		_cornerRadius = radius;
		_hasGeometry = true;
		return this;
	}

	public ShapeBuilder Rect(Rect2 rect)
	{
		return RoundedRect(rect, 0f);
	}

	public ShapeBuilder CubicBezier(Vector2 p0, Vector2 c0, Vector2 c1, Vector2 p1)
	{
		_kind = ShapeKind.CubicBezier;
		_p0 = p0;
		_c0 = c0;
		_c1 = c1;
		_p1 = p1;
		_hasGeometry = true;
		return this;
	}

	public ShapeBuilder QuadBezier(Vector2 p0, Vector2 c, Vector2 p1)
	{
		_kind = ShapeKind.QuadBezier;
		_p0 = p0;
		_c0 = c;
		_p1 = p1;
		_hasGeometry = true;
		return this;
	}

	public ShapeBuilder Path3(ReadOnlySpan<Vector3> points, in Projector projector, bool closed = false)
	{
		var set = new Contour3Set(Mathf.Max(points.Length, 4), 1);
		for (int i = 0; i < points.Length; i++)
		{
			set.AddPoint(points[i]);
		}

		set.AddContour(0, points.Length, closed);
		return Path3(set, projector);
	}

	/// <summary>Multi-contour faux-3D: the set is referenced, not copied. See Wireframes.</summary>
	public ShapeBuilder Path3(Contour3Set contours, in Projector projector)
	{
		_kind = ShapeKind.Path3;
		_contours3 = contours;
		_projector = projector;
		_hasGeometry = true;
		return this;
	}

	public ShapeBuilder Stroke(StrokeStyle style)
	{
		_stroke = style;
		_hasStroke = true;
		return this;
	}

	/// <summary>
	/// Alternating-color stroke with no gaps — the racing kerbs. Sugar over a Striped dash, so
	/// it needs no tessellator support of its own.
	/// </summary>
	public ShapeBuilder StripedStroke(float width, float segmentLength, Color colorA, Color colorB)
	{
		return Stroke(new StrokeStyle
		{
			Width = width,
			Color = colorA,
			DashColorB = colorB,
			DashPattern = [segmentLength],
			DashMode = DashMode.Striped,
			Cap = CapMode.Butt,
		});
	}

	public ShapeBuilder Fill(FillStyle style)
	{
		_fill = style;
		_hasFill = true;
		return this;
	}

	public ShapeBuilder Fill(Color color)
	{
		return Fill(new FillStyle { Color = color });
	}

	/// <summary>
	/// Routes to the per-frame child item, so per-frame dirt never re-uploads the static
	/// geometry. Note child items always draw above the static bucket regardless of SortKey —
	/// anything needing to interleave with static shapes wants its own ShapeCanvas and ZIndex.
	/// </summary>
	public ShapeBuilder Dynamic()
	{
		_dynamic = true;
		return this;
	}

	/// <summary>Draw order within this shape's bucket only. Default is commit order.</summary>
	public ShapeBuilder SortKey(int key)
	{
		_sortKey = key;
		return this;
	}

	/// <summary>Max chord deviation in screen pixels.</summary>
	public ShapeBuilder Tolerance(float tolerancePx)
	{
		_tolerancePx = tolerancePx;
		return this;
	}

	public ShapeBuilder Hidden()
	{
		_hidden = true;
		return this;
	}

	/// <summary>Routes the shape to its own child item, out of the vertex-color batch.</summary>
	public ShapeBuilder WithMaterial(ShaderMaterial material)
	{
		_material = material;
		return this;
	}

	/// <summary>Returns null on rejection: no geometry, or neither a stroke nor a fill.</summary>
	public Shape Commit()
	{
		if (!_hasGeometry)
		{
			GD.PushWarning("ShapeBuilder.Commit: no geometry was set; nothing committed.");
			Reset();
			return null;
		}

		if (!_hasStroke && !_hasFill)
		{
			GD.PushWarning("ShapeBuilder.Commit: shape has neither a stroke nor a fill; nothing committed.");
			Reset();
			return null;
		}

		var shape = new Shape
		{
			Kind = _kind,
			Center = _center,
			Radius = _radius,
			StartRad = _startRad,
			EndRad = _endRad,
			Rect = _rect,
			CornerRadius = _cornerRadius,
			P0 = _p0,
			C0 = _c0,
			C1 = _c1,
			P1 = _p1,
			SourceClosed = _closed,
			Source3 = _contours3,
			Projector = _projector,
			TolerancePx = _tolerancePx,
			IsDynamic = _dynamic,
			SortKey = _sortKey,
			Material = _material,
			Visible = !_hidden,
		};

		if (_pointCount > 0)
		{
			shape.StorePoints(_points.AsSpan(0, _pointCount));
		}

		// Registration precedes styling so SetStroke's MarkDirty reaches the canvas, and so the
		// array copies happen through the same choke point a runtime SetStroke uses.
		_canvas.Register(shape);

		if (_hasFill)
		{
			shape.SetFill(_fill);
		}

		if (_hasStroke)
		{
			shape.SetStroke(_stroke);
		}

		Reset();
		return shape;
	}

	private void StorePoints(ReadOnlySpan<Vector2> points)
	{
		if (_points == null || _points.Length < points.Length)
		{
			_points = new Vector2[Mathf.Max(points.Length, 8)];
		}

		points.CopyTo(_points);
		_pointCount = points.Length;
	}

	private void Reset()
	{
		_open = false;
		_hasGeometry = false;
		_kind = ShapeKind.Polyline;
		_center = Vector2.Zero;
		_radius = 0f;
		_startRad = 0f;
		_endRad = 0f;
		_rect = default;
		_cornerRadius = 0f;
		_p0 = Vector2.Zero;
		_c0 = Vector2.Zero;
		_c1 = Vector2.Zero;
		_p1 = Vector2.Zero;
		_pointCount = 0;
		_closed = false;
		_contours3 = null;
		_projector = default;
		_stroke = default;
		_hasStroke = false;
		_fill = default;
		_hasFill = false;
		_dynamic = false;
		_sortKey = 0;
		_tolerancePx = PathFlattener.DefaultTolerance;
		_hidden = false;
		_material = null;
	}
}
