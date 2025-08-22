using System;
using System.Collections.Generic;
using System.Linq;

namespace BarBox.Games.MiningGame
{
	public class MiningGlobalData
	{
		public Dictionary<GemType, int> GlobalGemInventory { get; set; } = new();
		public List<CreditPurchaseTimer> CreditTimers { get; set; } = new();

		public void AddGems(GemType gemType, int amount)
		{
			if (!GlobalGemInventory.TryAdd(gemType, amount))
				GlobalGemInventory[gemType] += amount;
		}
		
		public bool HasSufficientGems(Dictionary<GemType, int> required)
		{
			foreach (var kvp in required)
			{
				if (!GlobalGemInventory.ContainsKey(kvp.Key) || GlobalGemInventory[kvp.Key] < kvp.Value)
					return false;
			}
			return true;
		}
		
		public void SpendGems(Dictionary<GemType, int> cost)
		{
			foreach (var kvp in cost)
			{
				if (GlobalGemInventory.ContainsKey(kvp.Key))
					GlobalGemInventory[kvp.Key] = Math.Max(0, GlobalGemInventory[kvp.Key] - kvp.Value);
			}
		}
		
		public int GetAvailableCredits(double currentGameTime)
		{
			return CreditTimers.Count(t => t.IsRecharged(currentGameTime));
		}
		
		public void CleanupExpiredTimers(double currentGameTime)
		{
			CreditTimers = CreditTimers.Where(t => !t.IsRecharged(currentGameTime)).ToList();
		}
	}
}