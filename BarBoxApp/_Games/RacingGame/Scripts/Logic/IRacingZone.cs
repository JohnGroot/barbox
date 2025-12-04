namespace BarBox.Games.Racing;

/// <summary>
/// Types of racing zones that affect car behavior
/// </summary>
public enum ZoneType
{
	/// <summary>Reduces car speed and acceleration</summary>
	Slowdown,
	/// <summary>Increases car speed and acceleration</summary>
	Boost,
	/// <summary>Locks velocity and blocks input for duration</summary>
	Frictionless,
	/// <summary>Decorative track edge that counts as on-track</summary>
	Kerb
}

/// <summary>
/// Interface for racing zones that apply modifiers to car behavior
/// </summary>
public interface IRacingZone
{
	/// <summary>Type of zone for categorization and special handling</summary>
	ZoneType Type { get; }

	/// <summary>Speed multiplier (1.0 = normal, 0.5 = half speed, 2.0 = double)</summary>
	float SpeedModifier { get; }

	/// <summary>Acceleration multiplier (1.0 = normal)</summary>
	float AccelerationModifier { get; }

	/// <summary>Turn/rotation multiplier (1.0 = normal)</summary>
	float TurnModifier { get; }

	/// <summary>Whether this zone blocks player input (frictionless zones)</summary>
	bool BlocksInput { get; }

	/// <summary>Duration of effect in seconds (0 = while in zone)</summary>
	float Duration { get; }
}
