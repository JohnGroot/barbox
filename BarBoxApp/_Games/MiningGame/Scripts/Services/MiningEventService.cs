using Godot;
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
			return Result<bool>.Failure(ValidationMessages.MinValue("Quantity", 1));

		// Check EventService availability (required for location_id)
		if (_eventService == null)
			return Result<bool>.Failure("Event service not available");

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
			return Result<bool>.Failure(ValidationMessages.MinValue("Level", 1));

		if (cost == null || cost.Count == 0)
			return Result<bool>.Failure(ValidationMessages.Required("Upgrade cost"));

		if (string.IsNullOrEmpty(locationId))
			return Result<bool>.Failure(ValidationMessages.Required("Location ID"));

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
			return Result<bool>.Failure(ValidationMessages.MinValue("Gems spent", 1));

		if (creditsEarned < 1)
			return Result<bool>.Failure(ValidationMessages.MinValue("Credits earned", 1));

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
		return Result<bool>.Failure(ValidationMessages.Required("Location ID"));

	if (pendingGems < 0)
		return Result<bool>.Failure(ValidationMessages.MinValue("Pending gems", 0));

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
		if (!validation.IsSuccess)
			return Result<MiningInventoryData>.Failure(validation.Error);

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

		if (!playerIdResult.IsSuccess) return Result<MiningInventoryData>.Failure(playerIdResult.Error);
		if (!lastUpdatedResult.IsSuccess) return Result<MiningInventoryData>.Failure(lastUpdatedResult.Error);
		if (!gemsResult.IsSuccess) return Result<MiningInventoryData>.Failure(gemsResult.Error);

		return Result<MiningInventoryData>.Success(new MiningInventoryData
		{
			PlayerId = playerIdResult.Value,
			LastUpdated = lastUpdatedResult.Value,
			Gems = gemsResult.Value
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
		if (!validation.IsSuccess)
			return Result<MiningUpgradesData>.Failure(validation.Error);

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

		if (!playerIdResult.IsSuccess) return Result<MiningUpgradesData>.Failure(playerIdResult.Error);
		if (!lastUpdatedResult.IsSuccess) return Result<MiningUpgradesData>.Failure(lastUpdatedResult.Error);
		if (!upgradesResult.IsSuccess) return Result<MiningUpgradesData>.Failure(upgradesResult.Error);

		return Result<MiningUpgradesData>.Success(new MiningUpgradesData
		{
			PlayerId = playerIdResult.Value,
			LastUpdated = lastUpdatedResult.Value,
			Upgrades = upgradesResult.Value
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
		if (!validation.IsSuccess)
			return Result<MiningTimestampData>.Failure(validation.Error);

		if (string.IsNullOrEmpty(locationId))
			return Result<MiningTimestampData>.Failure(ValidationMessages.Required("Location ID"));

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

		if (!playerIdResult.IsSuccess) return Result<MiningTimestampData>.Failure(playerIdResult.Error);
		if (!lastMiningTimeResult.IsSuccess) return Result<MiningTimestampData>.Failure(lastMiningTimeResult.Error);

		if (!jsonDict.ContainsKey(JsonFieldNames.LocationId))
			return Result<MiningTimestampData>.Failure($"Missing required field: {JsonFieldNames.LocationId}");

		return Result<MiningTimestampData>.Success(new MiningTimestampData
		{
			PlayerId = playerIdResult.Value,
			LocationId = jsonDict[JsonFieldNames.LocationId].AsString(),
			LastMiningTime = lastMiningTimeResult.Value
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
		if (!validation.IsSuccess)
			return Result<MiningMetadataData>.Failure(validation.Error);

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

		if (!playerIdResult.IsSuccess) return Result<MiningMetadataData>.Failure(playerIdResult.Error);
		if (!firstEventTimeResult.IsSuccess) return Result<MiningMetadataData>.Failure(firstEventTimeResult.Error);
		if (!lastEventTimeResult.IsSuccess) return Result<MiningMetadataData>.Failure(lastEventTimeResult.Error);

		if (!jsonDict.ContainsKey(JsonFieldNames.HasReceivedBonus))
			return Result<MiningMetadataData>.Failure($"Missing required field: {JsonFieldNames.HasReceivedBonus}");

		if (!jsonDict.ContainsKey(JsonFieldNames.TotalEvents))
			return Result<MiningMetadataData>.Failure($"Missing required field: {JsonFieldNames.TotalEvents}");

		return Result<MiningMetadataData>.Success(new MiningMetadataData
		{
			PlayerId = playerIdResult.Value,
			HasReceivedBonus = jsonDict[JsonFieldNames.HasReceivedBonus].AsBool(),
			TotalEvents = jsonDict[JsonFieldNames.TotalEvents].AsInt32(),
			FirstEventTime = firstEventTimeResult.Value,
			LastEventTime = lastEventTimeResult.Value
		});
	}

	/// <summary>
	/// Emit first-time bonus event
	/// </summary>
	public async Task<Result<bool>> EmitFirstTimeBonusAsync(Guid playerId, string locationId, int bonusAmount)
	{
		// Input validation
		var validation = ValidateGuid(playerId, "Player ID");
		if (!validation.IsSuccess)
			return Result<bool>.Failure(validation.Error);

		if (bonusAmount < 1)
			return Result<bool>.Failure(ValidationMessages.MinValue("Bonus amount", 1));

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
public class MiningInventoryData
{
	public Guid PlayerId { get; set; }
	public Dictionary<string, int> Gems { get; set; }
	public DateTime LastUpdated { get; set; }
}

/// <summary>
/// Mining upgrades data structure
/// </summary>
public class MiningUpgradesData
{
	public Guid PlayerId { get; set; }
	public Dictionary<string, int> Upgrades { get; set; }
	public DateTime LastUpdated { get; set; }
}

/// <summary>
/// Mining timestamp data structure
/// </summary>
public class MiningTimestampData
{
	public Guid PlayerId { get; set; }
	public string LocationId { get; set; }
	public DateTime LastMiningTime { get; set; }
}

/// <summary>
/// Mining metadata data structure
/// </summary>
public class MiningMetadataData
{
	public Guid PlayerId { get; set; }
	public bool HasReceivedBonus { get; set; }
	public int TotalEvents { get; set; }
	public DateTime? FirstEventTime { get; set; }
	public DateTime? LastEventTime { get; set; }
}
