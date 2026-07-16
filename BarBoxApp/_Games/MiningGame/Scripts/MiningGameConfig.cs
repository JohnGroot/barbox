using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace BarBox.Games.MiningGame;

[GlobalClass]
public partial class MiningGameConfig : Resource
{
	[ExportCategory("Game Mechanics")]
	[Export]
	public float BaseMiningTimeSeconds { get; set; } = 7200.0f; // 2 hours

	[Export]
	public int BaseGemsPerTick { get; set; } = 25;

	[Export]
	public int BaseCapacity { get; set; } = 150;

	[Export]
	public int CreditCost { get; set; } = 150; // Gems needed per credit

	[Export]
	public int MaxUpgradeLevel { get; set; } = 15;

	[ExportCategory("Upgrade Scaling")]
	[Export]
	public int CapacityPerLevel { get; set; } = 50;

	[Export]
	public float SpeedMultiplierPerLevel { get; set; } = 0.9f; // 10% faster per level

	[Export]
	public int AmountPerLevel { get; set; } = 10;

	[ExportCategory("Upgrade Costs")]
	[Export]
	public Godot.Collections.Dictionary<UpgradeType, UpgradeCostData> UpgradeCostsDict { get; set; }

	[ExportCategory("Upgrade Tiers")]
	[Export]
	public int Tier1MaxLevel { get; set; } = 5;

	[Export]
	public int Tier2MaxLevel { get; set; } = 10;

	[Export]
	public int Tier3MaxLevel { get; set; } = 15;

	// Runtime caching
	private Dictionary<UpgradeType, UpgradeCostData> _upgradeCostsCache;
	private Random _random = new Random();

	public MiningGameConfig()
	{
		InitializeDefaults();
	}

	private void InitializeDefaults()
	{
		if (UpgradeCostsDict == null || UpgradeCostsDict.Count == 0)
		{
			UpgradeCostsDict = new Godot.Collections.Dictionary<UpgradeType, UpgradeCostData>
			{
				{ UpgradeType.Capacity, new UpgradeCostData { BaseCost = 100, CostMultiplier = 1.4f } },
				{ UpgradeType.MiningSpeed, new UpgradeCostData { BaseCost = 150, CostMultiplier = 1.5f } },
				{ UpgradeType.MiningAmount, new UpgradeCostData { BaseCost = 120, CostMultiplier = 1.45f } },
			};
		}
	}

	public Dictionary<UpgradeType, UpgradeCostData> UpgradeCosts
	{
		get
		{
			if (_upgradeCostsCache != null)
			{
				return _upgradeCostsCache;
			}

			_upgradeCostsCache = new Dictionary<UpgradeType, UpgradeCostData>();
			foreach (var kvp in UpgradeCostsDict)
			{
				_upgradeCostsCache[kvp.Key] = kvp.Value;
			}

			return _upgradeCostsCache;
		}
	}

	public Dictionary<GemType, int> GetUpgradeCost(UpgradeType upgradeType, int currentLevel, GemType primaryGemType)
	{
		var costs = new Dictionary<GemType, int>();

		if (!UpgradeCosts.TryGetValue(upgradeType, out var costData))
		{
			throw new InvalidOperationException(
				$"Upgrade cost not configured for {upgradeType}. This is a configuration error.");
		}

		int nextLevel = currentLevel + 1;
		var tier = GetUpgradeTier(nextLevel);
		int baseCost = costData.CalculateCost(currentLevel);

		// Primary gem cost (location gem)
		costs[primaryGemType] = baseCost;

		// Additional gems for higher tiers
		if (tier >= UpgradeTier.Tier2)
		{
			var otherGem = GetRandomOtherGemType(costs.Keys);
			costs[otherGem] = baseCost / 2; // 50% of base cost
		}

		if (tier >= UpgradeTier.Tier3)
		{
			var secondOtherGem = GetRandomOtherGemType(costs.Keys);
			costs[secondOtherGem] = baseCost / 3; // 33% of base cost
		}

		return costs;
	}

	public UpgradeTier GetUpgradeTier(int level)
	{
		if (level <= Tier1MaxLevel)
		{
			return UpgradeTier.Tier1;
		}

		if (level <= Tier2MaxLevel)
		{
			return UpgradeTier.Tier2;
		}

		return UpgradeTier.Tier3;
	}

	/// <summary>
	/// Calculate max capacity at given upgrade level.
	/// Formula: BaseCapacity + (level * CapacityPerLevel)
	/// </summary>
	public int GetMaxCapacity(int capacityLevel) =>
		BaseCapacity + (capacityLevel * CapacityPerLevel);

	/// <summary>
	/// Calculate mining tick time at given upgrade level.
	/// Formula: BaseMiningTimeSeconds * (SpeedMultiplierPerLevel ^ level)
	/// </summary>
	public float GetMiningTickTime(int speedLevel) =>
		BaseMiningTimeSeconds * Mathf.Pow(SpeedMultiplierPerLevel, speedLevel);

	/// <summary>
	/// Calculate gems per tick at given upgrade level.
	/// Formula: BaseGemsPerTick + (level * AmountPerLevel)
	/// </summary>
	public int GetGemsPerTick(int amountLevel) =>
		BaseGemsPerTick + (amountLevel * AmountPerLevel);

	private GemType GetRandomOtherGemType(IEnumerable<GemType> exclude)
	{
		var allGems = Enum.GetValues<GemType>()
			.Where(g => !exclude.Contains(g))
			.ToArray();

		if (allGems.Length == 0)
		{
			return GemType.Diamond; // Fallback
		}

		// Use a seeded random based on the exclude list for consistency
		int seed = exclude.Sum(g => (int)g);
		var seededRandom = new Random(seed);
		return allGems[seededRandom.Next(allGems.Length)];
	}
}
