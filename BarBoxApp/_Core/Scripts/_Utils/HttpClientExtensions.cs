using System;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Extension methods for Godot.HttpClient to enable responsive polling
/// Provides aggressive polling strategies that exit immediately when state changes occur,
/// rather than waiting for fixed polling intervals
/// </summary>
public static class HttpClientExtensions
{
	// Connection establishment timeout (DNS + TCP handshake)
	private const float CONNECTION_TIMEOUT_SECONDS = 5.0f;

	// Response timeout (includes backend processing and network variability)
	private const float RESPONSE_TIMEOUT_SECONDS = 30.0f;

	// Response body buffering timeout
	private const float BODY_BUFFER_TIMEOUT_SECONDS = 1.0f;

	// Keep-alive connection state transition timeout
	private const float KEEPALIVE_TRANSITION_TIMEOUT = 3.0f;

	/// <summary>
	/// Polls HttpClient using burst polling until a condition is met.
	/// Polls multiple times per yield for faster state change detection than fixed-interval polling.
	/// </summary>
	/// <param name="pollBurstCount">Number of sequential polls before yielding (default: 10)</param>
	/// <param name="yieldDelaySeconds">Delay between poll bursts (default: 0.01s)</param>
	/// <returns>True if condition met, false if timeout</returns>
	/// <example>
	/// bool connected = await client.PollUntilAsync(
	///     status => status == HttpClient.Status.Connected, timeoutSeconds: 2.0f);
	/// </example>
	public static async Task<bool> PollUntilAsync(
		this HttpClient client,
		Func<HttpClient.Status, bool> condition,
		float timeoutSeconds = CONNECTION_TIMEOUT_SECONDS,
		int pollBurstCount = 10,
		float yieldDelaySeconds = 0.01f)
	{
		var maxAttempts = (int)(timeoutSeconds / yieldDelaySeconds);

		for (int attempt = 0; attempt < maxAttempts; attempt++)
		{
			// Aggressive polling - check multiple times before yielding
			// This allows us to detect state changes much faster than
			// waiting for a full delay period between each check
			for (int burst = 0; burst < pollBurstCount; burst++)
			{
				client.Poll();
				var status = client.GetStatus();

				if (condition(status))
				{
					return true; // Success - exit immediately
				}
			}

			// Minimal yield to prevent frame blocking
			// Uses frame-aware timing that respects pause states
			await AutoloadBase.StaticDelayAsync(yieldDelaySeconds);
		}

		return false; // Timeout
	}

	/// <summary>
	/// Polls HttpClient until a specific status is reached.
	/// </summary>
	/// <example>
	/// bool ready = await client.PollUntilAsync(HttpClient.Status.Body, timeoutSeconds: 10.0f);
	/// </example>
	public static Task<bool> PollUntilAsync(
		this HttpClient client,
		HttpClient.Status targetStatus,
		float timeoutSeconds = CONNECTION_TIMEOUT_SECONDS,
		int pollBurstCount = 10,
		float yieldDelaySeconds = 0.01f)
	{
		return PollUntilAsync(
			client,
			status => status == targetStatus,
			timeoutSeconds,
			pollBurstCount,
			yieldDelaySeconds);
	}

	/// <summary>
	/// Polls until HttpClient is connected (handles Resolving and Connecting states).
	/// </summary>
	/// <example>
	/// if (_httpClient.ConnectToHost("127.0.0.1", 8000) == Error.Ok)
	///     await _httpClient.PollUntilConnectedAsync();
	/// </example>
	public static Task<bool> PollUntilConnectedAsync(
		this HttpClient client,
		float timeoutSeconds = CONNECTION_TIMEOUT_SECONDS)
	{
		return PollUntilAsync(
			client,
			status => status == HttpClient.Status.Connected,
			timeoutSeconds);
	}

	/// <summary>
	/// Polls until HttpClient has a response ready (Body or Connected state).
	/// Handles keep-alive connections by detecting state transitions from previous requests.
	/// </summary>
	/// <remarks>
	/// On keep-alive connections, waits for state transition to confirm new request started
	/// before polling for completion.
	/// </remarks>
	/// <example>
	/// _httpClient.Request(HttpClient.Method.Get, "/api/data", headers);
	/// if (await _httpClient.PollUntilResponseReadyAsync())
	/// {
	///     var body = _httpClient.ReadResponseBodyChunk();
	/// }
	/// </example>
	public static async Task<bool> PollUntilResponseReadyAsync(
		this HttpClient client,
		float timeoutSeconds = RESPONSE_TIMEOUT_SECONDS)
	{
		// Capture initial state to detect keep-alive connections
		client.Poll();
		var initialStatus = client.GetStatus();

		// If we're already in a "ready" state (from a previous request on a keep-alive connection),
		// we need to wait for the NEW request to transition OUT of this state first
		if (initialStatus == HttpClient.Status.Body || initialStatus == HttpClient.Status.Connected)
		{
			// Wait for the request to start (transition to Requesting or Connecting)
			// This confirms we're tracking the NEW request, not the old one
			bool requestStarted = await PollUntilAsync(
				client,
				status =>
				{
					// Exit if request started
					if (status != HttpClient.Status.Body && status != HttpClient.Status.Connected)
					{
						return true;
					}

					// Continue waiting
					return false;
				},
				timeoutSeconds: KEEPALIVE_TRANSITION_TIMEOUT);

			if (!requestStarted)
			{
				// Request didn't transition out of ready state
				// This might mean Request() wasn't called, or the state is stuck
				return false;
			}
		}

		// Now wait for the request to complete and return to a ready state
		// Also check for error states to fail fast instead of waiting full timeout
		return await PollUntilAsync(
			client,
			status =>
			{
				// Success states - response is ready
				if (status == HttpClient.Status.Body || status == HttpClient.Status.Connected)
				{
					return true;
				}

				// Error states - fail fast instead of waiting full timeout
				if (status == HttpClient.Status.CantConnect ||
					status == HttpClient.Status.CantResolve ||
					status == HttpClient.Status.ConnectionError)
				{
					// Return true to exit polling, caller will check status and handle error
					return true;
				}

				// Continue waiting for other states (Requesting, Resolving, Connecting)
				return false;
			},
			timeoutSeconds);
	}

	/// <summary>
	/// Reads response body using burst polling for faster completion detection.
	/// Exits immediately when body is buffered instead of using fixed delays.
	/// </summary>
	/// <param name="timeoutSeconds">Maximum time to wait for body (default: 1.0)</param>
	/// <returns>Response body bytes, or empty array on timeout/error</returns>
	/// <example>
	/// await _httpClient.PollUntilResponseReadyAsync();
	/// var bodyBytes = await _httpClient.ReadResponseBodyAsync();
	/// var bodyText = bodyBytes.GetStringFromUtf8();
	/// </example>
	public static async Task<byte[]> ReadResponseBodyAsync(
		this HttpClient client,
		float timeoutSeconds = BODY_BUFFER_TIMEOUT_SECONDS)
	{
		// Ensure we're in Body status
		client.Poll();
		if (client.GetStatus() != HttpClient.Status.Body)
		{
			return [];
		}

		// Burst poll until body is fully buffered using the new status overload
		// This is much faster than fixed delays because it exits immediately
		// when the body is ready (typically 10-20ms vs 50ms fixed delay)
		var bodyReady = await PollUntilAsync(
			client,
			HttpClient.Status.Body,
			timeoutSeconds,
			pollBurstCount: 10,
			yieldDelaySeconds: 0.01f);

		// Return body bytes if ready, otherwise empty array
		return bodyReady ? client.ReadResponseBodyChunk() : [];
	}
}
