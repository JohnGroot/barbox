using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

/// <summary>
/// Event-based persistence service for BarBox backend integration
/// Forwards all game/session events to local BarBoxServices backend
/// Replaces the old DataStore CRUD pattern with event sourcing
/// </summary>
public partial class EventService : AutoloadBase
{
	private const string BASE_URL = "http://127.0.0.1:8000";
	private const int BACKEND_PORT = 8000;
	private const int REQUEST_TIMEOUT_MS = 5000;
	private const int POLL_INTERVAL_MS = 10;

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	private Godot.HttpClient _httpClient;
	private string _locationId;
	private Guid _boxId;
	private Guid _currentSessionId;
	private Guid _currentBoxId;
	private bool _isInitialized = false;

	[Signal] public delegate void EventSubmittedEventHandler(string sessionId, string eventType);
	[Signal] public delegate void EventSubmissionFailedEventHandler(string sessionId, string error);
	[Signal] public delegate void SessionCreatedEventHandler(string sessionId);

	public static EventService Instance { get; private set; }

	protected override void OnServiceReady()
	{
		Instance = this;
	}

	protected override void OnServiceInitialize()
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
			return;
		}

		// Wait for backend to be ready
		var backendManager = BackendManager.GetInstance();
		if (backendManager != null)
		{
			if (backendManager.IsBackendRunning())
			{
				OnBackendReady();
			}
			else
			{
				backendManager.BackendReady += OnBackendReady;
			}
		}
		else
		{
			LogError("BackendManager not found - EventService requires BackendManager");
		}
	}

	private async void OnBackendReady()
	{
		try
		{
			// Mark as initialized so PostAsync/QueryAsync can be used
			_isInitialized = true;

			// Register box with backend
			var locationManager = LocationManager.GetAutoload();
			if (locationManager != null && locationManager.IsConfigLoaded)
			{
				var registerResult = await RegisterBoxAsync(_boxId, _locationId);
				if (!registerResult.IsSuccess)
				{
					LogError($"Failed to register box with backend: {registerResult.Error}");
					// Continue anyway - box may already exist
				}
			}
			else
			{
				LogWarning("LocationManager not available - skipping box registration");
			}

			LogInfo($"EventService ready - backend available at {BASE_URL}");
		}
		catch (Exception ex)
		{
			LogError($"Error during EventService initialization: {ex.Message}");
			// Still mark as initialized to avoid blocking app
			_isInitialized = true;
		}
	}

	/// <summary>
	/// Create new gameplay session
	/// Call this when player logs in or starts a game
	/// </summary>
	public async Task<Result<Guid>> CreateSessionAsync(Guid boxId, Guid playerId)
	{
		if (!_isInitialized)
			return Result<Guid>.Failure("EventService not initialized - backend not ready");

		var sessionId = Guid.NewGuid();

		try
		{
			// Connect to backend
			await EnsureConnectedAsync();

			// Create session via PUT /box/{boxId}/session/{sessionId}
			var headers = new[]
			{
				$"Player-Id: {playerId}",
				"User-Agent: BarBox-Client/1.0"
			};

			var error = _httpClient.Request(
				Godot.HttpClient.Method.Put,
				$"/box/{boxId}/session/{sessionId}",
				headers
			);

			if (error != Error.Ok)
				return Result<Guid>.Failure($"Session creation request failed: {error}");

			await WaitForResponseAsync();

			var responseCode = _httpClient.GetResponseCode();
			if (responseCode != 202)
				return Result<Guid>.Failure($"Session creation failed with code {responseCode}");

			// Store current session
			_currentSessionId = sessionId;
			_currentBoxId = boxId;

			LogInfo($"Session created: {sessionId}");
			CallDeferred(MethodName.EmitSignal, SignalName.SessionCreated, sessionId.ToString());

			return Result<Guid>.Success(sessionId);
		}
		catch (Exception ex)
		{
			return Result<Guid>.Failure($"Session creation exception: {ex.Message}");
		}
	}

	/// <summary>
	/// Submit gameplay event to backend
	/// Backend handles event persistence and queuing for remote sync
	/// </summary>
	public async Task<Result<bool>> EmitEventAsync(string eventType, object payload)
	{
		if (!_isInitialized)
			return Result<bool>.Failure("EventService not initialized - backend not ready");

		if (_currentSessionId == Guid.Empty)
			return Result<bool>.Failure("No active session - call CreateSessionAsync first");

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

			if (error != Error.Ok)
			{
				var errorMsg = $"Event submission request failed: {error}";
				CallDeferred(MethodName.EmitSignal, SignalName.EventSubmissionFailed, _currentSessionId.ToString(), errorMsg);
				return Result<bool>.Failure(errorMsg);
			}

			await WaitForResponseAsync();

			var responseCode = _httpClient.GetResponseCode();
			if (responseCode == 201)
			{
				CallDeferred(MethodName.EmitSignal, SignalName.EventSubmitted, _currentSessionId.ToString(), eventType);
				return Result<bool>.Success(true);
			}

			var failureMsg = $"Event submission failed with code {responseCode}";
			CallDeferred(MethodName.EmitSignal, SignalName.EventSubmissionFailed, _currentSessionId.ToString(), failureMsg);
			return Result<bool>.Failure(failureMsg);
		}
		catch (Exception ex)
		{
			var errorMsg = $"Event submission exception: {ex.Message}";
			LogError(errorMsg);
			return Result<bool>.Failure(errorMsg);
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
	/// Execute GET query against backend API
	/// </summary>
	public async Task<Result<TResponse>> QueryAsync<TResponse>(
		string endpoint,
		Dictionary<string, string> queryParams = null
	) where TResponse : class
	{
		if (!_isInitialized)
			return Result<TResponse>.Failure("EventService not initialized");

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
			if (error != Error.Ok)
				return Result<TResponse>.Failure($"Query request failed: {error}");

			await WaitForResponseAsync();

			var responseCode = _httpClient.GetResponseCode();
			if (responseCode != 200)
			{
				// Close connection to allow recovery on next request
				_httpClient.Close();
				return Result<TResponse>.Failure($"Query failed with code {responseCode}");
			}

			var bodyBytes = _httpClient.ReadResponseBodyChunk();
			var bodyText = bodyBytes.GetStringFromUtf8();

			var response = JsonSerializer.Deserialize<TResponse>(bodyText, JsonOptions);
			return Result<TResponse>.Success(response);
		}
		catch (Exception ex)
		{
			// Close connection on exception to ensure clean state
			_httpClient.Close();
			return Result<TResponse>.Failure($"Query exception: {ex.Message}");
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
		if (!_isInitialized)
			return Result<TResponse>.Failure("EventService not initialized");

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
			if (error != Error.Ok)
				return Result<TResponse>.Failure($"POST request failed: {error}");

			await WaitForResponseAsync();

			var responseCode = _httpClient.GetResponseCode();
			if (responseCode != expectedStatusCode)
			{
				var errorBytes = _httpClient.ReadResponseBodyChunk();
				var errorText = errorBytes.GetStringFromUtf8();

				// Close connection to allow recovery on next request
				_httpClient.Close();

				return Result<TResponse>.Failure($"POST failed with code {responseCode}: {errorText}");
			}

			var bodyBytes = _httpClient.ReadResponseBodyChunk();
			var bodyText = bodyBytes.GetStringFromUtf8();

			var response = JsonSerializer.Deserialize<TResponse>(bodyText, JsonOptions);
			return Result<TResponse>.Success(response);
		}
		catch (Exception ex)
		{
			// Close connection on exception to ensure clean state
			_httpClient.Close();
			return Result<TResponse>.Failure($"POST exception: {ex.Message}");
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

		if (result.IsSuccess)
			return Result<bool>.Success(result.Value.IsAvailable);

		return Result<bool>.Failure(result.Error);
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

		if (result.IsSuccess)
		{
			if (!result.Value.Valid)
			{
				// Validation failed - construct error message from validation errors
				var errorMessages = new System.Text.StringBuilder();
				errorMessages.AppendLine("Validation failed:");
				foreach (var error in result.Value.Errors)
				{
					errorMessages.AppendLine($"  - {error.Field}: {error.Message}");
				}
				LogWarning($"Player validation failed: {errorMessages}");
			}
			return Result<PlayerValidationResponse>.Success(result.Value);
		}

		LogError($"Failed to validate player creation: {result.Error}");
		return Result<PlayerValidationResponse>.Failure(result.Error);
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

		if (result.IsSuccess)
		{
			LogInfo($"Player created successfully: {username} ({playerId})");
			return Result<Guid>.Success(playerId);
		}

		LogError($"Failed to create player: {result.Error}");
		return Result<Guid>.Failure(result.Error);
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

		var result = await PostAsync<BoxCreateRequest, BoxDetailResponse>("/box/", request, 201);

		if (result.IsSuccess)
		{
			LogInfo($"Box registered successfully: {locationName} ({boxId})");
			return Result<Guid>.Success(boxId);
		}

		// 409 Conflict means box already exists - treat as success
		if (result.Error.Contains("409") || result.Error.Contains("Conflict"))
		{
			LogInfo($"Box already registered: {locationName} ({boxId})");
			return Result<Guid>.Success(boxId);
		}

		LogError($"Failed to register box: {result.Error}");
		return Result<Guid>.Failure(result.Error);
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

		if (result.IsSuccess)
			return Result<int>.Success(result.Value.Credits);

		return Result<int>.Failure(result.Error);
	}

	/// <summary>
	/// Emit credit earn event to backend
	/// </summary>
	public async Task<Result<bool>> AddCreditsAsync(Guid playerId, int amount, string reason = "")
	{
		var payload = new
		{
			player_id = playerId.ToString(),
			location_id = GetCurrentLocationId(),
			amount = amount,
			reason = reason
		};

		return await EmitEventAsync("credit/earn", payload);
	}

	/// <summary>
	/// Emit credit spend event to backend
	/// </summary>
	public async Task<Result<bool>> SpendCreditsAsync(Guid playerId, int amount, string reason = "")
	{
		var payload = new
		{
			player_id = playerId.ToString(),
			location_id = GetCurrentLocationId(),
			amount = amount,
			reason = reason
		};

		return await EmitEventAsync("credit/spend", payload);
	}

	// Private helper methods

	private async Task EnsureConnectedAsync()
	{
		if (_httpClient.GetStatus() == Godot.HttpClient.Status.Connected)
			return;

		var error = _httpClient.ConnectToHost("127.0.0.1", BACKEND_PORT);
		if (error != Error.Ok)
			throw new Exception($"Failed to connect to backend: {error}");

		// Wait for connection (including DNS resolution)
		// CRITICAL: Must check both Resolving and Connecting states
		var attempts = 0;
		while ((_httpClient.GetStatus() == Godot.HttpClient.Status.Resolving ||
		        _httpClient.GetStatus() == Godot.HttpClient.Status.Connecting) &&
		       attempts < 50)
		{
			// CRITICAL: Poll() advances the HttpClient state machine
			_httpClient.Poll();
			await DelayAsync(0.1f);
			attempts++;
		}

		if (_httpClient.GetStatus() != Godot.HttpClient.Status.Connected)
			throw new Exception("Connection timeout");
	}

	private async Task WaitForResponseAsync()
	{
		var attempts = 0;
		var maxAttempts = REQUEST_TIMEOUT_MS / POLL_INTERVAL_MS;

		while (_httpClient.GetStatus() == Godot.HttpClient.Status.Requesting && attempts < maxAttempts)
		{
			// CRITICAL: Poll() processes the response
			_httpClient.Poll();
			await DelayAsync(POLL_INTERVAL_MS / 1000.0f);
			attempts++;
		}

		if (attempts >= maxAttempts)
			throw new Exception("Request timeout");
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

	protected override void OnServiceDestroyed()
	{
		_httpClient?.Close();
		_httpClient = null;

		Instance = null;
	}
}
