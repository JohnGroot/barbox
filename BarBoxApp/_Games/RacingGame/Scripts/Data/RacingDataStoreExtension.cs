using BarBox.Games.Racing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// DataStore extensions for RacingGame - adds racing-specific properties to core DataStore classes
/// These extend the nested classes inside DataStore
/// </summary>
public partial class DataStore
{
	public partial class GlobalUserData
	{
		// Racing game database (cross-location)
		public RaceDatabase RaceDb { get; set; } = new();
	}
}

/// <summary>
/// Racing-specific DataStore extension methods
/// Provides convenient access to racing queries while keeping Core systems game-agnostic
/// </summary>
public static class RacingDataStoreExtensions
{
	// ============= RACING GAME QUERY EXTENSIONS =============

	/// <summary>
	/// Get leaderboard for a specific track with convenient error handling
	/// </summary>
	/// <param name="store">DataStore instance</param>
	/// <param name="trackId">Track identifier</param>
	/// <param name="lapCount">Number of laps for the race</param>
	/// <param name="limit">Maximum number of entries to return</param>
	/// <param name="locationId">Location filter (null for global leaderboard)</param>
	/// <returns>List of race entries or empty list if error</returns>
	public static async Task<List<RaceEntry>> GetRacingLeaderboardAsync(
		this DataStore store,
		string trackId,
		int lapCount,
		int limit = 10,
		string locationId = null)
	{
		if (store == null || string.IsNullOrEmpty(trackId))
			return [];

		try
		{
			var parameters = new Dictionary<string, object>
			{
				["trackId"] = trackId,
				["lapCount"] = lapCount,
				["limit"] = limit
			};

			if (!string.IsNullOrEmpty(locationId))
				parameters["locationId"] = locationId;

			// Use the generic query mechanism
			var result = await store.ExecuteGameQueryAsync<List<RaceEntry>>("racing", "leaderboard", parameters);

			if (result.IsSuccess)
				return result.Value ?? [];

			// If backend doesn't support the query (LocalFileBackend case), do it locally
			if (result.Error.Contains("Unsupported query type"))
			{
				return await GetRacingLeaderboardLocalAsync(store, trackId, lapCount, limit, locationId);
			}

			// Real error - log and return empty
			GD.Print($"[RacingDataStoreExtensions] Failed to get leaderboard for track {trackId}: {result.Error}");
			return [];
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[RacingDataStoreExtensions] Exception getting racing leaderboard: {ex.Message}");
			return [];
		}
	}

	/// <summary>
	/// Get best lap time for a track with convenient error handling
	/// </summary>
	/// <param name="store">DataStore instance</param>
	/// <param name="trackId">Track identifier</param>
	/// <param name="locationId">Location filter (null for global best)</param>
	/// <returns>Best lap race entry or null if none found</returns>
	public static async Task<RaceEntry> GetRacingBestLapAsync(
		this DataStore store,
		string trackId,
		string locationId = null)
	{
		if (store == null || string.IsNullOrEmpty(trackId))
			return null;

		try
		{
			var parameters = new Dictionary<string, object>
			{
				["trackId"] = trackId
			};

			if (!string.IsNullOrEmpty(locationId))
				parameters["locationId"] = locationId;

			// Use the generic query mechanism
			var result = await store.ExecuteGameQueryAsync<RaceEntry>("racing", "best_lap", parameters);

			if (result.IsSuccess)
				return result.Value;

			// If backend doesn't support the query (LocalFileBackend case), do it locally
			if (result.Error.Contains("Unsupported query type"))
			{
				return await GetRacingBestLapLocalAsync(store, trackId, locationId);
			}

			// Real error - log and return null
			GD.Print($"[RacingDataStoreExtensions] Failed to get best lap for track {trackId}: {result.Error}");
			return null;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[RacingDataStoreExtensions] Exception getting racing best lap: {ex.Message}");
			return null;
		}
	}

	/// <summary>
	/// Submit a race entry with convenient error handling
	/// </summary>
	/// <param name="store">DataStore instance</param>
	/// <param name="phoneNumber">User's phone number</param>
	/// <param name="raceEntry">Completed race entry</param>
	/// <returns>True if submission was successful</returns>
	public static async Task<bool> SubmitRacingEntryAsync(
		this DataStore store,
		string phoneNumber,
		RaceEntry raceEntry)
	{
		if (store == null || string.IsNullOrEmpty(phoneNumber) || raceEntry == null)
			return false;

		try
		{
			// For race submission, we can use the existing user-centric methods
			// since races are stored in the user's GlobalUserData
			var globalResult = await store.GetGlobalDataAsync(phoneNumber);
			if (!globalResult.IsSuccess)
			{
				GD.PrintErr($"[RacingDataStoreExtensions] Failed to get user global data: {globalResult.Error}");
				return false;
			}

			// Add race entry to user's race database
			globalResult.Value.RaceDb.AddRaceEntry(raceEntry);

			// Save updated global data
			var setResult = await store.SetGlobalDataAsync(phoneNumber, globalResult.Value);
			if (!setResult.IsSuccess)
			{
				GD.PrintErr($"[RacingDataStoreExtensions] Failed to save race entry: {setResult.Error}");
				return false;
			}

			GD.Print($"Race entry submitted for user {phoneNumber}: Track {raceEntry.TrackId}, Time {raceEntry.TotalTime:F2}s");
			return true;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[RacingDataStoreExtensions] Exception submitting race entry: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Get comprehensive racing statistics for a track
	/// </summary>
	/// <param name="store">DataStore instance</param>
	/// <param name="trackId">Track identifier</param>
	/// <param name="lapCount">Number of laps</param>
	/// <returns>Racing statistics with leaderboard and best lap info</returns>
	public static async Task<RacingTrackStats> GetRacingTrackStatsAsync(
		this DataStore store,
		string trackId,
		int lapCount)
	{
		if (store == null || string.IsNullOrEmpty(trackId))
			return new RacingTrackStats { TrackId = trackId, LapCount = lapCount };

		var leaderboardTask = store.GetRacingLeaderboardAsync(trackId, lapCount, 10);
		var bestLapTask = store.GetRacingBestLapAsync(trackId);

		await Task.WhenAll(leaderboardTask, bestLapTask);

		return new RacingTrackStats
		{
			TrackId = trackId,
			LapCount = lapCount,
			Leaderboard = await leaderboardTask,
			BestLapEntry = await bestLapTask
		};
	}

	// ============= LOCAL FILE BACKEND IMPLEMENTATIONS =============

	/// <summary>
	/// Local implementation for leaderboard queries (used when backend doesn't support the query)
	/// </summary>
	private static async Task<List<RaceEntry>> GetRacingLeaderboardLocalAsync(
		DataStore store,
		string trackId,
		int lapCount,
		int limit,
		string locationId)
	{
		try
		{
			// Get all global user data from the backend
			var allUserDataResult = await store.ExecuteGameQueryAsync<List<DataStore.GlobalUserData>>("racing", "all_global_data", new Dictionary<string, object>());

			if (!allUserDataResult.IsSuccess)
			{
				GD.PrintErr($"[RacingDataStoreExtensions] Failed to get all user data: {allUserDataResult.Error}");
				return [];
			}

			var allUserData = allUserDataResult.Value ?? [];

			// Extract and filter races from all users using LINQ
			var allRaces = allUserData
				.Where(userData => userData?.RaceDb?.RacesByTrack != null)
				.Where(userData => userData.RaceDb.RacesByTrack.TryGetValue(trackId, out _))
				.SelectMany(userData => userData.RaceDb.RacesByTrack[trackId]
					.Where(race => race.TotalLaps == lapCount &&
								   race.TotalTime > 0f &&
								   (locationId == null || race.PlayerId == userData.PhoneNumber)));

			// Sort by total time and take the requested limit
			return allRaces
				.OrderBy(race => race.TotalTime)
				.Take(limit)
				.ToList();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[RacingDataStoreExtensions] Exception in local leaderboard query: {ex.Message}");
			return [];
		}
	}

	/// <summary>
	/// Local implementation for best lap queries (used when backend doesn't support the query)
	/// </summary>
	private static async Task<RaceEntry> GetRacingBestLapLocalAsync(
		DataStore store,
		string trackId,
		string locationId)
	{
		try
		{
			// Get all global user data from the backend
			var allUserDataResult = await store.ExecuteGameQueryAsync<List<DataStore.GlobalUserData>>("racing", "all_global_data", new Dictionary<string, object>());

			if (!allUserDataResult.IsSuccess)
			{
				GD.PrintErr($"[RacingDataStoreExtensions] Failed to get all user data: {allUserDataResult.Error}");
				return null;
			}

			var allUserData = allUserDataResult.Value ?? [];

			// Extract and filter races from all users using LINQ, then find best lap
			return allUserData
				.Where(userData => userData?.RaceDb?.RacesByTrack != null)
				.Where(userData => userData.RaceDb.RacesByTrack.TryGetValue(trackId, out _))
				.SelectMany(userData => userData.RaceDb.RacesByTrack[trackId]
					.Where(race => race.LapTimes is { Length: > 0 } &&
								   race.LapTimes.Any(lap => lap > 0f) &&
								   (locationId == null || race.PlayerId == userData.PhoneNumber)))
				.Where(race => race.LapTimes.Any(lap => lap > 0f))
				.OrderBy(race => race.LapTimes.Where(lap => lap > 0f).Min())
				.FirstOrDefault();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[RacingDataStoreExtensions] Exception in local best lap query: {ex.Message}");
			return null;
		}
	}

}

/// <summary>
/// Helper class for comprehensive racing track statistics
/// </summary>
public class RacingTrackStats
{
	public string TrackId { get; set; }
	public int LapCount { get; set; }
	public List<RaceEntry> Leaderboard { get; set; } = new();
	public RaceEntry BestLapEntry { get; set; }

	/// <summary>
	/// Get the fastest race time from the leaderboard
	/// </summary>
	public float GetBestRaceTime()
	{
		return Leaderboard.FirstOrDefault()?.TotalTime ?? 0f;
	}

	/// <summary>
	/// Get the fastest individual lap time
	/// </summary>
	public float GetBestLapTime()
	{
		if (BestLapEntry?.LapTimes is { Length: > 0 })
		{
			return BestLapEntry.LapTimes.Where(lap => lap > 0f).Min();
		}
		return 0f;
	}

	/// <summary>
	/// Check if the user has a personal best on this track
	/// </summary>
	public bool HasUserEntry(string phoneNumber)
	{
		return Leaderboard.Any(entry => entry.PlayerId == phoneNumber);
	}

	/// <summary>
	/// Get user's best position on the leaderboard (1-based, 0 if not found)
	/// </summary>
	public int GetUserBestPosition(string phoneNumber)
	{
		for (int i = 0; i < Leaderboard.Count; i++)
		{
			if (Leaderboard[i].PlayerId == phoneNumber)
				return i + 1;
		}
		return 0;
	}
}