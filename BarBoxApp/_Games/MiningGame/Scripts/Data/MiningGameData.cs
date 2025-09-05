using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace BarBox.Games.MiningGame
{
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
		MiningAmount,   // Gems per tick
		CreditCharges   // Maximum credit charges available
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
		public List<GameTimeCreditTimer> CreditTimers { get; set; } = new();

		/// <summary>
		/// Add gems using type-safe enum with modern C# conversion
		/// </summary>
		public void AddGems(GemType gemType, int amount)
		{
			var key = gemType.ToString(); // Modern enum to string conversion
			Gems[key] = Gems.GetValueOrDefault(key, 0) + amount;
		}

		/// <summary>
		/// Get gem count using type-safe enum
		/// </summary>
		public int GetGems(GemType gemType) => 
			Gems.GetValueOrDefault(gemType.ToString(), 0);

		/// <summary>
		/// Check if sufficient gems are available using type-safe dictionary
		/// </summary>
		public bool HasSufficientGems(Dictionary<GemType, int> required)
		{
			foreach (var kvp in required)
			{
				if (GetGems(kvp.Key) < kvp.Value)
					return false;
			}
			return true;
		}

		/// <summary>
		/// Spend gems using type-safe enum conversion
		/// </summary>
		public void SpendGems(Dictionary<GemType, int> cost)
		{
			foreach (var kvp in cost)
			{
				var key = kvp.Key.ToString();
				var currentAmount = Gems.GetValueOrDefault(key, 0);
				Gems[key] = Math.Max(0, currentAmount - kvp.Value);
			}
		}

		/// <summary>
		/// Get count of available (recharged) credits
		/// </summary>
		public int GetAvailableCredits(double currentGameTime)
		{
			return CreditTimers.Count(t => t.IsRecharged(currentGameTime));
		}

		/// <summary>
		/// Remove expired credit timers that have recharged
		/// </summary>
		public void CleanupExpiredTimers(double currentGameTime)
		{
			CreditTimers = CreditTimers.Where(t => !t.IsRecharged(currentGameTime)).ToList();
		}
	}

	// ============= LOCAL DATA =============

	/// <summary>
	/// Mining game local data structure for DataStore integration.
	/// Matches the format expected by DataStore.LocalUserData.Mining property.
	/// </summary>
	[Serializable]
	public class MiningLocalData
	{
		public int LocalGemsReady { get; set; } = 0;
		public int GemCapacity { get; set; } = 150;
		public DateTime LastMiningTick { get; set; } = DateTime.UtcNow;
		public string LocationGemType { get; set; } = "Amethyst";
		public Dictionary<string, int> Upgrades { get; set; } = new(); // upgrade_type -> level
		public List<CreditPurchaseTimer> CreditTimers { get; set; } = new();
	}

	// ============= TIMER CLASSES =============

	/// <summary>
	/// Data class for tracking credit purchase timers using game time in the mining game.
	/// Each timer represents a credit that was purchased and needs to recharge before another can be bought.
	/// </summary>
	public class GameTimeCreditTimer
	{
		/// <summary>
		/// Game time when the credit was purchased.
		/// </summary>
		public double PurchaseGameTime { get; set; }
		
		/// <summary>
		/// Game time when the credit will be fully recharged and available for purchase again.
		/// </summary>
		public double RechargeGameTime { get; set; }
		
		/// <summary>
		/// Checks if the credit has finished recharging and is available for purchase.
		/// </summary>
		/// <param name="currentGameTime">Current game time in seconds</param>
		/// <returns>True if the credit has recharged</returns>
		public bool IsRecharged(double currentGameTime) => currentGameTime >= RechargeGameTime;
		
		/// <summary>
		/// Gets the remaining time in seconds until the credit is recharged.
		/// </summary>
		/// <param name="currentGameTime">Current game time in seconds</param>
		/// <returns>Remaining seconds until recharge, or 0 if already recharged</returns>
		public double GetTimeRemainingSeconds(double currentGameTime) 
		{
			return Math.Max(0, RechargeGameTime - currentGameTime);
		}
		
		/// <summary>
		/// Gets the remaining time in hours until the credit is recharged.
		/// </summary>
		/// <param name="currentGameTime">Current game time in seconds</param>
		/// <returns>Remaining hours until recharge, or 0 if already recharged</returns>
		public float GetTimeRemainingHours(double currentGameTime)
		{
			return (float)(GetTimeRemainingSeconds(currentGameTime) / 3600.0);
		}
	}

	[Serializable]
	public class CreditPurchaseTimer
	{
		public DateTime PurchaseTime { get; set; }
		public int RechargeHours { get; set; }
		public int CreditsRecharging { get; set; }
		
		public bool IsRecharged => DateTime.UtcNow >= PurchaseTime.AddHours(RechargeHours);
		public TimeSpan TimeRemaining => IsRecharged ? TimeSpan.Zero : PurchaseTime.AddHours(RechargeHours) - DateTime.UtcNow;
	}


	// ============= LOCATION STATE =============

	/// <summary>
	/// Simple data structure for MiningGame location-specific state.
	/// Aligned with DataStore's MiningLocalData format for direct integration.
	/// </summary>
	[GlobalClass]
	public partial class MiningLocationState : Resource
	{
		[Export] public string LocationId { get; set; } = "";
		[Export] public int PendingGems { get; set; } = 0;
		[Export] public Godot.Collections.Dictionary<string, int> UpgradeLevels { get; set; } = new();
		[Export] public bool FirstTimeBonus { get; set; } = true;
		public DateTime LastSaveTime { get; set; } = DateTime.UtcNow; // Not exported - DateTime not supported by Godot
		public DateTime LastMiningTickTime { get; set; } = DateTime.UtcNow; // Not exported - DateTime not supported by Godot
	}

}