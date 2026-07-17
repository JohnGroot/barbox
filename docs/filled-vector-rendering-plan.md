# BarBox Filled-Vector Rendering System — Implementation Plan

## Context

BarBox is getting a visual overhaul toward the teenage engineering OP-1 aesthetic: dark backgrounds, crisp colored strokes with round joins/caps, selective flat fills, dashed lines, gauges/arcs, and occasional faux-3D wireframes (isometric boxes, perspective grids). Reference: the OP-1 manual PDF and the user's Figma mockups (racing game + main menu, dark panels with 2px colored outline buttons).

Current state (verified by exploration):

- `addons/ShapesRenderer/` (~1,270 lines) is a **3D world-space** polyline batcher: `CompositorEffect` + raw `RenderingDevice`, instanced screen-space quad expansion (`Shaders/polyline.glsl`), round caps via fragment discard, OKLab gradients, single instanced draw call, zero managed allocs via a custom unmanaged arena (`src/LinearArena.cs`). Only primitive: polyline. No fills, no AA, no 2D integration, empty `examples/`, zero consumers in the app, demo code hardwired into the render callback. Known bugs: stale uniform set after SSBO growth, width-gradient index mismatch in the shader, double alloc in `LinearArena.AllocBuffer<T>`. Plugin is **enabled** in `project.godot` (`[editor_plugins]` line 48).
- The app is entirely 2D: portrait 1440×2560, `canvas_items` stretch, black clear color, touchscreen. Racing tracks are editor-authored `.tscn` `Line2D`/`Polygon2D` scenes (node-scaled 6×); HUDs and Carrom are immediate-mode `_Draw()`; Mining and all menus are code-built Control UI. There is **no central palette** — 192 inline `new Color(...)` literals and a single font theme (`_Core/Fonts/InterTheme.tres`, Inter SemiBold 16).
- Established perf conventions to preserve: pre-allocated reusable buffers, dirty-flag `QueueRedraw()` gating, cached values invalidated on change, zero per-frame managed allocation in hot paths.

## User decisions (fixed)

1. **Pivot to 2D canvas-native rendering.** Faux-3D (Vector3 paths + projector → 2D polylines) is first-class; a real 3D backend is not foreclosed (keep the model backend-agnostic; the parked RD renderer may return).
2. **Code-only v1 API** (fluent/imperative). Architecture must allow `[Tool]` editor nodes later without rework — unified code + editor is the long-term goal.
3. **Static-first, animation-ready**: crisp static rendering, cheap state changes; dash offset / trim / arc angles are plain mutable fields so animation bolts on later. Dashed strokes themselves ARE v1.
4. **Fonts stay Godot-native** (Inter). SVG import handled by an offline converter tool (see §6), not runtime.
5. **Proving ground: Racing game** restyled per the mockup. Central palette/theme module is the highest-leverage first step.
6. Scope: system + Racing pilot. Other games/menu polish follow in later plans (M3 does the main-menu chrome since it's small and exercises the Control-UI side).

---

## 1. Rendering core architecture

**CPU tessellation into pooled triangle buffers, uploaded via `RenderingServer.CanvasItemAddTriangleArray` from a retained `ShapeCanvas : Node2D`, with vertex-color feathered AA (~1.25px alpha skirt). No custom stroke shaders. Per ShapeCanvas: the node's own canvas item for rebuild-on-dirty (static-ish) shapes, one child RID item for `Dynamic`-flagged shapes (trails, live gauges), plus one child item per custom-material shape (escape hatch).** This is the engine's own approach generalized — `CanvasItemAddTriangleArray` "is internally used by Line2D and StyleBoxFlat" per the Godot docs, and `Line2D`'s `antialiased` mode is the same vertex-alpha feather technique.

Rejected alternatives: SDF-in-quad canvas shaders (fine for isolated circles/rects, falls apart on N-segment polylines with joins; forces per-primitive canvas items); resurrecting the RD renderer in 2D (loses z-ordering/CanvasLayer/Control interop and the editor; every feature hand-built). CPU tessellation handles every primitive through one uniform path and the math is unit-testable without a GPU.

Pipeline:

```
analytic primitive (arc/bezier/rrect/circle/poly; Vector2 or Vector3+Projector)
  → PathFlattener   (adaptive, tolerance-based, into pooled Vector2 buffers)
  → DashSplitter    (optional; arc-length walk → capped sub-paths)
  → StrokeTessellator / FillTessellator  (triangles into pooled VertexBuffer)
  → per-Shape cached sub-buffer (rebuilt only when that Shape is dirty)
  → ShapeCanvas._Draw(): concat cached sub-buffers → one CanvasItemAddTriangleArray
```

Key decisions:

- **StrokeTessellator (built ourselves — the hard part).** Core quad per segment at full alpha + feather skirt quads on both silhouette edges. Round joins = triangle fan between adjacent segment edges (fan step ~12° — do not go finer; a 113-point closed track stroke lands at only ~1k triangles at 12°), auto-miter below a turn-angle threshold (saves triangles on smooth curves). Caps: round (half-fan) / butt / square. Per-point width and per-point color supported (parity with the parked addon); color stops interpolated **in OKLab on the CPU** at tessellation time, baked as vertex colors (port `Shaders/oklab.gdshaderinc` → `OkLab.cs`). **sRGB boundary (required for correct gradients):** 2D vertex colors are consumed sRGB-encoded (`hdr_2d` off), OKLab needs linear RGB — the conversion chain is `Color.SrgbToLinear()` → OKLab → interpolate → `LinearToSrgb()` → bake; `OkLabTests` round-trips sRGB endpoints. Stroke alignment: Center / Inner / Outer (needed for track edge lines). Feather width is in **screen pixels**: tessellation takes a `PixelScale` derived from `GetScreenTransform().Scale.X` (this includes parent node scales AND the `canvas_items` stretch factor; assert near-uniform scale), cached in `_Ready` and refreshed on `GetTree().Root.SizeChanged`, with a manual override for tests. Never hardcode it — CircuitA's transforms differ from the other tracks (see §7).
- **Feather straddles the stroke edge** (settled in M1): the core edge lands at `offset - f/2` and the skirt at `offset + f/2`, so the 50%-alpha contour is exactly the requested `Width`. Line2D instead adds its feather *outside*, which would render M4's asphalt ~1.25px wider than the `TrackLine` collision geometry it is drawn from and fire penalties inside the visible edge. **Hairline clamp:** below `Width <= f` the core edges would cross and invert the alpha ramp, so the core collapses to the band centre, the skirt sits at `±f`, and alpha scales by `Width/f` — this preserves the ink integral exactly and meets the normal branch continuously at `Width == f` (skirt at `±f/2` would halve the ink and pop discontinuously).
- **Invalid input returns `false` + a rate-limited `PushWarning`, never throws** (settled in M1) — this code runs inside `_Draw()`, where a skipped shape beats an exception in a released build. All validation happens before the first vertex is emitted, so a rejected shape provably leaves the buffer unchanged.
- **Known limitation — inner-join overlap.** On the inside of a sharp turn the miter point is clamped and the segment quads overlap. Invisible for opaque single-color strokes (src-over of a color onto itself is that color), but a *translucent* stroke reads darker at such corners. M4 consequence: keep zone **outlines opaque** and put the translucency in the zone **fill** only.
- **Striped strokes (kerbs).** A stroke variant where dash segments alternate between two colors with no gaps (`StripedStroke`: alternating red/white along the polyline) — implemented as a DashSplitter mode (color-alternating, zero off-length). Required in M1 because kerbs are Line2D *polylines*, not polygons (see §7 step 2).
- **FillTessellator**: borrow `Geometry2D.TriangulatePolygon` (allocates, but only on rebuild). Robustness (verified engine behavior): it accepts either winding but **returns an empty `int[]` on failure** (self-intersecting/degenerate input) — treat empty as skip-shape-with-debug-log, never index into it. Bare fills get an automatic hairline stroke of the fill color for AA, synthesized in `Shape.Rebuild` (not at `Commit()` — its width is `DefaultFeatherPx / PixelScale`, so it depends on the canvas scale). Outlined fills need no feather **provided the stroke is opaque, `Center`/`Outer`-aligned, and at least the feather width** — a thin `Inner`-aligned outline leaves the fill's own hard edge exposed outside itself; v1 accepts that rather than adding coverage analysis. Holes are not a v1 general feature (donuts = fat arc strokes; or `Geometry2D.ClipPolygons` at build time).
- **DashSplitter**: pure function; on/off `DashPattern` + `DashOffset` float (animation-ready: set offset → mark dirty).
- **Patterned fills (checkered start line, zone tints): geometric, not shader.** Clip checker/stripe bands against the target polygon with `Geometry2D.IntersectPolygons` at build time — stays in the vertex-color batch with feathered edges (which `KerbStripes.gdshader` cannot do). Robustness (verified): `IntersectPolygons` can return **multiple disjoint polygons** (triangulate each) and, in principle, clockwise hole polygons (detect with `IsPolygonClockwise`, skip). `FillStyle.Material` routes a shape to a child canvas item for future animated/shader effects.
- **Dirty/batching model**: each `Shape` caches its triangles in its own pooled `VertexBuffer`; any setter marks the shape + its bucket dirty. `_Draw()` re-tessellates only dirty shapes, concatenates cached buffers per bucket, and issues one `CanvasItemAddTriangleArray` per bucket. **Static/dynamic bucket rule (important):** `canvas_item_clear` frees each command's GPU vertex/index buffers, so re-issuing an item re-creates its GPU buffers — a per-frame-dirty shape must never share a bucket with heavy static geometry, or the whole canvas re-uploads every frame. Shapes flagged `Dynamic` at build go to the child dynamic item; only that item is cleared/re-added on per-frame dirt. Within a bucket, draw order = commit order (painter's algorithm — confirmed: intra-item command order and intra-array primitive order both blend in submission order); an optional int sort key at `Commit()` allows reordering, and the sort is on `(SortKey, CommitSeq)` so `List.Sort`'s instability cannot reorder ties. Shapes inside one canvas cannot z-interleave with *external* nodes — anything needing independent layering (per-car visuals) gets its own `ShapeCanvas` (canvas `ZIndex` layers them: track surface < kerbs/zones < start line < cars/trails; today's TrackLine is z=-5, kerbs z=1).
- **Command issuance (settled in M2 — the plan previously contradicted itself here).** **Only static dirt may call `QueueRedraw()`.** A redraw clears the node's own item, forcing `_Draw()` to re-issue the static bucket — so routing per-frame dirt through `QueueRedraw` would re-upload the static geometry every frame and the bucket split would buy nothing. Static dirt → `QueueRedraw()` → `_Draw()` rebuilds the static concat if dirty and uploads it *unconditionally* (the engine already cleared the item, and `_Draw` also fires on `ENTER_CANVAS`/visibility changes). Dynamic and material dirt → `_childFlushPending` + `SetProcess(true)` → `_Process` calls `CanvasItemClear` + upload on that child item alone, then self-disables. Child items are never auto-cleared by the parent's redraw.
- **`SortKey` is intra-bucket only.** Child canvas items always draw after their parent item's own commands, so every `Dynamic()`/material shape paints above every static shape in the same canvas regardless of key. Cross-bucket layering needs a separate `ShapeCanvas` + `ZIndex` (or `CanvasItemSetDrawBehindParent`, unused so far).
- **Rigid motion without re-tessellation**: `Shape.SetTransform` bakes through `VertexBuffer.Append(src, xform)` at concat time (dirty level `Concat`), *not* `CanvasItemAddSetTransform` — that command affects all subsequent commands, so honouring it per shape would split the bucket into N command pairs and stop it being one draw call, and it cannot coexist with the relocatable-append model above (nothing tracks a shape's position inside the concat). Cost is one transform-multiply loop per bucket rebuild; no re-tessellation. **Rigid transforms only:** the bake happens after tessellation, so a scale would scale the baked stroke width and the pixel-accurate feather with it (warned at runtime). Convention: continuously *moving* content moves its Node2D (own canvas); `SetTransform` is for occasional layout shifts.
- **Memory: the addon's `LinearArena` is NOT salvaged.** GodotSharp 4.7 has **`ReadOnlySpan` overloads** of `CanvasItemAddTriangleArray` (and `TriangulatePolygon`/`IntersectPolygons`): pooled oversized arrays upload via `points.AsSpan(0, VertexCount)` etc. with zero managed allocation and no `[..count]` slice copies. Engine constraints: `colors` length must be 1 or exactly equal `points` length; indices length a multiple of 3 (the `count` parameter truncates indices only). Use grow-only pooled managed arrays (`VertexBuffer`: `Vector2[] Points; Color[] Colors; int[] Indices; int VertexCount, IndexCount`) + a per-canvas `ScratchBuffers` pool → zero steady-state allocation without `unsafe`. (This also fixes an existing leak: `RacingVisualFeedbackRenderer`'s `_trailPointsBuffer[..n]` slices allocate today; the span path doesn't.)
- **`VertexBuffer` is relocatable-append, not in-place mid-list segments** (settled in M1, constrains M2). Each `Shape` owns a small buffer whose indices are self-relative; `ShapeCanvas._Draw()` rebuilds a bucket with `Clear()` + `Append(each)`, where `Append` memcpys points/colors and rebases indices by `+= vertexBase`. Nothing tracks a shape's position inside a shared array, so §9's "mid-list shape grows/shrinks and corrupts its neighbours" bug class is structurally impossible rather than merely tested. Cost is one memcpy plus an add-loop per dirty rebuild — negligible over ~1k triangles. Concatenation order is draw order, which is the painter's-algorithm/`SortKey` model already specified above. **Tessellator contract:** an append records `vertexBase = VertexCount` on entry, emits absolute indices, never touches vertices below `vertexBase`, and on rejection leaves both counts unchanged.
- **Lifecycle**: child canvas-item RIDs (dynamic bucket, material buckets) are created via `CanvasItemCreate()` + `CanvasItemSetParent(child, GetCanvasItem())` and **freed in `_Notification(NotificationPredelete)` via `RenderingServer.FreeRid`** — `_ExitTree` only hides (nodes can re-enter the tree). This is also what makes future `[Tool]` use safe (the editor rebuilds nodes constantly); additionally guard side effects with `Engine.IsEditorHint()` where relevant and keep shape registration idempotent.
- Borrowed from Godot: `Geometry2D.TriangulatePolygon`, `Geometry2D.IntersectPolygons`. Built: flattener (Curve2D.Tessellate can't target pooled buffers), stroke tessellator, dash splitter. `Line2D`/`Polygon2D` remain the **editor authoring + collision geometry** layer (tracks), no longer the visual layer.
- **Text stays Godot** (`Label`/`DrawString`), styled by the theme layer.

## 2. Shape/path model & API

New module: **`_Core/Scripts/Drawing/`**, namespace `BarBox.Core.Drawing`. Plain classes, not an autoload (a utility like `LineUtils`). Production code — must pass the dotnet-format style gate (unlike the analyzer-excluded addon). C# uses **tabs**.

Style structs are **plain structs, not `record struct`s**: record equality compares the array fields by reference (structurally identical styles would compare unequal — never use style equality for dirty checks), and `with` would share array references. **Array ownership rule:** the builder copies `ColorStops`/`WidthProfile`/`DashPattern` at `Commit()`; style arrays are treated as immutable thereafter (mutating a shared preset's array would bypass dirty tracking). `VectorStyles` presets and `Palette` gradient arrays are deliberately **shared** references — safe because `Shape.SetStroke` is the single copy choke point, and `ShapeBuilder.Commit()` routes through it rather than copying separately. Enum defaults: `Round = 0` for both `JoinMode` and `CapMode` so `default` styles behave; note `default(StrokeStyle)` has `Width 0` and is invalid — start from `VectorStyles` presets.

**A `Shape` holds N contours, not one** (settled in M2). `FlatPath` is a single polyline, but a wireframe box is 12 disjoint edges and `PathBuilder.MoveTo` starts a new one — so the model is uniformly plural and a single-primitive shape is just `ContourCount == 1`. `Shape.Rebuild` flattens each contour into its own retained `FlatPath` and tessellates each into the shape's one `VertexBuffer`; that is structurally what `DashSplitter` + `TessellateSegment` already do, so the tessellators need nothing new. 3D sources use `Contour3Set` (pooled `Vector3[]` + `Contour{Start,Count,Closed}[]`). A fill on a multi-contour shape fills each closed contour independently — still no holes.

**Per-point width/color are parameterized by normalized arc length `t`, not by point index** (settled in M1). Index-based is unimplementable: `PathFlattener` picks the point count adaptively from tolerance, so the author cannot know it, and `DashSplitter` changes it by carving sub-paths. `ColorStop` carries an explicit `T`; `WidthProfile` uses implicit uniform `t` (entry `i` at `i/(n-1)`) and may be any length. Dash sub-paths inherit the parent's `t`, so gradients stay continuous across gaps for free.

```csharp
public struct StrokeStyle {
	public float Width;                 // canvas units
	public Color Color;
	public ColorStop[] ColorStops;      // {T, Color}, sorted; OKLab-interpolated (null/<2 = solid)
	public float[] WidthProfile;        // resampled at t = i/(n-1); any length (null/<2 = constant)
	public JoinMode Join;               // Round (default) | Miter | Bevel
	public CapMode Cap;                 // Round (default) | Butt | Square
	public float[] DashPattern;         // null = solid
	public float DashOffset;            // animation-ready
	public StrokeAlign Align;           // Center | Inner | Outer
	public float TrimStart;             // 0..1; v1 fixed 0, reserved for draw-on animation
	public float TrimEnd;               // 0..1; v1 fixed 1
	public float FeatherPx;             // screen px; 0 = default 1.25, negative = NoFeather
	public DashMode DashMode;           // OnOff (default) | Striped
	public Color DashColorB;            // the alternate stripe color in Striped mode
	public float MiterLimit;            // 0 = default 4.0
}
public struct FillStyle { public Color Color; public ShaderMaterial Material; }

public sealed class Shape {
	// Mutators raise a monotonic dirty level; the bucket rebuild clears it:
	//   Flatten > Tess > Concat > None
	// SetStroke/SetFill/SetStrokeColor/SetStrokeWidth/SetDashOffset  -> Tess
	// SetArc/SetRadius/SetRect/SetPoints/SetPoints3/SetProjector     -> Flatten
	// SetTransform/SetVisible/SetSortKey                             -> Concat
}

public partial class ShapeCanvas : Node2D {
	public ShapeBuilder Build();        // fluent; .Commit() returns retained Shape
	public void Remove(Shape s);
	// PixelScale auto-derives from GetScreenTransform().Scale.X (parent scales + stretch);
	// refreshed on Root.SizeChanged; settable override for tests/pure-math use
	public float PixelScale { get; set; }
}
```

Fluent usage (salvages the parked addon's builder concept):

```csharp
Shape speedArc = _hud.Build()
	.Arc(center, radius, startAngle, endAngle)
	.Stroke(VectorStyles.GaugeArc with { ColorStops = Palette.SpeedGradient })
	.Commit();

_track.Build().Polygon(trackPoints, closed: true)
	.Stroke(new StrokeStyle { Width = 60f, Color = Palette.Asphalt })  // drivable surface IS a fat stroke
	.Commit();

_bg.Build().Path3(boxEdges, Projector.Isometric(scale: 40f))          // faux-3D, same pipeline
	.Stroke(VectorStyles.Wireframe(Palette.Grid)).Commit();
```

Builder primitives v1 (M2 as shipped): `Polyline`, `Polygon(pts, closed)`, `Circle`, `Arc`, `RoundedRect`, `Rect`, `CubicBezier`, `QuadBezier`, `Path3(ReadOnlySpan<Vector3>, in Projector, closed)`, `Path3(Contour3Set, in Projector)`, `StripedStroke(width, segmentLength, colorA, colorB)` (alternating-color dash stroke — kerbs). Builder also takes `.Dynamic()` (per-frame bucket), `.SortKey(int)` (intra-bucket draw order; default = commit order), `.Tolerance(px)`, `.Hidden()`, `.WithMaterial(ShaderMaterial)`. The builder is **pooled one-per-canvas**, so a `Build()` chain allocates only the `Shape`.

**Deferred out of M2, at zero rework cost** (the multi-contour `Shape` is the only thing they needed from it):
- `PathBuilder` (`MoveTo/LineTo/CubicTo/ArcTo/Close`) → **M7**, alongside its actual consumer, `tools/svg2shape` (whose `M/L/C/S/Q/T/A/Z` command set *is* `PathBuilder`). Nothing in M3–M6 uses it, and it needs new append-style overloads on `PathFlattener` (every current method `Clear()`s its output and calls `FinalizeT()`) — reopening M1's most-tested file for a feature with no consumer is bad sequencing.
- `StripedFill(polygon, angle, freq, colorA, colorB)` and `CheckerFill(rect, cellSize, colorA, colorB)` → **M4**, next to the start line and zone tints that consume them.

Future layers bolt on without rework: animation = drive the plain float/Color fields via targeted setters (dirty path already the designed hot path); `[Tool]` editor nodes = thin `ShapeNode2D` adapter that serializes geometry+style and registers a `Shape` with the nearest `ShapeCanvas`; real 3D backend = model/tessellators are Godot-node-free — only `ShapeCanvas` touches RenderingServer.

## 3. Style/theme system

- **`_Core/Scripts/Drawing/Palette.cs`** — single source of color truth. Static readonly base surfaces (`Ink` near-black, `Panel`, `PanelRaised`), line grays (`Grid`, `EdgeGray`, `White`), OP-1 accents (`Blue`, `Orange`, `Green`, `Red`, `Yellow`, `Cyan`, `Purple`), **semantic aliases** (`TrackEdge`, `Danger`, `Success`, `HudNeedle`, `PlayerColors[]`), gradient stop arrays (`SpeedGradient`). New CLAUDE.md rule (added in M2): *no new `new Color(...)` literals outside Palette.cs*.
- **`_Core/Scripts/Drawing/VectorStyles.cs`** — stroke/fill presets: `HairLine`, `EdgeLine`, `GaugeArc`, `Wireframe(c)`, `DashedGuide(c)`, `ButtonOutline(c)`.
- **`_Core/Scripts/Drawing/UiTheme.cs`** — Control-side generators: `OutlineButton(Color accent)` StyleBoxFlat set (transparent bg, 2px accent border, radius 10; hover/pressed/disabled variants — pressed = filled accent with `Ink` text), `PanelBox()`, `ModalBox()`, `ApplyOutlineButton(Button, Color)` one-call helper; font-size constants (`FontSmall/Body/Title/Digits`). Font itself stays `InterTheme.tres`.
- The 192 inline colors migrate **incrementally**: each milestone converts files it touches; final mechanical sweep in M7.

## 4. Faux-3D projector

**`_Core/Scripts/Drawing/Projector.cs`** — readonly struct, pure math:

```csharp
public Vector2 Project(Vector3 p);
public static Projector Isometric(float scale, float elevationDeg = 30f, float azimuthDeg = 45f);
public static Projector Perspective(Vector3 camPos, Vector3 lookAt, float focalLength, float scale);
public Projector WithYawPitch(float yaw, float pitch);  // turntable animation later
```

`Path3()` projects into the same pooled Vector2 buffers; dashes/joins/feather identical downstream. `Wireframes.cs` generators write into a caller-owned `Contour3Set` (output-param, allocation-free, matching every other generator in the module): `Box(Vector3 size, Contour3Set)` — 6 contours (two closed face loops + four struts) covering 12 edges, rather than 12 two-point edges, so corners get real joins instead of stacked round caps; `Grid(width, depth, cellsX, cellsZ, Contour3Set)` — `cellsX + cellsZ + 2` open lines. Depth = painter's order (submit far edges first) — adequate for decorative OP-1 wireframes; documented limitation, and a non-issue while wireframes emit no faces to occlude.

## 5. Fate of `addons/ShapesRenderer`

- **Parked, not deleted** (reference for a future real-3D backend). In M7: remove `res://addons/ShapesRenderer/plugin.cfg` from `project.godot` `[editor_plugins] enabled` (it IS currently enabled, line 48); add a README in the addon pointing to `_Core/Scripts/Drawing/`.
- Salvaged: builder API concept, `oklab.gdshaderinc` math (→ `OkLab.cs` with round-trip tests), per-point width/color feature set as tessellator spec.
- Not salvaged: `LinearArena.cs`, `MemoryHelpers.cs`, `polyline.glsl`. Its three known bugs are not fixed — dead code earns no maintenance.

## 6. SVG import (researched recommendation)

Godot's built-in SVG (ThorVG) rasterizes at import — unusable here. **v1: offline CLI converter `tools/svg2shape/`** (standalone dotnet console project in the repo): `System.Xml.Linq` + hand-written SVG path-data (`d=`) parser (M/L/H/V/C/S/Q/T/A/Z, ~300 lines, unit-testable; elliptical-arc→cubic is textbook). Subset: `<path>`, `<rect>`, `<circle>`, `<ellipse>`, `<polygon>`, `<polyline>`, `<line>`, solid fill/stroke/stroke-width, `transform`. Ignored: gradients, masks, filters, `<text>`. Output: `ShapePathResource : Resource` `.tres` (contours + per-contour styles), loaded via `Build().Resource(res)`; converted output checked in. Rejected: runtime Svg.Skia/SkiaSharp (native binaries per export platform), Inkscape polygon export (lossy). Later option at no rework cost: wrap the same parser in an `EditorImportPlugin`.

## 7. Racing pilot migration

**Load-bearing constraint (verified in code):** gameplay reads *nodes*, not pixels. `RacingTrackDefinition.SetupTrack()` caches `TrackLine` (Line2D) points/width/closed and builds the spatial index; `IsValidTrackPointFast` calls `TrackLine.ToLocal()` per query (`_Games/RacingGame/Scripts/Components/RacingTrackDefinition.cs:100-122,161-185`). `RacingZoneManager` reads `CollisionPolygon2D`s; `RacingLineTrigger` has a `VisualLine` Line2D separate from its `SegmentShape2D`. **Strategy: keep every Line2D / CollisionPolygon2D / Area2D exactly where it is as geometry+collision source of truth; set visuals invisible; add ShapeCanvas renderers that read the same geometry at setup.** Validation/checkpoints/zones stay bit-identical — `RacingTrackValidationSystemTests`, `RacingZoneManagerTests`, `RacingTimingSystemTests` are the regression gate.

Order:

1. **`RacingTrackRenderer.cs` (new ShapeCanvas)** — added at runtime by `RacingGame.cs` after `SetupTrack()` (no .tscn surgery initially). **Transform strategy (critical — the 3 tracks differ):** OvalTrack/GoCartTrack have root scale 6× and `TrackLine` width 60, but **CircuitA has an unscaled root with `TrackLine` at scale 1.25 + rotation 1.574 + position offset and width 300**, and its triggers are scaled 5×. So: read width from `TrackLine.Width` (never hardcode 60), auto-derive `PixelScale` from the canvas's `GetScreenTransform()` (never hardcode 6), and since TrackLine, zones, and triggers each carry distinct transforms, bake each source node's transform-relative-to-the-canvas into the point data at build time (`sourceNode.GetGlobalTransform()` composed with the canvas's inverse global transform). Renders: dark flat surface (`TrackLine.Width`-wide closed round-join stroke, `Palette.Asphalt` — geometrically identical to today's Line2D so visuals match collision), inner hairline (`EdgeGray`, inner-aligned), **blue outer edge** (`Palette.TrackEdge`, ~2.5 wide, outer-aligned). Hides `TrackLine`, plus `InnerEdgeLine`/`OuterEdgeLine` where present (**OvalTrack/GoCartTrack only — CircuitA has neither; null-check**). Note for the M4 eye-check: today's edge lines are separately-scaled Line2D *copies*, not true offset curves, so aligned strokes will shift edge geometry slightly — intended improvement, verify it looks right. **Track-switch lifecycle:** `RacingGame.LoadTrack()` QueueFrees the old track and rebuilds (`RacingGame.cs:~1210–1274`, called at runtime) — parent the renderer to the track root so it dies with it, and re-create it in `SetupLoadedTrack()`.
2. **Kerbs + zones** — **corrected premise (verified): kerb *zones* do not exist.** No track instantiates one; `RacingZone.CreateKerbZone()` has zero callers; the `IsInKerbZone` validation path is dormant. Kerbs are `KerbLineA–E` — plain **Line2D polylines** (CircuitA only, width 50) with `KerbStripesMaterial`. There is no polygon to clip against: render kerbs as **`StripedStroke`** (alternating red/white segments along the polyline, matching the shader's stripe frequency), then hide the KerbLine nodes. Kerbs stay visual-only — introducing kerb zones would be a gameplay change, out of scope. Boost/slow/frictionless zones: flat translucent fill + dashed outline in zone color, sourced from each zone's `CollisionPolygon2D`. The existing visual path is private `RacingZone.UpdateVisual()` called only from `_Ready()` — **add** a small hook (direct callback/interface per CLAUDE.md convention — signals are for external integration only) so the renderer supplies visuals and `VisualPolygon` is hidden; collision untouched.
3. **Checkered start line** — `CheckerFill` band (2×8 cells) from `StartLine.GlobalPosition/Rotation`, cell size scaled by the trigger's global scale (5× on CircuitA, 6× elsewhere). Extent: triggers use `SegmentShape2D`, which `RacingLineTrigger.GetWidth()` does NOT handle — it silently falls back to `VisualLine` point distance (`RacingLineTrigger.cs:55-72`); extend `GetWidth()` to read `SegmentShape2D` so hiding `VisualLine` can't break it. Hide `VisualLine` on start AND checkpoint triggers (keep nodes — script null-checks them); checkpoint color feedback rerouted through a hook in the existing `RacingLineTrigger.SetLineColor`.
3b. **Barriers** — CircuitA has 5 and GoCartTrack has 9 `StaticBody2D` barriers with `BarrierLine` Line2D visuals (width 15). Restyle as simple strokes on the track renderer (collision untouched, nodes hidden) — otherwise the restyled track shows old-style barrier lines.
4. **`RacingCar.cs`** — replace `ColorRect` body + `Polygon2D` headlight with per-car ShapeCanvas: rounded-rect body fill (player color) + 2px white outline + windshield line; translucent headlight cone. Keep sizes identical (validation samples car size as data).
5. **`RacingVisualFeedbackRenderer.cs`** — port `_Draw()` to retained shapes: tire trails = two polylines updated via `SetPoints` (existing pooled `_trailPointsBuffer`s plug into the `ReadOnlySpan<Vector2>` API); input line = dashed stroke; touch indicator = arc. Keep update cadence/dirty gating.
6. **`RacingHUDArcRenderer.cs`** — speedo half-arc (bg arc + progress arc with `SpeedGradient` OKLab stops + needle), lap ring, countdown ring as retained shapes; keep `HasVisualStateChanged`/`RecordDrawnState` pattern; text stays `DrawString` but **switch `ThemeDB.FallbackFont` → Inter (existing bug)**.
7. **`RacingUIManager.cs`** (+ `RacingRaceCompleteUI.cs`, `RacingTracksLeaderboardUI.cs`) — mechanical restyle: inline `StyleBoxFlat` → `UiTheme` generators; `ApplyOutlineButton` (Time Trial = Orange, Restart = Blue, Leaderboard = Green); digital timer panel; colored player labels via `Palette.PlayerColors`; marquee = clipping Control + tweened Label.
8. **Track .tscn cleanup (last, optional)** — set `visible = false` on decorative Line2Ds in the 3 track scenes (or delete `InnerEdgeLine`/`OuterEdgeLine`; **never delete `TrackLine`** — validation source). Line2DHelpers/CatmullRom authoring workflow stays: author on Line2D, render via ShapeCanvas.

## 8. Milestones (each independently landable in one session)

Every milestone ends with: `cd BarBoxApp && dotnet build` clean → `bash scripts/run-tests.sh` green → listed manual check → style gate: `RunAnalyzersDuringBuild=true dotnet format BarBox.csproj --severity warn --exclude addons/ShapesRenderer/ --verify-no-changes`.

For drawing-only iteration, skip the backend phases (`run-tests.sh` starts BarBoxServices even in `frontend` mode) with `godot --path . --headless --run-tests --quit-on-finish`.

| # | Scope | Model | Why |
|---|-------|-------|-----|
| **M1** ✅ **DONE** | **Geometry core (pure C#, no nodes):** `DrawingEnums.cs`, `ColorStop.cs`, `StrokeStyle.cs`, `FillStyle.cs`, `Palette.cs`, `OkLab.cs` (sRGB↔linear boundary per §1), `VertexBuffer.cs`, `PolylineMath.cs`, `FlatPath.cs`, `PathFlattener.cs`, `DashResult.cs`, `DashSplitter.cs` (incl. color-alternating `Striped` mode — kerbs need it), `StrokeTessellator.cs` (joins/caps/feather/per-point width+color/alignment), `FillTessellator.cs` (empty-result handling), `Projector.cs`; 129 GoDotTest cases (§9). Landed as one milestone, no split needed | **Opus/Fable** | Join/cap/feather geometry, miter thresholds, dash arc-length math = highest-defect-risk code in the plan |
| **M2** ✅ **DONE** | **ShapeCanvas + fluent API + gallery:** `Contour3Set.cs`, `Shape.cs` (dirty ladder, N contours, `SetStroke` as the single array-copy choke point), `ShapeBucket.cs` (pure sort/concat half), `ShapeBuilder.cs` (pooled, `.Dynamic()`, `.SortKey()`), `ShapeCanvas.cs` (dirty routing, span-overload triangle upload, **static/dynamic buckets**, material-bucket child items, `PixelScale` auto-derivation, **`NotificationPredelete` RID cleanup**), `VectorStyles.cs`, `Wireframes.cs`, `_Core/Scenes/Dev/VectorGallery.tscn` + `--vector-gallery` flag; CLAUDE.md Palette rule. 56 new tests, all green (431 → 487 passing). Landed as one milestone. Resolved four contradictions in this doc — see §1 command issuance, §1 `SetTransform`, §1 SortKey, §2 multi-contour | **Opus/Fable** | API surface + dirty/batching architecture echo through everything after |
| **M3** ✅ **DONE** | **Theme/Control layer + main menu chrome:** `UiTheme.cs` (`OutlineButton`/`PanelBox`/`ModalBox`/`ApplyOutlineButton`, font-size constants); `Palette.Info`/`Warning` aliases; restyled `MainController.cs` menu buttons, `TopMenuBar.cs` (bar/labels/buttons), `NotificationService.cs` (severity colors), `UIManager.cs` help overlay (circular button, scrim, modal, text). Scope held strictly to these files — `LoginModal`/`ConfirmationDialog`/`BuyCreditsModal`/`OnScreenKeyboard`/`DataTableView`/`DebugPerformanceMonitor` deliberately left unconverted; `TopMenuBar.ApplyStandardButtonStyle` kept (still called by `BuyCreditsModal` and `CarromPlayerSetupMenu`). 497 tests green, style gate clean | **Sonnet** | Mechanical stylebox generation against an established pattern |
| **M4** ✅ **DONE** | **Racing track restyle (migration 1–3b):** `RacingTrackRenderer.cs` — surface/edges (new `PolylineOffset.cs`; §1's `StrokeAlign` alone can't place edge lines at a track's true boundary, since it offsets by the stroke's *own* width, not an arbitrary distance — see the file's doc comment), kerbs (`StripedStroke`), zone fill+dashed-outline, checkered start line + checkpoint/finish strokes (new `ShapeKind.Mesh`/`Shape.SetMesh`/`ShapeCanvas.CommitMesh`/`PatternFill.cs`, built on M1's `FillTessellator.TessellateClipped` — shipped then, zero callers until now), barriers. New hooks: `RacingZone.HideVisual`/`GetLocalPolygon`, `RacingLineTrigger.GetWidth` `SegmentShape2D` branch (every real trigger uses one, not `RectangleShape2D`) + `VisualColorChanged` callback. Wired into `RacingGame.SetupLoadedTrack()`. One canvas per track at `ZIndex = -5`, layered by commit order — car and (M5's) trail effects stay at the default `ZIndex = 0` and always draw on top, a deliberate simplification of the old per-node z-index scheme. Checkpoint/finish-line visuals are an inferred addition: the doc didn't specify a replacement once `VisualLine` is hidden, but §8's checkpoint-color-feedback check needs one. **Post-implementation review (8-angle, before commit) caught and fixed a real bug**: every shipped track's `StartLinePath`/`FinishLinePath` alias the same node, so the naive "render every trigger" pass drew a plain checkpoint stroke on top of the checkered start line and hijacked its color hook — fixed with a same-node skip, pinned by a new regression test. Same pass fixed `RenderZones` silently dropping a zone's own `ShowVisual` export, extracted a shared point-baking helper (`BakePolyline`) used by 3 call sites that had drifted apart, and removed dead API surface (`StripedFill` wrapper, unused `hidden` param). 23 new tests (521 total, all green); `RacingTrackRendererTests` loads all 3 real track scenes incl. CircuitA's divergent transform, pins its width-baking, and pins the start/finish-alias fix. Style gate clean | **Sonnet** | Highest integration risk — visual geometry must register perfectly with collision/validation across per-track transform variance |
| **M5** | **Car + feedback + HUD (migration 4–6)** | **Sonnet** (Fable if budget allows for the HUD arc port) | Porting draw calls onto established patterns |
| **M6** | **Racing UI panels/buttons/marquee (migration 7) + one faux-3D flourish** (isometric wireframe on race-complete overlay, exercising `Path3` in production) | **Sonnet** | ~1,860 lines of mechanical restyle (UIManager + RaceComplete + Leaderboard) + tested projector garnish |
| **M7** | **Sweep + park:** `tools/svg2shape` CLI + `ShapePathResource` + parser tests; remaining inline-color sweep (Carrom/Mining/Nines, colors only — **also** `TopMenuBar.ApplyStandardButtonStyle`'s literals in `_Core/Scripts/UI/`, kept alive in M3 for `BuyCreditsModal`/`CarromPlayerSetupMenu`; the M7 grep gate will trip on it even though it's outside the three game folders named above); disable ShapesRenderer plugin in project.godot; track .tscn cleanup (migration 8); addon README | **Sonnet** | Grammar-driven parser with test suite; mechanical sweep |

Manual checks per milestone: M2 — gallery crisp at 1440×2560, single-digit canvas items in profiler, no per-frame GC growth, and a dynamic shape in the gallery does not cause static-bucket re-upload (verify via profiler frame time while a dash-offset animates). M3 — menu shows OP-1 bordered buttons; login/menu flows work. M4 — drive all 3 tracks **starting with CircuitA**; penalties fire exactly at the visible edge; checkpoint color feedback works; laps count; barriers render. M5 — trails/dashes/gauges animate smoothly, **frame time flat** (trails re-tessellate per frame — watch the profiler, not just GC), HUD font is Inter. M6 — full race loop incl. purchase flow, race-complete, leaderboards. M7 — convert one real SVG logo, display in gallery; grep shows no hardcoded color *literals* outside Palette/tests/parked addon. **The grep gate needs judgement, not a raw `new Color(` count:** computed-channel constructions are legitimate and already exist in M1 (`OkLab.ToSrgb`, `StrokeTessellator`'s alpha fade) — the rule targets design literals. It also cannot see `.tscn`-authored colors (`default_color`, exported `ZoneColor`) — sweep those by hand or accept them as authoring data.

## 9. Testing strategy

GoDotTest unit tests in `Tests/Unit/Drawing/` (pure math — keeps the codebase's logic-only convention). Base directly on `Chickensoft.GoDotTest.TestClass`, not `BackendTestBase`, which would drag service discovery into pure math. Note: `Tests/CLAUDE.md` prescribes `[Category("Unit")]`, but GoDotTest defines no such attribute and no test in the repo uses one — that section of the doc is aspirational; don't copy it.

`DrawingTestHelpers.cs` is what makes triangle soup assertable without a GPU, and the coverage sampler is the load-bearing piece — it turns "does the round join fill the wedge" into an assertion that vertex counts cannot express:
- `CountCovering(buffer, point)` / `IsCovered` — barycentric point-in-triangle over every triangle. Overlap is legitimate on the inside of joins, so assert `> 0` there rather than `== 1`.
- `TotalArea(buffer)` — summed |cross|/2. Exact anchors: a butt-capped unfeathered straight stroke is exactly `length * width`; round caps add exactly one circle.
- `AssertWellFormed(buffer)` — indices a multiple of 3 and in range, all points finite, all colors finite and in [0,1]. Call from **every** tessellation test; this is where §9's "no NaN/degenerates" requirement actually lives.
- `MaxAlpha(buffer)` — the hairline clamp's observable output.

- `StrokeTessellatorTests` — vertex/index invariants per join/cap mode; no NaN/degenerates for 2-point lines, duplicate points, collinear runs, closed loops, 180° reversals; zero/negative width rejected; feather vertices alpha 0 / core alpha 1; inner/outer alignment offsets.
- `DashSplitterTests` — pattern coverage sums to path length; offset wraparound; closed-path seam; dash longer than path; empty pattern = solid; **StripedStroke mode: alternating colors tile the full length with no gaps, seam behavior on closed paths**.
- `FillTessellatorTests` — valid concave polygon triangulates fully (area preserved); self-intersecting/degenerate input → empty result handled as skip (no throw/index-out-of-range); `IntersectPolygons` multi-polygon results each triangulated; CW (hole) results skipped.
- `VertexBufferTests` — sub-buffer caching: a mid-list shape growing/shrinking keeps all other shapes' segments and indices intact (the likeliest home of subtle bugs — make buffer management a pure testable class).
- **`AllocationTests` pins §1's zero-steady-state-allocation promise** (added in M2), which nothing else tests: `GC.GetAllocatedBytesForCurrentThread()` around N warmed rebuilds, asserting **exactly 0** for restyling, dash-offset animation, `SetPoints` (M5's trail path), `SetTransform`, and a whole frame of dirt across both buckets. Verified sensitive by mutation — injecting one `new byte[8]` per rebuild fails all five. Two traps: **build the mutator delegate before the measurement window** (a closure constructed inside it reads as 64 bytes of phantom "drawing" allocation), and **warm up first** (the pooled buffers legitimately allocate while growing; a dash offset needs a full pattern cycle to settle). Scope is stroke paths — a fill goes through `Geometry2D.TriangulatePolygon`, which allocates per rebuild by design.
- **Assert on coverage or area, never on triangle counts** (learned in M2, the hard way). A count-based assertion is almost always a false proxy: it could not distinguish "13 dashes" from "13 overlapping full-length strokes" (a real bug that shipped past a green test and was caught only by eye in the gallery), and an explicit hairline stroke has the *same* triangle count as the synthesized one because they share joins and fan step — only width and colour differ. `IsCovered`/`TotalArea`/`MaxAlpha` express the property that actually matters; `IndexCount` expresses an accident of the tessellator.
- **`TessellateSegment`'s contract:** the caller must pass **only that segment's slice** (`Points.AsSpan(seg.Start, seg.Count)`). It reads the `DashSegment` solely for colour and per-end cap/feather overrides and never slices the span itself — passing the whole buffer silently redraws the entire contour once per dash.
- `ShapeTests` / `ShapeBuilderTests` / `ShapeCanvasTests` (M2) — the load-bearing ones: `SetTransform` raises only `Concat` and the concat carries the baked transform while the shape's own buffer stays local (pins §1's rigid-motion decision); dirtying a `Dynamic()` shape leaves the static bucket's `ConcatDirty` false and re-tessellates no static shape (pins §1's command-issuance rule — the empirical form of the M2 profiler check); equal `SortKey`s preserve commit order; a second `Build()` inherits nothing from the first (the pooled builder's characteristic failure); mutating a source array after `Commit()` cannot reach the shape.
- `PathFlattenerTests` — arc/bezier endpoint exactness; max deviation ≤ tolerance; point count monotonic in tolerance; rounded-rect corner continuity.
- `OkLabTests` — sRGB→linear→OKLab→linear→sRGB round trip within epsilon; blue→yellow midpoint is not gray; gradient endpoints match inputs exactly.
- `ProjectorTests` — isometric known points; perspective foreshortening ordering; yaw/pitch composition.
- `SvgPathParserTests` (M7) — every command letter, relative/implicit forms, arc flags, scientific notation, malformed input.
- Regression gate (M4/M5): existing `RacingTrackValidationSystemTests`, `RacingZoneManagerTests`, `RacingTimingSystemTests` unchanged and green.

Visual output verified by eye (project convention) via the VectorGallery scene + racing checks above; unit tests pin the math so visual review is about taste, not correctness.

## Critical files

- `_Games/RacingGame/Scripts/Components/RacingTrackDefinition.cs` — the Line2D-as-geometry contract to preserve
- `_Games/RacingGame/Scenes/_Tracks/CircuitA.tscn` — **the divergent track**: unscaled root, transformed TrackLine (rot 1.574, scale 1.25, width 300), KerbLineA–E polylines, 5 barriers, 5×-scaled triggers; OvalTrack/GoCartTrack are the simple cases (6× root, width 60)
- `_Games/RacingGame/Scripts/Components/RacingZone.cs` (private `UpdateVisual()` — hook point), `RacingLineTrigger.cs` (`GetWidth()` SegmentShape2D gap, `SetLineColor` hook), `RacingGame.cs` (`LoadTrack`/`SetupLoadedTrack` — renderer lifecycle)
- `_Games/RacingGame/Scripts/Visual/RacingHUDArcRenderer.cs` — dirty-flag pattern to adopt; first HUD port target
- `_Games/RacingGame/Scripts/Visual/RacingVisualFeedbackRenderer.cs`, `RacingUIManager.cs`, `Scripts/Components/RacingCar.cs`
- `addons/ShapesRenderer/src/Shapes.cs`, `Shaders/oklab.gdshaderinc` — salvage sources
- `_Core/Scripts/_Utils/_Lines/LineUtils.cs` — conventions the new `_Core/Scripts/Drawing/` module sits beside
- `project.godot` — plugin enablement (M7), display settings (do not change)
