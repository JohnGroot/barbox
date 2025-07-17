using Godot;

public interface ITrackGenerator
{
	/// <summary>
	/// Generate a new track using the configured parameters
	/// </summary>
	void GenerateTrack();

	/// <summary>
	/// Get the generated track curve for gameplay
	/// </summary>
	/// <returns>The generated track curve, or null if no track has been generated</returns>
	Curve2D GetTrackCurve();

	/// <summary>
	/// Check if a given point is valid for track generation
	/// </summary>
	/// <param name="point">The point to check</param>
	/// <returns>True if the point is valid for track generation</returns>
	bool IsValidTrackPoint(Vector2 point);
}