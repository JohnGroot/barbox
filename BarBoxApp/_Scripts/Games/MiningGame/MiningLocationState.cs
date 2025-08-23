using System;
using System.Collections.Generic;

namespace BarBox.Games.MiningGame
{
	public class MiningLocationState
	{
		public string LocationId { get; set; } = "";
		public int PendingGems { get; set; } = 0;
		public Dictionary<UpgradeType, int> UpgradeLevelsDict { get; set; } = new();
		public bool FirstTimeBonus { get; set; } = true;
		public DateTime LastSaveTime { get; set; } = DateTime.UtcNow;
		public DateTime LastMiningTickTime { get; set; } = DateTime.UtcNow; // Timestamp when last mining tick occurred
		
		// Legacy field for migration - will be removed after migration
		public float PartialProgress { get; set; } = 0.0f;
		
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
				UpgradeLevelsDict = new Dictionary<UpgradeType, int>(value);
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