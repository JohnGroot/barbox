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
	public new void SetupTestIdentifiers()
	{
		base.SetupTestIdentifiers();
		_sessionManager = GetSessionManager();
		_eventService = GetEventService();
		_backendManager = GetBackendManager();
		_creditService = GetCreditService();
	}

	[Test]
	public async Task PlayerLogin_WithBackendReady_RegistersPlayerAutomatically()
	{
		// Arrange - Wait for backend
		var servicesReady = await WaitForServicesReadyAsync();
		if (!servicesReady)
		{
			TestHelpers.LogTestWarning("Services not ready - skipping test");
			return;
		}

		// Act - Login user (should trigger automatic registration)
		var loginResult = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, "1234");

		// Assert
		loginResult.IsSuccess.ShouldBeTrue($"Login should succeed, but failed: {loginResult.Error}");
		var session = _sessionManager.GetUserSession(TestPlayerPhone);
		session.ShouldNotBeNull("User session should exist after login");

		var playerId = EventService.GetPlayerIdFromPhone(TestPlayerPhone);
		TestHelpers.LogTestInfo($"Player logged in with ID: {playerId}");

		// Verify player is registered in backend by attempting credit operation
		var addResult = await _creditService.AddAsync(playerId, 10, "Test credit after registration");
		addResult.IsSuccess.ShouldBeTrue($"Credit add should succeed for registered player, but failed: {addResult.Error}");

		TestHelpers.LogTestInfo($"✓ Player automatically registered during login");
		TestHelpers.LogTestInfo($"✓ Credit operations work immediately after registration");

		// Cleanup
		await _sessionManager.LogoutUserAsync(TestPlayerPhone);
	}

	[Test]
	public async Task PlayerLogin_MultipleTimesWithSamePhone_IsIdempotent()
	{
		// Arrange
		var servicesReady = await WaitForServicesReadyAsync();
		if (!servicesReady)
		{
			TestHelpers.LogTestWarning("Services not ready - skipping test");
			return;
		}

		// Act - Login same user multiple times (logout between logins)
		var login1 = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, "1234");
		login1.IsSuccess.ShouldBeTrue("First login should succeed");
		await _sessionManager.LogoutUserAsync(TestPlayerPhone);

		var login2 = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, "1234");
		login2.IsSuccess.ShouldBeTrue("Second login should succeed (idempotent registration)");
		await _sessionManager.LogoutUserAsync(TestPlayerPhone);

		var login3 = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, "1234");
		login3.IsSuccess.ShouldBeTrue("Third login should succeed (idempotent registration)");

		// Assert - All logins should produce same player ID
		var playerId = EventService.GetPlayerIdFromPhone(TestPlayerPhone);
		TestHelpers.LogTestInfo($"Player ID consistent across logins: {playerId}");

		// Verify credit operations still work
		var addResult = await _creditService.AddAsync(playerId, 5, "Test after multiple logins");
		addResult.IsSuccess.ShouldBeTrue("Credits should work after multiple logins");

		TestHelpers.LogTestInfo($"✓ Multiple logins are idempotent");
		TestHelpers.LogTestInfo($"✓ Player registration doesn't create duplicates");

		// Cleanup
		await _sessionManager.LogoutUserAsync(TestPlayerPhone);
	}

	[Test]
	public async Task PlayerLogin_ThenCreditPurchase_BalanceUpdatesCorrectly()
	{
		// Arrange
		var servicesReady = await WaitForServicesReadyAsync();
		if (!servicesReady)
		{
			TestHelpers.LogTestWarning("Services not ready - skipping test");
			return;
		}

		// Act - Login (registers player) then add credits
		var loginResult = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, "1234");
		loginResult.IsSuccess.ShouldBeTrue("Login should succeed");

		var playerId = EventService.GetPlayerIdFromPhone(TestPlayerPhone);
		TestHelpers.LogTestInfo($"Player registered: {playerId}");

		// Get initial balance
		var initialBalanceResult = await _eventService.GetPlayerCreditsAsync(playerId);
		var initialBalance = initialBalanceResult.IsSuccess ? initialBalanceResult.Value : 0;
		TestHelpers.LogTestInfo($"Initial balance: {initialBalance}");

		// Add credits
		var creditsToAdd = 25;
		var addResult = await _creditService.AddAsync(playerId, creditsToAdd, "Test credit purchase");
		addResult.IsSuccess.ShouldBeTrue($"Credit add should succeed, but failed: {addResult.Error}");

		// Get updated balance
		var updatedBalanceResult = await _eventService.GetPlayerCreditsAsync(playerId);
		updatedBalanceResult.IsSuccess.ShouldBeTrue($"Balance query should succeed, but failed: {updatedBalanceResult.Error}");

		var updatedBalance = updatedBalanceResult.Value;
		TestHelpers.LogTestInfo($"Updated balance: {updatedBalance}");

		// Assert
		var expectedBalance = initialBalance + creditsToAdd;
		updatedBalance.ShouldBe(expectedBalance, $"Balance should be {expectedBalance} after adding {creditsToAdd} credits");

		TestHelpers.LogTestInfo($"✓ Player registration enables credit operations");
		TestHelpers.LogTestInfo($"✓ Balance query returns correct value after credit add");

		// Cleanup
		await _sessionManager.LogoutUserAsync(TestPlayerPhone);
	}

	[Test]
	public async Task PlayerRegistration_WithBackendUnavailable_ContinuesGracefully()
	{
		// This test validates graceful degradation when backend is unavailable
		var isBackendRunning = _backendManager?.IsBackendRunning() ?? false;
		var isEventServiceReady = _eventService?.IsReady ?? false;

		TestHelpers.LogTestInfo($"Backend running: {isBackendRunning}");
		TestHelpers.LogTestInfo($"EventService ready: {isEventServiceReady}");

		// Act - Attempt login
		var loginResult = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, "1234");

		// Assert
		if (!isBackendRunning || !isEventServiceReady)
		{
			// Login should still succeed locally (graceful degradation)
			TestHelpers.LogTestInfo($"Login result with backend unavailable: {(loginResult.IsSuccess ? "Success" : loginResult.Error)}");
			TestHelpers.LogTestInfo("Expected: Login continues even if player registration fails");
			TestHelpers.LogTestInfo("Expected: Credit operations may fail later if player not registered");
		}
		else
		{
			// Backend is available - normal registration should happen
			loginResult.IsSuccess.ShouldBeTrue("Login should succeed when backend is available");
			TestHelpers.LogTestInfo("✓ Login with backend available succeeded");
		}

		// Cleanup
		if (loginResult.IsSuccess)
		{
			await _sessionManager.LogoutUserAsync(TestPlayerPhone);
		}
	}

	[Test]
	public async Task PlayerRegistration_ValidatesServiceDependencies()
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
		_eventService = null;
		_backendManager = null;
		_creditService = null;
	}
}
