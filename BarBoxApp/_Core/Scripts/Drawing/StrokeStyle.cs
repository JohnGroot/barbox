using Godot;

namespace BarBox.Core.Drawing;

/// <summary>
/// Stroke appearance. Deliberately a plain struct rather than a `record struct`: record
/// equality compares the array fields by reference, so two structurally identical styles
/// would compare unequal (never use style equality for dirty checks), and `with` would share
/// array references across styles.
///
/// Array ownership: the builder copies ColorStops/WidthProfile/DashPattern at Commit(). Style
/// arrays are immutable thereafter — mutating a shared preset's array bypasses dirty tracking.
///
/// `default(StrokeStyle)` is invalid (Width 0). Start from a VectorStyles preset.
/// </summary>
public struct StrokeStyle
{
	/// <summary>Feather width in screen pixels when FeatherPx is left at its 0 sentinel.</summary>
	public const float DefaultFeatherPx = 1.25f;

	/// <summary>Assign to FeatherPx to suppress the alpha skirt entirely (hard edges).</summary>
	public const float NoFeather = -1f;

	/// <summary>Miter ratio past which a miter join degrades to a bevel, per SVG.</summary>
	public const float DefaultMiterLimit = 4f;

	/// <summary>Canvas units. Zero or negative is rejected by the tessellator.</summary>
	public float Width;

	public Color Color;

	/// <summary>Sorted ascending by T, OKLab-interpolated. Null or fewer than 2 stops = solid Color.</summary>
	public ColorStop[] ColorStops;

	/// <summary>
	/// Widths resampled along normalized arc length: entry i sits at t = i/(n-1). Length is
	/// independent of the flattened point count. Null or fewer than 2 entries = constant Width.
	/// </summary>
	public float[] WidthProfile;

	public JoinMode Join;
	public CapMode Cap;

	/// <summary>On/off lengths in canvas units. Null = solid. Odd-length patterns tile doubled, per SVG.</summary>
	public float[] DashPattern;

	/// <summary>Mutable so animation can drive it: set and mark dirty.</summary>
	public float DashOffset;

	public StrokeAlign Align;

	/// <summary>
	/// Start of the fractional arc-length window [TrimStart, ResolveTrimEnd()] of the flattened
	/// contour that gets stroked, applied before dashing. Zero (the default) is already the
	/// correct "start of path" value and needs no resolver.
	/// </summary>
	public float TrimStart;

	/// <summary>
	/// End of the trim window. TrimEnd &lt;= 0 resolves to 1 (see ResolveTrimEnd), the same
	/// zero-is-default convention as FeatherPx/MiterLimit — a literal TrimEnd == 0 does NOT mean
	/// "draw nothing"; represent that with Shape.SetVisible(false) or a tiny epsilon end instead.
	/// </summary>
	public float TrimEnd;

	/// <summary>Screen pixels. 0 = DefaultFeatherPx; negative = no feather. See ResolveFeatherPx.</summary>
	public float FeatherPx;

	public DashMode DashMode;

	/// <summary>The alternate stripe color in DashMode.Striped. Ignored otherwise.</summary>
	public Color DashColorB;

	/// <summary>0 = DefaultMiterLimit. See ResolveMiterLimit.</summary>
	public float MiterLimit;

	public readonly float ResolveFeatherPx()
	{
		return FeatherPx == 0f ? DefaultFeatherPx : Mathf.Max(FeatherPx, 0f);
	}

	public readonly float ResolveMiterLimit()
	{
		return MiterLimit <= 0f ? DefaultMiterLimit : MiterLimit;
	}

	public readonly float ResolveTrimEnd()
	{
		return TrimEnd <= 0f ? 1f : TrimEnd;
	}
}
