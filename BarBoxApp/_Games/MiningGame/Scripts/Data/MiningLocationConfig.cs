namespace BarBox.Games.MiningGame;

using System;
using System.Text.Json.Serialization;

/// <summary>
/// Immutable location configuration from backend registration.
/// Used to configure the mining game at a specific venue.
/// </summary>
public sealed record MiningLocationConfig
{
	[JsonPropertyName("venue_name")]
	public required string VenueName { get; init; }

	[JsonPropertyName("gem_type")]
	public required string GemTypeString { get; init; }

	[JsonPropertyName("display_name")]
	public required string DisplayName { get; init; }

	/// <summary>
	/// Parse the gem type string from backend to strongly-typed enum.
	/// </summary>
	/// <exception cref="ArgumentException">Thrown when gem type string is not a valid GemType.</exception>
	public GemType GetGemType()
	{
		if (!Enum.TryParse<GemType>(GemTypeString, ignoreCase: true, out var gemType))
		{
			throw new ArgumentException($"Invalid gem type from backend: '{GemTypeString}'");
		}
		return gemType;
	}

	/// <summary>
	/// Create a fallback config for development mode when backend is unavailable.
	/// </summary>
	public static MiningLocationConfig CreateDevDefault() => new()
	{
		VenueName = "dev_location",
		GemTypeString = "amethyst",
		DisplayName = "Development Location"
	};
}
