using System;
using Godot;

namespace BarBox.Core.Drawing;

/// <summary>
/// Projects 3D paths to 2D canvas space for the faux-3D wireframes. Pure math — the result
/// feeds the same flattener/dash/stroke pipeline as any other polyline.
///
/// Depth is resolved by painter's order (submit far edges first), not by clipping or a depth
/// buffer. Perspective has no near plane: geometry behind the camera clamps to a small
/// positive depth and smears rather than disappearing. Adequate for decorative wireframes;
/// a moving camera through geometry would need real clipping.
/// </summary>
public readonly struct Projector
{
	private const float MinDepth = 1e-3f;
	private const float GimbalGuard = 0.9999f;

	public readonly Vector3 Right;
	public readonly Vector3 Up;
	public readonly Vector3 Forward;
	public readonly Vector3 CamPos;
	public readonly Vector3 LookAt;
	public readonly float FocalLength;
	public readonly float Scale;
	public readonly bool IsPerspective;

	/// <summary>Retained so WithYawPitch can rebuild the basis from scratch instead of accumulating error.</summary>
	public readonly float AzimuthRad;
	public readonly float ElevationRad;

	private Projector(
		Vector3 camPos,
		Vector3 lookAt,
		float focalLength,
		float scale,
		bool isPerspective,
		float azimuthRad,
		float elevationRad)
	{
		CamPos = camPos;
		LookAt = lookAt;
		FocalLength = focalLength;
		Scale = scale;
		IsPerspective = isPerspective;
		AzimuthRad = azimuthRad;
		ElevationRad = elevationRad;

		Forward = (lookAt - camPos).Normalized();

		// Right-handed basis. Falls back to a world-forward reference near the poles, where
		// Forward is parallel to world up and the cross product collapses.
		Vector3 reference = Mathf.Abs(Forward.Dot(Vector3.Up)) > GimbalGuard ? Vector3.Forward : Vector3.Up;
		Right = Forward.Cross(reference).Normalized();
		Up = Right.Cross(Forward).Normalized();
	}

	/// <summary>
	/// Orthographic. Elevation tips the camera above the horizon; azimuth spins it about world up.
	/// The 30/45 default is the classic isometric: +X reads right-and-down, +Z left-and-down, +Y up.
	/// </summary>
	public static Projector Isometric(float scale, float elevationDeg = 30f, float azimuthDeg = 45f)
	{
		float azimuth = Mathf.DegToRad(azimuthDeg);
		float elevation = Mathf.DegToRad(elevationDeg);
		Vector3 camPos = SphericalOffset(azimuth, elevation);

		return new Projector(camPos, Vector3.Zero, 1f, scale, isPerspective: false, azimuth, elevation);
	}

	public static Projector Perspective(Vector3 camPos, Vector3 lookAt, float focalLength, float scale)
	{
		Vector3 offset = camPos - lookAt;
		float radius = offset.Length();
		float azimuth = radius > 0f ? Mathf.Atan2(offset.X, offset.Z) : 0f;
		float elevation = radius > 0f ? Mathf.Asin(Mathf.Clamp(offset.Y / radius, -1f, 1f)) : 0f;

		return new Projector(camPos, lookAt, focalLength, scale, isPerspective: true, azimuth, elevation);
	}

	/// <summary>
	/// Godot 2D is Y-down, so world up maps to negative screen Y. Flipping this mirrors every
	/// wireframe vertically.
	/// </summary>
	public Vector2 Project(Vector3 p)
	{
		Vector3 v = p - CamPos;
		float x = v.Dot(Right);
		float y = v.Dot(Up);

		if (!IsPerspective)
		{
			return new Vector2(x, -y) * Scale;
		}

		float depth = Mathf.Max(v.Dot(Forward), MinDepth);
		return new Vector2(x, -y) * (FocalLength / depth) * Scale;
	}

	public void ProjectMany(ReadOnlySpan<Vector3> source, Span<Vector2> destination)
	{
		int count = Math.Min(source.Length, destination.Length);
		for (int i = 0; i < count; i++)
		{
			destination[i] = Project(source[i]);
		}
	}

	public void ProjectMany(ReadOnlySpan<Vector3> source, bool closed, FlatPath output)
	{
		output.Clear();
		output.EnsureCapacity(source.Length);
		for (int i = 0; i < source.Length; i++)
		{
			output.Add(Project(source[i]));
		}

		output.Closed = closed;
		output.FinalizeT();
	}

	/// <summary>
	/// Rebuilds from the stored angles rather than rotating the current basis, so a turntable
	/// animation stepping this every frame does not accumulate drift.
	/// </summary>
	public Projector WithYawPitch(float yaw, float pitch)
	{
		float azimuth = AzimuthRad + yaw;
		float elevation = ElevationRad + pitch;

		if (!IsPerspective)
		{
			return new Projector(
				SphericalOffset(azimuth, elevation),
				Vector3.Zero,
				FocalLength,
				Scale,
				isPerspective: false,
				azimuth,
				elevation);
		}

		float radius = (CamPos - LookAt).Length();
		Vector3 orbited = LookAt + (SphericalOffset(azimuth, elevation) * radius);
		return new Projector(orbited, LookAt, FocalLength, Scale, isPerspective: true, azimuth, elevation);
	}

	private static Vector3 SphericalOffset(float azimuthRad, float elevationRad)
	{
		float cosElevation = Mathf.Cos(elevationRad);
		return new Vector3(
			cosElevation * Mathf.Sin(azimuthRad),
			Mathf.Sin(elevationRad),
			cosElevation * Mathf.Cos(azimuthRad));
	}
}
