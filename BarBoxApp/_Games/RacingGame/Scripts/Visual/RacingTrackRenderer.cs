using System;
using System.Collections.Generic;
using BarBox.Core.Drawing;
using Godot;

namespace BarBox.Games.Racing;

/// <summary>
/// Retained-shape restyle of a loaded track. Reads geometry from the same Line2D/CollisionPolygon2D
/// nodes the game already uses for collision and validation, then hides them and draws the OP-1
/// look in their place — collision and validation stay bit-identical, since they read the source
/// nodes' structure, never their visibility.
///
/// Built once per track load via Setup. Nothing here changes per frame, so unlike
/// RacingHUDArcRenderer this carries no per-frame dirty-flag machinery — there is no dirty state
/// to track.
/// </summary>
[GlobalClass]
public sealed partial class RacingTrackRenderer : ShapeCanvas
{
	/// <summary>Segment length for kerb stripes, in the kerb's own local units (scaled like Width).
	/// KerbStripesMaterial drives its shader stripes by UV frequency, which has no direct canvas-unit
	/// equivalent to port — this targets roughly square segments against the kerb's own width.</summary>
	private const float KerbSegmentLengthLocal = 50f;

	/// <summary>2 cells span the trigger's width, so cellSize = width / 2; this is the depth in
	/// cells along the direction of travel, giving the doc's "2x8 cells" band.</summary>
	private const float StartLineDepthCells = 8f;

	private const float DefaultCheckpointStrokeWidthLocal = 6f;

	public void Setup(RacingTrackDefinition track)
	{
		if (track?.TrackLine == null)
		{
			return;
		}

		Line2D trackLine = track.TrackLine;
		RenderSurfaceAndEdges(track, trackLine);
		RenderKerbs(trackLine);
		RenderZones(track);
		RenderStartLineAndCheckpoints(track);
		RenderBarriers(trackLine);
	}

	private void RenderSurfaceAndEdges(RacingTrackDefinition track, Line2D trackLine)
	{
		Vector2[] points = BakePolyline(trackLine, out float scale);
		bool closed = trackLine.Closed;
		float width = trackLine.Width * scale;

		Build()
			.Polygon(points, closed)
			.Stroke(new StrokeStyle { Width = width, Color = Palette.Asphalt })
			.Commit();

		float halfWidth = width * 0.5f;

		var inner = new FlatPath();
		PolylineOffset.Offset(points, closed, -halfWidth, inner);
		Build().Polygon(inner.PointSpan, inner.Closed).Stroke(VectorStyles.HairLine).Commit();

		var outer = new FlatPath();
		PolylineOffset.Offset(points, closed, halfWidth, outer);
		Build().Polygon(outer.PointSpan, outer.Closed).Stroke(VectorStyles.EdgeLine).Commit();

		trackLine.Visible = false;
		HideLegacyEdgeLines(track);
	}

	/// <summary>
	/// Kerbs are plain Line2D polylines named KerbLine* under TrackLine, CircuitA only. Rendered
	/// as a striped stroke rather than a shader — no kerb *zones* exist in any track scene, so
	/// there is no polygon to clip against.
	/// </summary>
	private void RenderKerbs(Line2D trackLine)
	{
		foreach (Node child in trackLine.GetChildren())
		{
			if (child is not Line2D kerb)
			{
				continue;
			}

			string name = kerb.Name;
			if (!name.StartsWith("KerbLine", StringComparison.Ordinal))
			{
				continue;
			}

			Vector2[] points = BakePolyline(kerb, out float scale);
			float width = kerb.Width * scale;

			Build()
				.Polyline(points)
				.StripedStroke(width, KerbSegmentLengthLocal * scale, Palette.Red, Palette.White)
				.Commit();

			kerb.Visible = false;
		}
	}

	/// <summary>
	/// Boost/slowdown/frictionless zones: flat translucent fill (the zone's own authored color)
	/// plus a dashed outline, gated by the zone's own ShowVisual export — the same switch the
	/// legacy Polygon2D visual respected. The outline is deliberately opaque even though the
	/// fill is translucent: a translucent stroke reads darker where StrokeTessellator's
	/// inner-join overlap occurs on a sharp corner, so translucency stays fill-only.
	/// </summary>
	private void RenderZones(Node root)
	{
		foreach (RacingZone zone in FindZones(root))
		{
			Vector2[] localPolygon = zone.GetLocalPolygon();
			if (localPolygon.Length >= 3 && zone.ShowVisual)
			{
				Transform2D toCanvas = ToCanvas(zone);
				var points = new Vector2[localPolygon.Length];
				for (int i = 0; i < localPolygon.Length; i++)
				{
					points[i] = toCanvas * localPolygon[i];
				}

				Color fillColor = zone.ZoneColor;
				var outlineColor = new Color(fillColor.R, fillColor.G, fillColor.B, 1f);

				Build().Polygon(points, closed: true).Fill(fillColor).Commit();
				Build().Polygon(points, closed: true).Stroke(VectorStyles.DashedGuide(outlineColor)).Commit();
			}

			zone.HideVisual();
		}
	}

	private static List<RacingZone> FindZones(Node root)
	{
		var zones = new List<RacingZone>();
		foreach (Node child in root.GetChildren())
		{
			if (child is RacingZone zone)
			{
				zones.Add(zone);
			}

			zones.AddRange(FindZones(child));
		}

		return zones;
	}

	private void RenderStartLineAndCheckpoints(RacingTrackDefinition track)
	{
		RenderStartLine(track.StartLine);

		foreach (RacingLineTrigger checkpoint in track.CheckpointTriggers)
		{
			RenderCheckpointStroke(checkpoint);
		}

		// Every shipped track configures FinishLinePath identically to StartLinePath (a looping
		// track with no separate finish line), so the two properties return the same node —
		// rendering it again here would draw a checkpoint stroke over the checkered start line
		// and hijack its color hook. Only a track with a genuinely distinct finish trigger needs
		// this second call.
		if (track.FinishLine != track.StartLine)
		{
			RenderCheckpointStroke(track.FinishLine);
		}
	}

	/// <summary>
	/// Checkered band via PatternFill.Checker (a geometric clip against the trigger's local
	/// bounds, not a shader). Built in the trigger's own local space — unrotated/unscaled,
	/// matching the SegmentShape2D prefab's A=(0,+halfWidth)/B=(0,-halfWidth) convention, width
	/// along local Y and depth-of-travel along local X — so the checker grid stays axis-aligned
	/// with the track direction; the resulting vertices are baked into canvas space as a
	/// post-process, not the input polygon, since PatternFill's grid math needs to run unrotated.
	/// </summary>
	private void RenderStartLine(RacingLineTrigger startLine)
	{
		if (startLine == null)
		{
			return;
		}

		float localWidth = startLine.GetWidth();
		if (localWidth <= 0f)
		{
			return;
		}

		float cellSize = localWidth * 0.5f;
		float depth = cellSize * StartLineDepthCells;

		var subject = new[]
		{
			new Vector2(-depth * 0.5f, -localWidth * 0.5f),
			new Vector2(depth * 0.5f, -localWidth * 0.5f),
			new Vector2(depth * 0.5f, localWidth * 0.5f),
			new Vector2(-depth * 0.5f, localWidth * 0.5f),
		};

		var buffer = new VertexBuffer();
		PatternFill.Checker(subject, subject[0], cellSize, Palette.White, Palette.Ink, buffer);

		Transform2D toCanvas = ToCanvas(startLine);
		for (int i = 0; i < buffer.VertexCount; i++)
		{
			buffer.Points[i] = toCanvas * buffer.Points[i];
		}

		CommitMesh(buffer);

		if (startLine.VisualLine != null)
		{
			startLine.VisualLine.Visible = false;
		}
	}

	/// <summary>
	/// A plain colored stroke standing in for the checkpoint's hidden VisualLine, so its
	/// crossed/uncrossed/next-required color feedback stays visible. Subscribes to the
	/// VisualColorChanged hook so RacingCheckpointTrigger's existing color calls keep working.
	/// </summary>
	private void RenderCheckpointStroke(RacingLineTrigger trigger)
	{
		if (trigger == null)
		{
			return;
		}

		(Vector2 a, Vector2 b) = GetLocalEndpoints(trigger);
		if (a.IsEqualApprox(b))
		{
			return;
		}

		Transform2D toCanvas = ToCanvas(trigger);
		float scale = UniformScale(toCanvas, trigger.Name);
		float width = (trigger.VisualLine?.Width ?? DefaultCheckpointStrokeWidthLocal) * scale;

		var points = new[] { toCanvas * a, toCanvas * b };
		Shape stroke = Build()
			.Polyline(points)
			.Stroke(new StrokeStyle { Width = width, Color = trigger.GetColor() })
			.Commit();

		trigger.VisualColorChanged = color => stroke?.SetStrokeColor(color);

		if (trigger.VisualLine != null)
		{
			trigger.VisualLine.Visible = false;
		}
	}

	/// <summary>Mirrors GetWidth()'s own shape-detection fallback chain (same branch order) to recover the actual endpoints, not just their distance.</summary>
	private static (Vector2 A, Vector2 B) GetLocalEndpoints(RacingLineTrigger trigger)
	{
		if (trigger.CollisionShape?.Shape is RectangleShape2D rectShape)
		{
			float halfWidth = rectShape.Size.X * 0.5f;
			return (new Vector2(0f, halfWidth), new Vector2(0f, -halfWidth));
		}

		if (trigger.CollisionShape?.Shape is SegmentShape2D segmentShape)
		{
			return (segmentShape.A, segmentShape.B);
		}

		if (trigger.VisualLine != null && trigger.VisualLine.GetPointCount() >= 2)
		{
			return (trigger.VisualLine.GetPointPosition(0), trigger.VisualLine.GetPointPosition(1));
		}

		return (Vector2.Zero, Vector2.Zero);
	}

	/// <summary>
	/// Barriers are StaticBody2D children of TrackLine, each with a BarrierLine Line2D child
	/// (CircuitA: 5, one closed with its own local position offset; GoCartTrack: 9, some with
	/// multiple collision shapes but one BarrierLine polyline each; OvalTrack: none). Baked via
	/// the BarrierLine node's own transform, not its StaticBody2D parent's, since CircuitA's
	/// closed barrier's position offset lives on the BarrierLine itself.
	/// </summary>
	private void RenderBarriers(Line2D trackLine)
	{
		foreach (Node child in trackLine.GetChildren())
		{
			if (child is not StaticBody2D barrier)
			{
				continue;
			}

			var barrierLine = barrier.GetNodeOrNull<Line2D>("BarrierLine");
			if (barrierLine == null)
			{
				continue;
			}

			Vector2[] points = BakePolyline(barrierLine, out float scale);
			float width = barrierLine.Width * scale;

			Build()
				.Polygon(points, barrierLine.Closed)
				.Stroke(new StrokeStyle { Width = width, Color = Palette.EdgeGray })
				.Commit();

			barrierLine.Visible = false;
		}
	}

	/// <summary>
	/// InnerEdgeLine/OuterEdgeLine are separately-authored Line2D copies parented to the track
	/// root (not TrackLine), present on OvalTrack/GoCartTrack only — CircuitA has neither.
	/// </summary>
	private static void HideLegacyEdgeLines(RacingTrackDefinition track)
	{
		var inner = track.GetNodeOrNull<Line2D>("InnerEdgeLine");
		if (inner != null)
		{
			inner.Visible = false;
		}

		var outer = track.GetNodeOrNull<Line2D>("OuterEdgeLine");
		if (outer != null)
		{
			outer.Visible = false;
		}
	}

	/// <summary>
	/// Bakes a source node's transform relative to this canvas. Rigid-only Shape.SetTransform is
	/// the wrong tool here — every track's transform has non-1.0 scale, which SetTransform bakes
	/// after tessellation (scaling stroke width and feather with it). Points are transformed
	/// before reaching the builder instead.
	/// </summary>
	private Transform2D ToCanvas(Node2D source)
	{
		return GlobalTransform.AffineInverse() * source.GlobalTransform;
	}

	/// <summary>Bakes every point of a Line2D into canvas space in one pass. Shared by every Render* method that draws a plain polyline/polygon from a Line2D source.</summary>
	private Vector2[] BakePolyline(Line2D line, out float scale)
	{
		Transform2D toCanvas = ToCanvas(line);
		scale = UniformScale(toCanvas, line.Name);

		int pointCount = line.GetPointCount();
		var points = new Vector2[pointCount];
		for (int i = 0; i < pointCount; i++)
		{
			points[i] = toCanvas * line.GetPointPosition(i);
		}

		return points;
	}

	/// <summary>
	/// Widths live in the source node's own local space, not canvas space, and need the same
	/// bake as its points — missing this is the likeliest way the asphalt band renders a hair off
	/// from TrackLine's actual collision width.
	/// </summary>
	private static float UniformScale(Transform2D toCanvas, string context)
	{
		Vector2 scale = toCanvas.Scale;
		float max = Mathf.Max(Mathf.Abs(scale.X), Mathf.Abs(scale.Y));
		if (max > 0f && Mathf.Abs(Mathf.Abs(scale.X) - Mathf.Abs(scale.Y)) > 0.01f * max)
		{
			GD.PushWarning($"RacingTrackRenderer: non-uniform scale baking {context}; width will be approximate.");
		}

		return scale.X;
	}
}
