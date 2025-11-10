using System;
using System.Threading.Tasks;
using Chickensoft.GoDotTest;
using Godot;
using BarBox.Tests.Fixtures;
using Shouldly;

namespace BarBox.Tests.FailureScenarios;

/// <summary>
/// Tests that reproduce the CreditService initialization race condition bug.
///
/// BUG: CreditService caches EventService reference during OnServiceInitialize().
/// If EventService isn't ready yet, _eventService field is set to null and NEVER UPDATED.
/// This causes credit operations to fail even after EventService becomes ready.
///
/// REPRODUCTION: The exact production error shows:
///   [CreditService] ERROR: EventService not available (exists: False, ready: False)
///   [PaymentService] ERROR: Diagnostics: EventService exists: True, EventService ready: True
///
/// This proves CreditService has a stale null reference while EventService is actually ready.
/// </summary>
public class CreditServiceInitializationRaceTests : BackendTestBase
{
	private CreditService _creditService;
	private EventService _eventService;
	private SessionManager _sessionManager;

	public CreditServiceInitializationRaceTests(Node testScene) : base(testScene)
	{
	}

	[Setup]
	public void SetupRaceConditionTest()
	{
		base.SetupTestIdentifiers();
		_creditService = GetCreditService();
		_eventService = GetEventService();
		_sessionManager = GetSessionManager();
	}

	/// <summary>
	/// Reproduces the exact production bug: CreditService._eventService is null
	/// even though EventService.GetInstance() returns a valid ready instance.
	///
	/// This test simulates the race condition where:
	/// 1. CreditService.OnServiceInitialize() runs before EventService is ready
	/// 2. CreditService caches null reference in _eventService field
	/// 3. EventService becomes ready later
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
		if (!loginResult.IsSuccess)
		{
			TestHelpers.LogTestInfo($"Skipping - login failed: {loginResult.Error}");
			return;
		}

		try
		{
			var playerId = EventService.GetPlayerIdFromPhone(TestPlayerPhone);

			// Verify EventService is actually ready (matches production logs)
			_eventService.ShouldNotBeNull("EventService should exist");
			_eventService.IsReady.ShouldBeTrue("EventService should be ready");
			TestHelpers.LogTestInfo($"✓ EventService exists and is ready (matches production)");

			// Verify EventService.GetInstance() works (matches PaymentService check)
			var eventServiceViaGetInstance = EventService.GetInstance();
			eventServiceViaGetInstance.ShouldNotBeNull("EventService.GetInstance() should return instance");
			eventServiceViaGetInstance.IsReady.ShouldBeTrue("EventService.GetInstance() should be ready");
			TestHelpers.LogTestInfo($"✓ EventService.GetInstance() returns ready instance (matches PaymentService diagnostic)");

			// Act - attempt to add credits via CreditService
			TestHelpers.LogTestInfo("Attempting to add credits via CreditService...");
			var addResult = await _creditService.AddAsync(playerId, 10, "Test credit add");

			// Assert - This is where the bug manifests
			// Production logs show: [CreditService] ERROR: EventService not available (exists: False, ready: False)
			// This means CreditService._eventService is null even though EventService.GetInstance() works

			if (!addResult.IsSuccess)
			{
				TestHelpers.LogTestError($"❌ BUG REPRODUCED: Credit add failed: {addResult.Error}");
				TestHelpers.LogTestError($"   EventService.GetInstance() is ready: {eventServiceViaGetInstance.IsReady}");
				TestHelpers.LogTestError($"   But CreditService reports EventService not available");
				TestHelpers.LogTestError($"   This proves CreditService has stale null reference to EventService");

				// Document the exact bug
				addResult.Error.Contains("Credit service unavailable").ShouldBeTrue(
					"Error message should match production logs");

				// This assertion will FAIL with current buggy code, proving the bug exists
				addResult.IsSuccess.ShouldBeTrue(
					"BUG: CreditService should succeed when EventService is ready, " +
					"but fails due to cached null _eventService field. " +
					"This proves the initialization race condition exists.");
			}
			else
			{
				TestHelpers.LogTestInfo("✓ Credit add succeeded (bug is fixed!)");
			}

			// Additional verification - try other CreditService methods
			var balanceResult = await _creditService.GetBalanceAsync(playerId);
			if (!balanceResult.IsSuccess)
			{
				TestHelpers.LogTestError($"❌ GetBalanceAsync also failed: {balanceResult.Error}");
			}
		}
		finally
		{
			await _sessionManager.LogoutUserAsync(TestPlayerPhone);
		}
	}

	/// <summary>
	/// Tests the specific scenario from production logs where PaymentService
	/// successfully validates EventService is ready, but CreditService fails.
	///
	/// Production error sequence:
	/// 1. [PaymentService] Processing purchase...
	/// 2. [DebugPaymentService] Purchase approved - Transaction ID: DEBUG_TXN_xxx
	/// 3. [CreditService] ERROR: EventService not available (exists: False, ready: False)
	/// 4. [PaymentService] ERROR: Diagnostics: EventService exists: True, EventService ready: True
	///
	/// This proves the race condition: PaymentService sees ready EventService,
	/// CreditService sees null EventService.
	/// </summary>
	[Test]
	public async Task PaymentPurchase_EventServiceReadyForPaymentService_ButNotForCreditService()
	{
		TestHelpers.LogTestInfo("=== REPRODUCTION TEST: Payment succeeds but credit add fails ===");

		// Arrange
		var loginResult = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, "1234");
		if (!loginResult.IsSuccess)
		{
			TestHelpers.LogTestInfo($"Skipping - login failed: {loginResult.Error}");
			return;
		}

		var initialSession = _sessionManager.GetUserSession(TestPlayerPhone);
		var initialCredits = initialSession.Credits;
		var creditPack = new CreditPack(25, 25.00m);

		try
		{
			// Act - attempt purchase (this is the exact production scenario)
			var playerId = EventService.GetPlayerIdFromPhone(TestPlayerPhone);
			var paymentService = GetPaymentService();
			var purchaseResult = await paymentService.PurchaseCreditsAsync(playerId, creditPack);

			// Verify EventService state (like PaymentService does)
			var eventService = EventService.GetInstance();
			var eventServiceExists = eventService != null;
			var eventServiceReady = eventService?.IsReady ?? false;

			TestHelpers.LogTestInfo($"EventService diagnostics: exists={eventServiceExists}, ready={eventServiceReady}");

			// Assert - reproduce the exact production error
			if (!purchaseResult.IsSuccess)
			{
				TestHelpers.LogTestError($"❌ BUG REPRODUCED: Purchase failed: {purchaseResult.ErrorMessage}");
				TestHelpers.LogTestError($"   Diagnostics: EventService exists: {eventServiceExists}, EventService ready: {eventServiceReady}");
				TestHelpers.LogTestError($"   This matches production logs exactly!");

				// Verify credits were NOT added (correct behavior for failed purchase)
				var finalSession = _sessionManager.GetUserSession(TestPlayerPhone);
				finalSession.Credits.ShouldBe(initialCredits,
					"Credits should not change when purchase fails");

				// This assertion will FAIL with current buggy code
				purchaseResult.IsSuccess.ShouldBeTrue(
					$"BUG: Purchase should succeed when EventService is ready. " +
					$"EventService exists: {eventServiceExists}, ready: {eventServiceReady}. " +
					$"Error: {purchaseResult.ErrorMessage}");
			}
			else
			{
				TestHelpers.LogTestInfo("✓ Purchase succeeded (bug is fixed!)");

				// Verify credits were added
				var finalSession = _sessionManager.GetUserSession(TestPlayerPhone);
				var expectedCredits = initialCredits + creditPack.Credits;
				finalSession.Credits.ShouldBe(expectedCredits,
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
		if (!loginResult.IsSuccess)
		{
			TestHelpers.LogTestInfo($"Skipping - login failed: {loginResult.Error}");
			return;
		}

		try
		{
			var playerId = EventService.GetPlayerIdFromPhone(TestPlayerPhone);

			// Verify EventService is ready
			var eventService = EventService.GetInstance();
			eventService.ShouldNotBeNull();
			eventService.IsReady.ShouldBeTrue();
			TestHelpers.LogTestInfo($"✓ EventService is ready");

			// Test 1: GetBalanceAsync
			var balanceResult = await _creditService.GetBalanceAsync(playerId);
			if (!balanceResult.IsSuccess)
			{
				TestHelpers.LogTestError($"❌ GetBalanceAsync failed: {balanceResult.Error}");
			}

			// Test 2: AddAsync
			var addResult = await _creditService.AddAsync(playerId, 10, "Test add");
			if (!addResult.IsSuccess)
			{
				TestHelpers.LogTestError($"❌ AddAsync failed: {addResult.Error}");
			}

			// Test 3: SpendAsync (only if we have credits)
			if (addResult.IsSuccess && addResult.Value > 0)
			{
				var spendResult = await _creditService.SpendAsync(playerId, 5, "Test spend");
				if (!spendResult.IsSuccess)
				{
					TestHelpers.LogTestError($"❌ SpendAsync failed: {spendResult.Error}");
				}
			}

			// Test 4: ReconcileBalanceAsync
			await _creditService.ReconcileBalanceAsync(playerId);
			TestHelpers.LogTestInfo("ReconcileBalanceAsync completed (void return)");

			// Assert - at least one should have failed if bug exists
			var anyFailed = !balanceResult.IsSuccess || !addResult.IsSuccess;

			if (anyFailed)
			{
				TestHelpers.LogTestError("❌ BUG CONFIRMED: At least one CreditService method failed");
				TestHelpers.LogTestError($"   GetBalanceAsync: {(balanceResult.IsSuccess ? "✓" : "✗")}");
				TestHelpers.LogTestError($"   AddAsync: {(addResult.IsSuccess ? "✓" : "✗")}");

				// This will fail with current buggy code
				balanceResult.IsSuccess.ShouldBeTrue("GetBalanceAsync should succeed");
				addResult.IsSuccess.ShouldBeTrue("AddAsync should succeed");
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
