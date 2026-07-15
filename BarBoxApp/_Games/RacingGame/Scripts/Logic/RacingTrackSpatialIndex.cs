using System.Collections.Generic;
using Godot;

namespace BarBox.Games.Racing;

/// <summary>
/// Uniform-grid spatial acceleration structure over a track's line segments, in
/// the track line's local coordinate space. Answers the on/off-track range query
/// "is any segment within half-width of this point?" by examining only the segments
/// registered to the query point's grid cell, instead of scanning every segment.
///
/// Correctness guarantee: each segment is registered into every cell its
/// half-width-expanded axis-aligned bounding box overlaps. If a point lies within
/// half-width of a segment, that point necessarily falls inside the segment's
/// expanded AABB, and therefore inside one of the cells the segment was registered
/// to — the cell containing the point. So examining only the point's own cell
/// considers every segment that could classify it as on-track. Applying the same
/// per-segment distance test as the full scan then yields a bit-identical result.
///
/// This is an additive query path built alongside the existing full-scan
/// (LineUtils.IsPointNearCachedLine); it does not replace it.
/// </summary>
public sealed class RacingTrackSpatialIndex
{
	// Segment endpoints (flattened, includes the closing segment when the line is closed)
	private Vector2[] _segStart;
	private Vector2[] _segEnd;

	private float _halfWidthSq;

	// Grid geometry (local space)
	private Vector2 _origin;
	private float _cellSize;
	private int _cols;
	private int _rows;

	// Per-cell segment index lists, row-major (_cols * _rows). Null entry = empty cell.
	private int[][] _cells;

	private bool _built;

	// Cap on total cell count to bound memory for pathological bounds/width ratios.
	private const int MaxCells = 1 << 20;

	public bool IsBuilt => _built;

	/// <summary>
	/// Build the index over line points in the track line's local space.
	/// Mirrors LineUtils.IsPointNearCachedLine's segment set: consecutive points plus
	/// a closing segment (last to first) when <paramref name="closed"/> and 3+ points.
	/// </summary>
	public void Build(Vector2[] points, float halfWidth, bool closed)
	{
		_built = false;
		_cells = null;
		_segStart = null;
		_segEnd = null;

		if (points == null || points.Length < 2 || halfWidth <= 0f)
			return;

		_halfWidthSq = halfWidth * halfWidth;

		bool hasClosing = closed && points.Length >= 3;
		int segCount = (points.Length - 1) + (hasClosing ? 1 : 0);

		_segStart = new Vector2[segCount];
		_segEnd = new Vector2[segCount];
		for (int i = 0; i < points.Length - 1; i++)
		{
			_segStart[i] = points[i];
			_segEnd[i] = points[i + 1];
		}
		if (hasClosing)
		{
			_segStart[segCount - 1] = points[points.Length - 1];
			_segEnd[segCount - 1] = points[0];
		}

		// Grid bounds = point bounds expanded by half-width on every side, so that any
		// point within half-width of any segment lies inside the grid.
		Vector2 min = points[0];
		Vector2 max = points[0];
		for (int i = 1; i < points.Length; i++)
		{
			min = new Vector2(Mathf.Min(min.X, points[i].X), Mathf.Min(min.Y, points[i].Y));
			max = new Vector2(Mathf.Max(max.X, points[i].X), Mathf.Max(max.Y, points[i].Y));
		}
		_origin = min - new Vector2(halfWidth, halfWidth);
		Vector2 gridMax = max + new Vector2(halfWidth, halfWidth);
		Vector2 extent = gridMax - _origin;

		// Cell size: large enough that the grid stays small, but tied to feature scale.
		// Average segment length keeps a handful of segments per cell on typical tracks;
		// 2 * half-width guards against cells smaller than the on-track band.
		float totalLen = 0f;
		for (int i = 0; i < segCount; i++)
			totalLen += _segStart[i].DistanceTo(_segEnd[i]);
		float avgSegLen = segCount > 0 ? totalLen / segCount : 0f;
		_cellSize = Mathf.Max(2f * halfWidth, avgSegLen);
		if (_cellSize <= 0.0001f)
			_cellSize = 0.0001f;

		_cols = Mathf.Max(1, Mathf.CeilToInt(extent.X / _cellSize));
		_rows = Mathf.Max(1, Mathf.CeilToInt(extent.Y / _cellSize));

		// If the grid would be pathologically large, coarsen the cell size to fit the cap.
		long cellCount = (long)_cols * _rows;
		if (cellCount > MaxCells)
		{
			float scale = Mathf.Sqrt((float)cellCount / MaxCells);
			_cellSize *= scale;
			_cols = Mathf.Max(1, Mathf.CeilToInt(extent.X / _cellSize));
			_rows = Mathf.Max(1, Mathf.CeilToInt(extent.Y / _cellSize));
		}

		var buckets = new List<int>[_cols * _rows];
		for (int s = 0; s < segCount; s++)
		{
			Vector2 a = _segStart[s];
			Vector2 b = _segEnd[s];

			// Segment AABB expanded by half-width, in cell coordinates.
			float sMinX = Mathf.Min(a.X, b.X) - halfWidth;
			float sMaxX = Mathf.Max(a.X, b.X) + halfWidth;
			float sMinY = Mathf.Min(a.Y, b.Y) - halfWidth;
			float sMaxY = Mathf.Max(a.Y, b.Y) + halfWidth;

			int cx0 = Mathf.Clamp(Mathf.FloorToInt((sMinX - _origin.X) / _cellSize), 0, _cols - 1);
			int cx1 = Mathf.Clamp(Mathf.FloorToInt((sMaxX - _origin.X) / _cellSize), 0, _cols - 1);
			int cy0 = Mathf.Clamp(Mathf.FloorToInt((sMinY - _origin.Y) / _cellSize), 0, _rows - 1);
			int cy1 = Mathf.Clamp(Mathf.FloorToInt((sMaxY - _origin.Y) / _cellSize), 0, _rows - 1);

			for (int cy = cy0; cy <= cy1; cy++)
			{
				int rowBase = cy * _cols;
				for (int cx = cx0; cx <= cx1; cx++)
				{
					int idx = rowBase + cx;
					(buckets[idx] ??= new List<int>()).Add(s);
				}
			}
		}

		_cells = new int[_cols * _rows][];
		for (int i = 0; i < buckets.Length; i++)
			_cells[i] = buckets[i]?.ToArray();

		_built = true;
	}

	/// <summary>
	/// True if <paramref name="localPoint"/> (track-line local space) is within half-width
	/// of any track segment. Bit-identical to LineUtils.IsPointNearCachedLine for the same
	/// point set, width, and closed flag.
	/// </summary>
	public bool IsPointNear(Vector2 localPoint)
	{
		if (!_built)
			return false;

		int cx = Mathf.FloorToInt((localPoint.X - _origin.X) / _cellSize);
		int cy = Mathf.FloorToInt((localPoint.Y - _origin.Y) / _cellSize);

		// A point outside the grid is beyond half-width of every segment (grid bounds
		// were expanded by half-width), so it is off-track.
		if (cx < 0 || cx >= _cols || cy < 0 || cy >= _rows)
			return false;

		int[] cell = _cells[cy * _cols + cx];
		if (cell == null)
			return false;

		for (int i = 0; i < cell.Length; i++)
		{
			int s = cell[i];
			var closest = LineUtils.GetClosestPointOnSegment(localPoint, _segStart[s], _segEnd[s]);
			if (localPoint.DistanceSquaredTo(closest) <= _halfWidthSq)
				return true;
		}

		return false;
	}
}
