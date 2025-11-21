using Godot;
using LightResults;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace BarBox.Core.Autoloads;

/// <summary>
/// Event-based persistence service for BarBox backend integration
/// Forwards all game/session events to local BarBoxServices backend
/// </summary>
public partial class EventService : AutoloadBase
{
	// Configuration with environment variable support (matches BackendManager)
	// Supports two configuration modes:
	// 1. Full URL: BARBOX_BACKEND_URL="http://127.0.0.1:8000" (preferred)
	// 2. Host+Port: BARBOX_BACKEND_HOST="127.0.0.1" + BARBOX_BACKEND_PORT="8000" (fallback)
	private static readonly string BACKEND_HOST =
		System.Environment.GetEnvironmentVariable("BARBOX_BACKEND_HOST") ?? "127.0.0.1";

	private static readonly int BACKEND_PORT =
		int.TryParse(System.Environment.GetEnvironmentVariable("BARBOX_BACKEND_PORT"), out var port)
			? port
			: 8000;

	// Allow full URL override for deployment flexibility (supports http:// or https://)
	private static readonly string BASE_URL =
		System.Environment.GetEnvironmentVariable("BARBOX_BACKEND_URL")
		?? $"http://{BACKEND_HOST}:{BACKEND_PORT}";

	// Detect HTTPS from URL scheme (not from port or environment flags)
	private static readonly bool USE_HTTPS = BASE_URL.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

	// Deprecation warning for old BARBOX_USE_HTTPS environment variable
	static EventService()
	{
		var oldEnvVar = System.Environment.GetEnvironmentVariable("BARBOX_USE_HTTPS");
		if (oldEnvVar != null)
		{
			GD.PrintErr("[EventService] WARNING: BARBOX_USE_HTTPS environment variable is deprecated.");
			GD.PrintErr("[EventService] Use BARBOX_BACKEND_URL instead (e.g., BARBOX_BACKEND_URL=\"https://api.barbox.com\")");
		}
	}

	private const int REQUEST_TIMEOUT_MS = 5000;
	private const int POLL_INTERVAL_MS = 10;

	// Enable detailed HTTP lifecycle logging for debugging connection issues
	// To enable: Add DEBUG_HTTP_LIFECYCLE to project defines or use #define DEBUG_HTTP_LIFECYCLE at top of file

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	private HttpClient _httpClient;
	private string _venueName;
	private string _boxName;
	private Guid _boxId;
	private string _boxApiKey;
	private Guid _currentSessionId;
	private Guid _currentBoxId;

	[Signal] public delegate void EventSubmittedEventHandler(string sessionId, string eventType);
	[Signal] public delegate void EventSubmissionFailedEventHandler(string sessionId, string error);
	[Signal] public delegate void SessionCreatedEventHandler(string sessionId);

	protected override async Task OnServiceInitializeAsync(CancellationToken cancellationToken = default)
	{
		_httpClient = new HttpClient();

		// Load box identity from LocationManager
		var locationManager = LocationManager.GetAutoload();
		if (locationManager == null || !locationManager.IsConfigLoaded)
			throw new InvalidOperationException("LocationManager is required but not available or not initialized");

		_venueName = locationManager.VenueName;
		_boxName = locationManager.BoxName;

		try
		{
			_boxId = locationManager.BoxId;
			_boxApiKey = GetBoxApiKey();
			LogInfo($"Box ID: {_boxId}, API Key: {(string.IsNullOrEmpty(_boxApiKey) ? "NOT SET" : "SET")}");

			// FAIL LOUDLY in editor mode if API key missing
			if (string.IsNullOrEmpty(_boxApiKey) && OS.HasFeature("editor"))
			{
				LogError("╔════════════════════════════════════════════════════════════╗");
				LogError("║ CRITICAL: EventService API Key Configuration Missing!     ║");
				LogError("║                                                            ║");
				LogError("║ HTTP requests to backend will fail with 401/403 errors!   ║");
				LogError("║                                                            ║");
				LogError("║ Fix: Add to .env.local file:                               ║");
				LogError("║   BARBOX_API_KEY=<api-key>                                ║");
				LogError("║                                                            ║");
				LogError("║ Get key: curl -X POST http://127.0.0.1:8000/test/seed     ║");
				LogError("╚════════════════════════════════════════════════════════════╝");
			}
		}
		catch (Exception ex)
		{
			LogError($"Failed to get Box configuration: {ex.Message}");
			throw new InvalidOperationException($"EventService requires valid Box configuration: {ex.Message}\nStack Trace: {ex.StackTrace}");
		}

		// Wait for BackendManager to be ready
		var backendManager = BackendManager.GetInstance();
		if (backendManager == null)
		{
			throw new InvalidOperationException("BackendManager not found - EventService requires BackendManager");
		}

		LogInfo("Waiting for BackendManager to become ready...");
		var backendReady = await backendManager.WaitForReadyAsync(timeoutSeconds: 30.0f, cancellationToken);

		if (!backendReady)
		{
			throw new InvalidOperationException("BackendManager failed to become ready within timeout");
		}

		LogInfo($"EventService ready - backend available at {BASE_URL}");
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
			return Result.Failure<Guid>("EventService not initialized - backend not ready");

		if (string.IsNullOrEmpty(gameTag))
			return Result.Failure<Guid>("game_tag is required");

		var sessionId = Guid.NewGuid();

		try
		{
#if DEBUG_HTTP_LIFECYCLE
			LogInfo($"[HTTP] CreateActivitySession - Starting for game '{gameTag}'");
#endif
			await EnsureConnectedAsync();

			// Build URL with required game_tag
			var url = $"/box/{boxId}/session/{sessionId}?game_tag={Uri.EscapeDataString(gameTag)}";

			// Add player_ids for multiplayer
			if (playerIds != null && playerIds.Count > 1)
			{
				var playerIdsJson = JsonSerializer.Serialize(playerIds);
				url += $"&player_ids={Uri.EscapeDataString(playerIdsJson)}";
			}

			var headers = BuildPlayerHeaders(playerId).ToArray();

#if DEBUG_HTTP_LIFECYCLE
			LogInfo($"[HTTP] CreateActivitySession - PUT {url}");
#endif

			var error = _httpClient.Request(
				HttpClient.Method.Put,
				url,
				headers
			);

			if (error != Godot.Error.Ok)
				return Result.Failure<Guid>($"Activity session creation request failed: {error}");

			await WaitForResponseAsync();

			var responseCode = _httpClient.GetResponseCode();

#if DEBUG_HTTP_LIFECYCLE
			LogInfo($"[HTTP] CreateActivitySession - Response code: {responseCode}");
#endif

			if (responseCode != 202)
			{
				// Read response body for detailed error information
				_httpClient.Poll();
				await DelayAsync(0.05f);
				_httpClient.Poll();

				var errorBodyBytes = _httpClient.ReadResponseBodyChunk();
				var errorBody = errorBodyBytes.GetStringFromUtf8();

				_httpClient.Close();

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
			CallDeferred(GodotObject.MethodName.EmitSignal, SignalName.SessionCreated, sessionId.ToString());

			return Result.Success(sessionId);
		}
		catch (Exception ex)
		{
#if DEBUG_HTTP_LIFECYCLE
			LogError($"[HTTP] CreateActivitySession - Exception: {ex.Message}\nStack Trace: {ex.StackTrace}");
#endif

			_httpClient.Close();
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
			return Result.Failure<bool>("EventService not initialized - backend not ready");

		try
		{
#if DEBUG_HTTP_LIFECYCLE
			LogInfo($"[HTTP] CloseActivitySession - Closing session {sessionId}");
#endif

			await EnsureConnectedAsync();

			var url = $"/box/session/{sessionId}/close";
			var headers = BuildHeaders().ToArray();

#if DEBUG_HTTP_LIFECYCLE
			LogInfo($"[HTTP] CloseActivitySession - POST {url}");
#endif

			var error = _httpClient.Request(
				HttpClient.Method.Post,
				url,
				headers
			);

			if (error != Godot.Error.Ok)
				return Result.Failure<bool>($"Session close request failed: {error}");

			await WaitForResponseAsync();

			var responseCode = _httpClient.GetResponseCode();

#if DEBUG_HTTP_LIFECYCLE
			LogInfo($"[HTTP] CloseActivitySession - Response code: {responseCode}");
#endif

			if (responseCode != 200)
			{
				_httpClient.Close();
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

			_httpClient.Close();
			return Result.Failure<bool>($"Session close exception: {ex.Message}");
		}
	}

	private Guid _lobbySessionId = Guid.Empty;

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
			return Result.Failure<Guid>("EventService not initialized - backend not ready");

		try
		{
#if DEBUG_HTTP_LIFECYCLE
			LogInfo($"[HTTP] CreateLobbySession - Starting for player {playerId}");
#endif

			await EnsureConnectedAsync();

			var url = $"/box/{boxId}/lobby/session";
			var headers = BuildHeaders(playerId);
			headers.Add($"Player-Id: {playerId}");

#if DEBUG_HTTP_LIFECYCLE
			LogInfo($"[HTTP] CreateLobbySession - POST {url}");
#endif

			var error = _httpClient.Request(
				HttpClient.Method.Post,
				url,
				headers.ToArray()
			);

			if (error != Godot.Error.Ok)
				return Result.Failure<Guid>($"Lobby session creation request failed: {error}");

			await WaitForResponseAsync();

			var responseCode = _httpClient.GetResponseCode();

#if DEBUG_HTTP_LIFECYCLE
			LogInfo($"[HTTP] CreateLobbySession - Response code: {responseCode}");
#endif

			if (responseCode != 201)
			{
				// CRITICAL: Read error response body to understand backend error
				var errorBodyBytes = await _httpClient.ReadResponseBodyAsync();
				var errorBody = errorBodyBytes.GetStringFromUtf8();

				_httpClient.Close();

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
			_httpClient.Poll();
			await DelayAsync(0.05f);
			_httpClient.Poll();

			var bodyBytes = await _httpClient.ReadResponseBodyAsync();
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
				_lobbySessionId = sessionId;

				LogInfo($"Lobby session created: {sessionId}");
				CallDeferred(GodotObject.MethodName.EmitSignal, SignalName.SessionCreated, sessionId.ToString());
				return Result.Success(sessionId);
			}

			LogError($"[EventService] Failed to parse lobby session ID from response: {bodyText}");
			return Result.Failure<Guid>($"Failed to parse lobby session ID from response: {bodyText}");
		}
		catch (Exception ex)
		{
			LogError($"[EventService] Lobby session creation exception: {ex.Message}\nStack: {ex.StackTrace}");
			_httpClient.Close();
			return Result.Failure<Guid>($"Lobby session creation exception: {ex.Message}");
		}
	}

	/// <summary>
	/// Get current lobby session ID
	/// </summary>
	public Guid GetLobbySessionId() => _lobbySessionId;

	/// <summary>
	/// Submit gameplay event to backend
	/// Backend handles event persistence and queuing for remote sync
	/// </summary>
	public async Task<Result<bool>> EmitEventAsync(string eventType, object payload)
	{
		if (!IsReady)
			return Result.Failure<bool>("EventService not initialized - backend not ready");

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

			var json = JsonSerializer.Serialize(eventData, JsonOptions);

			await EnsureConnectedAsync();

			var headers = BuildJsonHeaders().ToArray();

			var error = _httpClient.Request(
				HttpClient.Method.Post,
				$"/box/session/{_currentSessionId}",
				headers,
				json
			);

			if (error != Godot.Error.Ok)
			{
				var errorMsg = $"Event submission request failed: {error}";
				CallDeferred(GodotObject.MethodName.EmitSignal, SignalName.EventSubmissionFailed, _currentSessionId.ToString(), errorMsg);
				return Result.Failure<bool>(errorMsg);
			}

			await WaitForResponseAsync();

			var responseCode = _httpClient.GetResponseCode();
			if (responseCode == 201)
			{
				// This prevents POST-then-GET state interference by clearing the Body state
				var responseBody = await _httpClient.ReadResponseBodyAsync();

#if DEBUG_HTTP_LIFECYCLE
				LogInfo($"[HTTP] POST succeeded, consumed {responseBody.Length} bytes");
				_httpClient.Poll();
				LogInfo($"[HTTP] Status after response read: {_httpClient.GetStatus()}");
#endif

				CallDeferred(GodotObject.MethodName.EmitSignal, SignalName.EventSubmitted, _currentSessionId.ToString(), eventType);
				return Result.Success(true);
			}

			// CRITICAL: Read error response body to see actual backend validation error
			var errorBodyBytes = await _httpClient.ReadResponseBodyAsync();
			var errorBody = errorBodyBytes.GetStringFromUtf8();

			var failureMsg = $"Event submission failed with code {responseCode}";
			if (!string.IsNullOrEmpty(errorBody))
			{
				LogError($"[EventService] Backend error response: {errorBody}");
				failureMsg += $" - {errorBody}";
			}

			CallDeferred(GodotObject.MethodName.EmitSignal, SignalName.EventSubmissionFailed, _currentSessionId.ToString(), failureMsg);
			return Result.Failure<bool>(failureMsg);
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
	/// Posts events to the specified or active lobby session
	/// </summary>
	/// <param name="lobbySessionId">Optional explicit lobby session ID. If null, uses global _lobbySessionId</param>
	public async Task<Result<bool>> EmitUserEventAsync(Guid playerId, string eventType, object payload, Guid? lobbySessionId = null)
	{
		if (!IsReady)
			return Result.Failure<bool>("EventService not initialized - backend not ready");

		// Use provided lobby session ID or fall back to global
		var sessionId = lobbySessionId ?? _lobbySessionId;

		// Check if we have an active lobby session
		if (sessionId == Guid.Empty)
		{
			return Result.Failure<bool>("No active lobby session - call CreateLobbySessionAsync first or provide explicit lobby session ID");
		}

		try
		{
#if DEBUG_HTTP_LIFECYCLE
			LogInfo($"[HTTP] EmitUserEvent - {eventType} to lobby session {sessionId}");
#endif

			await EnsureConnectedAsync();

			var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
			var eventJson = JsonSerializer.Serialize(new
			{
				type = eventType,
				payload = JsonSerializer.Deserialize<Dictionary<string, object>>(payloadJson)
			}, JsonOptions);

			var url = $"/box/session/{sessionId}";
			var headers = BuildHeaders();
			headers.Add("Content-Type: application/json");
			headers.Add($"Content-Length: {Encoding.UTF8.GetByteCount(eventJson)}");
			var headersArray = headers.ToArray();

#if DEBUG_HTTP_LIFECYCLE
			LogInfo($"[HTTP] EmitUserEvent - POST {url}");
#endif

			var error = _httpClient.Request(
				HttpClient.Method.Post,
				url,
				headersArray,
				eventJson
			);

			if (error != Godot.Error.Ok)
				return Result.Failure<bool>($"User event submission request failed: {error}");

			await WaitForResponseAsync();

			var responseCode = _httpClient.GetResponseCode();

#if DEBUG_HTTP_LIFECYCLE
			LogInfo($"[HTTP] EmitUserEvent - Response code: {responseCode}");
#endif

			if (responseCode == 201)
			{
				// Read response body to complete the request cycle
				// Using reactive polling
				var responseBodyBytes = await _httpClient.ReadResponseBodyAsync();
			var responseBody = responseBodyBytes.GetStringFromUtf8();

#if DEBUG_HTTP_LIFECYCLE
				LogInfo($"[HTTP] POST succeeded, consumed {responseBody.Length} bytes");
				_httpClient.Poll();
				LogInfo($"[HTTP] Status after response read: {_httpClient.GetStatus()}");
#endif

				LogInfo($"User event submitted: {eventType} to lobby session {_lobbySessionId}");
				return Result.Success(true);
			}

			var failureMsg = $"User event submission failed with code {responseCode}";
			return Result.Failure<bool>(failureMsg);
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
	public async Task<Result<TResponse>> QueryAsync<TResponse>(
		string endpoint,
		Dictionary<string, string> queryParams = null,
		Guid? playerId = null
	) where TResponse : class
	{
		if (!IsReady)
			return Result.Failure<TResponse>("EventService not initialized - backend not ready");

		try
		{
			await EnsureConnectedAsync();

			var url = endpoint;
			if (queryParams != null && queryParams.Count > 0)
			{
				var query = string.Join("&",
					queryParams.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
				url = $"{endpoint}?{query}";
			}

			var headers = BuildHeaders(playerId);
			headers.Add("Accept: application/json");
			var headersArray = headers.ToArray();

			var error = _httpClient.Request(HttpClient.Method.Get, url, headersArray);
			if (error != Godot.Error.Ok)
				return Result.Failure<TResponse>($"Query request failed: {error}");

			await WaitForResponseAsync();

			var responseCode = _httpClient.GetResponseCode();
			if (responseCode != 200)
			{
				// Close connection to allow recovery on next request
				_httpClient.Close();
				return Result.Failure<TResponse>($"Query failed with code {responseCode}");
			}

			var bodyBytes = await _httpClient.ReadResponseBodyAsync();
			var bodyText = bodyBytes.GetStringFromUtf8();

			// Validate body before deserializing
			if (string.IsNullOrEmpty(bodyText))
			{
				_httpClient.Close();
				return Result.Failure<TResponse>("Empty response body from query");
			}

			var response = JsonSerializer.Deserialize<TResponse>(bodyText, JsonOptions);
			return Result.Success(response);
		}
		catch (Exception ex)
		{
			// Close connection on exception to ensure clean state
			_httpClient.Close();
			return Result.Failure<TResponse>($"Query exception: {ex.Message}");
		}
	}

	/// <summary>
	/// Execute GET query against backend API, returning raw JSON string
	/// Used by game event services for custom JSON parsing
	/// </summary>
	/// <param name="playerId">Optional. If provided, includes player JWT token in Authorization header.</param>
	public async Task<Result<string>> QueryRawAsync(
		string endpoint,
		Dictionary<string, string> queryParams = null,
		Guid? playerId = null
	)
	{
		if (!IsReady)
			return Result.Failure<string>("EventService not initialized - backend not ready");

		try
		{
			await EnsureConnectedAsync();

			var url = endpoint;
			if (queryParams != null && queryParams.Count > 0)
			{
				var query = string.Join("&",
					queryParams.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
				url = $"{endpoint}?{query}";
			}

			var headers = BuildHeaders(playerId);
			headers.Add("Accept: application/json");
			var headersArray = headers.ToArray();

			var error = _httpClient.Request(HttpClient.Method.Get, url, headersArray);
			if (error != Godot.Error.Ok)
				return Result.Failure<string>($"Query request failed: {error}");

			await WaitForResponseAsync();

			// CRITICAL: Poll to ensure body is fully received
			// Using reactive polling
			var responseCode = _httpClient.GetResponseCode();
			var bodyBytes = await _httpClient.ReadResponseBodyAsync();
			var bodyText = bodyBytes.GetStringFromUtf8();

			if (responseCode != 200)
			{
				// Extract backend error details for better debugging
				var errorMessage = $"Query failed with code {responseCode}";

				// Try to parse backend error structure
				if (!string.IsNullOrEmpty(bodyText))
				{
					try
					{
						var parseResult = Json.ParseString(bodyText);
						if (parseResult.Obj != null)
						{
							var errorDict = parseResult.AsGodotDictionary();

							// Extract structured error fields
							var code = errorDict.GetValueOrDefault("code", "UNKNOWN").ToString();
							var message = errorDict.GetValueOrDefault("message", bodyText).ToString();
							var requestId = errorDict.GetValueOrDefault("request_id", "N/A").ToString();

							// Build comprehensive error message
							errorMessage = $"HTTP {responseCode} [{code}]: {message}";

							// Log full error details for debugging
							GD.PrintErr($"[EventService] Backend error:");
							GD.PrintErr($"  Status: {responseCode}");
							GD.PrintErr($"  Code: {code}");
							GD.PrintErr($"  Message: {message}");
							GD.PrintErr($"  Request ID: {requestId}");
							if (errorDict.ContainsKey("details"))
							{
								GD.PrintErr($"  Details: {errorDict["details"]}");
							}
							GD.PrintErr($"  Endpoint: {endpoint}");
						}
						else
						{
							// Failed to parse JSON, use raw body
							errorMessage = $"Query failed with code {responseCode}: {bodyText}";
							GD.PrintErr($"[EventService] HTTP {responseCode} error (raw): {bodyText}");
						}
					}
					catch (Exception parseEx)
					{
						// Parsing failed, use raw error
						errorMessage = $"Query failed with code {responseCode}: {bodyText}";
						GD.PrintErr($"[EventService] Failed to parse error response: {parseEx.Message}");
						GD.PrintErr($"[EventService] Raw error: {bodyText}");
					}
				}
				else
				{
					GD.PrintErr($"[EventService] HTTP {responseCode} with empty response body");
				}

				// Close connection to allow recovery on next request
				_httpClient.Close();
				return Result.Failure<string>(errorMessage);
			}

			return Result.Success(bodyText);
		}
		catch (Exception ex)
		{
			// Close connection on exception to ensure clean state
			_httpClient.Close();
			return Result.Failure<string>($"Query exception: {ex.Message}");
		}
	}

	/// <summary>
	/// Execute POST request against backend API with JSON body
	/// </summary>
	/// <param name="playerId">Optional. If provided, includes player JWT token in Authorization header.</param>
	public async Task<Result<TResponse>> PostAsync<TRequest, TResponse>(
		string endpoint,
		TRequest requestBody,
		int expectedStatusCode = 201,
		Guid? playerId = null
	) where TResponse : class
	{
		if (!IsReady)
			return Result.Failure<TResponse>("EventService not initialized - backend not ready");

		try
		{
			await EnsureConnectedAsync();

			var json = JsonSerializer.Serialize(requestBody, JsonOptions);

			var headers = BuildJsonHeaders(playerId).ToArray();

			var error = _httpClient.Request(HttpClient.Method.Post, endpoint, headers, json);
			if (error != Godot.Error.Ok)
				return Result.Failure<TResponse>($"POST request failed: {error}");

			await WaitForResponseAsync();

			var responseCode = _httpClient.GetResponseCode();
			if (responseCode != expectedStatusCode)
			{
				var errorBytes = await _httpClient.ReadResponseBodyAsync();
				var errorText = errorBytes.GetStringFromUtf8();

				// Close connection to allow recovery on next request
				_httpClient.Close();

				return Result.Failure<TResponse>($"POST failed with code {responseCode}: {errorText}");
			}

			// Ensure response body is fully received (Godot HttpClient timing issue)
			// Using reactive polling
			var bodyBytes = await _httpClient.ReadResponseBodyAsync();
			var bodyText = bodyBytes.GetStringFromUtf8();

#if DEBUG_HTTP_LIFECYCLE
			LogInfo($"[HTTP] PostAsync response: code={responseCode}, bodyLength={bodyBytes.Length}");
#endif

			// Validate body before deserializing
			if (string.IsNullOrEmpty(bodyText))
			{
				_httpClient.Close();
				return Result.Failure<TResponse>($"POST returned {responseCode} with empty response body");
			}

			var response = JsonSerializer.Deserialize<TResponse>(bodyText, JsonOptions);
			return Result.Success(response);
		}
		catch (Exception ex)
		{
			// Close connection on exception to ensure clean state
			_httpClient.Close();
			return Result.Failure<TResponse>($"POST exception: {ex.Message}");
		}
	}

	/// <summary>
	/// Execute PUT request against backend API with JSON body
	/// </summary>
	/// <param name="playerId">Optional. If provided, includes player JWT token in Authorization header.</param>
	public async Task<Result<TResponse>> PutAsync<TRequest, TResponse>(
		string endpoint,
		TRequest requestBody,
		int expectedStatusCode = 200,
		Guid? playerId = null
	) where TResponse : class
	{
		if (!IsReady)
			return Result.Failure<TResponse>("EventService not initialized - backend not ready");

		try
		{
			await EnsureConnectedAsync();

			var json = JsonSerializer.Serialize(requestBody, JsonOptions);

			var headers = BuildJsonHeaders(playerId).ToArray();

			var error = _httpClient.Request(HttpClient.Method.Put, endpoint, headers, json);
			if (error != Godot.Error.Ok)
				return Result.Failure<TResponse>($"PUT request failed: {error}");

			await WaitForResponseAsync();

			var responseCode = _httpClient.GetResponseCode();
			if (responseCode != expectedStatusCode)
			{
				var errorBytes = await _httpClient.ReadResponseBodyAsync();
				var errorText = errorBytes.GetStringFromUtf8();

				// Close connection to allow recovery on next request
				_httpClient.Close();

				return Result.Failure<TResponse>($"PUT failed with code {responseCode}: {errorText}");
			}

			var bodyBytes = await _httpClient.ReadResponseBodyAsync();
			var bodyText = bodyBytes.GetStringFromUtf8();

			var response = JsonSerializer.Deserialize<TResponse>(bodyText, JsonOptions);
			return Result.Success(response);
		}
		catch (Exception ex)
		{
			// Close connection on exception to ensure clean state
			_httpClient.Close();
			return Result.Failure<TResponse>($"PUT exception: {ex.Message}");
		}
	}

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

		return Result.Failure<bool>("Unknown error");
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

		return Result.Failure<PlayerValidationResponse>("Unknown error");
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

		return Result.Failure<Guid>("Unknown error");
	}

	/// <summary>
	/// Register player on first play (idempotent)
	/// This endpoint is safe to call multiple times - it will return existing player if already registered
	/// Auto-creates temporary player record even if origin box doesn't exist
	/// </summary>
	public async Task<Result<Guid>> RegisterPlayerFirstPlayAsync(Guid playerId, string username, string phoneNumber, string pin, Guid originBoxId)
	{
		var request = new PlayerCreateRequest
		{
			Id = playerId.ToString(),
			Tag = username,
			PhoneNumber = phoneNumber,
			Pin = pin,
			OriginId = originBoxId.ToString()
		};

		var result = await PostAsync<PlayerCreateRequest, PlayerDetailResponse>("/player/first_play", request, 201);

		if (result.IsSuccess(out var _))
		{
			LogInfo($"Player first_play registered: {username} ({playerId})");
			return Result.Success(playerId);
		}

		if (result.IsFailure(out var error))
		{
			LogError($"Failed to register player first_play: {error.Message}");
			return Result.Failure<Guid>(error.Message);
		}

		return Result.Failure<Guid>("Unknown error registering player first_play");
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
		var result = await PutAsync<BoxCreateRequest, BoxDetailResponse>(
			$"/box/{boxId}",
			request,
			200
		);

		if (result.IsSuccess(out var response))
		{
			// Security: Only log partial API key if present
			if (!string.IsNullOrEmpty(response.ApiKey))
			{
				var maskedKey = response.ApiKey.Length > 12
					? $"{response.ApiKey.Substring(0, 8)}...{response.ApiKey.Substring(response.ApiKey.Length - 4)}"
					: "***";
				LogInfo($"Box registered with new API key: {maskedKey}");
			}
			else
			{
				LogInfo($"Box verified: {locationName} ({boxId})");
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

		return Result.Failure<Guid>("Unknown error");
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
	/// <param name="lobbySessionId">Optional explicit lobby session ID. If null, uses global _lobbySessionId</param>
	public async Task<Result<bool>> AddCreditsAsync(Guid playerId, int amount, string reason = "", Guid? lobbySessionId = null)
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
	/// <param name="lobbySessionId">Optional explicit lobby session ID. If null, uses global _lobbySessionId</param>
	public async Task<Result<bool>> SpendCreditsAsync(Guid playerId, int amount, string reason = "", Guid? lobbySessionId = null)
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

	// Private helper methods

	/// <summary>
	/// Build HTTP headers with Box API key and optional player Authorization token
	/// </summary>
	/// <param name="playerId">Optional. If provided, includes player JWT token in Authorization header.</param>
	private List<string> BuildHeaders(Guid? playerId = null)
	{
		if (string.IsNullOrEmpty(_boxApiKey))
		{
			LogError("CRITICAL: Attempting HTTP request without API key configured!");
			throw new InvalidOperationException("Box API key not configured - check .env.local file");
		}

		var headers = new List<string>
		{
			$"X-Box-API-Key: {_boxApiKey}",
			"User-Agent: BarBox-Client/1.0"
		};

		if (!playerId.HasValue || playerId.Value == Guid.Empty)
			return headers;

		var sessionManager = SessionManager.GetInstance();
		if (sessionManager == null)
		{
			GD.PushError("[EventService] Authenticated request failed: SessionManager not available");
			return headers;
		}

		var jwtToken = sessionManager.GetJwtToken(playerId.Value);
		if (string.IsNullOrEmpty(jwtToken))
		{
			GD.PushError($"[EventService] Authenticated request for player {playerId.Value} failed: JWT token not found");
			return headers;
		}

		headers.Add($"Authorization: Bearer {jwtToken}");
		return headers;
	}

	/// <summary>
	/// Build headers for JSON request/response operations
	/// </summary>
	/// <param name="playerId">Optional. If provided, includes player JWT token in Authorization header.</param>
	private List<string> BuildJsonHeaders(Guid? playerId = null)
	{
		var headers = BuildHeaders(playerId);
		headers.Add("Accept: application/json");
		headers.Add("Content-Type: application/json");
		return headers;
	}

	/// <summary>
	/// Build headers with player ID for player-scoped operations
	/// </summary>
	private List<string> BuildPlayerHeaders(Guid playerId)
	{
		var headers = BuildHeaders();
		headers.Add($"Player-Id: {playerId}");
		return headers;
	}

	private async Task EnsureConnectedAsync()
	{
		_httpClient.Poll();
		var currentStatus = _httpClient.GetStatus();

#if DEBUG_HTTP_LIFECYCLE
		LogInfo($"[HTTP] EnsureConnected - Initial status: {currentStatus}");
#endif

		// Connection is ready for new requests
		if (currentStatus == HttpClient.Status.Connected)
		{
#if DEBUG_HTTP_LIFECYCLE
			LogInfo("[HTTP] Connection ready for reuse (keep-alive)");
#endif
			return;
		}

		// Handle intermediate states from previous requests
		if (currentStatus == HttpClient.Status.Body)
		{
#if DEBUG_HTTP_LIFECYCLE
			LogInfo("[HTTP] Cleaning up unconsumed response body from previous request");
#endif

			// Fully consume response body - may be chunked, requiring multiple reads
			const int MAX_DISCARD_BYTES = 10 * 1024 * 1024; // 10MB limit to prevent DOS
			int totalBytesDiscarded = 0;
			while (_httpClient.GetStatus() == HttpClient.Status.Body)
			{
				// Direct call intentional - chunked reading in loop
				var chunk = _httpClient.ReadResponseBodyChunk();
				if (chunk.Length == 0)
					break; // No more data available

				totalBytesDiscarded += chunk.Length;

				// Prevent unbounded memory consumption from malicious/buggy responses
				if (totalBytesDiscarded > MAX_DISCARD_BYTES)
				{
					LogError($"Response body exceeded {MAX_DISCARD_BYTES} bytes - aborting connection");
					_httpClient.Close();
					throw new InvalidOperationException($"Response body too large (>{MAX_DISCARD_BYTES} bytes)");
				}
			}

#if DEBUG_HTTP_LIFECYCLE
			if (totalBytesDiscarded > 0)
				LogInfo($"[HTTP] Discarded {totalBytesDiscarded} bytes of unconsumed response data");
#endif

			// Poll to advance state machine after full consumption
			_httpClient.Poll();
			currentStatus = _httpClient.GetStatus();

			if (currentStatus == HttpClient.Status.Connected)
			{
#if DEBUG_HTTP_LIFECYCLE
				LogInfo("[HTTP] Connection recovered to Connected state after cleanup");
#endif
				return; // Now ready for reuse
			}
		}

		// If still not in a good state, close and reconnect
		if (currentStatus != HttpClient.Status.Disconnected)
		{
#if DEBUG_HTTP_LIFECYCLE
			LogInfo($"[HTTP] Closing stale connection (status: {currentStatus})");
#endif

			_httpClient.Close();
			await DelayAsync(0.01f); // Brief delay for clean close
		}

		// Establish new connection
#if DEBUG_HTTP_LIFECYCLE
		LogInfo($"[HTTP] Establishing new connection to {BACKEND_HOST}:{BACKEND_PORT} (HTTPS: {USE_HTTPS})");
#endif

		// Configure TLS options for HTTPS
		TlsOptions tlsOptions = null;
		if (USE_HTTPS)
		{
			tlsOptions = TlsOptions.Client();
			// In development with self-signed certificates, we may need to allow unsafe connections
			// For production, proper CA-signed certificates should be used
		}

		var error = _httpClient.ConnectToHost(BACKEND_HOST, BACKEND_PORT, tlsOptions);
		if (error != Godot.Error.Ok)
			throw new InvalidOperationException($"Failed to connect to backend: {error}");

		// Use aggressive polling utility - exits immediately when connected
		bool connected = await _httpClient.PollUntilConnectedAsync(timeoutSeconds: 5.0f);
		if (!connected)
			throw new InvalidOperationException("Connection timeout");

#if DEBUG_HTTP_LIFECYCLE
		LogInfo("[HTTP] New connection established successfully");
#endif
	}

	private async Task WaitForResponseAsync()
	{
#if DEBUG_HTTP_LIFECYCLE
		_httpClient.Poll();
		var statusBeforeWait = _httpClient.GetStatus();
		LogInfo($"[HTTP] WaitForResponse - Status before wait: {statusBeforeWait}");
#endif

		// Use aggressive polling utility - exits immediately when response ready
		bool ready = await _httpClient.PollUntilResponseReadyAsync(
			timeoutSeconds: REQUEST_TIMEOUT_MS / 1000.0f
		);

		if (!ready)
		{
			_httpClient.Poll();
			var finalStatus = _httpClient.GetStatus();
			throw new InvalidOperationException($"Request timeout (final status: {finalStatus})");
		}

#if DEBUG_HTTP_LIFECYCLE
		_httpClient.Poll();
		var statusAfterWait = _httpClient.GetStatus();
		LogInfo($"[HTTP] WaitForResponse - Status after wait: {statusAfterWait}");
#endif
	}


	private string GetBoxApiKey()
	{
		var locationManager = LocationManager.GetAutoload();
		if (locationManager != null && locationManager.IsConfigLoaded)
		{
			return locationManager.BoxApiKey;
		}

		// Fallback
		var apiKey = System.Environment.GetEnvironmentVariable("BARBOX_API_KEY");
		if (string.IsNullOrEmpty(apiKey))
		{
			LogWarning("BARBOX_API_KEY not set - authenticated requests will fail");
			return "";
		}

		return apiKey;
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

		var session = sessionManager.GetUserSession(phoneNumber);
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
	/// Override IsReady property to check test override in DEBUG builds.
	/// In RELEASE builds, this property doesn't exist and base.IsReady is used.
	/// </summary>
	public new bool IsReady => _useTestReadyOverride ? _testReadyOverride : base.IsReady;
#endif

	protected override void OnServiceDestroyed()
	{
		_httpClient?.Close();
		_httpClient = null;
	}
}

// Machine Credits DTOs for request body payloads
public partial class MachineCreditsDepositRequest
{
	[JsonPropertyName("box_id")]
	public Guid BoxId { get; set; }

	[JsonPropertyName("player_id")]
	public Guid PlayerId { get; set; }

	[JsonPropertyName("amount")]
	public int Amount { get; set; }

	[JsonPropertyName("lobby_session_id")]
	public Guid LobbySessionId { get; set; }
}

public partial class MachineCreditsConsumeRequest
{
	[JsonPropertyName("box_id")]
	public Guid BoxId { get; set; }

	[JsonPropertyName("amount")]
	public int Amount { get; set; }

	[JsonPropertyName("game_session_id")]
	public Guid GameSessionId { get; set; }
}
