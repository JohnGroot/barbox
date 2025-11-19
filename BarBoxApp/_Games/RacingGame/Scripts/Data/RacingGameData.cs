using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace BarBox.Games.Racing;

// ============= RACE DATABASE RECORDS =============

	[Serializable]
	public record CheckpointTime(
		int Index,
		float Time,
		float Gap
	);

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
		CheckpointTime[] Checkpoints
	);

	// ============= RACE DATABASE =============

	[Serializable]
	public class RaceDatabase
	{
		public Dictionary<string, List<RaceEntry>> RacesByTrack { get; set; } = new();
		public int TotalRacesCompleted { get; set; } = 0;

		/// <summary>
		/// Example: "GoCartTrack.tscn" → "go_cart_track"
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
		/// Only call when race is fully completed
		/// </summary>
		public void AddRaceEntry(RaceEntry raceEntry)
		{
			if (raceEntry == null) return;

			if (!RacesByTrack.ContainsKey(raceEntry.TrackId))
			{
				RacesByTrack[raceEntry.TrackId] = new List<RaceEntry>();
			}

			RacesByTrack[raceEntry.TrackId].Add(raceEntry);
			TotalRacesCompleted++;
		}

		public static string GenerateRaceId()
		{
			return Guid.NewGuid().ToString("N")[..12]; // Short unique ID
		}

		public float GetBestLapTime(string trackId)
		{
			if (!RacesByTrack.ContainsKey(trackId))
				return 0f;

			return RacesByTrack[trackId]
				.SelectMany(race => race.LapTimes)
				.Where(lapTime => lapTime > 0f)
				.DefaultIfEmpty(0f)
				.Min();
		}

		public RaceEntry GetBestLapTimeEntry(string trackId)
		{
			if (!RacesByTrack.ContainsKey(trackId))
				return null;

			return RacesByTrack[trackId]
				.Where(race => race.LapTimes.Any(lap => lap > 0f))
				.OrderBy(race => race.LapTimes.Where(lap => lap > 0f).Min())
				.FirstOrDefault();
		}

		public float GetBestRaceTime(string trackId, int laps)
		{
			if (!RacesByTrack.ContainsKey(trackId))
				return 0f;

			return RacesByTrack[trackId]
				.Where(race => race.TotalLaps == laps && race.TotalTime > 0f)
				.Select(race => race.TotalTime)
				.DefaultIfEmpty(0f)
				.Min();
		}

		public RaceEntry GetBestRaceTimeEntry(string trackId, int laps)
		{
			if (!RacesByTrack.ContainsKey(trackId))
				return null;

			return RacesByTrack[trackId]
				.Where(race => race.TotalLaps == laps && race.TotalTime > 0f)
				.OrderBy(race => race.TotalTime)
				.FirstOrDefault();
		}

		public List<RaceEntry> GetRacesForTrack(string trackId)
		{
			if (!RacesByTrack.ContainsKey(trackId))
				return new List<RaceEntry>();

			return RacesByTrack[trackId]
				.OrderByDescending(race => race.Date)
				.ToList();
		}

		public List<RaceEntry> GetLeaderboard(string trackId, int laps, int maxEntries = 10)
		{
			if (!RacesByTrack.ContainsKey(trackId))
				return new List<RaceEntry>();

			return RacesByTrack[trackId]
				.Where(race => race.TotalLaps == laps && race.TotalTime > 0f)
				.OrderBy(race => race.TotalTime)
				.Take(maxEntries)
				.ToList();
		}

		public HashSet<string> GetTracksWithRaces()
		{
			return new HashSet<string>(RacesByTrack.Keys.Where(trackId => RacesByTrack[trackId].Count > 0));
		}

		public int GetRaceCountForTrack(string trackId)
		{
			return RacesByTrack.ContainsKey(trackId) ? RacesByTrack[trackId].Count : 0;
		}

		public List<RaceEntry> GetRacesByPlayer(string playerId)
		{
			return RacesByTrack.Values
				.SelectMany(races => races)
				.Where(race => race.PlayerId == playerId)
				.OrderByDescending(race => race.Date)
				.ToList();
		}
	}