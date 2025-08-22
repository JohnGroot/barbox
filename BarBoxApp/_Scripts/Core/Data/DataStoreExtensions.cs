using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Extension methods for DataStore to simplify common game data operations
/// Provides consistent error handling and reduces boilerplate code
/// </summary>
public static class DataStoreExtensions
{
	// ============= SIMPLE GAME STATE OPERATIONS =============
	
	/// <summary>
	/// Load a single game value with automatic error handling
	/// </summary>
	/// <typeparam name="T">Type of the value to load</typeparam>
	/// <param name="store">DataStore instance</param>
	/// <param name="userId">User ID</param>
	/// <param name="key">Key to load</param>
	/// <param name="defaultValue">Default value if key doesn't exist or loading fails</param>
	/// <returns>The loaded value or default value</returns>
	public static async Task<T> GetGameValueAsync<T>(
		this DataStore store, 
		string userId, 
		string key, 
		T defaultValue = default)
	{
		if (store == null || string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(key))
			return defaultValue;
		
		var result = await store.GetLocalDataAsync(userId);
		if (!result.IsSuccess) 
		{
			GD.Print($"[DataStoreExtensions] Failed to load local data for user {userId}: {result.Error}");
			return defaultValue;
		}
		
		var json = result.Value.GameState.GetValueOrDefault(key);
		if (string.IsNullOrEmpty(json)) 
			return defaultValue;
		
		try
		{
			return JsonSerializer.Deserialize<T>(json);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[DataStoreExtensions] Failed to deserialize key '{key}': {ex.Message}");
			return defaultValue;
		}
	}
	
	/// <summary>
	/// Save a single game value with automatic error handling
	/// </summary>
	/// <typeparam name="T">Type of the value to save</typeparam>
	/// <param name="store">DataStore instance</param>
	/// <param name="userId">User ID</param>
	/// <param name="key">Key to save</param>
	/// <param name="value">Value to save</param>
	/// <returns>True if save was successful</returns>
	public static async Task<bool> SetGameValueAsync<T>(
		this DataStore store,
		string userId,
		string key,
		T value)
	{
		if (store == null || string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(key))
			return false;
		
		var result = await store.GetLocalDataAsync(userId);
		if (!result.IsSuccess) 
		{
			GD.PrintErr($"[DataStoreExtensions] Failed to load local data for user {userId}: {result.Error}");
			return false;
		}
		
		try
		{
			result.Value.GameState[key] = JsonSerializer.Serialize(value);
			var saveResult = await store.SetLocalDataAsync(userId, result.Value);
			if (!saveResult.IsSuccess)
			{
				GD.PrintErr($"[DataStoreExtensions] Failed to save local data for user {userId}: {saveResult.Error}");
				return false;
			}
			return true;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[DataStoreExtensions] Failed to serialize and save key '{key}': {ex.Message}");
			return false;
		}
	}
	
	// ============= BATCH OPERATIONS =============
	
	/// <summary>
	/// Load entire game state object with automatic key generation
	/// </summary>
	/// <typeparam name="T">Type of the game state object</typeparam>
	/// <param name="store">DataStore instance</param>
	/// <param name="gameId">Game identifier (e.g., "mining_game")</param>
	/// <param name="userId">User ID</param>
	/// <returns>The loaded game state or a new instance</returns>
	public static async Task<T> LoadGameStateAsync<T>(
		this DataStore store,
		string gameId,
		string userId) where T : new()
	{
		var key = $"{gameId}_state";
		return await store.GetGameValueAsync(userId, key, new T());
	}
	
	/// <summary>
	/// Save entire game state object with automatic key generation
	/// </summary>
	/// <typeparam name="T">Type of the game state object</typeparam>
	/// <param name="store">DataStore instance</param>
	/// <param name="gameId">Game identifier (e.g., "mining_game")</param>
	/// <param name="userId">User ID</param>
	/// <param name="gameState">Game state object to save</param>
	/// <returns>True if save was successful</returns>
	public static async Task<bool> SaveGameStateAsync<T>(
		this DataStore store,
		string gameId,
		string userId,
		T gameState)
	{
		var key = $"{gameId}_state";
		return await store.SetGameValueAsync(userId, key, gameState);
	}
	
	// ============= HIGH SCORES =============
	
	/// <summary>
	/// Save a high score if it's better than the current one
	/// </summary>
	/// <param name="store">DataStore instance</param>
	/// <param name="userId">User ID</param>
	/// <param name="scoreKey">Score key (e.g., "racing_track1_3laps")</param>
	/// <param name="newScore">New score to potentially save</param>
	/// <param name="higherIsBetter">True if higher scores are better, false if lower is better</param>
	/// <returns>True if a new high score was saved</returns>
	public static async Task<bool> SaveHighScoreAsync(
		this DataStore store,
		string userId,
		string scoreKey,
		int newScore,
		bool higherIsBetter = true)
	{
		if (store == null || string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(scoreKey))
			return false;
		
		var result = await store.GetGlobalDataAsync(userId);
		if (!result.IsSuccess) 
		{
			GD.PrintErr($"[DataStoreExtensions] Failed to load global data for user {userId}: {result.Error}");
			return false;
		}
		
		var globalData = result.Value;
		var currentScore = globalData.GlobalHighScores.GetValueOrDefault(scoreKey, 
			higherIsBetter ? int.MinValue : int.MaxValue);
		
		bool isNewHighScore = higherIsBetter ? 
			newScore > currentScore : 
			newScore < currentScore;
			
		if (isNewHighScore)
		{
			globalData.GlobalHighScores[scoreKey] = newScore;
			var saveResult = await store.SetGlobalDataAsync(userId, globalData);
			if (!saveResult.IsSuccess)
			{
				GD.PrintErr($"[DataStoreExtensions] Failed to save high score for user {userId}: {saveResult.Error}");
				return false;
			}
			
			GD.Print($"[DataStoreExtensions] New high score saved: {scoreKey} = {newScore} (was {currentScore})");
			return true;
		}
		
		return false;
	}
	
	/// <summary>
	/// Get a high score value
	/// </summary>
	/// <param name="store">DataStore instance</param>
	/// <param name="userId">User ID</param>
	/// <param name="scoreKey">Score key</param>
	/// <param name="defaultScore">Default score if not found</param>
	/// <returns>The high score value or default</returns>
	public static async Task<int> GetHighScoreAsync(
		this DataStore store,
		string userId,
		string scoreKey,
		int defaultScore = 0)
	{
		if (store == null || string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(scoreKey))
			return defaultScore;
		
		var result = await store.GetGlobalDataAsync(userId);
		if (!result.IsSuccess) 
			return defaultScore;
		
		return result.Value.GlobalHighScores.GetValueOrDefault(scoreKey, defaultScore);
	}
	
	// ============= CREDITS OPERATIONS =============
	
	/// <summary>
	/// Quick credit check without going through SessionManager
	/// </summary>
	/// <param name="store">DataStore instance</param>
	/// <param name="userId">User ID</param>
	/// <returns>Current credit amount or 0 if unavailable</returns>
	public static async Task<int> GetCreditsAsync(
		this DataStore store,
		string userId)
	{
		if (store == null || string.IsNullOrEmpty(userId))
			return 0;
		
		var result = await store.GetGlobalDataAsync(userId);
		return result.IsSuccess ? result.Value.GlobalCredits : 0;
	}
	
	/// <summary>
	/// Check if user has enough credits for a purchase
	/// </summary>
	/// <param name="store">DataStore instance</param>
	/// <param name="userId">User ID</param>
	/// <param name="requiredAmount">Required credit amount</param>
	/// <returns>True if user has enough credits</returns>
	public static async Task<bool> HasCreditsAsync(
		this DataStore store,
		string userId,
		int requiredAmount)
	{
		var credits = await store.GetCreditsAsync(userId);
		return credits >= requiredAmount;
	}
	
	// ============= COMPETITIVE GAME TRACKING =============
	
	/// <summary>
	/// Increment competitive games played counter for a specific game type
	/// </summary>
	/// <param name="store">DataStore instance</param>
	/// <param name="userId">User ID</param>
	/// <param name="gameType">Game type identifier (e.g., "carrom", "racing")</param>
	/// <returns>True if increment was successful</returns>
	public static async Task<bool> IncrementCompetitiveGamesAsync(
		this DataStore store,
		string userId,
		string gameType)
	{
		if (store == null || string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(gameType))
			return false;
		
		var result = await store.GetGlobalDataAsync(userId);
		if (!result.IsSuccess) 
			return false;
		
		var globalData = result.Value;
		globalData.IncrementCompetitiveGames(gameType);
		
		var saveResult = await store.SetGlobalDataAsync(userId, globalData);
		return saveResult.IsSuccess;
	}
	
	/// <summary>
	/// Get competitive games played count for a specific game type
	/// </summary>
	/// <param name="store">DataStore instance</param>
	/// <param name="userId">User ID</param>
	/// <param name="gameType">Game type identifier</param>
	/// <returns>Number of competitive games played</returns>
	public static async Task<int> GetCompetitiveGamesPlayedAsync(
		this DataStore store,
		string userId,
		string gameType)
	{
		if (store == null || string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(gameType))
			return 0;
		
		var result = await store.GetGlobalDataAsync(userId);
		if (!result.IsSuccess) 
			return 0;
		
		return result.Value.GetCompetitiveGamesPlayed(gameType);
	}
}