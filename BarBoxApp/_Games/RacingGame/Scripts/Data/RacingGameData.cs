using System;
using Godot;

namespace BarBox.Games.Racing;

// ============= RACE DATABASE RECORDS =============
[Serializable]
public record CheckpointTime(
	int Index,
	float Time,
	float Gap);

/// <summary>
/// Only created when races are fully completed
/// </summary>
[Serializable]
public record RaceEntry(
	string RaceId,
	DateTime Date,
	string TrackId,
	string PlayerId,
	string Username,
	int TotalLaps,
	float TotalTime,
	float[] LapTimes,
	CheckpointTime[] Checkpoints);

// ============= RACE DATABASE =============

/// <summary>
/// Static utility methods for race ID generation.
/// Race data persistence is handled by the backend via event-sourcing.
/// </summary>
public static class RaceDatabase
{
	/// <summary>
	/// Example: "GoCartTrack.tscn" -> "go_cart_track"
	/// </summary>
	public static string GenerateTrackId(PackedScene trackScene)
	{
		if (trackScene?.ResourcePath == null)
		{
			return "unknown_track";
		}

		return trackScene.ResourcePath
			.GetFile() // "GoCartTrack.tscn"
			.GetBaseName() // "GoCartTrack"
			.ToSnakeCase() // "go_cart_track"
			.ToLowerInvariant();    // "go_cart_track"
	}

	public static string GenerateTrackIdFromName(string trackName)
	{
		if (string.IsNullOrEmpty(trackName))
		{
			return "unknown_track";
		}

		return trackName
			.ToSnakeCase()
			.ToLowerInvariant()
			.Replace(" ", "_");
	}

	public static string GenerateRaceId()
	{
		return Guid.NewGuid().ToString("N")[..12]; // Short unique ID
	}
}
