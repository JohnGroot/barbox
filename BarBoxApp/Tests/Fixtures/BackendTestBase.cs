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

	// Cached service references for performance
	private EventService _cachedEventService;
	private SessionManager _cachedSessionManager;
	private CreditService _cachedCreditService;
	private PaymentService _cachedPaymentService;
	private BackendManager _cachedBackendManager;

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
	/// Uses seeded test data pre-registered in the backend database.
	/// </summary>
	[Setup]
	public void SetupTestIdentifiers()
	{
		// Use seeded box ID that matches backend test data and API key authentication
		// The test backend seed creates box 00000000-0000-0000-0000-000000000001
		// and the API key authenticates to this specific box
		TestBoxId = TestHelpers.SeededTestBoxId;

		// Use seeded player data instead of random generation
		// This player is pre-registered in the test database with known credentials
		var (playerId, phone, pin, username) = TestHelpers.GetSeededTestPlayer(1);
		TestPlayerId = playerId;
		TestPlayerPhone = phone;
		TestPlayerUsername = username;

		TestHelpers.LogTestInfo($"Test identifiers loaded - Box: {TestBoxId}, Player: {TestPlayerId}, Phone: {TestPlayerPhone}");
	}

	/// <summary>
	/// Cleanup run after each individual test.
	/// Override in derived classes to clean up test-specific resources.
	/// </summary>
	[Cleanup]
	public virtual Task CleanupTestResources()
	{
		// Clear cached service references to ensure fresh state for next test
		_cachedEventService = null;
		_cachedSessionManager = null;
		_cachedCreditService = null;
		_cachedPaymentService = null;
		_cachedBackendManager = null;

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

	// ============================================================
	// SERVICE DISCOVERY PATTERN
	// ============================================================
	//
	// All service getters follow this pattern:
	//
	// 1. Cache Check: Return cached reference if valid
	//    - Uses GodotObject.IsInstanceValid() to verify node is not freed
	//    - Avoids repeated GetNode() calls for performance
	//
	// 2. Lazy Discovery: Fetch and cache on first use
	//    - Uses TestScene.GetNode<T>("/root/ServiceName")
	//    - Logs error if service not found
	//
	// 3. Automatic Cleanup: Cache cleared in [Cleanup]
	//    - Ensures fresh state between tests
	//    - Prevents stale references across test runs
	//
	// USAGE:
	//    var eventService = GetEventService();
	//    if (eventService != null && eventService.IsReady) { ... }
	//
	// NOTE: Services may legitimately be null in some test scenarios
	//       (e.g., testing graceful degradation). Always null-check.
	//
	// ============================================================

	/// <summary>
	/// Get EventService autoload for testing.
	/// Uses cached reference with validation for performance.
	/// </summary>
	protected EventService GetEventService()
	{
		if (_cachedEventService != null && GodotObject.IsInstanceValid(_cachedEventService))
		{
			return _cachedEventService;
		}

		_cachedEventService = TestScene.GetNode<EventService>("/root/EventService");
		if (_cachedEventService == null)
		{
			TestHelpers.LogTestError("EventService autoload not found!");
		}
		return _cachedEventService;
	}

	/// <summary>
	/// Get SessionManager autoload for testing.
	/// Uses cached reference with validation for performance.
	/// </summary>
	protected SessionManager GetSessionManager()
	{
		if (_cachedSessionManager != null && GodotObject.IsInstanceValid(_cachedSessionManager))
		{
			return _cachedSessionManager;
		}

		_cachedSessionManager = TestScene.GetNode<SessionManager>("/root/SessionManager");
		if (_cachedSessionManager == null)
		{
			TestHelpers.LogTestError("SessionManager autoload not found!");
		}
		return _cachedSessionManager;
	}

	/// <summary>
	/// Get CreditService autoload for testing.
	/// Uses cached reference with validation for performance.
	/// </summary>
	protected CreditService GetCreditService()
	{
		if (_cachedCreditService != null && GodotObject.IsInstanceValid(_cachedCreditService))
		{
			return _cachedCreditService;
		}

		_cachedCreditService = TestScene.GetNode<CreditService>("/root/CreditService");
		if (_cachedCreditService == null)
		{
			TestHelpers.LogTestError("CreditService autoload not found!");
		}
		return _cachedCreditService;
	}

	/// <summary>
	/// Get PaymentService autoload for testing.
	/// Uses cached reference with validation for performance.
	/// </summary>
	protected PaymentService GetPaymentService()
	{
		if (_cachedPaymentService != null && GodotObject.IsInstanceValid(_cachedPaymentService))
		{
			return _cachedPaymentService;
		}

		_cachedPaymentService = TestScene.GetNode<PaymentService>("/root/PaymentService");
		if (_cachedPaymentService == null)
		{
			TestHelpers.LogTestError("PaymentService autoload not found!");
		}
		return _cachedPaymentService;
	}

	/// <summary>
	/// Get BackendManager autoload for testing.
	/// Uses cached reference with validation for performance.
	/// </summary>
	protected BackendManager GetBackendManager()
	{
		if (_cachedBackendManager != null && GodotObject.IsInstanceValid(_cachedBackendManager))
		{
			return _cachedBackendManager;
		}

		_cachedBackendManager = TestScene.GetNode<BackendManager>("/root/BackendManager");
		if (_cachedBackendManager == null)
		{
			TestHelpers.LogTestError("BackendManager autoload not found!");
		}
		return _cachedBackendManager;
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

}
