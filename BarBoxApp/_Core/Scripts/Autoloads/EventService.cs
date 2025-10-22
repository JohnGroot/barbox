using Godot;
using System;
using System.Collections.Generic;
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
	private const string BASE_URL = "http://localhost:8000";
	private const int BACKEND_PORT = 8000;
	private const int REQUEST_TIMEOUT_MS = 5000;
	private const int POLL_INTERVAL_MS = 10;

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	private HttpClient _httpClient;
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
		_httpClient = new HttpClient();
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

	private void OnBackendReady()
	{
		_isInitialized = true;
		LogInfo($"EventService ready - backend available at {BASE_URL}");
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
				HttpClient.Method.Put,
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
				HttpClient.Method.Post,
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

	// Private helper methods

	private async Task EnsureConnectedAsync()
	{
		if (_httpClient.GetStatus() == HttpClient.Status.Connected)
			return;

		var error = _httpClient.ConnectToHost("localhost", BACKEND_PORT);
		if (error != Error.Ok)
			throw new Exception($"Failed to connect to backend: {error}");

		// Wait for connection
		var attempts = 0;
		while (_httpClient.GetStatus() == HttpClient.Status.Connecting && attempts < 50)
		{
			await DelayAsync(0.1f);
			attempts++;
		}

		if (_httpClient.GetStatus() != HttpClient.Status.Connected)
			throw new Exception("Connection timeout");
	}

	private async Task WaitForResponseAsync()
	{
		var attempts = 0;
		var maxAttempts = REQUEST_TIMEOUT_MS / POLL_INTERVAL_MS;

		while (_httpClient.GetStatus() == HttpClient.Status.Requesting && attempts < maxAttempts)
		{
			await DelayAsync(POLL_INTERVAL_MS / 1000.0f);
			attempts++;
		}

		if (attempts >= maxAttempts)
			throw new Exception("Request timeout");
	}

	private string GetLocationId()
	{
		return System.Environment.GetEnvironmentVariable("BARBOX_LOCATION_ID") ??
		       System.Environment.MachineName.ToLowerInvariant().Replace("-", "_");
	}

	private Guid GetBoxId()
	{
		var boxIdStr = System.Environment.GetEnvironmentVariable("BARBOX_BOX_ID");
		if (string.IsNullOrEmpty(boxIdStr))
		{
			throw new Exception("BARBOX_BOX_ID environment variable is required");
		}

		if (!Guid.TryParse(boxIdStr, out var boxId))
		{
			throw new Exception($"BARBOX_BOX_ID must be a valid GUID, got: {boxIdStr}");
		}

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
