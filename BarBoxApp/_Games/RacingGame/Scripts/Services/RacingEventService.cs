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
		if (_eventService == null)
			return Result<RacingLeaderboardData>.Failure("EventService not initialized");

		try
		{
			var httpClient = new HttpClient();

			// Connect to backend
			var error = httpClient.ConnectToHost("localhost", 8000);
			if (error != Error.Ok)
				return Result<RacingLeaderboardData>.Failure($"Connection failed: {error}");

			// Wait for connection
			var attempts = 0;
			while (httpClient.GetStatus() == HttpClient.Status.Connecting && attempts < 50)
			{
				await AutoloadBase.StaticDelayAsync(0.1f);
				attempts++;
			}

			if (httpClient.GetStatus() != HttpClient.Status.Connected)
				return Result<RacingLeaderboardData>.Failure("Connection timeout");

			// Build query string
			var query = $"track_id={trackId}&metric={metric}&limit={limit}";
			if (laps.HasValue)
				query += $"&laps={laps.Value}";

			// Make HTTP GET request
			var headers = new[]
			{
				"User-Agent: BarBox-Client/1.0",
				"Accept: application/json"
			};

			error = httpClient.Request(
				HttpClient.Method.Get,
				$"/game/racing/leaderboard?{query}",
				headers
			);

			if (error != Error.Ok)
			{
				httpClient.Close();
				return Result<RacingLeaderboardData>.Failure($"Request failed: {error}");
			}

			// Wait for response
			attempts = 0;
			while (httpClient.GetStatus() == HttpClient.Status.Requesting && attempts < 500)
			{
				await AutoloadBase.StaticDelayAsync(0.01f);
				attempts++;
			}

			if (httpClient.GetStatus() == HttpClient.Status.Requesting)
			{
				httpClient.Close();
				return Result<RacingLeaderboardData>.Failure("Request timeout");
			}

			var responseCode = httpClient.GetResponseCode();
			if (responseCode != 200)
			{
				httpClient.Close();
				return Result<RacingLeaderboardData>.Failure($"Server returned {responseCode}");
			}

			var responseBody = httpClient.GetResponseBodyAsString();
			httpClient.Close();

			// Parse JSON response
			var json = Json.ParseString(responseBody);
			if (json == null)
				return Result<RacingLeaderboardData>.Failure("Failed to parse response");

			var jsonDict = json.AsGodotDictionary();

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
					var lapTimesArray = Json.ParseString(lapTimesJson)?.AsGodotArray();
					if (lapTimesArray != null)
					{
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
		catch (Exception ex)
		{
			return Result<RacingLeaderboardData>.Failure($"Exception: {ex.Message}");
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
