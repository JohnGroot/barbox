using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Type-safe context for RacingGame data access
/// Focuses on global high score tracking across all locations
/// </summary>
public class RacingGameContext : BaseGameContext
{
	// Global data (cross-location)
	public int GlobalCredits { get; private set; }
	public Dictionary<string, float> BestLapTimes { get; private set; } = new(); // per track
	public Dictionary<string, float> BestRaceTimes { get; private set; } = new(); // per track  
	public int TotalRacesCompleted { get; private set; }
	public int GlobalRanking { get; private set; } = -1; // -1 = unranked

	// Local data (machine-specific)
	public DateTime LastPlayedAt { get; private set; } = DateTime.UtcNow;
	public int RacesAtLocation { get; private set; }
	public string FavoriteTrackAtLocation { get; private set; } = string.Empty;

	// Session data
	public int SessionRaces { get; private set; }
	public int SessionPersonalBests { get; private set; }
	public List<RaceResult> SessionResults { get; private set; } = new();

	private RacingGameContext(DataStore dataStore, SessionManager sessionManager, string userId, string locationId)
		: base(dataStore, sessionManager, userId, locationId)
	{
	}

	/// <summary>
	/// Load racing game context for the specified user
	/// </summary>
	public static async Task<RacingGameContext> LoadAsync(string userId)
	{
		var dataStore = DataStore.GetInstance();
		var sessionManager = SessionManager.GetInstance();
		
		if (dataStore == null || sessionManager == null)
			throw new InvalidOperationException("Required services not available");

		var locationId = sessionManager.CurrentLocationId;
		var context = new RacingGameContext(dataStore, sessionManager, userId, locationId);

		bool success = await context.InitializeAsync();
		if (!success)
			throw new InvalidOperationException("Failed to initialize RacingGameContext");

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

		// Load global racing stats
		TotalRacesCompleted = globalData.GlobalHighScores.GetValueOrDefault("racing_total_races", 0);
		GlobalRanking = globalData.GlobalHighScores.GetValueOrDefault("racing_ranking", -1);

		// Load best times from global inventory (using special format)
		BestLapTimes.Clear();
		BestRaceTimes.Clear();
		foreach (var item in globalData.GlobalInventory)
		{
			if (item.Key.StartsWith("racing_lap_"))
			{
				var trackId = item.Key.Substring(11); // Remove "racing_lap_" prefix
				BestLapTimes[trackId] = BitConverter.Int32BitsToSingle(item.Value);
			}
			else if (item.Key.StartsWith("racing_race_"))
			{
				var trackId = item.Key.Substring(12); // Remove "racing_race_" prefix
				BestRaceTimes[trackId] = BitConverter.Int32BitsToSingle(item.Value);
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
		LastPlayedAt = SafeParseBinaryDateTime(localData.GameState.GetValueOrDefault("racing_last_played", ""));
		RacesAtLocation = SafeParseInt(localData.GameState.GetValueOrDefault("racing_races_location", "0"));
		FavoriteTrackAtLocation = localData.GameState.GetValueOrDefault("racing_favorite_track", "");
		SessionRaces = SafeParseInt(localData.GameState.GetValueOrDefault("racing_session_races", "0"));
		SessionPersonalBests = SafeParseInt(localData.GameState.GetValueOrDefault("racing_session_pbs", "0"));
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

		// Update global stats
		globalData.GlobalHighScores["racing_total_races"] = TotalRacesCompleted;
		globalData.GlobalHighScores["racing_ranking"] = GlobalRanking;

		// Save best times in global inventory (using bit conversion for floats)
		foreach (var lapTime in BestLapTimes)
		{
			var key = $"racing_lap_{lapTime.Key}";
			globalData.GlobalInventory[key] = BitConverter.SingleToInt32Bits(lapTime.Value);
		}

		foreach (var raceTime in BestRaceTimes)
		{
			var key = $"racing_race_{raceTime.Key}";
			globalData.GlobalInventory[key] = BitConverter.SingleToInt32Bits(raceTime.Value);
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
		// Store racing-specific local data
		localData.GameState["racing_last_played"] = DateTimeToBinaryString(LastPlayedAt);
		localData.GameState["racing_races_location"] = RacesAtLocation.ToString();
		localData.GameState["racing_favorite_track"] = FavoriteTrackAtLocation;
		localData.GameState["racing_session_races"] = SessionRaces.ToString();
		localData.GameState["racing_session_pbs"] = SessionPersonalBests.ToString();

		var saveResult = await _dataStore.SetLocalDataAsync(_userId, localData);
		return saveResult.IsSuccess;
	}

	/// <summary>
	/// Start a time trial race (requires credits)
	/// </summary>
	public async Task<bool> StartTimeTrialAsync(int creditCost)
	{
		if (creditCost > 0)
		{
			bool spent = await SpendGlobalCreditsAsync(creditCost, "Racing Time Trial");
			if (!spent)
			{
				LogWarning($"Insufficient credits for time trial ({GlobalCredits}/{creditCost})");
				return false;
			}
			GlobalCredits -= creditCost;
		}

		LogInfo($"Started time trial race for {creditCost} credits");
		return true;
	}

	/// <summary>
	/// Submit a completed race result
	/// </summary>
	public async Task<RaceResultStatus> SubmitRaceResultAsync(string trackId, float raceTime, float bestLapTime)
	{
		var result = new RaceResult
		{
			TrackId = trackId,
			RaceTime = raceTime,
			BestLapTime = bestLapTime,
			CompletedAt = DateTime.UtcNow,
			LocationId = LocationId
		};

		SessionResults.Add(result);
		SessionRaces++;
		TotalRacesCompleted++;
		RacesAtLocation++;
		LastPlayedAt = DateTime.UtcNow;

		var status = RaceResultStatus.Completed;

		// Check for personal bests
		bool isNewBestLap = false;
		bool isNewBestRace = false;

		// Check lap time personal best
		if (!BestLapTimes.ContainsKey(trackId) || bestLapTime < BestLapTimes[trackId])
		{
			BestLapTimes[trackId] = bestLapTime;
			isNewBestLap = true;
			status |= RaceResultStatus.PersonalBestLap;
		}

		// Check race time personal best
		if (!BestRaceTimes.ContainsKey(trackId) || raceTime < BestRaceTimes[trackId])
		{
			BestRaceTimes[trackId] = raceTime;
			isNewBestRace = true;
			status |= RaceResultStatus.PersonalBestRace;
		}

		if (isNewBestLap || isNewBestRace)
		{
			SessionPersonalBests++;
		}

		// Update favorite track at location
		UpdateFavoriteTrack(trackId);

		// Reset idle timer
		ResetIdleTimer();

		// Save data
		await SaveGlobalDataAsync();
		await SaveLocalDataAsync();

		string statusText = "Race completed";
		if (isNewBestLap && isNewBestRace)
			statusText += " - New personal best lap AND race time!";
		else if (isNewBestLap)
			statusText += " - New personal best lap time!";
		else if (isNewBestRace)
			statusText += " - New personal best race time!";

		LogInfo($"{statusText} ({raceTime:F2}s race, {bestLapTime:F2}s lap)");

		return status;
	}

	/// <summary>
	/// Get personal best for a specific track
	/// </summary>
	public (float raceTime, float lapTime) GetPersonalBest(string trackId)
	{
		var raceTime = BestRaceTimes.GetValueOrDefault(trackId, 0f);
		var lapTime = BestLapTimes.GetValueOrDefault(trackId, 0f);
		return (raceTime, lapTime);
	}

	/// <summary>
	/// Get all tracks the user has raced on
	/// </summary>
	public string[] GetRacedTracks()
	{
		var tracks = new HashSet<string>();
		tracks.UnionWith(BestRaceTimes.Keys);
		tracks.UnionWith(BestLapTimes.Keys);
		return tracks.ToArray();
	}

	/// <summary>
	/// Get session statistics
	/// </summary>
	public SessionStats GetSessionStats()
	{
		float avgRaceTime = 0f;
		float avgLapTime = 0f;

		if (SessionResults.Count > 0)
		{
			avgRaceTime = SessionResults.Average(r => r.RaceTime);
			avgLapTime = SessionResults.Average(r => r.BestLapTime);
		}

		return new SessionStats
		{
			RacesCompleted = SessionRaces,
			PersonalBests = SessionPersonalBests,
			AverageRaceTime = avgRaceTime,
			AverageLapTime = avgLapTime,
			SessionDuration = SessionResults.Count > 0 ? DateTime.UtcNow - SessionResults.First().CompletedAt : TimeSpan.Zero
		};
	}

	/// <summary>
	/// Get racing statistics for display
	/// </summary>
	public RacingStats GetRacingStats()
	{
		return new RacingStats
		{
			TotalRaces = TotalRacesCompleted,
			RacesAtLocation = RacesAtLocation,
			TracksRaced = GetRacedTracks().Length,
			GlobalRanking = GlobalRanking,
			FavoriteTrack = FavoriteTrackAtLocation,
			SessionRaces = SessionRaces
		};
	}

	// Private helper methods

	private void UpdateFavoriteTrack(string trackId)
	{
		// Simple implementation: track with most races at this location
		// In a full implementation, could be more sophisticated
		FavoriteTrackAtLocation = trackId;
	}
}

// Supporting data structures
[Flags]
public enum RaceResultStatus
{
	Completed = 1,
	PersonalBestLap = 2,
	PersonalBestRace = 4,
	GlobalRecord = 8
}

public class RaceResult
{
	public string TrackId { get; set; } = string.Empty;
	public float RaceTime { get; set; }
	public float BestLapTime { get; set; }
	public DateTime CompletedAt { get; set; }
	public string LocationId { get; set; } = string.Empty;
}

public class SessionStats
{
	public int RacesCompleted { get; set; }
	public int PersonalBests { get; set; }
	public float AverageRaceTime { get; set; }
	public float AverageLapTime { get; set; }
	public TimeSpan SessionDuration { get; set; }
}

public class RacingStats
{
	public int TotalRaces { get; set; }
	public int RacesAtLocation { get; set; }
	public int TracksRaced { get; set; }
	public int GlobalRanking { get; set; }
	public string FavoriteTrack { get; set; } = string.Empty;
	public int SessionRaces { get; set; }
}