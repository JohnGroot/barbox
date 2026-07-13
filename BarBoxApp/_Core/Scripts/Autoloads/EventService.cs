using Godot;
using LightResults;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BarBox.Core.Autoloads;

/// <summary>
/// Event-based persistence service for BarBox backend integration
/// Forwards all game/session events to local BarBoxServices backend
/// </summary>
public partial class EventService : AutoloadBase
{
	private const string NotReadyError = "EventService not initialized - backend not ready";
	private const string UnknownError = "Unknown error";

	private string _venueName;
	private string _boxName;
	private Guid _boxId;
	private Guid _currentSessionId;
	private Guid _currentBoxId;
	private BackendClient _backend;

	protected override async Task OnServiceInitializeAsync(CancellationToken cancellationToken = default)
	{
		var locationManager = LocationManager.GetAutoload();
		if (locationManager == null || !locationManager.IsConfigLoaded)
			throw new InvalidOperationException("LocationManager is required but not available or not initialized");

		_venueName = locationManager.VenueName;
		_boxName = locationManager.BoxName;
		_boxId = locationManager.BoxId;

		LogInfo($"Identity: venue={_venueName}, box={_boxName} ({_boxId})");

		_backend = BackendClient.GetInstance();
		if (_backend == null)
		{
			throw new InvalidOperationException("BackendClient not found - EventService requires BackendClient");
		}

		LogInfo("Waiting for BackendClient to become ready...");
		var backendReady = await _backend.WaitForReadyAsync(timeoutSeconds: 30.0f, cancellationToken);

		if (!backendReady)
		{
			throw new InvalidOperationException("BackendClient failed to become ready within timeout");
		}

		LogInfo("EventService ready (transport delegated to BackendClient)");
	}


	/// <summary>
	/// Create new activity session (game launch → exit) with game tag
	/// This is the NEW recommended method that replaces CreateSessionAsync
	/// </summary>
	/// <param name="boxId">Box identifier</param>
	/// <param name="playerId">Player identifier (host for multiplayer)</param>
	/// <param name="gameTag">Game identifier (e.g., "carrom", "racing", "mining")</param>
	/// <param name="playerIds">Optional: Array of all player IDs for multiplayer</param>
	/// <returns>Result with created session ID</returns>
	public async Task<Result<Guid>> CreateActivitySessionAsync(
		Guid boxId,
		Guid playerId,
		string gameTag,
		List<string> playerIds = null)
	{
		if (!IsReady)
			return Result.Failure<Guid>(NotReadyError);

		if (string.IsNullOrEmpty(gameTag))
			return Result.Failure<Guid>("game_tag is required");

		var sessionId = Guid.NewGuid();

		try
		{
#if DEBUG_HTTP_LIFECYCLE
			LogInfo($"[HTTP] CreateActivitySession - Starting for game '{gameTag}'");
#endif
			await _backend.EnsureConnectedAsync();

			// Build URL with required game_tag
			var url = $"/box/{boxId}/session/{sessionId}?game_tag={Uri.EscapeDataString(gameTag)}";

			// Add player_ids for multiplayer
			if (playerIds != null && playerIds.Count > 1)
			{
				var playerIdsJson = JsonSerializer.Serialize(playerIds);
				url += $"&player_ids={Uri.EscapeDataString(playerIdsJson)}";
			}

			var headers = _backend.BuildPlayerHeaders(playerId).ToArray();

#if DEBUG_HTTP_LIFECYCLE
			LogInfo($"[HTTP] CreateActivitySession - PUT {url}");
#endif

			var error = _backend.Request(
				HttpClient.Method.Put,
				url,
				headers
			);

			if (error != Godot.Error.Ok)
				return Result.Failure<Guid>($"Activity session creation request failed: {error}");

			await _backend.WaitForResponseAsync();

			var responseCode = _backend.GetResponseCode();

#if DEBUG_HTTP_LIFECYCLE
			LogInfo($"[HTTP] CreateActivitySession - Response code: {responseCode}");
#endif

			if (responseCode != 202)
			{
				// Read response body for detailed error information
				_backend.Poll();
				await DelayAsync(0.05f);
				_backend.Poll();

				var errorBodyBytes = _backend.ReadResponseBodyChunk();
				var errorBody = errorBodyBytes.GetStringFromUtf8();

				_backend.Close();

				// Provide detailed error logging to help diagnose authentication issues
				LogError($"[EventService] Activity session creation failed:");
				LogError($"  Status Code: {responseCode}");
				LogError($"  Endpoint: PUT {url}");
				LogError($"  Box ID: {boxId}");
				LogError($"  Player ID: {playerId}");
				LogError($"  Game Tag: {gameTag}");

				if (!string.IsNullOrEmpty(errorBody))
				{
					LogError($"  Response Body: {errorBody}");
				}

				// Provide helpful hints based on status code
				if (responseCode == 404)
				{
					LogError($"  [HINT] 404 usually means the box is not registered in the database.");
					LogError($"  [HINT] Check that RegisterBoxAsync was called before session creation.");
				}
				else if (responseCode == 401 || responseCode == 403)
				{
					LogError($"  [HINT] Authentication failed - verify API key is correct for this box.");
				}

				return Result.Failure<Guid>($"Activity session creation failed with code {responseCode}: {errorBody}");
			}

			_currentSessionId = sessionId;
			_currentBoxId = boxId;

			LogInfo($"Activity session created: {sessionId} (game: {gameTag})");

			return Result.Success(sessionId);
		}
		catch (Exception ex)
		{
#if DEBUG_HTTP_LIFECYCLE
			LogError($"[HTTP] CreateActivitySession - Exception: {ex.Message}\nStack Trace: {ex.StackTrace}");
#endif

			_backend.Close();
			return Result.Failure<Guid>($"Activity session creation exception: {ex.Message}");
		}
	}

	/// <summary>
	/// Close an activity session (game exit)
	/// Call this when game ends to properly set end_time on backend
	/// </summary>
	/// <param name="sessionId">Session identifier to close</param>
	/// <returns>Result indicating success or failure</returns>
	public async Task<Result<bool>> CloseActivitySessionAsync(Guid sessionId)
	{
		if (!IsReady)
			return Result.Failure<bool>(NotReadyError);

		try
		{
#if DEBUG_HTTP_LIFECYCLE
			LogInfo($"[HTTP] CloseActivitySession - Closing session {sessionId}");
#endif

			await _backend.EnsureConnectedAsync();

			var url = $"/box/session/{sessionId}/close";
			var headers = _backend.BuildHeaders().ToArray();

#if DEBUG_HTTP_LIFECYCLE
			LogInfo($"[HTTP] CloseActivitySession - POST {url}");
#endif

			var error = _backend.Request(
				HttpClient.Method.Post,
				url,
				headers
			);

			if (error != Godot.Error.Ok)
				return Result.Failure<bool>($"Session close request failed: {error}");

			await _backend.WaitForResponseAsync();

			var responseCode = _backend.GetResponseCode();

#if DEBUG_HTTP_LIFECYCLE
			LogInfo($"[HTTP] CloseActivitySession - Response code: {responseCode}");
#endif

			if (responseCode != 200)
			{
				_backend.Close();
				return Result.Failure<bool>($"Session close failed with code {responseCode}");
			}

			// Clear current session if it was the one we just closed
			if (_currentSessionId == sessionId)
			{
				_currentSessionId = Guid.Empty;
			}

			LogInfo($"Activity session closed: {sessionId}");

			return Result.Success(true);
		}
		catch (Exception ex)
		{
#if DEBUG_HTTP_LIFECYCLE
			LogError($"[HTTP] CloseActivitySession - Exception: {ex.Message}");
#endif

			_backend.Close();
			return Result.Failure<bool>($"Session close exception: {ex.Message}");
		}
	}

	/// <summary>
	/// Create a lobby session for a logged-in player
	/// Lobby sessions are long-lived and receive user-scoped events (credit, login, logout)
	/// </summary>
	/// <param name="boxId">Box identifier</param>
	/// <param name="playerId">Player identifier</param>
	/// <returns>Result with created lobby session ID</returns>
	public async Task<Result<Guid>> CreateLobbySessionAsync(Guid boxId, Guid playerId)
	{
		if (!IsReady)
			return Result.Failure<Guid>(NotReadyError);

		try
		{
#if DEBUG_HTTP_LIFECYCLE
			LogInfo($"[HTTP] CreateLobbySession - Starting for player {playerId}");
#endif

			await _backend.EnsureConnectedAsync();

			var url = $"/box/{boxId}/lobby/session";
			var headers = _backend.BuildHeaders(playerId);
			headers.Add($"Player-Id: {playerId}");

#if DEBUG_HTTP_LIFECYCLE
			LogInfo($"[HTTP] CreateLobbySession - POST {url}");
#endif

			var error = _backend.Request(
				HttpClient.Method.Post,
				url,
				headers.ToArray()
			);

			if (error != Godot.Error.Ok)
				return Result.Failure<Guid>($"Lobby session creation request failed: {error}");

			await _backend.WaitForResponseAsync();

			var responseCode = _backend.GetResponseCode();

#if DEBUG_HTTP_LIFECYCLE
			LogInfo($"[HTTP] CreateLobbySession - Response code: {responseCode}");
#endif

			if (responseCode != 201)
			{
				// CRITICAL: Read error response body to understand backend error
				var errorBodyBytes = await _backend.ReadResponseBodyAsync();
				var errorBody = errorBodyBytes.GetStringFromUtf8();

				_backend.Close();

				// Log full error details for debugging
				LogError($"[EventService] Lobby session creation failed:");
				LogError($"  Endpoint: POST /box/{{boxId}}/lobby/session");
				LogError($"  Status Code: {responseCode}");
				LogError($"  Response Body: {errorBody}");
				LogError($"  Player ID: {playerId}");

				return Result.Failure<Guid>($"Lobby session creation failed with code {responseCode}: {errorBody}");
			}

			// Parse response to get session ID
			// Poll to ensure body is fully received
			_backend.Poll();
			await DelayAsync(0.05f);
			_backend.Poll();

			var bodyBytes = await _backend.ReadResponseBodyAsync();
			var bodyText = bodyBytes.GetStringFromUtf8();

			if (string.IsNullOrEmpty(bodyText))
			{
				LogError("[EventService] Empty response body from successful lobby session creation (201)");
				return Result.Failure<Guid>("Empty response body from lobby session creation");
			}

			var response = JsonSerializer.Deserialize<Dictionary<string, object>>(bodyText);
			if (response != null && response.ContainsKey("id"))
			{
				var sessionId = Guid.Parse(response["id"].ToString());
				LogInfo($"Lobby session created: {sessionId}");
				return Result.Success(sessionId);
			}

			LogError($"[EventService] Failed to parse lobby session ID from response: {bodyText}");
			return Result.Failure<Guid>($"Failed to parse lobby session ID from response: {bodyText}");
		}
		catch (Exception ex)
		{
			LogError($"[EventService] Lobby session creation exception: {ex.Message}\nStack: {ex.StackTrace}");
			_backend.Close();
			return Result.Failure<Guid>($"Lobby session creation exception: {ex.Message}");
		}
	}

	/// <summary>
	/// Submit gameplay event to backend
	/// Backend handles event persistence and queuing for remote sync
	/// </summary>
	public async Task<Result<bool>> EmitEventAsync(string eventType, object payload)
	{
		if (!IsReady)
			return Result.Failure<bool>(NotReadyError);

		if (_currentSessionId == Guid.Empty)
			return Result.Failure<bool>("No active session - call CreateSessionAsync first");

		try
		{
			var eventData = new
			{
				type = eventType,
				timestamp = DateTime.UtcNow.ToString("O"),
				payload = payload
			};

			var json = JsonSerializer.Serialize(eventData, BackendClient.JsonOptions);

			var result = await _backend.PostToSessionAsync(_currentSessionId, json);
			if (result.IsSuccess(out _))
				return Result.Success(true);

			var error = result.IsFailure(out var e) ? e.Message : UnknownError;
			LogError($"[EventService] Backend error response: {error}");
			return Result.Failure<bool>($"Event submission failed: {error}");
		}
		catch (Exception ex)
		{
			var errorMsg = $"Event submission exception: {ex.Message}";
			LogError(errorMsg);
			return Result.Failure<bool>(errorMsg);
		}
	}

	/// <summary>
	/// Submit user-level event to backend (login, logout, credit purchases)
	/// Posts events to the specified lobby session
	/// </summary>
	/// <param name="lobbySessionId">Lobby session ID (from UserSession.LobbySessionId)</param>
	public async Task<Result<bool>> EmitUserEventAsync(Guid playerId, string eventType, object payload, Guid lobbySessionId)
	{
		if (!IsReady)
			return Result.Failure<bool>(NotReadyError);

		if (lobbySessionId == Guid.Empty)
		{
			return Result.Failure<bool>("Invalid lobby session ID - ensure player has an active lobby session");
		}

		var sessionId = lobbySessionId;

		try
		{
#if DEBUG_HTTP_LIFECYCLE
			LogInfo($"[HTTP] EmitUserEvent - {eventType} to lobby session {sessionId}");
#endif

			var payloadJson = JsonSerializer.Serialize(payload, BackendClient.JsonOptions);
			var eventJson = JsonSerializer.Serialize(new
			{
				type = eventType,
				payload = JsonSerializer.Deserialize<Dictionary<string, object>>(payloadJson)
			}, BackendClient.JsonOptions);

			// NOTE: playerId is intentionally NOT passed to PostToSessionAsync here - this
			// preserves existing behavior where user events never attach a JWT (see BuildHeaders()
			// call with no playerId in the pre-split code this replaces).
			var result = await _backend.PostToSessionAsync(sessionId, eventJson);
			if (result.IsSuccess(out _))
			{
				LogInfo($"User event submitted: {eventType} to lobby session {sessionId}");
				return Result.Success(true);
			}

			var error = result.IsFailure(out var e) ? e.Message : UnknownError;
			return Result.Failure<bool>($"User event submission failed: {error}");
		}
		catch (Exception ex)
		{
			var errorMsg = $"User event submission exception: {ex.Message}";
			LogError(errorMsg);
			return Result.Failure<bool>(errorMsg);
		}
	}

	/// <summary>
	/// Get current active session ID
	/// </summary>
	public Guid GetCurrentSessionId() => _currentSessionId;

	/// <summary>
	/// Get venue name (human-readable location identifier for location-scoped data)
	/// </summary>
	public string GetVenueName() => _venueName;

	/// <summary>
	/// Get box name (human-readable machine name for display and logging)
	/// </summary>
	public string GetBoxName() => _boxName;

	/// <summary>
	/// Get box ID (UUID for backend security - rarely needed by games)
	/// </summary>
	public Guid GetBoxId() => _boxId;

	/// <summary>
	/// Execute GET query against backend API
	/// </summary>
	/// <param name="playerId">Optional. If provided, includes player JWT token in Authorization header.</param>
	public Task<Result<TResponse>> QueryAsync<TResponse>(
		string endpoint,
		Dictionary<string, string> queryParams = null,
		Guid? playerId = null
	) where TResponse : class
		=> _backend.QueryAsync<TResponse>(endpoint, queryParams, playerId);

	/// <summary>
	/// Execute GET query against backend API, returning raw JSON string
	/// Used by game event services for custom JSON parsing
	/// </summary>
	/// <param name="playerId">Optional. If provided, includes player JWT token in Authorization header.</param>
	public Task<Result<string>> QueryRawAsync(
		string endpoint,
		Dictionary<string, string> queryParams = null,
		Guid? playerId = null
	) => _backend.QueryRawAsync(endpoint, queryParams, playerId);

	/// <summary>
	/// Execute POST request against backend API with JSON body
	/// </summary>
	/// <param name="playerId">Optional. If provided, includes player JWT token in Authorization header.</param>
	public Task<Result<TResponse>> PostAsync<TRequest, TResponse>(
		string endpoint,
		TRequest requestBody,
		int expectedStatusCode = 201,
		Guid? playerId = null
	) where TResponse : class
		=> _backend.PostAsync<TRequest, TResponse>(endpoint, requestBody, expectedStatusCode, playerId);

	/// <summary>
	/// Check if username is available for registration
	/// </summary>
	public async Task<Result<bool>> IsUsernameAvailableAsync(string username)
	{
		var result = await QueryAsync<UsernameAvailabilityResponse>(
			$"/player/username/{Uri.EscapeDataString(username)}/available",
			null
		);

		if (result.IsSuccess(out var response))
			return Result.Success(response.IsAvailable);

		if (result.IsFailure(out var error))
			return Result.Failure<bool>(error.Message);

		return Result.Failure<bool>(UnknownError);
	}

	/// <summary>
	/// Validate player creation without actually creating the player (pre-flight check)
	/// Performs all validation checks: box exists, username available, player ID unique
	/// </summary>
	public async Task<Result<PlayerValidationResponse>> ValidatePlayerCreationAsync(Guid playerId, string username, string phoneNumber, string pin, Guid originBoxId)
	{
		var request = new PlayerCreateRequest
		{
			Id = playerId.ToString(),
			Tag = username,
			PhoneNumber = phoneNumber,
			Pin = pin,
			OriginId = originBoxId.ToString()
		};

		var result = await PostAsync<PlayerCreateRequest, PlayerValidationResponse>("/player/validate", request, 200);

		if (result.IsSuccess(out var response))
		{
			if (!response.Valid)
			{
				// Validation failed - construct error message from validation errors
				var errorMessages = new StringBuilder();
				errorMessages.AppendLine("Validation failed:");
				foreach (var validationError in response.Errors)
				{
					errorMessages.AppendLine($"  - {validationError.Field}: {validationError.Message}");
				}
				LogWarning($"Player validation failed: {errorMessages}");
			}
			return Result.Success(response);
		}

		if (result.IsFailure(out var error))
		{
			LogError($"Failed to validate player creation: {error.Message}");
			return Result.Failure<PlayerValidationResponse>(error.Message);
		}

		return Result.Failure<PlayerValidationResponse>(UnknownError);
	}

	/// <summary>
	/// Create a new player account via backend API
	/// </summary>
	public async Task<Result<Guid>> CreatePlayerAsync(Guid playerId, string username, string phoneNumber, string pin, Guid originBoxId)
	{
		var request = new PlayerCreateRequest
		{
			Id = playerId.ToString(),
			Tag = username,
			PhoneNumber = phoneNumber,
			Pin = pin,
			OriginId = originBoxId.ToString()
		};

		var result = await PostAsync<PlayerCreateRequest, PlayerDetailResponse>("/player/", request, 201);

		if (result.IsSuccess(out var _))
		{
			LogInfo($"Player created successfully: {username} ({playerId})");
			return Result.Success(playerId);
		}

		if (result.IsFailure(out var error))
		{
			LogError($"Failed to create player: {error.Message}");
			return Result.Failure<Guid>(error.Message);
		}

		return Result.Failure<Guid>(UnknownError);
	}

	/// <summary>
	/// Register or verify box with backend, returning full response including API key on first registration
	/// Idempotent - safe to call multiple times
	/// Returns BoxDetailResponse with ApiKey field populated ONLY on first-time registration
	/// </summary>
	public async Task<Result<BoxDetailResponse>> RegisterBoxWithDetailAsync(Guid boxId, string locationName)
	{
		var request = new BoxCreateRequest
		{
			Id = boxId.ToString(),
			Name = locationName,
			Tag = locationName
		};

		// Use PUT for idempotent box registration
		var result = await _backend.PutAsync<BoxCreateRequest, BoxDetailResponse>(
			$"/box/{boxId}",
			request,
			200
		);

		if (result.IsSuccess(out var response))
		{
			// API key is always returned now - mask it for logging
			var maskedKey = !string.IsNullOrEmpty(response.ApiKey) && response.ApiKey.Length > 12
				? $"{response.ApiKey[..8]}...{response.ApiKey[^4..]}"
				: "***";

			// Detect first-time registration vs re-registration via warning message
			var isFirstRegistration = !string.IsNullOrEmpty(response.Warning)
				&& response.Warning.StartsWith("Save", StringComparison.OrdinalIgnoreCase);

			if (isFirstRegistration)
			{
				LogInfo($"Box registered (first time): {locationName} ({boxId}), key: {maskedKey}");
			}
			else
			{
				LogInfo($"Box verified: {locationName} ({boxId}), key: {maskedKey}");

				// Log warning if name/tag mismatch was detected
				if (!string.IsNullOrEmpty(response.Warning) && response.Warning.Contains("different"))
				{
					LogWarning($"Box registration note: {response.Warning}");
				}
			}

			return Result.Success(response);
		}

		if (result.IsFailure(out var error))
		{
			LogError($"Box registration failed: {error.Message}");
			return Result.Failure<BoxDetailResponse>(error.Message);
		}

		return Result.Failure<BoxDetailResponse>("Unknown error registering box");
	}

	/// <summary>
	/// Register box (physical terminal) with backend
	/// Idempotent - safe to call multiple times
	/// Backward compatibility wrapper - use RegisterBoxWithDetailAsync for full response
	/// </summary>
	public async Task<Result<Guid>> RegisterBoxAsync(Guid boxId, string locationName)
	{
		var result = await RegisterBoxWithDetailAsync(boxId, locationName);

		if (result.IsSuccess(out var detail))
			return Result.Success(Guid.Parse(detail.Id));

		if (result.IsFailure(out var error))
			return Result.Failure<Guid>(error.Message);

		return Result.Failure<Guid>(UnknownError);
	}

	/// <summary>
	/// Get player's credit balance for current location
	/// </summary>
	public async Task<Result<int>> GetPlayerCreditsAsync(Guid playerId)
	{
		var locationId = GetVenueName();
		var result = await QueryAsync<PlayerCreditsResponse>(
			$"/player/{playerId}/credits",
			new Dictionary<string, string> { { "location_id", locationId } }
		);

		if (result.IsSuccess(out var creditsResponse))
			return Result.Success(creditsResponse.Credits);

		if (result.IsFailure(out var error))
			return Result.Failure<int>(error.Message);

		return Result.Failure<int>("Unknown error getting player credits");
	}

	/// <summary>
	/// Emit credit earn event to backend
	/// Uses user-scoped events - does not require ActivitySession
	/// </summary>
	/// <param name="lobbySessionId">Lobby session ID (from UserSession.LobbySessionId)</param>
	public async Task<Result<bool>> AddCreditsAsync(Guid playerId, int amount, string reason, Guid lobbySessionId)
	{
		var payload = new
		{
			location_id = GetVenueName(),
			amount = amount,
			reason = reason
		};

		return await EmitUserEventAsync(playerId, "credit/earn", payload, lobbySessionId);
	}

	/// <summary>
	/// Emit credit spend event to backend
	/// Uses user-scoped events - does not require ActivitySession
	/// </summary>
	/// <param name="lobbySessionId">Lobby session ID (from UserSession.LobbySessionId)</param>
	public async Task<Result<bool>> SpendCreditsAsync(Guid playerId, int amount, string reason, Guid lobbySessionId)
	{
		var payload = new
		{
			location_id = GetVenueName(),
			amount = amount,
			reason = reason
		};

		return await EmitUserEventAsync(playerId, "credit/spend", payload, lobbySessionId);
	}

	/// <summary>
	/// Create a Stripe Checkout Session for credit purchase
	/// Returns session URL for QR code display
	/// </summary>
	/// <param name="packId">Credit pack identifier (e.g., "pack_25")</param>
	/// <param name="playerId">Player making the purchase (requires JWT authentication)</param>
	public async Task<Result<CheckoutSessionResponse>> CreateCheckoutSessionAsync(string packId, Guid playerId)
	{
		var request = new CheckoutSessionRequest { PackId = packId };

		var result = await PostAsync<CheckoutSessionRequest, CheckoutSessionResponse>(
			"/payments/checkout/create",
			request,
			201,
			playerId: playerId
		);

		if (result.IsSuccess(out var response))
		{
			var validation = response.ValidateRequired();
			if (validation.IsFailure(out var validationError))
			{
				return Result.Failure<CheckoutSessionResponse>(validationError.Message);
			}

			LogInfo($"Checkout session created: {response.SessionId}");
			return Result.Success(response);
		}

		if (result.IsFailure(out var error))
		{
			LogError($"Failed to create checkout session: {error.Message}");
			return Result.Failure<CheckoutSessionResponse>(error.Message);
		}

		return Result.Failure<CheckoutSessionResponse>("Unknown error creating checkout session");
	}

	/// <summary>
	/// Check payment completion status by Stripe session ID.
	/// More efficient than polling credit balance (single indexed lookup vs full aggregation).
	/// </summary>
	/// <param name="sessionId">Stripe Checkout Session ID</param>
	/// <returns>Status response with "pending", "completed", or "failed" status</returns>
	public async Task<Result<CheckoutStatusResponse>> GetCheckoutStatusAsync(string sessionId)
	{
		var result = await QueryAsync<CheckoutStatusResponse>($"/payments/checkout/{sessionId}/status");

		if (result.IsSuccess(out var response))
		{
			return Result.Success(response);
		}

		if (result.IsFailure(out var error))
		{
			LogError($"Failed to get checkout status: {error.Message}");
			return Result.Failure<CheckoutStatusResponse>(error.Message);
		}

		return Result.Failure<CheckoutStatusResponse>("Unknown error getting checkout status");
	}

	/// <summary>
	/// Get machine credit pot balance and player contributions
	/// </summary>
	public async Task<Result<MachineCreditsResponse>> GetMachineCreditsAsync(string gameTag, Guid boxId)
	{
		var queryParams = new Dictionary<string, string>
		{
			{ "box_id", boxId.ToString() }
		};

		return await QueryAsync<MachineCreditsResponse>(
			$"/machine-credits/{gameTag}",
			queryParams
		);
	}

	/// <summary>
	/// Deposit credits to machine pot (from player account)
	/// </summary>
	public async Task<Result<MachineCreditsResponse>> DepositMachineCreditsAsync(
		string gameTag,
		Guid boxId,
		Guid playerId,
		int amount,
		Guid lobbySessionId)
	{
		var request = new MachineCreditsDepositRequest
		{
			BoxId = boxId,
			PlayerId = playerId,
			Amount = amount,
			LobbySessionId = lobbySessionId
		};
		return await PostAsync<MachineCreditsDepositRequest, MachineCreditsResponse>(
			$"/machine-credits/{gameTag}/deposit", request, 201, playerId: playerId);
	}

	/// <summary>
	/// Consume credits from machine pot (for game start)
	/// </summary>
	public async Task<Result<MachineCreditsResponse>> ConsumeMachineCreditsAsync(
		string gameTag,
		Guid boxId,
		int amount,
		Guid gameSessionId)
	{
		var request = new MachineCreditsConsumeRequest
		{
			BoxId = boxId,
			Amount = amount,
			GameSessionId = gameSessionId
		};
		return await PostAsync<MachineCreditsConsumeRequest, MachineCreditsResponse>(
			$"/machine-credits/{gameTag}/consume", request, 200);
	}

	/// <summary>
	/// Get player ID from phone number by looking up active session
	/// This is a compatibility helper for the phone-based auth system
	/// </summary>
	public static Guid GetPlayerIdFromPhone(string phoneNumber)
	{
		var sessionManager = SessionManager.GetInstance();
		if (sessionManager == null)
		{
			GD.PrintErr("[EventService] SessionManager not available - cannot get player ID from phone");
			return Guid.Empty;
		}

		var session = sessionManager.GetSessionByPhone(phoneNumber);
		if (session == null)
		{
			GD.PrintErr($"[EventService] No active session found for phone: {phoneNumber}");
			return Guid.Empty;
		}

		return session.PlayerId;
	}

	/// <summary>
	/// Get the singleton instance
	/// </summary>
	public static EventService GetInstance()
	{
		return GetAutoload<EventService>();
	}

#if DEBUG
	// Test infrastructure for simulating service states in unit tests
	private bool _testReadyOverride = false;
	private bool _useTestReadyOverride = false;

	/// <summary>
	/// FOR TESTING ONLY: Override ready state to simulate backend failures.
	/// This allows tests to verify error handling when EventService is not available.
	/// </summary>
	/// <param name="ready">The ready state to simulate (true = ready, false = not ready)</param>
	public void SetReadyStateForTesting(bool ready)
	{
		_testReadyOverride = ready;
		_useTestReadyOverride = true;
		LogInfo($"[TEST] Ready state override set to: {ready}");
	}

	/// <summary>
	/// FOR TESTING ONLY: Clear the ready state override and return to normal operation.
	/// </summary>
	public void ClearReadyStateOverride()
	{
		_useTestReadyOverride = false;
		LogInfo("[TEST] Ready state override cleared");
	}

	/// <summary>
	/// Ready state now forwards to BackendClient (the actual transport) - the test
	/// override takes priority over that forwarded value when active.
	/// </summary>
	public new bool IsReady => _useTestReadyOverride ? _testReadyOverride : (_backend?.IsReady ?? false);
#else
	/// <summary>
	/// Ready state forwards to BackendClient - EventService itself has no separate transport to be ready about.
	/// </summary>
	public new bool IsReady => _backend?.IsReady ?? false;
#endif
}
