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
	/// Create a unique box ID for testing
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

			await Task.Delay((int)(pollDelaySeconds * 1000));
		}

		return false;
	}

	/// <summary>
	/// Check if the test backend is healthy and responding
	/// </summary>
	public static async Task<bool> IsTestBackendHealthyAsync()
	{
		try
		{
			var httpClient = new HttpClient();
			var error = httpClient.ConnectToHost("127.0.0.1", TestConfig.TestBackendPort);
			if (error != Error.Ok)
			{
				httpClient.Close();
				return false;
			}

			// Poll for connection
			for (int i = 0; i < 50; i++)
			{
				httpClient.Poll();
				var status = httpClient.GetStatus();

				if (status == HttpClient.Status.Connected)
				{
					// Connection successful, now try a request
					var headers = new string[] { "Accept: application/json" };
					var requestError = httpClient.Request(Godot.HttpClient.Method.Get, "/alive", headers);
					if (requestError != Error.Ok)
					{
						httpClient.Close();
						return false;
					}

					// Wait for response
					while (httpClient.GetStatus() == HttpClient.Status.Requesting)
					{
						httpClient.Poll();
						await Task.Delay(50);
					}

					var responseCode = httpClient.GetResponseCode();
					httpClient.Close();

					return responseCode == 200;
				}

				if (status == HttpClient.Status.CantConnect ||
				    status == HttpClient.Status.CantResolve ||
				    status == HttpClient.Status.ConnectionError)
				{
					httpClient.Close();
					return false;
				}

				await Task.Delay(100);
			}

			httpClient.Close();
			return false;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[TestHelpers] Error checking backend health: {ex.Message}");
			return false;
		}
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
	/// </summary>
	/// <param name="seed">Seed for deterministic generation (use test name hash)</param>
	public static string GenerateTestPhoneNumber(int seed)
	{
		var random = new Random(seed);
		return $"555{random.Next(1000000, 9999999)}"; // 555-XXX-XXXX format
	}

	/// <summary>
	/// Create a test phone number with random generation.
	/// WARNING: Non-deterministic. Prefer GenerateTestPhoneNumber(int seed) for reproducible tests.
	/// </summary>
	[Obsolete("Use GenerateTestPhoneNumber(int seed) for deterministic test data")]
	public static string GenerateTestPhoneNumber()
	{
		var random = new Random();
		return $"555{random.Next(1000000, 9999999)}"; // 555-XXX-XXXX format
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
	/// Get EventService autoload instance
	/// </summary>
	public static EventService GetEventService()
	{
		if (Engine.GetMainLoop() is not SceneTree tree) return null;
		return tree.Root?.GetNode<EventService>("/root/EventService");
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
	/// Wait for EventService to be ready with timeout
	/// </summary>
	public static async Task<bool> WaitForEventServiceReadyAsync(float timeoutSeconds = 10.0f)
	{
		var eventService = GetEventService();
		if (eventService == null)
		{
			LogTestError("EventService not found");
			return false;
		}

		var isReady = await WaitForConditionAsync(
			() => eventService.IsReady,
			timeoutSeconds);

		if (isReady)
		{
			LogTestInfo("EventService ready");
		}
		else
		{
			LogTestWarning("EventService readiness timeout");
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
			LogTestWarning($"  EventService: {eventService?.IsReady ?? false}");
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
		LogTestInfo($"  EventService: {(eventService != null ? $"Present, Ready={eventService.IsReady}" : "Missing")}");
		LogTestInfo($"  SessionManager: {(sessionManager != null ? "Present" : "Missing")}");
		LogTestInfo($"  PaymentService: {(paymentService != null ? "Present" : "Missing")}");
		LogTestInfo($"  CreditService: {(creditService != null ? "Present" : "Missing")}");
	}

	/// <summary>
	/// Force EventService into not-ready state for failure testing.
	/// Only works in DEBUG builds. Returns true if successful.
	/// </summary>
	public static bool ForceEventServiceNotReady()
	{
#if DEBUG
		var eventService = GetEventService();
		if (eventService == null)
		{
			LogTestError("Cannot force EventService not ready - service not found");
			return false;
		}

		eventService.SetReadyStateForTesting(false);
		LogTestInfo("EventService forced to NOT READY state");
		return true;
#else
		LogTestWarning("Failure simulation requires DEBUG build");
		return false;
#endif
	}

	/// <summary>
	/// Restore EventService to ready state after failure testing.
	/// Only works in DEBUG builds.
	/// </summary>
	public static void RestoreEventServiceReady()
	{
#if DEBUG
		var eventService = GetEventService();
		if (eventService != null)
		{
			eventService.SetReadyStateForTesting(true);
			LogTestInfo("EventService restored to READY state");
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
}
