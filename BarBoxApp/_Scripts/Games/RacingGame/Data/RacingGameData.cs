using System;
using System.Collections.Generic;

namespace BarBox.Games.Racing
{
	// ============= GLOBAL DATA STORE =============

	/// <summary>
	/// Racing game global data structure for DataStore integration.
	/// Stores cross-location racing data like best times and global ranking.
	/// </summary>
	[Serializable]
	public class RacingGlobalDataStore
	{
		public Dictionary<string, float> BestLapTimes { get; set; } = new();
		public int TotalRaces { get; set; } = 0;
		public int GlobalRanking { get; set; } = 0;
		public Dictionary<string, float> BestRaceTimes { get; set; } = new();
	}

	// ============= LOCAL DATA =============

	/// <summary>
	/// Racing game local data structure for DataStore integration.
	/// Stores location-specific racing data like session stats and favorite tracks.
	/// </summary>
	[Serializable]
	public class RacingLocalData
	{
		public DateTime LastPlayedAt { get; set; } = DateTime.UtcNow;
		public int RacesAtLocation { get; set; }
		public string FavoriteTrack { get; set; } = string.Empty;
		public int SessionRaces { get; set; }
		public int SessionPersonalBests { get; set; }
	}
}