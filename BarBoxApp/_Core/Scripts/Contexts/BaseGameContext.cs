using Godot;
using System;
using System.Threading.Tasks;

/// <summary>
/// Base class for all game contexts providing shared functionality
/// Handles common patterns for data access, session management, and context loading
/// </summary>
public abstract class BaseGameContext
{
	protected readonly DataStore _dataStore;
	protected readonly SessionManager _sessionManager;
	protected readonly string _userId;
	protected readonly string _locationId;

	// Common properties
	public string UserId => _userId;
	public string LocationId => _locationId;
	public bool IsDataLoaded { get; private set; }

	protected BaseGameContext(DataStore dataStore, SessionManager sessionManager, string userId, string locationId)
	{
		_dataStore = dataStore ?? throw new ArgumentNullException(nameof(dataStore));
		_sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
		_userId = userId ?? throw new ArgumentException("User ID cannot be null", nameof(userId));
		_locationId = locationId ?? throw new ArgumentException("Location ID cannot be null", nameof(locationId));
	}

	/// <summary>
	/// Initialize the game context with data loading
	/// </summary>
	protected async Task<bool> InitializeAsync()
	{
		try
		{
			await LoadGlobalDataAsync();
			await LoadLocalDataAsync();
			IsDataLoaded = true;
			OnDataLoaded();
			return true;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Failed to initialize {GetType().Name}: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Load global (cross-location) data - implement in derived classes
	/// </summary>
	protected abstract Task LoadGlobalDataAsync();

	/// <summary>
	/// Load local (machine-specific) data - implement in derived classes
	/// </summary>
	protected abstract Task LoadLocalDataAsync();

	/// <summary>
	/// Save global data - implement in derived classes
	/// </summary>
	protected abstract Task<bool> SaveGlobalDataAsync();

	/// <summary>
	/// Save local data - implement in derived classes
	/// </summary>
	protected abstract Task<bool> SaveLocalDataAsync();

	/// <summary>
	/// Called after data is successfully loaded
	/// </summary>
	protected virtual void OnDataLoaded()
	{
		// Override in derived classes for post-load initialization
	}

	/// <summary>
	/// Get current user session
	/// </summary>
	protected UserSession GetCurrentSession()
	{
		return _sessionManager.GetUserSession(_userId);
	}

	/// <summary>
	/// Check if user has enough global credits
	/// </summary>
	protected async Task<bool> HasGlobalCreditsAsync(int amount)
	{
		var globalDataResult = await _dataStore.GetGlobalDataAsync(_userId);
		if (!globalDataResult.IsSuccess)
			return false;
		return globalDataResult.Value.GlobalCredits >= amount;
	}

	/// <summary>
	/// Spend global credits through SessionManager
	/// </summary>
	protected async Task<bool> SpendGlobalCreditsAsync(int amount, string reason)
	{
		return await _sessionManager.CheckAndSpendGlobalCreditsAsync(_userId, amount, reason);
	}

	/// <summary>
	/// Add global credits through SessionManager
	/// </summary>
	protected async Task<bool> AddGlobalCreditsAsync(int amount, string reason)
	{
		return await _sessionManager.AddGlobalCreditsAsync(_userId, amount, reason);
	}

	/// <summary>
	/// Reset user idle timer
	/// </summary>
	protected void ResetIdleTimer()
	{
		_sessionManager.ResetUserIdleTimer(_userId);
	}

	/// <summary>
	/// Helper method to safely parse string to int
	/// </summary>
	protected static int SafeParseInt(string value, int defaultValue = 0)
	{
		return int.TryParse(value, out var result) ? result : defaultValue;
	}

	/// <summary>
	/// Helper method to safely parse string to float
	/// </summary>
	protected static float SafeParseFloat(string value, float defaultValue = 0f)
	{
		return float.TryParse(value, out var result) ? result : defaultValue;
	}

	/// <summary>
	/// Helper method to safely parse binary DateTime
	/// </summary>
	protected static DateTime SafeParseBinaryDateTime(string value, DateTime defaultValue = default)
	{
		if (long.TryParse(value, out var binary))
		{
			try
			{
				return DateTime.FromBinary(binary);
			}
			catch
			{
				return defaultValue == default ? DateTime.UtcNow : defaultValue;
			}
		}
		return defaultValue == default ? DateTime.UtcNow : defaultValue;
	}

	/// <summary>
	/// Helper method to convert DateTime to binary string
	/// </summary>
	protected static string DateTimeToBinaryString(DateTime dateTime)
	{
		return dateTime.ToBinary().ToString();
	}

	/// <summary>
	/// Log context-specific information
	/// </summary>
	protected void LogInfo(string message)
	{
		GD.Print($"[{GetType().Name}] {message}");
	}

	/// <summary>
	/// Log context-specific warnings
	/// </summary>
	protected void LogWarning(string message)
	{
		GD.Print($"[{GetType().Name}] WARNING: {message}");
	}

	/// <summary>
	/// Log context-specific errors
	/// </summary>
	protected void LogError(string message)
	{
		GD.PrintErr($"[{GetType().Name}] ERROR: {message}");
	}

}