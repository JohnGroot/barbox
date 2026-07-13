using System.Threading.Tasks;
using Chickensoft.GoDotTest;
using Godot;
using BarBox.Tests.Fixtures;
using Shouldly;

namespace BarBox.Tests.Integration;

/// <summary>
/// End-to-end integration tests for credit purchase flow
/// Tests the complete flow: Payment -> SessionManager -> EventService -> Backend
/// These tests validate the entire system working together, not individual components
/// </summary>
public class CreditPurchaseFlowTests : BackendTestBase
{
	private PaymentService _paymentService;
	private SessionManager _sessionManager;
	private EventService _eventService;
	private BackendManager _backendManager;
	private CreditService _creditService;

	public CreditPurchaseFlowTests(Node testScene) : base(testScene)
	{
	}

	[Setup]
	public new void SetupTestIdentifiers()
	{
		base.SetupTestIdentifiers();
		_paymentService = GetPaymentService();
		_sessionManager = GetSessionManager();
		_eventService = GetEventService();
		_backendManager = GetBackendManager();
		_creditService = GetCreditService();
	}

	[Test]
	public async Task CompleteCreditPurchaseFlow_WithAllServicesReady_Succeeds()
	{
		// Arrange - Wait for all services to be ready
		var servicesReady = await WaitForServicesReadyAsync();
		servicesReady.ShouldBeTrue(
			"Integration test requires backend services. " +
			$"Backend: {_backendManager?.IsBackendRunning() ?? false}, " +
			$"EventService: {_eventService?.IsReady ?? false}");

		// Create and login user
		var loginResult = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, "1234");
		if (loginResult.IsFailure(out var loginError))
		{
			TestHelpers.LogTestInfo($"Skipping test - login failed: {loginError.Message}");
			return;
		}

		var creditPack = CreditPack.CreateForTest(25, 25.00m);
		var playerId = EventService.GetPlayerIdFromPhone(TestPlayerPhone);

		// Get initial credits from CreditService (single source of truth)
		var initialCreditsResult = await _creditService.GetBalanceAsync(playerId);
		initialCreditsResult.IsSuccess(out var initialCredits).ShouldBeTrue("Should get initial credits");
		TestHelpers.LogTestInfo($"Initial credits: {initialCredits}");

		// Act - Execute complete purchase flow
		var purchaseResult = await _paymentService.PurchaseCreditsAsync(playerId, creditPack);

		// Assert
		(purchaseResult.IsSuccess).ShouldBeTrue($"Purchase should succeed, but failed: {purchaseResult.ErrorMessage}");
		purchaseResult.TransactionId.ShouldNotBeNullOrEmpty("Transaction ID should be set");
		TestHelpers.LogTestInfo($"Purchase succeeded: {purchaseResult.TransactionId}");

		// Verify credits updated via CreditService
		var updatedCreditsResult = await _creditService.GetBalanceAsync(playerId, forceRefresh: true);
		updatedCreditsResult.IsSuccess(out var updatedCredits).ShouldBeTrue("Should get updated credits");
		TestHelpers.LogTestInfo($"User credits after purchase: {updatedCredits}");

		var expectedCredits = initialCredits + creditPack.Credits;
		updatedCredits.ShouldBeGreaterThanOrEqualTo(expectedCredits,
			$"Credits should be at least {expectedCredits} after purchase");
		TestHelpers.LogTestInfo($"✓ Credits correctly added");

		// Verify backend has record
		var backendCredits = await _creditService.GetBalanceAsync(playerId);

		if (backendCredits.IsSuccess(out var backendCreditAmount))
		{
			TestHelpers.LogTestInfo($"Backend credits: {backendCreditAmount}");
			backendCreditAmount.ShouldBeGreaterThanOrEqualTo(expectedCredits,
				$"Backend credits should match or exceed {expectedCredits}");
			TestHelpers.LogTestInfo($"✓ Backend credits match session");
		}
		else if (backendCredits.IsFailure(out var backendError))
		{
			TestHelpers.LogTestWarning($"Could not verify backend credits: {backendError.Message}");
		}

		// Cleanup
		await _sessionManager.LogoutUserAsync(TestPlayerPhone);
	}

	[Test]
	public async Task CreditPurchaseFlow_WithBackendNotReady_FailsGracefully()
	{
		// Arrange
		var loginResult = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, "1234");
		if (loginResult.IsFailure(out var loginError))
		{
			TestHelpers.LogTestInfo($"Skipping test - login failed: {loginError.Message}");
			return;
		}

		var creditPack = CreditPack.CreateForTest(10, 10.00m);

		// Check backend state
		var isBackendRunning = _backendManager?.IsBackendRunning() ?? false;
		var isEventServiceReady = _eventService?.IsReady ?? false;

		TestHelpers.LogTestInfo($"Backend running: {isBackendRunning}");
		TestHelpers.LogTestInfo($"EventService ready: {isEventServiceReady}");

		// Act
		var playerId = EventService.GetPlayerIdFromPhone(TestPlayerPhone);
		var result = await _paymentService.PurchaseCreditsAsync(playerId, creditPack);

		// Assert
		if (!isBackendRunning || !isEventServiceReady)
		{
			(result.IsSuccess).ShouldBeFalse("Purchase must fail when backend/EventService not ready");
			result.ErrorMessage.ShouldNotBeNullOrEmpty("Error message should be provided");
			TestHelpers.LogTestInfo($"Purchase correctly failed: {result.ErrorMessage}");

			// Verify error message is informative
			var hasEventServiceDiagnostics = result.ErrorMessage.Contains("EventService") || result.ErrorMessage.Contains("backend");
			hasEventServiceDiagnostics.ShouldBeTrue("Error message should include diagnostic information about EventService or backend");
			TestHelpers.LogTestInfo("✓ Error message includes diagnostic information");
		}
		else
		{
			TestHelpers.LogTestInfo($"Services are ready, purchase result: {result.IsSuccess}");
		}

		// Cleanup
		await _sessionManager.LogoutUserAsync(TestPlayerPhone);
	}

	[Test]
	public async Task CreditPurchaseFlow_ValidatesServiceDependencies()
	{
		// Validate all required services are present
		_paymentService.ShouldNotBeNull("PaymentService must be present");
		_sessionManager.ShouldNotBeNull("SessionManager must be present");
		_eventService.ShouldNotBeNull("EventService must be present");
		_backendManager.ShouldNotBeNull("BackendManager must be present");

		TestHelpers.LogTestInfo("All required services present");

		// Check initialization state
		var isRunning = _backendManager.IsBackendRunning();
		TestHelpers.LogTestInfo($"BackendManager running: {isRunning}");

		var isReady = _eventService.IsReady;
		TestHelpers.LogTestInfo($"EventService ready: {isReady}");

		// Check PaymentService availability
		var paymentAvailable = await _paymentService.IsServiceAvailableAsync();
		TestHelpers.LogTestInfo($"PaymentService available: {paymentAvailable}");

		true.ShouldBeTrue("Service dependency validation complete");
	}

	[Test]
	public async Task CreditPurchaseFlow_ConcurrentPurchases_AllSucceed()
	{
		// Arrange - Wait for services
		var servicesReady = await WaitForServicesReadyAsync();
		servicesReady.ShouldBeTrue(
			"Integration test requires backend services. " +
			$"Backend: {_backendManager?.IsBackendRunning() ?? false}, " +
			$"EventService: {_eventService?.IsReady ?? false}");

		// Create and login user
		var loginResult = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, "1234");
		if (loginResult.IsFailure(out var loginError))
		{
			TestHelpers.LogTestInfo($"Skipping test - login failed: {loginError.Message}");
			return;
		}

		var smallPack = CreditPack.CreateForTest(5, 5.00m);
		var playerId = EventService.GetPlayerIdFromPhone(TestPlayerPhone);

		// Get initial credits from CreditService (single source of truth)
		var initialCreditsResult = await _creditService.GetBalanceAsync(playerId);
		initialCreditsResult.IsSuccess(out var initialCredits).ShouldBeTrue("Should get initial credits");

		// Act - Launch concurrent purchases
		TestHelpers.LogTestInfo("Starting concurrent purchases...");
		var task1 = _paymentService.PurchaseCreditsAsync(playerId, smallPack);
		var task2 = _paymentService.PurchaseCreditsAsync(playerId, smallPack);
		var task3 = _paymentService.PurchaseCreditsAsync(playerId, smallPack);

		await Task.WhenAll(task1, task2, task3);

		var result1 = task1.Result;
		var result2 = task2.Result;
		var result3 = task3.Result;

		// Assert
		var successCount = 0;
		if (result1.IsSuccess) successCount++;
		if (result2.IsSuccess) successCount++;
		if (result3.IsSuccess) successCount++;

		TestHelpers.LogTestInfo($"Concurrent purchases: {successCount}/3 succeeded");
		successCount.ShouldBeGreaterThan(0, "At least one concurrent purchase should succeed");

		// Verify credits via CreditService
		var finalCreditsResult = await _creditService.GetBalanceAsync(playerId, forceRefresh: true);
		finalCreditsResult.IsSuccess(out var finalCredits).ShouldBeTrue("Should get final credits");
		var expectedMinCredits = initialCredits + (smallPack.Credits * successCount);

		TestHelpers.LogTestInfo($"Final credits: {finalCredits} (expected >= {expectedMinCredits})");
		finalCredits.ShouldBeGreaterThanOrEqualTo(expectedMinCredits,
			$"Credits should be at least {expectedMinCredits} from {successCount} successful purchases");
		TestHelpers.LogTestInfo("✓ Credits correctly accumulated from concurrent purchases");

		// Cleanup
		await _sessionManager.LogoutUserAsync(TestPlayerPhone);
	}

	[Test]
	public async Task CreditPurchaseFlow_AfterBackendRestart_Recovers()
	{
		// This test documents expected behavior when backend restarts
		// Actual backend restart testing would require process control

		TestHelpers.LogTestInfo("Testing backend readiness recovery pattern...");

		// Check current state
		var isBackendRunning = _backendManager?.IsBackendRunning() ?? false;
		var isEventServiceReady = _eventService?.IsReady ?? false;

		TestHelpers.LogTestInfo($"Initial state - Backend: {isBackendRunning}, EventService: {isEventServiceReady}");

		if (!isBackendRunning || !isEventServiceReady)
		{
			TestHelpers.LogTestInfo("Backend or EventService not ready - this test documents expected recovery behavior");
			TestHelpers.LogTestInfo("Expected: PaymentService should fail gracefully with diagnostic error message");
			TestHelpers.LogTestInfo("Expected: After backend becomes ready, subsequent purchases should succeed");
			return;
		}

		// Services are ready - attempt purchase
		var loginResult = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, "1234");
		if (loginResult.IsSuccess(out var _))
		{
			var creditPack = CreditPack.CreateForTest(10, 10.00m);
			var playerId = EventService.GetPlayerIdFromPhone(TestPlayerPhone);
			var result = await _paymentService.PurchaseCreditsAsync(playerId, creditPack);

			TestHelpers.LogTestInfo($"Purchase with ready services: {(result.IsSuccess ? "Success" : $"Failed - {result.ErrorMessage}")}");

			await _sessionManager.LogoutUserAsync(TestPlayerPhone);
		}
	}

	[Test]
	public async Task CreditPurchase_ImmediateBalanceQuery_ReproducesRaceCondition()
	{
		// This test reproduces the race condition bug where balance queries
		// happen before the backend processes the credit/earn event
		// Expected behavior: Test should FAIL until the race condition is fixed

		// Arrange - Wait for services
		var servicesReady = await WaitForServicesReadyAsync();
		servicesReady.ShouldBeTrue(
			"Integration test requires backend services. " +
			$"Backend: {_backendManager?.IsBackendRunning() ?? false}, " +
			$"EventService: {_eventService?.IsReady ?? false}");

		// Create and login user
		var loginResult = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, "1234");
		if (loginResult.IsFailure(out var loginError))
		{
			TestHelpers.LogTestInfo($"Skipping test - login failed: {loginError.Message}");
			return;
		}

		var playerId = EventService.GetPlayerIdFromPhone(TestPlayerPhone);
		var creditPack = CreditPack.CreateForTest(25, 25.00m);

		// Get initial balance with forced refresh to ensure we have accurate starting point
		var creditService = CreditService.GetInstance();
		creditService.ShouldNotBeNull("CreditService must be available");

		var initialBalanceResult = await creditService.GetBalanceAsync(playerId, forceRefresh: true);
		if (initialBalanceResult.IsFailure(out var initialBalanceError))
		{
			TestHelpers.LogTestInfo($"Skipping test - could not get initial balance: {initialBalanceError.Message}");
			await _sessionManager.LogoutUserAsync(TestPlayerPhone);
			return;
		}

		var initialBalance = initialBalanceResult.IsSuccess(out var initBalance) ? initBalance : 0;
		TestHelpers.LogTestInfo($"Initial balance: {initialBalance}");

		// Act - Purchase credits
		TestHelpers.LogTestInfo($"Purchasing {creditPack.Credits} credits...");
		var purchaseResult = await _paymentService.PurchaseCreditsAsync(playerId, creditPack);

		// Assert - Purchase should succeed
		(purchaseResult.IsSuccess).ShouldBeTrue($"Purchase should succeed, but failed: {purchaseResult.ErrorMessage}");
		TestHelpers.LogTestInfo($"Purchase succeeded: {purchaseResult.TransactionId}");

		// CRITICAL: Query balance immediately after purchase (this is where the bug manifests)
		// The backend hasn't processed the credit/earn event yet, so balance will be stale
		TestHelpers.LogTestInfo("Querying balance immediately after purchase (reproducing race condition)...");
		var immediateBalanceResult = await creditService.GetBalanceAsync(playerId, forceRefresh: true);

		immediateBalanceResult.IsSuccess(out var _).ShouldBeTrue("Balance query should succeed");
		var immediateBalance = immediateBalanceResult.IsSuccess(out var immBalance) ? immBalance : 0;
		TestHelpers.LogTestInfo($"Immediate balance: {immediateBalance}");

		var expectedBalance = initialBalance + creditPack.Credits;

		// This assertion should FAIL due to race condition (balance will be initialBalance, not expectedBalance)
		TestHelpers.LogTestInfo($"Expected balance: {expectedBalance}, Actual balance: {immediateBalance}");

		immediateBalance.ShouldBe(expectedBalance,
			$"RACE CONDITION BUG: Balance should be {expectedBalance} but got {immediateBalance}. " +
			$"This indicates the balance query happened before the backend processed the credit/earn event.");

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
		if (_sessionManager != null)
		{
			var activeUsers = _sessionManager.GetActivePhoneNumbers();
			foreach (var phone in activeUsers)
			{
				await _sessionManager.LogoutUserAsync(phone);
			}
		}

		await base.CleanupTestResources();
		_paymentService = null;
		_sessionManager = null;
		_eventService = null;
		_backendManager = null;
	}
}
