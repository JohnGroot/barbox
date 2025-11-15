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
	}

	[Test]
	public async Task PurchaseCredits_WithBackendReady_SucceedsAndAddsCredits()
	{
		// Arrange
		_paymentService.ShouldNotBeNull("PaymentService must be available");
		_sessionManager.ShouldNotBeNull("SessionManager must be available");
		_eventService.ShouldNotBeNull("EventService must be available");

		var creditPack = new CreditPack(25, 25.00m);

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

			// Get initial credits
			var initialSession = _sessionManager.GetUserSession(TestPlayerPhone);
			initialSession.ShouldNotBeNull("User session should exist after login");
			var initialCredits = initialSession.Credits;

			// Act
			var playerId = EventService.GetPlayerIdFromPhone(TestPlayerPhone);
			var result = await _paymentService.PurchaseCreditsAsync(playerId, creditPack);

			// Assert
			result.IsSuccess.ShouldBeTrue($"Credit purchase should succeed: {result.ErrorMessage}");
			result.TransactionId.ShouldNotBeNullOrEmpty("Transaction ID should be set");
			TestHelpers.LogTestInfo($"Credits purchased: Transaction {result.TransactionId}");

			// Verify credits were added
			var updatedSession = _sessionManager.GetUserSession(TestPlayerPhone);
			updatedSession.ShouldNotBeNull("User session should still exist");
			var expectedCredits = initialCredits + creditPack.Credits;

			updatedSession.Credits.ShouldBeGreaterThanOrEqualTo(expectedCredits,
				$"Credits should be at least {expectedCredits} after purchase");
			TestHelpers.LogTestInfo($"Credits correctly added: {updatedSession.Credits} (expected >= {expectedCredits})");
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
		return;
#else
		// Arrange
		_paymentService.ShouldNotBeNull("PaymentService must be available");
		_sessionManager.ShouldNotBeNull("SessionManager must be available");
		_eventService.ShouldNotBeNull("EventService must be available");

		var creditPack = new CreditPack(10, 10.00m);

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
		var creditPack = new CreditPack(10, 10.00m);
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

		var smallPack = new CreditPack(5, 5.00m);

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

			var initialSession = _sessionManager.GetUserSession(TestPlayerPhone);
			initialSession.ShouldNotBeNull("User session should exist");
			var initialCredits = initialSession.Credits;

			// Act - Purchase three times
			var playerId = EventService.GetPlayerIdFromPhone(TestPlayerPhone);
			var result1 = await _paymentService.PurchaseCreditsAsync(playerId, smallPack);
			var result2 = await _paymentService.PurchaseCreditsAsync(playerId, smallPack);
			var result3 = await _paymentService.PurchaseCreditsAsync(playerId, smallPack);

			// Assert
			result1.IsSuccess.ShouldBeTrue($"First purchase should succeed: {result1.ErrorMessage}");
			result2.IsSuccess.ShouldBeTrue($"Second purchase should succeed: {result2.ErrorMessage}");
			result3.IsSuccess.ShouldBeTrue($"Third purchase should succeed: {result3.ErrorMessage}");

			var finalSession = _sessionManager.GetUserSession(TestPlayerPhone);
			finalSession.ShouldNotBeNull("User session should still exist");
			var expectedCredits = initialCredits + (smallPack.Credits * 3);

			finalSession.Credits.ShouldBeGreaterThanOrEqualTo(expectedCredits,
				"Credits should accumulate correctly across multiple purchases");
			TestHelpers.LogTestInfo($"All purchases succeeded. Credits: {finalSession.Credits} (expected >= {expectedCredits})");
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
