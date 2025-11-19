using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace BarBox.Games.MiningGame;

// ============= ENUMS =============

public enum GemType
{
	Ruby,
	Sapphire,
	Emerald,
	Diamond,
	Amethyst
}

public enum UpgradeType
{
	Capacity,       // Max gem storage capacity
	MiningSpeed,    // Mining tick frequency (reduces time)
	MiningAmount    // Gems per tick
}

public enum UpgradeTier
{
	Tier1 = 1,      // Levels 1-5: Location gem only
	Tier2 = 2,      // Levels 6-10: Location gem + 1 other gem
	Tier3 = 3       // Levels 11-15: Location gem + 2 other gems
}

// ============= GLOBAL DATA STORE =============

/// <summary>
/// Mining game global data structure for DataStore integration.
/// Stores cross-location mining data like gems collected across all locations.
/// </summary>
[Serializable]
public class MiningGlobalDataStore
{
	public Dictionary<string, int> Gems { get; set; } = new();

	public void AddGems(GemType gemType, int amount)
	{
		var key = gemType.ToString();
		Gems[key] = Gems.GetValueOrDefault(key, 0) + amount;
	}

	public int GetGems(GemType gemType) =>
		Gems.GetValueOrDefault(gemType.ToString(), 0);

	public bool HasSufficientGems(Dictionary<GemType, int> required)
	{
		foreach (var kvp in required)
		{
			if (GetGems(kvp.Key) < kvp.Value)
				return false;
		}
		return true;
	}

	public void SpendGems(Dictionary<GemType, int> cost)
	{
		foreach (var kvp in cost)
		{
			var key = kvp.Key.ToString();
			var currentAmount = Gems.GetValueOrDefault(key, 0);
			Gems[key] = Math.Max(0, currentAmount - kvp.Value);
		}
	}
}