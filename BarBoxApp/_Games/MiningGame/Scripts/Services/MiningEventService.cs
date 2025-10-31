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
			return Result<bool>.Failure("Quantity must be at least 1");

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
			return Result<bool>.Failure("Level must be at least 1");

		if (cost == null || cost.Count == 0)
			return Result<bool>.Failure("Upgrade cost is required");

		if (string.IsNullOrEmpty(locationId))
			return Result<bool>.Failure("Location ID is required");

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
			return Result<bool>.Failure("Gems spent must be at least 1");

		if (creditsEarned < 1)
			return Result<bool>.Failure("Credits earned must be at least 1");

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
		return Result<bool>.Failure("Location ID is required");

	if (pendingGems < 0)
		return Result<bool>.Failure("Pending gems cannot be negative");

	var payload = new
	{
		location_id = locationId,
		pending_gems = pendingGems,
		timestamp = System.DateTime.UtcNow
	};

	return await EmitEventSafeAsync("mining/tick_update", payload);
}

	/// <summary>
	/// Create backend session for mining game events
	/// REQUIRED before any events can be emitted
	/// </summary>
	public async Task<Result<Guid>> CreateSessionAsync(Guid boxId, Guid playerId)
	{
		// Input validation
		if (boxId == Guid.Empty)
			return Result<Guid>.Failure("Box ID is required");

		if (playerId == Guid.Empty)
			return Result<Guid>.Failure("Player ID is required");

		if (_eventService == null || !GodotObject.IsInstanceValid(_eventService))
			return Result<Guid>.Failure("Event service not available");

		// Delegate to EventService
		var result = await _eventService.CreateSessionAsync(boxId, playerId);

		if (result.IsSuccess)
			LogInfo($"Mining session created: {result.Value}");
		else
			LogError($"Failed to create mining session: {result.Error}");

		return result;
	}

/// <summary>
/// Get player's global mining inventory from backend using shared HTTP helper
/// </summary>
public async Task<Result<MiningInventoryData>> GetPlayerInventoryAsync(Guid playerId)
	{
		// Input validation
		if (playerId == Guid.Empty)
			return Result<MiningInventoryData>.Failure("Player ID is required");

		try
		{
			// Use shared HTTP helper - handles connection, polling, timeouts
			var responseResult = await QueryBackendRawAsync($"/game/mining/player/{playerId}/inventory");
			if (!responseResult.IsSuccess)
				return Result<MiningInventoryData>.Failure(responseResult.Error);

			// Parse JSON response using base class helper
			var parseResult = ParseJsonResponse(responseResult.Value);
			if (!parseResult.IsSuccess)
				return Result<MiningInventoryData>.Failure(parseResult.Error);

			var jsonDict = parseResult.Value;

			var inventoryData = new MiningInventoryData
			{
				PlayerId = Guid.Parse(jsonDict["player_id"].AsString()),
				LastUpdated = DateTime.Parse(jsonDict["last_updated"].AsString()),
				Gems = new Dictionary<string, int>()
			};

			Variant gemsVariant = jsonDict["gems"];
			var gemsDict = gemsVariant.AsGodotDictionary();
			foreach (var key in gemsDict.Keys)
			{
				inventoryData.Gems[key.AsString()] = gemsDict[key].AsInt32();
			}

			return Result<MiningInventoryData>.Success(inventoryData);
		}
		catch (FormatException ex)
		{
			LogError($"Invalid data format in inventory response: {ex.Message}");
			return Result<MiningInventoryData>.Failure("Invalid server response format");
		}
		catch (System.Exception ex)
		{
			LogError($"Unexpected error retrieving inventory: {ex.Message}");
			return Result<MiningInventoryData>.Failure("Unable to retrieve inventory data");
		}
	}

	/// <summary>
	/// Get player's upgrade levels from backend using shared HTTP helper
	/// </summary>
	public async Task<Result<MiningUpgradesData>> GetPlayerUpgradesAsync(Guid playerId)
	{
		// Input validation
		if (playerId == Guid.Empty)
			return Result<MiningUpgradesData>.Failure("Player ID is required");

		try
		{
			// Use shared HTTP helper
			var responseResult = await QueryBackendRawAsync($"/game/mining/player/{playerId}/upgrades");
			if (!responseResult.IsSuccess)
				return Result<MiningUpgradesData>.Failure(responseResult.Error);

			// Parse JSON response using base class helper
			var parseResult = ParseJsonResponse(responseResult.Value);
			if (!parseResult.IsSuccess)
				return Result<MiningUpgradesData>.Failure(parseResult.Error);

			var jsonDict = parseResult.Value;

			var upgradesData = new MiningUpgradesData
			{
				PlayerId = Guid.Parse(jsonDict["player_id"].AsString()),
				LastUpdated = DateTime.Parse(jsonDict["last_updated"].AsString()),
				Upgrades = new Dictionary<string, int>()
			};

			Variant upgradesVariant = jsonDict["upgrades"];
			var upgradesDict = upgradesVariant.AsGodotDictionary();
			foreach (var key in upgradesDict.Keys)
			{
				upgradesData.Upgrades[key.AsString()] = upgradesDict[key].AsInt32();
			}

			return Result<MiningUpgradesData>.Success(upgradesData);
		}
		catch (FormatException ex)
		{
			LogError($"Invalid data format in upgrades response: {ex.Message}");
			return Result<MiningUpgradesData>.Failure("Invalid server response format");
		}
		catch (System.Exception ex)
		{
			LogError($"Unexpected error retrieving upgrades: {ex.Message}");
			return Result<MiningUpgradesData>.Failure("Unable to retrieve upgrade data");
		}
	}

	/// <summary>
	/// Get player's last mining timestamp for a location from backend
	/// </summary>
	public async Task<Result<MiningTimestampData>> GetPlayerMiningTimestampAsync(Guid playerId, string locationId)
	{
		// Input validation
		if (playerId == Guid.Empty)
			return Result<MiningTimestampData>.Failure("Player ID is required");

		if (string.IsNullOrEmpty(locationId))
			return Result<MiningTimestampData>.Failure("Location ID is required");

		try
		{
			// Query with location_id as query parameter
			var queryParams = new Dictionary<string, string> { ["location_id"] = locationId };
			var responseResult = await QueryBackendRawAsync($"/game/mining/player/{playerId}/mining_timestamp", queryParams);
			if (!responseResult.IsSuccess)
				return Result<MiningTimestampData>.Failure(responseResult.Error);

			// Parse JSON response using base class helper
			var parseResult = ParseJsonResponse(responseResult.Value);
			if (!parseResult.IsSuccess)
				return Result<MiningTimestampData>.Failure(parseResult.Error);

			var jsonDict = parseResult.Value;

			var timestampData = new MiningTimestampData
			{
				PlayerId = Guid.Parse(jsonDict["player_id"].AsString()),
				LocationId = jsonDict["location_id"].AsString(),
				LastMiningTime = DateTime.Parse(jsonDict["last_mining_time"].AsString())
			};

			return Result<MiningTimestampData>.Success(timestampData);
		}
		catch (FormatException ex)
		{
			LogError($"Invalid data format in timestamp response: {ex.Message}");
			return Result<MiningTimestampData>.Failure("Invalid server response format");
		}
		catch (System.Exception ex)
		{
			LogError($"Unexpected error retrieving mining timestamp: {ex.Message}");
			return Result<MiningTimestampData>.Failure("Unable to retrieve timestamp data");
		}
	}

	/// <summary>
	/// Get player's mining metadata from backend
	/// </summary>
	public async Task<Result<MiningMetadataData>> GetPlayerMetadataAsync(Guid playerId)
	{
		// Input validation
		if (playerId == Guid.Empty)
			return Result<MiningMetadataData>.Failure("Player ID is required");

		try
		{
			// Use shared HTTP helper
			var responseResult = await QueryBackendRawAsync($"/game/mining/player/{playerId}/metadata");
			if (!responseResult.IsSuccess)
				return Result<MiningMetadataData>.Failure(responseResult.Error);

			// Parse JSON response using base class helper
			var parseResult = ParseJsonResponse(responseResult.Value);
			if (!parseResult.IsSuccess)
				return Result<MiningMetadataData>.Failure(parseResult.Error);

			var jsonDict = parseResult.Value;

			var metadataData = new MiningMetadataData
			{
				PlayerId = Guid.Parse(jsonDict["player_id"].AsString()),
				HasReceivedBonus = jsonDict["has_received_bonus"].AsBool(),
				TotalEvents = jsonDict["total_events"].AsInt32(),
				FirstEventTime = jsonDict.ContainsKey("first_event_time") && jsonDict["first_event_time"].VariantType != Godot.Variant.Type.Nil
					? DateTime.Parse(jsonDict["first_event_time"].AsString())
					: (DateTime?)null,
				LastEventTime = jsonDict.ContainsKey("last_event_time") && jsonDict["last_event_time"].VariantType != Godot.Variant.Type.Nil
					? DateTime.Parse(jsonDict["last_event_time"].AsString())
					: (DateTime?)null
			};

			return Result<MiningMetadataData>.Success(metadataData);
		}
		catch (FormatException ex)
		{
			LogError($"Invalid data format in metadata response: {ex.Message}");
			return Result<MiningMetadataData>.Failure("Invalid server response format");
		}
		catch (System.Exception ex)
		{
			LogError($"Unexpected error retrieving metadata: {ex.Message}");
			return Result<MiningMetadataData>.Failure("Unable to retrieve metadata");
		}
	}

	/// <summary>
	/// Emit first-time bonus event
	/// </summary>
	public async Task<Result<bool>> EmitFirstTimeBonusAsync(Guid playerId, string locationId, int bonusAmount)
	{
		// Input validation
		if (playerId == Guid.Empty)
			return Result<bool>.Failure("Player ID is required");

		if (bonusAmount < 1)
			return Result<bool>.Failure("Bonus amount must be at least 1");

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
