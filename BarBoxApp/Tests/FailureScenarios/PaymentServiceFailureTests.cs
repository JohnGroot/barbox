using System.Threading.Tasks;
using Chickensoft.GoDotTest;
using Godot;
using BarBox.Tests.Fixtures;
using Shouldly;

namespace BarBox.Tests.FailureScenarios;

/// <summary>
/// Comprehensive failure scenario tests for PaymentService.
/// Tests the exact production bug: backend fails to start, EventService not ready,
/// payment succeeds but credits aren't added.
/// Requires DEBUG build to use test hooks.
/// </summary>
public class PaymentServiceFailureTests : FailureScenarioTestBase
{
	private PaymentService _paymentService;
	private SessionManager _sessionManager;
	private CreditService _creditService;

	public PaymentServiceFailureTests(Node testScene) : base(testScene)
	{
	}

	[Setup]
	public new void SetupFailureScenario()
	{
		base.SetupFailureScenario();
		_paymentService = GetPaymentService();
		_sessionManager = GetSessionManager();
		_creditService = GetCreditService();
	}

	[Test]
	public async Task PurchaseCredits_BackendNotReady_MustFailGracefully()
	{
		if (!CanSimulateFailures())
		{
			TestHelpers.LogTestInfo("Skipping - requires DEBUG build");
			return;
		}

		// Arrange - create user session
		var loginResult = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, "1234");
		if (loginResult.IsFailure(out var loginError))
		{
			TestHelpers.LogTestInfo($"Skipping - login failed: {loginError.Message}");
			return;
		}

		var creditPack = new CreditPack(25, 25.00m);

		try
		{
			// Simulate backend failure (EventService not ready)
			SimulateEventServiceNotReady();

			// Verify simulation worked
			IsEventServiceNotReady().ShouldBeTrue("Test infrastructure error: EventService should be simulated as not ready");

			// Act - attempt purchase (simulates exact production scenario)
			var playerId = EventService.GetPlayerIdFromPhone(TestPlayerPhone);
			var result = await _paymentService.PurchaseCreditsAsync(playerId, creditPack);

			// Assert - MUST fail
			result.IsSuccess.ShouldBeFalse("CRITICAL REGRESSION: Purchase must fail without backend!");
			result.ErrorMessage.ShouldNotBeNullOrEmpty("Error message should be provided");
			TestHelpers.LogTestInfo($"✓ Purchase correctly failed: {result.ErrorMessage}");

			// Verify diagnostic info in error
			result.ErrorMessage.Contains("EventService").ShouldBeTrue(
				"Error message should include diagnostic information about EventService");
		}
		finally
		{
			RestoreEventServiceReady();
			await _sessionManager.LogoutUserAsync(TestPlayerPhone);
		}
	}

	[Test]
	public async Task PurchaseCredits_BackendNotReady_CreditsNotAdded()
	{
		if (!CanSimulateFailures())
		{
			TestHelpers.LogTestInfo("Skipping - requires DEBUG build");
			return;
		}

		// Arrange
		var loginResult = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, "1234");
		if (loginResult.IsFailure(out var loginError))
		{
			TestHelpers.LogTestInfo($"Skipping - login failed: {loginError.Message}");
			return;
		}

		var playerId = EventService.GetPlayerIdFromPhone(TestPlayerPhone);
		var creditPack = new CreditPack(25, 25.00m);

		// Get initial credits from CreditService (single source of truth)
		var initialCreditsResult = await _creditService.GetBalanceAsync(playerId);
		initialCreditsResult.IsSuccess(out var initialCredits).ShouldBeTrue("Should get initial credits");

		try
		{
			// Simulate backend failure
			SimulateEventServiceNotReady();

			// Act
			var result = await _paymentService.PurchaseCreditsAsync(playerId, creditPack);

			// Assert - Credits must NOT change despite payment approval
			RestoreEventServiceReady(); // Need to restore to check credits
			var finalCreditsResult = await _creditService.GetBalanceAsync(playerId, forceRefresh: true);
			finalCreditsResult.IsSuccess(out var finalCredits).ShouldBeTrue("Should get final credits");

			finalCredits.ShouldBe(initialCredits,
				$"REGRESSION: Credits should not change when purchase fails (was {initialCredits}, now {finalCredits})");
			TestHelpers.LogTestInfo($"✓ Credits unchanged: {finalCredits} (correct behavior)");

			result.IsSuccess.ShouldBeFalse("Purchase should fail when EventService not ready");
		}
		finally
		{
			RestoreEventServiceReady();
			await _sessionManager.LogoutUserAsync(TestPlayerPhone);
		}
	}

	[Test]
	public async Task PurchaseCredits_BackendFailsThenRecovers_SubsequentPurchaseSucceeds()
	{
		if (!CanSimulateFailures())
		{
			TestHelpers.LogTestInfo("Skipping - requires DEBUG build");
			return;
		}

		// Arrange
		var loginResult = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, "1234");
		if (loginResult.IsFailure(out var loginError))
		{
			TestHelpers.LogTestInfo($"Skipping - login failed: {loginError.Message}");
			return;
		}

		var creditPack = new CreditPack(10, 10.00m);

		try
		{
			// Phase 1: Backend failure
			SimulateEventServiceNotReady();

			var playerId = EventService.GetPlayerIdFromPhone(TestPlayerPhone);
		var failureResult = await _paymentService.PurchaseCreditsAsync(playerId, creditPack);
			failureResult.IsSuccess.ShouldBeFalse("Purchase must fail during backend failure");
			TestHelpers.LogTestInfo($"Phase 1: Purchase correctly failed during outage");

			// Phase 2: Backend recovery
			RestoreEventServiceReady();
			await TestHelpers.WaitForConditionAsync(() => true, 0.1f); // Brief delay for state propagation

			var recoveryResult = await _paymentService.PurchaseCreditsAsync(playerId, creditPack);
			recoveryResult.IsSuccess.ShouldBeTrue($"Purchase should succeed after recovery, but failed: {recoveryResult.ErrorMessage}");
			TestHelpers.LogTestInfo("✓ Phase 2: Purchase succeeded after backend recovery");
		}
		finally
		{
			RestoreEventServiceReady();
			await _sessionManager.LogoutUserAsync(TestPlayerPhone);
		}
	}

	[Test]
	public async Task PurchaseCredits_BackendNotReady_ErrorMessageHasDiagnostics()
	{
		if (!CanSimulateFailures())
		{
			TestHelpers.LogTestInfo("Skipping - requires DEBUG build");
			return;
		}

		// Arrange
		var loginResult = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, "1234");
		if (loginResult.IsFailure(out var loginError))
		{
			TestHelpers.LogTestInfo($"Skipping - login failed: {loginError.Message}");
			return;
		}

		try
		{
			SimulateEventServiceNotReady();

			// Act
			var playerId = EventService.GetPlayerIdFromPhone(TestPlayerPhone);
			var result = await _paymentService.PurchaseCreditsAsync(playerId, new CreditPack(5, 5.00m));

			// Assert - Error message must contain diagnostic info
			result.IsSuccess.ShouldBeFalse("Purchase should fail when EventService not ready");

			var error = result.ErrorMessage;
			error.ShouldNotBeNullOrEmpty("Error message should be provided");
			TestHelpers.LogTestInfo($"Error message: {error}");

			// Check for diagnostic keywords
			error.Contains("EventService").ShouldBeTrue("Error should mention EventService");

			var hasReadinessMention = error.Contains("ready", System.StringComparison.OrdinalIgnoreCase) ||
			                         error.Contains("initialized", System.StringComparison.OrdinalIgnoreCase);
			hasReadinessMention.ShouldBeTrue("Error should mention readiness/initialization state");
			TestHelpers.LogTestInfo("✓ Error has service and readiness info");

			var hasStateInfo = error.Contains("True") || error.Contains("False");
			if (hasStateInfo)
			{
				TestHelpers.LogTestInfo("✓ Error includes state diagnostics");
			}
		}
		finally
		{
			RestoreEventServiceReady();
			await _sessionManager.LogoutUserAsync(TestPlayerPhone);
		}
	}

	[Test]
	public async Task MultiplePurchases_FirstFailsSecondSucceeds_VerifyStateConsistency()
	{
		if (!CanSimulateFailures())
		{
			TestHelpers.LogTestInfo("Skipping - requires DEBUG build");
			return;
		}

		// Arrange
		var loginResult = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, "1234");
		if (loginResult.IsFailure(out var loginError))
		{
			TestHelpers.LogTestInfo($"Skipping - login failed: {loginError.Message}");
			return;
		}

		var playerId = EventService.GetPlayerIdFromPhone(TestPlayerPhone);
		var creditPack = new CreditPack(10, 10.00m);

		// Get initial credits from CreditService (single source of truth)
		var initialCreditsResult = await _creditService.GetBalanceAsync(playerId);
		initialCreditsResult.IsSuccess(out var initialCredits).ShouldBeTrue("Should get initial credits");

		try
		{
			// Purchase 1: Fails (backend down)
			SimulateEventServiceNotReady();
			var result1 = await _paymentService.PurchaseCreditsAsync(playerId, creditPack);

			// Note: Can't check credits while EventService is down
			TestHelpers.LogTestInfo($"After failed purchase attempt (EventService down)");

			// Purchase 2: Succeeds (backend up)
			RestoreEventServiceReady();
			await TestHelpers.WaitForConditionAsync(() => true, 0.1f); // Brief delay for state propagation
			var result2 = await _paymentService.PurchaseCreditsAsync(playerId, creditPack);

			// Verify credits via CreditService
			var afterSuccessResult = await _creditService.GetBalanceAsync(playerId, forceRefresh: true);
			afterSuccessResult.IsSuccess(out var afterSuccessCredits).ShouldBeTrue("Should get credits after success");
			TestHelpers.LogTestInfo($"After successful purchase: {afterSuccessCredits} credits");

			// Assert
			result1.IsSuccess.ShouldBeFalse("First purchase should fail");
			result2.IsSuccess.ShouldBeTrue("Second purchase should succeed");

			var expectedCredits = initialCredits + creditPack.Credits;
			afterSuccessCredits.ShouldBe(expectedCredits,
				$"State inconsistent: expected {expectedCredits}, got {afterSuccessCredits}");
			TestHelpers.LogTestInfo("✓ State consistent: only successful purchase added credits");
		}
		finally
		{
			RestoreEventServiceReady();
			await _sessionManager.LogoutUserAsync(TestPlayerPhone);
		}
	}

	[Cleanup]
	public override async Task CleanupTestResources()
	{
		// Clean up sessions
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
	}
}
