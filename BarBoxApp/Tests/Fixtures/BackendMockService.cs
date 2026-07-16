using System;
using System.Threading.Tasks;
using Godot;

namespace BarBox.Tests.Fixtures;

/// <summary>
/// Mock backend service for simulating backend behavior in test scenarios.
///
/// PURPOSE:
/// Provides an alternative to DEBUG-only hooks for testing failure scenarios.
/// Allows comprehensive failure testing without modifying production code or
/// requiring DEBUG compilation flags.
///
/// WHEN TO USE:
/// - Testing backend startup failures (slow startup, timeouts, connection errors)
/// - Testing retry logic and error handling
/// - Testing graceful degradation when backend is unavailable
/// - Simulating specific failure scenarios in a controlled manner
///
/// WHEN NOT TO USE:
/// - Integration tests that verify actual backend communication (use real test backend)
/// - Testing backend API contracts (use Hurl integration tests)
/// - Performance testing (mock doesn't reflect real network latency)
///
/// VS. DEBUG HOOKS:
/// - DEBUG hooks (like SessionEventService.SetReadyStateForTesting): Require DEBUG builds,
///   modify real service state, tightly coupled to production code
/// - BackendMockService: Works in all builds, isolated test fixture, no production
///   code coupling
///
/// SIMULATED SCENARIOS:
/// - Slow Startup: Backend takes extended time to become healthy
/// - Connection Failure: Backend refuses connections
/// - Timeout: Backend never responds to health checks
/// - Health State: Backend healthy/unhealthy states
///
/// USAGE PATTERN:
///   var mockBackend = SetupMockBackend(); // In BackendTestBase
///   mockBackend.SimulateSlowStartup(10.0f); // Configure scenario
///   mockBackend.BeginSlowStartupSequence(); // Start simulation
///   var isHealthy = await mockBackend.SimulateHealthCheckAsync(); // Test response
///   mockBackend.ResetAllSimulations(); // Cleanup
///
/// </summary>
public partial class BackendMockService : Node
{
	private bool _isMockBackendHealthy = false;
	private bool _simulateSlowStartup = false;
	private float _simulatedStartupDelay = 0.0f;
	private bool _simulateConnectionFailure = false;
	private bool _simulateTimeout = false;

	[Signal]
	public delegate void MockBackendReadyEventHandler();

	[Signal]
	public delegate void MockBackendFailedEventHandler(string error);

	/// <summary>
	/// Set the mock backend to healthy state.
	/// </summary>
	public void SetHealthy(bool healthy)
	{
		_isMockBackendHealthy = healthy;

		if (healthy)
		{
			EmitSignal(SignalName.MockBackendReady);
		}
		else
		{
			EmitSignal(SignalName.MockBackendFailed, "Mock backend set to unhealthy");
		}
	}

	/// <summary>
	/// Check if mock backend is healthy.
	/// </summary>
	public bool IsHealthy()
	{
		return _isMockBackendHealthy;
	}

	/// <summary>
	/// Simulate slow backend startup for timeout testing.
	/// </summary>
	/// <param name="delaySeconds">Startup delay in seconds</param>
	public void SimulateSlowStartup(float delaySeconds)
	{
		_simulateSlowStartup = true;
		_simulatedStartupDelay = delaySeconds;
		_isMockBackendHealthy = false;
	}

	/// <summary>
	/// Begin simulated slow startup sequence.
	/// Call this to start the delayed health check response.
	/// </summary>
	public async void BeginSlowStartupSequence()
	{
		if (!_simulateSlowStartup)
		{
			return;
		}

		GD.Print($"[BackendMockService] Simulating slow startup ({_simulatedStartupDelay}s delay)");

		await AutoloadBase.StaticDelayAsync(_simulatedStartupDelay);

		_isMockBackendHealthy = true;
		_simulateSlowStartup = false;

		GD.Print("[BackendMockService] Slow startup complete - backend now healthy");
		EmitSignal(SignalName.MockBackendReady);
	}

	/// <summary>
	/// Simulate connection failure for testing error handling.
	/// </summary>
	public void SimulateConnectionFailure(bool shouldFail)
	{
		_simulateConnectionFailure = shouldFail;

		if (shouldFail)
		{
			_isMockBackendHealthy = false;
		}
	}

	/// <summary>
	/// Check if connection failure is being simulated.
	/// </summary>
	public bool IsSimulatingConnectionFailure()
	{
		return _simulateConnectionFailure;
	}

	/// <summary>
	/// Simulate timeout scenario for testing timeout handling.
	/// </summary>
	public void SimulateTimeout(bool shouldTimeout)
	{
		_simulateTimeout = shouldTimeout;

		if (shouldTimeout)
		{
			_isMockBackendHealthy = false;
		}
	}

	/// <summary>
	/// Check if timeout is being simulated.
	/// </summary>
	public bool IsSimulatingTimeout()
	{
		return _simulateTimeout;
	}

	/// <summary>
	/// Simulate health check with configurable response.
	/// Mimics BackendManager.IsBackendHealthyAsync behavior.
	/// </summary>
	public async Task<bool> SimulateHealthCheckAsync(float timeoutSeconds = 5.0f)
	{
		// If simulating connection failure, return false immediately
		if (_simulateConnectionFailure)
		{
			GD.Print("[BackendMockService] Health check failed - connection failure simulated");
			return false;
		}

		// If simulating timeout, wait for timeout then return false
		if (_simulateTimeout)
		{
			GD.Print($"[BackendMockService] Health check timing out ({timeoutSeconds}s)");
			await AutoloadBase.StaticDelayAsync(timeoutSeconds);
			GD.Print("[BackendMockService] Health check timed out");
			return false;
		}

		// If simulating slow startup, check if enough time has elapsed
		if (_simulateSlowStartup)
		{
			GD.Print("[BackendMockService] Health check during slow startup - waiting...");
			return false; // Not ready yet during startup delay
		}

		// Normal case - return current health state
		return _isMockBackendHealthy;
	}

	/// <summary>
	/// Reset all simulation states to default.
	/// </summary>
	public void ResetAllSimulations()
	{
		_isMockBackendHealthy = false;
		_simulateSlowStartup = false;
		_simulatedStartupDelay = 0.0f;
		_simulateConnectionFailure = false;
		_simulateTimeout = false;

		GD.Print("[BackendMockService] All simulations reset");
	}

	/// <summary>
	/// Create a mock SessionEventService state for testing payment validation.
	/// Returns a simple state object that can be used in tests.
	/// </summary>
	public static EventServiceMockState CreateMockEventServiceState(bool isReady, bool exists = true)
	{
		return new EventServiceMockState
		{
			Exists = exists,
			IsReady = isReady,
		};
	}

	/// <summary>
	/// Simple struct to hold SessionEventService mock state.
	/// </summary>
	public struct EventServiceMockState
	{
		public bool Exists { get; set; }

		public bool IsReady { get; set; }

		public override string ToString()
		{
			return $"SessionEventService [Exists={Exists}, Ready={IsReady}]";
		}
	}

	public override void _Ready()
	{
		GD.Print("[BackendMockService] Mock backend service initialized");
	}

	public override void _ExitTree()
	{
		ResetAllSimulations();
	}
}
