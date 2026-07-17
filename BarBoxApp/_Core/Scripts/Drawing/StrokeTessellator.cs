using System;
using System.Buffers;
using Godot;

namespace BarBox.Core.Drawing;

/// <summary>
/// Turns a polyline plus a StrokeStyle into feathered triangles.
///
/// Structure: every vertex produces a "rib" — one boundary point per side of the stroke, plus
/// an alpha-0 skirt twin outside each. Consecutive ribs are joined by a quad strip; joins and
/// caps emit their own fan geometry inline and expose the ribs the strip connects to.
///
/// Feather straddles the stroke edge rather than sitting outside it: the core edge lands at
/// offset - f/2 and the skirt at offset + f/2, so the 50%-alpha contour is exactly the
/// requested Width. Line2D instead adds its feather outside, which would render the racing
/// track's asphalt wider than the collision geometry it is drawn from.
///
/// Known limitation: on the inside of a sharp turn the miter point is clamped and the segment
/// quads overlap slightly. Invisible for opaque single-color strokes (src-over of a color
/// onto itself is that color), but a translucent stroke will read darker at such corners.
/// </summary>
public static class StrokeTessellator
{
	/// <summary>
	/// Angular step for round joins and caps. Coarser than it looks: a 113-point closed track
	/// stroke lands around 1k triangles at 12 degrees. Do not reduce without re-checking that.
	/// </summary>
	public const float FanStepRad = 0.20944f;

	/// <summary>Turn angles beyond this are treated as a reversal, where a miter is an infinite spike.</summary>
	private const float ReversalEpsilon = 1e-3f;

	private const int StackallocLimit = 256;
	private const int MaxWarnings = 8;

	private static int _warningCount;

	public static bool Tessellate(
		ReadOnlySpan<Vector2> points,
		ReadOnlySpan<float> t,
		bool closed,
		StrokeStyle style,
		float pixelScale,
		VertexBuffer output)
	{
		return TessellateCore(points, t, closed, style, pixelScale, output, false, default);
	}

	/// <summary>
	/// Tessellates one dash or stripe, honouring its per-end cap/feather overrides. Callers
	/// loop DashResult.SegmentCount and append every segment into the same buffer.
	/// </summary>
	public static bool TessellateSegment(
		ReadOnlySpan<Vector2> points,
		ReadOnlySpan<float> t,
		bool closed,
		StrokeStyle style,
		in DashSegment segment,
		float pixelScale,
		VertexBuffer output)
	{
		return TessellateCore(points, t, closed, style, pixelScale, output, true, segment);
	}

	private static bool TessellateCore(
		ReadOnlySpan<Vector2> points,
		ReadOnlySpan<float> t,
		bool closed,
		StrokeStyle style,
		float pixelScale,
		VertexBuffer output,
		bool hasSegment,
		in DashSegment segment)
	{
		// Everything that can reject the input is checked before a single vertex is emitted, so
		// a rejected shape provably leaves the buffer byte-for-byte unchanged.
		if (output == null)
		{
			return false;
		}

		if (!(style.Width > 0f) || !float.IsFinite(style.Width))
		{
			Warn("StrokeTessellator: stroke width must be positive and finite.");
			return false;
		}

		if (!(pixelScale > 0f) || !float.IsFinite(pixelScale))
		{
			Warn("StrokeTessellator: pixelScale must be positive and finite.");
			return false;
		}

		if (points.Length < 2 || !PolylineMath.AllFinite(points))
		{
			return false;
		}

		int[] rentedMap = null;
		float[] rentedT = null;

		try
		{
			Span<int> map = points.Length <= StackallocLimit
				? stackalloc int[points.Length]
				: (rentedMap = ArrayPool<int>.Shared.Rent(points.Length)).AsSpan(0, points.Length);

			int count = PolylineMath.Dedupe(points, closed, map);
			if (count < 2)
			{
				return false;
			}

			if (closed && count < 3)
			{
				closed = false;
			}

			bool reversed = false;
			if (closed && PolylineMath.SignedArea(points, map, count) < 0f)
			{
				// Normalize winding so Orthogonal() is the outward normal, which is what makes
				// StrokeAlign.Outer mean "away from the interior" regardless of authoring order.
				PolylineMath.ReverseMap(map, count);
				reversed = true;
			}

			ReadOnlySpan<float> resolvedT = t;
			if (resolvedT.IsEmpty && NeedsT(style))
			{
				rentedT = ArrayPool<float>.Shared.Rent(points.Length);
				BuildArcLengthT(points, map, count, closed, rentedT);
				resolvedT = rentedT.AsSpan(0, points.Length);
			}

			var ctx = new Context
			{
				Points = points,
				T = resolvedT,
				Map = map,
				Count = count,
				Closed = closed,
				Reversed = reversed,
				Style = style,
				Feather = style.ResolveFeatherPx() / pixelScale,
				Output = output,
				HasOverride = hasSegment && segment.UseColor,
				OverrideColor = segment.Color,
				StartCap = hasSegment ? segment.StartCap : style.Cap,
				EndCap = hasSegment ? segment.EndCap : style.Cap,
				FeatherStart = !hasSegment || segment.FeatherStart,
				FeatherEnd = !hasSegment || segment.FeatherEnd,
			};

			Emit(ref ctx);
			return true;
		}
		finally
		{
			if (rentedMap != null)
			{
				ArrayPool<int>.Shared.Return(rentedMap);
			}

			if (rentedT != null)
			{
				ArrayPool<float>.Shared.Return(rentedT);
			}
		}
	}

	private static void Emit(ref Context c)
	{
		int ribCount = c.Count;
		Rib previous = default;
		Rib first = default;

		for (int i = 0; i < ribCount; i++)
		{
			Rib rib = BuildRib(ref c, i);

			if (i == 0)
			{
				first = rib;
			}
			else
			{
				Strip(ref c, previous, rib);
			}

			previous = rib;
		}

		if (c.Closed)
		{
			Strip(ref c, previous, first);
		}
	}

	private static Rib BuildRib(ref Context c, int i)
	{
		bool isStart = !c.Closed && i == 0;
		bool isEnd = !c.Closed && i == c.Count - 1;

		if (isStart)
		{
			return BuildCap(ref c, i, (c.P(i + 1) - c.P(i)).Normalized(), atStart: true);
		}

		if (isEnd)
		{
			return BuildCap(ref c, i, (c.P(i) - c.P(i - 1)).Normalized(), atStart: false);
		}

		int prev = (i - 1 + c.Count) % c.Count;
		int next = (i + 1) % c.Count;
		return BuildJoint(ref c, i, prev, next);
	}

	private static Rib BuildJoint(ref Context c, int i, int prev, int next)
	{
		Vector2 p = c.P(i);
		Vector2 dirIn = (p - c.P(prev)).Normalized();
		Vector2 dirOut = (c.P(next) - p).Normalized();
		Vector2 nIn = dirIn.Orthogonal();
		Vector2 nOut = dirOut.Orthogonal();

		Geometry g = ComputeGeometry(ref c, i);
		Color color = ResolveColor(ref c, i, g.AlphaScale);
		Color clear = Fade(color);

		float theta = PolylineMath.SignedTurnAngle(dirIn, dirOut);
		var rib = default(Rib);
		rib.HasSkirt = g.HasSkirt;

		if (Mathf.Abs(theta) < PolylineMath.CollinearEpsilon)
		{
			// A straight joint needs no wedge: both segments share the same normal.
			rib.PosFirst = rib.PosLast = c.Output.AddVertex(p + (nIn * g.CorePos), color);
			rib.NegFirst = rib.NegLast = c.Output.AddVertex(p + (nIn * g.CoreNeg), color);
			if (g.HasSkirt)
			{
				rib.PosSkirtFirst = rib.PosSkirtLast = c.Output.AddVertex(p + (nIn * g.SkirtPos), clear);
				rib.NegSkirtFirst = rib.NegSkirtLast = c.Output.AddVertex(p + (nIn * g.SkirtNeg), clear);
			}

			return rib;
		}

		bool outerIsPos = theta > 0f;
		float outerCore = outerIsPos ? g.CorePos : g.CoreNeg;
		float outerSkirt = outerIsPos ? g.SkirtPos : g.SkirtNeg;
		float innerCore = outerIsPos ? g.CoreNeg : g.CorePos;
		float innerSkirt = outerIsPos ? g.SkirtNeg : g.SkirtPos;

		float maxTravel = Mathf.Max(
			Mathf.Abs(innerCore),
			0.5f * Mathf.Min(p.DistanceTo(c.P(prev)), c.P(next).DistanceTo(p)));

		int innerIndex = c.Output.AddVertex(
			InnerPoint(p, nIn, nOut, dirIn, dirOut, innerCore, maxTravel), color);
		int innerSkirtIndex = g.HasSkirt
			? c.Output.AddVertex(InnerPoint(p, nIn, nOut, dirIn, dirOut, innerSkirt, maxTravel + Mathf.Abs(c.Feather)), clear)
			: 0;

		JoinMode join = ResolveJoin(c.Style.Join, Mathf.Abs(theta));
		int outerFirst;
		int outerLast;
		int outerSkirtFirst;
		int outerSkirtLast;

		if (join == JoinMode.Miter &&
			TryMiter(p, nIn, nOut, dirIn, dirOut, outerCore, c.Style.ResolveMiterLimit(), out Vector2 miter))
		{
			outerFirst = outerLast = c.Output.AddVertex(miter, color);
			if (g.HasSkirt)
			{
				// The skirt miter is bounded by the same limit test the core already passed.
				Vector2 skirtMiter = TryMiter(p, nIn, nOut, dirIn, dirOut, outerSkirt, float.MaxValue, out Vector2 sm)
					? sm
					: p + (nIn * outerSkirt);
				outerSkirtFirst = outerSkirtLast = c.Output.AddVertex(skirtMiter, clear);
			}
			else
			{
				outerSkirtFirst = outerSkirtLast = 0;
			}
		}
		else
		{
			// A bevel is a one-step fan, so both share this path.
			int steps = join == JoinMode.Bevel ? 1 : Mathf.Max(Mathf.CeilToInt(Mathf.Abs(theta) / FanStepRad), 1);
			EmitFan(
				ref c,
				p,
				nIn,
				theta,
				steps,
				outerCore,
				outerSkirt,
				color,
				clear,
				g.HasSkirt,
				innerIndex,
				out outerFirst,
				out outerLast,
				out outerSkirtFirst,
				out outerSkirtLast);
		}

		if (outerIsPos)
		{
			rib.PosFirst = outerFirst;
			rib.PosLast = outerLast;
			rib.PosSkirtFirst = outerSkirtFirst;
			rib.PosSkirtLast = outerSkirtLast;
			rib.NegFirst = rib.NegLast = innerIndex;
			rib.NegSkirtFirst = rib.NegSkirtLast = innerSkirtIndex;
		}
		else
		{
			rib.NegFirst = outerFirst;
			rib.NegLast = outerLast;
			rib.NegSkirtFirst = outerSkirtFirst;
			rib.NegSkirtLast = outerSkirtLast;
			rib.PosFirst = rib.PosLast = innerIndex;
			rib.PosSkirtFirst = rib.PosSkirtLast = innerSkirtIndex;
		}

		return rib;
	}

	private static Rib BuildCap(ref Context c, int i, Vector2 travelDir, bool atStart)
	{
		Geometry g = ComputeGeometry(ref c, i);
		Color color = ResolveColor(ref c, i, g.AlphaScale);
		Color clear = Fade(color);

		CapMode cap = atStart ? c.StartCap : c.EndCap;
		bool feather = g.HasSkirt && (atStart ? c.FeatherStart : c.FeatherEnd);
		Vector2 n = travelDir.Orthogonal();
		Vector2 outward = atStart ? -travelDir : travelDir;
		Vector2 p = c.P(i);

		if (cap == CapMode.Square)
		{
			// A square cap is a butt cap on a point extended by the stroke's half width.
			p += outward * ((g.CorePos - g.CoreNeg) * 0.5f);
		}

		var rib = default(Rib);
		rib.HasSkirt = g.HasSkirt;

		if (cap == CapMode.Round)
		{
			// Centre the fan on the ring's midpoint, not the path point: an outer-aligned
			// stroke's cap would otherwise bulge off-axis.
			float mid = (g.CorePos + g.CoreNeg) * 0.5f;
			float coreRadius = (g.CorePos - g.CoreNeg) * 0.5f;
			float skirtRadius = (g.SkirtPos - g.SkirtNeg) * 0.5f;
			Vector2 center = p + (n * mid);

			// Sweeping this way carries the fan through the outward direction rather than back
			// through the stroke.
			float sweep = atStart ? -Mathf.Pi : Mathf.Pi;
			int steps = Mathf.Max(Mathf.CeilToInt(Mathf.Pi / FanStepRad), 1);

			// The cap's half-disc fans from its own centre, which sits inside the stroke.
			int centerIndex = c.Output.AddVertex(center, color);

			EmitFan(
				ref c,
				center,
				n,
				sweep,
				steps,
				coreRadius,
				feather ? skirtRadius : coreRadius,
				color,
				clear,
				feather,
				centerIndex,
				out int fanFirst,
				out int fanLast,
				out int fanSkirtFirst,
				out int fanSkirtLast);

			rib.PosFirst = rib.PosLast = fanFirst;
			rib.NegFirst = rib.NegLast = fanLast;

			if (g.HasSkirt)
			{
				rib.PosSkirtFirst = rib.PosSkirtLast = feather
					? fanSkirtFirst
					: c.Output.AddVertex(p + (n * g.SkirtPos), clear);
				rib.NegSkirtFirst = rib.NegSkirtLast = feather
					? fanSkirtLast
					: c.Output.AddVertex(p + (n * g.SkirtNeg), clear);
			}

			return rib;
		}

		// Butt (and the extended point of a square cap). With feather the ring pulls inward by
		// f/2 so the end face's alpha ramp straddles the endpoint, matching how the sides work.
		Vector2 ringPoint = feather ? p - (outward * (c.Feather * 0.5f)) : p;

		rib.PosFirst = rib.PosLast = c.Output.AddVertex(ringPoint + (n * g.CorePos), color);
		rib.NegFirst = rib.NegLast = c.Output.AddVertex(ringPoint + (n * g.CoreNeg), color);

		if (g.HasSkirt)
		{
			rib.PosSkirtFirst = rib.PosSkirtLast = c.Output.AddVertex(ringPoint + (n * g.SkirtPos), clear);
			rib.NegSkirtFirst = rib.NegSkirtLast = c.Output.AddVertex(ringPoint + (n * g.SkirtNeg), clear);
		}

		if (feather)
		{
			Vector2 endFace = p + (outward * (c.Feather * 0.5f));
			int endPos = c.Output.AddVertex(endFace + (n * g.CorePos), clear);
			int endNeg = c.Output.AddVertex(endFace + (n * g.CoreNeg), clear);

			c.Output.AddQuad(rib.PosFirst, endPos, endNeg, rib.NegFirst);
			c.Output.AddTriangle(rib.PosFirst, rib.PosSkirtFirst, endPos);
			c.Output.AddTriangle(rib.NegFirst, endNeg, rib.NegSkirtFirst);
		}

		return rib;
	}

	/// <summary>
	/// Emits an arc of ring points (plus its alpha-0 skirt arc) rotating `sweep` radians from
	/// `startNormal` around `center`, filling the wedge as a triangle fan from `fanIndex`.
	/// </summary>
	private static void EmitFan(
		ref Context c,
		Vector2 center,
		Vector2 startNormal,
		float sweep,
		int steps,
		float coreOffset,
		float skirtOffset,
		Color color,
		Color clear,
		bool hasSkirt,
		int fanIndex,
		out int first,
		out int last,
		out int skirtFirst,
		out int skirtLast)
	{
		first = last = skirtFirst = skirtLast = 0;
		int previousCore = 0;
		int previousSkirt = 0;

		for (int k = 0; k <= steps; k++)
		{
			Vector2 rotated = startNormal.Rotated(sweep * k / steps);
			int coreIndex = c.Output.AddVertex(center + (rotated * coreOffset), color);
			int skirtIndex = hasSkirt ? c.Output.AddVertex(center + (rotated * skirtOffset), clear) : 0;

			if (k == 0)
			{
				first = coreIndex;
				skirtFirst = skirtIndex;
			}
			else
			{
				c.Output.AddTriangle(fanIndex, previousCore, coreIndex);

				if (hasSkirt)
				{
					c.Output.AddQuad(previousCore, coreIndex, skirtIndex, previousSkirt);
				}
			}

			previousCore = coreIndex;
			previousSkirt = skirtIndex;
			last = coreIndex;
			skirtLast = skirtIndex;
		}
	}

	private static void Strip(ref Context c, Rib a, Rib b)
	{
		c.Output.AddQuad(a.PosLast, b.PosFirst, b.NegFirst, a.NegLast);

		if (a.HasSkirt && b.HasSkirt)
		{
			c.Output.AddQuad(a.PosSkirtLast, b.PosSkirtFirst, b.PosFirst, a.PosLast);
			c.Output.AddQuad(a.NegLast, b.NegFirst, b.NegSkirtFirst, a.NegSkirtLast);
		}
	}

	private static JoinMode ResolveJoin(JoinMode requested, float absTheta)
	{
		// A round join this shallow is sub-pixel-identical to a miter and costs triangles the
		// track stroke's ~113 mostly-shallow joints cannot afford.
		if (absTheta <= FanStepRad)
		{
			return JoinMode.Miter;
		}

		// At a reversal the offset lines are antiparallel: there is no miter point to find.
		if (absTheta >= Mathf.Pi - ReversalEpsilon)
		{
			return JoinMode.Round;
		}

		return requested;
	}

	private static bool TryMiter(
		Vector2 p,
		Vector2 nIn,
		Vector2 nOut,
		Vector2 dirIn,
		Vector2 dirOut,
		float offset,
		float limit,
		out Vector2 miter)
	{
		miter = default;
		if (!PolylineMath.LineIntersection(p + (nIn * offset), dirIn, p + (nOut * offset), dirOut, out Vector2 hit))
		{
			return false;
		}

		float magnitude = Mathf.Abs(offset);
		if (magnitude > 1e-6f && p.DistanceTo(hit) / magnitude > limit)
		{
			return false;
		}

		miter = hit;
		return true;
	}

	private static Vector2 InnerPoint(
		Vector2 p,
		Vector2 nIn,
		Vector2 nOut,
		Vector2 dirIn,
		Vector2 dirOut,
		float offset,
		float maxTravel)
	{
		Vector2 fallback = p + (nIn * offset);
		if (!PolylineMath.LineIntersection(p + (nIn * offset), dirIn, p + (nOut * offset), dirOut, out Vector2 hit))
		{
			return fallback;
		}

		Vector2 delta = hit - p;
		float distance = delta.Length();
		if (distance > maxTravel && distance > 0f)
		{
			// Clamping rather than splitting into two inner points keeps every joint to one
			// inner vertex, at the cost of a bounded overlap on the inside of sharp turns.
			return p + (delta / distance * maxTravel);
		}

		return hit;
	}

	private static Geometry ComputeGeometry(ref Context c, int i)
	{
		float width = Mathf.Max(PolylineMath.SampleProfile(c.Style.WidthProfile, c.Tt(i), c.Style.Width), 0f);

		float offsetPos;
		float offsetNeg;
		switch (c.Style.Align)
		{
			case StrokeAlign.Outer:
				offsetPos = width;
				offsetNeg = 0f;
				break;
			case StrokeAlign.Inner:
				offsetPos = 0f;
				offsetNeg = -width;
				break;
			default:
				offsetPos = width * 0.5f;
				offsetNeg = width * -0.5f;
				break;
		}

		var g = default(Geometry);
		g.AlphaScale = 1f;

		float f = c.Feather;
		if (f <= 0f)
		{
			g.CorePos = g.SkirtPos = offsetPos;
			g.CoreNeg = g.SkirtNeg = offsetNeg;
			g.HasSkirt = false;
			return g;
		}

		g.HasSkirt = true;

		if (width >= f)
		{
			g.CorePos = offsetPos - (f * 0.5f);
			g.CoreNeg = offsetNeg + (f * 0.5f);
			g.SkirtPos = offsetPos + (f * 0.5f);
			g.SkirtNeg = offsetNeg - (f * 0.5f);
			return g;
		}

		// Narrower than the feather, the core edges would cross and invert the alpha ramp.
		// Collapse to a triangular profile of half-width f and scale the peak by width/f, which
		// preserves the ink integral exactly and meets the branch above continuously at width == f.
		float center = (offsetPos + offsetNeg) * 0.5f;
		g.CorePos = g.CoreNeg = center;
		g.SkirtPos = center + f;
		g.SkirtNeg = center - f;
		g.AlphaScale = width / f;
		return g;
	}

	private static Color ResolveColor(ref Context c, int i, float alphaScale)
	{
		Color color;
		if (c.HasOverride)
		{
			color = c.OverrideColor;
		}
		else if (c.Style.ColorStops != null && c.Style.ColorStops.Length >= 2)
		{
			color = OkLab.Sample(c.Style.ColorStops, c.Tt(i));
		}
		else
		{
			color = c.Style.Color;
		}

		color.A *= alphaScale;
		return color;
	}

	private static Color Fade(Color color)
	{
		return new Color(color.R, color.G, color.B, 0f);
	}

	private static bool NeedsT(StrokeStyle style)
	{
		return (style.ColorStops != null && style.ColorStops.Length >= 2) ||
			(style.WidthProfile != null && style.WidthProfile.Length >= 2);
	}

	private static void BuildArcLengthT(
		ReadOnlySpan<Vector2> points,
		ReadOnlySpan<int> map,
		int count,
		bool closed,
		float[] destination)
	{
		float total = PolylineMath.TotalLength(points, map, count, closed);
		destination[map[0]] = 0f;

		if (total <= 0f)
		{
			for (int i = 1; i < count; i++)
			{
				destination[map[i]] = 0f;
			}

			return;
		}

		float cumulative = 0f;
		for (int i = 1; i < count; i++)
		{
			cumulative += points[map[i]].DistanceTo(points[map[i - 1]]);
			destination[map[i]] = cumulative / total;
		}
	}

	private static void Warn(string message)
	{
		// Rate-limited: this runs inside _Draw, where a per-frame invalid shape would otherwise
		// flood the log until the editor stalls.
		if (_warningCount >= MaxWarnings)
		{
			return;
		}

		_warningCount++;
		GD.PushWarning(message);
	}

	private struct Geometry
	{
		public float CorePos;
		public float CoreNeg;
		public float SkirtPos;
		public float SkirtNeg;
		public float AlphaScale;
		public bool HasSkirt;
	}

	private struct Rib
	{
		public int PosFirst;
		public int PosLast;
		public int NegFirst;
		public int NegLast;
		public int PosSkirtFirst;
		public int PosSkirtLast;
		public int NegSkirtFirst;
		public int NegSkirtLast;
		public bool HasSkirt;
	}

	private ref struct Context
	{
		public ReadOnlySpan<Vector2> Points;
		public ReadOnlySpan<float> T;
		public ReadOnlySpan<int> Map;
		public int Count;
		public bool Closed;
		public bool Reversed;
		public StrokeStyle Style;
		public float Feather;
		public VertexBuffer Output;
		public bool HasOverride;
		public Color OverrideColor;
		public CapMode StartCap;
		public CapMode EndCap;
		public bool FeatherStart;
		public bool FeatherEnd;

		public readonly Vector2 P(int i)
		{
			return Points[Map[i]];
		}

		/// <summary>Reversed winding must reverse t too, or gradients flip on CW-authored paths.</summary>
		public readonly float Tt(int i)
		{
			if (T.IsEmpty)
			{
				return 0f;
			}

			float value = T[Map[i]];
			return Reversed ? 1f - value : value;
		}
	}
}
