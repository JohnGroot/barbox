using System;
using System.Collections.Generic;
using Godot;

namespace BarBox.Games.MiningGame
{
	[GlobalClass]
	public partial class MiningLocationState : Resource
	{
		[Export] public string LocationId { get; set; } = "";
		[Export] public int PendingGems { get; set; } = 0;
		[Export] public Godot.Collections.Dictionary<UpgradeType, int> UpgradeLevelsDict { get; set; } = new();
		[Export] public bool FirstTimeBonus { get; set; } = true;
		
		// Cache for performance optimization
		private Dictionary<UpgradeType, int> _cachedUpgradeLevels;
		private bool _upgradeLevelsCache_Valid = false;
		
		public Dictionary<UpgradeType, int> UpgradeLevels
		{
			get
			{
				if (!_upgradeLevelsCache_Valid || _cachedUpgradeLevels == null)
				{
					_cachedUpgradeLevels = new Dictionary<UpgradeType, int>();
					foreach (var kvp in UpgradeLevelsDict)
					{
						_cachedUpgradeLevels[kvp.Key] = kvp.Value;
					}
					_upgradeLevelsCache_Valid = true;
				}
				return _cachedUpgradeLevels;
			}
			set
			{
				UpgradeLevelsDict.Clear();
				foreach (var kvp in value)
					UpgradeLevelsDict[kvp.Key] = kvp.Value;
				_upgradeLevelsCache_Valid = false; // Invalidate cache
			}
		}
		
		// Method to invalidate cache when UpgradeLevelsDict is modified directly
		public void InvalidateUpgradeLevelsCache()
		{
			_upgradeLevelsCache_Valid = false;
		}
	}
}