using Godot;
using LightResults;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BarBox.Games.MiningGame;

namespace BarBox.Games.MiningGame;

/// <summary>
/// Mining game-specific event service
/// Provides type-safe methods for emitting mining events
/// Inherits validation and error handling from GameEventServiceBase
/// </summary>
public class MiningEventService : GameEventServiceBase
{
	private const string EVENT_EXTRACT_COMPLETE = "mining/extract_complete";
	private const string EVENT_UPGRADE_PURCHASE = "mining/upgrade_purchase";
	private const string EVENT_CREDIT_DEPOSIT = "mining/credit_deposit";
	private const string EVENT_FIRST_TIME_BONUS = "mining/first_time_bonus";

	public MiningEventService(EventService eventService = null) : base(eventService)
	{
	}

	public async Task<Result<bool>> EmitExtractCompleteAsync(GemType gemType, int quantity, DateTime lastExtractionTime)
	{
		// Input validation
		if (quantity < 1)
			return Result.Failure<bool>(ValidationMessages.MinValue("Quantity", 1));

		// Check EventService availability (required for location_id)
		if (_eventService == null)
			return Result.Failure<bool>("Event service not available");

		// Get venue name with fallback
		var locationId = _eventService.GetVenueName();
		if (string.IsNullOrEmpty(locationId))
		{
			LogWarning("Venue name not available from EventService, using 'default'");
			locationId = "default";
		}

		// NOTE: Backend API uses "location_id" parameter name for venue name values.
		// This violates the "id"=UUID naming convention but is maintained for backend compatibility.
		// See BOX_IDENTITY_GUIDE.md "Backend Parameter Naming Exception" for details.
		var payload = new
		{
			gem_type = gemType.ToString().ToLowerInvariant(),
			quantity = quantity,
			location_id = locationId,  // Contains venue name (string), not a UUID
			last_extraction_time = lastExtractionTime.ToString("O")  // ISO 8601 format for precise timestamp
		};

		return await EmitEventSafeAsync(EVENT_EXTRACT_COMPLETE, payload);
	}

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

		return await EmitEventSafeAsync(EVENT_UPGRADE_PURCHASE, payload);
	}

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

		return await EmitEventSafeAsync(EVENT_CREDIT_DEPOSIT, payload);
	}

	private Result<MiningMetadataData> ParseMetadataData(Godot.Collections.Dictionary jsonDict)
	{
		var playerIdResult = JsonParsingHelpers.ParseGuid(jsonDict, JsonFieldNames.PlayerId);
		var locationIdResult = JsonParsingHelpers.ParseString(jsonDict, JsonFieldNames.LocationId);
		var firstEventTimeResult = JsonParsingHelpers.ParseNullableDateTime(jsonDict, JsonFieldNames.FirstEventTime);
		var lastEventTimeResult = JsonParsingHelpers.ParseNullableDateTime(jsonDict, JsonFieldNames.LastEventTime);

		if (playerIdResult.IsFailure(out var playerIdError)) return Result.Failure<MiningMetadataData>(playerIdError.Message);
		if (!playerIdResult.IsSuccess(out var playerId)) return Result.Failure<MiningMetadataData>("Failed to extract player ID");
		if (locationIdResult.IsFailure(out var locationIdError)) return Result.Failure<MiningMetadataData>(locationIdError.Message);
		if (!locationIdResult.IsSuccess(out var locationId)) return Result.Failure<MiningMetadataData>("Failed to extract location ID");
		if (firstEventTimeResult.IsFailure(out var firstEventTimeError)) return Result.Failure<MiningMetadataData>(firstEventTimeError.Message);
		if (!firstEventTimeResult.IsSuccess(out var firstEventTime)) return Result.Failure<MiningMetadataData>("Failed to extract first event time");
		if (lastEventTimeResult.IsFailure(out var lastEventTimeError)) return Result.Failure<MiningMetadataData>(lastEventTimeError.Message);
		if (!lastEventTimeResult.IsSuccess(out var lastEventTime)) return Result.Failure<MiningMetadataData>("Failed to extract last event time");

		if (!jsonDict.ContainsKey("has_received_bonus_at_location"))
			return Result.Failure<MiningMetadataData>($"Missing required field: has_received_bonus_at_location");

		if (!jsonDict.ContainsKey(JsonFieldNames.TotalEvents))
			return Result.Failure<MiningMetadataData>($"Missing required field: {JsonFieldNames.TotalEvents}");

		return Result.Success(new MiningMetadataData
		{
			PlayerId = playerId,
			LocationId = locationId,
			HasReceivedBonusAtLocation = jsonDict["has_received_bonus_at_location"].AsBool(),
			TotalEvents = jsonDict[JsonFieldNames.TotalEvents].AsInt32(),
			FirstEventTime = firstEventTime,
			LastEventTime = lastEventTime
		});
	}

	public async Task<Result<bool>> EmitFirstTimeBonusAsync(Guid playerId, string locationId, int bonusAmount)
	{
		// Input validation
		if (ValidateGuid(playerId, "Player ID").IsFailure(out var validationError))
			return Result.Failure<bool>(validationError.Message);

		if (bonusAmount < 1)
			return Result.Failure<bool>(ValidationMessages.MinValue("Bonus amount", 1));

		var payload = new
		{
			player_id = playerId.ToString(),
			location_id = locationId,
			bonus_amount = bonusAmount
		};

		return await EmitEventSafeAsync(EVENT_FIRST_TIME_BONUS, payload);
	}

	/// <summary>
	/// Register this location with the backend and get deterministic configuration.
	/// Idempotent - returns same gem type for same venue name.
	/// </summary>
	/// <param name="venueName">Venue identifier (e.g., "best_intentions")</param>
	/// <returns>Location configuration with assigned gem type</returns>
	public async Task<Result<MiningLocationConfig>> RegisterLocationAsync(string venueName)
	{
		if (string.IsNullOrEmpty(venueName))
			return Result.Failure<MiningLocationConfig>(ValidationMessages.Required("Venue name"));

		var queryParams = new Dictionary<string, string> { ["venue_name"] = venueName };
		return await QueryBackendAsync(
			"/game/mining/location/register",
			queryParams,
			ParseLocationConfig
		);
	}

	private Result<MiningLocationConfig> ParseLocationConfig(Godot.Collections.Dictionary jsonDict)
	{
		var venueNameResult = JsonParsingHelpers.ParseString(jsonDict, "venue_name");
		var gemTypeResult = JsonParsingHelpers.ParseString(jsonDict, "gem_type");
		var displayNameResult = JsonParsingHelpers.ParseString(jsonDict, "display_name");

		if (venueNameResult.IsFailure(out var venueNameError))
			return Result.Failure<MiningLocationConfig>(venueNameError.Message);
		if (!venueNameResult.IsSuccess(out var venueName))
			return Result.Failure<MiningLocationConfig>("Failed to extract venue_name");

		if (gemTypeResult.IsFailure(out var gemTypeError))
			return Result.Failure<MiningLocationConfig>(gemTypeError.Message);
		if (!gemTypeResult.IsSuccess(out var gemType))
			return Result.Failure<MiningLocationConfig>("Failed to extract gem_type");

		if (displayNameResult.IsFailure(out var displayNameError))
			return Result.Failure<MiningLocationConfig>(displayNameError.Message);
		if (!displayNameResult.IsSuccess(out var displayName))
			return Result.Failure<MiningLocationConfig>("Failed to extract display_name");

		return Result.Success(new MiningLocationConfig
		{
			VenueName = venueName,
			GemTypeString = gemType,
			DisplayName = displayName
		});
	}

	/// <summary>
	/// Get complete player mining state in single request.
	/// Replaces 4 separate API calls with unified endpoint for faster initialization.
	/// </summary>
	/// <param name="playerId">Player UUID to query state for</param>
	/// <param name="locationId">Location identifier</param>
	/// <returns>Result containing complete state or error message</returns>
	public async Task<Result<MiningStateData>> GetPlayerStateAsync(Guid playerId, string locationId)
	{
		// Input validation
		if (ValidateGuid(playerId, "Player ID").IsFailure(out var validationError))
			return Result.Failure<MiningStateData>(validationError.Message);

		if (string.IsNullOrEmpty(locationId))
			return Result.Failure<MiningStateData>(ValidationMessages.Required("Location ID"));

		var queryParams = new Dictionary<string, string> { ["location_id"] = locationId };
		return await QueryBackendAsync(
			$"/game/mining/player/{playerId}/state",
			queryParams,
			ParseStateData
		);
	}

	private Result<MiningStateData> ParseStateData(Godot.Collections.Dictionary jsonDict)
	{
		var playerIdResult = JsonParsingHelpers.ParseGuid(jsonDict, JsonFieldNames.PlayerId);
		var locationIdResult = JsonParsingHelpers.ParseString(jsonDict, JsonFieldNames.LocationId);
		var inventoryResult = JsonParsingHelpers.ParseIntDictionary(jsonDict, JsonFieldNames.Inventory);
		var upgradesResult = JsonParsingHelpers.ParseIntDictionary(jsonDict, JsonFieldNames.Upgrades);
		var lastExtractionTimeResult = JsonParsingHelpers.ParseNullableDateTime(jsonDict, "last_extraction_time");

		if (playerIdResult.IsFailure(out var playerIdError)) return Result.Failure<MiningStateData>(playerIdError.Message);
		if (!playerIdResult.IsSuccess(out var playerId)) return Result.Failure<MiningStateData>("Failed to extract player ID");
		if (locationIdResult.IsFailure(out var locationIdError)) return Result.Failure<MiningStateData>(locationIdError.Message);
		if (!locationIdResult.IsSuccess(out var locationId)) return Result.Failure<MiningStateData>("Failed to extract location ID");
		if (inventoryResult.IsFailure(out var inventoryError)) return Result.Failure<MiningStateData>(inventoryError.Message);
		if (!inventoryResult.IsSuccess(out var inventory)) return Result.Failure<MiningStateData>("Failed to extract inventory");
		if (upgradesResult.IsFailure(out var upgradesError)) return Result.Failure<MiningStateData>(upgradesError.Message);
		if (!upgradesResult.IsSuccess(out var upgrades)) return Result.Failure<MiningStateData>("Failed to extract upgrades");
		if (lastExtractionTimeResult.IsFailure(out var timestampError)) return Result.Failure<MiningStateData>(timestampError.Message);
		if (!lastExtractionTimeResult.IsSuccess(out var lastExtractionTime)) return Result.Failure<MiningStateData>("Failed to extract timestamp");

		// Parse nested metadata object
		if (!jsonDict.ContainsKey("metadata"))
			return Result.Failure<MiningStateData>("Missing required field: metadata");

		var metadataDict = jsonDict["metadata"].AsGodotDictionary();
		var metadataResult = ParseMetadataData(metadataDict);

		if (metadataResult.IsFailure(out var metadataError))
			return Result.Failure<MiningStateData>($"Failed to parse metadata: {metadataError.Message}");
		if (!metadataResult.IsSuccess(out var metadata))
			return Result.Failure<MiningStateData>("Failed to extract metadata");

		return Result.Success(new MiningStateData
		{
			PlayerId = playerId,
			LocationId = locationId,
			Inventory = inventory,
			Upgrades = upgrades,
			LastExtractionTime = lastExtractionTime,
			Metadata = metadata
		});
	}
}

/// <summary>
/// Location-specific metadata for first-time bonus tracking
/// </summary>
public record MiningMetadataData
{
	public required Guid PlayerId { get; init; }
	public required string LocationId { get; init; }

	/// <summary>Whether first-time bonus has been received at THIS location</summary>
	public required bool HasReceivedBonusAtLocation { get; init; }

	public required int TotalEvents { get; init; }
	public DateTime? FirstEventTime { get; init; }
	public DateTime? LastEventTime { get; init; }
}

/// <summary>
/// Complete mining game state for a specific location.
/// Inventory is GLOBAL (all locations). Upgrades and timestamp are LOCATION-SPECIFIC.
/// </summary>
public record MiningStateData
{
	public required Guid PlayerId { get; init; }

	/// <summary>Venue name for location-scoped data</summary>
	public required string LocationId { get; init; }

	/// <summary>Global inventory - gems from ALL locations</summary>
	public required Dictionary<string, int> Inventory { get; init; }

	/// <summary>Location-specific upgrade levels</summary>
	public required Dictionary<string, int> Upgrades { get; init; }

	/// <summary>Location-specific last extraction time (null = no extraction history)</summary>
	public DateTime? LastExtractionTime { get; init; }

	/// <summary>Location-specific metadata (bonus status)</summary>
	public required MiningMetadataData Metadata { get; init; }
}
