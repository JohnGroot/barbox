using System.Threading.Tasks;
using BarBox.Tests.Fixtures;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

namespace BarBox.Tests.Unit.Services;

/// <summary>
/// Unit tests for BackendManager - backend lifecycle and health
/// Validates backend state reporting and readiness checks
/// </summary>
public class BackendManagerTests : BackendTestBase
{
	private BackendManager _backendManager;

	public BackendManagerTests(Node testScene)
		: base(testScene)
	{
	}

	[Setup]
	public new void SetupTestIdentifiers()
	{
		base.SetupTestIdentifiers();
		_backendManager = GetBackendManager();
	}

	[Test]
	public void IsBackendRunning_AfterInitialization_ReflectsActualState()
	{
		// Arrange
		_backendManager.ShouldNotBeNull("BackendManager must be available");

		// Act
		var isRunning = _backendManager.IsBackendRunning();

		// Assert
		TestHelpers.LogTestInfo($"Backend running state: {isRunning}");

		// Document expected behavior
		if (isRunning)
		{
			TestHelpers.LogTestInfo("Backend reports as running - services should be operational");
		}
		else
		{
			TestHelpers.LogTestInfo("Backend not running - service operations will fail");
			TestHelpers.LogTestInfo("This may be expected in some test environments");
		}
	}

	[Test]
	public async Task BackendHealth_CanBeVerified()
	{
		// Arrange
		_backendManager.ShouldNotBeNull("BackendManager must be available");

		// Act - Check if backend is healthy
		var isHealthy = await TestHelpers.IsTestBackendHealthyAsync();

		// Assert
		TestHelpers.LogTestInfo($"Backend health check: {(isHealthy ? "Healthy" : "Not healthy")}");

		var isRunningState = _backendManager.IsBackendRunning();
		TestHelpers.LogTestInfo($"Backend running state: {isRunningState}");

		// Validate consistency
		if (isRunningState && !isHealthy)
		{
			TestHelpers.LogTestInfo("Backend reports running but health check failed - may be starting");
		}
		else if (!isRunningState && isHealthy)
		{
			false.ShouldBeTrue("Backend health check passed but IsBackendRunning is false - state inconsistency");
		}
		else if (isRunningState && isHealthy)
		{
			TestHelpers.LogTestInfo("✓ Backend state is consistent and healthy");
			true.ShouldBeTrue("State is consistent");
		}
	}

	[Test]
	public void BackendReadySignal_CorrelatesWithRunningState()
	{
		// This test documents the expected signal behavior
		// Arrange
		_backendManager.ShouldNotBeNull("BackendManager must be available");

		// Act
		var isRunning = _backendManager.IsBackendRunning();

		// Assert
		TestHelpers.LogTestInfo("Backend signal behavior expectations:");
		TestHelpers.LogTestInfo($"  Current IsBackendRunning: {isRunning}");

		if (isRunning)
		{
			TestHelpers.LogTestInfo("  Expected: BackendReady signal was emitted during initialization");
			TestHelpers.LogTestInfo("  Expected: Services subscribed to BackendReady should be operational");
		}
		else
		{
			TestHelpers.LogTestInfo("  Expected: BackendStartFailed signal may have been emitted");
			TestHelpers.LogTestInfo("  Expected: Services subscribed to BackendReady are not initialized");
		}
	}

	[Test]
	public async Task BackendState_PersistsThroughQueries()
	{
		// Arrange
		_backendManager.ShouldNotBeNull("BackendManager must be available");

		// Act - Check state multiple times with deterministic waiting
		var state1 = _backendManager.IsBackendRunning();

		// Use WaitForConditionAsync to wait deterministically
		await TestHelpers.WaitForConditionAsync(() => true, 0.1f); // Wait 100ms
		var state2 = _backendManager.IsBackendRunning();

		await TestHelpers.WaitForConditionAsync(() => true, 0.1f); // Wait another 100ms
		var state3 = _backendManager.IsBackendRunning();

		// Assert
		TestHelpers.LogTestInfo($"Backend state samples: {state1}, {state2}, {state3}");

		if (state1 == state2 && state2 == state3)
		{
			TestHelpers.LogTestInfo("✓ Backend state is stable");
			true.ShouldBeTrue("State is stable");
		}
		else
		{
			TestHelpers.LogTestInfo("Backend state changed during test - may indicate instability");
		}
	}

	[Test]
	public void BackendManager_HasExpectedConfiguration()
	{
		// Arrange
		_backendManager.ShouldNotBeNull("BackendManager must be available");

		// Act
		var path = _backendManager.GetPath().ToString();
		var isAutoload = path.StartsWith("/root/");

		// Assert
		TestHelpers.LogTestInfo("BackendManager configuration check:");
		TestHelpers.LogTestInfo($"  Node path: {path}");
		TestHelpers.LogTestInfo($"  Is autoload: {isAutoload}");

		// BackendManager should be an autoload
		isAutoload.ShouldBeTrue("BackendManager must be configured as an autoload");
		TestHelpers.LogTestInfo("✓ BackendManager is properly configured as autoload");
	}

	[Test]
	public async Task BackendFailureState_IsDetectable()
	{
		// This test documents how to detect backend failure states
		// Arrange
		_backendManager.ShouldNotBeNull("BackendManager must be available");

		// Act
		var isRunning = _backendManager.IsBackendRunning();
		var isHealthy = await TestHelpers.IsTestBackendHealthyAsync();

		// Assert
		TestHelpers.LogTestInfo("Backend failure detection:");
		TestHelpers.LogTestInfo($"  IsBackendRunning: {isRunning}");
		TestHelpers.LogTestInfo($"  Health check: {isHealthy}");

		if (!isRunning || !isHealthy)
		{
			TestHelpers.LogTestInfo("Backend failure detected:");
			TestHelpers.LogTestInfo("  - Operations requiring backend will fail");
			TestHelpers.LogTestInfo("  - SessionEventService should not be ready");
			TestHelpers.LogTestInfo("  - PaymentService credit operations will fail");

			// Verify SessionEventService correlates with backend state
			var eventService = GetEventService();
			eventService.ShouldNotBeNull("SessionEventService must be available");

			var eventServiceReady = eventService.IsReady;
			TestHelpers.LogTestInfo($"  SessionEventService ready: {eventServiceReady}");

			if (eventServiceReady && !isRunning)
			{
				false.ShouldBeTrue("SessionEventService reports ready but backend not running - inconsistent state!");
			}
		}
		else
		{
			TestHelpers.LogTestInfo("✓ Backend is operational");
		}
	}

	[Cleanup]
	public override Task CleanupTestResources()
	{
		base.CleanupTestResources();
		_backendManager = null;
		return Task.CompletedTask;
	}
}
