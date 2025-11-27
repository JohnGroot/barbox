using System;
using System.Threading.Tasks;
using Chickensoft.GoDotTest;
using Godot;

namespace BarBox.Tests.Fixtures;

/// <summary>
/// Base class for tests that need to simulate service failure scenarios.
/// Provides helpers to force services into failure states for testing error handling.
/// Requires DEBUG build to access test hooks in production code.
/// </summary>
public abstract class FailureScenarioTestBase : BackendTestBase
{
	private EventService _eventService;
#if DEBUG
	private bool _eventServiceStateModified = false;
	private bool _originalEventServiceState = false;
#endif

	protected FailureScenarioTestBase(Node testScene) : base(testScene)
	{
	}

	/// <summary>
	/// Setup before each test - cache service references
	/// </summary>
	[Setup]
	public void SetupFailureScenario()
	{
		base.SetupTestIdentifiers();
		_eventService = GetEventService();
#if DEBUG
		_eventServiceStateModified = false;
#endif
	}

	/// <summary>
	/// Simulate EventService not being ready (backend not initialized).
	/// This forces EventService.IsReady to return false, simulating the
	/// production bug where backend fails to start.
	/// </summary>
	protected void SimulateEventServiceNotReady()
	{
#if DEBUG
		if (_eventService == null)
		{
			TestHelpers.LogTestWarning("EventService not found - cannot simulate failure");
			return;
		}

		// Save original state for restoration
		_originalEventServiceState = _eventService.IsReady;
		_eventServiceStateModified = true;

		// Force not ready state
		_eventService.SetReadyStateForTesting(false);
		TestHelpers.LogTestInfo("EventService forced to NOT READY state");
#else
		TestHelpers.LogTestWarning("Failure simulation requires DEBUG build");
#endif
	}

	/// <summary>
	/// Restore EventService to ready state after simulating failure.
	/// Should be called in test cleanup or finally blocks.
	/// </summary>
	protected void RestoreEventServiceReady()
	{
#if DEBUG
		if (_eventService != null && _eventServiceStateModified)
		{
			_eventService.SetReadyStateForTesting(_originalEventServiceState);
			_eventServiceStateModified = false;
			TestHelpers.LogTestInfo($"EventService restored to original state: {(_originalEventServiceState ? "READY" : "NOT READY")}");
		}
#endif
	}

	/// <summary>
	/// Check if we're in a DEBUG build that supports failure simulation
	/// </summary>
	protected bool CanSimulateFailures()
	{
#if DEBUG
		return true;
#else
		return false;
#endif
	}

	/// <summary>
	/// Verify that EventService is currently in not-ready state
	/// </summary>
	protected bool IsEventServiceNotReady()
	{
		return _eventService == null || !_eventService.IsReady;
	}

	/// <summary>
	/// Cleanup after each test - restore service states
	/// </summary>
	[Cleanup]
	public override async Task CleanupTestResources()
	{
		// Always restore service states before cleanup
		RestoreEventServiceReady();
		await base.CleanupTestResources();
		_eventService = null;
	}
}
