using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BarBox.Core.Utils;
using BarBox.Games.Racing;
using Godot;
using LightResults;

/// <summary>
/// Racing game-specific event service
/// Provides type-safe methods for emitting racing events
/// Inherits validation and error handling from GameEventServiceBase
/// </summary>
public class RacingEventService : GameEventServiceBase
{
	public RacingEventService(SessionEventService eventService = null)
		: base(eventService)
	{
	}

	public async Task<Result<bool>> EmitLapCompleteAsync(string trackId, int lapNum, float lapTime, CheckpointTime[] checkpoints)
	{
		// Input validation
		if (string.IsNullOrEmpty(trackId))
		{
			return Result.Failure<bool>(ValidationMessages.Required("Track ID"));
		}

		if (lapNum < 1)
		{
			return Result.Failure<bool>(ValidationMessages.MinValue("Lap number", 1));
		}

		if (lapTime < 0)
		{
			return Result.Failure<bool>(ValidationMessages.MinValue("Lap time", 0));
		}

		if (checkpoints == null || checkpoints.Length == 0)
		{
			return Result.Failure<bool>(ValidationMessages.Required("Checkpoint data"));
		}

		// Validate checkpoint data integrity
		if (!ValidateCheckpointData(checkpoints, out string validationError))
		{
			return Result.Failure<bool>(validationError);
		}

		// Convert checkpoints array to anonymous objects manually (avoid Linq)
		var checkpointArray = new object[checkpoints.Length];
		for (int i = 0; i < checkpoints.Length; i++)
		{
			checkpointArray[i] = new
			{
				index = checkpoints[i].Index,
				time = checkpoints[i].Time,
				gap = checkpoints[i].Gap,
			};
		}

		var payload = new
		{
			track_id = trackId,
			lap_num = lapNum,
			lap_time = lapTime,
			checkpoints = checkpointArray,
		};

		return await SubmitResultAsync("racing/lap_complete", payload);
	}

	/// <summary>
	/// Ensures indices are sequential, times monotonically increasing, gaps positive
	/// </summary>
	private bool ValidateCheckpointData(CheckpointTime[] checkpoints, out string error)
	{
		error = null;

		// Check sequential indices
		for (int i = 0; i < checkpoints.Length; i++)
		{
			if (checkpoints[i].Index != i)
			{
				error = $"Checkpoint index mismatch at position {i}: expected {i}, got {checkpoints[i].Index}";
				return false;
			}
		}

		// Check monotonically increasing times
		for (int i = 1; i < checkpoints.Length; i++)
		{
			if (checkpoints[i].Time <= checkpoints[i - 1].Time)
			{
				error = $"Checkpoint times not monotonically increasing at index {i}: {checkpoints[i].Time} <= {checkpoints[i - 1].Time}";
				return false;
			}
		}

		// Check positive gap times
		for (int i = 0; i < checkpoints.Length; i++)
		{
			if (checkpoints[i].Gap < 0)
			{
				error = $"Negative gap time at checkpoint {i}: {checkpoints[i].Gap}";
				return false;
			}
		}

		return true;
	}

	public async Task<Result<bool>> EmitRaceFinishAsync(RaceEntry raceEntry)
	{
		// Input validation
		if (raceEntry == null)
		{
			return Result.Failure<bool>(ValidationMessages.Required("Race entry"));
		}

		if (string.IsNullOrEmpty(raceEntry.TrackId))
		{
			return Result.Failure<bool>(ValidationMessages.Required("Track ID"));
		}

		if (raceEntry.TotalTime < 0)
		{
			return Result.Failure<bool>(ValidationMessages.MinValue("Total time", 0));
		}

		if (raceEntry.TotalLaps < 1)
		{
			return Result.Failure<bool>(ValidationMessages.MinValue("Lap count", 1));
		}

		// Validate race data integrity
		if (!ValidateRaceDataIntegrity(raceEntry, out string integrityError))
		{
			return Result.Failure<bool>(integrityError);
		}

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
					gap = raceEntry.Checkpoints[i].Gap,
				};
			}
		}

		var payload = new
		{
			track_id = raceEntry.TrackId,
			total_time = raceEntry.TotalTime,
			total_laps = raceEntry.TotalLaps,
			lap_times = raceEntry.LapTimes,
			checkpoints = checkpointArray,
		};

		return await SubmitResultAsync("racing/race_finish", payload);
	}

	/// <summary>
	/// Validates data structure correctness without time limits - trusts physics system
	/// </summary>
	private bool ValidateRaceDataIntegrity(RaceEntry raceEntry, out string error)
	{
		error = null;

		// Check data structure integrity
		if (raceEntry.LapTimes != null)
		{
			// Verify lap count matches
			if (raceEntry.LapTimes.Length != raceEntry.TotalLaps)
			{
				error = $"Lap count mismatch: expected {raceEntry.TotalLaps}, got {raceEntry.LapTimes.Length} lap times";
				return false;
			}

			// Check all lap times are positive (physics can't produce negative time)
			for (int i = 0; i < raceEntry.LapTimes.Length; i++)
			{
				if (raceEntry.LapTimes[i] <= 0)
				{
					error = $"Invalid lap time at lap {i + 1}: {raceEntry.LapTimes[i]:F3}s (must be positive)";
					return false;
				}
			}

			// Check total time consistency with lap times sum (exact match within float precision)
			float lapTimesSum = 0f;
			for (int i = 0; i < raceEntry.LapTimes.Length; i++)
			{
				lapTimesSum += raceEntry.LapTimes[i];
			}

			// Use tight tolerance for actual float precision (not arbitrary 10%)
			const float FLOAT_PRECISION_TOLERANCE = 0.001f;
			if (System.Math.Abs(raceEntry.TotalTime - lapTimesSum) > FLOAT_PRECISION_TOLERANCE)
			{
				error = $"Total time ({raceEntry.TotalTime:F3}s) doesn't match lap times sum ({lapTimesSum:F3}s)";
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// Preserves partial race data when player quits mid-race
	/// </summary>
	public async Task<Result<bool>> EmitRaceAbandonedAsync(string trackId, string playerId, int completedLaps, float[] lapTimes, string reason)
	{
		// Input validation
		if (string.IsNullOrEmpty(trackId))
		{
			return Result.Failure<bool>(ValidationMessages.Required("Track ID"));
		}

		if (string.IsNullOrEmpty(playerId))
		{
			return Result.Failure<bool>(ValidationMessages.Required("Player ID"));
		}

		if (completedLaps < 0)
		{
			return Result.Failure<bool>(ValidationMessages.MinValue("Completed laps", 0));
		}

		// Allow empty lap times array (race abandoned before completing any lap)
		lapTimes ??= [];

		var payload = new
		{
			track_id = trackId,
			player_id = playerId,
			completed_laps = completedLaps,
			lap_times = lapTimes,
			reason = reason ?? "unknown",
		};

		return await SubmitResultAsync("racing/race_abandoned", payload);
	}

	/// <summary>
	/// Get racing leaderboard from backend
	/// Aggregates best times from racing events for specified track and metric
	/// </summary>
	/// <param name="trackId">Track identifier (e.g., "gocart_track")</param>
	/// <param name="metric">Leaderboard metric: "best_lap" or "best_race"</param>
	/// <param name="laps">Required for "best_race" metric (number of laps for the race)</param>
	/// <param name="limit">Maximum number of entries to return (1-100)</param>
	/// <param name="expectedLaps">Expected lap count for validation (optional)</param>
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
		int limit = 10,
		int? expectedLaps = null)
	{
		// Input validation
		if (string.IsNullOrEmpty(trackId))
		{
			return Result.Failure<RacingLeaderboardData>(ValidationMessages.Required("Track ID"));
		}

		var limitValidation = ValidateLeaderboardLimit(limit);
		if (limitValidation.IsFailure(out var limitError))
		{
			return Result.Failure<RacingLeaderboardData>(limitError.Message);
		}

		// Validate lap count if provided
		if (laps.HasValue && expectedLaps.HasValue && laps.Value != expectedLaps.Value)
		{
			return Result.Failure<RacingLeaderboardData>(
				$"Leaderboard query lap count ({laps.Value}) doesn't match track configuration ({expectedLaps.Value})");
		}

		// Build query parameters
		var queryParams = new Dictionary<string, string>
		{
			{ "track_id", trackId },
			{ "metric", metric },
			{ "limit", limit.ToString() },
		};

		if (laps.HasValue)
		{
			queryParams["laps"] = laps.Value.ToString();
		}

		return await QueryBackendAsync(
			ApiPaths.Racing.Leaderboard,
			queryParams,
			jsonDict => ParseLeaderboardData(jsonDict, metric));
	}

	private Result<RacingLeaderboardData> ParseLeaderboardData(Godot.Collections.Dictionary jsonDict, string metric)
	{
		if (!jsonDict.ContainsKey(JsonFieldNames.TrackId))
		{
			return Result.Failure<RacingLeaderboardData>($"Missing required field: {JsonFieldNames.TrackId}");
		}

		if (!jsonDict.ContainsKey(JsonFieldNames.Metric))
		{
			return Result.Failure<RacingLeaderboardData>($"Missing required field: {JsonFieldNames.Metric}");
		}

		if (!jsonDict.ContainsKey(JsonFieldNames.Leaderboard))
		{
			return Result.Failure<RacingLeaderboardData>($"Missing required field: {JsonFieldNames.Leaderboard}");
		}

		var leaderboardData = new RacingLeaderboardData
		{
			TrackId = jsonDict[JsonFieldNames.TrackId].AsString(),
			Metric = jsonDict[JsonFieldNames.Metric].AsString(),
			Leaderboard = new List<RacingLeaderboardEntry>(),
		};

		var leaderboardArray = jsonDict[JsonFieldNames.Leaderboard].AsGodotArray();
		foreach (var entry in leaderboardArray)
		{
			var entryDict = entry.AsGodotDictionary();

			var playerIdResult = JsonParsingHelpers.ParseGuid(entryDict, JsonFieldNames.PlayerId);
			var entryDateResult = JsonParsingHelpers.ParseDateTime(entryDict, JsonFieldNames.EntryDate);

			if (playerIdResult.IsFailure(out var playerIdError))
			{
				return Result.Failure<RacingLeaderboardData>(playerIdError.Message);
			}

			if (entryDateResult.IsFailure(out var entryDateError))
			{
				return Result.Failure<RacingLeaderboardData>(entryDateError.Message);
			}

			if (!entryDict.ContainsKey(JsonFieldNames.Username))
			{
				return Result.Failure<RacingLeaderboardData>($"Missing required field: {JsonFieldNames.Username}");
			}

			if (!entryDict.ContainsKey(JsonFieldNames.MetricValue))
			{
				return Result.Failure<RacingLeaderboardData>($"Missing required field: {JsonFieldNames.MetricValue}");
			}

			if (!playerIdResult.IsSuccess(out var playerId))
			{
				return Result.Failure<RacingLeaderboardData>("Failed to parse player ID");
			}

			if (!entryDateResult.IsSuccess(out var entryDate))
			{
				return Result.Failure<RacingLeaderboardData>("Failed to parse entry date");
			}

			// Parse lap_times if present (only for best_race metric)
			float[] lapTimes = null;
			if (!JsonParsingHelpers.IsNullOrMissing(entryDict, JsonFieldNames.LapTimes))
			{
				var lapTimesArray = entryDict[JsonFieldNames.LapTimes].AsGodotArray();
				lapTimes = new float[lapTimesArray.Count];
				for (int i = 0; i < lapTimesArray.Count; i++)
				{
					lapTimes[i] = (float)lapTimesArray[i].AsDouble();
				}
			}

			var leaderboardEntry = new RacingLeaderboardEntry
			{
				PlayerId = playerId,
				Username = entryDict[JsonFieldNames.Username].AsString(),
				MetricValue = (float)entryDict[JsonFieldNames.MetricValue].AsDouble(),
				EntryDate = entryDate,
				LapTimes = lapTimes,
			};

			leaderboardData.Leaderboard.Add(leaderboardEntry);
		}

		return Result.Success(leaderboardData);
	}
}

public record class RacingLeaderboardData
{
	public string TrackId { get; init; }

	public string Metric { get; init; }

	public List<RacingLeaderboardEntry> Leaderboard { get; init; }
}

public record class RacingLeaderboardEntry
{
	public Guid PlayerId { get; init; }

	public string Username { get; init; }

	public float MetricValue { get; init; }

	public DateTime EntryDate { get; init; }

	public float[] LapTimes { get; init; }
}
