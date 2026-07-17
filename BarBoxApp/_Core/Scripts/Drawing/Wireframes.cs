using Godot;

namespace BarBox.Core.Drawing;

/// <summary>
/// Faux-3D edge generators. Output into a caller-owned Contour3Set, matching the rest of the
/// module's pooling convention, then project with Build().Path3(set, projector).
///
/// Depth is painter's order only — these emit no faces, so there is nothing to occlude, and the
/// decorative OP-1 wireframes this exists for do not need a depth sort.
/// </summary>
public static class Wireframes
{
	/// <summary>
	/// Axis-aligned box centred on the origin: the two Z faces as closed loops plus the four
	/// struts joining them. Six contours rather than twelve separate edges, so the corners get
	/// real joins instead of four round caps stacked on each other.
	/// </summary>
	public static void Box(Vector3 size, Contour3Set output)
	{
		if (output == null)
		{
			return;
		}

		output.Clear();

		Vector3 h = size * 0.5f;

		AddFace(output, -h.Z, h);
		AddFace(output, h.Z, h);

		for (int i = 0; i < 4; i++)
		{
			float x = (i == 0 || i == 3) ? -h.X : h.X;
			float y = i < 2 ? -h.Y : h.Y;

			int start = output.BeginContour();
			output.AddPoint(new Vector3(x, y, -h.Z));
			output.AddPoint(new Vector3(x, y, h.Z));
			output.EndContour(start, false);
		}
	}

	/// <summary>
	/// Grid on the XZ plane centred on the origin, as cellsX + cellsZ + 2 straight lines. Zero
	/// cells in either axis still yields that axis's two bounding lines.
	/// </summary>
	public static void Grid(float width, float depth, int cellsX, int cellsZ, Contour3Set output)
	{
		if (output == null)
		{
			return;
		}

		output.Clear();

		if (cellsX < 0 || cellsZ < 0 || !float.IsFinite(width) || !float.IsFinite(depth))
		{
			return;
		}

		float halfW = width * 0.5f;
		float halfD = depth * 0.5f;

		for (int i = 0; i <= cellsX; i++)
		{
			float x = cellsX == 0 ? -halfW : Mathf.Lerp(-halfW, halfW, (float)i / cellsX);
			int start = output.BeginContour();
			output.AddPoint(new Vector3(x, 0f, -halfD));
			output.AddPoint(new Vector3(x, 0f, halfD));
			output.EndContour(start, false);
		}

		for (int i = 0; i <= cellsZ; i++)
		{
			float z = cellsZ == 0 ? -halfD : Mathf.Lerp(-halfD, halfD, (float)i / cellsZ);
			int start = output.BeginContour();
			output.AddPoint(new Vector3(-halfW, 0f, z));
			output.AddPoint(new Vector3(halfW, 0f, z));
			output.EndContour(start, false);
		}
	}

	private static void AddFace(Contour3Set output, float z, Vector3 h)
	{
		int start = output.BeginContour();
		output.AddPoint(new Vector3(-h.X, -h.Y, z));
		output.AddPoint(new Vector3(h.X, -h.Y, z));
		output.AddPoint(new Vector3(h.X, h.Y, z));
		output.AddPoint(new Vector3(-h.X, h.Y, z));
		output.EndContour(start, true);
	}
}
