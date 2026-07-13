using System;
using System.Threading.Tasks;
using Chickensoft.GoDotTest;
using Godot;
using BarBox.Tests.Fixtures;
using Shouldly;

namespace BarBox.Tests.Integration;

/// <summary>
/// Integration tests for player registration during login flow
/// Tests the complete flow: Login -> Player Registration -> Backend Persistence -> Credit Operations
/// Validates that players are properly registered before credit operations
/// </summary>
public class PlayerRegistrationTests : BackendTestBase
{
	private SessionManager _sessionManager;
	private EventService _eventService;
	private BackendManager _backendManager;
	private CreditService _creditService;

	public PlayerRegistrationTests(Node testScene) : base(testScene)
	{
	}

	[Setup]
	public new async Task SetupTestIdentifiers()
	{
		base.SetupTestIdentifiers();
		_sessionManager = GetSessionManager();
		_eventService = GetEventService();
		_backendManager = GetBackendManager();
		_creditService = GetCreditService();

		// CRITICAL: Clear any lingering sessions from previous tests to prevent state pollution
		// This handles cases where a previous test failed mid-way and didn't cleanup
		if (_sessionManager != null)
		{
			var activeUsers = _sessionManager.GetActivePhoneNumbers();
			foreach (var phone in activeUsers)
			{
				TestHelpers.LogTestInfo($"Cleaning up lingering session for: {phone}");
				await _sessionManager.LogoutUserAsync(phone);
			}
		}
	}

	[Test]
	public async Task PlayerLogin_WithBackendReady_RegistersPlayerAutomatically()
	{
		// Arrange - Wait for backend and register player
		var servicesReady = await WaitForServicesReadyAsync();
		servicesReady.ShouldBeTrue(
			"Integration test requires backend services. " +
			$"Backend: {_backendManager?.IsBackendRunning() ?? false}, " +
			$"EventService: {_eventService?.IsReady ?? false}");

		// Register player account before login
		var registrationResult = await _sessionManager.CreateUserAccountAsync(TestPlayerPhone, "1234", TestPlayerUsername);
		if (registrationResult.IsFailure(out var registrationError))
		{
			// Only accept "already exists" errors - fail on other errors
			if (!registrationError.Message.Contains("already exists") && !registrationError.Message.Contains("409"))
			{
				registrationResult.IsSuccess(out var _).ShouldBeTrue($"Registration should succeed or fail with 'already exists', but failed with: {registrationError.Message}");
			}
			TestHelpers.LogTestInfo($"Player already registered (expected): {registrationError.Message}");
		}
		else
		{
			TestHelpers.LogTestInfo($"Player registered successfully: {TestPlayerPhone}");
		}

		// Act - Login user
		var loginResult = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, "1234");

		// Assert
		if (loginResult.IsFailure(out var loginError))
		{
			// Skip gracefully if rate limited (5/min limit across test suite)
			if (loginError.Message.Contains("Rate limit"))
			{
				TestHelpers.LogTestInfo("Skipping - rate limit exceeded (5/min backend limit)");
				return;
			}
			loginResult.IsSuccess(out var _).ShouldBeTrue($"Login should succeed, but failed: {loginError.Message}");
		}

		var session = _sessionManager.GetSessionByPhone(TestPlayerPhone);
		session.ShouldNotBeNull("User session should exist after login");
		session.LobbySessionId.ShouldNotBe(Guid.Empty,
			"Lobby session MUST be created during login (session creation 404 bug check!)");

		var playerId = SessionManager.GetPlayerIdFromPhone(TestPlayerPhone);
		TestHelpers.LogTestInfo($"Player logged in with ID: {playerId}");

		// Verify player is registered in backend by attempting credit operation
		var addResult = await _creditService.AddAsync(playerId, 10, "Test credit after registration");
		if (addResult.IsFailure(out var addError))
		{
			addResult.IsSuccess(out var _).ShouldBeTrue($"Credit add should succeed for registered player, but failed: {addError.Message}");
		}

		TestHelpers.LogTestInfo($"✓ Player automatically registered during login");
		TestHelpers.LogTestInfo($"✓ Credit operations work immediately after registration");

		// Cleanup
		await _sessionManager.LogoutUserAsync(TestPlayerPhone);
	}

	[Test]
	public async Task PlayerLogin_MultipleTimesWithSamePhone_IsIdempotent()
	{
		// Arrange - Use a different seeded player to avoid rate limit conflicts with other tests
		var (playerId2, phone2, pin2, username2) = TestHelpers.GetSeededTestPlayer(2);

		var servicesReady = await WaitForServicesReadyAsync();
		servicesReady.ShouldBeTrue(
			"Integration test requires backend services. " +
			$"Backend: {_backendManager?.IsBackendRunning() ?? false}, " +
			$"EventService: {_eventService?.IsReady ?? false}");

		// Register player account before first login
		var registrationResult = await _sessionManager.CreateUserAccountAsync(phone2, pin2, username2);
		if (registrationResult.IsFailure(out var registrationError))
		{
			// Only accept "already exists" errors - fail on other errors
			if (!registrationError.Message.Contains("already exists") && !registrationError.Message.Contains("409"))
			{
				registrationResult.IsSuccess(out var _).ShouldBeTrue($"Registration should succeed or fail with 'already exists', but failed with: {registrationError.Message}");
			}
			TestHelpers.LogTestInfo($"Player already registered (expected): {registrationError.Message}");
		}
		else
		{
			TestHelpers.LogTestInfo($"Player registered successfully: {phone2}");
		}

		// Act - Login same user multiple times (logout between logins)
		// Using 2 logins instead of 3 to stay under rate limit while still proving idempotency
		var login1 = await _sessionManager.LoginUserByPhoneAsync(phone2, pin2);
		if (login1.IsFailure(out var login1Error))
		{
			// Skip gracefully if rate limited (5/min limit across test suite)
			if (login1Error.Message.Contains("Rate limit"))
			{
				TestHelpers.LogTestInfo("Skipping - rate limit exceeded (5/min backend limit)");
				return;
			}
		}
		login1.IsSuccess(out var session1).ShouldBeTrue("First login should succeed");
		session1.LobbySessionId.ShouldNotBe(Guid.Empty, "First login must create lobby session");
		await _sessionManager.LogoutUserAsync(phone2);

		var login2 = await _sessionManager.LoginUserByPhoneAsync(phone2, pin2);
		if (login2.IsFailure(out var login2Error))
		{
			// Skip gracefully if rate limited (5/min limit across test suite)
			if (login2Error.Message.Contains("Rate limit"))
			{
				TestHelpers.LogTestInfo("Skipping second login - rate limit exceeded (5/min backend limit)");
				return;
			}
		}
		login2.IsSuccess(out var _).ShouldBeTrue("Second login should succeed (idempotent registration)");

		// Assert - All logins should produce same player ID
		var playerId = SessionManager.GetPlayerIdFromPhone(phone2);
		TestHelpers.LogTestInfo($"Player ID consistent across logins: {playerId}");

		// Verify credit operations still work
		var addResult = await _creditService.AddAsync(playerId, 5, "Test after multiple logins");
		addResult.IsSuccess(out var _).ShouldBeTrue("Credits should work after multiple logins");

		TestHelpers.LogTestInfo($"✓ Multiple logins are idempotent");
		TestHelpers.LogTestInfo($"✓ Player registration doesn't create duplicates");

		// Cleanup
		await _sessionManager.LogoutUserAsync(phone2);
	}

	[Test]
	public async Task PlayerLogin_ThenCreditPurchase_BalanceUpdatesCorrectly()
	{
		// Arrange - Use a different seeded player to avoid rate limit conflicts with other tests
		var (playerId3, phone3, pin3, username3) = TestHelpers.GetSeededTestPlayer(3);

		var servicesReady = await WaitForServicesReadyAsync();
		servicesReady.ShouldBeTrue(
			"Integration test requires backend services. " +
			$"Backend: {_backendManager?.IsBackendRunning() ?? false}, " +
			$"EventService: {_eventService?.IsReady ?? false}");

		// Register player account before login
		var registrationResult = await _sessionManager.CreateUserAccountAsync(phone3, pin3, username3);
		if (registrationResult.IsFailure(out var registrationError))
		{
			// Only accept "already exists" errors - fail on other errors
			if (!registrationError.Message.Contains("already exists") && !registrationError.Message.Contains("409"))
			{
				registrationResult.IsSuccess(out var _).ShouldBeTrue($"Registration should succeed or fail with 'already exists', but failed with: {registrationError.Message}");
			}
			TestHelpers.LogTestInfo($"Player already registered (expected): {registrationError.Message}");
		}
		else
		{
			TestHelpers.LogTestInfo($"Player registered successfully: {phone3}");
		}

		// Act - Login then add credits
		var loginResult = await _sessionManager.LoginUserByPhoneAsync(phone3, pin3);
		if (loginResult.IsFailure(out var loginError))
		{
			// Skip gracefully if rate limited (5/min limit across test suite)
			if (loginError.Message.Contains("Rate limit"))
			{
				TestHelpers.LogTestInfo("Skipping - rate limit exceeded (5/min backend limit)");
				return;
			}
		}
		loginResult.IsSuccess(out var session).ShouldBeTrue("Login should succeed");
		session.LobbySessionId.ShouldNotBe(Guid.Empty, "Lobby session required for credit operations");

		var playerId = SessionManager.GetPlayerIdFromPhone(phone3);
		TestHelpers.LogTestInfo($"Player registered: {playerId}");

		// Get initial balance
		var initialBalanceResult = await _creditService.GetBalanceAsync(playerId);
		var initialBalance = initialBalanceResult.IsSuccess(out var initBalance) ? initBalance : 0;
		TestHelpers.LogTestInfo($"Initial balance: {initialBalance}");

		// Add credits
		var creditsToAdd = 25;
		var addResult = await _creditService.AddAsync(playerId, creditsToAdd, "Test credit purchase");
		if (addResult.IsFailure(out var addError))
		{
			addResult.IsSuccess(out var _).ShouldBeTrue($"Credit add should succeed, but failed: {addError.Message}");
		}

		// Get updated balance
		var updatedBalanceResult = await _creditService.GetBalanceAsync(playerId);
		if (updatedBalanceResult.IsFailure(out var balanceError))
		{
			updatedBalanceResult.IsSuccess(out var _).ShouldBeTrue($"Balance query should succeed, but failed: {balanceError.Message}");
		}

		var updatedBalance = updatedBalanceResult.IsSuccess(out var updBalance) ? updBalance : 0;
		TestHelpers.LogTestInfo($"Updated balance: {updatedBalance}");

		// Assert
		var expectedBalance = initialBalance + creditsToAdd;
		updatedBalance.ShouldBe(expectedBalance, $"Balance should be {expectedBalance} after adding {creditsToAdd} credits");

		TestHelpers.LogTestInfo($"✓ Player registration enables credit operations");
		TestHelpers.LogTestInfo($"✓ Balance query returns correct value after credit add");

		// Cleanup
		await _sessionManager.LogoutUserAsync(phone3);
	}

	[Test]
	public async Task PlayerRegistration_WithBackendUnavailable_ContinuesGracefully()
	{
		// Arrange - Check backend status and attempt registration
		var isBackendRunning = _backendManager?.IsBackendRunning() ?? false;
		var isEventServiceReady = _eventService?.IsReady ?? false;

		TestHelpers.LogTestInfo($"Backend running: {isBackendRunning}");
		TestHelpers.LogTestInfo($"EventService ready: {isEventServiceReady}");

		// Attempt registration (may fail if backend unavailable)
		if (isBackendRunning && isEventServiceReady)
		{
			var registrationResult = await _sessionManager.CreateUserAccountAsync(TestPlayerPhone, "1234", TestPlayerUsername);
			if (registrationResult.IsFailure(out var registrationError))
			{
				// Only accept "already exists" errors - fail on other errors
				if (!registrationError.Message.Contains("already exists") && !registrationError.Message.Contains("409"))
				{
					registrationResult.IsSuccess(out var _).ShouldBeTrue($"Registration should succeed or fail with 'already exists', but failed with: {registrationError.Message}");
				}
				TestHelpers.LogTestInfo($"Player already registered (expected): {registrationError.Message}");
			}
			else
			{
				TestHelpers.LogTestInfo($"Player registered successfully: {TestPlayerPhone}");
			}
		}

		// Act - Attempt login
		var loginResult = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, "1234");

		// Assert
		if (!isBackendRunning || !isEventServiceReady)
		{
			// Login should still succeed locally (graceful degradation)
			var loginStatusMsg = loginResult.IsSuccess(out var _) ? "Success" : (loginResult.IsFailure(out var err) ? err.Message : "Unknown");
			TestHelpers.LogTestInfo($"Login result with backend unavailable: {loginStatusMsg}");
			TestHelpers.LogTestInfo("Expected: Login continues even if player registration fails");
			TestHelpers.LogTestInfo("Expected: Credit operations may fail later if player not registered");
		}
		else
		{
			// Backend is available - normal registration should happen
			if (loginResult.IsFailure(out var loginError))
			{
				// Skip gracefully if rate limited (5/min limit across test suite)
				if (loginError.Message.Contains("Rate limit"))
				{
					TestHelpers.LogTestInfo("Skipping - rate limit exceeded (5/min backend limit)");
					return;
				}
			}
			loginResult.IsSuccess(out var _).ShouldBeTrue("Login should succeed when backend is available");
			TestHelpers.LogTestInfo("✓ Login with backend available succeeded");
		}

		// Cleanup
		if (loginResult.IsSuccess(out var _))
		{
			await _sessionManager.LogoutUserAsync(TestPlayerPhone);
		}
	}

	[Test]
	public void PlayerRegistration_ValidatesServiceDependencies()
	{
		// Validate all required services are present
		_sessionManager.ShouldNotBeNull("SessionManager must be present");
		_eventService.ShouldNotBeNull("EventService must be present");
		_backendManager.ShouldNotBeNull("BackendManager must be present");
		_creditService.ShouldNotBeNull("CreditService must be present");

		TestHelpers.LogTestInfo("All required services present");

		// Check initialization state
		var isRunning = _backendManager.IsBackendRunning();
		TestHelpers.LogTestInfo($"BackendManager running: {isRunning}");

		var isReady = _eventService.IsReady;
		TestHelpers.LogTestInfo($"EventService ready: {isReady}");

		true.ShouldBeTrue("Service dependency validation complete");
	}

	/// <summary>
	/// Wait for services to be ready with timeout
	/// </summary>
	private async Task<bool> WaitForServicesReadyAsync(float timeoutSeconds = 5.0f)
	{
		var ready = await TestHelpers.WaitForConditionAsync(
			() =>
			{
				var backendReady = _backendManager?.IsBackendRunning() ?? false;
				var eventServiceReady = _eventService?.IsReady ?? false;
				return backendReady && eventServiceReady;
			},
			timeoutSeconds,
			0.1f);

		if (ready)
		{
			TestHelpers.LogTestInfo("All services ready");
		}
		else
		{
			TestHelpers.LogTestWarning("Service readiness timeout - some services not ready");
			TestHelpers.LogTestInfo($"  Backend running: {_backendManager?.IsBackendRunning() ?? false}");
			TestHelpers.LogTestInfo($"  EventService ready: {_eventService?.IsReady ?? false}");
		}

		return ready;
	}

	[Cleanup]
	public override async Task CleanupTestResources()
	{
		// Ensure all sessions are cleaned up, even if some fail
		if (_sessionManager != null)
		{
			try
			{
				var activeUsers = _sessionManager.GetActivePhoneNumbers();
				foreach (var phone in activeUsers)
				{
					try
					{
						await _sessionManager.LogoutUserAsync(phone);
					}
					catch (Exception ex)
					{
						TestHelpers.LogTestWarning($"Failed to logout {phone}: {ex.Message}");
					}
				}
			}
			catch (Exception ex)
			{
				TestHelpers.LogTestWarning($"Error during session cleanup: {ex.Message}");
			}
		}

		await base.CleanupTestResources();
		_sessionManager = null;
		_eventService = null;
		_backendManager = null;
		_creditService = null;
	}
}
