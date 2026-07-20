using BarBox.Core.Drawing;
using Godot;

namespace BarBox.Core.Dev;

/// <summary>
/// Permanent visual test bed for the drawing module: every primitive, join, cap, and style in
/// one screen, plus the two animated cells that prove the batching model. Unit tests pin the
/// math, so this exists for taste — and for the profiler checks that no test can make.
///
/// Launch with `--vector-gallery`, or run the scene directly from the editor.
/// </summary>
public partial class VectorGallery : Node2D
{
	private const int Columns = 3;
	private const int Rows = 6;
	private const float CellWidth = 460f;
	private const float CellHeight = 300f;
	private const float MarginX = 30f;
	private const float MarginY = 90f;

	private static readonly Vector2[] ZigZag =
	[
		new Vector2(-90f, 40f),
		new Vector2(-30f, -50f),
		new Vector2(30f, 40f),
		new Vector2(90f, -50f),
	];

	private static readonly Vector2[] Concave =
	[
		new Vector2(-80f, -50f),
		new Vector2(80f, -50f),
		new Vector2(80f, 50f),
		new Vector2(10f, 50f),
		new Vector2(10f, -5f),
		new Vector2(-10f, -5f),
		new Vector2(-10f, 50f),
		new Vector2(-80f, 50f),
	];

	/// <summary>Demo-only gradient — proves OKLab interpolation avoids muddy grays, independent of any game's own palette.</summary>
	private static readonly ColorStop[] DemoGradient =
	[
		new(0f, Palette.Cyan),
		new(0.5f, Palette.Green),
		new(0.8f, Palette.Yellow),
		new(1f, Palette.Red),
	];

	private readonly Contour3Set _boxEdges = new();
	private readonly Contour3Set _gridLines = new();

	private Label _stats;
	private Shape _dashRunner;
	private Shape _rotator;
	private Shape _trimSweep;
	private Shape _recolorPulse;
	private Shape _retessPulse;
	private int _cell;
	private float _time;

	public override void _Ready()
	{
		BuildStatsLabel();

		BuildJoins();
		BuildCaps();
		BuildWidths();
		BuildAlignment();
		BuildHairlineClamp();
		BuildGradient();
		BuildWidthProfile();
		BuildDashes();
		BuildStripes();
		BuildCheckerFill();
		BuildFills();
		BuildPrimitives();
		BuildWireframeBox();
		BuildPerspectiveGrid();
		BuildRotator();
		BuildTrimReveal();
		BuildRecolorPulse();
		BuildPalette();
	}

	public override void _Process(double delta)
	{
		_time += (float)delta;

		// The two cells that make the batching model observable: a per-frame dash offset on a
		// Dynamic() shape and a per-frame rigid transform on a static one. Frame time must stay
		// flat for both — if it tracks the static shape count, the bucket split is broken.
		_dashRunner?.SetDashOffset(-_time * 40f);
		_rotator?.SetTransform(new Transform2D(_time, Vector2.Zero));

		// Draw-on trim sweep: ping-pongs 0..1, epsilon end so a fully-collapsed range never
		// reads as "SetTrim broke" instead of "the sweep is momentarily at its start."
		float sweep = (Mathf.Sin(_time * 0.6f) * 0.5f) + 0.5f;
		_trimSweep?.SetTrim(0f, Mathf.Max(sweep, 0.001f));

		// Same color/alpha math on both circles, driven through the two different setters, so any
		// perceptible difference between them is purely internal rebuild cost, not appearance.
		float pulse = (Mathf.Sin(_time * 3f) * 0.4f) + 0.6f;
		var pulsedColor = Palette.Orange * new Color(1f, 1f, 1f, pulse);
		_recolorPulse?.SetFillColor(pulsedColor);
		_retessPulse?.SetFill(pulsedColor);

		if (_stats != null && Engine.GetProcessFrames() % 30 == 0)
		{
			_stats.Text = BuildStatsText();
		}
	}

	private void BuildJoins()
	{
		ShapeCanvas canvas = Cell("joins: round / miter / bevel");
		JoinMode[] modes = [JoinMode.Round, JoinMode.Miter, JoinMode.Bevel];
		Color[] colors = [Palette.Blue, Palette.Orange, Palette.Green];

		for (int i = 0; i < modes.Length; i++)
		{
			canvas.Build()
				.Polyline(Offset(ZigZag, new Vector2(0f, (i * 70f) - 70f)))
				.Stroke(new StrokeStyle { Width = 14f, Color = colors[i], Join = modes[i] })
				.Commit();
		}
	}

	private void BuildCaps()
	{
		ShapeCanvas canvas = Cell("caps: round / butt / square");
		CapMode[] modes = [CapMode.Round, CapMode.Butt, CapMode.Square];
		Color[] colors = [Palette.Blue, Palette.Orange, Palette.Green];

		for (int i = 0; i < modes.Length; i++)
		{
			float y = (i * 60f) - 60f;
			canvas.Build()
				.Polyline([new Vector2(-80f, y), new Vector2(80f, y)])
				.Stroke(new StrokeStyle { Width = 18f, Color = colors[i], Cap = modes[i] })
				.Commit();

			// A hairline down the true endpoints shows exactly how far each cap extends past them.
			canvas.Build()
				.Polyline([new Vector2(-80f, y - 14f), new Vector2(-80f, y + 14f)])
				.Stroke(new StrokeStyle { Width = 1f, Color = Palette.EdgeGray })
				.Commit();
		}
	}

	private void BuildWidths()
	{
		ShapeCanvas canvas = Cell("widths: 1 / 2.5 / 12");
		float[] widths = [1f, 2.5f, 12f];

		for (int i = 0; i < widths.Length; i++)
		{
			float y = (i * 60f) - 60f;
			canvas.Build()
				.Polyline([new Vector2(-90f, y), new Vector2(90f, y)])
				.Stroke(new StrokeStyle { Width = widths[i], Color = Palette.White })
				.Commit();
		}
	}

	private void BuildAlignment()
	{
		ShapeCanvas canvas = Cell("align: center / inner / outer");
		var rect = new Rect2(-70f, -50f, 140f, 100f);

		canvas.Build().RoundedRect(rect, 16f).Fill(Palette.Panel).Commit();

		StrokeAlign[] aligns = [StrokeAlign.Center, StrokeAlign.Inner, StrokeAlign.Outer];
		Color[] colors = [Palette.Blue, Palette.Orange, Palette.Green];

		for (int i = 0; i < aligns.Length; i++)
		{
			canvas.Build()
				.RoundedRect(rect.Grow(i * 22f), 16f)
				.Stroke(new StrokeStyle { Width = 6f, Color = colors[i], Align = aligns[i] })
				.Commit();
		}
	}

	private void BuildHairlineClamp()
	{
		ShapeCanvas canvas = Cell("hairline clamp: 0.5 / 1.25 / 3");
		float[] widths = [0.5f, 1.25f, 3f];

		for (int i = 0; i < widths.Length; i++)
		{
			float y = (i * 60f) - 60f;
			canvas.Build()
				.Polyline([new Vector2(-90f, y), new Vector2(90f, y)])
				.Stroke(new StrokeStyle { Width = widths[i], Color = Palette.White })
				.Commit();
		}
	}

	private void BuildGradient()
	{
		ShapeCanvas canvas = Cell("OKLab gradient");
		canvas.Build()
			.Arc(new Vector2(0f, 40f), 90f, Mathf.Pi, Mathf.Tau)
			.Stroke(VectorStyles.GaugeArc with { ColorStops = DemoGradient })
			.Commit();

		// Blue to yellow through OKLab must not pass through gray; that is the whole reason the
		// interpolation is not done in sRGB.
		canvas.Build()
			.Polyline([new Vector2(-90f, 70f), new Vector2(90f, 70f)])
			.Stroke(new StrokeStyle
			{
				Width = 16f,
				Color = Palette.Blue,
				ColorStops = [new ColorStop(0f, Palette.Blue), new ColorStop(1f, Palette.Yellow)],
			})
			.Commit();
	}

	private void BuildWidthProfile()
	{
		ShapeCanvas canvas = Cell("tapered width profile");
		canvas.Build()
			.Polyline([new Vector2(-90f, 0f), new Vector2(0f, -30f), new Vector2(90f, 0f)])
			.Stroke(new StrokeStyle
			{
				Width = 20f,
				Color = Palette.Cyan,
				WidthProfile = [1f, 20f, 1f],
			})
			.Commit();
	}

	private void BuildDashes()
	{
		ShapeCanvas canvas = Cell("dashes (bottom is Dynamic, animating)");
		canvas.Build()
			.Polyline([new Vector2(-90f, -40f), new Vector2(90f, -40f)])
			.Stroke(VectorStyles.DashedGuide(Palette.EdgeGray))
			.Commit();

		canvas.Build()
			.Circle(new Vector2(0f, 20f), 45f)
			.Stroke(VectorStyles.DashedGuide(Palette.Blue))
			.Commit();

		_dashRunner = canvas.Build()
			.Polyline([new Vector2(-90f, 80f), new Vector2(90f, 80f)])
			.Stroke(VectorStyles.DashedGuide(Palette.Orange) with { Width = 5f })
			.Dynamic()
			.Commit();
	}

	private void BuildStripes()
	{
		ShapeCanvas canvas = Cell("striped stroke (kerb) / stripes fill (zone tint)");
		canvas.Build()
			.Polyline([new Vector2(-90f, -48f), new Vector2(-20f, -98f), new Vector2(60f, -88f), new Vector2(90f, -38f)])
			.StripedStroke(18f, 14f, Palette.Red, Palette.White)
			.Commit();

		var subject = new[]
		{
			new Vector2(-100f, 0f),
			new Vector2(100f, 0f),
			new Vector2(100f, 100f),
			new Vector2(-100f, 100f),
		};

		canvas.StripesFill(subject, Mathf.DegToRad(30f), 16f, Palette.Blue, Palette.Panel);
	}

	private void BuildCheckerFill()
	{
		ShapeCanvas canvas = Cell("checker fill (start line)");
		var subject = new[]
		{
			new Vector2(-100f, -40f),
			new Vector2(100f, -40f),
			new Vector2(100f, 40f),
			new Vector2(-100f, 40f),
		};

		canvas.CheckerFill(subject, new Vector2(-100f, -40f), 25f, Palette.White, Palette.Ink);
	}

	private void BuildFills()
	{
		ShapeCanvas canvas = Cell("fills: bare / outlined / concave");
		canvas.Build()
			.Circle(new Vector2(-100f, 0f), 45f)
			.Fill(Palette.Blue)
			.Commit();

		canvas.Build()
			.Circle(new Vector2(0f, 0f), 45f)
			.Fill(Palette.Panel)
			.Stroke(VectorStyles.EdgeLine with { Color = Palette.Orange })
			.Commit();

		canvas.Build()
			.Polygon(Offset(Concave, new Vector2(110f, 0f)))
			.Fill(Palette.Green)
			.Commit();
	}

	private void BuildPrimitives()
	{
		ShapeCanvas canvas = Cell("primitives: rrect / arc / bezier");
		canvas.Build()
			.RoundedRect(new Rect2(-110f, -60f, 90f, 60f), 14f)
			.Stroke(VectorStyles.ButtonOutline(Palette.Purple))
			.Commit();

		canvas.Build()
			.Arc(new Vector2(50f, -30f), 45f, -Mathf.Pi * 0.75f, Mathf.Pi * 0.25f)
			.Stroke(VectorStyles.GaugeArc with { Color = Palette.Yellow })
			.Commit();

		canvas.Build()
			.CubicBezier(new Vector2(-100f, 70f), new Vector2(-40f, -20f), new Vector2(40f, 130f), new Vector2(100f, 40f))
			.Stroke(new StrokeStyle { Width = 5f, Color = Palette.Cyan })
			.Commit();
	}

	private void BuildWireframeBox()
	{
		ShapeCanvas canvas = Cell("isometric wireframe box");
		Wireframes.Box(new Vector3(2.2f, 2.2f, 2.2f), _boxEdges);
		canvas.Build()
			.Path3(_boxEdges, Projector.Isometric(45f))
			.Stroke(VectorStyles.Wireframe(Palette.Green))
			.Commit();
	}

	private void BuildPerspectiveGrid()
	{
		ShapeCanvas canvas = Cell("perspective grid");
		Wireframes.Grid(8f, 8f, 8, 8, _gridLines);
		canvas.Build()
			.Path3(_gridLines, Projector.Perspective(new Vector3(0f, 4f, -7f), Vector3.Zero, 3f, 60f))
			.Stroke(VectorStyles.Wireframe(Palette.Grid))
			.Commit();
	}

	private void BuildRotator()
	{
		ShapeCanvas canvas = Cell("SetTransform (rebuilds must stay flat)");
		_rotator = canvas.Build()
			.Polygon([new Vector2(-60f, -40f), new Vector2(60f, -40f), new Vector2(0f, 60f)])
			.Fill(Palette.PanelRaised)
			.Stroke(VectorStyles.EdgeLine with { Color = Palette.Orange })
			.Commit();
	}

	private void BuildTrimReveal()
	{
		ShapeCanvas canvas = Cell("SetTrim: draw-on reveal (gauge sweep)");

		// Dim full-circle reference so the sweep's progress reads clearly against it.
		canvas.Build()
			.Circle(Vector2.Zero, 90f)
			.Stroke(VectorStyles.GaugeArc with { Color = Palette.Grid })
			.Commit();

		_trimSweep = canvas.Build()
			.Circle(Vector2.Zero, 90f)
			.Stroke(VectorStyles.GaugeArc with { Color = Palette.Cyan })
			.Dynamic()
			.Commit();
	}

	private void BuildRecolorPulse()
	{
		ShapeCanvas canvas = Cell("SetFillColor recolor (left) vs SetFill re-tess (right)");

		_recolorPulse = canvas.Build()
			.Circle(new Vector2(-60f, 0f), 50f)
			.Fill(Palette.Orange)
			.Dynamic()
			.Commit();

		_retessPulse = canvas.Build()
			.Circle(new Vector2(60f, 0f), 50f)
			.Fill(Palette.Orange)
			.Dynamic()
			.Commit();
	}

	private void BuildPalette()
	{
		ShapeCanvas canvas = Cell("palette");
		Color[] swatches =
		[
			Palette.Blue, Palette.Orange, Palette.Green, Palette.Red,
			Palette.Yellow, Palette.Cyan, Palette.Purple, Palette.White,
			Palette.EdgeGray, Palette.Grid, Palette.PanelRaised, Palette.Panel,
		];

		for (int i = 0; i < swatches.Length; i++)
		{
			float x = ((i % 4) * 58f) - 87f;
			float y = ((i / 4) * 58f) - 58f;
			canvas.Build()
				.RoundedRect(new Rect2(x - 24f, y - 24f, 48f, 48f), 8f)
				.Fill(swatches[i])
				.Commit();
		}
	}

	private ShapeCanvas Cell(string title)
	{
		int column = _cell % Columns;
		int row = _cell / Columns;
		_cell++;

		var origin = new Vector2(
			MarginX + (column * CellWidth) + (CellWidth * 0.5f),
			MarginY + (row * CellHeight) + (CellHeight * 0.5f));

		var canvas = new ShapeCanvas { Name = title, Position = origin };
		AddChild(canvas);

		// The frame and caption are plain shapes on their own canvas, so a cell's own canvas
		// holds only what it is demonstrating and its triangle count stays readable.
		var chrome = new ShapeCanvas { Name = $"{title} chrome", Position = origin, ZIndex = -1 };
		AddChild(chrome);
		chrome.Build()
			.RoundedRect(new Rect2((-CellWidth * 0.5f) + 8f, (-CellHeight * 0.5f) + 8f, CellWidth - 16f, CellHeight - 16f), 12f)
			.Fill(Palette.Panel)
			.Stroke(new StrokeStyle { Width = 1f, Color = Palette.Grid })
			.Commit();

		var label = new Label
		{
			Text = title,
			Position = origin + new Vector2((-CellWidth * 0.5f) + 20f, (-CellHeight * 0.5f) + 14f),
			Modulate = Palette.EdgeGray,
		};
		AddChild(label);

		return canvas;
	}

	private void BuildStatsLabel()
	{
		// Below the grid, not above it: the platform autoloads keep their own menu bar on screen
		// across a scene change, and it would sit on top of anything placed at the top edge.
		_stats = new Label
		{
			Position = new Vector2(MarginX, MarginY + (Rows * CellHeight) + 24f),
			Modulate = Palette.White,
		};
		AddChild(_stats);
	}

	private string BuildStatsText()
	{
		int shapes = 0;
		int triangles = 0;
		int items = 0;
		float pixelScale = 1f;

		foreach (Node child in GetChildren())
		{
			if (child is ShapeCanvas canvas)
			{
				shapes += canvas.ShapeCount;
				triangles += canvas.TriangleCount;
				items += canvas.CanvasItemCount;
				pixelScale = canvas.PixelScale;
			}
		}

		return $"shapes {shapes}   triangles {triangles}   canvas items {items}   " +
			$"pixel scale {pixelScale:0.###}   rotator rebuilds {_rotator?.RebuildCount ?? 0}   " +
			$"recolor rebuilds {_recolorPulse?.RebuildCount ?? 0}   re-tess rebuilds {_retessPulse?.RebuildCount ?? 0}   " +
			$"fps {Engine.GetFramesPerSecond():0}";
	}

	private static Vector2[] Offset(Vector2[] points, Vector2 delta)
	{
		var result = new Vector2[points.Length];
		for (int i = 0; i < points.Length; i++)
		{
			result[i] = points[i] + delta;
		}

		return result;
	}
}
