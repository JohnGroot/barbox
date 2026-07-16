using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using LightResults;

namespace BarBox.Core.Autoloads;

/// <summary>
/// Sole HTTP transport layer for backend communication.
/// Owns the HttpClient connection lifecycle, header building, and the generic
/// query/post/put verbs. Domain services (SessionEventService, CreditService, etc.)
/// build their URLs/payloads and call through this - no other class should
/// touch Godot's HttpClient directly.
/// </summary>
public partial class BackendClient : AutoloadBase
{
	// Configuration - initialized in OnServiceInitializeAsync after LocationManager loads .env files
	// (Static readonly fields would capture env vars before .env is loaded!)
	private string _baseUrl;
	private string _backendHost;
	private int _backendPort;
	private bool _useHttps;

	private const int REQUEST_TIMEOUT_MS = 5000;

	private const string NotReadyError = "BackendClient not initialized - backend not ready";
	private const string AcceptJsonHeader = "Accept: application/json";
	private const string ContentTypeJsonHeader = "Content-Type: application/json";

	public static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
	};

	private HttpClient _httpClient;
	private Guid _boxId;
	private string _boxApiKey;

	/// <summary>
	/// Supplies the JWT Bearer token for a given player ID when building authenticated
	/// request headers. Set by SessionManager during its own initialization - this
	/// delegate is what lets BackendClient add JWT headers without depending on the
	/// SessionManager type (which itself depends on BackendClient via SessionEventService).
	/// </summary>
	public Func<Guid, string> JwtTokenProvider { get; set; }

	protected override async Task OnServiceInitializeAsync(CancellationToken cancellationToken = default)
	{
		_httpClient = new HttpClient();

		// Load configuration AFTER LocationManager has loaded .env files
		// (Static readonly fields would capture env vars before .env is loaded!)
		var locationManager = LocationManager.GetAutoload();
		_baseUrl = locationManager?.BackendUrl
			?? System.Environment.GetEnvironmentVariable("BARBOX_BACKEND_URL")
			?? "http://127.0.0.1:8000";

		var parsedUrl = new Uri(_baseUrl);
		_backendHost = parsedUrl.Host;
		_backendPort = parsedUrl.Port;
		_useHttps = parsedUrl.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);

		// Log backend configuration at startup for debugging
		LogInfo($"Backend configuration: {_backendHost}:{_backendPort} (HTTPS: {_useHttps})");
		LogInfo($"BASE_URL: {_baseUrl}");

		// Load box identity from LocationManager (reuse variable from above)
		if (locationManager == null || !locationManager.IsConfigLoaded)
		{
			throw new InvalidOperationException("LocationManager is required but not available or not initialized");
		}

		try
		{
			_boxId = locationManager.BoxId;
			_boxApiKey = GetBoxApiKey();
			LogInfo($"Box ID: {_boxId}, API Key: {(string.IsNullOrEmpty(_boxApiKey) ? "NOT SET" : "SET")}");

			// FAIL LOUDLY in editor mode if API key missing
			if (string.IsNullOrEmpty(_boxApiKey) && OS.HasFeature("editor"))
			{
				LogError("╔════════════════════════════════════════════════════════════╗");
				LogError("║ CRITICAL: BackendClient API Key Configuration Missing!     ║");
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
			throw new InvalidOperationException($"BackendClient requires valid Box configuration: {ex.Message}\nStack Trace: {ex.StackTrace}");
		}

		// Wait for BackendManager to be ready
		var backendManager = BackendManager.GetInstance() ?? throw new InvalidOperationException("BackendManager not found - BackendClient requires BackendManager");
		LogInfo("Waiting for BackendManager to become ready...");
		var backendReady = await backendManager.WaitForReadyAsync(timeoutSeconds: 30.0f, cancellationToken);

		if (!backendReady)
		{
			throw new InvalidOperationException("BackendManager failed to become ready within timeout");
		}

		LogInfo($"BackendClient ready - backend available at {_baseUrl}");
	}

	/// <summary>
	/// Execute GET query against backend API
	/// </summary>
	/// <param name="playerId">Optional. If provided, includes player JWT token in Authorization header.</param>
	public async Task<Result<TResponse>> QueryAsync<TResponse>(
		string endpoint,
		Dictionary<string, string> queryParams = null,
		Guid? playerId = null)
		where TResponse : class
	{
		if (!IsReady)
		{
			return Result.Failure<TResponse>(NotReadyError);
		}

		try
		{
			await EnsureConnectedAsync();

			var url = endpoint;
			if (queryParams != null && queryParams.Count > 0)
			{
				var query = string.Join(
					"&",
					queryParams.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
				url = $"{endpoint}?{query}";
			}

			var headers = BuildHeaders(playerId);
			headers.Add(AcceptJsonHeader);
			var headersArray = headers.ToArray();

			var error = _httpClient.Request(HttpClient.Method.Get, url, headersArray);
			if (error != Godot.Error.Ok)
			{
				return Result.Failure<TResponse>($"Query request failed: {error}");
			}

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
		Guid? playerId = null)
	{
		if (!IsReady)
		{
			return Result.Failure<string>(NotReadyError);
		}

		try
		{
			await EnsureConnectedAsync();

			var url = endpoint;
			if (queryParams != null && queryParams.Count > 0)
			{
				var query = string.Join(
					"&",
					queryParams.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
				url = $"{endpoint}?{query}";
			}

			var headers = BuildHeaders(playerId);
			headers.Add(AcceptJsonHeader);
			var headersArray = headers.ToArray();

			var error = _httpClient.Request(HttpClient.Method.Get, url, headersArray);
			if (error != Godot.Error.Ok)
			{
				return Result.Failure<string>($"Query request failed: {error}");
			}

			await WaitForResponseAsync();

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
							GD.PrintErr($"[BackendClient] Backend error:");
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
							GD.PrintErr($"[BackendClient] HTTP {responseCode} error (raw): {bodyText}");
						}
					}
					catch (Exception parseEx)
					{
						// Parsing failed, use raw error
						errorMessage = $"Query failed with code {responseCode}: {bodyText}";
						GD.PrintErr($"[BackendClient] Failed to parse error response: {parseEx.Message}");
						GD.PrintErr($"[BackendClient] Raw error: {bodyText}");
					}
				}
				else
				{
					GD.PrintErr($"[BackendClient] HTTP {responseCode} with empty response body");
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
		Guid? playerId = null)
		where TResponse : class
	{
		if (!IsReady)
		{
			return Result.Failure<TResponse>(NotReadyError);
		}

		try
		{
			await EnsureConnectedAsync();

			var json = JsonSerializer.Serialize(requestBody, JsonOptions);

			var headers = BuildJsonHeaders(playerId).ToArray();

			var error = _httpClient.Request(HttpClient.Method.Post, endpoint, headers, json);
			if (error != Godot.Error.Ok)
			{
				return Result.Failure<TResponse>($"POST request failed: {error}");
			}

			await WaitForResponseAsync();

			var responseCode = _httpClient.GetResponseCode();
			if (responseCode != expectedStatusCode)
			{
				var errorBytes = await _httpClient.ReadResponseBodyAsync();
				var errorText = errorBytes.GetStringFromUtf8();

				// Close connection to allow recovery on next request
				_httpClient.Close();

				// Extract user-friendly error message from backend response
				var userFriendlyError = BarBox.Core.Utils.ErrorMessageHelper.GetUserFriendlyError(errorText);
				return Result.Failure<TResponse>(userFriendlyError);
			}

			// Ensure response body is fully received (Godot HttpClient timing issue)
			var bodyBytes = await _httpClient.ReadResponseBodyAsync();
			var bodyText = bodyBytes.GetStringFromUtf8();

#if DEBUG_HTTP_LIFECYCLE
			LogInfo($"[HTTP] PostAsync response: code={responseCode}, bodyLength={bodyBytes.Length}");
#endif

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
	/// <param name="extraHeaders">Optional. Additional raw header lines appended after the standard JSON headers.</param>
	public async Task<Result<TResponse>> PutAsync<TRequest, TResponse>(
		string endpoint,
		TRequest requestBody,
		int expectedStatusCode = 200,
		Guid? playerId = null,
		IEnumerable<string> extraHeaders = null)
		where TResponse : class
	{
		if (!IsReady)
		{
			return Result.Failure<TResponse>(NotReadyError);
		}

		try
		{
			await EnsureConnectedAsync();

			var json = JsonSerializer.Serialize(requestBody, JsonOptions);

			var headerList = BuildJsonHeaders(playerId);
			if (extraHeaders != null)
			{
				headerList.AddRange(extraHeaders);
			}

			var headers = headerList.ToArray();

			var error = _httpClient.Request(HttpClient.Method.Put, endpoint, headers, json);
			if (error != Godot.Error.Ok)
			{
				return Result.Failure<TResponse>($"PUT request failed: {error}");
			}

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
	/// POST a pre-serialized JSON event body to a session endpoint (/box/session/{sessionId}).
	/// Shared primitive behind SessionEventService.EmitEventAsync / EmitUserEventAsync.
	/// Returns the raw response body on 201; callers interpret/log domain-specific errors.
	/// </summary>
	public async Task<Result<string>> PostToSessionAsync(Guid sessionId, string json, Guid? playerId = null)
	{
		if (!IsReady)
		{
			return Result.Failure<string>(NotReadyError);
		}

		try
		{
			await EnsureConnectedAsync();

			var headers = BuildJsonHeaders(playerId).ToArray();
			var url = $"/box/session/{sessionId}";

			var error = _httpClient.Request(HttpClient.Method.Post, url, headers, json);
			if (error != Godot.Error.Ok)
			{
				return Result.Failure<string>($"Session event submission request failed: {error}");
			}

			await WaitForResponseAsync();

			var responseCode = _httpClient.GetResponseCode();
			var bodyBytes = await _httpClient.ReadResponseBodyAsync();
			var bodyText = bodyBytes.GetStringFromUtf8();

			if (responseCode != 201)
			{
				var suffix = string.IsNullOrEmpty(bodyText) ? string.Empty : $": {bodyText}";
				return Result.Failure<string>($"Session event submission failed with code {responseCode}{suffix}");
			}

			return Result.Success(bodyText);
		}
		catch (Exception ex)
		{
			return Result.Failure<string>($"Session event submission exception: {ex.Message}");
		}
	}

	/// <summary>
	/// Build HTTP headers with Box API key and optional player Authorization token
	/// </summary>
	/// <param name="playerId">Optional. If provided, includes player JWT token in Authorization header.</param>
	internal List<string> BuildHeaders(Guid? playerId = null)
	{
		if (string.IsNullOrEmpty(_boxApiKey))
		{
			LogError("CRITICAL: Attempting HTTP request without API key configured!");
			throw new InvalidOperationException("Box API key not configured - check .env.local file");
		}

		// Capacity 6 covers the largest caller shape (BuildJsonHeaders + JWT: 3 base + Authorization + Accept + Content-Type)
		// without a second internal array resize.
		var headers = new List<string>(6)
		{
			$"X-Box-API-Key: {_boxApiKey}",
			$"X-Box-ID: {_boxId}",
			"User-Agent: BarBox-Client/1.0",
		};

		if (!playerId.HasValue || playerId.Value == Guid.Empty)
		{
			return headers;
		}

		var jwtToken = JwtTokenProvider?.Invoke(playerId.Value);
		if (string.IsNullOrEmpty(jwtToken))
		{
			GD.PushError($"[BackendClient] Authenticated request for player {playerId.Value} failed: JWT token not found or provider not registered");
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
		headers.Add(AcceptJsonHeader);
		headers.Add(ContentTypeJsonHeader);
		return headers;
	}

	internal List<string> BuildPlayerHeaders(Guid playerId)
	{
		var headers = BuildHeaders();
		headers.Add($"Player-Id: {playerId}");
		return headers;
	}

	internal async Task EnsureConnectedAsync()
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
				{
					break;
				}

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
				return;
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

#if DEBUG_HTTP_LIFECYCLE
		LogInfo($"[HTTP] Establishing new connection to {_backendHost}:{_backendPort} (HTTPS: {_useHttps})");
#endif

		TlsOptions tlsOptions = null;
		if (_useHttps)
		{
			// Pass hostname for SNI (Server Name Indication) - required for Fly.io's shared IP infrastructure
			tlsOptions = TlsOptions.Client(null, _backendHost);
		}

		var error = _httpClient.ConnectToHost(_backendHost, _backendPort, tlsOptions);
		if (error != Godot.Error.Ok)
		{
			throw new InvalidOperationException($"Failed to connect to backend: {error}");
		}

		// Use aggressive polling utility - exits immediately when connected
		bool connected = await _httpClient.PollUntilConnectedAsync(timeoutSeconds: 5.0f);
		if (!connected)
		{
			throw new InvalidOperationException("Connection timeout");
		}

#if DEBUG_HTTP_LIFECYCLE
		LogInfo("[HTTP] New connection established successfully");
#endif
	}

	internal async Task WaitForResponseAsync()
	{
#if DEBUG_HTTP_LIFECYCLE
		_httpClient.Poll();
		var statusBeforeWait = _httpClient.GetStatus();
		LogInfo($"[HTTP] WaitForResponse - Status before wait: {statusBeforeWait}");
#endif

		// Use aggressive polling utility - exits immediately when response ready
		bool ready = await _httpClient.PollUntilResponseReadyAsync(
			timeoutSeconds: REQUEST_TIMEOUT_MS / 1000.0f);

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

		var apiKey = System.Environment.GetEnvironmentVariable("BARBOX_API_KEY");
		if (string.IsNullOrEmpty(apiKey))
		{
			LogWarning("BARBOX_API_KEY not set - authenticated requests will fail");
			return string.Empty;
		}

		return apiKey;
	}

	// Forwarding members giving SessionEventService's raw-HttpClient session methods
	// (CreateActivitySessionAsync/CloseActivitySessionAsync/CreateLobbySessionAsync)
	// access to _httpClient, which SessionEventService does not own directly.
	internal Godot.Error Request(HttpClient.Method method, string url, string[] headers, string body = null)
	{
		return body == null
			? _httpClient.Request(method, url, headers)
			: _httpClient.Request(method, url, headers, body);
	}

	internal void Poll() => _httpClient.Poll();

	internal HttpClient.Status GetStatus() => _httpClient.GetStatus();

	internal int GetResponseCode() => _httpClient.GetResponseCode();

	internal byte[] ReadResponseBodyChunk() => _httpClient.ReadResponseBodyChunk();

	internal Task<byte[]> ReadResponseBodyAsync() => _httpClient.ReadResponseBodyAsync();

	internal void Close() => _httpClient.Close();

	public static BackendClient GetInstance()
	{
		return GetAutoload<BackendClient>();
	}

	protected override void OnServiceDestroyed()
	{
		_httpClient?.Close();
		_httpClient = null;
	}
}
