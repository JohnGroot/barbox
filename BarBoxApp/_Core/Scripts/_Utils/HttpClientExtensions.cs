using Godot;
using System;
using System.Threading.Tasks;

/// <summary>
/// Extension methods for Godot.HttpClient to enable responsive polling
/// Provides aggressive polling strategies that exit immediately when state changes occur,
/// rather than waiting for fixed polling intervals
/// </summary>
public static class HttpClientExtensions
{
	/// <summary>
	/// Poll HttpClient until a condition is met, using aggressive polling strategy
	///
	/// This method polls the HttpClient state machine multiple times in rapid succession
	/// before yielding to the game loop, allowing it to detect state changes much faster
	/// than traditional fixed-interval polling.
	/// </summary>
	/// <param name="client">The HttpClient to poll</param>
	/// <param name="condition">Predicate that returns true when desired state is reached</param>
	/// <param name="timeoutSeconds">Maximum time to wait in seconds (default: 5.0)</param>
	/// <param name="pollBurstCount">Number of polls per yield (default: 10)</param>
	/// <param name="yieldDelaySeconds">Delay between poll bursts in seconds (default: 0.01)</param>
	/// <returns>True if condition met, false if timeout</returns>
	/// <example>
	/// // Wait for connection with custom timeout
	/// bool connected = await _httpClient.PollUntilAsync(
	///     status => status == HttpClient.Status.Connected,
	///     timeoutSeconds: 2.0f
	/// );
	/// </example>
	public static async Task<bool> PollUntilAsync(
		this HttpClient client,
		Func<HttpClient.Status, bool> condition,
		float timeoutSeconds = 5.0f,
		int pollBurstCount = 10,
		float yieldDelaySeconds = 0.01f
	)
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
					return true; // Success - exit immediately
			}

			// Minimal yield to prevent frame blocking
			// Uses frame-aware timing that respects pause states
			await AutoloadBase.StaticDelayAsync(yieldDelaySeconds);
		}

		return false; // Timeout
	}

	/// <summary>
	/// Poll until HttpClient reaches a specific status
	/// </summary>
	/// <param name="client">The HttpClient to poll</param>
	/// <param name="targetStatus">The status to wait for</param>
	/// <param name="timeoutSeconds">Maximum time to wait in seconds (default: 5.0)</param>
	/// <returns>True if target status reached, false if timeout</returns>
	/// <example>
	/// bool ready = await _httpClient.PollUntilStatusAsync(HttpClient.Status.Body);
	/// </example>
	public static Task<bool> PollUntilStatusAsync(
		this HttpClient client,
		HttpClient.Status targetStatus,
		float timeoutSeconds = 5.0f
	)
	{
		return PollUntilAsync(
			client,
			status => status == targetStatus,
			timeoutSeconds
		);
	}

	/// <summary>
	/// Poll until HttpClient is connected (handles Resolving and Connecting states)
	///
	/// This is the most common operation - waiting for a connection to be established.
	/// It will exit as soon as the Connected state is reached, regardless of how long
	/// it was in Resolving or Connecting states.
	/// </summary>
	/// <param name="client">The HttpClient to poll</param>
	/// <param name="timeoutSeconds">Maximum time to wait in seconds (default: 5.0)</param>
	/// <returns>True if connected, false if timeout</returns>
	/// <example>
	/// var error = _httpClient.ConnectToHost("127.0.0.1", 8000);
	/// if (error == Error.Ok)
	/// {
	///     bool connected = await _httpClient.PollUntilConnectedAsync();
	///     if (!connected)
	///         throw new Exception("Connection timeout");
	/// }
	/// </example>
	public static Task<bool> PollUntilConnectedAsync(
		this HttpClient client,
		float timeoutSeconds = 5.0f
	)
	{
		return PollUntilAsync(
			client,
			status => status == HttpClient.Status.Connected,
			timeoutSeconds
		);
	}

	/// <summary>
	/// Poll until HttpClient has response available for a NEW request (Body or Connected state)
	///
	/// This method properly handles keep-alive connections by detecting when a NEW request
	/// has completed, not just checking if the client is in a ready state from a PREVIOUS request.
	///
	/// When reusing a keep-alive connection:
	/// 1. Client starts in Status.Connected (from previous request)
	/// 2. Request() is called, transitions to Status.Requesting
	/// 3. Response arrives, transitions to Status.Body or Status.Connected
	///
	/// This method detects the state transition to ensure we're waiting for the NEW request,
	/// not incorrectly detecting the PREVIOUS request's lingering Connected state.
	///
	/// After a request completes, HttpClient can transition to either:
	/// - Status.Body: Response body is buffered and ready to read
	/// - Status.Connected: Response body was consumed, connection is keep-alive
	/// </summary>
	/// <param name="client">The HttpClient to poll</param>
	/// <param name="timeoutSeconds">Maximum time to wait in seconds (default: 30.0)</param>
	/// <returns>True if response ready, false if timeout</returns>
	/// <example>
	/// _httpClient.Request(HttpClient.Method.Get, "/api/data", headers);
	/// bool ready = await _httpClient.PollUntilResponseReadyAsync(timeoutSeconds: 10.0f);
	/// if (ready)
	/// {
	///     var responseCode = _httpClient.GetResponseCode();
	///     var body = _httpClient.ReadResponseBodyChunk();
	/// }
	/// </example>
	public static async Task<bool> PollUntilResponseReadyAsync(
		this HttpClient client,
		float timeoutSeconds = 30.0f
	)
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
			// Use a shorter timeout for this transition (should be immediate)
			bool requestStarted = await PollUntilAsync(
				client,
				status => status != HttpClient.Status.Body && status != HttpClient.Status.Connected,
				timeoutSeconds: 1.0f
			);

			if (!requestStarted)
			{
				// Request didn't transition out of ready state
				// This might mean Request() wasn't called, or the state is stuck
				return false;
			}
		}

		// Now wait for the request to complete and return to a ready state
		return await PollUntilAsync(
			client,
			// Accept both valid response-ready states
			status => status == HttpClient.Status.Body || status == HttpClient.Status.Connected,
			timeoutSeconds
		);
	}
}
