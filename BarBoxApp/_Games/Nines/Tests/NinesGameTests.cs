using System;
using System.Threading.Tasks;
using Chickensoft.GoDotTest;
using Godot;
using BarBox.Tests.Fixtures;
using BarBox.Games.Nines;
using Shouldly;

namespace BarBox.Games.Nines.Tests;

/// <summary>
/// Integration tests for the Nines backend flow: asserts jackpot-win events are
/// accepted on an active activity session, that NinesEventService records them, and
/// that entry-credit deduction rolls back cleanly.
/// Runs against the real seeded test backend (see MiningGameTests for the pattern).
/// </summary>
public class NinesGameTests : TestClass
{
	private const string GAME_TAG = "nines";
	private SessionEventService _eventService;
	private CreditService _creditService;
	private Guid _testBoxId;
	private Guid _testPlayerId;
	private string _testPlayerPhone;
	private Guid _testSessionId;

	public NinesGameTests(Node testScene) : base(testScene)
	{
	}

	[SetupAll]
	public async Task SetupNinesGameSession()
	{
		TestHelpers.LogTestInfo("Setting up Nines Game test session");

		var isHealthy = await TestHelpers.IsTestBackendHealthyAsync();
		if (!isHealthy)
		{
			TestHelpers.LogTestWarning("Test backend is not healthy - some tests may be skipped");
			return;
		}

		_eventService = TestScene.GetNode<SessionEventService>("/root/SessionEventService");
		_eventService.ShouldNotBeNull("SessionEventService autoload must be available");
		_creditService = TestScene.GetNode<CreditService>("/root/CreditService");

		// Use seeded test identifiers (API key is only valid for the seeded Box ID)
		_testBoxId = TestHelpers.SeededTestBoxId;
		var (playerId, phone, _, _) = TestHelpers.GetSeededTestPlayer(1);
		_testPlayerId = playerId;
		_testPlayerPhone = phone;

		// Fold Nines into the activity-session lifecycle it previously skipped
		var sessionResult = await _eventService.CreateActivitySessionAsync(
			_testBoxId,
			_testPlayerId,
			GAME_TAG);

		if (sessionResult.IsSuccess(out var sessionId))
		{
			_testSessionId = sessionId;
			_testSessionId.ShouldNotBe(Guid.Empty, "Session ID should be valid");
			TestHelpers.LogTestInfo($"Nines session created: {_testSessionId}");
		}
		else if (sessionResult.IsFailure(out var error))
		{
			TestHelpers.LogTestWarning($"Failed to create session: {error.Message}");
		}
	}

	[Test]
	public async Task JackpotWonEvent_WithActiveSession_IsAccepted()
	{
		if (_eventService == null || _testSessionId == Guid.Empty)
		{
			TestHelpers.LogTestInfo("Skipping - no active backend session");
			return;
		}

		// player_id is the winner's PHONE NUMBER (Nines convention), not a UUID
		var result = await _eventService.EmitEventAsync("nines/jackpot_won", new
		{
			venue_name = "test_venue",
			player_id = _testPlayerPhone,
			jackpot_amount = 5000,
			timestamp = DateTime.UtcNow.ToString("o")
		});

		result.IsSuccess(out var _).ShouldBeTrue("Jackpot-won event should be accepted on an active nines session");
	}

	[Test]
	public async Task NinesEventService_EmitJackpotWon_RecordsThroughSession()
	{
		if (_eventService == null || _testSessionId == Guid.Empty)
		{
			TestHelpers.LogTestInfo("Skipping - no active backend session");
			return;
		}

		// Exercise the production NinesEventService wrapper
		var service = new NinesEventService(_eventService);
		var result = await service.EmitJackpotWonAsync("test_venue", _testPlayerPhone, "Test Winner", 7500);

		result.IsSuccess(out var _).ShouldBeTrue("NinesEventService should record the jackpot win through the active session");
	}

	[Test]
	public async Task NinesEventService_EmitJackpotWon_MissingVenue_FailsValidation()
	{
		var service = new NinesEventService(_eventService);
		var result = await service.EmitJackpotWonAsync("", _testPlayerPhone, "Test Winner", 100);

		result.IsSuccess(out var _).ShouldBeFalse("Empty venue name should fail input validation before any backend call");
	}

	[Test]
	public async Task EntryCreditDeduction_ThenRollback_RestoresBalance()
	{
		if (_creditService == null || _testSessionId == Guid.Empty)
		{
			TestHelpers.LogTestInfo("Skipping - CreditService or session not available");
			return;
		}

		const int entryCost = 25;

		// Seed enough balance to cover the entry deduction
		var seedResult = await _creditService.AddAsync(_testPlayerId, entryCost, "Nines Test Seed");
		if (!seedResult.IsSuccess(out var seededBalance))
		{
			TestHelpers.LogTestWarning("Skipping - could not seed credits");
			return;
		}

		// Deduct entry fee (mirrors DeductCreditsWithRollbackAsync)
		var spendResult = await _creditService.SpendAsync(_testPlayerId, entryCost, "Nines Entry");
		spendResult.IsSuccess(out var afterSpend).ShouldBeTrue("Entry deduction should succeed with sufficient balance");
		afterSpend.ShouldBe(seededBalance - entryCost, "Balance should drop by the entry cost");

		// Roll back (mirrors RollbackCreditsAsync on failure)
		var refundResult = await _creditService.AddAsync(_testPlayerId, entryCost, "Nines Entry Rollback");
		refundResult.IsSuccess(out var afterRefund).ShouldBeTrue("Rollback should restore credits");
		afterRefund.ShouldBe(seededBalance, "Balance should be restored after rollback");
	}

	[CleanupAll]
	public async Task CleanupNinesGameSession()
	{
		if (_eventService != null && _testSessionId != Guid.Empty)
		{
			TestHelpers.LogTestInfo("Closing nines game session");
			var result = await _eventService.CloseActivitySessionAsync(_testSessionId);

			if (result.IsSuccess(out var _))
				TestHelpers.LogTestInfo("Session closed successfully");
			else if (result.IsFailure(out var error))
				TestHelpers.LogTestWarning($"Session close failed: {error.Message}");
		}
	}
}
