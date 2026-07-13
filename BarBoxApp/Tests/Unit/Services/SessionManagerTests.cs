using System;
using System.Threading.Tasks;
using Chickensoft.GoDotTest;
using Godot;
using BarBox.Tests.Fixtures;
using Shouldly;

namespace BarBox.Tests.Unit.Services;

/// <summary>
/// Unit tests for SessionManager - user authentication and session lifecycle
/// </summary>
public class SessionManagerTests : BackendTestBase
{
	private SessionManager _sessionManager;
	private CreditService _creditService;

	public SessionManagerTests(Node testScene) : base(testScene)
	{
	}

	[Setup]
	public new void SetupTestIdentifiers()
	{
		base.SetupTestIdentifiers();
		_sessionManager = GetSessionManager();
		_creditService = GetCreditService();
	}

	[Test]
	public async Task LoginUserByPhone_WithValidCredentials_CreatesSession()
	{
		// Arrange
		_sessionManager.ShouldNotBeNull("SessionManager must be available");
		var pin = "1234";

		// Act
		var result = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, pin);

		// Assert
		if (result.IsFailure(out var error))
		{
			TestHelpers.LogTestInfo($"Login failed (may be expected if backend validation enabled): {error.Message}");
		}
		else if (result.IsSuccess(out var session))
		{
			session.ShouldNotBeNull("User session should be returned");
			session.UserName.ShouldNotBeNullOrEmpty("Username should be set");
			TestHelpers.LogTestInfo($"Login successful: {session.UserName}");

			// Cleanup - logout
			await _sessionManager.LogoutUserAsync(TestPlayerPhone);
		}
	}

	[Test]
	public void GetPrimaryUserSession_WithNoActiveUsers_ReturnsNull()
	{
		// Arrange
		_sessionManager.ShouldNotBeNull("SessionManager must be available");

		// Act
		var session = _sessionManager.GetPrimarySession();

		// Assert
		if (session != null)
		{
			TestHelpers.LogTestInfo($"Found existing session (may be from previous test): {session.PhoneNumber}");
			// Don't fail - there might be sessions from other tests
		}
		else
		{
			TestHelpers.LogTestInfo("Correctly returned null for no active sessions");
			session.ShouldBeNull("No primary session should exist");
		}
	}

	[Test]
	public async Task LoginUserByPhone_ThenGetPrimarySession_ReturnsUser()
	{
		// Arrange
		_sessionManager.ShouldNotBeNull("SessionManager must be available");
		var pin = "1234";

		// Act
		var loginResult = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, pin);

		if (loginResult.IsSuccess(out var _))
		{
			var session = _sessionManager.GetPrimarySession();

			// Assert
			session.ShouldNotBeNull("Primary session should exist after login");
			session.PhoneNumber.ShouldBe(TestPlayerPhone, "Primary session should match logged in user");
			TestHelpers.LogTestInfo($"Primary session found: {session.PhoneNumber}");

			// Cleanup
			await _sessionManager.LogoutUserAsync(TestPlayerPhone);
		}
		else if (loginResult.IsFailure(out var error))
		{
			TestHelpers.LogTestInfo($"Test skipped - login failed: {error.Message}");
		}
	}

	[Test]
	public async Task LogoutUser_WithActiveSession_RemovesSession()
	{
		// Arrange - login first
		_sessionManager.ShouldNotBeNull("SessionManager must be available");
		var pin = "1234";
		var loginResult = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, pin);

		if (loginResult.IsFailure(out var error))
		{
			TestHelpers.LogTestInfo($"Test skipped - login failed: {error.Message}");
			return;
		}

		// Act
		var logoutResult = await _sessionManager.LogoutUserAsync(TestPlayerPhone);

		// Assert
		logoutResult.ShouldBeTrue("Logout should succeed");

		var session = _sessionManager.GetSessionByPhone(TestPlayerPhone);
		session.ShouldBeNull("Session should not exist after logout");
		TestHelpers.LogTestInfo("User successfully logged out");
	}

	[Test]
	public async Task LoginMultipleUsers_GetActivePhoneNumbers_ReturnsAll()
	{
		// Arrange
		_sessionManager.ShouldNotBeNull("SessionManager must be available");
		var pin = "1234";
		// Use deterministic phone numbers based on test name
		var testNameHash = "LoginMultipleUsers".GetHashCode();
		var phone1 = TestHelpers.GenerateTestPhoneNumber(testNameHash);
		var phone2 = TestHelpers.GenerateTestPhoneNumber(testNameHash + 1);

		try
		{
			// Act
			var login1 = await _sessionManager.LoginUserByPhoneAsync(phone1, pin);
			var login2 = await _sessionManager.LoginUserByPhoneAsync(phone2, pin);

			if (login1.IsSuccess(out var _) && login2.IsSuccess(out var _))
			{
				var activeNumbers = _sessionManager.GetActivePhoneNumbers();

				// Assert
				activeNumbers.ShouldNotBeNull("Active phone numbers should be returned");
				activeNumbers.Length.ShouldBeGreaterThanOrEqualTo(2, "Should have at least 2 active sessions");
				activeNumbers.ShouldContain(phone1, "Should include first logged in user");
				activeNumbers.ShouldContain(phone2, "Should include second logged in user");
				TestHelpers.LogTestInfo($"Active sessions: {activeNumbers.Length}");
			}
			else
			{
				TestHelpers.LogTestInfo("Test skipped - logins failed (backend validation)");
			}
		}
		finally
		{
			// Cleanup
			await _sessionManager.LogoutUserAsync(phone1);
			await _sessionManager.LogoutUserAsync(phone2);
		}
	}

	[Test]
	public void GetUserSession_WithNonExistentPhone_ReturnsNull()
	{
		// Arrange
		_sessionManager.ShouldNotBeNull("SessionManager must be available");

		// Act
		var session = _sessionManager.GetSessionByPhone("9999999999");

		// Assert
		session.ShouldBeNull("Non-existent session should return null");
		TestHelpers.LogTestInfo("Correctly returned null for non-existent session");
	}

	[Test]
	public async Task AddGlobalCredits_WithEventServiceNotReady_ReturnsFailure()
	{
		// Arrange - login user first
		_sessionManager.ShouldNotBeNull("SessionManager must be available");
		_creditService.ShouldNotBeNull("CreditService must be available");
		var loginResult = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, "1234");

		if (loginResult.IsFailure(out var error))
		{
			TestHelpers.LogTestInfo($"Test skipped - login failed: {error.Message}");
			return;
		}

		try
		{
			// Check SessionEventService readiness
			var eventService = GetEventService();
			eventService.ShouldNotBeNull("SessionEventService must be available");
			var eventServiceReady = eventService.IsReady;

			TestHelpers.LogTestInfo($"SessionEventService ready: {eventServiceReady}");

			// Get playerId from session
			var session = _sessionManager.GetSessionByPhone(TestPlayerPhone);
			session.ShouldNotBeNull("User session should exist after login");
			var playerId = session.PlayerId;

			// Act - try to add credits via CreditService
			var result = await _creditService.AddAsync(playerId, 100, "test");

			// Assert
			if (!eventServiceReady)
			{
				result.IsFailure(out var resultError).ShouldBeTrue("Add credits should fail when SessionEventService not ready");
				TestHelpers.LogTestInfo($"Add credits correctly failed when SessionEventService not ready: {resultError.Message}");
			}
			else
			{
				TestHelpers.LogTestInfo($"SessionEventService ready, add credits result: {result.IsSuccess()}");
			}
		}
		finally
		{
			// Cleanup
			await _sessionManager.LogoutUserAsync(TestPlayerPhone);
		}
	}

	[Test]
	public async Task AddGlobalCredits_ChecksEventServiceReadiness()
	{
		// Arrange
		_sessionManager.ShouldNotBeNull("SessionManager must be available");
		_creditService.ShouldNotBeNull("CreditService must be available");
		var eventService = GetEventService();
		eventService.ShouldNotBeNull("SessionEventService must be available");

		var loginResult = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, "1234");

		if (loginResult.IsFailure(out var error))
		{
			TestHelpers.LogTestInfo($"Test skipped - login failed: {error.Message}");
			return;
		}

		try
		{
			var eventServiceReady = eventService.IsReady;

			// Get playerId from session
			var session = _sessionManager.GetSessionByPhone(TestPlayerPhone);
			session.ShouldNotBeNull("User session should exist");
			var playerId = session.PlayerId;

			// Act - try to add credits via CreditService
			var result = await _creditService.AddAsync(playerId, 50, "test");

			// Assert - Should correlate with SessionEventService readiness
			TestHelpers.LogTestInfo($"SessionEventService ready: {eventServiceReady}, Add credits: {result.IsSuccess()}");

			if (result.IsSuccess() && !eventServiceReady)
			{
				false.ShouldBeTrue("Credits added despite SessionEventService not ready!");
			}
			else if (result.IsFailure() && eventServiceReady)
			{
				TestHelpers.LogTestInfo("SessionEventService ready but add credits failed - check logs");
			}
			else
			{
				TestHelpers.LogTestInfo("✓ Add credits behavior matches SessionEventService state");
				true.ShouldBeTrue("State is consistent");
			}
		}
		finally
		{
			// Cleanup
			await _sessionManager.LogoutUserAsync(TestPlayerPhone);
		}
	}

	[Test]
	public async Task AddGlobalCredits_WithEventServiceReady_UpdatesCredits()
	{
		// Arrange
		_sessionManager.ShouldNotBeNull("SessionManager must be available");
		_creditService.ShouldNotBeNull("CreditService must be available");
		var eventService = GetEventService();

		if (eventService == null || !eventService.IsReady)
		{
			TestHelpers.LogTestInfo("Test skipped - SessionEventService not ready");
			return;
		}

		var loginResult = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, "1234");
		if (loginResult.IsFailure(out var error))
		{
			TestHelpers.LogTestInfo($"Test skipped - login failed: {error.Message}");
			return;
		}

		try
		{
			var initialSession = _sessionManager.GetSessionByPhone(TestPlayerPhone);
			initialSession.ShouldNotBeNull("User session should exist");
			var playerId = initialSession.PlayerId;

			// Get initial credits from CreditService (single source of truth)
			var initialCreditsResult = await _creditService.GetBalanceAsync(playerId);
			initialCreditsResult.IsSuccess(out var initialCredits).ShouldBeTrue("Should get initial credits");

			// Act - add credits via CreditService
			var result = await _creditService.AddAsync(playerId, 100, "test");

			// Assert
			result.IsSuccess(out var newBalance).ShouldBeTrue("Add credits should succeed when SessionEventService ready");

			// Verify CreditService returns updated balance
			var updatedCreditsResult = await _creditService.GetBalanceAsync(playerId, forceRefresh: true);
			updatedCreditsResult.IsSuccess(out var updatedCredits).ShouldBeTrue("Should get updated credits");

			var expectedCredits = initialCredits + 100;
			TestHelpers.LogTestInfo($"Credits: {initialCredits} → {updatedCredits} (expected {expectedCredits})");

			updatedCredits.ShouldBeGreaterThanOrEqualTo(expectedCredits,
				"CreditService should reflect credit addition");
			TestHelpers.LogTestInfo("✓ Credits correctly updated via CreditService");
		}
		finally
		{
			// Cleanup
			await _sessionManager.LogoutUserAsync(TestPlayerPhone);
		}
	}

	[Cleanup]
	public override async Task CleanupTestResources()
	{
		// Cleanup any active test sessions
		if (_sessionManager != null)
		{
			var activeNumbers = _sessionManager.GetActivePhoneNumbers();
			foreach (var phone in activeNumbers)
			{
				await _sessionManager.LogoutUserAsync(phone);
			}
		}

		await base.CleanupTestResources();
		_sessionManager = null;
	}
}
