using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// Multi-user session management service with Phone/Username/PIN authentication
///
/// ARCHITECTURE:
/// - Supports multiple concurrent user sessions (multi-user games like Carrom 2v2)
/// - Phone number is primary identifier (10 digits)
/// - Username is display name (max 7 characters, public)
/// - 4-digit PIN for authentication
///
/// PRIMARY USER CONCEPT:
/// - Primary user = first logged-in user (for UI display and single-user convenience)
/// - Use GetPrimaryUserSession() for:
///   * TopMenuBar/UI display (showing "a" user)
///   * Single-user game contexts (Racing, Mining)
///   * Auto-populate convenience (first slot in multiplayer setup)
///   * Fallback scenarios (GameHost.GetPlayerSession)
///
/// MULTI-USER PATTERNS:
/// - Multi-user games MUST use GetUserSession(phoneNumber) for explicit targeting
/// - Example: CarromPlayerSetupMenu tracks users per slot explicitly
/// - User-specific operations (BuyCredits, logout) require explicit phone number
///
/// API USAGE:
/// - GetPrimaryUserSession() → Primary user (UI/single-user contexts)
/// - GetUserSession(phoneNumber) → Specific user (multi-user contexts)
/// - LoginUserByPhoneAsync(phone, pin) → Authenticate user
/// - LogoutUserAsync(phoneNumber) → Logout specific user
/// </summary>
public partial class SessionManager : AutoloadBase
{
	[Signal] public delegate void UserLoggedInEventHandler(string phoneNumber);
	[Signal] public delegate void UserLoggedOutEventHandler(string phoneNumber);
	[Signal] public delegate void CreditsSpentEventHandler(string phoneNumber, int amount, string reason);
	[Signal] public delegate void CreditsEarnedEventHandler(string phoneNumber, int amount, string reason);

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
	/// Authenticate and log in a user by phone number and PIN
	/// </summary>
	public async Task<bool> LoginUserByPhoneAsync(string phoneNumber, string pin)
	{
		if (string.IsNullOrEmpty(phoneNumber))
			return false;

		// Clean phone number for consistency
		var cleanedPhone = InputValidator.CleanPhoneNumber(phoneNumber);
		if (!InputValidator.IsValidPhoneNumber(cleanedPhone))
			return false;

		// Clean PIN for consistency
		var cleanedPin = InputValidator.CleanPin(pin);
		if (!GameHost.IsDevelopmentContext() && !InputValidator.IsValidPin(cleanedPin))
			return false;

		try
		{
			// Authenticate with backend
			var authResult = await _dataStore.AuthenticateByPhoneAsync(cleanedPhone, cleanedPin);
			if (!authResult.IsSuccess)
			{
				LogWarning($"Authentication failed for phone {cleanedPhone}: {authResult.Error}");
				return false;
			}

			var userData = authResult.Value;
			var userPhoneNumber = userData.PhoneNumber;

			// Don't allow duplicate sessions for the same user
			if (_activeSessions.ContainsKey(userPhoneNumber))
			{
				LogWarning($"User {userPhoneNumber} already has an active session");
				return false;
			}

			return await CreateUserSessionFromGlobalData(userData);
		}
		catch (Exception ex)
		{
			LogError($"Error logging in user with phone {cleanedPhone}: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Create a new user account with phone number authentication
	/// </summary>
	public async Task<Result<string>> CreateUserAccountAsync(string phoneNumber, string pin, string username)
	{
		// Validate inputs
		var cleanedPhone = InputValidator.CleanPhoneNumber(phoneNumber);
		if (!InputValidator.IsValidPhoneNumber(cleanedPhone))
			return Result<string>.Failure("Invalid phone number format");

		var cleanedPin = InputValidator.CleanPin(pin);
		if (!InputValidator.IsValidPin(cleanedPin))
			return Result<string>.Failure("PIN must be exactly 4 digits");

		var cleanedUsername = InputValidator.CleanUsername(username);
		if (!InputValidator.IsValidUsername(cleanedUsername))
			return Result<string>.Failure("Username must be 1-7 characters, alphanumeric and underscore only");

		try
		{
			// Check if phone number is already registered
			var phoneResult = await _dataStore.IsPhoneNumberRegisteredAsync(cleanedPhone);
			if (!phoneResult.IsSuccess)
				return Result<string>.Failure($"Error checking phone number: {phoneResult.Error}");

			if (phoneResult.Value)
				return Result<string>.Failure("Phone number is already registered");

			// Check if username is already taken
			var usernameResult = await _dataStore.IsUsernameTakenAsync(cleanedUsername);
			if (!usernameResult.IsSuccess)
				return Result<string>.Failure($"Error checking username: {usernameResult.Error}");

			if (usernameResult.Value)
				return Result<string>.Failure("Username is already taken");

			// Create the user account
			var createResult = await _dataStore.CreateUserAccountAsync(cleanedPhone, cleanedPin, cleanedUsername, CurrentLocationId);
			if (!createResult.IsSuccess)
				return Result<string>.Failure($"Failed to create account: {createResult.Error}");

			var userData = createResult.Value;
			LogInfo($"User account created successfully: {userData.UserName} ({userData.PhoneNumber})");

			return Result<string>.Success(userData.PhoneNumber);
		}
		catch (Exception ex)
		{
			LogError($"Error creating user account: {ex.Message}");
			return Result<string>.Failure($"Account creation failed: {ex.Message}");
		}
	}

	/// <summary>
	/// Check if a username is available for registration
	/// </summary>
	public async Task<Result<bool>> IsUsernameAvailableAsync(string username)
	{
		var cleanedUsername = InputValidator.CleanUsername(username);
		if (!InputValidator.IsValidUsername(cleanedUsername))
			return Result<bool>.Failure("Invalid username format");

		try
		{
			var result = await _dataStore.IsUsernameTakenAsync(cleanedUsername);
			if (!result.IsSuccess)
				return Result<bool>.Failure(result.Error);

			return Result<bool>.Success(!result.Value); // Available if NOT taken
		}
		catch (Exception ex)
		{
			return Result<bool>.Failure($"Error checking username availability: {ex.Message}");
		}
	}

	/// <summary>
	/// Log out a user and sync their data
	/// </summary>
	public async Task<bool> LogoutUserAsync(string phoneNumber)
	{
		if (!_activeSessions.TryGetValue(phoneNumber, out var session))
			return false;

		try
		{
			// Sync user data before logout
			await _dataStore.SyncUserDataAsync(phoneNumber);

			// Update local data with final session info
			var localDataResult = await _dataStore.GetLocalDataAsync(phoneNumber);
			if (localDataResult.IsSuccess)
			{
				var localData = localDataResult.Value;
				localData.LastLoginTime = DateTime.UtcNow;
				var saveResult = await _dataStore.SetLocalDataAsync(phoneNumber, localData);
				if (!saveResult.IsSuccess)
					LogWarning($"Failed to save logout time for phone {phoneNumber}: {saveResult.Error}");
			}
			else
			{
				LogWarning($"Failed to get local data for logout of phone {phoneNumber}: {localDataResult.Error}");
			}

			_activeSessions.Remove(phoneNumber);
			EmitSignal(SignalName.UserLoggedOut, phoneNumber);

			LogInfo($"Phone {phoneNumber} logged out");
			return true;
		}
		catch (Exception ex)
		{
			LogError($"Error logging out phone {phoneNumber}: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Get active user session
	/// </summary>
	public UserSession GetUserSession(string phoneNumber)
	{
		return _activeSessions.GetValueOrDefault(phoneNumber);
	}

	/// <summary>
	/// Get primary user session (first logged-in user)
	/// Use for: UI display (TopMenuBar), single-user game contexts, auto-populate convenience
	/// Multi-user games: use GetUserSession(phoneNumber) for explicit targeting
	/// </summary>
	public UserSession GetPrimaryUserSession()
	{
		foreach (var session in _activeSessions.Values)
			return session;
		return null;
	}

	/// <summary>
	/// Check if user is logged in
	/// </summary>
	public bool IsUserLoggedIn(string phoneNumber)
	{
		return _activeSessions.ContainsKey(phoneNumber);
	}

	/// <summary>
	/// Get all active user IDs
	/// </summary>
	public string[] GetActivePhoneNumbers()
	{
		var phoneNumbers = new string[_activeSessions.Count];
		_activeSessions.Keys.CopyTo(phoneNumbers, 0);
		return phoneNumbers;
	}

	/// <summary>
	/// Check and spend global credits with UI confirmation
	/// </summary>
	public async Task<bool> CheckAndSpendGlobalCreditsAsync(string phoneNumber, int amount, string reason)
	{
		if (!IsUserLoggedIn(phoneNumber))
		{
			LogWarning($"User {phoneNumber} not logged in for credit spending");
			return false;
		}

		// In development mode, bypass credit costs
		if (GameHost.ShouldBypassCredits())
		{
			LogInfo($"Development mode - bypassing credit cost of {amount} for {reason}");
			EmitSignal(SignalName.CreditsSpent, phoneNumber, amount, reason);
			return true;
		}

		// Check if user has enough credits
		var globalDataResult = await _dataStore.GetGlobalDataAsync(phoneNumber);
		if (!globalDataResult.IsSuccess)
		{
			LogError($"Failed to get global data for user {phoneNumber}: {globalDataResult.Error}");
			return false;
		}

		var globalData = globalDataResult.Value;
		if (globalData.GlobalCredits < amount)
		{
			LogInfo($"User {phoneNumber} has insufficient global credits ({globalData.GlobalCredits}/{amount})");
			// TODO: Show buy credits dialog
			return false;
		}

		// Show confirmation dialog (simplified for prototype)
		bool confirmed = await ShowCreditConfirmationAsync(phoneNumber, amount, reason, globalData.GlobalCredits);
		if (!confirmed)
		{
			LogInfo($"User {phoneNumber} cancelled credit spending for {reason}");
			return false;
		}

		// Spend the credits
		var spendResult = await _dataStore.SpendGlobalCreditsAsync(phoneNumber, amount, reason);
		if (spendResult.IsSuccess)
		{
			EmitSignal(SignalName.CreditsSpent, phoneNumber, amount, reason);
		}

		return spendResult.IsSuccess;
	}


	/// <summary>
	/// Add credits to user account
	/// </summary>
	public async Task<bool> AddGlobalCreditsAsync(string phoneNumber, int amount, string reason)
	{
		var addResult = await _dataStore.AddGlobalCreditsAsync(phoneNumber, amount, reason);
		if (addResult.IsSuccess)
		{
			// Update cached session data
			if (_activeSessions.TryGetValue(phoneNumber, out var session) && session.GlobalData != null)
			{
				session.GlobalData.GlobalCredits += amount;
			}

			EmitSignal(SignalName.CreditsEarned, phoneNumber, amount, reason);
		}

		return addResult.IsSuccess;
	}

	/// <summary>
	/// Reset idle timer for user activity
	/// </summary>
	public void ResetUserIdleTimer(string phoneNumber)
	{
		if (_activeSessions.TryGetValue(phoneNumber, out var session))
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

	private async Task<bool> CreateUserSessionFromGlobalData(DataStore.GlobalUserData globalData)
	{
		var phoneNumber = globalData.PhoneNumber;

		// Load or create local data
		var localDataResult = await _dataStore.GetLocalDataAsync(phoneNumber);
		DataStore.LocalUserData localData;

		if (!localDataResult.IsSuccess)
		{
			// Create new local data if it doesn't exist
			localData = new DataStore.LocalUserData
			{
				PhoneNumber = phoneNumber,
				LocationId = CurrentLocationId,
				LastLoginTime = DateTime.UtcNow
			};

			var saveResult = await _dataStore.SetLocalDataAsync(phoneNumber, localData);
			if (!saveResult.IsSuccess)
				LogWarning($"Failed to save initial local data for phone {phoneNumber}: {saveResult.Error}");
		}
		else
		{
			localData = localDataResult.Value;
		}

		// Create session
		var session = new UserSession
		{
			PhoneNumber = phoneNumber,
			LocationId = CurrentLocationId,
			LoginTime = DateTime.UtcNow,
			LastActivity = DateTime.UtcNow,
			GlobalData = globalData,
			LocalData = localData
		};

		_activeSessions[phoneNumber] = session;

		// Update login time in local data
		localData.LastLoginTime = DateTime.UtcNow;
		var updateResult = await _dataStore.SetLocalDataAsync(phoneNumber, localData);
		if (!updateResult.IsSuccess)
			LogWarning($"Failed to update login time for phone {phoneNumber}: {updateResult.Error}");

		EmitSignal(SignalName.UserLoggedIn, phoneNumber);
		LogInfo($"User {globalData.UserName} (phone: {globalData.PhoneNumber}) logged in at location {CurrentLocationId}");

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
			var phoneNumber = kvp.Key;
			var session = kvp.Value;
			var idleTime = (now - session.LastActivity).TotalSeconds;

			if (idleTime >= IDLE_TIMEOUT)
			{
				usersToLogout.Add(phoneNumber);
			}
		}

		// Logout idle users
		foreach (var phoneNumber in usersToLogout)
		{
			LogInfo($"Auto-logout user {phoneNumber} due to idle timeout");
			await LogoutUserAsync(phoneNumber);
		}
	}

	private Task<bool> ShowCreditConfirmationAsync(string phoneNumber, int amount, string reason, int currentCredits)
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
		var phoneNumbers = GetActivePhoneNumbers();
		foreach (var phoneNumber in phoneNumbers)
		{
			_ = LogoutUserAsync(phoneNumber); // Fire and forget for cleanup
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
	public string PhoneNumber { get; set; } = string.Empty;
	public string LocationId { get; set; } = string.Empty;
	public DateTime LoginTime { get; set; }
	public DateTime LastActivity { get; set; }
	public DataStore.GlobalUserData GlobalData { get; set; }
	public DataStore.LocalUserData LocalData { get; set; }

	public TimeSpan SessionDuration => DateTime.UtcNow - LoginTime;
	public TimeSpan IdleTime => DateTime.UtcNow - LastActivity;
}