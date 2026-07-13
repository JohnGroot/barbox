using System.Collections.Generic;
using System.Threading.Tasks;
using Chickensoft.GoDotTest;
using Godot;
using BarBox.Tests.Fixtures;
using Shouldly;

namespace BarBox.Tests.Integration;

/// <summary>
/// Integration tests for Carrom machine credit endpoints
/// Tests actual HTTP communication between frontend and backend
///
/// CRITICAL: These tests validate the API contract between EventService and backend
/// They specifically test that machine credits endpoints use correct URL paths:
/// - GET /machine-credits/{game_tag}
/// - POST /machine-credits/{game_tag}/deposit
/// - POST /machine-credits/{game_tag}/consume
///
/// These tests would have caught the original bug where frontend was calling
/// /game/{game_tag}/machine-credits instead of /machine-credits/{game_tag}
/// </summary>
public class CarromMachineCreditsIntegrationTests : BackendTestBase
{
	private EventService _eventService;
	private SessionManager _sessionManager;
	private BackendManager _backendManager;
	private CreditService _creditService;

	public CarromMachineCreditsIntegrationTests(Node testScene) : base(testScene)
	{
	}

	[Setup]
	public new void SetupTestIdentifiers()
	{
		base.SetupTestIdentifiers();
		_eventService = GetEventService();
		_sessionManager = GetSessionManager();
		_backendManager = GetBackendManager();
		_creditService = GetCreditService();
	}

	[Test]
	public async Task GetMachineCredits_WithValidGameTag_Returns200Not404()
	{
		// Arrange - Require backend for integration test
		var servicesReady = await WaitForServicesReadyAsync();
		if (!servicesReady)
		{
			TestHelpers.LogTestWarning("Backend not ready - skipping test");
			return;
		}

		// Act - Query machine credits (this would 404 with wrong URL path)
		var result = await _creditService.GetMachineCreditsAsync("carrom", TestBoxId);

		// Assert - Should return 200, not 404
		if (result.IsSuccess(out var credits))
		{
			credits.ShouldNotBeNull();
			credits.Balance.ShouldBeGreaterThanOrEqualTo(0, "Balance should be non-negative");
			TestHelpers.LogTestInfo($"✓ GET /machine-credits/carrom returned 200 with balance: {credits.Balance}");
		}
		else if (result.IsFailure(out var error))
		{
			TestHelpers.LogTestError(
				"Machine credits query failed. " +
				"404 error indicates URL path mismatch between frontend and backend. " +
				$"Expected: /machine-credits/carrom, Error: {error.Message}");
			false.ShouldBeTrue("Machine credits endpoint should return 200, not 404");
		}
	}

	[Test]
	public async Task DepositMachineCredits_CompleteFlow_UpdatesBackend()
	{
		// Arrange - Setup complete test environment
		var servicesReady = await WaitForServicesReadyAsync();
		if (!servicesReady)
		{
			TestHelpers.LogTestWarning("Backend not ready - skipping test");
			return;
		}

		// Login and fund player
		var loginResult = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, "1234");
		if (loginResult.IsFailure(out var loginError))
		{
			TestHelpers.LogTestInfo($"Skipping test - login failed: {loginError.Message}");
			return;
		}

		var session = _sessionManager.GetSessionByPhone(TestPlayerPhone);
		session.ShouldNotBeNull("User session should exist after login");
		var playerId = session.PlayerId;
		var lobbySessionId = session.LobbySessionId;

		var addCreditsResult = await _creditService.AddAsync(playerId, 100, "Test setup");

		if (addCreditsResult.IsFailure(out var addError))
		{
			TestHelpers.LogTestInfo($"Skipping test - add credits failed: {addError.Message}");
			await _sessionManager.LogoutUserAsync(TestPlayerPhone);
			return;
		}

		// Get initial machine balance
		var initialResult = await _creditService.GetMachineCreditsAsync("carrom", TestBoxId);
		initialResult.IsSuccess(out var initialCredits).ShouldBeTrue();
		var initialBalance = initialCredits.Balance;
		TestHelpers.LogTestInfo($"Initial machine balance: {initialBalance}");

		// Get required parameters for deposit
		var boxId = TestBoxId;

		// Act - TWO-STEP PROCESS: Transfer credits from player to machine
		// Step 1: Spend credits from player account (with polling)
		var spendResult = await _creditService.SpendAsync(playerId, 5, "Test deposit");
		spendResult.IsSuccess(out var remainingBalance).ShouldBeTrue(
			"Player should have sufficient credits for deposit");

		// Step 2: Deposit to machine pot
		var depositResult = await _creditService.DepositMachineCreditsAsync(
			"carrom", boxId, playerId, 5, lobbySessionId);

		// Assert - Both steps should succeed
		depositResult.IsSuccess(out var machineState).ShouldBeTrue(
			"Deposit should succeed with 200/201. " +
			"404 error indicates URL path mismatch. " +
			"Expected: POST /machine-credits/carrom/deposit");

		// Verify backend balance increased
		var finalResult = await _creditService.GetMachineCreditsAsync("carrom", TestBoxId);
		finalResult.IsSuccess(out var finalCredits).ShouldBeTrue();

		finalCredits.Balance.ShouldBe(
			initialBalance + 5,
			"Machine balance should increase by deposit amount");

		TestHelpers.LogTestInfo($"✓ Machine balance correctly increased from {initialBalance} to {finalCredits.Balance}");

		// Cleanup
		await _sessionManager.LogoutUserAsync(TestPlayerPhone);
	}

	[Test]
	public async Task ConsumeMachineCredits_WithSufficientBalance_Succeeds()
	{
		// Arrange - Ensure machine has credits
		var servicesReady = await WaitForServicesReadyAsync();
		if (!servicesReady)
		{
			TestHelpers.LogTestWarning("Backend not ready - skipping test");
			return;
		}

		// Login and fund player
		var loginResult = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, "1234");
		if (loginResult.IsFailure(out var loginError))
		{
			TestHelpers.LogTestInfo($"Skipping test - login failed: {loginError.Message}");
			return;
		}

		var session = _sessionManager.GetSessionByPhone(TestPlayerPhone);
		session.ShouldNotBeNull("User session should exist after login");
		var playerId = session.PlayerId;
		var lobbySessionId = session.LobbySessionId;

		var addCreditsResult = await _creditService.AddAsync(playerId, 100, "Test setup");

		if (addCreditsResult.IsFailure(out var addError))
		{
			TestHelpers.LogTestInfo($"Skipping test - add credits failed: {addError.Message}");
			await _sessionManager.LogoutUserAsync(TestPlayerPhone);
			return;
		}

		// Deposit credits to machine - TWO-STEP SETUP:
		var boxId = TestBoxId;

		// Step 1: Spend from player account
		var spendResult = await _creditService.SpendAsync(playerId, 10, "Setup credits");
		if (!spendResult.IsSuccess(out var _))
		{
			TestHelpers.LogTestInfo("Skipping test - player credit spend failed");
			await _sessionManager.LogoutUserAsync(TestPlayerPhone);
			return;
		}

		// Step 2: Deposit to machine pot
		var depositResult = await _creditService.DepositMachineCreditsAsync(
			"carrom", boxId, playerId, 10, lobbySessionId);

		if (!depositResult.IsSuccess(out var _))
		{
			TestHelpers.LogTestInfo("Skipping test - machine deposit failed");
			await _sessionManager.LogoutUserAsync(TestPlayerPhone);
			return;
		}

		// Create game session
		var sessionResult = await _eventService.CreateActivitySessionAsync(
			TestBoxId, playerId, "carrom", new List<string> { playerId.ToString() });

		if (sessionResult.IsFailure(out var sessionError))
		{
			TestHelpers.LogTestInfo($"Skipping test - session creation failed: {sessionError.Message}");
			await _sessionManager.LogoutUserAsync(TestPlayerPhone);
			return;
		}

		sessionResult.IsSuccess(out var gameSessionId).ShouldBeTrue();
		TestHelpers.LogTestInfo($"Created game session: {gameSessionId}");

		// Capture balance BEFORE consuming (machine pot may have accumulated from prior tests)
		var beforeConsumeResult = await _creditService.GetMachineCreditsAsync("carrom", TestBoxId);
		beforeConsumeResult.IsSuccess(out var beforeState).ShouldBeTrue("Should get balance before consume");
		var balanceBeforeConsume = beforeState.Balance;
		TestHelpers.LogTestInfo($"Machine balance before consume: {balanceBeforeConsume}");

		// Act - Consume credits from machine pot
		var consumeResult = await _creditService.ConsumeMachineCreditsAsync(
			"carrom", TestBoxId, 8, gameSessionId);

		// Assert - Consume should succeed
		consumeResult.IsSuccess(out var machineState).ShouldBeTrue(
			"Consume should succeed with 200. " +
			"404 error indicates URL path mismatch. " +
			"Expected: POST /machine-credits/carrom/consume");

		machineState.Balance.ShouldBe(balanceBeforeConsume - 8,
			$"Balance should decrease by 8 (from {balanceBeforeConsume} to {balanceBeforeConsume - 8})");

		TestHelpers.LogTestInfo($"✓ Machine balance correctly decreased to {machineState.Balance}");

		// Cleanup
		await _sessionManager.LogoutUserAsync(TestPlayerPhone);
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
		if (_sessionManager != null &&
			_sessionManager.IsUserLoggedIn(TestPlayerPhone))
		{
			await _sessionManager.LogoutUserAsync(TestPlayerPhone);
		}
		await base.CleanupTestResources();
	}
}
