using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// Carrom game-specific event service
/// Provides type-safe methods for emitting carrom events
/// Inherits validation and error handling from GameEventServiceBase
/// </summary>
public class CarromEventService : GameEventServiceBase
{
	public CarromEventService(EventService eventService = null) : base(eventService)
	{
	}

	/// <summary>
	/// Emit round start event with validation
	/// </summary>
	public async Task<Result<bool>> EmitRoundStartAsync(string mode, string[] playerIds)
	{
		// Input validation
		if (string.IsNullOrEmpty(mode))
			return Result<bool>.Failure("Game mode is required");

		if (playerIds == null || playerIds.Length == 0)
			return Result<bool>.Failure("At least one player is required");

		var payload = new
		{
			mode = mode,
			players = playerIds
		};

		return await EmitEventSafeAsync("carrom/round_start", payload);
	}

	/// <summary>
	/// Emit piece pocketed event with validation
	/// </summary>
	public async Task<Result<bool>> EmitPiecePocketedAsync(string pieceType, int playerIndex, int points)
	{
		// Input validation
		if (string.IsNullOrEmpty(pieceType))
			return Result<bool>.Failure("Piece type is required");

		if (playerIndex < 0)
			return Result<bool>.Failure("Invalid player index");

		var payload = new
		{
			piece_type = pieceType,
			player_idx = playerIndex,
			points = points
		};

		return await EmitEventSafeAsync("carrom/piece_pocketed", payload);
	}

	/// <summary>
	/// Emit round finish event with validation
	/// </summary>
	public async Task<Result<bool>> EmitRoundFinishAsync(
		string mode,
		string winnerId,
		Dictionary<string, int> scores)
	{
		// Input validation
		if (string.IsNullOrEmpty(mode))
			return Result<bool>.Failure("Game mode is required");

		if (scores == null || scores.Count == 0)
			return Result<bool>.Failure("Score data is required");

		var payload = new
		{
			mode = mode,
			winner = winnerId,
			scores = scores
		};

		return await EmitEventSafeAsync("carrom/round_finish", payload);
	}

	/// <summary>
	/// Get carrom leaderboard from backend using shared HTTP helper
	/// </summary>
	public async Task<Result<CarromLeaderboardData>> GetLeaderboardAsync(
		string metric = "total_score",
		int limit = 10)
	{
		// Input validation
		if (limit < 1 || limit > 100)
			return Result<CarromLeaderboardData>.Failure("Limit must be between 1 and 100");

		try
		{
			// Build query parameters
			var queryParams = new Dictionary<string, string>
			{
				{ "metric", metric },
				{ "limit", limit.ToString() }
			};

			// Use shared HTTP helper - handles connection, polling, timeouts
			var responseResult = await QueryBackendRawAsync("/game/carrom/leaderboard", queryParams);
			if (!responseResult.IsSuccess)
				return Result<CarromLeaderboardData>.Failure(responseResult.Error);

			// Parse JSON response using base class helper
			var parseResult = ParseJsonResponse(responseResult.Value);
			if (!parseResult.IsSuccess)
				return Result<CarromLeaderboardData>.Failure(parseResult.Error);

			var jsonDict = parseResult.Value;

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
		catch (FormatException ex)
		{
			LogError($"Invalid data format in leaderboard response: {ex.Message}");
			return Result<CarromLeaderboardData>.Failure("Invalid server response format");
		}
		catch (System.Exception ex)
		{
			LogError($"Unexpected error retrieving leaderboard: {ex.Message}");
			return Result<CarromLeaderboardData>.Failure("Unable to retrieve leaderboard data");
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
