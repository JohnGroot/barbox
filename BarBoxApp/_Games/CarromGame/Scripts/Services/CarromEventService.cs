using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// Carrom game-specific event service
/// Provides type-safe methods for emitting carrom events
/// </summary>
public partial class CarromEventService : Node
{
	private EventService _eventService;

	public override void _Ready()
	{
		_eventService = EventService.GetInstance();
		if (_eventService == null)
		{
			GD.PrintErr("CarromEventService: EventService not found");
		}
	}

	/// <summary>
	/// Emit round start event
	/// </summary>
	public async Task<Result<bool>> EmitRoundStartAsync(string mode, string[] playerIds)
	{
		if (_eventService == null)
			return Result<bool>.Failure("EventService not initialized");

		var payload = new
		{
			mode = mode,
			players = playerIds
		};

		return await _eventService.EmitEventAsync("carrom/round_start", payload);
	}

	/// <summary>
	/// Emit piece pocketed event
	/// </summary>
	public async Task<Result<bool>> EmitPiecePocketedAsync(string pieceType, int playerIndex, int points)
	{
		if (_eventService == null)
			return Result<bool>.Failure("EventService not initialized");

		var payload = new
		{
			piece_type = pieceType,
			player_idx = playerIndex,
			points = points
		};

		return await _eventService.EmitEventAsync("carrom/piece_pocketed", payload);
	}

	/// <summary>
	/// Emit round finish event
	/// </summary>
	public async Task<Result<bool>> EmitRoundFinishAsync(
		string mode,
		string winnerId,
		Dictionary<string, int> scores)
	{
		if (_eventService == null)
			return Result<bool>.Failure("EventService not initialized");

		var payload = new
		{
			mode = mode,
			winner = winnerId,
			scores = scores
		};

		return await _eventService.EmitEventAsync("carrom/round_finish", payload);
	}

	/// <summary>
	/// Get carrom leaderboard from backend
	/// </summary>
	public async Task<Result<CarromLeaderboardData>> GetLeaderboardAsync(
		string metric = "total_score",
		int limit = 10)
	{
		if (_eventService == null)
			return Result<CarromLeaderboardData>.Failure("EventService not initialized");

		try
		{
			var httpClient = new HttpClient();

			// Connect to backend
			var error = httpClient.ConnectToHost("localhost", 8000);
			if (error != Error.Ok)
				return Result<CarromLeaderboardData>.Failure($"Connection failed: {error}");

			// Wait for connection
			var attempts = 0;
			while (httpClient.GetStatus() == HttpClient.Status.Connecting && attempts < 50)
			{
				await AutoloadBase.StaticDelayAsync(0.1f);
				attempts++;
			}

			if (httpClient.GetStatus() != HttpClient.Status.Connected)
				return Result<CarromLeaderboardData>.Failure("Connection timeout");

			// Build query string
			var query = $"metric={metric}&limit={limit}";

			// Make HTTP GET request
			var headers = new[]
			{
				"User-Agent: BarBox-Client/1.0",
				"Accept: application/json"
			};

			error = httpClient.Request(
				HttpClient.Method.Get,
				$"/game/carrom/leaderboard?{query}",
				headers
			);

			if (error != Error.Ok)
			{
				httpClient.Close();
				return Result<CarromLeaderboardData>.Failure($"Request failed: {error}");
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
				return Result<CarromLeaderboardData>.Failure("Request timeout");
			}

			var responseCode = httpClient.GetResponseCode();
			if (responseCode != 200)
			{
				httpClient.Close();
				return Result<CarromLeaderboardData>.Failure($"Server returned {responseCode}");
			}

			var responseBody = httpClient.GetResponseBodyAsString();
			httpClient.Close();

			// Parse JSON response
			var json = Json.ParseString(responseBody);
			if (json == null)
				return Result<CarromLeaderboardData>.Failure("Failed to parse response");

			var jsonDict = json.AsGodotDictionary();

			var leaderboardData = new CarromLeaderboardData
			{
				Metric = jsonDict["metric"].AsString(),
				Leaderboard = new List<CarromLeaderboardEntry>()
			};

			var leaderboardArray = jsonDict["leaderboard"].AsGodotArray();
			foreach (var entry in leaderboardArray)
			{
				var entryDict = entry.AsGodotDictionary();

				var leaderboardEntry = new CarromLeaderboardEntry
				{
					PlayerId = Guid.Parse(entryDict["player_id"].AsString()),
					Username = entryDict["username"].AsString(),
					TotalScore = entryDict["total_score"].AsInt32()
				};

				// Parse total_wins if present
				if (entryDict.ContainsKey("total_wins") && entryDict["total_wins"].VariantType != Variant.Type.Nil)
				{
					leaderboardEntry.TotalWins = entryDict["total_wins"].AsInt32();
				}

				leaderboardData.Leaderboard.Add(leaderboardEntry);
			}

			return Result<CarromLeaderboardData>.Success(leaderboardData);
		}
		catch (Exception ex)
		{
			return Result<CarromLeaderboardData>.Failure($"Exception: {ex.Message}");
		}
	}
}

/// <summary>
/// Carrom leaderboard data structure
/// </summary>
public class CarromLeaderboardData
{
	public string Metric { get; set; }
	public List<CarromLeaderboardEntry> Leaderboard { get; set; }
}

/// <summary>
/// Carrom leaderboard entry
/// </summary>
public class CarromLeaderboardEntry
{
	public Guid PlayerId { get; set; }
	public string Username { get; set; }
	public int TotalScore { get; set; }
	public int? TotalWins { get; set; }
}
