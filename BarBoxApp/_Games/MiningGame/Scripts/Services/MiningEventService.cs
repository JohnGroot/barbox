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

		var payload = new
		{
			gem_type = gemType.ToString().ToLowerInvariant(),
			quantity = quantity,
			location_id = _eventService.GetCurrentLocationId()
		};

		return await EmitEventSafeAsync("mining/extract_complete", payload);
	}

	/// <summary>
	/// Emit upgrade purchase event with validation
	/// </summary>
	public async Task<Result<bool>> EmitUpgradePurchaseAsync(
		UpgradeType upgradeType,
		int level,
		Dictionary<GemType, int> cost)
	{
		// Input validation
		if (level < 1)
			return Result<bool>.Failure("Level must be at least 1");

		if (cost == null || cost.Count == 0)
			return Result<bool>.Failure("Upgrade cost is required");

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
			cost = costDict
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

			var gemsDict = jsonDict["gems"].AsGodotDictionary();
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
