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
	public async void SetupBackendConnection()
	{
		TestHelpers.LogTestInfo($"Setting up backend connection for {GetType().Name}");

		// Verify test backend is healthy
		var isHealthy = await TestHelpers.IsTestBackendHealthyAsync();
		if (!isHealthy)
		{
			TestHelpers.LogTestError("Test backend is not healthy! Tests will fail.");
			TestHelpers.LogTestInfo("Make sure test backend is running: sh BarBoxServices/scripts/test-backend.sh start");
		}
		else
		{
			TestHelpers.LogTestInfo("Test backend is healthy");
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
		TestPlayerPhone = TestHelpers.GenerateTestPhoneNumber();

		TestHelpers.LogTestInfo($"Test identifiers created - Box: {TestBoxId}, Player: {TestPlayerId}");
	}

	/// <summary>
	/// Cleanup run after each individual test.
	/// Override in derived classes to clean up test-specific resources.
	/// </summary>
	[Cleanup]
	public virtual void CleanupTestResources()
	{
		// Derived classes can override to clean up test data
		TestHelpers.LogTestInfo($"Test cleanup complete for {GetType().Name}");
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
}
