using Godot;
using LightResults;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BarBox.Core.Utils;

namespace BarBox.Core.Autoloads;

/// <summary>
/// Event-based persistence service for BarBox backend integration
/// Forwards all game/session events to local BarBoxServices backend
/// </summary>
public partial class SessionEventService : AutoloadBase
{
	private const string NotReadyError = "SessionEventService not initialized - backend not ready";
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
			throw new InvalidOperationException("BackendClient not found - SessionEventService requires BackendClient");
		}

		LogInfo("Waiting for BackendClient to become ready...");
		var backendReady = await _backend.WaitForReadyAsync(timeoutSeconds: 30.0f, cancellationToken);

		if (!backendReady)
		{
			throw new InvalidOperationException("BackendClient failed to become ready within timeout");
		}

		LogInfo("SessionEventService ready (transport delegated to BackendClient)");
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
			var url = ApiPaths.Box.Session(boxId, sessionId, gameTag);

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
				LogError($"[SessionEventService] Activity session creation failed:");
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

			var url = ApiPaths.Box.CloseSession(sessionId);
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

			var url = ApiPaths.Box.LobbySession(boxId);
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
				LogError($"[SessionEventService] Lobby session creation failed:");
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
				LogError("[SessionEventService] Empty response body from successful lobby session creation (201)");
				return Result.Failure<Guid>("Empty response body from lobby session creation");
			}

			var response = JsonSerializer.Deserialize<Dictionary<string, object>>(bodyText);
			if (response != null && response.ContainsKey("id"))
			{
				var sessionId = Guid.Parse(response["id"].ToString());
				LogInfo($"Lobby session created: {sessionId}");
				return Result.Success(sessionId);
			}

			LogError($"[SessionEventService] Failed to parse lobby session ID from response: {bodyText}");
			return Result.Failure<Guid>($"Failed to parse lobby session ID from response: {bodyText}");
		}
		catch (Exception ex)
		{
			LogError($"[SessionEventService] Lobby session creation exception: {ex.Message}\nStack: {ex.StackTrace}");
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
			LogError($"[SessionEventService] Backend error response: {error}");
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
	/// Get the singleton instance
	/// </summary>
	public static SessionEventService GetInstance()
	{
		return GetAutoload<SessionEventService>();
	}

#if DEBUG
	// Test infrastructure for simulating service states in unit tests
	private bool _testReadyOverride = false;
	private bool _useTestReadyOverride = false;

	/// <summary>
	/// FOR TESTING ONLY: Override ready state to simulate backend failures.
	/// This allows tests to verify error handling when SessionEventService is not available.
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
	/// Ready state forwards to BackendClient - SessionEventService itself has no separate transport to be ready about.
	/// </summary>
	public new bool IsReady => _backend?.IsReady ?? false;
#endif
}
