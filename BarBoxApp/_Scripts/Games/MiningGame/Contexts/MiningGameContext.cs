using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Type-safe context for MiningGame data access
/// Provides clean separation between local (machine-specific) and global (cross-location) data
/// </summary>
public class MiningGameContext : BaseGameContext
{
	// Local data (machine-specific)
	public int LocalGemsReadyToExtract { get; private set; }
	public int GemMiningCapacity { get; private set; } = 150; // Starting capacity
	public Dictionary<UpgradeType, int> UpgradeLevels { get; private set; } = new();
	public DateTime LastMiningTick { get; private set; } = DateTime.UtcNow;
	public List<CreditPurchaseTimer> CreditTimers { get; private set; } = new();

	// Global data (cross-location)
	public Dictionary<string, int> GlobalGemInventory { get; private set; } = new();
	public int GlobalCredits { get; private set; }

	// Configuration
	public string LocationGemType { get; private set; } = "ruby"; // Default
	public TimeSpan MiningInterval { get; private set; } = TimeSpan.FromHours(2);
	public int BaseGemsPerTick { get; private set; } = 15;

	private MiningGameContext(DataStore dataStore, SessionManager sessionManager, string userId, string locationId)
		: base(dataStore, sessionManager, userId, locationId)
	{
	}

	/// <summary>
	/// Load mining game context for the specified user and location
	/// </summary>
	public static async Task<MiningGameContext> LoadAsync(string userId)
	{
		var dataStore = DataStore.GetInstance();
		var sessionManager = SessionManager.GetInstance();
		
		if (dataStore == null || sessionManager == null)
			throw new InvalidOperationException("Required services not available");

		var locationId = sessionManager.CurrentLocationId;
		var context = new MiningGameContext(dataStore, sessionManager, userId, locationId);

		bool success = await context.InitializeAsync();
		if (!success)
			throw new InvalidOperationException("Failed to initialize MiningGameContext");

		return context;
	}

	/// <summary>
	/// Load global (cross-location) data
	/// </summary>
	protected override async Task LoadGlobalDataAsync()
	{
		var globalDataResult = await _dataStore.GetGlobalDataAsync(_userId);
		if (!globalDataResult.IsSuccess)
			throw new InvalidOperationException($"Failed to load global data: {globalDataResult.Error}");

		var globalData = globalDataResult.Value;
		GlobalCredits = globalData.GlobalCredits;
		
		// Load global gem inventory
		GlobalGemInventory.Clear();
		foreach (var item in globalData.GlobalInventory)
		{
			if (item.Key.StartsWith("gem_"))
			{
				var gemType = item.Key.Substring(4); // Remove "gem_" prefix
				GlobalGemInventory[gemType] = item.Value;
			}
		}
	}

	/// <summary>
	/// Load local (machine-specific) data
	/// </summary>
	protected override async Task LoadLocalDataAsync()
	{
		var localDataResult = await _dataStore.GetLocalDataAsync(_userId);
		if (!localDataResult.IsSuccess)
			throw new InvalidOperationException($"Failed to load local data: {localDataResult.Error}");

		var localData = localDataResult.Value;
		// Parse mining-specific data
		LocalGemsReadyToExtract = SafeParseInt(localData.GameState.GetValueOrDefault("mining_gems_ready", "0"));
		GemMiningCapacity = SafeParseInt(localData.GameState.GetValueOrDefault("mining_capacity", "150"), 150);
		LastMiningTick = SafeParseBinaryDateTime(localData.GameState.GetValueOrDefault("mining_last_tick", ""));
		LocationGemType = localData.GameState.GetValueOrDefault("mining_gem_type", "ruby");

		// Load upgrade levels
		UpgradeLevels.Clear();
		foreach (UpgradeType upgradeType in Enum.GetValues<UpgradeType>())
		{
			var key = $"mining_upgrade_{upgradeType}";
			if (localData.GameState.TryGetValue(key, out var levelStr))
			{
				var level = SafeParseInt(levelStr);
				if (level > 0)
				{
					UpgradeLevels[upgradeType] = level;
					ApplyUpgradeEffect(upgradeType, level);
				}
			}
		}

		// Load credit timers
		CreditTimers.Clear();
		var timersData = localData.GameState.GetValueOrDefault("mining_credit_timers", "");
		if (!string.IsNullOrEmpty(timersData))
		{
			var timers = timersData.Split(';', StringSplitOptions.RemoveEmptyEntries);
			foreach (var timer in timers)
			{
				var parts = timer.Split(',');
				if (parts.Length == 3 && 
					long.TryParse(parts[0], out var purchaseTime) &&
					int.TryParse(parts[1], out var rechargeHours) &&
					int.TryParse(parts[2], out var credits))
				{
					CreditTimers.Add(new CreditPurchaseTimer
					{
						PurchaseTime = DateTime.FromBinary(purchaseTime),
						RechargeHours = rechargeHours,
						CreditsRecharging = credits
					});
				}
			}
		}
	}

	/// <summary>
	/// Save global data
	/// </summary>
	protected override async Task<bool> SaveGlobalDataAsync()
	{
		var globalDataResult = await _dataStore.GetGlobalDataAsync(_userId);
		if (!globalDataResult.IsSuccess)
			return false;

		var globalData = globalDataResult.Value;
		globalData.GlobalCredits = GlobalCredits;

		// Save global gem inventory
		foreach (var gem in GlobalGemInventory)
		{
			var key = $"gem_{gem.Key}";
			globalData.GlobalInventory[key] = gem.Value;
		}

		var saveResult = await _dataStore.SetGlobalDataAsync(_userId, globalData);
		return saveResult.IsSuccess;
	}

	/// <summary>
	/// Save local data
	/// </summary>
	protected override async Task<bool> SaveLocalDataAsync()
	{
		var localDataResult = await _dataStore.GetLocalDataAsync(_userId);
		if (!localDataResult.IsSuccess)
			return false;

		var localData = localDataResult.Value;
		// Store mining-specific data in game state
		localData.GameState["mining_gems_ready"] = LocalGemsReadyToExtract.ToString();
		localData.GameState["mining_capacity"] = GemMiningCapacity.ToString();
		localData.GameState["mining_last_tick"] = DateTimeToBinaryString(LastMiningTick);
		localData.GameState["mining_gem_type"] = LocationGemType;

		// Store upgrade levels
		foreach (var upgrade in UpgradeLevels)
		{
			localData.GameState[$"mining_upgrade_{upgrade.Key}"] = upgrade.Value.ToString();
		}

		// Store credit timers
		var timersData = string.Join(";", CreditTimers.Select(t => $"{DateTimeToBinaryString(t.PurchaseTime)},{t.RechargeHours},{t.CreditsRecharging}"));
		localData.GameState["mining_credit_timers"] = timersData;

		var saveResult = await _dataStore.SetLocalDataAsync(_userId, localData);
		return saveResult.IsSuccess;
	}

	/// <summary>
	/// Extract local gems to global inventory
	/// </summary>
	public async Task<bool> ExtractGemsAsync()
	{
		if (LocalGemsReadyToExtract <= 0)
			return false;

		// Add to global inventory
		if (!GlobalGemInventory.ContainsKey(LocationGemType))
			GlobalGemInventory[LocationGemType] = 0;
		
		GlobalGemInventory[LocationGemType] += LocalGemsReadyToExtract;

		// Clear local gems and save
		var extractedAmount = LocalGemsReadyToExtract;
		LocalGemsReadyToExtract = 0;

		bool globalSaved = await SaveGlobalDataAsync();
		bool localSaved = await SaveLocalDataAsync();

		if (globalSaved && localSaved)
		{
			LogInfo($"Extracted {extractedAmount} {LocationGemType} gems to global inventory");
			return true;
		}

		return false;
	}

	/// <summary>
	/// Purchase a credit using global gems
	/// </summary>
	public async Task<bool> PurchaseCreditsAsync(int creditAmount)
	{
		const int GEMS_PER_CREDIT = 150;
		int gemsNeeded = creditAmount * GEMS_PER_CREDIT;

		// Check if user has enough gems
		if (!GlobalGemInventory.ContainsKey(LocationGemType) || 
			GlobalGemInventory[LocationGemType] < gemsNeeded)
		{
			return false;
		}

		// Spend gems and add credits
		GlobalGemInventory[LocationGemType] -= gemsNeeded;
		
		bool creditsAdded = await AddGlobalCreditsAsync(creditAmount, $"Mining game gem exchange ({gemsNeeded} {LocationGemType} gems)");
		if (!creditsAdded)
		{
			// Rollback gem spending
			GlobalGemInventory[LocationGemType] += gemsNeeded;
			return false;
		}

		// Add credit recharge timer
		CreditTimers.Add(new CreditPurchaseTimer
		{
			PurchaseTime = DateTime.UtcNow,
			RechargeHours = 24,
			CreditsRecharging = creditAmount
		});

		// Save changes
		bool globalSaved = await SaveGlobalDataAsync();
		bool localSaved = await SaveLocalDataAsync();

		if (globalSaved && localSaved)
		{
			LogInfo($"Purchased {creditAmount} credits for {gemsNeeded} {LocationGemType} gems");
			return true;
		}

		return false;
	}

	/// <summary>
	/// Purchase an upgrade using location gems
	/// </summary>
	public async Task<bool> PurchaseUpgradeAsync(UpgradeType upgradeType)
	{
		var currentLevel = UpgradeLevels.GetValueOrDefault(upgradeType, 0);
		if (currentLevel >= 15) // Max level
			return false;

		var cost = CalculateUpgradeCost(upgradeType, currentLevel + 1);
		
		// Check if user has enough local gems
		if (LocalGemsReadyToExtract < cost.LocalGems)
			return false;

		// TODO: Check for other gem requirements in higher tiers
		
		// Spend gems and apply upgrade
		LocalGemsReadyToExtract -= cost.LocalGems;
		UpgradeLevels[upgradeType] = currentLevel + 1;

		// Apply upgrade effect
		ApplyUpgradeEffect(upgradeType, currentLevel + 1);

		bool saved = await SaveLocalDataAsync();
		if (saved)
		{
			LogInfo($"Purchased {upgradeType} upgrade level {currentLevel + 1}");
			return true;
		}

		return false;
	}

	/// <summary>
	/// Process mining tick (called periodically)
	/// </summary>
	public async Task<int> ProcessMiningTickAsync()
	{
		var now = DateTime.UtcNow;
		if (now - LastMiningTick < MiningInterval)
			return 0; // Not time for next tick yet

		// Calculate gems to mine
		var gemsMined = BaseGemsPerTick;
		
		// Apply mining amount upgrades
		if (UpgradeLevels.ContainsKey(UpgradeType.MiningAmount))
		{
			gemsMined += UpgradeLevels[UpgradeType.MiningAmount] * 5;
		}

		// Check capacity
		int gemsToAdd = Math.Min(gemsMined, GemMiningCapacity - LocalGemsReadyToExtract);
		if (gemsToAdd > 0)
		{
			LocalGemsReadyToExtract += gemsToAdd;
			LastMiningTick = now;
			
			await SaveLocalDataAsync();
			LogInfo($"Mined {gemsToAdd} {LocationGemType} gems (Total ready: {LocalGemsReadyToExtract}/{GemMiningCapacity})");
		}

		return gemsToAdd;
	}

	/// <summary>
	/// Get time until next mining tick
	/// </summary>
	public TimeSpan GetTimeUntilNextTick()
	{
		var nextTick = LastMiningTick.Add(MiningInterval);
		var timeLeft = nextTick - DateTime.UtcNow;
		return timeLeft > TimeSpan.Zero ? timeLeft : TimeSpan.Zero;
	}

	// Private helper methods

	private void ApplyUpgradeEffect(UpgradeType upgradeType, int level)
	{
		switch (upgradeType)
		{
			case UpgradeType.GemCapacity:
				GemMiningCapacity = 150 + (level * 25);
				break;
			case UpgradeType.MiningSpeed:
				var speedBonus = level * 0.1; // 10% faster per level
				MiningInterval = TimeSpan.FromHours(2.0 * (1.0 - speedBonus));
				break;
		}
	}

	private UpgradeCost CalculateUpgradeCost(UpgradeType upgradeType, int level)
	{
		// Simple cost calculation for prototype
		var baseCost = upgradeType switch
		{
			UpgradeType.GemCapacity => 50,
			UpgradeType.MiningAmount => 75,
			UpgradeType.MiningSpeed => 100,
			UpgradeType.CreditCharges => 125,
			_ => 50
		};

		var multiplier = Math.Pow(1.5, level - 1);
		return new UpgradeCost
		{
			LocalGems = (int)(baseCost * multiplier),
			RequiredGemTypes = new List<string>() // TODO: Implement tier-based requirements
		};
	}
}

// Supporting data structures
public enum UpgradeType
{
	GemCapacity,
	MiningAmount,
	MiningSpeed,
	CreditCharges
}

public class UpgradeCost
{
	public int LocalGems { get; set; }
	public List<string> RequiredGemTypes { get; set; } = new();
}

public class CreditPurchaseTimer
{
	public DateTime PurchaseTime { get; set; }
	public int RechargeHours { get; set; }
	public int CreditsRecharging { get; set; }

	public bool IsRecharged => DateTime.UtcNow >= PurchaseTime.AddHours(RechargeHours);
	public TimeSpan TimeRemaining => IsRecharged ? TimeSpan.Zero : PurchaseTime.AddHours(RechargeHours) - DateTime.UtcNow;
}