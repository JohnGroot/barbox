using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace BarBox.Games.Racing
{
	// ============= RACE TIME ENTRY =============

	/// <summary>
	/// Represents a race time entry with associated username
	/// </summary>
	[Serializable]
	public class RaceTimeEntry
	{
		public float Time { get; set; }
		public string Username { get; set; } = string.Empty;

		public RaceTimeEntry() { }

		public RaceTimeEntry(float time, string username)
		{
			Time = time;
			Username = username ?? string.Empty;
		}
	}

	// ============= GLOBAL DATA STORE =============

	/// <summary>
	/// Racing game global data structure for DataStore integration.
	/// Stores cross-location racing data like best times and global ranking.
	/// 
	/// KEY FORMATS:
	/// - Best Lap Times: "{trackId}_bestlap" (e.g., "gocart_track_bestlap")
	/// - Best Race Times: "{trackId}_{laps}laps" (e.g., "gocart_track_3laps")
	/// Each entry stores both time and username for proper leaderboard display
	/// </summary>
	[Serializable]
	public class RacingGlobalDataStore
	{
		public Dictionary<string, RaceTimeEntry> BestLapTimes { get; set; } = new();
		public int TotalRaces { get; set; } = 0;
		public int GlobalRanking { get; set; } = 0;
		public Dictionary<string, RaceTimeEntry> BestRaceTimes { get; set; } = new();

		/// <summary>
		/// Generate stable track ID from scene resource path.
		/// Converts "res://_Games/RacingGame/Scenes/_Tracks/GoCartTrack.tscn" → "gocart_track"
		/// </summary>
		public static string GenerateTrackId(PackedScene trackScene)
		{
			if (trackScene?.ResourcePath == null)
				return "unknown_track";

			return trackScene.ResourcePath
				.GetFile()				// "GoCartTrack.tscn"
				.GetBaseName()			// "GoCartTrack"
				.ToSnakeCase()			// "go_cart_track"
				.ToLowerInvariant();	// "go_cart_track"
		}

		/// <summary>
		/// Generate stable track ID from track name (fallback method)
		/// </summary>
		public static string GenerateTrackIdFromName(string trackName)
		{
			if (string.IsNullOrEmpty(trackName))
				return "unknown_track";

			return trackName
				.ToSnakeCase()
				.ToLowerInvariant()
				.Replace(" ", "_");
		}

		/// <summary>
		/// Get best lap time key for a track
		/// </summary>
		public static string GetBestLapKey(string trackId)
		{
			return $"{trackId}_bestlap";
		}

		/// <summary>
		/// Get best race time key for a track with specific lap count
		/// </summary>
		public static string GetBestRaceKey(string trackId, int laps)
		{
			return $"{trackId}_{laps}laps";
		}

		/// <summary>
		/// Get best lap time for a track
		/// </summary>
		public float GetBestLapTime(string trackId)
		{
			var key = GetBestLapKey(trackId);
			return BestLapTimes.TryGetValue(key, out RaceTimeEntry entry) ? entry.Time : 0f;
		}

		/// <summary>
		/// Get best lap time entry (with username) for a track
		/// </summary>
		public RaceTimeEntry GetBestLapTimeEntry(string trackId)
		{
			var key = GetBestLapKey(trackId);
			return BestLapTimes.TryGetValue(key, out RaceTimeEntry entry) ? entry : null;
		}

		/// <summary>
		/// Get best race time for a track with specific lap count
		/// </summary>
		public float GetBestRaceTime(string trackId, int laps)
		{
			var key = GetBestRaceKey(trackId, laps);
			return BestRaceTimes.TryGetValue(key, out RaceTimeEntry entry) ? entry.Time : 0f;
		}

		/// <summary>
		/// Get best race time entry (with username) for a track with specific lap count
		/// </summary>
		public RaceTimeEntry GetBestRaceTimeEntry(string trackId, int laps)
		{
			var key = GetBestRaceKey(trackId, laps);
			return BestRaceTimes.TryGetValue(key, out RaceTimeEntry entry) ? entry : null;
		}

		/// <summary>
		/// Set best lap time with username
		/// </summary>
		public void SetBestLapTime(string trackId, float time, string username)
		{
			var key = GetBestLapKey(trackId);
			BestLapTimes[key] = new RaceTimeEntry(time, username);
		}

		/// <summary>
		/// Set best race time with username
		/// </summary>
		public void SetBestRaceTime(string trackId, int laps, float time, string username)
		{
			var key = GetBestRaceKey(trackId, laps);
			BestRaceTimes[key] = new RaceTimeEntry(time, username);
			TotalRaces++;
		}

		/// <summary>
		/// Get all tracks that have recorded times
		/// </summary>
		public HashSet<string> GetTracksWithTimes()
		{
			var tracks = new HashSet<string>();

			// Extract track IDs from lap time keys
			foreach (var key in BestLapTimes.Keys)
			{
				if (key.EndsWith("_bestlap"))
				{
					tracks.Add(key.Substring(0, key.Length - "_bestlap".Length));
				}
			}

			// Extract track IDs from race time keys
			foreach (var key in BestRaceTimes.Keys)
			{
				if (key.Contains("laps"))
				{
					var lapIndex = key.LastIndexOf("_");
					if (lapIndex > 0)
					{
						tracks.Add(key.Substring(0, lapIndex));
					}
				}
			}

			return tracks;
		}
	}

	// ============= LOCAL DATA =============

	/// <summary>
	/// Racing game local data structure for DataStore integration.
	/// Stores location-specific racing data like session stats and favorite tracks.
	/// </summary>
	[Serializable]
	public class RacingLocalData
	{
		public DateTime LastPlayedAt { get; set; } = DateTime.UtcNow;
		public int RacesAtLocation { get; set; }
		public string FavoriteTrack { get; set; } = string.Empty;
		public int SessionRaces { get; set; }
		public int SessionPersonalBests { get; set; }
	}
}