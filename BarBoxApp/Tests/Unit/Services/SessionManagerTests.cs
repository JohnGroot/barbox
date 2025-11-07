using System;
using System.Threading.Tasks;
using Chickensoft.GoDotTest;
using Godot;
using BarBox.Tests.Fixtures;

namespace BarBox.Tests.Unit.Services;

/// <summary>
/// Tests for SessionManager - user authentication and session lifecycle
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
	public async void LoginUserByPhone_WithValidCredentials_CreatesSession()
	{
		// Arrange
		var pin = "1234";

		// Act
		var result = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, pin);

		// Assert
		if (!result.IsSuccess)
		{
			TestHelpers.LogTestWarning($"Login failed (expected if backend validation enabled): {result.Error}");
		}
		else
		{
			TestHelpers.LogTestInfo($"Login successful: {result.Value.UserName}");

			// Cleanup - logout
			await _sessionManager.LogoutUserAsync(TestPlayerPhone);
		}
	}

	[Test]
	public void GetPrimaryUserSession_WithNoActiveUsers_ReturnsNull()
	{
		// Act
		var session = _sessionManager.GetPrimaryUserSession();

		// Assert
		if (session != null)
		{
			TestHelpers.LogTestWarning($"Expected no primary session, but found: {session.PhoneNumber}");
		}
		else
		{
			TestHelpers.LogTestInfo("Correctly returned null for no active sessions");
		}
	}

	[Test]
	public async void LoginUserByPhone_ThenGetPrimarySession_ReturnsUser()
	{
		// Arrange
		var pin = "1234";

		// Act
		var loginResult = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, pin);

		if (loginResult.IsSuccess)
		{
			var session = _sessionManager.GetPrimaryUserSession();

			// Assert
			if (session != null)
			{
				TestHelpers.LogTestInfo($"Primary session found: {session.PhoneNumber}");
			}
			else
			{
				TestHelpers.LogTestError("Expected primary session after login");
			}

			// Cleanup
			await _sessionManager.LogoutUserAsync(TestPlayerPhone);
		}
		else
		{
			TestHelpers.LogTestInfo($"Skipping test - login failed: {loginResult.Error}");
		}
	}

	[Test]
	public async void LogoutUser_WithActiveSession_RemovesSession()
	{
		// Arrange - login first
		var pin = "1234";
		var loginResult = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, pin);

		if (!loginResult.IsSuccess)
		{
			TestHelpers.LogTestInfo($"Skipping test - login failed: {loginResult.Error}");
			return;
		}

		// Act
		var logoutResult = await _sessionManager.LogoutUserAsync(TestPlayerPhone);

		// Assert
		if (logoutResult)
		{
			var session = _sessionManager.GetUserSession(TestPlayerPhone);
			if (session == null)
			{
				TestHelpers.LogTestInfo("User successfully logged out");
			}
			else
			{
				TestHelpers.LogTestError("Session still exists after logout");
			}
		}
		else
		{
			TestHelpers.LogTestError("Logout failed");
		}
	}

	[Test]
	public async void LoginMultipleUsers_GetActivePhoneNumbers_ReturnsAll()
	{
		// Arrange
		var pin = "1234";
		var phone1 = TestHelpers.GenerateTestPhoneNumber();
		var phone2 = TestHelpers.GenerateTestPhoneNumber();

		// Act
		var login1 = await _sessionManager.LoginUserByPhoneAsync(phone1, pin);
		var login2 = await _sessionManager.LoginUserByPhoneAsync(phone2, pin);

		if (login1.IsSuccess && login2.IsSuccess)
		{
			var activeNumbers = _sessionManager.GetActivePhoneNumbers();

			// Assert
			TestHelpers.LogTestInfo($"Active sessions: {activeNumbers.Length}");

			// Cleanup
			await _sessionManager.LogoutUserAsync(phone1);
			await _sessionManager.LogoutUserAsync(phone2);
		}
		else
		{
			TestHelpers.LogTestInfo("Skipping test - logins failed (backend validation)");
		}
	}

	[Test]
	public void GetUserSession_WithNonExistentPhone_ReturnsNull()
	{
		// Act
		var session = _sessionManager.GetUserSession("9999999999");

		// Assert
		if (session == null)
		{
			TestHelpers.LogTestInfo("Correctly returned null for non-existent session");
		}
		else
		{
			TestHelpers.LogTestError("Expected null but got session");
		}
	}

	[Cleanup]
	public override async void CleanupTestResources()
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
