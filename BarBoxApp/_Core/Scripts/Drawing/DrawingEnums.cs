namespace BarBox.Core.Drawing;

// Zero-valued members are the intended defaults: `default(StrokeStyle)` must not select an
// exotic join/cap. Reordering these silently changes the meaning of every default style.
public enum JoinMode
{
	Round = 0,
	Miter,
	Bevel,
}

public enum CapMode
{
	Round = 0,
	Butt,
	Square,
}

public enum StrokeAlign
{
	Center = 0,
	Inner,
	Outer,
}

public enum DashMode
{
	OnOff = 0,
	Striped,
}
