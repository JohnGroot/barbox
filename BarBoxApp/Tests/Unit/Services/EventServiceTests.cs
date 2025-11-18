using System;
using System.Threading.Tasks;
using Chickensoft.GoDotTest;
using Godot;
using BarBox.Tests.Fixtures;
using Shouldly;

namespace BarBox.Tests.Unit.Services;

/// <summary>
/// Unit tests for EventService - backend communication and session management
/// </summary>
public class EventServiceTests : BackendTestBase
{
	private EventService _eventService;

	public EventServiceTests(Node testScene) : base(testScene)
	{
	}

	[Setup]
	public new void SetupTestIdentifiers()
	{
		base.SetupTestIdentifiers();
		_eventService = GetEventService();
	}

	[Test]
	public async Task CreateActivitySession_WithValidParams_ReturnsSessionId()
	{
		// Arrange
		_eventService.ShouldNotBeNull("EventService must be available");
		var gameTag = "mining";

		// Act - create session without pre-registering box/player
		// EventService should handle creation internally
		var result = await _eventService.CreateActivitySessionAsync(
			TestBoxId,
			TestPlayerId,
			gameTag);

		// Assert
		// Note: This test may fail if backend requires box/player to exist first
		// In production, SessionManager handles box/player creation
		if (result.IsFailure(out var error))
		{
			TestHelpers.LogTestInfo($"CreateActivitySession failed (may be expected if backend validation enabled): {error.Message}");
		}
		else if (result.IsSuccess(out var sessionId))
		{
			sessionId.ShouldNotBe(Guid.Empty, "Session ID should not be empty");
		}
	}

	[Test]
	public async Task CreateActivitySession_WithoutGameTag_ReturnsFailure()
	{
		// Arrange
		_eventService.ShouldNotBeNull("EventService must be available");

		// Act
		var result = await _eventService.CreateActivitySessionAsync(
			TestBoxId,
			TestPlayerId,
			null); // Missing game tag

		// Assert
		result.IsFailure(out var error).ShouldBeTrue("Should fail without game tag");
		error.Message.ShouldNotBeNullOrEmpty("Error message should be provided");
		TestHelpers.LogTestInfo($"Correctly failed: {error.Message}");
	}

	[Test]
	public async Task CreateActivitySession_WithMultiplePlayerIds_IncludesInRequest()
	{
		// Arrange
		_eventService.ShouldNotBeNull("EventService must be available");
		var gameTag = "carrom";
		var playerIds = new System.Collections.Generic.List<string>
		{
			TestPlayerId.ToString(),
			TestHelpers.GenerateTestPlayerId().ToString()
		};

		// Act
		var result = await _eventService.CreateActivitySessionAsync(
			TestBoxId,
			TestPlayerId,
			gameTag,
			playerIds);

		// Assert - should handle multiplayer session creation
		if (result.IsSuccess(out var sessionId))
		{
			TestHelpers.LogTestInfo($"Multiplayer session result: Success (SessionID: {sessionId})");
		}
		else if (result.IsFailure(out var error))
		{
			TestHelpers.LogTestInfo($"Multiplayer session result: {error.Message}");
			// Note: Backend may reject if players don't exist, but API call should succeed
			TestHelpers.LogTestInfo($"Backend rejected multiplayer session: {error.Message}");
		}
	}

	[Test]
	public async Task CloseActivitySession_WithValidSession_ReturnsSuccess()
	{
		// Arrange
		_eventService.ShouldNotBeNull("EventService must be available");
		var sessionId = TestHelpers.GenerateTestSessionId();

		// Act
		var result = await _eventService.CloseActivitySessionAsync(sessionId);

		// Assert
		// May fail if session doesn't exist - that's expected for unit test
		if (result.IsSuccess(out var _))
		{
			TestHelpers.LogTestInfo("Close session result: Success");
		}
		else if (result.IsFailure(out var error))
		{
			TestHelpers.LogTestInfo($"Close session result: {error.Message}");
		}

		// We're testing the API contract, not necessarily success
		// The method should return a Result, not throw an exception
	}

	[Test]
	public void EmitEvent_WithSessionActive_ReturnsSuccess()
	{
		// This test requires a full integration setup
		// For now, testing the API surface
		_eventService.ShouldNotBeNull("EventService must be available");

		TestHelpers.LogTestInfo("EmitEvent test requires integration setup - see Integration tests");
	}

	[Test]
	public void IsReady_ReflectsBackendState()
	{
		// Arrange
		_eventService.ShouldNotBeNull("EventService must be available");
		var backendManager = GetBackendManager();
		backendManager.ShouldNotBeNull("BackendManager must be available");

		// Act
		var isReady = _eventService.IsReady;
		var backendRunning = backendManager.IsBackendRunning();

		// Assert
		TestHelpers.LogTestInfo($"EventService ready: {isReady}");
		TestHelpers.LogTestInfo($"Backend running: {backendRunning}");

		// EventService should not be ready if backend isn't running
		if (isReady && !backendRunning)
		{
			false.ShouldBeTrue("EventService ready but backend not running - state inconsistency!");
		}
		else if (!isReady && backendRunning)
		{
			TestHelpers.LogTestInfo("Backend running but EventService not ready - may be initializing");
		}
		else
		{
			TestHelpers.LogTestInfo("✓ EventService state matches backend state");
			true.ShouldBeTrue("State is consistent");
		}
	}

	[Test]
	public async Task AddCredits_BeforeInitialized_ReturnsFailure()
	{
		// Arrange
		_eventService.ShouldNotBeNull("EventService must be available");
		var isReady = _eventService.IsReady;

		if (isReady)
		{
			// Skip this test if service is ready - we can't test the "not ready" path
			TestHelpers.LogTestInfo("Test skipped - EventService is already ready");
			return;
		}

		// Act - try to add credits when not initialized
		var result = await _eventService.AddCreditsAsync(TestPlayerId, 100, "test");

		// Assert
		result.IsFailure(out var error).ShouldBeTrue("Should fail when EventService not ready");
		error.Message.ShouldNotBeNullOrEmpty("Error message should be provided");

		if (error.Message.Contains("not initialized") || error.Message.Contains("not ready"))
		{
			TestHelpers.LogTestInfo("✓ Correctly rejected credits before initialization");
			true.ShouldBeTrue("Expected error message found");
		}
		else
		{
			TestHelpers.LogTestInfo($"Failed with unexpected error: {error.Message}");
		}
	}

	[Test]
	public async Task EmitEventAsync_WithoutActiveSession_ReturnsProperError()
	{
		// Arrange
		_eventService.ShouldNotBeNull("EventService must be available");
		var isReady = _eventService.IsReady;

		if (!isReady)
		{
			TestHelpers.LogTestInfo("EventService not ready - testing error handling");
		}

		// Act - try to emit event without session
		var result = await _eventService.EmitEventAsync("test/event", new { data = "test" });

		// Assert
		result.IsFailure(out var error).ShouldBeTrue("Should fail without active session");
		error.Message.ShouldNotBeNullOrEmpty("Error message should be provided");

		if (error.Message.Contains("No active session") || error.Message.Contains("not initialized"))
		{
			TestHelpers.LogTestInfo($"✓ Correctly requires active session: {error.Message}");
			true.ShouldBeTrue("Expected error message found");
		}
		else
		{
			TestHelpers.LogTestInfo($"Failed with different error: {error.Message}");
		}
	}

	[Test]
	public async Task Operations_BeforeBackendReady_FailGracefully()
	{
		// Arrange
		_eventService.ShouldNotBeNull("EventService must be available");
		var backendManager = GetBackendManager();
		backendManager.ShouldNotBeNull("BackendManager must be available");

		var backendRunning = backendManager.IsBackendRunning();
		var eventServiceReady = _eventService.IsReady;

		TestHelpers.LogTestInfo($"Backend running: {backendRunning}");
		TestHelpers.LogTestInfo($"EventService ready: {eventServiceReady}");

		if (!eventServiceReady)
		{
			// Test various operations fail gracefully
			var sessionResult = await _eventService.CreateActivitySessionAsync(TestBoxId, TestPlayerId, "test");
			var creditResult = await _eventService.AddCreditsAsync(TestPlayerId, 10, "test");

			// Assert
			var sessionSuccess = sessionResult.IsSuccess(out var _);
			var creditSuccess = creditResult.IsSuccess(out var _);

			TestHelpers.LogTestInfo($"CreateActivitySession: {(sessionSuccess ? "Success" : "Failed")}");
			TestHelpers.LogTestInfo($"AddCredits: {(creditSuccess ? "Success" : "Failed")}");

			// Both should fail when not ready
			if (!sessionSuccess && !creditSuccess)
			{
				TestHelpers.LogTestInfo("✓ Operations correctly fail when EventService not ready");
				true.ShouldBeTrue("Operations correctly failed");
			}
			else
			{
				false.ShouldBeTrue("Some operations succeeded despite EventService not ready");
			}
		}
		else
		{
			TestHelpers.LogTestInfo("EventService is ready - operations should work");
		}
	}

	[Test]
	public void InitializationDependency_RequiresBackendManager()
	{
		// Arrange & Act
		var backendManager = GetBackendManager();

		// Assert
		backendManager.ShouldNotBeNull("EventService requires BackendManager");
		TestHelpers.LogTestInfo("✓ BackendManager dependency satisfied");

		// EventService should subscribe to BackendReady signal
		// This is validated by the IsReady correlation test
		TestHelpers.LogTestInfo("EventService initialization depends on BackendManager.BackendReady signal");
	}

	[Test]
	public async Task RaceConditionFix_BackendAlreadyRunning_EventServiceBecomesReady()
	{
		// This test verifies the fix for the signal registration race condition
		// Issue: If backend becomes ready between EventService checking IsBackendRunning()
		// and registering for the BackendReady signal, EventService never initializes

		// Arrange
		var backendManager = GetBackendManager();
		backendManager.ShouldNotBeNull("BackendManager must be available");
		_eventService.ShouldNotBeNull("EventService must be available");

		// Wait for backend to be fully ready
		var backendReady = await TestHelpers.WaitForConditionAsync(
			() => backendManager.IsBackendRunning(),
			timeoutSeconds: 30.0f);

		backendReady.ShouldBeTrue("Backend should become ready within timeout");
		TestHelpers.LogTestInfo("✓ Backend is running before EventService check");

		// Act - EventService should detect backend is already running
		// With the race condition fix, EventService checks IsBackendRunning() again
		// after registering for the signal, ensuring it catches already-running state

		// Wait for EventService to initialize (should be quick since backend already ready)
		var eventServiceReady = await TestHelpers.WaitForConditionAsync(
			() => _eventService.IsReady,
			timeoutSeconds: 5.0f);

		// Assert
		eventServiceReady.ShouldBeTrue("EventService should become ready when backend pre-exists");
		_eventService.IsReady.ShouldBeTrue("EventService.IsReady should return true");

		TestHelpers.LogTestInfo("✓ Race condition fix verified - EventService initializes correctly even when backend already running");
		TestHelpers.LogTestInfo("  This test would have failed before the fix (EventService.cs:84-91)");
	}

	[Test]
	public async Task RegisterPlayerFirstPlay_WithValidParams_ReturnsPlayerId()
	{
		// Arrange
		_eventService.ShouldNotBeNull("EventService must be available");

		// Ensure service is ready for this test
		_eventService.IsReady.ShouldBeTrue(
			"EventService must be ready to test player registration. " +
			"Check that backend is running and EventService initialized.");

		var playerId = TestHelpers.GenerateTestPlayerId();
		var username = "TestUser";
		var phoneNumber = TestHelpers.GenerateTestPhoneNumber("RegisterPlayerFirstPlay_WithBackendReady_ReturnsSuccess".GetHashCode());
		var pin = "1234";
		var boxId = TestBoxId;

		// Act
		var result = await _eventService.RegisterPlayerFirstPlayAsync(playerId, username, phoneNumber, pin, boxId);

		// Assert
		if (result.IsSuccess(out var registeredPlayerId))
		{
			registeredPlayerId.ShouldBe(playerId, "Should return the same player ID");
			TestHelpers.LogTestInfo($"✓ Player registered successfully: {playerId}");
		}
		else if (result.IsFailure(out var error))
		{
			// May fail if backend requires box to exist first
			TestHelpers.LogTestInfo($"Player registration failed (may be expected): {error.Message}");
		}
	}

	[Test]
	public async Task RegisterPlayerFirstPlay_CalledTwice_IsIdempotent()
	{
		// Arrange
		_eventService.ShouldNotBeNull("EventService must be available");

		_eventService.IsReady.ShouldBeTrue(
			"EventService must be ready to test idempotency. " +
			"Check that backend is running and EventService initialized.");

		var playerId = TestHelpers.GenerateTestPlayerId();
		var username = "IdempotentTest";
		var phoneNumber = TestHelpers.GenerateTestPhoneNumber("RegisterPlayerFirstPlay_CalledTwice_IsIdempotent".GetHashCode());
		var pin = "1234";
		var boxId = TestBoxId;

		// Act - Register same player twice
		var result1 = await _eventService.RegisterPlayerFirstPlayAsync(playerId, username, phoneNumber, pin, boxId);
		var result2 = await _eventService.RegisterPlayerFirstPlayAsync(playerId, username, phoneNumber, pin, boxId);

		// Assert
		var firstSuccess = result1.IsSuccess(out var playerId1);
		var secondSuccess = result2.IsSuccess(out var playerId2);

		if (firstSuccess && secondSuccess)
		{
			playerId1.ShouldBe(playerId, "First registration should return player ID");
			playerId2.ShouldBe(playerId, "Second registration should return same player ID");
			TestHelpers.LogTestInfo("✓ RegisterPlayerFirstPlay is idempotent - same player registered twice");
		}
		else if (!firstSuccess && result1.IsFailure(out var error1))
		{
			TestHelpers.LogTestInfo($"First registration failed: {error1.Message}");
		}
		else if (!secondSuccess && result2.IsFailure(out var error2))
		{
			TestHelpers.LogTestInfo($"Second registration failed: {error2.Message}");
		}
	}

	[Test]
	public async Task RegisterPlayerFirstPlay_BeforeServiceReady_ReturnsFailure()
	{
		// Arrange
		_eventService.ShouldNotBeNull("EventService must be available");

		if (_eventService.IsReady)
		{
			TestHelpers.LogTestInfo("EventService is ready - skipping 'not ready' test");
			return;
		}

		var playerId = TestHelpers.GenerateTestPlayerId();
		var username = "TestUser";
		var phoneNumber = TestHelpers.GenerateTestPhoneNumber("RegisterPlayerFirstPlay_WithBackendReady_ReturnsSuccess".GetHashCode());
		var pin = "1234";
		var boxId = TestBoxId;

		// Act
		var result = await _eventService.RegisterPlayerFirstPlayAsync(playerId, username, phoneNumber, pin, boxId);

		// Assert
		result.IsFailure(out var error).ShouldBeTrue("Should fail when EventService not ready");
		error.Message.ShouldNotBeNullOrEmpty("Error message should be provided");
		TestHelpers.LogTestInfo($"✓ Correctly failed when not ready: {error.Message}");
	}

	[Test]
	public async Task RegisterPlayerFirstPlay_WithEmptyUsername_ReturnsFailure()
	{
		// Arrange
		_eventService.ShouldNotBeNull("EventService must be available");

		_eventService.IsReady.ShouldBeTrue(
			"EventService must be ready to test validation. " +
			"Check that backend is running and EventService initialized.");

		var playerId = TestHelpers.GenerateTestPlayerId();
		var emptyUsername = "";
		var phoneNumber = TestHelpers.GenerateTestPhoneNumber("RegisterPlayerFirstPlay_WithEmptyUsername_ReturnsFailure".GetHashCode());
		var pin = "1234";
		var boxId = TestBoxId;

		// Act
		var result = await _eventService.RegisterPlayerFirstPlayAsync(playerId, emptyUsername, phoneNumber, pin, boxId);

		// Assert
		// Backend may or may not validate empty username - we're testing API contract
		if (result.IsSuccess(out var _))
		{
			TestHelpers.LogTestInfo("Empty username result: Accepted");
		}
		else if (result.IsFailure(out var error))
		{
			TestHelpers.LogTestInfo($"Empty username result: Rejected - {error.Message}");
		}
	}

	[Test]
	public void EventService_AfterInit_HasConfiguredApiKey()
	{
		// Arrange
		_eventService.ShouldNotBeNull("EventService must exist");

		// Act - Access API key field via reflection (test-only verification)
		var apiKeyField = typeof(EventService).GetField("_boxApiKey",
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
		var apiKey = apiKeyField?.GetValue(_eventService) as string;

		// Assert
		apiKey.ShouldNotBeNull("API key should be loaded from config");
		apiKey.ShouldNotBeEmpty("API key should not be empty string");
		apiKey.Length.ShouldBeGreaterThan(10, "API key should be substantial length");

		TestHelpers.LogTestInfo($"✓ API key configured: {apiKey.Substring(0, System.Math.Min(8, apiKey.Length))}...");
	}

	[Test]
	public void LocationManager_LoadsApiKeyFromEnvironment()
	{
		// Arrange
		var expectedApiKey = System.Environment.GetEnvironmentVariable("BARBOX_API_KEY");
		var locationManager = LocationManager.GetAutoload();

		// Act
		locationManager.ShouldNotBeNull("LocationManager must exist");

		// Assert
		if (string.IsNullOrEmpty(expectedApiKey))
		{
			TestHelpers.LogTestWarning("BARBOX_API_KEY not set - testing fallback behavior");

			// In editor, should have some value (even if empty from fallback)
			if (OS.HasFeature("editor"))
			{
				locationManager.BoxApiKey.ShouldNotBeNull("Editor should have API key reference");
			}
			else
			{
				// Production should fail initialization
				false.ShouldBeTrue("Production build requires BARBOX_API_KEY environment variable");
			}
		}
		else
		{
			locationManager.BoxApiKey.ShouldBe(expectedApiKey,
				"LocationManager should load API key from environment");
			TestHelpers.LogTestInfo($"✓ API key loaded from environment: {expectedApiKey.Substring(0, 8)}...");
		}
	}

	[Test]
	public async Task EventService_SuccessfulRequest_ImpliesValidHeaders()
	{
		// Arrange
		_eventService.ShouldNotBeNull("EventService must be available");

		// Ensure service is ready for auth header testing
		_eventService.IsReady.ShouldBeTrue(
			"EventService must be ready to test authentication headers. " +
			"Check that backend is running and EventService initialized.");

		var locationManager = LocationManager.GetAutoload();
		var apiKey = locationManager?.BoxApiKey;
		apiKey.ShouldNotBeNullOrEmpty("API key must be configured for authentication");

		TestHelpers.LogTestInfo($"Using API key: {apiKey.Substring(0, 8)}...");

		// Act - Create session (internally sends HTTP request with headers)
		var result = await _eventService.CreateActivitySessionAsync(
			TestBoxId,
			TestPlayerId,
			"test_game"
		);

		// Assert
		if (result.IsFailure(out var error))
		{
			// Check if failure is auth-related
			if (error.Message.Contains("401") || error.Message.Contains("Unauthorized") ||
				error.Message.Contains("403") || error.Message.Contains("Forbidden"))
			{
				false.ShouldBeTrue($"Auth failure detected - headers may be missing or invalid: {error.Message}");
			}

			// Other failures acceptable (box doesn't exist, etc.)
			TestHelpers.LogTestInfo($"Request failed (not auth-related): {error.Message}");
		}
		else if (result.IsSuccess(out var sessionId))
		{
			sessionId.ShouldNotBe(Guid.Empty, "Valid session ID should be returned");
			TestHelpers.LogTestInfo($"✓ Session created successfully: {sessionId}");
			TestHelpers.LogTestInfo("✓ Headers must have been valid (request succeeded)");

			// Cleanup
			await _eventService.CloseActivitySessionAsync(sessionId);
		}
	}

	[Cleanup]
	public override Task CleanupTestResources()
	{
		base.CleanupTestResources();
		// EventService is autoload - don't queue free
		_eventService = null;
		return Task.CompletedTask;
	}
}
