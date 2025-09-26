using System.Threading.Tasks;

/// <summary>
/// Interface for DataStore backend implementations
/// Defines all data access operations that backends must support
/// </summary>
public interface IDataStoreBackend
{
	/// <summary>
	/// Initialize the backend with location and configuration
	/// </summary>
	Task<bool> InitializeAsync(string locationId, string apiBaseUrl = null);

	/// <summary>
	/// Check if the backend service is available
	/// </summary>
	Task<bool> IsServiceAvailableAsync();

	/// <summary>
	/// Get global user data (cross-location)
	/// </summary>
	Task<Result<DataStore.GlobalUserData>> GetGlobalDataAsync(string phoneNumber);

	/// <summary>
	/// Update global user data (cross-location)
	/// </summary>
	Task<Result<bool>> SetGlobalDataAsync(string phoneNumber, DataStore.GlobalUserData data);

	/// <summary>
	/// Get local user data for this location
	/// </summary>
	Task<Result<DataStore.LocalUserData>> GetLocalDataAsync(string phoneNumber);

	/// <summary>
	/// Update local user data for this location
	/// </summary>
	Task<Result<bool>> SetLocalDataAsync(string phoneNumber, DataStore.LocalUserData data);

	/// <summary>
	/// Get machine-game data for this location and specific game
	/// </summary>
	Task<DataStore.MachineGameData> GetMachineGameDataAsync(string gameId);

	/// <summary>
	/// Update machine-game data for this location and specific game
	/// </summary>
	Task<bool> SetMachineGameDataAsync(string gameId, DataStore.MachineGameData data);

	/// <summary>
	/// Spend global credits (cross-location)
	/// </summary>
	Task<Result<bool>> SpendGlobalCreditsAsync(string phoneNumber, int amount, string reason = "");

	/// <summary>
	/// Add global credits (cross-location)
	/// </summary>
	Task<Result<bool>> AddGlobalCreditsAsync(string phoneNumber, int amount, string reason = "");

	/// <summary>
	/// Transfer credits from user's global account to machine credits for specific game
	/// </summary>
	Task<bool> TransferCreditsToMachineAsync(string phoneNumber, string gameId, int amount, string reason = "");

	/// <summary>
	/// Spend machine credits for specific game
	/// </summary>
	Task<bool> SpendMachineGameCreditsAsync(string gameId, int amount, string reason = "");

	/// <summary>
	/// Add machine credits for specific game
	/// </summary>
	Task<bool> AddMachineGameCreditsAsync(string gameId, int amount, string reason = "");

	/// <summary>
	/// Sync user data - called when user logs out or system shuts down
	/// </summary>
	Task<bool> SyncUserDataAsync(string phoneNumber);

	/// <summary>
	/// Get the current location ID for this backend instance
	/// </summary>
	string GetCurrentLocationId();

	/// <summary>
	/// Check if a phone number is already registered
	/// </summary>
	Task<Result<bool>> IsPhoneNumberRegisteredAsync(string phoneNumber);

	/// <summary>
	/// Check if a username is already taken
	/// </summary>
	Task<Result<bool>> IsUsernameTakenAsync(string username);

	/// <summary>
	/// Create a new user account with phone number authentication
	/// </summary>
	Task<Result<DataStore.GlobalUserData>> CreateUserAccountAsync(string phoneNumber, string pin, string username, string locationId);

	/// <summary>
	/// Authenticate user by phone number and PIN
	/// </summary>
	Task<Result<DataStore.GlobalUserData>> AuthenticateByPhoneAsync(string phoneNumber, string pin);

	// ============= GENERIC GAME QUERY MECHANISM =============

	/// <summary>
	/// Execute a game-specific query operation
	/// This provides a generic hook for games to implement their own cross-user queries
	/// without Core systems needing to know about game-specific types
	/// </summary>
	/// <typeparam name="TResult">The expected result type</typeparam>
	/// <param name="gameId">Game identifier (e.g., "racing", "mining")</param>
	/// <param name="queryType">Type of query (e.g., "leaderboard", "best_lap")</param>
	/// <param name="parameters">Query parameters as key-value pairs</param>
	/// <returns>Result containing the query response or error</returns>
	Task<Result<TResult>> ExecuteGameQueryAsync<TResult>(
		string gameId,
		string queryType,
		System.Collections.Generic.Dictionary<string, object> parameters);
}