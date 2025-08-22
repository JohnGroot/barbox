using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Type-safe context for CarromsGame data access
/// Handles global credits vs local "table credits" distinction
/// </summary>
public class CarromsGameContext : BaseGameContext
{
	// Machine data (shared across users on this machine)
	public int MachineCredits { get; private set; } // Credits currently available on this machine for Carroms
	
	// Local data (user-specific at this location)
	public int GamesPlayedAtLocation { get; private set; }
	public DateTime LastPlayedAt { get; private set; } = DateTime.UtcNow;

	// Global data (cross-location)
	public int GlobalCredits { get; private set; }
	public int GlobalWins { get; private set; }
	public int GlobalLosses { get; private set; }
	public int GlobalGamesPlayed { get; private set; }
	public int BestWinStreak { get; private set; }

	// Current session data
	public int CurrentWinStreak { get; private set; }
	public int SessionWins { get; private set; }
	public int SessionLosses { get; private set; }

	private CarromsGameContext(DataStore dataStore, SessionManager sessionManager, string userId, string locationId)
		: base(dataStore, sessionManager, userId, locationId)
	{
	}

	/// <summary>
	/// Load carroms game context for the specified user
	/// </summary>
	public static async Task<CarromsGameContext> LoadAsync(string userId)
	{
		var dataStore = DataStore.GetInstance();
		var sessionManager = SessionManager.GetInstance();
		
		if (dataStore == null || sessionManager == null)
			throw new InvalidOperationException("Required services not available");

		var locationId = sessionManager.CurrentLocationId;
		var context = new CarromsGameContext(dataStore, sessionManager, userId, locationId);

		bool success = await context.InitializeAsync();
		if (!success)
			throw new InvalidOperationException("Failed to initialize CarromsGameContext");

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

		// Load global stats from high scores dictionary
		GlobalWins = globalData.GlobalHighScores.GetValueOrDefault("carroms_wins", 0);
		GlobalLosses = globalData.GlobalHighScores.GetValueOrDefault("carroms_losses", 0);
		GlobalGamesPlayed = globalData.GlobalHighScores.GetValueOrDefault("carroms_games_played", 0);
		BestWinStreak = globalData.GlobalHighScores.GetValueOrDefault("carroms_best_win_streak", 0);
	}

	/// <summary>
	/// Load local (user-specific) and machine (shared) data
	/// </summary>
	protected override async Task LoadLocalDataAsync()
	{
		// Load user-specific local data
		var localDataResult = await _dataStore.GetLocalDataAsync(_userId);
		if (!localDataResult.IsSuccess)
			throw new InvalidOperationException($"Failed to load local data: {localDataResult.Error}");

		var localData = localDataResult.Value;
		GamesPlayedAtLocation = SafeParseInt(localData.GameState.GetValueOrDefault("carroms_games_played_location", "0"));
		LastPlayedAt = SafeParseBinaryDateTime(localData.GameState.GetValueOrDefault("carroms_last_played", ""));
		SessionWins = SafeParseInt(localData.GameState.GetValueOrDefault("carroms_session_wins", "0"));
		SessionLosses = SafeParseInt(localData.GameState.GetValueOrDefault("carroms_session_losses", "0"));
		CurrentWinStreak = SafeParseInt(localData.GameState.GetValueOrDefault("carroms_current_win_streak", "0"));
		
		// Load machine-specific data (shared across users)
		var machineData = await _dataStore.GetMachineGameDataAsync("carroms_game");
		MachineCredits = machineData.MachineCredits;
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

		// Update global stats in high scores dictionary
		globalData.GlobalHighScores["carroms_wins"] = GlobalWins;
		globalData.GlobalHighScores["carroms_losses"] = GlobalLosses;
		globalData.GlobalHighScores["carroms_games_played"] = GlobalGamesPlayed;
		globalData.GlobalHighScores["carroms_best_win_streak"] = BestWinStreak;

		var saveResult = await _dataStore.SetGlobalDataAsync(_userId, globalData);
		return saveResult.IsSuccess;
	}

	/// <summary>
	/// Save local (user-specific) and machine (shared) data
	/// </summary>
	protected override async Task<bool> SaveLocalDataAsync()
	{
		// Save user-specific local data
		var localDataResult = await _dataStore.GetLocalDataAsync(_userId);
		if (!localDataResult.IsSuccess)
			return false;

		var localData = localDataResult.Value;
		localData.GameState["carroms_games_played_location"] = GamesPlayedAtLocation.ToString();
		localData.GameState["carroms_last_played"] = DateTimeToBinaryString(LastPlayedAt);
		localData.GameState["carroms_session_wins"] = SessionWins.ToString();
		localData.GameState["carroms_session_losses"] = SessionLosses.ToString();
		localData.GameState["carroms_current_win_streak"] = CurrentWinStreak.ToString();
		
		var localSaveResult = await _dataStore.SetLocalDataAsync(_userId, localData);
		bool localSaved = localSaveResult.IsSuccess;
		
		// Save machine-specific data (shared across users)
		var machineData = await _dataStore.GetMachineGameDataAsync("carroms_game");
		machineData.MachineCredits = MachineCredits;
		bool machineSaved = await _dataStore.SetMachineGameDataAsync("carroms_game", machineData);
		
		return localSaved && machineSaved;
	}

	/// <summary>
	/// Deposit credits from user's global account to machine for competitive play
	/// </summary>
	public async Task<bool> DepositMachineCreditsAsync(int amount)
	{
		// Transfer from user's global credits to machine credits
		bool transferred = await _dataStore.TransferCreditsToMachineAsync(_userId, "carroms_game", amount, "Competitive play deposit");
		if (transferred)
		{
			MachineCredits += amount;
			GlobalCredits -= amount; // Update cached value
			LogInfo($"Deposited {amount} credits to machine (Machine: {MachineCredits}, User Global: {GlobalCredits})");
			return true;
		}

		return false;
	}

	/// <summary>
	/// Note: Machine credits cannot be withdrawn back to individual users
	/// This is by design - once credits are on the machine, they stay there for any user to use
	/// This method is kept for compatibility but will always return false
	/// </summary>
	[System.Obsolete("Machine credits are shared and cannot be withdrawn to individual users")]
	public async Task<bool> WithdrawTableCreditsAsync(int amount)
	{
		LogWarning("Attempted to withdraw machine credits - this is not supported in the new architecture");
		await Task.CompletedTask; // Suppress async warning
		return false;
	}

	/// <summary>
	/// Start a competitive game by spending machine credits
	/// </summary>
	public async Task<bool> StartCompetitiveGameAsync(int creditCost)
	{
		if (MachineCredits < creditCost)
		{
			LogWarning($"Insufficient machine credits for competitive game ({MachineCredits}/{creditCost})");
			return false;
		}

		// Spend machine credits for the game
		bool spent = await _dataStore.SpendMachineGameCreditsAsync("carroms_game", creditCost, "Competitive Carroms game");
		if (spent)
		{
			MachineCredits -= creditCost; // Update cached value
			LogInfo($"Started competitive game for {creditCost} machine credits");
			return true;
		}

		return false;
	}

	/// <summary>
	/// Record a game result (win/loss)
	/// </summary>
	public async Task<bool> RecordGameResultAsync(bool won, int creditsAwarded = 0)
	{
		// Update session stats
		if (won)
		{
			SessionWins++;
			CurrentWinStreak++;
		}
		else
		{
			SessionLosses++;
			CurrentWinStreak = 0;
		}

		// Update global stats
		GlobalGamesPlayed++;
		GamesPlayedAtLocation++;
		LastPlayedAt = DateTime.UtcNow;

		if (won)
		{
			GlobalWins++;
			if (CurrentWinStreak > BestWinStreak)
				BestWinStreak = CurrentWinStreak;

			// Award credits to machine if any
			if (creditsAwarded > 0)
			{
				await _dataStore.AddMachineGameCreditsAsync("carroms_game", creditsAwarded, "Game win reward");
				MachineCredits += creditsAwarded; // Update cached value
				LogInfo($"Won game! Awarded {creditsAwarded} machine credits");
			}
		}
		else
		{
			GlobalLosses++;
		}

		// Reset idle timer
		ResetIdleTimer();

		// Save both local and global data
		await SaveGlobalDataAsync();
		await SaveLocalDataAsync();

		LogInfo($"Game result recorded - Won: {won}, Win Streak: {CurrentWinStreak}, Session: {SessionWins}W/{SessionLosses}L");
		return true;
	}

	/// <summary>
	/// Get win rate percentage
	/// </summary>
	public double GetWinRate()
	{
		if (GlobalGamesPlayed == 0) return 0.0;
		return (double)GlobalWins / GlobalGamesPlayed * 100.0;
	}

	/// <summary>
	/// Get session win rate percentage
	/// </summary>
	public double GetSessionWinRate()
	{
		var sessionGames = SessionWins + SessionLosses;
		if (sessionGames == 0) return 0.0;
		return (double)SessionWins / sessionGames * 100.0;
	}

	/// <summary>
	/// Check if machine has enough credits for a competitive game
	/// </summary>
	public bool CanAffordCompetitiveGame(int creditCost)
	{
		return MachineCredits >= creditCost;
	}

	/// <summary>
	/// Get recommendation for machine credit deposit
	/// </summary>
	public int GetRecommendedDeposit(int gameCreditsNeeded)
	{
		var shortfall = Math.Max(0, gameCreditsNeeded - MachineCredits);
		var recommendedBuffer = 50; // Extra credits for multiple games
		return Math.Min(shortfall + recommendedBuffer, GlobalCredits);
	}
}