using System;
using System.Threading.Tasks;
using Godot;

namespace BarBox.Tests.Fixtures;

/// <summary>
/// Shared test utilities and helper methods for the test suite.
/// </summary>
public static class TestHelpers
{
	/// <summary>
	/// Generate a unique test identifier for isolation
	/// </summary>
	public static string GenerateTestId(string prefix = "test")
	{
		return $"{prefix}_{Guid.NewGuid():N}".Substring(0, 20);
	}

	/// <summary>
	/// Seeded test box ID that matches backend test seed data.
	/// This box ID is created by the test backend seed endpoint and has an associated API key.
	/// Tests MUST use this box ID to authenticate successfully.
	/// </summary>
	public static readonly Guid SeededTestBoxId = TestConstants.TEST_BOX_ID_1;

	/// <summary>
	/// Get a seeded test player ID and credentials for integration tests.
	/// These players are pre-registered in the test database.
	/// Use these instead of GenerateTestPlayerId() for backend integration tests.
	/// </summary>
	public static (Guid playerId, string phone, string pin, string username) GetSeededTestPlayer(int playerNumber = 1)
	{
		return playerNumber switch
		{
			1 => (TestConstants.TEST_PLAYER_ID_1, TestConstants.TEST_PLAYER_1_PHONE,
			      TestConstants.TEST_PLAYER_1_PIN, TestConstants.TEST_PLAYER_1_USERNAME),
			2 => (TestConstants.TEST_PLAYER_ID_2, TestConstants.TEST_PLAYER_2_PHONE,
			      TestConstants.TEST_PLAYER_2_PIN, TestConstants.TEST_PLAYER_2_USERNAME),
			3 => (TestConstants.TEST_PLAYER_ID_3, TestConstants.TEST_PLAYER_3_PHONE,
			      TestConstants.TEST_PLAYER_3_PIN, TestConstants.TEST_PLAYER_3_USERNAME),
			_ => throw new ArgumentException($"Player number must be 1-3, got {playerNumber}")
		};
	}

	/// <summary>
	/// Create a unique box ID for testing.
	/// WARNING: For backend integration tests, use SeededTestBoxId instead.
	/// Random box IDs will fail authentication because they don't match the API key.
	/// </summary>
	public static Guid GenerateTestBoxId()
	{
		return Guid.NewGuid();
	}

	/// <summary>
	/// Create a unique player ID for testing
	/// </summary>
	public static Guid GenerateTestPlayerId()
	{
		return Guid.NewGuid();
	}

	/// <summary>
	/// Create a unique session ID for testing
	/// </summary>
	public static Guid GenerateTestSessionId()
	{
		return Guid.NewGuid();
	}

	/// <summary>
	/// Wait for a condition to be true with timeout.
	/// Uses more frequent polling for faster response while maintaining timeout accuracy.
	/// </summary>
	/// <param name="condition">Condition to check</param>
	/// <param name="timeoutSeconds">Timeout in seconds</param>
	/// <param name="pollDelaySeconds">Delay between checks in seconds (default 50ms for fast response)</param>
	/// <returns>True if condition became true, false if timeout</returns>
	public static async Task<bool> WaitForConditionAsync(
		Func<bool> condition,
		float timeoutSeconds = TestConfig.DefaultTimeoutSeconds,
		float pollDelaySeconds = 0.05f) // Faster polling by default (50ms)
	{
		var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

		while (DateTime.UtcNow < deadline)
		{
			if (condition())
			{
				return true;
			}

			await AutoloadBase.StaticDelayAsync(pollDelaySeconds);
		}

		return false;
	}

	/// <summary>
	/// Check if the test backend is healthy and responding
	/// </summary>
	public static async Task<bool> IsTestBackendHealthyAsync()
	{
		var (success, responseCode, _) = await ExecuteSimpleHttpRequestAsync(
			Godot.HttpClient.Method.Get,
			"/alive"
		);

		return success && responseCode == 200;
	}

	/// <summary>
	/// Create a unique username for testing (max 7 chars as per requirements)
	/// </summary>
	public static string GenerateTestUsername()
	{
		return $"tst{Guid.NewGuid():N}".Substring(0, 7);
	}

	/// <summary>
	/// Create a deterministic test phone number based on test name or seed.
	/// This ensures reproducible test data and prevents collisions.
	/// Returns E.164 format (+1XXXXXXXXXX) as required by backend.
	/// Uses XXX-555-01XX format which is reserved for testing/fictional use and passes libphonenumber validation.
	/// </summary>
	/// <param name="seed">Seed for deterministic generation (use test name hash)</param>
	public static string GenerateTestPhoneNumber(int seed)
	{
		var random = new Random(seed);
		// Format: +1-XXX-555-01XX (testing/fictional number range)
		// Area code: Valid US area code (212=NYC, 415=SF, 310=LA, etc.)
		// Exchange: 555 (reserved for testing/fictional use)
		// Suffix: 01XX (reserved range 0100-0199 for testing)
		var areaCodes = new[] { 212, 415, 310, 312, 617, 202, 713, 305, 602, 503 };
		var areaCode = areaCodes[random.Next(areaCodes.Length)];
		var suffix = random.Next(100, 200); // 0100-0199 reserved for testing
		return $"+1{areaCode}555{suffix:D4}"; // E.164 format: +1-XXX-555-01XX
	}

	/// <summary>
	/// Create a test phone number with random generation.
	/// WARNING: Non-deterministic. Prefer GenerateTestPhoneNumber(int seed) for reproducible tests.
	/// Returns E.164 format (+1XXXXXXXXXX) as required by backend.
	/// Uses XXX-555-01XX format which is reserved for testing/fictional use.
	/// </summary>
	[Obsolete("Use GenerateTestPhoneNumber(int seed) for deterministic test data")]
	public static string GenerateTestPhoneNumber()
	{
		var random = new Random();
		var areaCodes = new[] { 212, 415, 310, 312, 617, 202, 713, 305, 602, 503 };
		var areaCode = areaCodes[random.Next(areaCodes.Length)];
		var suffix = random.Next(100, 200); // 0100-0199 reserved for testing
		return $"+1{areaCode}555{suffix:D4}"; // E.164 format: +1-XXX-555-01XX
	}

	/// <summary>
	/// Generate a deterministic test identifier based on test name.
	/// Combines prefix with hash of test name for uniqueness and reproducibility.
	/// </summary>
	public static string GenerateTestId(string testName, string prefix = "test")
	{
		var hash = Math.Abs(testName.GetHashCode());
		return $"{prefix}_{hash:X8}".Substring(0, Math.Min(20, prefix.Length + 9));
	}

	/// <summary>
	/// Generate a deterministic GUID based on test name.
	/// Useful for creating reproducible test data.
	/// </summary>
	public static Guid GenerateDeterministicGuid(string testName)
	{
		var hash = testName.GetHashCode();
		var bytes = new byte[16];
		var hashBytes = BitConverter.GetBytes(hash);

		// Fill GUID with deterministic pattern based on hash
		for (int i = 0; i < 16; i++)
		{
			bytes[i] = hashBytes[i % hashBytes.Length];
		}

		return new Guid(bytes);
	}

	/// <summary>
	/// Log test information with proper formatting
	/// </summary>
	public static void LogTestInfo(string message)
	{
		GD.Print($"[TEST INFO] {message}");
	}

	/// <summary>
	/// Log test error with proper formatting
	/// </summary>
	public static void LogTestError(string message)
	{
		GD.PrintErr($"[TEST ERROR] {message}");
	}

	/// <summary>
	/// Log test warning with proper formatting
	/// </summary>
	public static void LogTestWarning(string message)
	{
		GD.Print($"[TEST WARNING] {message}");
	}

	/// <summary>
	/// Wait for backend to be ready with timeout
	/// </summary>
	public static async Task<bool> WaitForBackendReadyAsync(float timeoutSeconds = 10.0f)
	{
		var backendManager = GetBackendManager();
		if (backendManager == null)
		{
			LogTestError("BackendManager not found");
			return false;
		}

		var isReady = await WaitForConditionAsync(
			() => backendManager.IsBackendRunning(),
			timeoutSeconds);

		if (isReady)
		{
			LogTestInfo("Backend ready");
		}
		else
		{
			LogTestWarning("Backend readiness timeout");
		}

		return isReady;
	}

	/// <summary>
	/// Check if all critical services are ready for testing
	/// </summary>
	public static bool AreAllServicesReady()
	{
		var backendManager = GetBackendManager();
		var eventService = GetEventService();
		var sessionManager = GetSessionManager();

		var backendReady = backendManager?.IsBackendRunning() ?? false;
		var eventServiceReady = eventService?.IsReady ?? false;
		var sessionManagerReady = sessionManager != null;

		return backendReady && eventServiceReady && sessionManagerReady;
	}

	/// <summary>
	/// Get BackendManager autoload instance
	/// </summary>
	public static BackendManager GetBackendManager()
	{
		if (Engine.GetMainLoop() is not SceneTree tree) return null;
		return tree.Root?.GetNode<BackendManager>("/root/BackendManager");
	}

	/// <summary>
	/// Get SessionEventService autoload instance
	/// </summary>
	public static SessionEventService GetEventService()
	{
		if (Engine.GetMainLoop() is not SceneTree tree) return null;
		return tree.Root?.GetNode<SessionEventService>("/root/SessionEventService");
	}

	/// <summary>
	/// Get SessionManager autoload instance
	/// </summary>
	public static SessionManager GetSessionManager()
	{
		if (Engine.GetMainLoop() is not SceneTree tree) return null;
		return tree.Root?.GetNode<SessionManager>("/root/SessionManager");
	}

	/// <summary>
	/// Get PaymentService autoload instance
	/// </summary>
	public static PaymentService GetPaymentService()
	{
		if (Engine.GetMainLoop() is not SceneTree tree) return null;
		return tree.Root?.GetNode<PaymentService>("/root/PaymentService");
	}

	/// <summary>
	/// Get CreditService autoload instance
	/// </summary>
	public static CreditService GetCreditService()
	{
		if (Engine.GetMainLoop() is not SceneTree tree) return null;
		return tree.Root?.GetNode<CreditService>("/root/CreditService");
	}

	/// <summary>
	/// Wait for SessionEventService to be ready with timeout
	/// </summary>
	public static async Task<bool> WaitForEventServiceReadyAsync(float timeoutSeconds = 10.0f)
	{
		var eventService = GetEventService();
		if (eventService == null)
		{
			LogTestError("SessionEventService not found");
			return false;
		}

		var isReady = await WaitForConditionAsync(
			() => eventService.IsReady,
			timeoutSeconds);

		if (isReady)
		{
			LogTestInfo("SessionEventService ready");
		}
		else
		{
			LogTestWarning("SessionEventService readiness timeout");
		}

		return isReady;
	}

	/// <summary>
	/// Wait for all critical services to be ready
	/// </summary>
	public static async Task<bool> WaitForAllServicesReadyAsync(float timeoutSeconds = 10.0f)
	{
		var isReady = await WaitForConditionAsync(
			AreAllServicesReady,
			timeoutSeconds);

		if (isReady)
		{
			LogTestInfo("All services ready");
		}
		else
		{
			// Log which services are not ready
			var backendManager = GetBackendManager();
			var eventService = GetEventService();
			var sessionManager = GetSessionManager();

			LogTestWarning("Service readiness timeout. Status:");
			LogTestWarning($"  Backend: {backendManager?.IsBackendRunning() ?? false}");
			LogTestWarning($"  SessionEventService: {eventService?.IsReady ?? false}");
			LogTestWarning($"  SessionManager: {sessionManager != null}");
		}

		return isReady;
	}

	/// <summary>
	/// Log the current state of all services
	/// </summary>
	public static void LogServiceStates()
	{
		var backendManager = GetBackendManager();
		var eventService = GetEventService();
		var sessionManager = GetSessionManager();
		var paymentService = GetPaymentService();
		var creditService = GetCreditService();

		LogTestInfo("Service States:");
		LogTestInfo($"  BackendManager: {(backendManager != null ? $"Present, Running={backendManager.IsBackendRunning()}" : "Missing")}");
		LogTestInfo($"  SessionEventService: {(eventService != null ? $"Present, Ready={eventService.IsReady}" : "Missing")}");
		LogTestInfo($"  SessionManager: {(sessionManager != null ? "Present" : "Missing")}");
		LogTestInfo($"  PaymentService: {(paymentService != null ? "Present" : "Missing")}");
		LogTestInfo($"  CreditService: {(creditService != null ? "Present" : "Missing")}");
	}

	/// <summary>
	/// Force SessionEventService into not-ready state for failure testing.
	/// Only works in DEBUG builds. Returns true if successful.
	/// </summary>
	public static bool ForceEventServiceNotReady()
	{
#if DEBUG
		var eventService = GetEventService();
		if (eventService == null)
		{
			LogTestError("Cannot force SessionEventService not ready - service not found");
			return false;
		}

		eventService.SetReadyStateForTesting(false);
		LogTestInfo("SessionEventService forced to NOT READY state");
		return true;
#else
		LogTestWarning("Failure simulation requires DEBUG build");
		return false;
#endif
	}

	/// <summary>
	/// Restore SessionEventService to ready state after failure testing.
	/// Only works in DEBUG builds.
	/// </summary>
	public static void RestoreEventServiceReady()
	{
#if DEBUG
		var eventService = GetEventService();
		if (eventService != null)
		{
			eventService.SetReadyStateForTesting(true);
			LogTestInfo("SessionEventService restored to READY state");
		}
#endif
	}

	/// <summary>
	/// Check if we can simulate service failures (requires DEBUG build)
	/// </summary>
	public static bool CanSimulateFailures()
	{
#if DEBUG
		return true;
#else
		return false;
#endif
	}

	/// <summary>
	/// Execute a simple HTTP request to the test backend with proper cleanup.
	/// Consolidates common HTTP client logic to reduce duplication.
	/// </summary>
	/// <param name="method">HTTP method to use</param>
	/// <param name="endpoint">Endpoint path (e.g., "/alive")</param>
	/// <param name="headers">Optional request headers</param>
	/// <param name="timeoutSeconds">Request timeout in seconds</param>
	/// <returns>Tuple of (success, responseCode, responseBody)</returns>
	public static async Task<(bool Success, int ResponseCode, string ResponseBody)> ExecuteSimpleHttpRequestAsync(
		Godot.HttpClient.Method method,
		string endpoint,
		string[] headers = null,
		float timeoutSeconds = 5.0f)
	{
		HttpClient httpClient = null;
		try
		{
			httpClient = new HttpClient();
			var error = httpClient.ConnectToHost("127.0.0.1", TestConfig.TestBackendPort);
			if (error != Error.Ok)
			{
				return (false, 0, $"Connection failed: {error}");
			}

			// Poll for connection
			for (int i = 0; i < 50; i++)
			{
				httpClient.Poll();
				var status = httpClient.GetStatus();

				if (status == HttpClient.Status.Connected)
				{
					// Send request
					headers ??= new string[] { "Accept: application/json" };
					var requestError = httpClient.Request(method, endpoint, headers);
					if (requestError != Error.Ok)
					{
						return (false, 0, $"Request failed: {requestError}");
					}

					// Wait for response
					while (httpClient.GetStatus() == HttpClient.Status.Requesting)
					{
						httpClient.Poll();
						await AutoloadBase.StaticDelayAsync(0.05f);
					}

					var responseCode = httpClient.GetResponseCode();
					var bodyBytes = httpClient.ReadResponseBodyChunk();
					var bodyText = bodyBytes.GetStringFromUtf8();

					return (true, responseCode, bodyText);
				}

				if (status == HttpClient.Status.CantConnect ||
				    status == HttpClient.Status.CantResolve ||
				    status == HttpClient.Status.ConnectionError)
				{
					return (false, 0, $"Connection error: {status}");
				}

				await AutoloadBase.StaticDelayAsync(0.1f);
			}

			return (false, 0, "Connection timeout");
		}
		catch (Exception ex)
		{
			return (false, 0, $"Exception: {ex.Message}");
		}
		finally
		{
			httpClient?.Close();
		}
	}
}
