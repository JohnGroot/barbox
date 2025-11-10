using System;
using System.Threading.Tasks;
using Godot;

namespace BarBox.Tests.Fixtures;

/// <summary>
/// Mock backend service for testing without requiring actual backend or DEBUG builds.
/// Provides controlled simulation of backend states and failures.
/// </summary>
public partial class BackendMockService : Node
{
	private bool _isMockBackendHealthy = false;
	private bool _simulateSlowStartup = false;
	private float _simulatedStartupDelay = 0.0f;
	private bool _simulateConnectionFailure = false;
	private bool _simulateTimeout = false;

	[Signal] public delegate void MockBackendReadyEventHandler();
	[Signal] public delegate void MockBackendFailedEventHandler(string error);

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

		await Task.Delay((int)(_simulatedStartupDelay * 1000));

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
			await Task.Delay((int)(timeoutSeconds * 1000));
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
	/// Create a mock EventService state for testing payment validation.
	/// Returns a simple state object that can be used in tests.
	/// </summary>
	public static EventServiceMockState CreateMockEventServiceState(bool isReady, bool exists = true)
	{
		return new EventServiceMockState
		{
			Exists = exists,
			IsReady = isReady
		};
	}

	/// <summary>
	/// Simple struct to hold EventService mock state.
	/// </summary>
	public struct EventServiceMockState
	{
		public bool Exists { get; set; }
		public bool IsReady { get; set; }

		public override string ToString()
		{
			return $"EventService [Exists={Exists}, Ready={IsReady}]";
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
