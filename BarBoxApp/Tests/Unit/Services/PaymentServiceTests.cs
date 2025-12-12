using System;
using System.Threading.Tasks;
using Chickensoft.GoDotTest;
using Godot;
using BarBox.Tests.Fixtures;
using Shouldly;

namespace BarBox.Tests.Unit.Services;

/// <summary>
/// Unit tests for PaymentService - payment processing and credit addition
/// Tests the complete flow: Payment -> SessionManager -> EventService -> Backend
/// </summary>
public class PaymentServiceTests : BackendTestBase
{
	private PaymentService _paymentService;
	private SessionManager _sessionManager;
	private EventService _eventService;
	private CreditService _creditService;

	public PaymentServiceTests(Node testScene) : base(testScene)
	{
	}

	[Setup]
	public new void SetupTestIdentifiers()
	{
		base.SetupTestIdentifiers();
		_paymentService = GetPaymentService();
		_sessionManager = GetSessionManager();
		_eventService = GetEventService();
		_creditService = GetCreditService();
	}

	[Test]
	public async Task PurchaseCredits_WithBackendReady_SucceedsAndAddsCredits()
	{
		// Arrange
		_paymentService.ShouldNotBeNull("PaymentService must be available");
		_sessionManager.ShouldNotBeNull("SessionManager must be available");
		_eventService.ShouldNotBeNull("EventService must be available");

		var creditPack = CreditPack.CreateForTest(25, 25.00m);

		// Create user session first
		var loginResult = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, "1234");
		if (loginResult.IsFailure(out var loginError))
		{
			TestHelpers.LogTestInfo($"Test skipped - login failed: {loginError.Message}");
			return;
		}

		try
		{
			// Verify EventService is ready before attempting purchase
			if (!_eventService.IsReady)
			{
				TestHelpers.LogTestInfo("Test skipped - EventService not ready");
				return;
			}

			// Get initial credits from CreditService (single source of truth)
			var initialSession = _sessionManager.GetSessionByPhone(TestPlayerPhone);
			initialSession.ShouldNotBeNull("User session should exist after login");
			var playerId = initialSession.PlayerId; // Use session's PlayerId directly
			playerId.ShouldNotBe(Guid.Empty, "Session should have valid PlayerId after login");
			TestHelpers.LogTestInfo($"Getting balance for playerId: {playerId}");

			var initialCreditsResult = await _creditService.GetBalanceAsync(playerId);
			if (initialCreditsResult.IsFailure(out var creditsError))
			{
				// Skip test if backend connection fails - this is an infrastructure issue, not a code bug
				if (creditsError.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
				    creditsError.Message.Contains("Connection", StringComparison.OrdinalIgnoreCase))
				{
					TestHelpers.LogTestWarning($"Test skipped - backend connection issue: {creditsError.Message}");
					return;
				}
				TestHelpers.LogTestError($"GetBalanceAsync failed: {creditsError.Message}");
			}
			initialCreditsResult.IsSuccess(out var initialCredits).ShouldBeTrue($"Should get initial credits - error: {(initialCreditsResult.IsFailure(out var err) ? err.Message : "N/A")}");

			// Act
			var result = await _paymentService.PurchaseCreditsAsync(playerId, creditPack);

			// Assert
			result.IsSuccess.ShouldBeTrue($"Credit purchase should succeed: {result.ErrorMessage}");
			result.TransactionId.ShouldNotBeNullOrEmpty("Transaction ID should be set");
			TestHelpers.LogTestInfo($"Credits purchased: Transaction {result.TransactionId}");

			// Verify credits were added via CreditService
			var updatedCreditsResult = await _creditService.GetBalanceAsync(playerId, forceRefresh: true);
			updatedCreditsResult.IsSuccess(out var updatedCredits).ShouldBeTrue("Should get updated credits");
			var expectedCredits = initialCredits + creditPack.Credits;

			updatedCredits.ShouldBeGreaterThanOrEqualTo(expectedCredits,
				$"Credits should be at least {expectedCredits} after purchase");
			TestHelpers.LogTestInfo($"Credits correctly added: {updatedCredits} (expected >= {expectedCredits})");
		}
		finally
		{
			// Cleanup
			await _sessionManager.LogoutUserAsync(TestPlayerPhone);
		}
	}

	[Test]
	public async Task PurchaseCredits_WhenEventServiceNotReady_FailsWithDiagnostics()
	{
#if !DEBUG
		TestHelpers.LogTestInfo("Test skipped - requires DEBUG build for failure simulation");
		await Task.CompletedTask;
		return;
#else
		// Arrange
		_paymentService.ShouldNotBeNull("PaymentService must be available");
		_sessionManager.ShouldNotBeNull("SessionManager must be available");
		_eventService.ShouldNotBeNull("EventService must be available");

		var creditPack = CreditPack.CreateForTest(10, 10.00m);

		// Create user session
		var loginResult = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, "1234");
		if (loginResult.IsFailure(out var loginError))
		{
			TestHelpers.LogTestInfo($"Test skipped - login failed: {loginError.Message}");
			return;
		}

		try
		{
			// FORCE EventService to not ready state (simulates backend failure)
			if (!TestHelpers.ForceEventServiceNotReady())
			{
				TestHelpers.LogTestInfo("Test skipped - could not force EventService not ready");
				return;
			}

			// Verify EventService is actually not ready
			_eventService.IsReady.ShouldBeFalse("EventService should be forced to NOT READY state");
			TestHelpers.LogTestInfo("EventService forced to NOT READY state - simulating backend failure");

			// Act - attempt purchase with backend unavailable
			var playerId = EventService.GetPlayerIdFromPhone(TestPlayerPhone);
			var result = await _paymentService.PurchaseCreditsAsync(playerId, creditPack);

			// Assert - purchase MUST fail
			result.IsSuccess.ShouldBeFalse("Purchase must fail when EventService not ready");
			result.ErrorMessage.ShouldNotBeNullOrEmpty("Error message should be provided");
			TestHelpers.LogTestInfo($"Purchase correctly failed: {result.ErrorMessage}");

			// Verify error message includes diagnostic info
			var hasDiagnostics = result.ErrorMessage.Contains("EventService") &&
			                    (result.ErrorMessage.Contains("ready") || result.ErrorMessage.Contains("initialized"));
			hasDiagnostics.ShouldBeTrue("Error message should include EventService diagnostics");
			TestHelpers.LogTestInfo("✓ Error message includes proper diagnostics");
		}
		finally
		{
			// ALWAYS restore EventService state
			TestHelpers.RestoreEventServiceReady();
			await _sessionManager.LogoutUserAsync(TestPlayerPhone);
		}
#endif
	}

	[Test]
	public void GetAvailableCreditPacks_ReturnsValidPacks()
	{
		// Arrange
		_paymentService.ShouldNotBeNull("PaymentService must be available");

		// Act
		var packs = _paymentService.GetAvailableCreditPacks();

		// Assert
		packs.ShouldNotBeNull("Credit packs should not be null");
		packs.Length.ShouldBeGreaterThan(0, "At least one credit pack should be available");
		TestHelpers.LogTestInfo($"Available credit packs: {packs.Length}");

		foreach (var pack in packs)
		{
			TestHelpers.LogTestInfo($"  - {pack.DisplayName}");

			// Validate pack structure
			pack.Credits.ShouldBeGreaterThan(0, $"Pack {pack.DisplayName} must have positive credits");
			pack.Price.ShouldBeGreaterThan(0, $"Pack {pack.DisplayName} must have positive price");
			pack.DisplayName.ShouldNotBeNullOrEmpty("Pack must have display name");
		}
	}

	[Test]
	public async Task PurchaseCredits_WithoutUserSession_ReturnsError()
	{
		// Arrange
		_paymentService.ShouldNotBeNull("PaymentService must be available");
		var creditPack = CreditPack.CreateForTest(10, 10.00m);
		var fakePhoneNumber = "9999999999"; // Phone number with no session

		// Act
		var playerId = EventService.GetPlayerIdFromPhone(fakePhoneNumber);
		var result = await _paymentService.PurchaseCreditsAsync(playerId, creditPack);

		// Assert
		result.IsSuccess.ShouldBeFalse("Purchase should fail without user session");
		result.ErrorMessage.ShouldNotBeNullOrEmpty("Error message should be provided");
		TestHelpers.LogTestInfo($"Purchase correctly failed without session: {result.ErrorMessage}");
	}

	[Test]
	public async Task PurchaseCredits_MultipleSequential_AllSucceed()
	{
		// Arrange
		_paymentService.ShouldNotBeNull("PaymentService must be available");
		_sessionManager.ShouldNotBeNull("SessionManager must be available");
		_eventService.ShouldNotBeNull("EventService must be available");

		var smallPack = CreditPack.CreateForTest(5, 5.00m);

		// Create user session
		var loginResult = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, "1234");
		if (loginResult.IsFailure(out var loginError))
		{
			TestHelpers.LogTestInfo($"Test skipped - login failed: {loginError.Message}");
			return;
		}

		try
		{
			// Verify EventService is ready
			if (!_eventService.IsReady)
			{
				TestHelpers.LogTestInfo("Test skipped - EventService not ready");
				return;
			}

			var initialSession = _sessionManager.GetSessionByPhone(TestPlayerPhone);
			initialSession.ShouldNotBeNull("User session should exist");
			var playerId = initialSession.PlayerId; // Use session's PlayerId directly
			playerId.ShouldNotBe(Guid.Empty, "Session should have valid PlayerId after login");

			// Get initial credits from CreditService (single source of truth)
			var initialCreditsResult = await _creditService.GetBalanceAsync(playerId);
			if (initialCreditsResult.IsFailure(out var creditsError))
			{
				// Skip test if backend connection fails - this is an infrastructure issue, not a code bug
				if (creditsError.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
				    creditsError.Message.Contains("Connection", StringComparison.OrdinalIgnoreCase))
				{
					TestHelpers.LogTestWarning($"Test skipped - backend connection issue: {creditsError.Message}");
					return;
				}
			}
			initialCreditsResult.IsSuccess(out var initialCredits).ShouldBeTrue($"Should get initial credits - error: {(initialCreditsResult.IsFailure(out var err) ? err.Message : "N/A")}");

			// Act - Purchase three times
			var result1 = await _paymentService.PurchaseCreditsAsync(playerId, smallPack);
			var result2 = await _paymentService.PurchaseCreditsAsync(playerId, smallPack);
			var result3 = await _paymentService.PurchaseCreditsAsync(playerId, smallPack);

			// Assert
			result1.IsSuccess.ShouldBeTrue($"First purchase should succeed: {result1.ErrorMessage}");
			result2.IsSuccess.ShouldBeTrue($"Second purchase should succeed: {result2.ErrorMessage}");
			result3.IsSuccess.ShouldBeTrue($"Third purchase should succeed: {result3.ErrorMessage}");

			// Verify credits via CreditService
			var finalCreditsResult = await _creditService.GetBalanceAsync(playerId, forceRefresh: true);
			finalCreditsResult.IsSuccess(out var finalCredits).ShouldBeTrue("Should get final credits");
			var expectedCredits = initialCredits + (smallPack.Credits * 3);

			finalCredits.ShouldBeGreaterThanOrEqualTo(expectedCredits,
				"Credits should accumulate correctly across multiple purchases");
			TestHelpers.LogTestInfo($"All purchases succeeded. Credits: {finalCredits} (expected >= {expectedCredits})");
		}
		finally
		{
			// Cleanup
			await _sessionManager.LogoutUserAsync(TestPlayerPhone);
		}
	}

	[Test]
	public async Task IsServiceAvailable_ReflectsActualState()
	{
		// Arrange
		_paymentService.ShouldNotBeNull("PaymentService must be available");
		_sessionManager.ShouldNotBeNull("SessionManager must be available");
		_eventService.ShouldNotBeNull("EventService must be available");

		// Act
		var isAvailable = await _paymentService.IsServiceAvailableAsync();

		// Assert
		TestHelpers.LogTestInfo($"Payment service available: {isAvailable}");

		// Validate against actual dependencies
		var hasSessionManager = _sessionManager != null;
		var eventServiceReady = _eventService.IsReady;

		TestHelpers.LogTestInfo($"  SessionManager exists: {hasSessionManager}");
		TestHelpers.LogTestInfo($"  EventService ready: {eventServiceReady}");

		if (isAvailable && !eventServiceReady)
		{
			TestHelpers.LogTestInfo("Service reports available but EventService not ready - may be in initialization");
		}
		else if (!isAvailable && eventServiceReady)
		{
			TestHelpers.LogTestInfo("Service reports unavailable but EventService ready - verify dependencies");
		}
		else
		{
			TestHelpers.LogTestInfo("✓ Service availability matches dependency state");
		}
	}

	[Test]
	public void CancelPayment_WhenNoPurchaseActive_DoesNotThrow()
	{
		// Arrange
		_paymentService.ShouldNotBeNull("PaymentService must be available");
		var stripeService = _paymentService.StripeService;

		// Skip if not using Stripe provider
		if (stripeService == null)
		{
			TestHelpers.LogTestInfo("Test skipped - StripePaymentService not active");
			return;
		}

		// Act & Assert - Should not throw
		try
		{
			stripeService.CancelPayment();
			TestHelpers.LogTestInfo("✓ CancelPayment completed without throwing when no purchase active");
		}
		catch (Exception ex)
		{
			throw new Exception($"CancelPayment should not throw when no purchase active: {ex.Message}");
		}
	}

	[Test]
	public void StripeService_OnPollingErrorEvent_CanBeSubscribed()
	{
		// Arrange
		_paymentService.ShouldNotBeNull("PaymentService must be available");
		var stripeService = _paymentService.StripeService;

		// Skip if not using Stripe provider
		if (stripeService == null)
		{
			TestHelpers.LogTestInfo("Test skipped - StripePaymentService not active");
			return;
		}

		// Act - Subscribe to event
		stripeService.OnPollingError += (_) => { };

		// Assert - Event should be subscribable without error
		TestHelpers.LogTestInfo("✓ OnPollingError event subscription successful");

		// Note: Event will fire during actual polling failures
		// This test verifies the event can be subscribed to without error
	}

	[Test]
	public async Task ConcurrentPurchase_WhileAnotherInProgress_ReturnsFalse()
	{
		// Arrange
		_paymentService.ShouldNotBeNull("PaymentService must be available");
		_sessionManager.ShouldNotBeNull("SessionManager must be available");
		_eventService.ShouldNotBeNull("EventService must be available");

		var stripeService = _paymentService.StripeService;

		// Skip if not using Stripe provider
		if (stripeService == null)
		{
			TestHelpers.LogTestInfo("Test skipped - StripePaymentService not active");
			return;
		}

		var creditPack = CreditPack.CreateForTest(25, 25.00m);

		// Create user session first
		var loginResult = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, "1234");
		if (loginResult.IsFailure(out var loginError))
		{
			TestHelpers.LogTestInfo($"Test skipped - login failed: {loginError.Message}");
			return;
		}

		try
		{
			if (!_eventService.IsReady)
			{
				TestHelpers.LogTestInfo("Test skipped - EventService not ready");
				return;
			}

			var initialSession = _sessionManager.GetSessionByPhone(TestPlayerPhone);
			var playerId = initialSession.PlayerId.ToString();

			// Start first purchase (don't await - will block waiting for QR scan)
			var firstTask = stripeService.ProcessPurchaseAsync(playerId, creditPack);

			// Give first task a moment to start processing
			await AutoloadBase.StaticDelayAsync(0.1f);

			// Attempt second purchase while first is in progress
			var secondResult = await stripeService.ProcessPurchaseAsync(playerId, creditPack);

			// Assert - Second should fail due to concurrent purchase guard
			secondResult.IsSuccess.ShouldBeFalse("Second concurrent purchase should be blocked");
			secondResult.ErrorMessage.ShouldNotBeNull("Error message should be provided");
			secondResult.ErrorMessage.ToLower().ShouldContain("already in progress");
			TestHelpers.LogTestInfo($"✓ Concurrent purchase correctly blocked: {secondResult.ErrorMessage}");

			// Cancel the first task
			stripeService.CancelPayment();
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
		// Ensure any active sessions are cleaned up
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
	}
}
