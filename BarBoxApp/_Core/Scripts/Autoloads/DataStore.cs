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
		public string PhoneNumber { get; init; } = string.Empty; // Primary identifier (10 digits)
		public string UserName { get; set; } = string.Empty; // Display name (max 7 chars)
		public string CreatedAtLocation { get; init; } = string.Empty; // Location where account was created
		public int GlobalCredits { get; set; }
		public DateTime LastSyncAt { get; init; } = DateTime.UtcNow;
		public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

		// Game-specific properties added via partial class extensions in game directories
	}

	/// <summary>
	/// Local user data structure (machine-specific)
	/// </summary>
	public partial class LocalUserData
	{
		public string PhoneNumber { get; init; } = string.Empty;
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
	private bool _isInitializing = false;

	protected override void OnServiceReady()
	{
		Instance = this;
		// Minimal setup only - actual initialization happens in OnServiceInitialize
	}

	protected override void OnServiceInitialize()
	{
		// Initialize location and API settings synchronously
		_locationId = InitializeLocationId();
		_apiBaseUrl = GetApiBaseUrl();
		_storageMode = GetStorageMode();
		
		// Create placeholder local backend for immediate availability
		_backend = new LocalFileBackend();
		
		// Defer async backend initialization to prevent race conditions
		_isInitializing = true;
		CallDeferred(MethodName.CompleteAsyncInitialization);
	}
	
	/// <summary>
	/// Complete backend initialization asynchronously after service is marked ready
	/// </summary>
	private async void CompleteAsyncInitialization()
	{
		try
		{
			// Check if initialization was cancelled
			if (!_isInitializing) return;
			
			var backend = await CreateBackendAsync();
			
			// Check again after await in case of cancellation
			if (!_isInitializing) return;
			
			_backend = backend; // Atomic replacement
			LogInfo($"DataStore initialized for location '{_locationId}' using {_backend.GetType().Name}");
		}
		catch (System.Exception ex)
		{
			LogError($"Async backend initialization failed: {ex.Message}");
			// Keep local backend as fallback
		}
		finally
		{
			_isInitializing = false;
		}
	}

	/// <summary>
	/// Get global user data (cross-location)
	/// </summary>
	public async Task<Result<GlobalUserData>> GetGlobalDataAsync(string phoneNumber)
	{
		return await _backend.GetGlobalDataAsync(phoneNumber);
	}

	/// <summary>
	/// Update global user data (cross-location)
	/// </summary>
	public async Task<Result<bool>> SetGlobalDataAsync(string phoneNumber, GlobalUserData data)
	{
		return await _backend.SetGlobalDataAsync(phoneNumber, data);
	}

	/// <summary>
	/// Get local user data for this location
	/// </summary>
	public async Task<Result<LocalUserData>> GetLocalDataAsync(string phoneNumber)
	{
		return await _backend.GetLocalDataAsync(phoneNumber);
	}

	/// <summary>
	/// Update local user data for this location
	/// </summary>
	public async Task<Result<bool>> SetLocalDataAsync(string phoneNumber, LocalUserData data)
	{
		return await _backend.SetLocalDataAsync(phoneNumber, data);
	}

	/// <summary>
	/// Spend global credits (cross-location)
	/// </summary>
	public async Task<Result<bool>> SpendGlobalCreditsAsync(string phoneNumber, int amount, string reason = "")
	{
		return await _backend.SpendGlobalCreditsAsync(phoneNumber, amount, reason);
	}

	/// <summary>
	/// Add global credits (cross-location)
	/// </summary>
	public async Task<Result<bool>> AddGlobalCreditsAsync(string phoneNumber, int amount, string reason = "")
	{
		return await _backend.AddGlobalCreditsAsync(phoneNumber, amount, reason);
	}


	/// <summary>
	/// Sync user data - called when user logs out or system shuts down
	/// </summary>
	public async Task<bool> SyncUserDataAsync(string phoneNumber)
	{
		return await _backend.SyncUserDataAsync(phoneNumber);
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
	public async Task<bool> TransferCreditsToMachineAsync(string phoneNumber, string gameId, int amount, string reason = "")
	{
		return await _backend.TransferCreditsToMachineAsync(phoneNumber, gameId, amount, reason);
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

	/// <summary>
	/// Check if a phone number is already registered
	/// </summary>
	public async Task<Result<bool>> IsPhoneNumberRegisteredAsync(string phoneNumber)
	{
		return await _backend.IsPhoneNumberRegisteredAsync(phoneNumber);
	}

	/// <summary>
	/// Check if a username is already taken
	/// </summary>
	public async Task<Result<bool>> IsUsernameTakenAsync(string username)
	{
		return await _backend.IsUsernameTakenAsync(username);
	}

	/// <summary>
	/// Create a new user account with phone number authentication
	/// </summary>
	public async Task<Result<GlobalUserData>> CreateUserAccountAsync(string phoneNumber, string pin, string username, string locationId)
	{
		return await _backend.CreateUserAccountAsync(phoneNumber, pin, username, locationId);
	}

	/// <summary>
	/// Authenticate user by phone number and PIN
	/// </summary>
	public async Task<Result<GlobalUserData>> AuthenticateByPhoneAsync(string phoneNumber, string pin)
	{
		return await _backend.AuthenticateByPhoneAsync(phoneNumber, pin);
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
		// Cancel any pending async initialization
		_isInitializing = false;
		
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
