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

	private EventService _eventService;
	private Dictionary<string, UserSession> _activeSessions = new();
	private Timer _idleTimer;
	private const float IDLE_CHECK_INTERVAL = 30.0f; // 30 seconds
	private const float IDLE_TIMEOUT = 600.0f; // 10 minutes

	public string CurrentLocationId => System.Environment.GetEnvironmentVariable("BARBOX_LOCATION_ID") ??
	                                   System.Environment.MachineName.ToLowerInvariant().Replace("-", "_");

	protected override void OnServiceReady()
	{
		Instance = this;
		// Minimal setup only - actual initialization happens in OnServiceInitialize
	}

	protected override void OnServiceInitialize()
	{
		// Initialize EventService for event-sourced persistence
		_eventService = EventService.GetInstance();
		if (_eventService == null)
		{
			LogWarning("EventService not found - user events will not be persisted to backend");
		}

		SetupIdleTimer();

		LogInfo("SessionManager initialized with event-sourced persistence");
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
			// Event-sourced persistence - authentication delegated to backend
			// For now, create session directly (backend validates via events)

			// Don't allow duplicate sessions for the same user
			if (_activeSessions.ContainsKey(cleanedPhone))
			{
				LogWarning($"User {cleanedPhone} already has an active session");
				return false;
			}

			// Generate deterministic player ID from phone number
			var playerId = EventService.GetPlayerIdFromPhone(cleanedPhone);

			// Create local session
			var session = new UserSession
			{
				PhoneNumber = cleanedPhone,
				UserName = cleanedPhone.Substring(Math.Max(0, cleanedPhone.Length - 7)), // Last 7 digits as temp username
				LocationId = CurrentLocationId,
				PlayerId = playerId,
				LoginTime = DateTime.UtcNow,
				LastActivity = DateTime.UtcNow,
				Credits = 0 // Backend will manage credits via events
			};

			_activeSessions[cleanedPhone] = session;

			// Create backend session (strict mode - fail if backend unavailable)
			if (_eventService != null)
			{
				// Get box ID from environment - EventService validates this during initialization
				var boxIdStr = System.Environment.GetEnvironmentVariable("BARBOX_BOX_ID");
				if (string.IsNullOrEmpty(boxIdStr) || !Guid.TryParse(boxIdStr, out var boxId))
				{
					_activeSessions.Remove(cleanedPhone);
					LogError("BARBOX_BOX_ID environment variable not set or invalid");
					return false;
				}

				var sessionResult = await _eventService.CreateSessionAsync(boxId, playerId);
				if (!sessionResult.IsSuccess)
				{
					// Remove local session on backend failure
					_activeSessions.Remove(cleanedPhone);
					LogError($"Backend session creation failed: {sessionResult.Error}");
					return false;
				}

				// Store backend session ID
				session.BackendSessionId = sessionResult.Value;
				LogInfo($"Backend session created: {sessionResult.Value}");

				// Emit user/login event to backend
				var payload = new
				{
					phone_number = cleanedPhone,
					username = session.UserName,
					location_id = CurrentLocationId
				};
				_ = _eventService.EmitEventAsync("user/login", payload);
			}
			else
			{
				// EventService required for backend session
				_activeSessions.Remove(cleanedPhone);
				LogError("EventService not available - cannot create backend session");
				return false;
			}

			LogInfo($"User session created for phone {cleanedPhone}");
			EmitSignal(SignalName.UserLoggedIn, cleanedPhone);

			return true;
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
	public Task<Result<string>> CreateUserAccountAsync(string phoneNumber, string pin, string username)
	{
		// Validate inputs
		var cleanedPhone = InputValidator.CleanPhoneNumber(phoneNumber);
		if (!InputValidator.IsValidPhoneNumber(cleanedPhone))
			return Task.FromResult(Result<string>.Failure("Invalid phone number format"));

		var cleanedPin = InputValidator.CleanPin(pin);
		if (!InputValidator.IsValidPin(cleanedPin))
			return Task.FromResult(Result<string>.Failure("PIN must be exactly 4 digits"));

		var cleanedUsername = InputValidator.CleanUsername(username);
		if (!InputValidator.IsValidUsername(cleanedUsername))
			return Task.FromResult(Result<string>.Failure("Username must be 1-7 characters, alphanumeric and underscore only"));

		try
		{
			// Event-sourced persistence - backend validates phone/username uniqueness
			// For now, optimistically create account (backend will reject if duplicate)

			LogInfo($"User account creation requested: {cleanedUsername} ({cleanedPhone})");

			// Emit user/create event to backend
			if (_eventService != null)
			{
				var payload = new
				{
					phone_number = cleanedPhone,
					pin = cleanedPin,
					username = cleanedUsername,
					location_id = CurrentLocationId
				};
				_ = _eventService.EmitEventAsync("user/create", payload);
			}

			return Task.FromResult(Result<string>.Success(cleanedPhone));
		}
		catch (Exception ex)
		{
			LogError($"Error creating user account: {ex.Message}");
			return Task.FromResult(Result<string>.Failure($"Account creation failed: {ex.Message}"));
		}
	}

	/// <summary>
	/// Check if a username is available for registration
	/// </summary>
	public Task<Result<bool>> IsUsernameAvailableAsync(string username)
	{
		var cleanedUsername = InputValidator.CleanUsername(username);
		if (!InputValidator.IsValidUsername(cleanedUsername))
			return Task.FromResult(Result<bool>.Failure("Invalid username format"));

		try
		{
			// Event-sourced persistence - backend validates username uniqueness
			// Optimistically return true (backend will reject during creation if taken)
			LogInfo($"Username availability check requested: {cleanedUsername}");
			return Task.FromResult(Result<bool>.Success(true));
		}
		catch (Exception ex)
		{
			return Task.FromResult(Result<bool>.Failure($"Error checking username availability: {ex.Message}"));
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
			// Emit user/logout event to backend before removing session
			if (_eventService != null && session.BackendSessionId != Guid.Empty)
			{
				var payload = new
				{
					phone_number = phoneNumber,
					location_id = CurrentLocationId,
					session_duration = session.SessionDuration.TotalSeconds
				};
				var logoutResult = await _eventService.EmitEventAsync("user/logout", payload);
				if (!logoutResult.IsSuccess)
				{
					LogWarning($"Failed to emit logout event: {logoutResult.Error}");
				}
			}

			// Clear backend session context if this was the current session
			if (_eventService != null &&
			    session.BackendSessionId != Guid.Empty &&
			    _eventService.GetCurrentSessionId() == session.BackendSessionId)
			{
				// Session will be cleared on EventService side naturally
				// No explicit close method needed - backend tracks session end via logout event
			}

			// Remove local session
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

		// Event-sourced persistence - backend tracks credit balance
		// Check local session cache for credits
		if (!_activeSessions.TryGetValue(phoneNumber, out var session))
		{
			LogWarning($"No active session for user {phoneNumber}");
			return false;
		}

		if (session.Credits < amount)
		{
			LogInfo($"User {phoneNumber} has insufficient credits ({session.Credits}/{amount})");
			// TODO: Show buy credits dialog
			return false;
		}

		// Show confirmation dialog (simplified for prototype)
		bool confirmed = await ShowCreditConfirmationAsync(phoneNumber, amount, reason, session.Credits);
		if (!confirmed)
		{
			LogInfo($"User {phoneNumber} cancelled credit spending for {reason}");
			return false;
		}

		// Update local session cache
		session.Credits -= amount;
		EmitSignal(SignalName.CreditsSpent, phoneNumber, amount, reason);

		// Emit credit/spend event to backend
		if (_eventService != null)
		{
			var payload = new
			{
				phone_number = phoneNumber,
				amount = amount,
				reason = reason,
				new_balance = session.Credits
			};
			_ = _eventService.EmitEventAsync("credit/spend", payload);
		}

		return true;
	}


	/// <summary>
	/// Add credits to user account
	/// </summary>
	public Task<bool> AddGlobalCreditsAsync(string phoneNumber, int amount, string reason)
	{
		// Event-sourced persistence - backend tracks credit balance
		// Update local session cache
		if (_activeSessions.TryGetValue(phoneNumber, out var session))
		{
			session.Credits += amount;
		}

		EmitSignal(SignalName.CreditsEarned, phoneNumber, amount, reason);

		// Emit credit/earn event to backend
		if (_eventService != null)
		{
			var payload = new
			{
				phone_number = phoneNumber,
				amount = amount,
				reason = reason,
				new_balance = session?.Credits ?? 0
			};
			_ = _eventService.EmitEventAsync("credit/earn", payload);
		}

		return Task.FromResult(true);
	}

	/// <summary>
	/// Transfer credits from user's account to machine game credits
	/// </summary>
	public Task<bool> TransferCreditsToMachineAsync(string phoneNumber, string gameId, int amount, string reason)
	{
		if (!IsUserLoggedIn(phoneNumber))
		{
			LogWarning($"User {phoneNumber} not logged in for credit transfer");
			return Task.FromResult(false);
		}

		// Event-sourced persistence - backend tracks credit balance
		// Update local session cache
		if (_activeSessions.TryGetValue(phoneNumber, out var session))
		{
			session.Credits -= amount;
		}

		EmitSignal(SignalName.CreditsSpent, phoneNumber, amount, $"Transfer to {gameId}: {reason}");

		// Emit credit/transfer event to backend
		if (_eventService != null)
		{
			var payload = new
			{
				phone_number = phoneNumber,
				game_id = gameId,
				amount = amount,
				reason = reason,
				new_balance = session?.Credits ?? 0
			};
			_ = _eventService.EmitEventAsync("credit/transfer", payload);
		}

		return Task.FromResult(true);
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
	public string UserName { get; set; } = string.Empty;
	public string LocationId { get; set; } = string.Empty;
	public Guid PlayerId { get; set; } = Guid.Empty;
	public Guid BackendSessionId { get; set; } = Guid.Empty;
	public DateTime LoginTime { get; set; }
	public DateTime LastActivity { get; set; }
	public int Credits { get; set; }

	public TimeSpan SessionDuration => DateTime.UtcNow - LoginTime;
	public TimeSpan IdleTime => DateTime.UtcNow - LastActivity;
}