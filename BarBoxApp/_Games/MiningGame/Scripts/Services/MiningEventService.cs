using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BarBox.Games.MiningGame;

/// <summary>
/// Mining game-specific event service
/// Provides type-safe methods for emitting mining events
/// </summary>
public partial class MiningEventService : Node
{
	private EventService _eventService;

	public override void _Ready()
	{
		_eventService = EventService.GetInstance();
		if (_eventService == null)
		{
			GD.PrintErr("MiningEventService: EventService not found");
		}
	}

	/// <summary>
	/// Emit extraction complete event
	/// </summary>
	public async Task<Result<bool>> EmitExtractCompleteAsync(GemType gemType, int quantity)
	{
		if (_eventService == null)
			return Result<bool>.Failure("EventService not initialized");

		var payload = new
		{
			gem_type = gemType.ToString().ToLowerInvariant(),
			quantity = quantity,
			location_id = _eventService.GetCurrentLocationId()
		};

		return await _eventService.EmitEventAsync("mining/extract_complete", payload);
	}

	/// <summary>
	/// Emit upgrade purchase event
	/// </summary>
	public async Task<Result<bool>> EmitUpgradePurchaseAsync(
		UpgradeType upgradeType,
		int level,
		Dictionary<GemType, int> cost)
	{
		if (_eventService == null)
			return Result<bool>.Failure("EventService not initialized");

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

		return await _eventService.EmitEventAsync("mining/upgrade_purchase", payload);
	}

	/// <summary>
	/// Emit credit deposit event when gems are spent for credits
	/// </summary>
	public async Task<Result<bool>> EmitCreditDepositAsync(
		GemType gemType,
		int gemsSpent,
		int creditsEarned)
	{
		if (_eventService == null)
			return Result<bool>.Failure("EventService not initialized");

		var payload = new
		{
			gem_type = gemType.ToString().ToLowerInvariant(),
			gems_spent = gemsSpent,
			credits_earned = creditsEarned
		};

		return await _eventService.EmitEventAsync("mining/credit_deposit", payload);
	}

	/// <summary>
	/// Get player's global mining inventory from backend
	/// </summary>
	public async Task<Result<MiningInventoryData>> GetPlayerInventoryAsync(Guid playerId)
	{
		if (_eventService == null)
			return Result<MiningInventoryData>.Failure("EventService not initialized");

		// For now, the backend inventory query is not implemented yet
		// Once the backend is fully deployed, this will make an HTTP GET request to:
		// GET /game/mining/player/{playerId}/inventory
		//
		// This will return aggregated gem inventory from all mining/extract_complete events
		// For the initial migration, we'll keep using local state and emit events alongside
		await Task.Delay(1);
		return Result<MiningInventoryData>.Failure("Backend inventory query not yet implemented - using local state");
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
