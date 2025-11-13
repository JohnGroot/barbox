using Godot;
using LightResults;
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
///   * Fallback scenarios (GameHost.GetUserSession)
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
	private CreditService _creditService;
	private Dictionary<string, UserSession> _activeSessions = new();
	private Dictionary<Guid, UserSession> _sessionsByPlayerId = new(); // Secondary index for O(1) lookup by PlayerId
	private Timer _idleTimer;
	private const float IDLE_CHECK_INTERVAL = 30.0f; // 30 seconds
	private const float IDLE_TIMEOUT = 600.0f; // 10 minutes

	public string CurrentLocationId
	{
		get
		{
			var locationManager = LocationManager.GetAutoload();
			return locationManager?.LocationId ??
			       System.Environment.MachineName.ToLowerInvariant().Replace("-", "_");
		}
	}

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

		// Initialize CreditService and connect to credit changes
		_creditService = CreditService.Instance;
		if (_creditService != null)
		{
			_creditService.CreditsChanged += OnCreditsChanged;
			LogInfo("SessionManager connected to CreditService for credit change notifications");
		}
		else
		{
			LogWarning("CreditService not found - credit change signals will not be emitted");
		}

		SetupIdleTimer();

		LogInfo("SessionManager initialized with event-sourced persistence");
	}

	/// <summary>
	/// Relay CreditService.CreditsChanged signal as SessionManager.CreditsEarned
	/// This allows UI components to listen to SessionManager instead of CreditService directly
	/// </summary>
	private void OnCreditsChanged(string playerIdStr, int newBalance)
	{
		// Parse playerId to Guid to match against active sessions
		if (!Guid.TryParse(playerIdStr, out var playerId))
		{
			LogWarning($"Invalid player ID format in credit change: {playerIdStr}");
			return;
		}

		// Find the session with matching player ID (O(1) lookup via secondary index)
		if (_sessionsByPlayerId.TryGetValue(playerId, out var matchedSession))
		{
			// Calculate amount changed
			int previousBalance = matchedSession.Credits;
			int amountChanged = newBalance - previousBalance;

			// Update session credits
			matchedSession.Credits = newBalance;

			// Emit signal with phone number (not player ID UUID)
			EmitSignal(SignalName.CreditsEarned, matchedSession.PhoneNumber, amountChanged, "Credit balance updated");
			LogInfo($"Credit change relayed for {matchedSession.UserName}: {previousBalance} -> {newBalance} ({amountChanged:+#;-#;0})");
		}
		else
		{
			// No active session found - log but don't emit (UI won't show anyway)
			LogInfo($"Credit changed for player {playerId} but no active session found (balance: {newBalance})");
		}
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

			// Create logout tasks for all active users
			var logoutTasks = new List<Task<bool>>();
			foreach (var phoneNumber in phoneNumbers)
			{
				logoutTasks.Add(LogoutUserAsync(phoneNumber));
			}

			// Wait for all logouts with 5-second timeout
			var allLogoutsTask = Task.WhenAll(logoutTasks);
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
			// Event-sourced persistence - authentication delegated to backend
			// For now, create session directly (backend validates via events)

			// Don't allow duplicate sessions for the same user
			if (_activeSessions.ContainsKey(cleanedPhone))
			{
				LogWarning($"User {cleanedPhone} already has an active session");
				return Result.Failure<UserSession>("User already logged in");
			}

			// Generate deterministic player ID from phone number
			var playerId = EventService.GetPlayerIdFromPhone(cleanedPhone);

			// Register player with backend (idempotent)
			if (_eventService != null && _eventService.IsReady)
			{
				var username = cleanedPhone.Substring(Math.Max(0, cleanedPhone.Length - 7)); // Last 7 digits
				var boxId = _eventService.GetCurrentBoxId();

				var registrationResult = await _eventService.RegisterPlayerFirstPlayAsync(playerId, username, boxId);

				if (registrationResult.IsFailure(out var regError))
				{
					// Only continue if error indicates player already exists
					if (regError.Message != null &&
					    (regError.Message.Contains("already exists") ||
					     regError.Message.Contains("409") ||
					     regError.Message.Contains("conflict", StringComparison.OrdinalIgnoreCase)))
					{
						LogInfo($"Player already registered (continuing with login): {playerId}");
					}
					else
					{
						LogError($"Player registration failed: {regError.Message}");
						return Result.Failure<UserSession>($"Unable to register player: {regError.Message}");
					}
				}
				else
				{
					LogInfo($"Player registered in backend: {playerId}");
				}
			}

			// Create local session
			var session = new UserSession
			{
				PhoneNumber = cleanedPhone,
				UserName = cleanedPhone.Substring(Math.Max(0, cleanedPhone.Length - 7)), // Last 7 digits as temp username
				LocationId = CurrentLocationId,
				PlayerId = playerId,
				LoginTime = DateTime.UtcNow,
				LastActivity = DateTime.UtcNow,
				Credits = 0 // Will be populated from backend below
			};

			// Fetch initial credit balance from backend BEFORE adding to active sessions
			// This prevents race condition where UI queries session before credits are loaded
			if (_creditService != null)
			{
				var balanceResult = await _creditService.GetBalanceAsync(playerId, forceRefresh: true);
				if (balanceResult.IsSuccess(out var balance))
				{
					session.Credits = balance;
					LogInfo($"Initial credit balance loaded: {session.Credits} credits for {session.UserName}");
				}
				else if (balanceResult.IsFailure(out var balanceError))
				{
					LogWarning($"Failed to fetch initial credit balance: {balanceError.Message}");
					// Keep Credits = 0 as fallback
				}
			}
			else
			{
				LogWarning("CreditService not available - credit balance will remain 0 until updated");
			}

			// Add session to active sessions AFTER initialization is complete
			_activeSessions[cleanedPhone] = session;
			_sessionsByPlayerId[playerId] = session; // Maintain secondary index

			// Create lobby session for user-scoped events
			if (_eventService != null && _eventService.IsReady)
			{
				var boxId = _eventService.GetCurrentBoxId();
				var lobbySessionResult = await _eventService.CreateLobbySessionAsync(boxId, playerId);

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
					var loginEventResult = await _eventService.EmitUserEventAsync(playerId, "user/login", payload);
					if (loginEventResult.IsFailure(out var loginError))
					{
						LogWarning($"Failed to emit login event: {loginError.Message}");
					}
				}
				else if (lobbySessionResult.IsFailure(out var lobbyError))
				{
					LogWarning($"Failed to create lobby session: {lobbyError.Message}");
					// Continue with login but warn that events won't be persisted
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

			// Generate deterministic player ID from phone number
			var playerId = EventService.GetPlayerIdFromPhone(cleanedPhone);

			// Get box ID from LocationManager
			var locationManager = LocationManager.GetAutoload();
			if (locationManager == null || !locationManager.IsConfigLoaded)
			{
				LogError("LocationManager not available for account creation");
				return Result.Failure<string>("Location service unavailable - please try again later");
			}

			var boxId = locationManager.BoxId;

			// Create player account via direct REST call to backend
			var result = await _eventService.CreatePlayerAsync(playerId, cleanedUsername, boxId);

			if (result.IsSuccess(out var _))
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
			// Generate deterministic player ID from phone number
			var playerId = EventService.GetPlayerIdFromPhone(cleanedPhone);

			// Get box ID from LocationManager
			var locationManager = LocationManager.GetAutoload();
			if (locationManager == null || !locationManager.IsConfigLoaded)
			{
				LogError("LocationManager not available for validation");
				return Result.Failure<PlayerValidationResponse>("Location service unavailable - please try again later");
			}

			var boxId = locationManager.BoxId;

			// Validate via backend
			var result = await _eventService.ValidatePlayerCreationAsync(playerId, cleanedUsername, boxId);

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
		if (!_activeSessions.TryGetValue(phoneNumber, out var session))
			return false;

		try
		{
			// Emit user/logout event to lobby session (before closing it)
			if (_eventService != null && _eventService.IsReady && session.LobbySessionId != Guid.Empty)
			{
				var payload = new
				{
					phone_number = phoneNumber,
					location_id = CurrentLocationId,
					session_duration = session.SessionDuration.TotalSeconds
				};
				var logoutResult = await _eventService.EmitUserEventAsync(session.PlayerId, "user/logout", payload);
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
	}

	/// <summary>
	/// Log out all users except the primary (earliest logged-in) user
	/// Used when exiting to main menu from multi-player games
	/// </summary>
	public async Task LogoutNonPrimaryUsersAsync()
	{
		var primarySession = GetPrimaryUserSession();
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
	/// Get active user session
	/// </summary>
	public UserSession GetUserSession(string phoneNumber)
	{
		return _activeSessions.GetValueOrDefault(phoneNumber);
	}

	/// <summary>
	/// Get primary user session (earliest logged-in user)
	/// Use for: UI display (TopMenuBar), single-user game contexts, auto-populate convenience
	/// Multi-user games: use GetUserSession(phoneNumber) for explicit targeting
	/// </summary>
	public UserSession GetPrimaryUserSession()
	{
		if (_activeSessions.Count == 0)
			return null;

		// Return the session with the earliest login time
		UserSession primarySession = null;
		DateTime earliestLoginTime = DateTime.MaxValue;

		foreach (var session in _activeSessions.Values)
		{
			if (session.LoginTime < earliestLoginTime)
			{
				earliestLoginTime = session.LoginTime;
				primarySession = session;
			}
		}

		return primarySession;
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
	public async Task<bool> AddGlobalCreditsAsync(string phoneNumber, int amount, string reason = "")
	{
		if (amount <= 0)
		{
			LogWarning("Cannot add non-positive credits");
			return false;
		}

		try
		{
			var playerId = EventService.GetPlayerIdFromPhone(phoneNumber);

			if (_eventService != null && _eventService.IsReady)
			{
				var result = await _eventService.AddCreditsAsync(playerId, amount, reason);
				if (result.IsSuccess(out var _))
				{
					LogInfo($"Added {amount} credits for {phoneNumber}");

					// Update local session cache if available
					if (_activeSessions.TryGetValue(phoneNumber, out var session))
					{
						session.Credits += amount;
					}

					EmitSignal(SignalName.CreditsEarned, phoneNumber, amount, reason);
					return true;
				}
				else if (result.IsFailure(out var error))
				{
					LogError($"Failed to add credits: {error.Message}");
					return false;
				}

				// Fallback - should not reach here due to exhaustive Result<T> pattern
				return false;
			}
			else
			{
				if (_eventService == null)
					LogWarning("EventService not available, cannot sync credits");
				else
					LogWarning("EventService not ready (backend not initialized), cannot sync credits");
				return false;
			}
		}
		catch (Exception ex)
		{
			LogError($"Error adding credits: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Transfer credits from user's account to machine game credits
	/// </summary>
	public async Task<bool> TransferCreditsToMachineAsync(string phoneNumber, string gameId, int amount, string reason = "")
	{
		if (amount <= 0) return false;

		if (!IsUserLoggedIn(phoneNumber))
		{
			LogWarning($"User {phoneNumber} not logged in for credit transfer");
			return false;
		}

		try
		{
			var playerId = EventService.GetPlayerIdFromPhone(phoneNumber);

			if (_eventService != null)
			{
				// Query current balance
				var balanceResult = await _eventService.GetPlayerCreditsAsync(playerId);
				if (balanceResult.IsFailure(out var _) || !balanceResult.IsSuccess(out var currentBalance) || currentBalance < amount)
				{
					if (balanceResult.IsSuccess(out var balance))
					{
						LogWarning($"Insufficient credits: has {balance}, needs {amount}");
					}
					return false;
				}

				// Spend credits from player account
				var spendResult = await _eventService.SpendCreditsAsync(playerId, amount, $"Transfer to {gameId}: {reason}");
				if (spendResult.IsSuccess(out var _))
				{
					LogInfo($"Transferred {amount} credits for {phoneNumber}");

					// Update local session cache if available
					if (_activeSessions.TryGetValue(phoneNumber, out var session))
					{
						session.Credits -= amount;

						// Deposit credits to machine pot (backend tracking)
						var locationManager = LocationManager.GetAutoload();
						if (locationManager != null && session.LobbySessionId != Guid.Empty)
						{
							var boxId = locationManager.BoxId;
							// Use "carrom" game tag (not "carrom_game" ID) to match backend queries
							var gameTag = gameId.Replace("_game", "");
							var depositResult = await _eventService.DepositMachineCreditsAsync(
								gameTag,
								boxId,
								playerId,
								amount,
								session.LobbySessionId
							);

							if (depositResult.IsFailure(out var depositError))
							{
								LogWarning($"Failed to deposit to machine pot: {depositError.Message}");
								// Continue anyway - player credits already deducted
							}
						}
					}

					EmitSignal(SignalName.CreditsSpent, phoneNumber, amount, $"Transfer to {gameId}: {reason}");
					return true;
				}
			}

			return false;
		}
		catch (Exception ex)
		{
			LogError($"Error transferring credits: {ex.Message}");
			return false;
		}
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
		GD.PushWarning("SessionManager: OnServiceDestroyed() called - graceful shutdown notification may have been missed");

		// Logout all users before shutdown (fallback path - fire and forget)
		var phoneNumbers = GetActivePhoneNumbers();
		foreach (var phoneNumber in phoneNumbers)
		{
			_ = LogoutUserAsync(phoneNumber); // Fire and forget for cleanup
		}

		_idleTimer?.Stop();
		_idleTimer?.QueueFree();

		// Disconnect from CreditService
		if (_creditService != null && GodotObject.IsInstanceValid(_creditService))
		{
			_creditService.CreditsChanged -= OnCreditsChanged;
		}
		_creditService = null;

		Instance = null;
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
	public int Credits { get; set; }

	public TimeSpan SessionDuration => DateTime.UtcNow - LoginTime;
	public TimeSpan IdleTime => DateTime.UtcNow - LastActivity;
}