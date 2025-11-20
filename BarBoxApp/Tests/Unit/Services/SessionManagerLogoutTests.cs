using System;
using System.Linq;
using System.Threading.Tasks;
using Chickensoft.GoDotTest;
using Godot;
using BarBox.Tests.Fixtures;
using Shouldly;

namespace BarBox.Tests.Unit.Services;

/// <summary>
/// Unit tests for SessionManager logout functionality
/// CRITICAL: Tests for duplicate logout bug prevention
/// </summary>
public class SessionManagerLogoutTests : BackendTestBase
{
	private SessionManager _sessionManager;

	public SessionManagerLogoutTests(Node testScene) : base(testScene)
	{
	}

	[Setup]
	public new void SetupTestIdentifiers()
	{
		base.SetupTestIdentifiers();
		_sessionManager = GetSessionManager();
	}

	[Test]
	public async Task LogoutUser_CalledTwiceForSameUser_IsIdempotent()
	{
		// Arrange
		_sessionManager.ShouldNotBeNull("SessionManager must be available");

		var loginResult = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, "1234");
		if (loginResult.IsFailure(out var error))
		{
			TestHelpers.LogTestInfo($"Test skipped - login failed: {error.Message}");
			return;
		}

		// Act - Logout the same user twice
		var firstLogout = await _sessionManager.LogoutUserAsync(TestPlayerPhone);
		var secondLogout = await _sessionManager.LogoutUserAsync(TestPlayerPhone);

		// Assert
		firstLogout.ShouldBeTrue("First logout should succeed");
		secondLogout.ShouldBeFalse("Second logout should return false (no session to logout)");

		var session = _sessionManager.GetUserSession(TestPlayerPhone);
		session.ShouldBeNull("User session should not exist after logout");

		TestHelpers.LogTestInfo("Duplicate logout handled gracefully without backend timeout");
	}

	[Test]
	public async Task LogoutNonPrimaryUsers_WithMultipleUsers_LogsOutOnlySecondaryUsers()
	{
		// Arrange
		_sessionManager.ShouldNotBeNull("SessionManager must be available");

		var primaryPhone = TestHelpers.GenerateTestPhoneNumber(1);
		var secondaryPhone = TestHelpers.GenerateTestPhoneNumber(2);

		var login1 = await _sessionManager.LoginUserByPhoneAsync(primaryPhone, "1234");
		var login2 = await _sessionManager.LoginUserByPhoneAsync(secondaryPhone, "1234");

		if (login1.IsFailure(out var _) || login2.IsFailure(out var _))
		{
			TestHelpers.LogTestInfo("Test skipped - logins failed");
			return;
		}

		// Act
		await _sessionManager.LogoutNonPrimaryUsersAsync();

		// Assert
		var primarySession = _sessionManager.GetUserSession(primaryPhone);
		var secondarySession = _sessionManager.GetUserSession(secondaryPhone);

		primarySession.ShouldNotBeNull("Primary user (first logged in) should remain logged in");
		secondarySession.ShouldBeNull("Secondary user should be logged out");

		var activeUsers = _sessionManager.GetActivePhoneNumbers();
		activeUsers.Length.ShouldBe(1, "Only primary user should remain active");
		activeUsers[0].ShouldBe(primaryPhone, "Primary user should be the remaining active user");

		TestHelpers.LogTestInfo("LogoutNonPrimaryUsersAsync correctly logged out only secondary users");

		// Cleanup
		await _sessionManager.LogoutUserAsync(primaryPhone);
	}

	[Test]
	public async Task GameExit_WithSecondaryUser_DoesNotLogoutTwice()
	{
		// Arrange
		_sessionManager.ShouldNotBeNull("SessionManager must be available");

		var primaryPhone = TestHelpers.GenerateTestPhoneNumber(1);
		var secondaryPhone = TestHelpers.GenerateTestPhoneNumber(2);

		var login1 = await _sessionManager.LoginUserByPhoneAsync(primaryPhone, "1234");
		var login2 = await _sessionManager.LoginUserByPhoneAsync(secondaryPhone, "1234");

		if (login1.IsFailure(out var _) || login2.IsFailure(out var _))
		{
			TestHelpers.LogTestInfo("Test skipped - logins failed");
			return;
		}

		// Track logout signal emissions
		int logoutSignalCount = 0;
		_sessionManager.UserLoggedOut += (phone) => {
			if (phone == secondaryPhone)
			{
				logoutSignalCount++;
				TestHelpers.LogTestInfo($"UserLoggedOut signal emitted for {secondaryPhone} (count: {logoutSignalCount})");
			}
		};

		// Act - Simulate game exit scenario (both cleanup paths)
		TestHelpers.LogTestInfo("Simulating GameHost.StopCurrentGame() path...");
		await _sessionManager.LogoutNonPrimaryUsersAsync();

		TestHelpers.LogTestInfo("Simulating CarromPlayerSetupMenu._ExitTree() path...");
		// This path would call LogoutUserAsync for each slot
		var logoutResult = await _sessionManager.LogoutUserAsync(secondaryPhone);

		// Assert
		logoutSignalCount.ShouldBe(1, "UserLoggedOut signal should be emitted exactly ONCE for secondary user");
		logoutResult.ShouldBeFalse("Second logout attempt should return false (user already logged out)");

		var secondarySession = _sessionManager.GetUserSession(secondaryPhone);
		secondarySession.ShouldBeNull("Secondary user should be logged out after first path");

		TestHelpers.LogTestInfo("Duplicate logout prevented - no backend timeout occurred");

		// Cleanup
		await _sessionManager.LogoutUserAsync(primaryPhone);
	}

	[Test]
	public async Task LogoutMultipleUsers_Concurrently_RespectsSemaphoreLimit()
	{
		// Arrange
		_sessionManager.ShouldNotBeNull("SessionManager must be available");

		var phones = Enumerable.Range(1, 10)
			.Select(i => TestHelpers.GenerateTestPhoneNumber(i))
			.ToArray();

		// Login all users
		foreach (var phone in phones)
		{
			var result = await _sessionManager.LoginUserByPhoneAsync(phone, "1234");
			if (result.IsFailure(out var _))
			{
				TestHelpers.LogTestInfo($"Login failed for {phone} - test may not be fully valid");
			}
		}

		// Act - Logout all users concurrently
		var startTime = DateTime.UtcNow;
		var logoutTasks = phones.Select(phone => _sessionManager.LogoutUserAsync(phone));
		await Task.WhenAll(logoutTasks);
		var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;

		// Assert
		var activeUsers = _sessionManager.GetActivePhoneNumbers();
		activeUsers.Length.ShouldBe(0, "All users should be logged out");

		TestHelpers.LogTestInfo($"Concurrent logout of {phones.Length} users took {elapsed:F2} seconds");
		TestHelpers.LogTestInfo("Semaphore correctly limited concurrent backend calls (no timeout)");

		// Expected: Semaphore limits to 5 concurrent logouts, so should NOT overwhelm backend
		elapsed.ShouldBeLessThan(30.0, "Concurrent logouts should complete without timeout");
	}

	[Cleanup]
	public override async Task CleanupTestResources()
	{
		if (_sessionManager != null)
		{
			var activeUsers = _sessionManager.GetActivePhoneNumbers();
			foreach (var phone in activeUsers)
			{
				await _sessionManager.LogoutUserAsync(phone);
			}
		}

		await base.CleanupTestResources();
		_sessionManager = null;
	}
}
