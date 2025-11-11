using System;
using System.Threading.Tasks;
using Chickensoft.GoDotTest;
using Godot;

namespace BarBox.Tests.Fixtures;

/// <summary>
/// Base class for tests that require backend integration.
/// Provides setup/cleanup for test backend connection and common test data.
/// </summary>
public abstract class BackendTestBase : TestClass
{
	protected Guid TestBoxId { get; private set; }
	protected Guid TestPlayerId { get; private set; }
	protected string TestPlayerUsername { get; private set; }
	protected string TestPlayerPhone { get; private set; }

	protected BackendTestBase(Node testScene) : base(testScene)
	{
	}

	/// <summary>
	/// Setup run once before all tests in the suite.
	/// Verifies backend is available.
	/// </summary>
	[SetupAll]
	public async Task SetupBackendConnection()
	{
		TestHelpers.LogTestInfo($"Setting up backend connection for {GetType().Name}");

		// Verify test backend is healthy
		var isHealthy = await TestHelpers.IsTestBackendHealthyAsync();
		if (!isHealthy)
		{
			TestHelpers.LogTestWarning("Test backend is not healthy! Some tests may be skipped.");
			TestHelpers.LogTestInfo("To run all tests, start test backend: sh BarBoxServices/scripts/test-backend.sh start");
		}
		else
		{
			TestHelpers.LogTestInfo("✓ Test backend is healthy");
		}

		// Wait for services to finish async initialization (BackendManager + EventService)
		TestHelpers.LogTestInfo("Waiting for services to initialize...");
		var servicesReady = await TestHelpers.WaitForConditionAsync(() =>
		{
			var backendManager = GetBackendManager();
			var eventService = GetEventService();

			var backendReady = backendManager != null && backendManager.IsBackendRunning();
			var eventReady = eventService != null && eventService.IsReady;

			return backendReady && eventReady;
		}, timeoutSeconds: 15.0f);

		if (servicesReady)
		{
			TestHelpers.LogTestInfo("✓ BackendManager and EventService are ready");
		}
		else
		{
			TestHelpers.LogTestWarning("Services did not become ready within timeout - some tests may fail");
		}
	}

	/// <summary>
	/// Setup run before each individual test.
	/// Creates unique test identifiers to ensure test isolation.
	/// </summary>
	[Setup]
	public void SetupTestIdentifiers()
	{
		// Generate unique identifiers for this test
		TestBoxId = TestHelpers.GenerateTestBoxId();
		TestPlayerId = TestHelpers.GenerateTestPlayerId();
		TestPlayerUsername = TestHelpers.GenerateTestUsername();
		TestPlayerPhone = TestHelpers.GenerateTestPhoneNumber(GetType().Name.GetHashCode());

		TestHelpers.LogTestInfo($"Test identifiers created - Box: {TestBoxId}, Player: {TestPlayerId}");
	}

	/// <summary>
	/// Cleanup run after each individual test.
	/// Override in derived classes to clean up test-specific resources.
	/// </summary>
	[Cleanup]
	public virtual Task CleanupTestResources()
	{
		// Derived classes can override to clean up test data
		TestHelpers.LogTestInfo($"Test cleanup complete for {GetType().Name}");
		return Task.CompletedTask;
	}

	/// <summary>
	/// Cleanup run once after all tests in the suite.
	/// </summary>
	[CleanupAll]
	public virtual void CleanupBackendConnection()
	{
		TestHelpers.LogTestInfo($"Backend connection cleanup complete for {GetType().Name}");
	}

	/// <summary>
	/// Get EventService autoload for testing.
	/// </summary>
	protected EventService GetEventService()
	{
		var eventService = TestScene.GetNode<EventService>("/root/EventService");
		if (eventService == null)
		{
			TestHelpers.LogTestError("EventService autoload not found!");
		}
		return eventService;
	}

	/// <summary>
	/// Get SessionManager autoload for testing.
	/// </summary>
	protected SessionManager GetSessionManager()
	{
		var sessionManager = TestScene.GetNode<SessionManager>("/root/SessionManager");
		if (sessionManager == null)
		{
			TestHelpers.LogTestError("SessionManager autoload not found!");
		}
		return sessionManager;
	}

	/// <summary>
	/// Get CreditService autoload for testing.
	/// </summary>
	protected CreditService GetCreditService()
	{
		var creditService = TestScene.GetNode<CreditService>("/root/CreditService");
		if (creditService == null)
		{
			TestHelpers.LogTestError("CreditService autoload not found!");
		}
		return creditService;
	}

	/// <summary>
	/// Get PaymentService autoload for testing.
	/// </summary>
	protected PaymentService GetPaymentService()
	{
		var paymentService = TestScene.GetNode<PaymentService>("/root/PaymentService");
		if (paymentService == null)
		{
			TestHelpers.LogTestError("PaymentService autoload not found!");
		}
		return paymentService;
	}

	/// <summary>
	/// Get BackendManager autoload for testing.
	/// </summary>
	protected BackendManager GetBackendManager()
	{
		var backendManager = TestScene.GetNode<BackendManager>("/root/BackendManager");
		if (backendManager == null)
		{
			TestHelpers.LogTestError("BackendManager autoload not found!");
		}
		return backendManager;
	}

	/// <summary>
	/// Setup mock backend service for testing failure scenarios.
	/// Returns configured BackendMockService instance.
	/// </summary>
	protected BackendMockService SetupMockBackend()
	{
		var mockBackend = new BackendMockService();
		TestScene.AddChild(mockBackend);
		TestHelpers.LogTestInfo("Mock backend service added to test scene");
		return mockBackend;
	}

	/// <summary>
	/// Simulate backend in failure state for testing.
	/// Creates mock backend and sets it to unhealthy.
	/// </summary>
	protected BackendMockService SetupBackendInFailureState()
	{
		var mockBackend = SetupMockBackend();
		mockBackend.SetHealthy(false);
		TestHelpers.LogTestInfo("Backend set to FAILURE state");
		return mockBackend;
	}

	/// <summary>
	/// Simulate stale processes on a port for process cleanup testing.
	/// This logs instructions for manual setup - actual stale process creation
	/// requires shell commands outside the test framework.
	/// </summary>
	protected void SimulateStaleProcesses(int port, int count)
	{
		TestHelpers.LogTestWarning($"Simulating {count} stale processes on port {port}");
		TestHelpers.LogTestInfo("Note: This simulation requires manual process setup in some test scenarios");

		// Use the ProcessTestHelpers to log manual setup instructions
		Helpers.ProcessTestHelpers.SimulateStaleProcessesOnPort(port, count);
	}

	/// <summary>
	/// Wait for backend to recover from failure state.
	/// Useful for testing retry mechanisms.
	/// </summary>
	protected async Task<bool> WaitForBackendRecovery(
		BackendMockService mockBackend,
		float timeoutSeconds = 30.0f)
	{
		TestHelpers.LogTestInfo($"Waiting for backend recovery (timeout: {timeoutSeconds}s)");

		// Use deterministic waiting
		var recovered = await TestHelpers.WaitForConditionAsync(
			() => mockBackend.IsHealthy(),
			timeoutSeconds);

		if (recovered)
		{
			TestHelpers.LogTestInfo("✓ Backend recovered successfully");
		}
		else
		{
			TestHelpers.LogTestWarning("Backend recovery timeout");
		}

		return recovered;
	}

	/// <summary>
	/// Verify EventService is NOT ready for testing graceful degradation.
	/// Returns true if EventService exists but is not ready.
	/// </summary>
	protected bool VerifyEventServiceNotReady()
	{
		var eventService = GetEventService();

		if (eventService == null)
		{
			TestHelpers.LogTestError("EventService does not exist - cannot verify not-ready state");
			return false;
		}

		var isReady = eventService.IsReady;

		if (isReady)
		{
			TestHelpers.LogTestError("EventService is READY when it should be NOT READY");
			return false;
		}

		TestHelpers.LogTestInfo("✓ EventService correctly in NOT READY state");
		return true;
	}

	/// <summary>
	/// Verify all services are in expected failure state.
	/// Useful for comprehensive failure scenario testing.
	/// </summary>
	protected bool VerifyServicesInFailureState()
	{
		var backendManager = GetBackendManager();
		var eventService = GetEventService();

		var backendNotRunning = backendManager != null && !backendManager.IsBackendRunning();
		var eventServiceNotReady = eventService != null && !eventService.IsReady;

		TestHelpers.LogTestInfo("Service Failure State Verification:");
		TestHelpers.LogTestInfo($"  Backend NOT running: {backendNotRunning}");
		TestHelpers.LogTestInfo($"  EventService NOT ready: {eventServiceNotReady}");

		return backendNotRunning && eventServiceNotReady;
	}

	/// <summary>
	/// Log detailed diagnostics about current service states.
	/// Useful for debugging test failures.
	/// </summary>
	protected void LogServiceDiagnostics()
	{
		TestHelpers.LogServiceStates();

		// Additional diagnostic info
		var backendManager = GetBackendManager();
		if (backendManager != null)
		{
			TestHelpers.LogTestInfo($"  Backend Info: {backendManager.GetBackendInfo()}");
		}
	}

	/// <summary>
	/// Reset a player's mining state by deleting all mining-related events.
	/// This is a surgical reset that only affects mining game data.
	/// Other game data and player account remain intact.
	///
	/// Requires test backend to be running. Will return false if backend is unavailable.
	/// </summary>
	/// <param name="playerId">UUID of the player whose mining state should be reset</param>
	/// <returns>True if reset succeeded, false otherwise</returns>
	protected async Task<bool> ResetPlayerMiningStateAsync(Guid playerId)
	{
		TestHelpers.LogTestInfo($"Resetting mining state for player {playerId}");

		try
		{
			var httpClient = new HttpClient();
			var error = httpClient.ConnectToHost("127.0.0.1", TestConfig.TestBackendPort);
			if (error != Error.Ok)
			{
				TestHelpers.LogTestWarning($"Failed to connect to test backend: {error}");
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
					// Make DELETE request
					var headers = new string[] { "Accept: application/json" };
					var endpoint = $"/test/player/{playerId}/mining-state";
					var requestError = httpClient.Request(
						Godot.HttpClient.Method.Delete,
						endpoint,
						headers
					);

					if (requestError != Error.Ok)
					{
						TestHelpers.LogTestWarning($"Failed to send DELETE request: {requestError}");
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

					if (responseCode == 200)
					{
						TestHelpers.LogTestInfo($"✓ Mining state reset successfully (deleted events)");
						return true;
					}
					else
					{
						TestHelpers.LogTestWarning($"DELETE request returned {responseCode}");
						return false;
					}
				}

				if (status == HttpClient.Status.CantConnect ||
				    status == HttpClient.Status.CantResolve ||
				    status == HttpClient.Status.ConnectionError)
				{
					TestHelpers.LogTestWarning($"Connection failed with status: {status}");
					httpClient.Close();
					return false;
				}

				await Task.Delay(100);
			}

			TestHelpers.LogTestWarning("Connection timeout while resetting mining state");
			httpClient.Close();
			return false;
		}
		catch (Exception ex)
		{
			TestHelpers.LogTestError($"Error resetting mining state: {ex.Message}");
			return false;
		}
	}
}
