using Godot;
using LightResults;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BarBox.Core.Autoloads;

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
/// - Use GetPrimarySession() for:
///   * TopMenuBar/UI display (showing "a" user)
///   * Single-user game contexts (Racing, Mining)
///   * Auto-populate convenience (first slot in multiplayer setup)
///   * Fallback scenarios (GameHost.GetUserSession)
///
/// MULTI-USER PATTERNS:
/// - Multi-user games MUST use GetSessionByPhone(phoneNumber) for explicit targeting
/// - Example: CarromPlayerSetupMenu tracks users per slot explicitly
/// - User-specific operations (BuyCredits, logout) require explicit phone number
///
/// API USAGE:
/// - GetSession(playerId) → Primary API for backend operations (O(1) lookup by player ID)
/// - GetPrimarySession() → Primary user (UI/single-user contexts)
/// - GetSessionByPhone(phoneNumber) → Specific user (multi-user contexts)
/// - LoginUserByPhoneAsync(phone, pin) → Authenticate user
/// - LogoutUserAsync(phoneNumber) → Logout specific user
/// </summary>
public partial class SessionManager : AutoloadBase
{
	[Signal] public delegate void UserLoggedInEventHandler(string phoneNumber);
	[Signal] public delegate void UserLoggedOutEventHandler(string phoneNumber);

	private EventService _eventService;
	private Dictionary<string, UserSession> _activeSessions = new();
	private Dictionary<Guid, UserSession> _sessionsByPlayerId = new(); // Secondary index for O(1) lookup by PlayerId
	private Godot.Timer _idleTimer;
	private const float IDLE_CHECK_INTERVAL = 30.0f; // 30 seconds
	private const float IDLE_TIMEOUT = 600.0f; // 10 minutes

	// CRITICAL FIX: Limit concurrent logout operations to prevent overwhelming backend
	private readonly SemaphoreSlim _logoutSemaphore = new(5, 5); // Max 5 concurrent logouts

	public string CurrentLocationId
	{
		get
		{
			var locationManager = LocationManager.GetAutoload();
			return locationManager?.VenueName ??
				   System.Environment.MachineName.ToLowerInvariant().Replace("-", "_");
		}
	}

	protected override void OnServiceEnterTree()
	{
		_eventService = EventService.GetInstance();
		if (_eventService == null)
		{
			LogWarning("EventService not found - user events will not be persisted to backend");
		}

		SetupIdleTimer();

		LogInfo("SessionManager initialized with event-sourced persistence");
	}

	public override void _Notification(int what)
	{
		if (what == NotificationWMCloseRequest)
		{
			// Intercept application shutdown to properly logout all users
			GetTree().AutoAcceptQuit = false;
			_ = ShutdownGracefullyAsync();
		}
	}

	/// <summary>
	/// Gracefully shutdown by logging out all active users with timeout
	/// </summary>
	private async Task ShutdownGracefullyAsync()
	{
		try
		{
			LogInfo("SessionManager: Beginning graceful shutdown...");

			var phoneNumbers = GetActivePhoneNumbers();
			if (phoneNumbers.Length == 0)
			{
				LogInfo("SessionManager: No active sessions to cleanup");
				GetTree().Quit();
				return;
			}

			LogInfo($"SessionManager: Logging out {phoneNumbers.Length} active user(s)...");

			// Log out sequentially — LogoutUserAsync mutates the shared, non-thread-safe
			// session dictionaries, so concurrent logouts would race (tenet 2). Preserve the
			// overall 5-second shutdown budget by racing the sequential loop against a timeout.
			async Task LogoutAllSequentiallyAsync()
			{
				foreach (var phoneNumber in phoneNumbers)
				{
					await LogoutUserAsync(phoneNumber);
				}
			}

			var allLogoutsTask = LogoutAllSequentiallyAsync();
			var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));
			var completedTask = await Task.WhenAny(allLogoutsTask, timeoutTask);

			if (completedTask == timeoutTask)
			{
				GD.PushWarning("SessionManager: Logout timeout during shutdown - some sessions may not have completed logout");
			}
			else
			{
				LogInfo("SessionManager: All user sessions logged out successfully");
			}

			// Proceed with shutdown
			GetTree().Quit();
		}
		catch (System.Exception ex)
		{
			GD.PushError($"SessionManager: Error during graceful shutdown: {ex.Message}");
			// Proceed with shutdown even if error occurred
			GetTree().Quit();
		}
	}

	/// <summary>
	/// Authenticate and log in a user by phone number and PIN
	/// Returns Result with UserSession on success or specific error message on failure
	/// </summary>
	public async Task<Result<UserSession>> LoginUserByPhoneAsync(string phoneNumber, string pin)
	{
		if (string.IsNullOrEmpty(phoneNumber))
			return Result.Failure<UserSession>("Phone number is required");

		// Clean phone number for consistency
		var cleanedPhone = InputValidator.CleanPhoneNumber(phoneNumber);
		if (!InputValidator.IsValidPhoneNumber(cleanedPhone))
			return Result.Failure<UserSession>("Invalid phone number format");

		// Clean PIN for consistency
		var cleanedPin = InputValidator.CleanPin(pin);
		if (!InputValidator.IsValidPin(cleanedPin))
			return Result.Failure<UserSession>("PIN must be exactly 4 digits");

		try
		{
			// Don't allow duplicate sessions for the same user
			if (_activeSessions.ContainsKey(cleanedPhone))
			{
				LogWarning($"User {cleanedPhone} already has an active session");
				return Result.Failure<UserSession>("User already logged in");
			}

			// Authenticate with backend and get JWT token
			if (_eventService == null || !_eventService.IsReady)
			{
				LogError("EventService not available - cannot authenticate");
				return Result.Failure<UserSession>("Backend service not available");
			}

			var boxId = _eventService.GetBoxId();
			var loginRequest = new PlayerLoginRequest
			{
				PhoneNumber = cleanedPhone,
				Pin = cleanedPin,
				BoxId = boxId.ToString()
			};

			var loginResult = await _eventService.PostAsync<PlayerLoginRequest, PlayerLoginResponse>(
				"/player/auth/login",
				loginRequest,
				expectedStatusCode: 200
			);

			if (loginResult.IsFailure(out var loginError))
			{
				LogError($"Backend authentication failed: {loginError.Message}");
				return Result.Failure<UserSession>($"Authentication failed: {loginError.Message}");
			}

			if (!loginResult.IsSuccess(out var loginData))
			{
				LogError("Login result was neither success nor failure - unexpected state");
				return Result.Failure<UserSession>("Unexpected authentication state");
			}

			// Validate response contains all required fields
			var validationResult = loginData.ValidateRequired();
			if (validationResult.IsFailure(out var validationError))
			{
				LogError($"Backend response validation failed: {validationError.Message}");
				return Result.Failure<UserSession>($"Invalid response: {validationError.Message}");
			}

			// Now safe to parse - validation guarantees non-empty strings
			var playerId = Guid.Parse(loginData.PlayerId);

			// Validate player ID is not empty/zero GUID
			if (playerId == Guid.Empty)
			{
				LogError($"Backend returned invalid player ID (all zeros) for phone {cleanedPhone}");
				return Result.Failure<UserSession>("Invalid player ID received from backend");
			}

			// Parse token expiration (ISO 8601 format from backend)
			// CRITICAL: Use DateTimeStyles.RoundtripKind to preserve the UTC timezone from the Z suffix
			// Without this, DateTime.TryParse() may parse as local time, causing 4+ hour timezone mismatch
			if (!DateTime.TryParse(loginData.ExpiresAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var tokenExpiration))
			{
				LogError($"Failed to parse token expiration: {loginData.ExpiresAt}");
				return Result.Failure<UserSession>("Invalid token expiration format");
			}

			// Create local session with JWT token
			var session = new UserSession
			{
				PhoneNumber = cleanedPhone,
				UserName = loginData.Username, // Use username from backend response
				LocationId = CurrentLocationId,
				PlayerId = playerId,
				LoginTime = DateTime.UtcNow,
				LastActivity = DateTime.UtcNow,
				JwtToken = loginData.AccessToken,
				TokenExpiration = tokenExpiration
			};

			LogInfo($"User authenticated successfully - Token expires at: {tokenExpiration:u}");

			// Add session to dictionaries
			_activeSessions[cleanedPhone] = session;
			_sessionsByPlayerId[playerId] = session; // Maintain secondary index

			// Create lobby session for user-scoped events
			if (_eventService != null && _eventService.IsReady)
			{
				var lobbyBoxId = _eventService.GetBoxId();
				var lobbySessionResult = await _eventService.CreateLobbySessionAsync(lobbyBoxId, playerId);
				if (lobbySessionResult.IsSuccess(out var lobbySessionId))
				{
					session.LobbySessionId = lobbySessionId;
					LogInfo($"Lobby session created: {session.LobbySessionId}");

					// Emit user/login event to lobby session
					var payload = new
					{
						username = session.UserName,
						location_id = CurrentLocationId
					};
					var loginEventResult = await _eventService.EmitUserEventAsync(playerId, "user/login", payload, session.LobbySessionId);
					if (loginEventResult.IsFailure(out var eventError))
					{
						LogWarning($"Failed to emit login event: {eventError.Message}");
					}
				}
				else if (lobbySessionResult.IsFailure(out var lobbyError))
				{
					LogWarning($"Failed to create lobby session: {lobbyError.Message}");
					LogWarning("User logged in without lobby session - credit operations will require lazy lobby creation");
					session.LobbySessionId = Guid.Empty; // Explicitly mark as missing
				}
			}
			else
			{
				LogWarning("EventService not available - user events will not be persisted");
			}

			LogInfo($"User session created for phone {cleanedPhone}");
			EmitSignal(SignalName.UserLoggedIn, cleanedPhone);

			return Result.Success(session);
		}
		catch (Exception ex)
		{
			LogError($"Error logging in user with phone {cleanedPhone}: {ex.Message}");
			return Result.Failure<UserSession>($"Unexpected error: {ex.Message}");
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
			return Result.Failure<string>("Invalid phone number format");

		var cleanedPin = InputValidator.CleanPin(pin);
		if (!InputValidator.IsValidPin(cleanedPin))
			return Result.Failure<string>("PIN must be exactly 4 digits");

		var cleanedUsername = InputValidator.CleanUsername(username);
		if (!InputValidator.IsValidUsername(cleanedUsername))
			return Result.Failure<string>("Username must be 1-7 characters, alphanumeric and underscore only");

		if (_eventService == null)
		{
			LogError("EventService not available for account creation");
			return Result.Failure<string>("Backend service unavailable - please try again later");
		}

		try
		{
			LogInfo($"User account creation requested: {cleanedUsername} ({cleanedPhone})");

			// Generate new player ID (UUID v4)
			var playerId = Guid.NewGuid();

			// Get box ID from LocationManager
			var locationManager = LocationManager.GetAutoload();
			if (locationManager == null || !locationManager.IsConfigLoaded)
			{
				LogError("LocationManager not available for account creation");
				return Result.Failure<string>("Location service unavailable - please try again later");
			}

			var boxId = locationManager.BoxId;
			// Create player account via direct REST call to backend
			var result = await _eventService.CreatePlayerAsync(playerId, cleanedUsername, cleanedPhone, cleanedPin, boxId);
			if (result.IsSuccess(out _))
			{
				LogInfo($"Account created successfully for {cleanedUsername} ({cleanedPhone})");
				return Result.Success(cleanedPhone);
			}
			else if (result.IsFailure(out var error))
			{
				LogError($"Account creation failed: {error.Message}");

				// Provide user-friendly error messages
				if (error.Message.Contains("409") || error.Message.Contains("already exists"))
					return Result.Failure<string>("An account with this phone number or username already exists");
				else if (error.Message.Contains("timeout"))
					return Result.Failure<string>("Connection timeout - please check your network and try again");
				else
					return Result.Failure<string>($"Account creation failed: {error.Message}");
			}

			return Result.Failure<string>("Unknown error during account creation");
		}
		catch (Exception ex)
		{
			LogError($"Error creating user account: {ex.Message}");
			return Result.Failure<string>($"Account creation failed: {ex.Message}");
		}
	}

	/// <summary>
	/// Validate player creation before attempting to create (pre-flight check)
	/// Checks: box exists, username available, player ID unique
	/// </summary>
	public async Task<Result<PlayerValidationResponse>> ValidatePlayerCreationAsync(string phoneNumber, string pin, string username)
	{
		// Clean and validate inputs
		var cleanedPhone = InputValidator.CleanPhoneNumber(phoneNumber);
		if (!InputValidator.IsValidPhoneNumber(cleanedPhone))
			return Result.Failure<PlayerValidationResponse>("Invalid phone number format");

		var cleanedPin = InputValidator.CleanPin(pin);
		if (!InputValidator.IsValidPin(cleanedPin))
			return Result.Failure<PlayerValidationResponse>("PIN must be exactly 4 digits");

		var cleanedUsername = InputValidator.CleanUsername(username);
		if (!InputValidator.IsValidUsername(cleanedUsername))
			return Result.Failure<PlayerValidationResponse>("Username must be 1-7 characters, alphanumeric and underscore only");

		if (_eventService == null)
		{
			LogError("EventService not available for validation");
			return Result.Failure<PlayerValidationResponse>("Backend service unavailable - please try again later");
		}

		try
		{
			// Generate temporary player ID for validation
			// NOTE: Cannot use GetPlayerIdFromPhone() here because user hasn't registered yet (no session exists)
			var playerId = Guid.NewGuid();

			// Get box ID from LocationManager
			var locationManager = LocationManager.GetAutoload();
			if (locationManager == null || !locationManager.IsConfigLoaded)
			{
				LogError("LocationManager not available for validation");
				return Result.Failure<PlayerValidationResponse>("Location service unavailable - please try again later");
			}

			var boxId = locationManager.BoxId;

			// Validate via backend
			var result = await _eventService.ValidatePlayerCreationAsync(playerId, cleanedUsername, cleanedPhone, cleanedPin, boxId);
			if (result.IsSuccess(out var validationResponse))
			{
				LogInfo($"Validation result for {cleanedUsername}: Valid={validationResponse.Valid}, ErrorCount={validationResponse.Errors?.Length ?? 0}");
				return Result.Success(validationResponse);
			}
			else if (result.IsFailure(out var error))
			{
				LogWarning($"Validation failed: {error.Message}");
				return Result.Failure<PlayerValidationResponse>(error.Message);
			}

			return Result.Failure<PlayerValidationResponse>("Unknown validation error");
		}
		catch (Exception ex)
		{
			LogError($"Error validating player creation: {ex.Message}");
			return Result.Failure<PlayerValidationResponse>($"Validation error: {ex.Message}");
		}
	}

	/// <summary>
	/// Check if a username is available for registration
	/// </summary>
	public async Task<Result<bool>> IsUsernameAvailableAsync(string username)
	{
		var cleanedUsername = InputValidator.CleanUsername(username);
		if (!InputValidator.IsValidUsername(cleanedUsername))
			return Result.Failure<bool>("Invalid username format");

		try
		{
			if (_eventService == null)
			{
				// Fallback to optimistic validation if backend unavailable
				LogWarning("EventService not available, optimistically allowing username");
				return Result.Success(true);
			}

			var result = await _eventService.IsUsernameAvailableAsync(cleanedUsername);
			if (result.IsSuccess(out var isAvailable))
			{
				LogInfo($"Username '{cleanedUsername}' availability: {isAvailable}");
				return Result.Success(isAvailable);
			}
			else if (result.IsFailure(out var error))
			{
				LogWarning($"Failed to check username: {error.Message}");
				return Result.Failure<bool>(error.Message);
			}

			return Result.Failure<bool>("Unknown error checking username");
		}
		catch (Exception ex)
		{
			return Result.Failure<bool>($"Error checking username availability: {ex.Message}");
		}
	}

	/// <summary>
	/// Log out a user and sync their data
	/// </summary>
	public async Task<bool> LogoutUserAsync(string phoneNumber)
	{
		// CRITICAL FIX: Use semaphore to limit concurrent logout operations
		// Prevents overwhelming backend with many simultaneous HTTP calls during mass logout
		await _logoutSemaphore.WaitAsync();

		try
		{
			// IDEMPOTENCY CHECK: Double-check session exists after acquiring semaphore
			// Prevents duplicate logout attempts if multiple callers try to logout the same user
			if (!_activeSessions.TryGetValue(phoneNumber, out var session))
			{
				LogInfo($"User {phoneNumber} already logged out - skipping");
				return false;
			}
			// Revoke JWT token on backend
			if (_eventService != null && _eventService.IsReady && !string.IsNullOrEmpty(session.JwtToken))
			{
				// Create a simple logout request (backend reads JWT from Authorization header)
				var logoutRequest = new { };

				// Pass playerId to include Authorization header
				var revokeResult = await _eventService.PostAsync<object, object>(
					"/player/auth/logout",
					logoutRequest,
					expectedStatusCode: 200,
					playerId: session.PlayerId
				);

				if (revokeResult.IsFailure(out var revokeError))
				{
					LogWarning($"Failed to revoke JWT token: {revokeError.Message}");
				}
				else
				{
					LogInfo("JWT token revoked successfully");
				}
			}

			// Emit user/logout event to lobby session (before closing it)
			if (_eventService != null && _eventService.IsReady && session.LobbySessionId != Guid.Empty)
			{
				var payload = new
				{
					phone_number = phoneNumber,
					location_id = CurrentLocationId,
					session_duration = session.SessionDuration.TotalSeconds
				};
				var logoutResult = await _eventService.EmitUserEventAsync(session.PlayerId, "user/logout", payload, session.LobbySessionId);
				if (logoutResult.IsFailure(out var logoutError))
				{
					LogWarning($"Failed to emit logout event: {logoutError.Message}");
				}

				// Close lobby session
				var closeResult = await _eventService.CloseActivitySessionAsync(session.LobbySessionId);
				if (closeResult.IsFailure(out var closeError))
				{
					LogWarning($"Failed to close lobby session: {closeError.Message}");
				}
				else
				{
					LogInfo($"Lobby session closed: {session.LobbySessionId}");
				}
			}

			// Remove local session
			_activeSessions.Remove(phoneNumber);
			_sessionsByPlayerId.Remove(session.PlayerId); // Maintain secondary index
			EmitSignal(SignalName.UserLoggedOut, phoneNumber);

			LogInfo($"Phone {phoneNumber} logged out");
			return true;
		}
		catch (Exception ex)
		{
			LogError($"Error logging out phone {phoneNumber}: {ex.Message}");
			return false;
		}
		finally
		{
			_logoutSemaphore.Release();
		}
	}

	/// <summary>
	/// Log out all users except the primary (earliest logged-in) user
	/// Used when exiting to main menu from multi-player games
	/// </summary>
	public async Task LogoutNonPrimaryUsersAsync()
	{
		var primarySession = GetPrimarySession();
		if (primarySession == null)
		{
			LogInfo("No primary user found - nothing to logout");
			return;
		}

		var sessionsToLogout = new List<string>();
		foreach (var phoneNumber in _activeSessions.Keys)
		{
			if (phoneNumber != primarySession.PhoneNumber)
			{
				sessionsToLogout.Add(phoneNumber);
			}
		}

		if (sessionsToLogout.Count == 0)
		{
			LogInfo("No non-primary users to logout");
			return;
		}

		LogInfo($"Logging out {sessionsToLogout.Count} non-primary user(s)");

		foreach (var phoneNumber in sessionsToLogout)
		{
			await LogoutUserAsync(phoneNumber);
		}
	}

	/// <summary>
	/// Get user session by player ID (primary API for backend operations)
	/// Uses secondary index for O(1) lookup performance
	/// </summary>
	public UserSession GetSession(Guid playerId)
	{
		return _sessionsByPlayerId.GetValueOrDefault(playerId);
	}

	/// <summary>
	/// Ensures the player has a lobby session, creating one on-demand if missing.
	/// Used for lazy lobby session creation when operations require it.
	/// </summary>
	/// <returns>Result with lobby session ID on success, or error message on failure</returns>
	public async Task<Result<Guid>> EnsureLobbySessionAsync(Guid playerId)
	{
		var session = GetSession(playerId);
		if (session == null)
		{
			return Result.Failure<Guid>($"No session found for player {playerId}");
		}

		// Already has a lobby session
		if (session.LobbySessionId != Guid.Empty)
		{
			return Result.Success(session.LobbySessionId);
		}

		// Create lobby session on-demand
		if (_eventService == null || !_eventService.IsReady)
		{
			return Result.Failure<Guid>("EventService not available for lobby session creation");
		}

		LogInfo($"Creating lobby session on-demand for player {playerId}");
		var boxId = _eventService.GetBoxId();
		var lobbyResult = await _eventService.CreateLobbySessionAsync(boxId, playerId);

		if (lobbyResult.IsSuccess(out var lobbySessionId))
		{
			session.LobbySessionId = lobbySessionId;
			LogInfo($"Lazy lobby session created: {lobbySessionId}");
			return Result.Success(lobbySessionId);
		}

		if (lobbyResult.IsFailure(out var error))
		{
			LogWarning($"Failed to create lobby session: {error.Message}");
			return Result.Failure<Guid>(error.Message);
		}

		return Result.Failure<Guid>("Unknown error creating lobby session");
	}

	/// <summary>
	/// Get primary user session (earliest logged-in user)
	/// Use for: UI display (TopMenuBar), single-user game contexts, auto-populate convenience
	/// Multi-user games: use GetSessionByPhone(phoneNumber) for explicit targeting
	/// </summary>
	public UserSession GetPrimarySession()
	{
		return _activeSessions.Values.MinBy(s => s.LoginTime);
	}

	/// <summary>
	/// Get user session by phone number (multi-user explicit targeting)
	/// Use when you need a specific user in a multi-user context (e.g., Carrom player slots)
	/// </summary>
	public UserSession GetSessionByPhone(string phoneNumber)
	{
		return _activeSessions.GetValueOrDefault(phoneNumber);
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
		return _activeSessions.Keys.ToArray();
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

	/// <summary>
	/// Get JWT token for a specific player (for EventService Authorization header)
	/// Returns null if session not found or token expired
	/// </summary>
	public string GetJwtToken(Guid playerId)
	{
		// Skip logging for empty GUID (expected during initialization)
		if (playerId == Guid.Empty)
			return null;

		if (!_sessionsByPlayerId.TryGetValue(playerId, out var session))
		{
			// Use Info level - session may still be initializing
			LogInfo($"[GetJwtToken] No session found for player {playerId} (may be initializing, {_sessionsByPlayerId.Count} active sessions)");
			return null;
		}

		if (session.IsTokenExpired)
		{
			LogWarning($"[GetJwtToken] JWT token expired for player {playerId}");
			LogWarning($"[GetJwtToken] Token expiration: {session.TokenExpiration:u}, Current time: {DateTime.UtcNow:u}");
			return null;
		}

		if (string.IsNullOrEmpty(session.JwtToken))
		{
			// Use Info level - token may still be loading
			LogInfo($"[GetJwtToken] Session found for player {playerId} but JwtToken is null/empty (may be initializing)");
			return null;
		}

		// Successful token retrieval - use debug level to reduce noise
		return session.JwtToken;
	}
	/// <summary>
	/// Get JWT token for the primary (first logged-in) user
	/// Returns null if no users logged in or token expired
	/// </summary>
	public string GetPrimaryUserJwtToken()
	{
		var primarySession = GetPrimarySession();
		if (primarySession == null)
			return null;

		if (primarySession.IsTokenExpired)
		{
			LogWarning($"JWT token expired for primary user {primarySession.PhoneNumber}");
			return null;
		}

		return primarySession.JwtToken;
	}

	// Private helper methods

	private void SetupIdleTimer()
	{
		_idleTimer = new Godot.Timer();
		_idleTimer.WaitTime = IDLE_CHECK_INTERVAL;
		_idleTimer.Timeout += OnIdleTimerTimeout;
		AddChild(_idleTimer);
		_idleTimer.Start();
	}

	private async void OnIdleTimerTimeout()
	{
		var now = DateTime.UtcNow;
		var usersToLogout = _activeSessions
			.Where(kvp => (now - kvp.Value.LastActivity).TotalSeconds >= IDLE_TIMEOUT)
			.Select(kvp => kvp.Key)
			.ToList();

		foreach (var phoneNumber in usersToLogout)
		{
			LogInfo($"Auto-logout user {phoneNumber} due to idle timeout");
			await LogoutUserAsync(phoneNumber);
		}
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
		GD.PushWarning("SessionManager: OnServiceDestroyed() called - graceful shutdown notification may have been missed");

		// Logout all users before shutdown (fallback path - fire and forget)
		var phoneNumbers = GetActivePhoneNumbers();
		foreach (var phoneNumber in phoneNumbers)
		{
			_ = LogoutUserAsync(phoneNumber); // Fire and forget for cleanup
		}

		_idleTimer?.Stop();
		_idleTimer?.QueueFree();

		// Dispose of logout semaphore
		_logoutSemaphore?.Dispose();
	}
}

/// <summary>
/// User session data structure
/// Represents authentication session (login → logout), not gameplay session
/// </summary>
public class UserSession
{
	public string PhoneNumber { get; set; } = string.Empty;
	public string UserName { get; set; } = string.Empty;
	public string LocationId { get; set; } = string.Empty;
	public Guid PlayerId { get; set; } = Guid.Empty;
	public Guid LobbySessionId { get; set; } = Guid.Empty;  // Lobby session for user-scoped events
	public DateTime LoginTime { get; set; }
	public DateTime LastActivity { get; set; }

	// JWT authentication fields
	public string JwtToken { get; set; } = string.Empty;
	public DateTime TokenExpiration { get; set; } = DateTime.MinValue;
	public bool IsTokenExpired => DateTime.UtcNow >= TokenExpiration;

	public TimeSpan SessionDuration => DateTime.UtcNow - LoginTime;
	public TimeSpan IdleTime => DateTime.UtcNow - LastActivity;
}