using Godot;

public interface IRacingTrackDefinition
{
	/// <summary>
	/// Setups or resets the track for a race
	/// </summary>
	void SetupTrack();

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

	/// <summary>
	/// Get the world position of the starting line
	/// </summary>
	Vector2 GetStartLinePosition();

	/// <summary>
	/// Get the direction vector for the starting line (perpendicular to track)
	/// </summary>
	Vector2 GetStartLineDirection();

}