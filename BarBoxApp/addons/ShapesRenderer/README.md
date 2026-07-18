# ShapesRenderer (parked)

This addon is parked, not deleted — kept as reference for a possible future real-3D
rendering backend. It is disabled in `project.godot` (`[editor_plugins]`) and has no
consumers in the app.

The active 2D drawing system lives in `_Core/Scripts/Drawing/` (`BarBox.Core.Drawing`,
CPU-tessellated `ShapeCanvas`). See `docs/filled-vector-rendering-plan.md` for its design.

Salvaged from this addon into that system: the builder API concept, the OKLab gradient
math (`Shaders/oklab.gdshaderinc` → `OkLab.cs`), and the per-point width/color feature
set as a tessellator spec.

Not salvaged, and not fixed: `src/LinearArena.cs`, `src/MemoryHelpers.cs`,
`Shaders/polyline.glsl`. Known bugs in this code (stale uniform set after SSBO growth,
width-gradient index mismatch in the shader, double alloc in
`LinearArena.AllocBuffer<T>`) are left as-is — dead code earns no maintenance.
