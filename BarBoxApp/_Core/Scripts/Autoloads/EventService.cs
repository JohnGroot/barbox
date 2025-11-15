using Godot;
using LightResults;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Event-based persistence service for BarBox backend integration
/// Forwards all game/session events to local BarBoxServices backend
/// Replaces the old DataStore CRUD pattern with event sourcing
/// </summary>
public partial class EventService : AutoloadBase
{
	// Configuration with environment variable support (matches BackendManager)
	private static readonly string BACKEND_HOST =
		System.Environment.GetEnvironmentVariable("BARBOX_BACKEND_HOST") ?? "127.0.0.1";

	private static readonly int BACKEND_PORT =
		int.TryParse(System.Environment.GetEnvironmentVariable("BARBOX_BACKEND_PORT"), out var port)
			? port
			: 8000;

	private static readonly string BASE_URL = $"http://{BACKEND_HOST}:{BACKEND_PORT}";

	private const int REQUEST_TIMEOUT_MS = 5000;
	private const int POLL_INTERVAL_MS = 10;

	// Enable detailed HTTP lifecycle logging for debugging connection issues
	// To enable: Add DEBUG_HTTP_LIFECYCLE to project defines or use #define DEBUG_HTTP_LIFECYCLE at top of file

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	private Godot.HttpClient _httpClient;
	private string _locationId;
	private Guid _boxId;
	private Guid _currentSessionId;
	private Guid _currentBoxId;

	[Signal] public delegate void EventSubmittedEventHandler(string sessionId, string eventType);
	[Signal] public delegate void EventSubmissionFailedEventHandler(string sessionId, string error);
	[Signal] public delegate void SessionCreatedEventHandler(string sessionId);

	protected override async Task OnServiceInitializeAsync(CancellationToken cancellationToken = default)
	{
		_httpClient = new Godot.HttpClient();
		_locationId = GetLocationId();

		try
		{
			_boxId = GetBoxId();
			LogInfo($"Box ID: {_boxId}");
		}
		catch (Exception ex)
		{
			LogError($"Failed to get Box ID: {ex.Message}");
			throw new Exception($"EventService requires valid Box ID: {ex.Message}\nStack Trace: {ex.StackTrace}");
		}

		// Wait for BackendManager to be ready
		var backendManager = BackendManager.GetInstance();
		if (backendManager == null)
		{
			throw new Exception("BackendManager not found - EventService requires BackendManager");
		}

		LogInfo("Waiting for BackendManager to become ready...");
		var backendReady = await backendManager.WaitForReadyAsync(timeoutSeconds: 30.0f, cancellationToken);

		if (!backendReady)
		{
			throw new Exception("BackendManager failed to become ready within timeout");
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

			var headers = new[]
			{
				$"Player-Id: {playerId}",
				"User-Agent: BarBox-Client/1.0"
			};

#if DEBUG_HTTP_LIFECYCLE
			LogInfo($"[HTTP] CreateActivitySession - PUT {url}");
#endif

			var error = _httpClient.Request(
				Godot.HttpClient.Method.Put,
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
				_httpClient.Close();
				return Result.Failure<Guid>($"Activity session creation failed with code {responseCode}");
			}

			_currentSessionId = sessionId;
			_currentBoxId = boxId;

			LogInfo($"Activity session created: {sessionId} (game: {gameTag})");
			CallDeferred(MethodName.EmitSignal, SignalName.SessionCreated, sessionId.ToString());

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
			var headers = new[] { "User-Agent: BarBox-Client/1.0" };

#if DEBUG_HTTP_LIFECYCLE
			LogInfo($"[HTTP] CloseActivitySession - POST {url}");
#endif

			var error = _httpClient.Request(
				Godot.HttpClient.Method.Post,
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
			var headers = new[]
			{
				$"Player-Id: {playerId}",
				"User-Agent: BarBox-Client/1.0"
			};

#if DEBUG_HTTP_LIFECYCLE
			LogInfo($"[HTTP] CreateLobbySession - POST {url}");
#endif

			var error = _httpClient.Request(
				Godot.HttpClient.Method.Post,
				url,
				headers
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
				_httpClient.Close();
				return Result.Failure<Guid>($"Lobby session creation failed with code {responseCode}");
			}

			// Parse response to get session ID
			var bodyBytes = await _httpClient.ReadResponseBodyOptimizedAsync();
			var bodyText = bodyBytes.GetStringFromUtf8();

			if (string.IsNullOrEmpty(bodyText))
			{
				return Result.Failure<Guid>("Empty response body from lobby session creation");
			}

			var response = JsonSerializer.Deserialize<Dictionary<string, object>>(bodyText);
			if (response != null && response.ContainsKey("id"))
			{
				var sessionId = Guid.Parse(response["id"].ToString());
				_lobbySessionId = sessionId;

				LogInfo($"Lobby session created: {sessionId}");
				CallDeferred(MethodName.EmitSignal, SignalName.SessionCreated, sessionId.ToString());
				return Result.Success(sessionId);
			}

			return Result.Failure<Guid>("Failed to parse lobby session ID from response");
		}
		catch (Exception ex)
		{
#if DEBUG_HTTP_LIFECYCLE
			LogError($"[HTTP] CreateLobbySession - Exception: {ex.Message}");
#endif

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

			var headers = new[]
			{
				"User-Agent: BarBox-Client/1.0",
				"Accept: application/json",
				"Content-Type: application/json"
			};

			var error = _httpClient.Request(
				Godot.HttpClient.Method.Post,
				$"/box/session/{_currentSessionId}",
				headers,
				json
			);

			if (error != Godot.Error.Ok)
			{
				var errorMsg = $"Event submission request failed: {error}";
				CallDeferred(MethodName.EmitSignal, SignalName.EventSubmissionFailed, _currentSessionId.ToString(), errorMsg);
				return Result.Failure<bool>(errorMsg);
			}

			await WaitForResponseAsync();

			var responseCode = _httpClient.GetResponseCode();
			if (responseCode == 201)
			{
				// This prevents POST-then-GET state interference by clearing the Body state
				var responseBody = await _httpClient.ReadResponseBodyOptimizedAsync();

#if DEBUG_HTTP_LIFECYCLE
				LogInfo($"[HTTP] POST succeeded, consumed {responseBody.Length} bytes");
				_httpClient.Poll();
				LogInfo($"[HTTP] Status after response read: {_httpClient.GetStatus()}");
#endif

				CallDeferred(MethodName.EmitSignal, SignalName.EventSubmitted, _currentSessionId.ToString(), eventType);
				return Result.Success(true);
			}

			// CRITICAL: Read error response body to see actual backend validation error
			var errorBodyBytes = await _httpClient.ReadResponseBodyOptimizedAsync();
			var errorBody = errorBodyBytes.GetStringFromUtf8();

			var failureMsg = $"Event submission failed with code {responseCode}";
			if (!string.IsNullOrEmpty(errorBody))
			{
				LogError($"[EventService] Backend error response: {errorBody}");
				failureMsg += $" - {errorBody}";
			}

			CallDeferred(MethodName.EmitSignal, SignalName.EventSubmissionFailed, _currentSessionId.ToString(), failureMsg);
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
	/// Posts events to the active lobby session
	/// </summary>
	public async Task<Result<bool>> EmitUserEventAsync(Guid playerId, string eventType, object payload)
	{
		if (!IsReady)
			return Result.Failure<bool>("EventService not initialized - backend not ready");

		// Check if we have an active lobby session
		if (_lobbySessionId == Guid.Empty)
		{
			return Result.Failure<bool>("No active lobby session - call CreateLobbySessionAsync first");
		}

		try
		{
#if DEBUG_HTTP_LIFECYCLE
			LogInfo($"[HTTP] EmitUserEvent - {eventType} to lobby session {_lobbySessionId}");
#endif

			await EnsureConnectedAsync();

			var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
			var eventJson = JsonSerializer.Serialize(new
			{
				type = eventType,
				payload = JsonSerializer.Deserialize<Dictionary<string, object>>(payloadJson)
			}, JsonOptions);

			var url = $"/box/session/{_lobbySessionId}";
			var headers = new[]
			{
				"User-Agent: BarBox-Client/1.0",
				"Content-Type: application/json",
				$"Content-Length: {Encoding.UTF8.GetByteCount(eventJson)}"
			};

#if DEBUG_HTTP_LIFECYCLE
			LogInfo($"[HTTP] EmitUserEvent - POST {url}");
#endif

			var error = _httpClient.Request(
				Godot.HttpClient.Method.Post,
				url,
				headers,
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
				var responseBodyBytes = await _httpClient.ReadResponseBodyOptimizedAsync();
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
	/// Get current location ID
	/// </summary>
	public string GetCurrentLocationId() => _locationId;

	/// <summary>
	/// Get current box ID
	/// </summary>
	public Guid GetCurrentBoxId() => _boxId;

	/// <summary>
	/// Execute GET query against backend API
	/// </summary>
	public async Task<Result<TResponse>> QueryAsync<TResponse>(
		string endpoint,
		Dictionary<string, string> queryParams = null
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

			var headers = new[]
			{
				"User-Agent: BarBox-Client/1.0",
				"Accept: application/json"
			};

			var error = _httpClient.Request(Godot.HttpClient.Method.Get, url, headers);
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

			var bodyBytes = await _httpClient.ReadResponseBodyOptimizedAsync();
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
	public async Task<Result<string>> QueryRawAsync(
		string endpoint,
		Dictionary<string, string> queryParams = null
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

			var headers = new[]
			{
				"User-Agent: BarBox-Client/1.0",
				"Accept: application/json"
			};

			var error = _httpClient.Request(Godot.HttpClient.Method.Get, url, headers);
			if (error != Godot.Error.Ok)
				return Result.Failure<string>($"Query request failed: {error}");

			await WaitForResponseAsync();

			// CRITICAL: Poll to ensure body is fully received
			// Using reactive polling
			var responseCode = _httpClient.GetResponseCode();
			var bodyBytes = await _httpClient.ReadResponseBodyOptimizedAsync();
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
	public async Task<Result<TResponse>> PostAsync<TRequest, TResponse>(
		string endpoint,
		TRequest requestBody,
		int expectedStatusCode = 201
	) where TResponse : class
	{
		if (!IsReady)
			return Result.Failure<TResponse>("EventService not initialized - backend not ready");

		try
		{
			await EnsureConnectedAsync();

			var json = JsonSerializer.Serialize(requestBody, JsonOptions);

			var headers = new[]
			{
				"User-Agent: BarBox-Client/1.0",
				"Accept: application/json",
				"Content-Type: application/json"
			};

			var error = _httpClient.Request(Godot.HttpClient.Method.Post, endpoint, headers, json);
			if (error != Godot.Error.Ok)
				return Result.Failure<TResponse>($"POST request failed: {error}");

			await WaitForResponseAsync();

			var responseCode = _httpClient.GetResponseCode();
			if (responseCode != expectedStatusCode)
			{
				var errorBytes = await _httpClient.ReadResponseBodyOptimizedAsync();
				var errorText = errorBytes.GetStringFromUtf8();

				// Close connection to allow recovery on next request
				_httpClient.Close();

				return Result.Failure<TResponse>($"POST failed with code {responseCode}: {errorText}");
			}

			// Ensure response body is fully received (Godot HttpClient timing issue)
			// Using reactive polling
			var bodyBytes = await _httpClient.ReadResponseBodyOptimizedAsync();
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
	public async Task<Result<TResponse>> PutAsync<TRequest, TResponse>(
		string endpoint,
		TRequest requestBody,
		int expectedStatusCode = 200
	) where TResponse : class
	{
		if (!IsReady)
			return Result.Failure<TResponse>("EventService not initialized - backend not ready");

		try
		{
			await EnsureConnectedAsync();

			var json = JsonSerializer.Serialize(requestBody, JsonOptions);

			var headers = new[]
			{
				"User-Agent: BarBox-Client/1.0",
				"Accept: application/json",
				"Content-Type: application/json"
			};

			var error = _httpClient.Request(Godot.HttpClient.Method.Put, endpoint, headers, json);
			if (error != Godot.Error.Ok)
				return Result.Failure<TResponse>($"PUT request failed: {error}");

			await WaitForResponseAsync();

			var responseCode = _httpClient.GetResponseCode();
			if (responseCode != expectedStatusCode)
			{
				var errorBytes = await _httpClient.ReadResponseBodyOptimizedAsync();
				var errorText = errorBytes.GetStringFromUtf8();

				// Close connection to allow recovery on next request
				_httpClient.Close();

				return Result.Failure<TResponse>($"PUT failed with code {responseCode}: {errorText}");
			}

			var bodyBytes = await _httpClient.ReadResponseBodyOptimizedAsync();
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
	public async Task<Result<PlayerValidationResponse>> ValidatePlayerCreationAsync(Guid playerId, string username, Guid originBoxId)
	{
		var request = new PlayerCreateRequest
		{
			Id = playerId.ToString(),
			Tag = username,
			OriginId = originBoxId.ToString()
		};

		var result = await PostAsync<PlayerCreateRequest, PlayerValidationResponse>("/player/validate", request, 200);

		if (result.IsSuccess(out var response))
		{
			if (!response.Valid)
			{
				// Validation failed - construct error message from validation errors
				var errorMessages = new System.Text.StringBuilder();
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
	public async Task<Result<Guid>> CreatePlayerAsync(Guid playerId, string username, Guid originBoxId)
	{
		var request = new PlayerCreateRequest
		{
			Id = playerId.ToString(),
			Tag = username,
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
	public async Task<Result<Guid>> RegisterPlayerFirstPlayAsync(Guid playerId, string username, Guid originBoxId)
	{
		var request = new PlayerCreateRequest
		{
			Id = playerId.ToString(),
			Tag = username,
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
	/// Register box (physical terminal) with backend
	/// Idempotent - safe to call multiple times
	/// </summary>
	public async Task<Result<Guid>> RegisterBoxAsync(Guid boxId, string locationName)
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

		if (result.IsSuccess(out var _))
		{
			LogInfo($"Box registered/verified: {locationName} ({boxId})");
			return Result.Success(boxId);
		}

		if (result.IsFailure(out var error))
		{
			LogError($"Failed to register box: {error.Message}");
			return Result.Failure<Guid>(error.Message);
		}

		return Result.Failure<Guid>("Unknown error registering box");
	}

	/// <summary>
	/// Get player's credit balance for current location
	/// </summary>
	public async Task<Result<int>> GetPlayerCreditsAsync(Guid playerId)
	{
		var locationId = GetCurrentLocationId();
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
	public async Task<Result<bool>> AddCreditsAsync(Guid playerId, int amount, string reason = "")
	{
		var payload = new
		{
			location_id = GetCurrentLocationId(),
			amount = amount,
			reason = reason
		};

		return await EmitUserEventAsync(playerId, "credit/earn", payload);
	}

	/// <summary>
	/// Emit credit spend event to backend
	/// Uses user-scoped events - does not require ActivitySession
	/// </summary>
	public async Task<Result<bool>> SpendCreditsAsync(Guid playerId, int amount, string reason = "")
	{
		var payload = new
		{
			location_id = GetCurrentLocationId(),
			amount = amount,
			reason = reason
		};

		return await EmitUserEventAsync(playerId, "credit/spend", payload);
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
			$"/game/{gameTag}/machine-credits",
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
		// Build URL with query parameters
		var url = $"/game/{gameTag}/machine-credits/deposit?" +
			$"box_id={boxId}&" +
			$"player_id={playerId}&" +
			$"amount={amount}&" +
			$"lobby_session_id={lobbySessionId}";

		// POST request with no body (parameters in URL)
		return await PostAsync<object, MachineCreditsResponse>(url, null, 201);
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
		// Build URL with query parameters
		var url = $"/game/{gameTag}/machine-credits/consume?" +
			$"box_id={boxId}&" +
			$"amount={amount}&" +
			$"game_session_id={gameSessionId}";

		// POST request with no body (parameters in URL)
		return await PostAsync<object, MachineCreditsResponse>(url, null, 200);
	}

	// Private helper methods

	private async Task EnsureConnectedAsync()
	{
		_httpClient.Poll();
		var currentStatus = _httpClient.GetStatus();

#if DEBUG_HTTP_LIFECYCLE
		LogInfo($"[HTTP] EnsureConnected - Initial status: {currentStatus}");
#endif

		// Connection is ready for new requests
		if (currentStatus == Godot.HttpClient.Status.Connected)
		{
#if DEBUG_HTTP_LIFECYCLE
			LogInfo("[HTTP] Connection ready for reuse (keep-alive)");
#endif
			return;
		}

		// Handle intermediate states from previous requests
		if (currentStatus == Godot.HttpClient.Status.Body)
		{
#if DEBUG_HTTP_LIFECYCLE
			LogInfo("[HTTP] Cleaning up unconsumed response body from previous request");
#endif

			// Fully consume response body - may be chunked, requiring multiple reads
			const int MAX_DISCARD_BYTES = 10 * 1024 * 1024; // 10MB limit to prevent DOS
			int totalBytesDiscarded = 0;
			while (_httpClient.GetStatus() == Godot.HttpClient.Status.Body)
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
					throw new Exception($"Response body too large (>{MAX_DISCARD_BYTES} bytes)");
				}
			}

#if DEBUG_HTTP_LIFECYCLE
			if (totalBytesDiscarded > 0)
				LogInfo($"[HTTP] Discarded {totalBytesDiscarded} bytes of unconsumed response data");
#endif

			// Poll to advance state machine after full consumption
			_httpClient.Poll();
			currentStatus = _httpClient.GetStatus();

			if (currentStatus == Godot.HttpClient.Status.Connected)
			{
#if DEBUG_HTTP_LIFECYCLE
				LogInfo("[HTTP] Connection recovered to Connected state after cleanup");
#endif
				return; // Now ready for reuse
			}
		}

		// If still not in a good state, close and reconnect
		if (currentStatus != Godot.HttpClient.Status.Disconnected)
		{
#if DEBUG_HTTP_LIFECYCLE
			LogInfo($"[HTTP] Closing stale connection (status: {currentStatus})");
#endif

			_httpClient.Close();
			await DelayAsync(0.01f); // Brief delay for clean close
		}

		// Establish new connection
#if DEBUG_HTTP_LIFECYCLE
		LogInfo($"[HTTP] Establishing new connection to {BACKEND_HOST}:{BACKEND_PORT}");
#endif

		var error = _httpClient.ConnectToHost(BACKEND_HOST, BACKEND_PORT);
		if (error != Godot.Error.Ok)
			throw new Exception($"Failed to connect to backend: {error}");

		// Use aggressive polling utility - exits immediately when connected
		bool connected = await _httpClient.PollUntilConnectedAsync(timeoutSeconds: 5.0f);
		if (!connected)
			throw new Exception("Connection timeout");

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
			throw new Exception($"Request timeout (final status: {finalStatus})");
		}

#if DEBUG_HTTP_LIFECYCLE
		_httpClient.Poll();
		var statusAfterWait = _httpClient.GetStatus();
		LogInfo($"[HTTP] WaitForResponse - Status after wait: {statusAfterWait}");
#endif
	}

	private string GetLocationId()
	{
		var locationManager = LocationManager.GetAutoload();
		if (locationManager != null && locationManager.IsConfigLoaded)
		{
			return locationManager.LocationId;
		}

		// Fallback for rare edge cases
		return System.Environment.GetEnvironmentVariable("BARBOX_LOCATION_ID") ??
		       System.Environment.MachineName.ToLowerInvariant().Replace("-", "_");
	}

	private Guid GetBoxId()
	{
		var locationManager = LocationManager.GetAutoload();
		if (locationManager != null && locationManager.IsConfigLoaded)
		{
			return locationManager.BoxId;
		}

		// Fallback validation
		var boxIdStr = System.Environment.GetEnvironmentVariable("BARBOX_BOX_ID");
		if (string.IsNullOrEmpty(boxIdStr))
			throw new Exception("BARBOX_BOX_ID environment variable is required");

		if (!Guid.TryParse(boxIdStr, out var boxId))
			throw new Exception($"BARBOX_BOX_ID must be a valid GUID, got: {boxIdStr}");

		return boxId;
	}

	/// <summary>
	/// Generate deterministic player UUID from phone number
	/// Uses consistent hashing to ensure same phone always gets same UUID
	/// </summary>
	public static Guid GetPlayerIdFromPhone(string phoneNumber)
	{
		if (string.IsNullOrEmpty(phoneNumber))
			throw new ArgumentException("Phone number cannot be null or empty");

		// Use SHA256 for consistent hashing
		using var sha256 = System.Security.Cryptography.SHA256.Create();
		var phoneBytes = System.Text.Encoding.UTF8.GetBytes(phoneNumber);
		var hashBytes = sha256.ComputeHash(phoneBytes);

		// Take first 16 bytes for UUID
		var guidBytes = new byte[16];
		Array.Copy(hashBytes, guidBytes, 16);

		return new Guid(guidBytes);
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
