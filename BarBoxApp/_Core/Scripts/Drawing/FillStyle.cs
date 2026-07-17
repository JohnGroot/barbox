using Godot;

namespace BarBox.Core.Drawing;

public struct FillStyle
{
	public Color Color;

	/// <summary>
	/// Escape hatch for animated/shader effects. Ignored by FillTessellator — a shape with a
	/// Material is routed to its own child canvas item by ShapeCanvas rather than joining the
	/// vertex-color batch.
	/// </summary>
	public ShaderMaterial Material;
}
