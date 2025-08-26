using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// Result wrapper for explicit error handling
/// </summary>
public readonly struct Result<T>
{
	public T Value { get; }
	public string Error { get; }
	public bool IsSuccess { get; }
	
	private Result(T value)
	{
		Value = value;
		Error = string.Empty;
		IsSuccess = true;
	}
	
	private Result(string error)
	{
		Value = default(T);
		Error = error ?? string.Empty;
		IsSuccess = false;
	}
	
	public static Result<T> Success(T value) => new Result<T>(value);
	public static Result<T> Failure(string error) => new Result<T>(error);
}

/// <summary>
/// Unified data access service for BarBox ecosystem with multiple backend support
/// Supports REST API backend (production) and local file backend (development fallback)
/// Handles both global (cross-location) and local (machine-specific) data
/// </summary>
public partial class DataStore : AutoloadBase
{
	public static DataStore Instance { get; private set; }

	// Backend storage mode constants
	private const string STORAGE_MODE_REST = "rest";
	private const string STORAGE_MODE_LOCAL = "local";
	private const string STORAGE_MODE_AUTO = "auto";

	/// <summary>
	/// Global user data structure (cross-location)
	/// </summary>
	public partial class GlobalUserData
	{
		public string UserId { get; init; } = string.Empty;
		public int GlobalCredits { get; set; }
		public DateTime LastSyncAt { get; init; } = DateTime.UtcNow;
		
		// Game-specific properties added via partial class extensions in game directories
	}

	/// <summary>
	/// Local user data structure (machine-specific)
	/// </summary>
	public partial class LocalUserData
	{
		public string UserId { get; init; } = string.Empty;
		public string LocationId { get; set; } = string.Empty;
		public DateTime LastLoginTime { get; set; } = DateTime.UtcNow;
		
		// Game-specific properties added via partial class extensions in game directories
	}

	/// <summary>
	/// Machine-game data structure (machine-specific, game-specific shared resources)
	/// </summary>
	public class MachineGameData
	{
		public string LocationId { get; set; } = string.Empty;
		public string GameId { get; set; } = string.Empty;
		public int MachineCredits { get; set; }
		public Dictionary<string, string> GameState { get; set; } = new();
		public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
	}

	private IDataStoreBackend _backend;
	private string _locationId;
	private string _storageMode;
	private string _apiBaseUrl;

	protected override void OnServiceReady()
	{
		Instance = this;
		// Minimal setup only - actual initialization happens in OnServiceInitialize
	}

	protected override async void OnServiceInitialize()
	{
		// Initialize location and API settings
		_locationId = InitializeLocationId();
		_apiBaseUrl = GetApiBaseUrl();
		_storageMode = GetStorageMode();
		
		// Create appropriate backend based on storage mode
		_backend = await CreateBackendAsync();
		
		LogInfo($"DataStore initialized for location '{_locationId}' using {_backend.GetType().Name}");
	}

	/// <summary>
	/// Get global user data (cross-location)
	/// </summary>
	public async Task<Result<GlobalUserData>> GetGlobalDataAsync(string userId)
	{
		return await _backend.GetGlobalDataAsync(userId);
	}

	/// <summary>
	/// Update global user data (cross-location)
	/// </summary>
	public async Task<Result<bool>> SetGlobalDataAsync(string userId, GlobalUserData data)
	{
		return await _backend.SetGlobalDataAsync(userId, data);
	}

	/// <summary>
	/// Get local user data for this location
	/// </summary>
	public async Task<Result<LocalUserData>> GetLocalDataAsync(string userId)
	{
		return await _backend.GetLocalDataAsync(userId);
	}

	/// <summary>
	/// Update local user data for this location
	/// </summary>
	public async Task<Result<bool>> SetLocalDataAsync(string userId, LocalUserData data)
	{
		return await _backend.SetLocalDataAsync(userId, data);
	}

	/// <summary>
	/// Spend global credits (cross-location)
	/// </summary>
	public async Task<Result<bool>> SpendGlobalCreditsAsync(string userId, int amount, string reason = "")
	{
		return await _backend.SpendGlobalCreditsAsync(userId, amount, reason);
	}

	/// <summary>
	/// Add global credits (cross-location)
	/// </summary>
	public async Task<Result<bool>> AddGlobalCreditsAsync(string userId, int amount, string reason = "")
	{
		return await _backend.AddGlobalCreditsAsync(userId, amount, reason);
	}


	/// <summary>
	/// Sync user data - called when user logs out or system shuts down
	/// </summary>
	public async Task<bool> SyncUserDataAsync(string userId)
	{
		return await _backend.SyncUserDataAsync(userId);
	}

	/// <summary>
	/// Get machine-game data for this location and specific game
	/// </summary>
	public async Task<MachineGameData> GetMachineGameDataAsync(string gameId)
	{
		return await _backend.GetMachineGameDataAsync(gameId);
	}

	/// <summary>
	/// Update machine-game data for this location and specific game
	/// </summary>
	public async Task<bool> SetMachineGameDataAsync(string gameId, MachineGameData data)
	{
		return await _backend.SetMachineGameDataAsync(gameId, data);
	}

	/// <summary>
	/// Transfer credits from user's global account to machine credits for specific game
	/// </summary>
	public async Task<bool> TransferCreditsToMachineAsync(string userId, string gameId, int amount, string reason = "")
	{
		return await _backend.TransferCreditsToMachineAsync(userId, gameId, amount, reason);
	}

	/// <summary>
	/// Spend machine credits for specific game
	/// </summary>
	public async Task<bool> SpendMachineGameCreditsAsync(string gameId, int amount, string reason = "")
	{
		return await _backend.SpendMachineGameCreditsAsync(gameId, amount, reason);
	}

	/// <summary>
	/// Add machine credits for specific game
	/// </summary>
	public async Task<bool> AddMachineGameCreditsAsync(string gameId, int amount, string reason = "")
	{
		return await _backend.AddMachineGameCreditsAsync(gameId, amount, reason);
	}

	/// <summary>
	/// Get the current location ID for this instance
	/// </summary>
	public string GetCurrentLocationId() => _backend?.GetCurrentLocationId() ?? _locationId;

	/// <summary>
	/// Check if the data service is available
	/// </summary>
	public async Task<bool> IsServiceAvailableAsync()
	{
		return await _backend.IsServiceAvailableAsync();
	}

	// Private helper methods

	private string GetStorageMode()
	{
		return System.Environment.GetEnvironmentVariable("BARBOX_STORAGE_MODE") ?? STORAGE_MODE_AUTO;
	}

	private async Task<IDataStoreBackend> CreateBackendAsync()
	{
		IDataStoreBackend backend = null;
		
		switch (_storageMode.ToLowerInvariant())
		{
			case STORAGE_MODE_REST:
				backend = new RestApiBackend();
				break;
				
			case STORAGE_MODE_LOCAL:
				backend = new LocalFileBackend();
				break;
				
			case STORAGE_MODE_AUTO:
				// Try REST first, fallback to local if unavailable
				var restBackend = new RestApiBackend();
				await restBackend.InitializeAsync(_locationId, _apiBaseUrl);
				
				if (await restBackend.IsServiceAvailableAsync())
				{
					LogInfo($"REST API available, using RestApiBackend");
					backend = restBackend;
				}
				else
				{
					LogInfo($"REST API unavailable, falling back to LocalFileBackend");
					backend = new LocalFileBackend();
				}
				break;
				
			default:
				LogError($"Unknown storage mode '{_storageMode}', using auto mode");
				backend = new LocalFileBackend();
				break;
		}

		// Initialize the backend if not already initialized
		await backend.InitializeAsync(_locationId, _apiBaseUrl);

		return backend;
	}

	private string InitializeLocationId()
	{
		// For now, use machine name or environment variable
		// In production, this could be configured per deployment
		return System.Environment.GetEnvironmentVariable("BARBOX_LOCATION_ID") ?? 
		       System.Environment.MachineName.ToLowerInvariant().Replace("-", "_");
	}

	private string GetApiBaseUrl()
	{
		// Default to localhost for development, configurable via environment
		return System.Environment.GetEnvironmentVariable("BARBOX_API_URL") ?? "http://localhost:5000";
	}


	/// <summary>
	/// Get the singleton instance
	/// </summary>
	public static DataStore GetInstance()
	{
		return GetAutoload<DataStore>();
	}

	protected override void OnServiceDestroyed()
	{
		if (_backend is RestApiBackend restBackend)
		{
			restBackend.Dispose();
		}
		_backend = null;
		Instance = null;
	}
}

/// <summary>
/// Game data types are now defined in their respective game directories:
/// - MiningGame: _Scripts/Games/MiningGame/Data/
/// - RacingGame: _Scripts/Games/RacingGame/Data/ 
/// - CarromGame: _Scripts/Games/CarromGame/Data/
/// 
/// Each game extends GlobalUserData and LocalUserData via partial class extensions.
/// </summary>
