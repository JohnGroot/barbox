using Godot;
using LightResults;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BarBox.Games.MiningGame;

/// <summary>
/// Mining game-specific event service
/// Provides type-safe methods for emitting mining events
/// Inherits validation and error handling from GameEventServiceBase
/// </summary>
public class MiningEventService : GameEventServiceBase
{
	public MiningEventService(EventService eventService = null) : base(eventService)
	{
	}

	/// <summary>
	/// Emit extraction complete event with validation
	/// </summary>
	public async Task<Result<bool>> EmitExtractCompleteAsync(GemType gemType, int quantity)
	{
		// Input validation
		if (quantity < 1)
			return Result.Failure<bool>(ValidationMessages.MinValue("Quantity", 1));

		// Check EventService availability (required for location_id)
		if (_eventService == null)
			return Result.Failure<bool>("Event service not available");

		// Get location ID with fallback
		var locationId = _eventService.GetCurrentLocationId();
		if (string.IsNullOrEmpty(locationId))
		{
			LogWarning("Location ID not available from EventService, using 'default'");
			locationId = "default";
		}

		var payload = new
		{
			gem_type = gemType.ToString().ToLowerInvariant(),
			quantity = quantity,
			location_id = locationId
		};

		return await EmitEventSafeAsync("mining/extract_complete", payload);
	}

	/// <summary>
	/// Emit upgrade purchase event with validation
	/// </summary>
	public async Task<Result<bool>> EmitUpgradePurchaseAsync(
		UpgradeType upgradeType,
		int level,
		Dictionary<GemType, int> cost,
		string locationId)
	{
		// Input validation
		if (level < 1)
			return Result.Failure<bool>(ValidationMessages.MinValue("Level", 1));

		if (cost == null || cost.Count == 0)
			return Result.Failure<bool>(ValidationMessages.Required("Upgrade cost"));

		if (string.IsNullOrEmpty(locationId))
			return Result.Failure<bool>(ValidationMessages.Required("Location ID"));

		// Convert cost dictionary to string keys
		var costDict = new Dictionary<string, int>();
		foreach (var kvp in cost)
		{
			costDict[kvp.Key.ToString().ToLowerInvariant()] = kvp.Value;
		}

		var payload = new
		{
			upgrade_type = upgradeType.ToString().ToLowerInvariant(),
			level = level,
			cost = costDict,
			location_id = locationId
		};

		return await EmitEventSafeAsync("mining/upgrade_purchase", payload);
	}

	/// <summary>
	/// Emit credit deposit event when gems are spent for credits with validation
	/// </summary>
	public async Task<Result<bool>> EmitCreditDepositAsync(
		GemType gemType,
		int gemsSpent,
		int creditsEarned)
	{
		// Input validation
		if (gemsSpent < 1)
			return Result.Failure<bool>(ValidationMessages.MinValue("Gems spent", 1));

		if (creditsEarned < 1)
			return Result.Failure<bool>(ValidationMessages.MinValue("Credits earned", 1));

		var payload = new
		{
			gem_type = gemType.ToString().ToLowerInvariant(),
			gems_spent = gemsSpent,
			credits_earned = creditsEarned
		};

		return await EmitEventSafeAsync("mining/credit_deposit", payload);
	}

	/// <summary>
	/// Emit mining tick event to update backend timestamp during active gameplay
	/// </summary>
	public async Task<Result<bool>> EmitMiningTickAsync(string locationId, int pendingGems)
{
	// Input validation
	if (string.IsNullOrEmpty(locationId))
		return Result.Failure<bool>(ValidationMessages.Required("Location ID"));

	if (pendingGems < 0)
		return Result.Failure<bool>(ValidationMessages.MinValue("Pending gems", 0));

	var payload = new
	{
		location_id = locationId,
		pending_gems = pendingGems,
		timestamp = System.DateTime.UtcNow
	};

	return await EmitEventSafeAsync("mining/tick_update", payload);
}

/// <summary>
/// Get player's global mining inventory from backend
/// Aggregates all extracted gems minus spent gems from upgrades/credits
/// </summary>
/// <param name="playerId">Player UUID to query inventory for</param>
/// <returns>Result containing inventory data or error message</returns>
/// <remarks>
/// Inventory is calculated as: extracted gems - upgrade costs - credit deposits
/// Returns only gem types with positive quantities
/// </remarks>
public async Task<Result<MiningInventoryData>> GetPlayerInventoryAsync(Guid playerId)
	{
		// Input validation
		var validation = ValidateGuid(playerId, "Player ID");
		if (validation.IsFailure(out var validationError))
			return Result.Failure<MiningInventoryData>(validationError.Message);

		return await QueryBackendAsync(
			$"/game/mining/player/{playerId}/inventory",
			null,
			ParseInventoryData
		);
	}

	private Result<MiningInventoryData> ParseInventoryData(Godot.Collections.Dictionary jsonDict)
	{
		var playerIdResult = JsonParsingHelpers.ParseGuid(jsonDict, JsonFieldNames.PlayerId);
		var lastUpdatedResult = JsonParsingHelpers.ParseDateTime(jsonDict, JsonFieldNames.LastUpdated);
		var gemsResult = JsonParsingHelpers.ParseIntDictionary(jsonDict, JsonFieldNames.Gems);

		if (playerIdResult.IsFailure(out var playerIdError)) return Result.Failure<MiningInventoryData>(playerIdError.Message);
		if (!playerIdResult.IsSuccess(out var playerId)) return Result.Failure<MiningInventoryData>("Failed to extract player ID");
		if (lastUpdatedResult.IsFailure(out var lastUpdatedError)) return Result.Failure<MiningInventoryData>(lastUpdatedError.Message);
		if (!lastUpdatedResult.IsSuccess(out var lastUpdated)) return Result.Failure<MiningInventoryData>("Failed to extract last updated timestamp");
		if (gemsResult.IsFailure(out var gemsError)) return Result.Failure<MiningInventoryData>(gemsError.Message);
		if (!gemsResult.IsSuccess(out var gems)) return Result.Failure<MiningInventoryData>("Failed to extract gems data");

		return Result.Success(new MiningInventoryData
		{
			PlayerId = playerId,
			LastUpdated = lastUpdated,
			Gems = gems
		});
	}

	/// <summary>
	/// Get player's upgrade levels from backend
	/// Returns the current level for each purchased upgrade type
	/// </summary>
	/// <param name="playerId">Player UUID to query upgrades for</param>
	/// <returns>Result containing upgrade data or error message</returns>
	/// <remarks>
	/// Upgrade levels are tracked globally across all locations
	/// Only returns upgrade types that have been purchased (level > 0)
	/// </remarks>
	public async Task<Result<MiningUpgradesData>> GetPlayerUpgradesAsync(Guid playerId)
	{
		// Input validation
		var validation = ValidateGuid(playerId, "Player ID");
		if (validation.IsFailure(out var validationError))
			return Result.Failure<MiningUpgradesData>(validationError.Message);

		return await QueryBackendAsync(
			$"/game/mining/player/{playerId}/upgrades",
			null,
			ParseUpgradesData
		);
	}

	private Result<MiningUpgradesData> ParseUpgradesData(Godot.Collections.Dictionary jsonDict)
	{
		var playerIdResult = JsonParsingHelpers.ParseGuid(jsonDict, JsonFieldNames.PlayerId);
		var lastUpdatedResult = JsonParsingHelpers.ParseDateTime(jsonDict, JsonFieldNames.LastUpdated);
		var upgradesResult = JsonParsingHelpers.ParseIntDictionary(jsonDict, JsonFieldNames.Upgrades);

		if (playerIdResult.IsFailure(out var playerIdError)) return Result.Failure<MiningUpgradesData>(playerIdError.Message);
		if (!playerIdResult.IsSuccess(out var playerId)) return Result.Failure<MiningUpgradesData>("Failed to extract player ID");
		if (lastUpdatedResult.IsFailure(out var lastUpdatedError)) return Result.Failure<MiningUpgradesData>(lastUpdatedError.Message);
		if (!lastUpdatedResult.IsSuccess(out var lastUpdated)) return Result.Failure<MiningUpgradesData>("Failed to extract last updated timestamp");
		if (upgradesResult.IsFailure(out var upgradesError)) return Result.Failure<MiningUpgradesData>(upgradesError.Message);
		if (!upgradesResult.IsSuccess(out var upgrades)) return Result.Failure<MiningUpgradesData>("Failed to extract upgrades data");

		return Result.Success(new MiningUpgradesData
		{
			PlayerId = playerId,
			LastUpdated = lastUpdated,
			Upgrades = upgrades
		});
	}

	/// <summary>
	/// Get player's last mining timestamp for a specific location
	/// Used to calculate offline gem accumulation when player returns
	/// </summary>
	/// <param name="playerId">Player UUID to query timestamp for</param>
	/// <param name="locationId">Location identifier (e.g., "mining_cave_1")</param>
	/// <returns>Result containing timestamp data or error message</returns>
	/// <remarks>
	/// Timestamps are location-specific to support different mining areas
	/// Updated by mining/tick_update events during active gameplay
	/// </remarks>
	public async Task<Result<MiningTimestampData>> GetPlayerMiningTimestampAsync(Guid playerId, string locationId)
	{
		// Input validation
		var validation = ValidateGuid(playerId, "Player ID");
		if (validation.IsFailure(out var validationError))
			return Result.Failure<MiningTimestampData>(validationError.Message);

		if (string.IsNullOrEmpty(locationId))
			return Result.Failure<MiningTimestampData>(ValidationMessages.Required("Location ID"));

		var queryParams = new Dictionary<string, string> { ["location_id"] = locationId };
		return await QueryBackendAsync(
			$"/game/mining/player/{playerId}/mining_timestamp",
			queryParams,
			ParseTimestampData
		);
	}

	private Result<MiningTimestampData> ParseTimestampData(Godot.Collections.Dictionary jsonDict)
	{
		var playerIdResult = JsonParsingHelpers.ParseGuid(jsonDict, JsonFieldNames.PlayerId);
		var lastMiningTimeResult = JsonParsingHelpers.ParseDateTime(jsonDict, JsonFieldNames.LastMiningTime);

		if (playerIdResult.IsFailure(out var playerIdError)) return Result.Failure<MiningTimestampData>(playerIdError.Message);
		if (!playerIdResult.IsSuccess(out var playerId)) return Result.Failure<MiningTimestampData>("Failed to extract player ID");
		if (lastMiningTimeResult.IsFailure(out var lastMiningTimeError)) return Result.Failure<MiningTimestampData>(lastMiningTimeError.Message);
		if (!lastMiningTimeResult.IsSuccess(out var lastMiningTime)) return Result.Failure<MiningTimestampData>("Failed to extract last mining time");

		if (!jsonDict.ContainsKey(JsonFieldNames.LocationId))
			return Result.Failure<MiningTimestampData>($"Missing required field: {JsonFieldNames.LocationId}");

		return Result.Success(new MiningTimestampData
		{
			PlayerId = playerId,
			LocationId = jsonDict[JsonFieldNames.LocationId].AsString(),
			LastMiningTime = lastMiningTime
		});
	}

	/// <summary>
	/// Get player's mining metadata from backend
	/// Includes bonus status and lifetime event statistics
	/// </summary>
	/// <param name="playerId">Player UUID to query metadata for</param>
	/// <returns>Result containing metadata or error message</returns>
	/// <remarks>
	/// Metadata tracks player engagement and bonus eligibility
	/// HasReceivedBonus is used for one-time first-play rewards
	/// Event timestamps help track player activity patterns
	/// </remarks>
	public async Task<Result<MiningMetadataData>> GetPlayerMetadataAsync(Guid playerId)
	{
		// Input validation
		var validation = ValidateGuid(playerId, "Player ID");
		if (validation.IsFailure(out var validationError))
			return Result.Failure<MiningMetadataData>(validationError.Message);

		return await QueryBackendAsync(
			$"/game/mining/player/{playerId}/metadata",
			null,
			ParseMetadataData
		);
	}

	private Result<MiningMetadataData> ParseMetadataData(Godot.Collections.Dictionary jsonDict)
	{
		var playerIdResult = JsonParsingHelpers.ParseGuid(jsonDict, JsonFieldNames.PlayerId);
		var firstEventTimeResult = JsonParsingHelpers.ParseNullableDateTime(jsonDict, JsonFieldNames.FirstEventTime);
		var lastEventTimeResult = JsonParsingHelpers.ParseNullableDateTime(jsonDict, JsonFieldNames.LastEventTime);

		if (playerIdResult.IsFailure(out var playerIdError)) return Result.Failure<MiningMetadataData>(playerIdError.Message);
		if (!playerIdResult.IsSuccess(out var playerId)) return Result.Failure<MiningMetadataData>("Failed to extract player ID");
		if (firstEventTimeResult.IsFailure(out var firstEventTimeError)) return Result.Failure<MiningMetadataData>(firstEventTimeError.Message);
		if (!firstEventTimeResult.IsSuccess(out var firstEventTime)) return Result.Failure<MiningMetadataData>("Failed to extract first event time");
		if (lastEventTimeResult.IsFailure(out var lastEventTimeError)) return Result.Failure<MiningMetadataData>(lastEventTimeError.Message);
		if (!lastEventTimeResult.IsSuccess(out var lastEventTime)) return Result.Failure<MiningMetadataData>("Failed to extract last event time");

		if (!jsonDict.ContainsKey(JsonFieldNames.HasReceivedBonus))
			return Result.Failure<MiningMetadataData>($"Missing required field: {JsonFieldNames.HasReceivedBonus}");

		if (!jsonDict.ContainsKey(JsonFieldNames.TotalEvents))
			return Result.Failure<MiningMetadataData>($"Missing required field: {JsonFieldNames.TotalEvents}");

		return Result.Success(new MiningMetadataData
		{
			PlayerId = playerId,
			HasReceivedBonus = jsonDict[JsonFieldNames.HasReceivedBonus].AsBool(),
			TotalEvents = jsonDict[JsonFieldNames.TotalEvents].AsInt32(),
			FirstEventTime = firstEventTime,
			LastEventTime = lastEventTime
		});
	}

	/// <summary>
	/// Emit first-time bonus event
	/// </summary>
	public async Task<Result<bool>> EmitFirstTimeBonusAsync(Guid playerId, string locationId, int bonusAmount)
	{
		// Input validation
		var validation = ValidateGuid(playerId, "Player ID");
		if (validation.IsFailure(out var validationError))
			return Result.Failure<bool>(validationError.Message);

		if (bonusAmount < 1)
			return Result.Failure<bool>(ValidationMessages.MinValue("Bonus amount", 1));

		var payload = new
		{
			player_id = playerId.ToString(),
			location_id = locationId,
			bonus_amount = bonusAmount
		};

		return await EmitEventSafeAsync("mining/first_time_bonus", payload);
	}
}

/// <summary>
/// Mining inventory data structure
/// </summary>
public record class MiningInventoryData
{
	public Guid PlayerId { get; init; }
	public Dictionary<string, int> Gems { get; init; }
	public DateTime LastUpdated { get; init; }
}

/// <summary>
/// Mining upgrades data structure
/// </summary>
public record class MiningUpgradesData
{
	public Guid PlayerId { get; init; }
	public Dictionary<string, int> Upgrades { get; init; }
	public DateTime LastUpdated { get; init; }
}

/// <summary>
/// Mining timestamp data structure
/// </summary>
public record class MiningTimestampData
{
	public Guid PlayerId { get; init; }
	public string LocationId { get; init; }
	public DateTime LastMiningTime { get; init; }
}

/// <summary>
/// Mining metadata data structure
/// </summary>
public record class MiningMetadataData
{
	public Guid PlayerId { get; init; }
	public bool HasReceivedBonus { get; init; }
	public int TotalEvents { get; init; }
	public DateTime? FirstEventTime { get; init; }
	public DateTime? LastEventTime { get; init; }
}
