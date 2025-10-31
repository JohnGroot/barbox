using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BarBox.Games.Racing;

/// <summary>
/// Racing game-specific event service
/// Provides type-safe methods for emitting racing events
/// Inherits validation and error handling from GameEventServiceBase
/// </summary>
public class RacingEventService : GameEventServiceBase
{
	public RacingEventService(EventService eventService = null) : base(eventService)
	{
	}

	/// <summary>
	/// Emit lap complete event with validation
	/// </summary>
	public async Task<Result<bool>> EmitLapCompleteAsync(string trackId, int lapNum, float lapTime, CheckpointTime[] checkpoints)
	{
		// Input validation
		if (string.IsNullOrEmpty(trackId))
			return Result<bool>.Failure(ValidationMessages.Required("Track ID"));

		if (lapNum < 1)
			return Result<bool>.Failure(ValidationMessages.MinValue("Lap number", 1));

		if (lapTime < 0)
			return Result<bool>.Failure(ValidationMessages.MinValue("Lap time", 0));

		if (checkpoints == null || checkpoints.Length == 0)
			return Result<bool>.Failure(ValidationMessages.Required("Checkpoint data"));

		// Convert checkpoints array to anonymous objects manually (avoid Linq)
		var checkpointArray = new object[checkpoints.Length];
		for (int i = 0; i < checkpoints.Length; i++)
		{
			checkpointArray[i] = new
			{
				index = checkpoints[i].Index,
				time = checkpoints[i].Time,
				gap = checkpoints[i].Gap
			};
		}

		var payload = new
		{
			track_id = trackId,
			lap_num = lapNum,
			lap_time = lapTime,
			checkpoints = checkpointArray
		};

		return await EmitEventSafeAsync("racing/lap_complete", payload);
	}

	/// <summary>
	/// Emit race finish event with validation
	/// </summary>
	public async Task<Result<bool>> EmitRaceFinishAsync(RaceEntry raceEntry)
	{
		// Input validation
		if (raceEntry == null)
			return Result<bool>.Failure(ValidationMessages.Required("Race entry"));

		if (string.IsNullOrEmpty(raceEntry.TrackId))
			return Result<bool>.Failure(ValidationMessages.Required("Track ID"));

		if (raceEntry.TotalTime < 0)
			return Result<bool>.Failure(ValidationMessages.MinValue("Total time", 0));

		if (raceEntry.TotalLaps < 1)
			return Result<bool>.Failure(ValidationMessages.MinValue("Lap count", 1));

		// Convert checkpoints array manually if not null (avoid Linq)
		object[] checkpointArray = null;
		if (raceEntry.Checkpoints != null)
		{
			checkpointArray = new object[raceEntry.Checkpoints.Length];
			for (int i = 0; i < raceEntry.Checkpoints.Length; i++)
			{
				checkpointArray[i] = new
				{
					index = raceEntry.Checkpoints[i].Index,
					time = raceEntry.Checkpoints[i].Time,
					gap = raceEntry.Checkpoints[i].Gap
				};
			}
		}

		var payload = new
		{
			track_id = raceEntry.TrackId,
			total_time = raceEntry.TotalTime,
			total_laps = raceEntry.TotalLaps,
			lap_times = raceEntry.LapTimes,
			checkpoints = checkpointArray
		};

		return await EmitEventSafeAsync("racing/race_finish", payload);
	}

	/// <summary>
	/// Get racing leaderboard from backend
	/// Aggregates best times from racing events for specified track and metric
	/// </summary>
	/// <param name="trackId">Track identifier (e.g., "gocart_track")</param>
	/// <param name="metric">Leaderboard metric: "best_lap" or "best_race"</param>
	/// <param name="laps">Required for "best_race" metric (number of laps for the race)</param>
	/// <param name="limit">Maximum number of entries to return (1-100)</param>
	/// <returns>Result containing leaderboard data or error message</returns>
	/// <remarks>
	/// For "best_lap": Returns fastest single lap time per player
	/// For "best_race": Returns fastest complete race time, optionally filtered by lap count
	/// Leaderboard includes lap times array for "best_race" metric
	/// </remarks>
	public async Task<Result<RacingLeaderboardData>> GetLeaderboardAsync(
		string trackId,
		string metric = "best_race",
		int? laps = null,
		int limit = 10)
	{
		// Input validation
		if (string.IsNullOrEmpty(trackId))
			return Result<RacingLeaderboardData>.Failure(ValidationMessages.Required("Track ID"));

		var limitValidation = ValidateLeaderboardLimit(limit);
		if (!limitValidation.IsSuccess)
			return Result<RacingLeaderboardData>.Failure(limitValidation.Error);

		// Build query parameters
		var queryParams = new Dictionary<string, string>
		{
			{ "track_id", trackId },
			{ "metric", metric },
			{ "limit", limit.ToString() }
		};

		if (laps.HasValue)
			queryParams["laps"] = laps.Value.ToString();

		return await QueryBackendAsync(
			"/game/racing/leaderboard",
			queryParams,
			jsonDict => ParseLeaderboardData(jsonDict, metric)
		);
	}

	private Result<RacingLeaderboardData> ParseLeaderboardData(Godot.Collections.Dictionary jsonDict, string metric)
	{
		if (!jsonDict.ContainsKey(JsonFieldNames.TrackId))
			return Result<RacingLeaderboardData>.Failure($"Missing required field: {JsonFieldNames.TrackId}");

		if (!jsonDict.ContainsKey(JsonFieldNames.Metric))
			return Result<RacingLeaderboardData>.Failure($"Missing required field: {JsonFieldNames.Metric}");

		if (!jsonDict.ContainsKey(JsonFieldNames.Leaderboard))
			return Result<RacingLeaderboardData>.Failure($"Missing required field: {JsonFieldNames.Leaderboard}");

		var leaderboardData = new RacingLeaderboardData
		{
			TrackId = jsonDict[JsonFieldNames.TrackId].AsString(),
			Metric = jsonDict[JsonFieldNames.Metric].AsString(),
			Leaderboard = new List<RacingLeaderboardEntry>()
		};

		var leaderboardArray = jsonDict[JsonFieldNames.Leaderboard].AsGodotArray();
		foreach (var entry in leaderboardArray)
		{
			var entryDict = entry.AsGodotDictionary();

			var playerIdResult = JsonParsingHelpers.ParseGuid(entryDict, JsonFieldNames.PlayerId);
			var entryDateResult = JsonParsingHelpers.ParseDateTime(entryDict, JsonFieldNames.EntryDate);

			if (!playerIdResult.IsSuccess) return Result<RacingLeaderboardData>.Failure(playerIdResult.Error);
			if (!entryDateResult.IsSuccess) return Result<RacingLeaderboardData>.Failure(entryDateResult.Error);

			if (!entryDict.ContainsKey(JsonFieldNames.Username))
				return Result<RacingLeaderboardData>.Failure($"Missing required field: {JsonFieldNames.Username}");

			if (!entryDict.ContainsKey(JsonFieldNames.MetricValue))
				return Result<RacingLeaderboardData>.Failure($"Missing required field: {JsonFieldNames.MetricValue}");

			var leaderboardEntry = new RacingLeaderboardEntry
			{
				PlayerId = playerIdResult.Value,
				Username = entryDict[JsonFieldNames.Username].AsString(),
				MetricValue = (float)entryDict[JsonFieldNames.MetricValue].AsDouble(),
				EntryDate = entryDateResult.Value
			};

			// Parse lap_times if present (only for best_race metric)
			if (!JsonParsingHelpers.IsNullOrMissing(entryDict, JsonFieldNames.LapTimes))
			{
				var lapTimesJson = entryDict[JsonFieldNames.LapTimes].AsString();
				var lapTimesResult = Json.ParseString(lapTimesJson);
				if (lapTimesResult.Obj != null)
				{
					var lapTimesArray = ((Variant)lapTimesResult.Obj).AsGodotArray();
					leaderboardEntry.LapTimes = new float[lapTimesArray.Count];
					for (int i = 0; i < lapTimesArray.Count; i++)
					{
						leaderboardEntry.LapTimes[i] = (float)lapTimesArray[i].AsDouble();
					}
				}
			}

			leaderboardData.Leaderboard.Add(leaderboardEntry);
		}

		return Result<RacingLeaderboardData>.Success(leaderboardData);
	}
}

/// <summary>
/// Racing leaderboard data structure
/// </summary>
public class RacingLeaderboardData
{
	public string TrackId { get; set; }
	public string Metric { get; set; }
	public List<RacingLeaderboardEntry> Leaderboard { get; set; }
}

/// <summary>
/// Racing leaderboard entry
/// </summary>
public class RacingLeaderboardEntry
{
	public Guid PlayerId { get; set; }
	public string Username { get; set; }
	public float MetricValue { get; set; }
	public DateTime EntryDate { get; set; }
	public float[] LapTimes { get; set; }
}
