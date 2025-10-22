using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BarBox.Games.Racing;

/// <summary>
/// Racing game-specific event service
/// Provides type-safe methods for emitting racing events
/// </summary>
public partial class RacingEventService : Node
{
	private EventService _eventService;

	public override void _Ready()
	{
		_eventService = EventService.GetInstance();
		if (_eventService == null)
		{
			GD.PrintErr("RacingEventService: EventService not found");
		}
	}

	/// <summary>
	/// Emit lap complete event
	/// </summary>
	public async Task<Result<bool>> EmitLapCompleteAsync(int lapNum, float lapTime, CheckpointTime[] checkpoints)
	{
		if (_eventService == null)
			return Result<bool>.Failure("EventService not initialized");

		var payload = new
		{
			lap_num = lapNum,
			lap_time = lapTime,
			checkpoints = checkpoints.Select(c => new
			{
				index = c.Index,
				time = c.Time,
				gap = c.Gap
			}).ToArray()
		};

		return await _eventService.EmitEventAsync("racing/lap_complete", payload);
	}

	/// <summary>
	/// Emit race finish event
	/// </summary>
	public async Task<Result<bool>> EmitRaceFinishAsync(RaceEntry raceEntry)
	{
		if (_eventService == null)
			return Result<bool>.Failure("EventService not initialized");

		var payload = new
		{
			track_id = raceEntry.TrackId,
			total_time = raceEntry.TotalTime,
			total_laps = raceEntry.TotalLaps,
			lap_times = raceEntry.LapTimes,
			checkpoints = raceEntry.Checkpoints?.Select(c => new
			{
				index = c.Index,
				time = c.Time,
				gap = c.Gap
			}).ToArray()
		};

		return await _eventService.EmitEventAsync("racing/race_finish", payload);
	}

	/// <summary>
	/// Get racing leaderboard from backend
	/// </summary>
	public async Task<Result<RacingLeaderboardData>> GetLeaderboardAsync(
		string trackId,
		string metric = "best_race",
		int? laps = null,
		int limit = 10)
	{
		// TODO: Implement HTTP GET to /game/racing/leaderboard
		// For now, return placeholder
		await Task.Delay(1);
		return Result<RacingLeaderboardData>.Failure("Not implemented");
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
