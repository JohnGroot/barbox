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
			return Result<bool>.Failure(ValidationMessages.Required("Game mode"));

		if (playerIds == null || playerIds.Length == 0)
			return Result<bool>.Failure(ValidationMessages.Required("Player IDs"));

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
			return Result<bool>.Failure(ValidationMessages.Required("Piece type"));

		if (playerIndex < 0)
			return Result<bool>.Failure(ValidationMessages.MinValue("Player index", 0));

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
			return Result<bool>.Failure(ValidationMessages.Required("Game mode"));

		if (scores == null || scores.Count == 0)
			return Result<bool>.Failure(ValidationMessages.Required("Score data"));

		var payload = new
		{
			mode = mode,
			winner = winnerId,
			scores = scores
		};

		return await EmitEventSafeAsync("carrom/round_finish", payload);
	}

	/// <summary>
	/// Create a new backend session for the carrom game
	/// </summary>
	public async Task<Result<Guid>> CreateSessionAsync(Guid boxId, Guid playerId)
	{
		// Input validation
		var boxValidation = ValidateGuid(boxId, "Box ID");
		if (!boxValidation.IsSuccess)
			return Result<Guid>.Failure(boxValidation.Error);

		var playerValidation = ValidateGuid(playerId, "Player ID");
		if (!playerValidation.IsSuccess)
			return Result<Guid>.Failure(playerValidation.Error);

		if (_eventService == null || !GodotObject.IsInstanceValid(_eventService))
			return Result<Guid>.Failure("Event service not available");

		// Delegate to EventService
		var result = await _eventService.CreateSessionAsync(boxId, playerId);

		if (result.IsSuccess)
			LogInfo($"Carrom session created: {result.Value}");
		else
			LogError($"Failed to create carrom session: {result.Error}");

		return result;
	}

	/// <summary>
	/// Create multiplayer game session when "Start Game" is clicked.
	/// Called AFTER all players have logged in during preparation screen.
	/// </summary>
	/// <param name="boxId">Box identifier</param>
	/// <param name="playerIds">Array of 2-4 player IDs</param>
	/// <returns>Result containing session ID if successful</returns>
	public async Task<Result<Guid>> CreateMultiplayerSessionAsync(Guid boxId, Guid[] playerIds)
	{
		// Input validation
		var boxValidation = ValidateGuid(boxId, "Box ID");
		if (!boxValidation.IsSuccess)
			return Result<Guid>.Failure(boxValidation.Error);

		if (playerIds == null || playerIds.Length < 2 || playerIds.Length > 4)
			return Result<Guid>.Failure(ValidationMessages.RangeError("Player count", 2, 4));

		if (_eventService == null || !GodotObject.IsInstanceValid(_eventService))
			return Result<Guid>.Failure("Event service not available");

		// Create session with all players via EventService
		var sessionId = Guid.NewGuid();
		var result = await _eventService.CreateSessionWithPlayersAsync(boxId, sessionId, playerIds);

		if (result.IsSuccess)
			LogInfo($"Multiplayer session created: {sessionId} with {playerIds.Length} players");
		else
			LogError($"Failed to create multiplayer session: {result.Error}");

		return result.IsSuccess
			? Result<Guid>.Success(sessionId)
			: Result<Guid>.Failure(result.Error);
	}

	/// <summary>
	/// Get carrom leaderboard from backend using shared HTTP helper
	/// </summary>
	public async Task<Result<CarromLeaderboardData>> GetLeaderboardAsync(
		string metric = "total_score",
		int limit = 10)
	{
		// Input validation
		var limitValidation = ValidateLeaderboardLimit(limit);
		if (!limitValidation.IsSuccess)
			return Result<CarromLeaderboardData>.Failure(limitValidation.Error);

		// Build query parameters
		var queryParams = new Dictionary<string, string>
		{
			{ JsonFieldNames.Metric, metric },
			{ "limit", limit.ToString() }
		};

		return await QueryBackendAsync(
			"/game/carrom/leaderboard",
			queryParams,
			jsonDict => ParseLeaderboardData(jsonDict)
		);
	}

	private Result<CarromLeaderboardData> ParseLeaderboardData(Godot.Collections.Dictionary jsonDict)
	{
		if (!jsonDict.ContainsKey(JsonFieldNames.Metric))
			return Result<CarromLeaderboardData>.Failure($"Missing required field: {JsonFieldNames.Metric}");

		if (!jsonDict.ContainsKey(JsonFieldNames.Leaderboard))
			return Result<CarromLeaderboardData>.Failure($"Missing required field: {JsonFieldNames.Leaderboard}");

		var leaderboardData = new CarromLeaderboardData
		{
			Metric = jsonDict[JsonFieldNames.Metric].AsString(),
			Leaderboard = new List<CarromLeaderboardEntry>()
		};

		var leaderboardArray = jsonDict[JsonFieldNames.Leaderboard].AsGodotArray();
		foreach (var entry in leaderboardArray)
		{
			var entryDict = entry.AsGodotDictionary();

			var playerIdResult = JsonParsingHelpers.ParseGuid(entryDict, JsonFieldNames.PlayerId);
			var entryDateResult = JsonParsingHelpers.ParseDateTime(entryDict, JsonFieldNames.EntryDate);

			if (!playerIdResult.IsSuccess) return Result<CarromLeaderboardData>.Failure(playerIdResult.Error);
			if (!entryDateResult.IsSuccess) return Result<CarromLeaderboardData>.Failure(entryDateResult.Error);

			if (!entryDict.ContainsKey(JsonFieldNames.Username))
				return Result<CarromLeaderboardData>.Failure($"Missing required field: {JsonFieldNames.Username}");

			if (!entryDict.ContainsKey(JsonFieldNames.TotalScore))
				return Result<CarromLeaderboardData>.Failure($"Missing required field: {JsonFieldNames.TotalScore}");

			var leaderboardEntry = new CarromLeaderboardEntry
			{
				PlayerId = playerIdResult.Value,
				Username = entryDict[JsonFieldNames.Username].AsString(),
				TotalScore = entryDict[JsonFieldNames.TotalScore].AsInt32(),
				EntryDate = entryDateResult.Value
			};

			// Parse total_wins if present (nullable field)
			if (!JsonParsingHelpers.IsNullOrMissing(entryDict, JsonFieldNames.TotalWins))
			{
				leaderboardEntry.TotalWins = entryDict[JsonFieldNames.TotalWins].AsInt32();
			}

			leaderboardData.Leaderboard.Add(leaderboardEntry);
		}

		return Result<CarromLeaderboardData>.Success(leaderboardData);
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
	public DateTime EntryDate { get; set; }
}
