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

	public PaymentServiceFailureTests(Node testScene) : base(testScene)
	{
	}

	[Setup]
	public new void SetupFailureScenario()
	{
		base.SetupFailureScenario();
		_paymentService = GetPaymentService();
		_sessionManager = GetSessionManager();
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
		if (!loginResult.IsSuccess)
		{
			TestHelpers.LogTestInfo($"Skipping - login failed: {loginResult.Error}");
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
			// Simulate backend failure
			SimulateEventServiceNotReady();

			// Act
			var playerId = EventService.GetPlayerIdFromPhone(TestPlayerPhone);
			var result = await _paymentService.PurchaseCreditsAsync(playerId, creditPack);

			// Assert - Credits must NOT change despite payment approval
			var finalSession = _sessionManager.GetUserSession(TestPlayerPhone);
			finalSession.ShouldNotBeNull("User session should exist");
			var finalCredits = finalSession.Credits;

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
		if (!loginResult.IsSuccess)
		{
			TestHelpers.LogTestInfo($"Skipping - login failed: {loginResult.Error}");
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
		if (!loginResult.IsSuccess)
		{
			TestHelpers.LogTestInfo($"Skipping - login failed: {loginResult.Error}");
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
		if (!loginResult.IsSuccess)
		{
			TestHelpers.LogTestInfo($"Skipping - login failed: {loginResult.Error}");
			return;
		}

		var initialSession = _sessionManager.GetUserSession(TestPlayerPhone);
		var initialCredits = initialSession.Credits;
		var creditPack = new CreditPack(10, 10.00m);

		try
		{
			// Purchase 1: Fails (backend down)
			SimulateEventServiceNotReady();
			var playerId = EventService.GetPlayerIdFromPhone(TestPlayerPhone);
			var result1 = await _paymentService.PurchaseCreditsAsync(playerId, creditPack);

			var afterFailure = _sessionManager.GetUserSession(TestPlayerPhone);
			afterFailure.ShouldNotBeNull("Session should exist after failed purchase");
			TestHelpers.LogTestInfo($"After failed purchase: {afterFailure.Credits} credits");

			// Purchase 2: Succeeds (backend up)
			RestoreEventServiceReady();
			await TestHelpers.WaitForConditionAsync(() => true, 0.1f); // Brief delay for state propagation
			var result2 = await _paymentService.PurchaseCreditsAsync(playerId, creditPack);

			var afterSuccess = _sessionManager.GetUserSession(TestPlayerPhone);
			afterSuccess.ShouldNotBeNull("Session should exist after successful purchase");
			TestHelpers.LogTestInfo($"After successful purchase: {afterSuccess.Credits} credits");

			// Assert
			result1.IsSuccess.ShouldBeFalse("First purchase should fail");
			result2.IsSuccess.ShouldBeTrue("Second purchase should succeed");

			var expectedCredits = initialCredits + creditPack.Credits;
			afterSuccess.Credits.ShouldBe(expectedCredits,
				$"State inconsistent: expected {expectedCredits}, got {afterSuccess.Credits}");
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
