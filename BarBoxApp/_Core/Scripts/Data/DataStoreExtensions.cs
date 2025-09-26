using System;
using System.Threading.Tasks;
using Godot;
using BarBox.Games.MiningGame;

/// <summary>
/// Extension methods for DataStore to simplify common game data operations
/// Provides consistent error handling and reduces boilerplate code
/// </summary>
public static class DataStoreExtensions
{
	// ============= SIMPLE GAME STATE OPERATIONS =============
	
	/// <summary>
	/// Load mining game data with automatic error handling
	/// </summary>
	/// <param name="store">DataStore instance</param>
	/// <param name="userId">User ID</param>
	/// <returns>Mining local data or new instance</returns>
	public static async Task<MiningLocalData> GetMiningDataAsync(
		this DataStore store, 
		string userId)
	{
		if (store == null || string.IsNullOrEmpty(userId))
			return new MiningLocalData();
		
		var result = await store.GetLocalDataAsync(userId);
		if (!result.IsSuccess) 
		{
			GD.Print($"[DataStoreExtensions] Failed to load local data for user {userId}: {result.Error}");
			return new MiningLocalData();
		}
		
		return result.Value.Mining ?? new MiningLocalData();
	}

	/// <summary>
	/// Save mining game data with automatic error handling
	/// </summary>
	/// <param name="store">DataStore instance</param>
	/// <param name="userId">User ID</param>
	/// <param name="miningData">Mining data to save</param>
	/// <returns>True if save was successful</returns>
	public static async Task<bool> SetMiningDataAsync(
		this DataStore store,
		string userId,
		MiningLocalData miningData)
	{
		if (store == null || string.IsNullOrEmpty(userId) || miningData == null)
			return false;

		var result = await store.GetLocalDataAsync(userId);
		if (!result.IsSuccess)
		{
			GD.PrintErr($"[DataStoreExtensions] Failed to load local data for user {userId}: {result.Error}");
			return false;
		}

		try
		{
			result.Value.Mining = miningData;
			var saveResult = await store.SetLocalDataAsync(userId, result.Value);
			if (!saveResult.IsSuccess)
			{
				GD.PrintErr($"[DataStoreExtensions] Failed to save mining data for user {userId}: {saveResult.Error}");
				return false;
			}
			return true;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[DataStoreExtensions] Failed to save mining data: {ex.Message}");
			return false;
		}
	}

}