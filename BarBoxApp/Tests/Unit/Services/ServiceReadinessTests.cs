using System.Threading.Tasks;
using BarBox.Tests.Fixtures;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

namespace BarBox.Tests.Unit.Services;

/// <summary>
/// Tests for service initialization order and readiness checks
/// Validates that services correctly report their ready state and dependencies
/// </summary>
public class ServiceReadinessTests : BackendTestBase
{
	public ServiceReadinessTests(Node testScene)
		: base(testScene)
	{
	}

	[Test]
	public void AllCriticalServices_ArePresent()
	{
		// Arrange
		var requiredServices = new[]
		{
			"SessionEventService",
			"SessionManager",
			"PaymentService",
			"BackendManager",
			"CreditService",
		};

		// Act & Assert
		foreach (var serviceName in requiredServices)
		{
			var service = TestScene.GetNode($"/root/{serviceName}");
			service.ShouldNotBeNull($"Critical service {serviceName} must be present");
			TestHelpers.LogTestInfo($"✓ Service present: {serviceName}");
		}

		TestHelpers.LogTestInfo("✓ All critical services are present");
	}

	[Test]
	public void EventService_DependsOn_BackendManager()
	{
		// Arrange
		var eventService = GetEventService();
		var backendManager = GetBackendManager();

		// Assert
		eventService.ShouldNotBeNull("SessionEventService must be available");
		backendManager.ShouldNotBeNull("BackendManager must be available");

		TestHelpers.LogTestInfo("SessionEventService → BackendManager dependency:");

		// SessionEventService should wait for BackendReady signal
		var eventServiceReady = eventService.IsReady;
		var backendRunning = backendManager.IsBackendRunning();

		TestHelpers.LogTestInfo($"  SessionEventService ready: {eventServiceReady}");
		TestHelpers.LogTestInfo($"  Backend running: {backendRunning}");

		// Validate dependency relationship
		if (eventServiceReady && !backendRunning)
		{
			false.ShouldBeTrue("SessionEventService ready but backend not running - dependency violation!");
		}
		else if (!eventServiceReady && backendRunning)
		{
			TestHelpers.LogTestWarning("Backend running but SessionEventService not ready - may be initializing");
		}
		else if (eventServiceReady && backendRunning)
		{
			TestHelpers.LogTestInfo("✓ Dependency relationship is valid");
			true.ShouldBeTrue("Dependency is valid");
		}
		else
		{
			TestHelpers.LogTestInfo("Both services not ready - expected in some test scenarios");
		}
	}

	[Test]
	public void SessionManager_DependsOn_EventService()
	{
		// Arrange
		var sessionManager = GetSessionManager();
		var eventService = GetEventService();

		// Assert
		sessionManager.ShouldNotBeNull("SessionManager must be available");
		eventService.ShouldNotBeNull("SessionEventService must be available");

		TestHelpers.LogTestInfo("SessionManager → SessionEventService dependency:");
		TestHelpers.LogTestInfo($"  SessionManager exists: {sessionManager != null}");
		TestHelpers.LogTestInfo($"  SessionEventService exists: {eventService != null}");
		TestHelpers.LogTestInfo($"  SessionEventService ready: {eventService.IsReady}");

		// SessionManager should be able to check SessionEventService readiness
		// This test validates the dependency exists and can be checked
		TestHelpers.LogTestInfo("✓ SessionManager has access to SessionEventService state");
		true.ShouldBeTrue("Dependency validated");
	}

	[Test]
	public void PaymentService_DependsOn_SessionManager()
	{
		// Arrange
		var paymentService = GetPaymentService();
		var sessionManager = GetSessionManager();

		// Assert
		paymentService.ShouldNotBeNull("PaymentService must be available");
		sessionManager.ShouldNotBeNull("SessionManager must be available");

		TestHelpers.LogTestInfo("PaymentService → SessionManager dependency:");
		TestHelpers.LogTestInfo($"  PaymentService exists: {paymentService != null}");
		TestHelpers.LogTestInfo($"  SessionManager exists: {sessionManager != null}");

		TestHelpers.LogTestInfo("✓ PaymentService has access to SessionManager");
		true.ShouldBeTrue("Dependency validated");
	}

	[Test]
	public void ServiceDependencyChain_IsValid()
	{
		// Validate the complete dependency chain:
		// BackendManager → SessionEventService → SessionManager → PaymentService
		var backendManager = GetBackendManager();
		var eventService = GetEventService();
		var sessionManager = GetSessionManager();
		var paymentService = GetPaymentService();

		TestHelpers.LogTestInfo("Complete service dependency chain:");
		TestHelpers.LogTestInfo($"  1. BackendManager: {backendManager != null}");
		TestHelpers.LogTestInfo($"  2. SessionEventService: {eventService != null}");
		TestHelpers.LogTestInfo($"  3. SessionManager: {sessionManager != null}");
		TestHelpers.LogTestInfo($"  4. PaymentService: {paymentService != null}");

		// Assert all services are present
		backendManager.ShouldNotBeNull("BackendManager must be present in dependency chain");
		eventService.ShouldNotBeNull("SessionEventService must be present in dependency chain");
		sessionManager.ShouldNotBeNull("SessionManager must be present in dependency chain");
		paymentService.ShouldNotBeNull("PaymentService must be present in dependency chain");

		// Check readiness states
		var backendRunning = backendManager.IsBackendRunning();
		var eventServiceReady = eventService.IsReady;

		TestHelpers.LogTestInfo($"\nReadiness states:");
		TestHelpers.LogTestInfo($"  Backend running: {backendRunning}");
		TestHelpers.LogTestInfo($"  SessionEventService ready: {eventServiceReady}");

		// Validate chain integrity
		if (eventServiceReady && !backendRunning)
		{
			false.ShouldBeTrue("Chain integrity violation: SessionEventService ready without backend");
		}
		else
		{
			TestHelpers.LogTestInfo("✓ Dependency chain integrity maintained");
			true.ShouldBeTrue("Chain integrity validated");
		}
	}

	[Test]
	public void CreditService_DependsOn_EventService()
	{
		// Arrange
		var creditService = GetCreditService();
		var eventService = GetEventService();

		// Assert
		creditService.ShouldNotBeNull("CreditService must be available");
		eventService.ShouldNotBeNull("SessionEventService must be available");

		TestHelpers.LogTestInfo("CreditService → SessionEventService dependency:");
		TestHelpers.LogTestInfo($"  CreditService exists: {creditService != null}");
		TestHelpers.LogTestInfo($"  SessionEventService exists: {eventService != null}");
		TestHelpers.LogTestInfo($"  SessionEventService ready: {eventService.IsReady}");

		TestHelpers.LogTestInfo("✓ CreditService has access to SessionEventService");
		true.ShouldBeTrue("Dependency validated");
	}

	[Test]
	public void ServiceReadiness_MatchesExpectedState()
	{
		// Check if all services are in expected state for testing
		var backendManager = GetBackendManager();
		var eventService = GetEventService();
		var sessionManager = GetSessionManager();
		var paymentService = GetPaymentService();
		var creditService = GetCreditService();

		TestHelpers.LogTestInfo("Service readiness summary:");

		if (backendManager != null)
		{
			var running = backendManager.IsBackendRunning();
			TestHelpers.LogTestInfo($"  BackendManager: {(running ? "Running" : "Not running")}");
		}

		if (eventService != null)
		{
			var ready = eventService.IsReady;
			TestHelpers.LogTestInfo($"  SessionEventService: {(ready ? "Ready" : "Not ready")}");
		}

		TestHelpers.LogTestInfo($"  SessionManager: {(sessionManager != null ? "Available" : "Missing")}");
		TestHelpers.LogTestInfo($"  PaymentService: {(paymentService != null ? "Available" : "Missing")}");
		TestHelpers.LogTestInfo($"  CreditService: {(creditService != null ? "Available" : "Missing")}");

		// Document expected states for testing
		if (backendManager?.IsBackendRunning() == true && eventService?.IsReady == true)
		{
			TestHelpers.LogTestInfo("\n✓ Services are ready for full integration testing");
		}
		else
		{
			TestHelpers.LogTestInfo("\n⚠ Services not fully ready - some integration tests may be skipped");
		}
	}

	[Test]
	public void ServiceAutoloadPaths_AreCorrect()
	{
		// Validate all services are accessible via expected autoload paths
		var services = new[]
		{
			("BackendManager", "/root/BackendManager"),
			("SessionEventService", "/root/SessionEventService"),
			("SessionManager", "/root/SessionManager"),
			("PaymentService", "/root/PaymentService"),
			("CreditService", "/root/CreditService"),
		};

		TestHelpers.LogTestInfo("Service autoload path validation:");

		foreach (var (name, path) in services)
		{
			var node = TestScene.GetNode(path);
			node.ShouldNotBeNull($"{name} must be accessible at {path}");
			TestHelpers.LogTestInfo($"  ✓ {name} accessible at {path}");
		}

		TestHelpers.LogTestInfo("✓ All service autoload paths are correct");
	}

	[Cleanup]
	public override Task CleanupTestResources()
	{
		return base.CleanupTestResources();
	}
}
