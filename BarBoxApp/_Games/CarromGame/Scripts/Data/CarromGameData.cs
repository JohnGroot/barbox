using System;

namespace BarBox.Games.Carrom;

// ============= GLOBAL DATA STORE =============

/// <summary>
/// Carrom game global data structure for DataStore integration.
	/// Stores cross-location carrom data like wins, losses, and win streaks.
	/// </summary>
	[Serializable]
	public class CarromGlobalDataStore
	{
		public int GlobalWins { get; set; } = 0;
		public int GlobalLosses { get; set; } = 0;
		public int BestWinStreak { get; set; } = 0;
	}

	// ============= LOCAL DATA =============

	/// <summary>
	/// Carrom game local data structure for DataStore integration.
	/// Stores location-specific carrom data like games played and session stats.
	/// </summary>
	[Serializable]
	public class CarromLocalData
	{
		public int GamesAtLocation { get; set; }
		public DateTime LastPlayedAt { get; set; } = DateTime.UtcNow;
		public int CurrentWinStreak { get; set; }
		public int SessionWins { get; set; }
		public int SessionLosses { get; set; }
	}
