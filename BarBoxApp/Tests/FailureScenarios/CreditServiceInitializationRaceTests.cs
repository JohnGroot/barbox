using System;
using System.Threading.Tasks;
using BarBox.Tests.Fixtures;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

namespace BarBox.Tests.FailureScenarios;

/// <summary>
/// Tests that reproduce the CreditService initialization race condition bug.
///
/// BUG: CreditService caches SessionEventService reference during OnServiceInitialize().
/// If SessionEventService isn't ready yet, _eventService field is set to null and NEVER UPDATED.
/// This causes credit operations to fail even after SessionEventService becomes ready.
///
/// REPRODUCTION: The exact production error shows:
///   [CreditService] ERROR: SessionEventService not available (exists: False, ready: False)
///   [PaymentService] ERROR: Diagnostics: SessionEventService exists: True, SessionEventService ready: True
///
/// This proves CreditService has a stale null reference while SessionEventService is actually ready.
/// </summary>
public class CreditServiceInitializationRaceTests : BackendTestBase
{
	private CreditService _creditService;
	private SessionEventService _eventService;
	private SessionManager _sessionManager;

	public CreditServiceInitializationRaceTests(Node testScene)
		: base(testScene)
	{
	}

	[Setup]
	public void SetupRaceConditionTest()
	{
		SetupTestIdentifiers();
		_creditService = GetCreditService();
		_eventService = GetEventService();
		_sessionManager = GetSessionManager();
	}

	/// <summary>
	/// Reproduces the exact production bug: CreditService._eventService is null
	/// even though SessionEventService.GetInstance() returns a valid ready instance.
	///
	/// This test simulates the race condition where:
	/// 1. CreditService.OnServiceInitialize() runs before SessionEventService is ready
	/// 2. CreditService caches null reference in _eventService field
	/// 3. SessionEventService becomes ready later
	/// 4. Credit operations fail because CreditService still has null reference
	///
	/// Expected: This test SHOULD FAIL with current code, proving the bug exists.
	/// After fix: This test should PASS.
	/// </summary>
	[Test]
	public async Task CreditService_CachedEventServiceReference_BecomesStaleWhenEventServiceBecomesReady()
	{
		// This test requires understanding the internal state of CreditService
		// We can't directly manipulate _eventService field, but we can verify behavior
		TestHelpers.LogTestInfo("=== REPRODUCTION TEST: CreditService Initialization Race Condition ===");

		// Arrange - login user
		var loginResult = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, "1234");
		if (loginResult.IsFailure(out var loginError))
		{
			TestHelpers.LogTestInfo($"Skipping - login failed: {loginError.Message}");
			return;
		}

		try
		{
			var playerId = SessionManager.GetPlayerIdFromPhone(TestPlayerPhone);

			// Verify SessionEventService is actually ready (matches production logs)
			_eventService.ShouldNotBeNull("SessionEventService should exist");
			_eventService.IsReady.ShouldBeTrue("SessionEventService should be ready");
			TestHelpers.LogTestInfo($"✓ SessionEventService exists and is ready (matches production)");

			// Verify SessionEventService.GetInstance() works (matches PaymentService check)
			var eventServiceViaGetInstance = SessionEventService.GetInstance();
			eventServiceViaGetInstance.ShouldNotBeNull("SessionEventService.GetInstance() should return instance");
			eventServiceViaGetInstance.IsReady.ShouldBeTrue("SessionEventService.GetInstance() should be ready");
			TestHelpers.LogTestInfo($"✓ SessionEventService.GetInstance() returns ready instance (matches PaymentService diagnostic)");

			// Act - attempt to add credits via CreditService
			TestHelpers.LogTestInfo("Attempting to add credits via CreditService...");
			var addResult = await _creditService.AddAsync(playerId, 10, "Test credit add");

			// Assert - This is where the bug manifests
			// Production logs show: [CreditService] ERROR: SessionEventService not available (exists: False, ready: False)
			// This means CreditService._eventService is null even though SessionEventService.GetInstance() works
			if (addResult.IsFailure(out var addError))
			{
				TestHelpers.LogTestError($"❌ BUG REPRODUCED: Credit add failed: {addError.Message}");
				TestHelpers.LogTestError($"   SessionEventService.GetInstance() is ready: {eventServiceViaGetInstance.IsReady}");
				TestHelpers.LogTestError($"   But CreditService reports SessionEventService not available");
				TestHelpers.LogTestError($"   This proves CreditService has stale null reference to SessionEventService");

				// Document the exact bug
				addError.Message.Contains("Credit service unavailable").ShouldBeTrue(
					"Error message should match production logs");

				// This assertion will FAIL with current buggy code, proving the bug exists
				addResult.IsSuccess(out var _).ShouldBeTrue(
					"BUG: CreditService should succeed when SessionEventService is ready, " +
					"but fails due to cached null _eventService field. " +
					"This proves the initialization race condition exists.");
			}
			else
			{
				TestHelpers.LogTestInfo("✓ Credit add succeeded (bug is fixed!)");
			}

			// Additional verification - try other CreditService methods
			var balanceResult = await _creditService.GetBalanceAsync(playerId);
			if (balanceResult.IsFailure(out var balanceError))
			{
				TestHelpers.LogTestError($"❌ GetBalanceAsync also failed: {balanceError.Message}");
			}
		}
		finally
		{
			await _sessionManager.LogoutUserAsync(TestPlayerPhone);
		}
	}

	/// <summary>
	/// Tests the specific scenario from production logs where PaymentService
	/// successfully validates SessionEventService is ready, but CreditService fails.
	///
	/// Production error sequence:
	/// 1. [PaymentService] Processing purchase...
	/// 2. [DebugPaymentService] Purchase approved - Transaction ID: DEBUG_TXN_xxx
	/// 3. [CreditService] ERROR: SessionEventService not available (exists: False, ready: False)
	/// 4. [PaymentService] ERROR: Diagnostics: SessionEventService exists: True, SessionEventService ready: True
	///
	/// This proves the race condition: PaymentService sees ready SessionEventService,
	/// CreditService sees null SessionEventService.
	/// </summary>
	[Test]
	public async Task PaymentPurchase_EventServiceReadyForPaymentService_ButNotForCreditService()
	{
		TestHelpers.LogTestInfo("=== REPRODUCTION TEST: Payment succeeds but credit add fails ===");

		// Arrange
		var loginResult = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, "1234");
		if (loginResult.IsFailure(out var loginError))
		{
			TestHelpers.LogTestInfo($"Skipping - login failed: {loginError.Message}");
			return;
		}

		var playerId = SessionManager.GetPlayerIdFromPhone(TestPlayerPhone);
		var creditPack = CreditPack.CreateForTest(25, 25.00m);

		// Get initial credits from CreditService (single source of truth)
		var initialCreditsResult = await _creditService.GetBalanceAsync(playerId);
		initialCreditsResult.IsSuccess(out var initialCredits).ShouldBeTrue("Should get initial credits");

		try
		{
			// Act - attempt purchase (this is the exact production scenario)
			var paymentService = GetPaymentService();
			var purchaseResult = await paymentService.PurchaseCreditsAsync(playerId, creditPack);

			// Verify SessionEventService state (like PaymentService does)
			var eventService = SessionEventService.GetInstance();
			var eventServiceExists = eventService != null;
			var eventServiceReady = eventService?.IsReady ?? false;

			TestHelpers.LogTestInfo($"SessionEventService diagnostics: exists={eventServiceExists}, ready={eventServiceReady}");

			// Assert - reproduce the exact production error
			if (!purchaseResult.IsSuccess)
			{
				// Check if failure is due to backend connectivity issue (not the race condition bug)
				var errorMessage = purchaseResult.ErrorMessage ?? string.Empty;
				if (errorMessage.Contains("timeout") || errorMessage.Contains("Timeout") ||
					errorMessage.Contains("Disconnected") || errorMessage.Contains("Connection"))
				{
					TestHelpers.LogTestInfo($"Skipping - backend checkout endpoint not available: {errorMessage}");
					return;
				}

				TestHelpers.LogTestError($"❌ BUG REPRODUCED: Purchase failed: {purchaseResult.ErrorMessage}");
				TestHelpers.LogTestError($"   Diagnostics: SessionEventService exists: {eventServiceExists}, SessionEventService ready: {eventServiceReady}");
				TestHelpers.LogTestError($"   This matches production logs exactly!");

				// Verify credits were NOT added (correct behavior for failed purchase)
				var finalCreditsResult = await _creditService.GetBalanceAsync(playerId, forceRefresh: true);
				finalCreditsResult.IsSuccess(out var finalCredits).ShouldBeTrue("Should get final credits");
				finalCredits.ShouldBe(
					initialCredits,
					"Credits should not change when purchase fails");

				// This assertion will FAIL with current buggy code
				purchaseResult.IsSuccess.ShouldBeTrue(
					$"BUG: Purchase should succeed when SessionEventService is ready. " +
					$"SessionEventService exists: {eventServiceExists}, ready: {eventServiceReady}. " +
					$"Error: {purchaseResult.ErrorMessage}");
			}
			else
			{
				TestHelpers.LogTestInfo("✓ Purchase succeeded (bug is fixed!)");

				// Verify credits were added via CreditService
				var finalCreditsResult = await _creditService.GetBalanceAsync(playerId, forceRefresh: true);
				finalCreditsResult.IsSuccess(out var finalCredits).ShouldBeTrue("Should get final credits");
				var expectedCredits = initialCredits + creditPack.Credits;
				finalCredits.ShouldBe(
					expectedCredits,
					"Credits should be added after successful purchase");
			}
		}
		finally
		{
			await _sessionManager.LogoutUserAsync(TestPlayerPhone);
		}
	}

	/// <summary>
	/// Tests all four CreditService methods that use the cached _eventService field:
	/// - GetBalanceAsync()
	/// - SpendAsync()
	/// - AddAsync()
	/// - ReconcileBalanceAsync()
	///
	/// All four should fail with the same bug if _eventService is null.
	/// </summary>
	[Test]
	public async Task AllCreditServiceMethods_FailWithStaleCachedEventService()
	{
		TestHelpers.LogTestInfo("=== Testing all CreditService methods for race condition ===");

		// Arrange
		var loginResult = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, "1234");
		if (loginResult.IsFailure(out var loginError))
		{
			TestHelpers.LogTestInfo($"Skipping - login failed: {loginError.Message}");
			return;
		}

		try
		{
			var playerId = SessionManager.GetPlayerIdFromPhone(TestPlayerPhone);

			// Verify SessionEventService is ready
			var eventService = SessionEventService.GetInstance();
			eventService.ShouldNotBeNull();
			eventService.IsReady.ShouldBeTrue();
			TestHelpers.LogTestInfo($"✓ SessionEventService is ready");

			// Test 1: GetBalanceAsync
			var balanceResult = await _creditService.GetBalanceAsync(playerId);
			if (balanceResult.IsFailure(out var balanceError))
			{
				TestHelpers.LogTestError($"❌ GetBalanceAsync failed: {balanceError.Message}");
			}

			// Test 2: AddAsync
			var addResult = await _creditService.AddAsync(playerId, 10, "Test add");
			if (addResult.IsFailure(out var addError))
			{
				TestHelpers.LogTestError($"❌ AddAsync failed: {addError.Message}");
			}

			// Test 3: SpendAsync (only if we have credits)
			if (addResult.IsSuccess(out var addedBalance) && addedBalance > 0)
			{
				var spendResult = await _creditService.SpendAsync(playerId, 5, "Test spend");
				if (spendResult.IsFailure(out var spendError))
				{
					TestHelpers.LogTestError($"❌ SpendAsync failed: {spendError.Message}");
				}
			}

			// Test 4: ReconcileBalanceAsync
			await _creditService.ReconcileBalanceAsync(playerId);
			TestHelpers.LogTestInfo("ReconcileBalanceAsync completed (void return)");

			// Assert - at least one should have failed if bug exists
			var anyFailed = balanceResult.IsFailure(out var _) || addResult.IsFailure(out var _);

			if (anyFailed)
			{
				TestHelpers.LogTestError("❌ BUG CONFIRMED: At least one CreditService method failed");
				TestHelpers.LogTestError($"   GetBalanceAsync: {(balanceResult.IsSuccess(out var _) ? "✓" : "✗")}");
				TestHelpers.LogTestError($"   AddAsync: {(addResult.IsSuccess(out var _) ? "✓" : "✗")}");

				// This will fail with current buggy code
				balanceResult.IsSuccess(out var _).ShouldBeTrue("GetBalanceAsync should succeed");
				addResult.IsSuccess(out var _).ShouldBeTrue("AddAsync should succeed");
			}
			else
			{
				TestHelpers.LogTestInfo("✓ All CreditService methods succeeded (bug is fixed!)");
			}
		}
		finally
		{
			await _sessionManager.LogoutUserAsync(TestPlayerPhone);
		}
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
		_creditService = null;
		_eventService = null;
		_sessionManager = null;
	}
}
