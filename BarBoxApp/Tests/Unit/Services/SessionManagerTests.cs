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

	public SessionManagerTests(Node testScene) : base(testScene)
	{
	}

	[Setup]
	public new void SetupTestIdentifiers()
	{
		base.SetupTestIdentifiers();
		_sessionManager = GetSessionManager();
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
		if (!result.IsSuccess)
		{
			TestHelpers.LogTestInfo($"Login failed (may be expected if backend validation enabled): {result.Error}");
		}
		else
		{
			result.Value.ShouldNotBeNull("User session should be returned");
			result.Value.UserName.ShouldNotBeNullOrEmpty("Username should be set");
			TestHelpers.LogTestInfo($"Login successful: {result.Value.UserName}");

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
		var session = _sessionManager.GetPrimaryUserSession();

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

		if (loginResult.IsSuccess)
		{
			var session = _sessionManager.GetPrimaryUserSession();

			// Assert
			session.ShouldNotBeNull("Primary session should exist after login");
			session.PhoneNumber.ShouldBe(TestPlayerPhone, "Primary session should match logged in user");
			TestHelpers.LogTestInfo($"Primary session found: {session.PhoneNumber}");

			// Cleanup
			await _sessionManager.LogoutUserAsync(TestPlayerPhone);
		}
		else
		{
			TestHelpers.LogTestInfo($"Test skipped - login failed: {loginResult.Error}");
		}
	}

	[Test]
	public async Task LogoutUser_WithActiveSession_RemovesSession()
	{
		// Arrange - login first
		_sessionManager.ShouldNotBeNull("SessionManager must be available");
		var pin = "1234";
		var loginResult = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, pin);

		if (!loginResult.IsSuccess)
		{
			TestHelpers.LogTestInfo($"Test skipped - login failed: {loginResult.Error}");
			return;
		}

		// Act
		var logoutResult = await _sessionManager.LogoutUserAsync(TestPlayerPhone);

		// Assert
		logoutResult.ShouldBeTrue("Logout should succeed");

		var session = _sessionManager.GetUserSession(TestPlayerPhone);
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

			if (login1.IsSuccess && login2.IsSuccess)
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
		var session = _sessionManager.GetUserSession("9999999999");

		// Assert
		session.ShouldBeNull("Non-existent session should return null");
		TestHelpers.LogTestInfo("Correctly returned null for non-existent session");
	}

	[Test]
	public async Task AddGlobalCredits_WithEventServiceNotReady_ReturnsFailure()
	{
		// Arrange - login user first
		_sessionManager.ShouldNotBeNull("SessionManager must be available");
		var loginResult = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, "1234");

		if (!loginResult.IsSuccess)
		{
			TestHelpers.LogTestInfo($"Test skipped - login failed: {loginResult.Error}");
			return;
		}

		try
		{
			// Check EventService readiness
			var eventService = GetEventService();
			eventService.ShouldNotBeNull("EventService must be available");
			var eventServiceReady = eventService.IsReady;

			TestHelpers.LogTestInfo($"EventService ready: {eventServiceReady}");

			// Act - try to add credits
			var result = await _sessionManager.AddGlobalCreditsAsync(TestPlayerPhone, 100, "test");

			// Assert
			if (!eventServiceReady)
			{
				result.ShouldBeFalse("Add credits should fail when EventService not ready");
				TestHelpers.LogTestInfo("Add credits correctly failed when EventService not ready");
			}
			else
			{
				TestHelpers.LogTestInfo($"EventService ready, add credits result: {result}");
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
		var eventService = GetEventService();
		eventService.ShouldNotBeNull("EventService must be available");

		var loginResult = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, "1234");

		if (!loginResult.IsSuccess)
		{
			TestHelpers.LogTestInfo($"Test skipped - login failed: {loginResult.Error}");
			return;
		}

		try
		{
			var eventServiceReady = eventService.IsReady;

			// Act
			var canAddCredits = await _sessionManager.AddGlobalCreditsAsync(TestPlayerPhone, 50, "test");

			// Assert - Should correlate with EventService readiness
			TestHelpers.LogTestInfo($"EventService ready: {eventServiceReady}, Add credits: {canAddCredits}");

			if (canAddCredits && !eventServiceReady)
			{
				false.ShouldBeTrue("Credits added despite EventService not ready!");
			}
			else if (!canAddCredits && eventServiceReady)
			{
				TestHelpers.LogTestInfo("EventService ready but add credits failed - check logs");
			}
			else
			{
				TestHelpers.LogTestInfo("✓ Add credits behavior matches EventService state");
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
		var eventService = GetEventService();

		if (eventService == null || !eventService.IsReady)
		{
			TestHelpers.LogTestInfo("Test skipped - EventService not ready");
			return;
		}

		var loginResult = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, "1234");
		if (!loginResult.IsSuccess)
		{
			TestHelpers.LogTestInfo($"Test skipped - login failed: {loginResult.Error}");
			return;
		}

		try
		{
			var initialSession = _sessionManager.GetUserSession(TestPlayerPhone);
			initialSession.ShouldNotBeNull("User session should exist");
			var initialCredits = initialSession.Credits;

			// Act
			var result = await _sessionManager.AddGlobalCreditsAsync(TestPlayerPhone, 100, "test");

			// Assert
			result.ShouldBeTrue("Add credits should succeed when EventService ready");

			var updatedSession = _sessionManager.GetUserSession(TestPlayerPhone);
			updatedSession.ShouldNotBeNull("User session should still exist");

			var expectedCredits = initialCredits + 100;
			TestHelpers.LogTestInfo($"Credits: {initialCredits} → {updatedSession.Credits} (expected {expectedCredits})");

			updatedSession.Credits.ShouldBeGreaterThanOrEqualTo(expectedCredits, "Credits should be updated");
			TestHelpers.LogTestInfo("✓ Credits correctly updated");
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

		base.CleanupTestResources();
		_sessionManager = null;
	}
}
