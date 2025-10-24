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
			return Result<bool>.Failure("Track ID is required");

		if (lapNum < 1)
			return Result<bool>.Failure("Invalid lap number");

		if (lapTime < 0)
			return Result<bool>.Failure("Invalid lap time");

		if (checkpoints == null || checkpoints.Length == 0)
			return Result<bool>.Failure("No checkpoint data provided");

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
			return Result<bool>.Failure("Race entry cannot be null");

		if (string.IsNullOrEmpty(raceEntry.TrackId))
			return Result<bool>.Failure("Track ID is required");

		if (raceEntry.TotalTime < 0)
			return Result<bool>.Failure("Invalid total time");

		if (raceEntry.TotalLaps < 1)
			return Result<bool>.Failure("Invalid lap count");

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
	/// Get racing leaderboard from backend using shared HTTP helper
	/// </summary>
	public async Task<Result<RacingLeaderboardData>> GetLeaderboardAsync(
		string trackId,
		string metric = "best_race",
		int? laps = null,
		int limit = 10)
	{
		// Input validation
		if (string.IsNullOrEmpty(trackId))
			return Result<RacingLeaderboardData>.Failure("Track ID is required");

		if (limit < 1 || limit > 100)
			return Result<RacingLeaderboardData>.Failure("Limit must be between 1 and 100");

		try
		{
			// Build query parameters
			var queryParams = new Dictionary<string, string>
			{
				{ "track_id", trackId },
				{ "metric", metric },
				{ "limit", limit.ToString() }
			};

			if (laps.HasValue)
				queryParams["laps"] = laps.Value.ToString();

			// Use shared HTTP helper - handles connection, polling, timeouts
			var responseResult = await QueryBackendRawAsync("/game/racing/leaderboard", queryParams);
			if (!responseResult.IsSuccess)
				return Result<RacingLeaderboardData>.Failure(responseResult.Error);

			// Parse JSON response using base class helper
			var parseResult = ParseJsonResponse(responseResult.Value);
			if (!parseResult.IsSuccess)
				return Result<RacingLeaderboardData>.Failure(parseResult.Error);

			var jsonDict = parseResult.Value;

			var leaderboardData = new RacingLeaderboardData
			{
				TrackId = jsonDict["track_id"].AsString(),
				Metric = jsonDict["metric"].AsString(),
				Leaderboard = new List<RacingLeaderboardEntry>()
			};

			var leaderboardArray = jsonDict["leaderboard"].AsGodotArray();
			foreach (var entry in leaderboardArray)
			{
				var entryDict = entry.AsGodotDictionary();

				var leaderboardEntry = new RacingLeaderboardEntry
				{
					PlayerId = Guid.Parse(entryDict["player_id"].AsString()),
					Username = entryDict["username"].AsString(),
					MetricValue = (float)entryDict["metric_value"].AsDouble(),
					EntryDate = DateTime.Parse(entryDict["entry_date"].AsString())
				};

				// Parse lap_times if present
				if (entryDict.ContainsKey("lap_times") && entryDict["lap_times"].VariantType != Variant.Type.Nil)
				{
					var lapTimesJson = entryDict["lap_times"].AsString();
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
		catch (FormatException ex)
		{
			LogError($"Invalid data format in leaderboard response: {ex.Message}");
			return Result<RacingLeaderboardData>.Failure("Invalid server response format");
		}
		catch (System.Exception ex)
		{
			LogError($"Unexpected error retrieving leaderboard: {ex.Message}");
			return Result<RacingLeaderboardData>.Failure("Unable to retrieve leaderboard data");
		}
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
