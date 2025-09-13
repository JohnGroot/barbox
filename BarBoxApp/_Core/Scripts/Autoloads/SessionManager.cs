using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// Simplified session management service replacing UserManager + CreditManager
/// Handles user authentication, sessions, and credit operations
/// </summary>
public partial class SessionManager : AutoloadBase
{
	[Signal] public delegate void UserLoggedInEventHandler(string userId);
	[Signal] public delegate void UserLoggedOutEventHandler(string userId);
	[Signal] public delegate void CreditsSpentEventHandler(string userId, int amount, string reason);
	[Signal] public delegate void CreditsEarnedEventHandler(string userId, int amount, string reason);

	public static SessionManager Instance { get; private set; }

	private DataStore _dataStore;
	private Dictionary<string, UserSession> _activeSessions = new();
	private Timer _idleTimer;
	private const float IDLE_CHECK_INTERVAL = 30.0f; // 30 seconds
	private const float IDLE_TIMEOUT = 600.0f; // 10 minutes

	public string CurrentLocationId => _dataStore?.GetCurrentLocationId() ?? "unknown";

	protected override void OnServiceReady()
	{
		Instance = this;
		// Minimal setup only - actual initialization happens in OnServiceInitialize
	}

	protected override void OnServiceInitialize()
	{
		// Now safe to access DataStore - it's been initialized by SceneManager
		_dataStore = DataStore.GetInstance();
		if (_dataStore == null)
		{
			LogError("DataStore not found - SessionManager requires DataStore");
			return;
		}

		SetupIdleTimer();
		
		LogInfo("SessionManager initialized with DataStore dependency");
	}

	/// <summary>
	/// Authenticate and log in a user
	/// </summary>
	public async Task<bool> LoginUserAsync(string userId, string pin = null)
	{
		if (string.IsNullOrEmpty(userId))
			return false;

		// Don't allow duplicate sessions
		if (_activeSessions.ContainsKey(userId))
			return false;

		try
		{
			// In development, skip PIN validation
			if (GameHost.IsDevelopmentContext())
			{
				return await CreateUserSession(userId);
			}

			// In production, validate PIN (simplified for prototype)
			if (string.IsNullOrEmpty(pin) || pin.Length > 5)
				return false;

			// For prototype: accept any PIN of reasonable length
			return await CreateUserSession(userId);
		}
		catch (Exception ex)
		{
			LogError($"Error logging in user {userId}: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Log out a user and sync their data
	/// </summary>
	public async Task<bool> LogoutUserAsync(string userId)
	{
		if (!_activeSessions.TryGetValue(userId, out var session))
			return false;

		try
		{
			// Sync user data before logout
			await _dataStore.SyncUserDataAsync(userId);
			
			// Update local data with final session info
			var localDataResult = await _dataStore.GetLocalDataAsync(userId);
			if (localDataResult.IsSuccess)
			{
				var localData = localDataResult.Value;
				localData.LastLoginTime = DateTime.UtcNow;
				var saveResult = await _dataStore.SetLocalDataAsync(userId, localData);
				if (!saveResult.IsSuccess)
					LogWarning($"Failed to save logout time for user {userId}: {saveResult.Error}");
			}
			else
			{
				LogWarning($"Failed to get local data for logout of user {userId}: {localDataResult.Error}");
			}

			_activeSessions.Remove(userId);
			EmitSignal(SignalName.UserLoggedOut, userId);
			
			LogInfo($"User {userId} logged out");
			return true;
		}
		catch (Exception ex)
		{
			LogError($"Error logging out user {userId}: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Get active user session
	/// </summary>
	public UserSession GetUserSession(string userId)
	{
		return _activeSessions.GetValueOrDefault(userId);
	}

	/// <summary>
	/// Get first active user (for single-user scenarios)
	/// </summary>
	public UserSession GetCurrentUserSession()
	{
		foreach (var session in _activeSessions.Values)
			return session;
		return null;
	}

	/// <summary>
	/// Check if user is logged in
	/// </summary>
	public bool IsUserLoggedIn(string userId)
	{
		return _activeSessions.ContainsKey(userId);
	}

	/// <summary>
	/// Get all active user IDs
	/// </summary>
	public string[] GetActiveUserIds()
	{
		var userIds = new string[_activeSessions.Count];
		_activeSessions.Keys.CopyTo(userIds, 0);
		return userIds;
	}

	/// <summary>
	/// Check and spend global credits with UI confirmation
	/// </summary>
	public async Task<bool> CheckAndSpendGlobalCreditsAsync(string userId, int amount, string reason)
	{
		if (!IsUserLoggedIn(userId))
		{
			LogWarning($"User {userId} not logged in for credit spending");
			return false;
		}

		// In development mode, bypass credit costs
		if (GameHost.ShouldBypassCredits())
		{
			LogInfo($"Development mode - bypassing credit cost of {amount} for {reason}");
			EmitSignal(SignalName.CreditsSpent, userId, amount, reason);
			return true;
		}

		// Check if user has enough credits
		var globalDataResult = await _dataStore.GetGlobalDataAsync(userId);
		if (!globalDataResult.IsSuccess)
		{
			LogError($"Failed to get global data for user {userId}: {globalDataResult.Error}");
			return false;
		}

		var globalData = globalDataResult.Value;
		if (globalData.GlobalCredits < amount)
		{
			LogInfo($"User {userId} has insufficient global credits ({globalData.GlobalCredits}/{amount})");
			// TODO: Show buy credits dialog
			return false;
		}

		// Show confirmation dialog (simplified for prototype)
		bool confirmed = await ShowCreditConfirmationAsync(userId, amount, reason, globalData.GlobalCredits);
		if (!confirmed)
		{
			LogInfo($"User {userId} cancelled credit spending for {reason}");
			return false;
		}

		// Spend the credits
		var spendResult = await _dataStore.SpendGlobalCreditsAsync(userId, amount, reason);
		if (spendResult.IsSuccess)
		{
			EmitSignal(SignalName.CreditsSpent, userId, amount, reason);
		}

		return spendResult.IsSuccess;
	}


	/// <summary>
	/// Add credits to user account
	/// </summary>
	public async Task<bool> AddGlobalCreditsAsync(string userId, int amount, string reason)
	{
		var addResult = await _dataStore.AddGlobalCreditsAsync(userId, amount, reason);
		if (addResult.IsSuccess)
		{
			EmitSignal(SignalName.CreditsEarned, userId, amount, reason);
		}

		return addResult.IsSuccess;
	}

	/// <summary>
	/// Reset idle timer for user activity
	/// </summary>
	public void ResetUserIdleTimer(string userId)
	{
		if (_activeSessions.TryGetValue(userId, out var session))
		{
			session.LastActivity = DateTime.UtcNow;
		}
	}

	/// <summary>
	/// Reset idle timer for all active users
	/// </summary>
	public void ResetAllIdleTimers()
	{
		var now = DateTime.UtcNow;
		foreach (var session in _activeSessions.Values)
		{
			session.LastActivity = now;
		}
	}

	// Private helper methods

	private async Task<bool> CreateUserSession(string userId)
	{
		// Load user data
		var globalDataResult = await _dataStore.GetGlobalDataAsync(userId);
		var localDataResult = await _dataStore.GetLocalDataAsync(userId);

		if (!globalDataResult.IsSuccess)
		{
			LogError($"Failed to get global data for user {userId}: {globalDataResult.Error}");
			return false;
		}

		if (!localDataResult.IsSuccess)
		{
			LogError($"Failed to get local data for user {userId}: {localDataResult.Error}");
			return false;
		}

		var globalData = globalDataResult.Value;
		var localData = localDataResult.Value;

		// Create session
		var session = new UserSession
		{
			UserId = userId,
			LocationId = CurrentLocationId,
			LoginTime = DateTime.UtcNow,
			LastActivity = DateTime.UtcNow,
			GlobalData = globalData,
			LocalData = localData
		};

		_activeSessions[userId] = session;
		
		// Update login time in local data
		localData.LastLoginTime = DateTime.UtcNow;
		var saveResult = await _dataStore.SetLocalDataAsync(userId, localData);
		if (!saveResult.IsSuccess)
			LogWarning($"Failed to save login time for user {userId}: {saveResult.Error}");

		EmitSignal(SignalName.UserLoggedIn, userId);
		LogInfo($"User {userId} logged in at location {CurrentLocationId}");
		
		return true;
	}

	private void SetupIdleTimer()
	{
		_idleTimer = new Timer();
		_idleTimer.WaitTime = IDLE_CHECK_INTERVAL;
		_idleTimer.Timeout += OnIdleTimerTimeout;
		AddChild(_idleTimer);
		_idleTimer.Start();
	}

	private async void OnIdleTimerTimeout()
	{
		var now = DateTime.UtcNow;
		var usersToLogout = new List<string>();

		// Find idle users
		foreach (var kvp in _activeSessions)
		{
			var userId = kvp.Key;
			var session = kvp.Value;
			var idleTime = (now - session.LastActivity).TotalSeconds;

			if (idleTime >= IDLE_TIMEOUT)
			{
				usersToLogout.Add(userId);
			}
		}

		// Logout idle users
		foreach (var userId in usersToLogout)
		{
			LogInfo($"Auto-logout user {userId} due to idle timeout");
			await LogoutUserAsync(userId);
		}
	}

	private Task<bool> ShowCreditConfirmationAsync(string userId, int amount, string reason, int currentCredits)
	{
		// For prototype: simplified confirmation
		// In full implementation, this would show a proper dialog
		LogInfo($"Credit confirmation: Spend {amount} credits for {reason}? (Balance: {currentCredits})");
		
		// Auto-confirm in development mode
		if (GameHost.IsDevelopmentContext())
			return Task.FromResult(true);

		// TODO: Implement proper UI dialog
		// For now, always confirm
		return Task.FromResult(true);
	}

	/// <summary>
	/// Get the singleton instance
	/// </summary>
	public static SessionManager GetInstance()
	{
		return GetAutoload<SessionManager>();
	}

	protected override void OnServiceDestroyed()
	{
		// Logout all users before shutdown
		var userIds = GetActiveUserIds();
		foreach (var userId in userIds)
		{
			_ = LogoutUserAsync(userId); // Fire and forget for cleanup
		}

		_idleTimer?.Stop();
		_idleTimer?.QueueFree();
		
		Instance = null;
	}
}

/// <summary>
/// User session data structure
/// </summary>
public class UserSession
{
	public string UserId { get; set; } = string.Empty;
	public string LocationId { get; set; } = string.Empty;
	public DateTime LoginTime { get; set; }
	public DateTime LastActivity { get; set; }
	public DataStore.GlobalUserData GlobalData { get; set; }
	public DataStore.LocalUserData LocalData { get; set; }

	public TimeSpan SessionDuration => DateTime.UtcNow - LoginTime;
	public TimeSpan IdleTime => DateTime.UtcNow - LastActivity;
}