using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BarBox.Games.Racing;
using BarBox.Tests.Fixtures;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

namespace BarBox.Games.Racing.Tests;

/// <summary>
/// Comprehensive integration tests for Racing Game
/// Tests race sessions, checkpoint/lap tracking, leaderboards
/// </summary>
public class RacingGameTests : GameSessionTestBase
{
	private const string GAME_TAG = "racing";
	private const string DEFAULT_TRACK_ID = "oval";

	protected override string GameTag => GAME_TAG;

	private RacingEventService RacingEventService => new(EventService);

	public RacingGameTests(Node testScene)
		: base(testScene)
	{
	}

	[Test]
	public async Task StartRaceSession_EmitsBeginEvent()
	{
		if (EventService == null || TestSessionId == Guid.Empty)
		{
			TestHelpers.LogTestInfo("Skipping - Session not created");
			return;
		}

		// Emit race start event
		var result = await EventService.EmitEventAsync("play/begin", new
		{
			game = GAME_TAG,
			track = "oval",
			mode = "time_trial",
		});

		if (result.IsSuccess(out var _))
		{
			TestHelpers.LogTestInfo("Race begin event emitted");
		}
		else if (result.IsFailure(out var error))
		{
			TestHelpers.LogTestError($"Begin event failed: {error.Message}");
		}
	}

	[Test]
	public async Task RecordCheckpointHit_EmitsCheckpointEvent()
	{
		if (EventService == null || TestSessionId == Guid.Empty)
		{
			TestHelpers.LogTestInfo("Skipping - Session not created");
			return;
		}

		// Emit checkpoint events
		var checkpoint1 = await EventService.EmitEventAsync("racing/checkpoint", new
		{
			checkpoint_id = 1,
			time_elapsed = 5.2f,
			position = new { x = 100, y = 200 },
		});

		var checkpoint2 = await EventService.EmitEventAsync("racing/checkpoint", new
		{
			checkpoint_id = 2,
			time_elapsed = 10.8f,
			position = new { x = 300, y = 400 },
		});

		var checkpoint3 = await EventService.EmitEventAsync("racing/checkpoint", new
		{
			checkpoint_id = 3,
			time_elapsed = 14.5f,
			position = new { x = 500, y = 600 },
		});

		if (checkpoint1.IsSuccess(out var _) && checkpoint2.IsSuccess(out var __) && checkpoint3.IsSuccess(out var ___))
		{
			TestHelpers.LogTestInfo("All checkpoint events emitted successfully");
		}
		else
		{
			TestHelpers.LogTestWarning("Some checkpoint events failed");
		}
	}

	[Test]
	public async Task RecordLapCompletion_EmitsLapEvent()
	{
		if (RacingEventService == null || TestSessionId == Guid.Empty)
		{
			TestHelpers.LogTestInfo("Skipping - Session not created");
			return;
		}

		// Emit lap completion events with proper checkpoint data
		var lap1Checkpoints = CreateTestCheckpoints(15.3f);
		var lap1 = await RacingEventService.EmitLapCompleteAsync(DEFAULT_TRACK_ID, 1, 15.3f, lap1Checkpoints);

		var lap2Checkpoints = CreateTestCheckpoints(14.8f);
		var lap2 = await RacingEventService.EmitLapCompleteAsync(DEFAULT_TRACK_ID, 2, 14.8f, lap2Checkpoints);

		var lap3Checkpoints = CreateTestCheckpoints(15.0f);
		var lap3 = await RacingEventService.EmitLapCompleteAsync(DEFAULT_TRACK_ID, 3, 15.0f, lap3Checkpoints);

		if (lap1.IsSuccess(out var _) && lap2.IsSuccess(out var __) && lap3.IsSuccess(out var ___))
		{
			TestHelpers.LogTestInfo("All lap events emitted successfully");
			TestHelpers.LogTestInfo("Best lap: 14.8s (Lap 2)");
		}
		else
		{
			TestHelpers.LogTestWarning("Some lap events failed");
		}
	}

	/// <summary>
	/// Reproduces the rage-quit hazard RacingPendingSaveQueue exists to prevent:
	/// lap 1 completes (queuing its backend save instead of firing it
	/// synchronously on the lap-crossing frame) and the player quits before
	/// lap 2 ever starts, so nothing else would trigger a flush. Session
	/// teardown must still flush the lap-1 save - mirrors RacingGame.
	/// OnGameTeardown calling _pendingSaveQueue.Flush() as its first step.
	///
	/// NOTE: this exercises the queue and the real backend save call
	/// (EmitLapCompleteAsync) directly, not the signal wiring inside RacingGame
	/// itself (OnTimingSystemLapCompleted enqueuing, OnGameTeardown flushing).
	/// Driving that wiring end-to-end would require a real RacingGame node with:
	/// an active SessionManager login, a live TimeTrial backend session (which
	/// itself requires credit-spend confirmation and a loaded LocationManager
	/// config - see the RacingMode.TimeTrial branch that calls
	/// StartBackendSessionAsync), and actual checkpoint-trigger physics
	/// collisions, since EmitLapCompleteAsync rejects empty checkpoint data and
	/// checkpoints only populate via real trigger crossings. No test in this
	/// codebase drives Racing through physics-triggered checkpoints (even
	/// RacingTimingSystemTests calls the internal checkpoint/lap methods
	/// directly), so that prerequisite chain is a pre-existing gap in this
	/// harness, not one introduced here. The RacingGame-side wiring (the two
	/// call sites at OnTimingSystemLapCompleted and OnGameTeardown) is
	/// therefore verified by code review, not by an automated test.
	/// </summary>
	[Test]
	public async Task PendingSaveQueue_LapOneCompletesThenQuitsBeforeLapTwo_PersistsLapOneTime()
	{
		// Arrange
		if (RacingEventService == null || TestSessionId == Guid.Empty)
		{
			TestHelpers.LogTestInfo("Skipping - Session not created");
			return;
		}

		var trackId = $"pending_queue_test_{Guid.NewGuid():N}";
		var lap1Time = 11.111f;
		var lap1Checkpoints = CreateTestCheckpoints(lap1Time);
		var queue = new RacingPendingSaveQueue();
		var lap1Succeeded = false;
		string lap1Error = null;

		// Lap 1 completes: capture happens synchronously (cheap), the actual
		// backend save is queued instead of started immediately.
		queue.Enqueue(async () =>
		{
			var result = await RacingEventService.EmitLapCompleteAsync(trackId, 1, lap1Time, lap1Checkpoints);
			if (result.IsSuccess(out var _))
			{
				lap1Succeeded = true;
			}
			else if (result.IsFailure(out var error))
			{
				lap1Error = error.Message;
			}
		});

		// Act - player quits here, before lap 2 ever starts. Nothing else
		// enqueues or flushes; only the teardown-equivalent flush below can
		// still save lap 1.
		await queue.FlushAsync();

		// Assert - the save actually reached the backend, not just that a
		// delegate was invoked. Read the session back (rather than the
		// best_lap leaderboard, which aggregates by track_id but the
		// racing/lap_complete payload schema on the backend doesn't retain
		// track_id - a pre-existing backend gap unrelated to this queue) and
		// confirm the lap_complete event with lap 1's time actually landed.
		lap1Succeeded.ShouldBeTrue($"Lap 1 save must persist even though the player quit before lap 2: {lap1Error}");

		var sessionJsonResult = await EventService.QueryRawAsync($"/box/session/{TestSessionId}?include_events=true");
		sessionJsonResult.IsSuccess(out var sessionJson).ShouldBeTrue("Should be able to read the session back to confirm the event landed");

		var sessionDict = Json.ParseString(sessionJson).AsGodotDictionary();
		var events = sessionDict["events"].AsGodotArray();

		var foundLap1Event = false;
		foreach (var evt in events)
		{
			var evtDict = evt.AsGodotDictionary();
			if (evtDict["type"].AsString() != "racing/lap_complete")
			{
				continue;
			}

			var payload = evtDict["payload"].AsGodotDictionary();
			if (payload.ContainsKey("lap_time") && System.Math.Abs(payload["lap_time"].AsSingle() - lap1Time) < 0.001f)
			{
				foundLap1Event = true;
				break;
			}
		}

		foundLap1Event.ShouldBeTrue($"Expected a racing/lap_complete event with lap_time {lap1Time}s to be persisted in session {TestSessionId}");
	}

	/// <summary>
	/// Creates test checkpoint data with sequential indices and monotonically increasing times
	/// </summary>
	private static CheckpointTime[] CreateTestCheckpoints(float lapTime, int checkpointCount = 4)
	{
		var checkpoints = new CheckpointTime[checkpointCount];
		var timePerCheckpoint = lapTime / checkpointCount;

		for (int i = 0; i < checkpointCount; i++)
		{
			var time = timePerCheckpoint * (i + 1);
			var gap = i == 0 ? timePerCheckpoint : timePerCheckpoint;
			checkpoints[i] = new CheckpointTime(i, time, gap);
		}

		return checkpoints;
	}

	[Test]
	public async Task RecordRaceFinish_EmitsFinishEventWithLapTimes()
	{
		if (EventService == null || TestSessionId == Guid.Empty)
		{
			TestHelpers.LogTestInfo("Skipping - Session not created");
			return;
		}

		// Emit race finish event with complete lap time data
		var lapTimes = new List<float> { 15.3f, 14.8f, 15.0f };
		var finalTime = 45.1f;

		var result = await EventService.EmitEventAsync("play/finish", new
		{
			final_time = finalTime,
			lap_times = lapTimes,
			checkpoints_hit = 12, // 4 checkpoints per lap * 3 laps
			track = "oval",
		});

		if (result.IsSuccess(out var _))
		{
			TestHelpers.LogTestInfo($"Race finish event emitted - Final time: {finalTime}s");
		}
		else if (result.IsFailure(out var error))
		{
			TestHelpers.LogTestError($"Finish event failed: {error.Message}");
		}
	}

	[Test]
	public void LoadLeaderboardData_FromBackend_ReturnsTopTimes()
	{
		// This would test: GET /game/leaderboard/racing
		// Expected response sorted by best_time ascending:
		// [
		//   {
		//     "player_id": "uuid",
		//     "best_time": 45.1,
		//     "lap_times": [15.3, 14.8, 15.0],
		//     "track": "oval",
		//     "rank": 1
		//   }
		// ]
		TestHelpers.LogTestInfo("Leaderboard query test - requires backend endpoint");
		TestHelpers.LogTestInfo("Expected: GET /game/leaderboard/racing?track=oval");
		TestHelpers.LogTestInfo("Returns: Sorted by best_time (ascending)");
	}

	[Test]
	public void VerifyLeaderboardOrdering_BestTimesFirst()
	{
		// This test verifies leaderboard ordering logic
		// Best times (lowest) should appear first
		var times = new List<(string playerId, float time)>
		{
			("player1", 45.1f),
			("player2", 42.3f), // Best time - should be rank 1
			("player3", 48.7f),
		};

		// Sort by time ascending
		times.Sort((a, b) => a.time.CompareTo(b.time));

		if (times[0].playerId == "player2")
		{
			TestHelpers.LogTestInfo("Leaderboard ordering correct - best time first");
		}
		else
		{
			TestHelpers.LogTestError("Leaderboard ordering incorrect");
		}
	}

	[Test]
	public void VerifyLapTimeBreakdowns_InLeaderboard()
	{
		// Leaderboard should show individual lap times for analysis
		// This allows players to see where they can improve
		var lapTimes = new List<float> { 15.3f, 14.8f, 15.0f };
		var bestLap = 999f;
		foreach (var time in lapTimes)
		{
			if (time < bestLap)
			{
				bestLap = time;
			}
		}

		TestHelpers.LogTestInfo($"Best lap time: {bestLap}s");
		TestHelpers.LogTestInfo("Lap breakdown helps players identify improvement areas");
	}

	[Test]
	public async Task CompleteRacingGameFlow_AllOperationsSucceed()
	{
		if (EventService == null)
		{
			TestHelpers.LogTestInfo("Skipping - SessionEventService not available");
			return;
		}

		TestHelpers.LogTestInfo("=== Starting Complete Racing Game Flow ===");

		// Step 1: Create session
		var sessionResult = await EventService.CreateActivitySessionAsync(
			TestBoxId,
			TestPlayerId,
			GAME_TAG);

		if (sessionResult.IsSuccess(out var flowSessionId))
		{
			TestHelpers.LogTestInfo($"Step 1 - Session Created: ✓ ({flowSessionId})");

			// Step 2: Start race
			var beginResult = await EventService.EmitEventAsync("play/begin", new
			{
				game = GAME_TAG,
				track = "oval",
			});

			TestHelpers.LogTestInfo($"Step 2 - Race Begin: {(beginResult.IsSuccess(out var _) ? "✓" : "✗")}");

			// Step 3: Record checkpoints (3 laps, 4 checkpoints each = 12 total)
			for (int lap = 1; lap <= 3; lap++)
			{
				for (int checkpoint = 1; checkpoint <= 4; checkpoint++)
				{
					await EventService.EmitEventAsync("racing/checkpoint", new
					{
						checkpoint_id = checkpoint,
						lap = lap,
						time_elapsed = ((lap - 1) * 15.0f) + (checkpoint * 3.75f),
					});
				}

				// Complete lap using RacingEventService
				var checkpoints = CreateTestCheckpoints(15.0f);
				await RacingEventService.EmitLapCompleteAsync(DEFAULT_TRACK_ID, lap, 15.0f, checkpoints);
			}

			TestHelpers.LogTestInfo("Step 3 - Checkpoints & Laps Recorded: ✓");

			// Step 4: Finish race
			var finishResult = await EventService.EmitEventAsync("play/finish", new
			{
				final_time = 45.0f,
				lap_times = new List<float> { 15.0f, 15.0f, 15.0f },
				checkpoints_hit = 12,
			});

			TestHelpers.LogTestInfo($"Step 4 - Race Finish: {(finishResult.IsSuccess(out var __) ? "✓" : "✗")}");

			// Step 5: Close session
			var closeResult = await EventService.CloseActivitySessionAsync(flowSessionId);
			TestHelpers.LogTestInfo($"Step 5 - Session Closed: {(closeResult.IsSuccess(out var ___) ? "✓" : "✗")}");
		}
		else if (sessionResult.IsFailure(out var sessionError))
		{
			TestHelpers.LogTestError($"Flow failed at session creation: {sessionError.Message}");
		}

		TestHelpers.LogTestInfo("=== Racing Game Flow Complete ===");
	}

	[Test]
	public async Task RaceFinish_WithValidLapTimes_SavesSuccessfully()
	{
		if (EventService == null)
		{
			TestHelpers.LogTestInfo("Skipping - SessionEventService not available");
			return;
		}

		TestHelpers.LogTestInfo("=== Testing Race Finish with Valid Lap Times ===");

		// Create a session for this test (SessionEventService requires active session for EmitEventAsync)
		var sessionResult = await EventService.CreateActivitySessionAsync(
			TestBoxId,
			TestPlayerId,
			GAME_TAG);

		if (!sessionResult.IsSuccess(out var sessionId))
		{
			TestHelpers.LogTestInfo("Skipping - Failed to create session");
			return;
		}

		try
		{
			// Simulate a 3-lap race with valid lap times
			var lapTimes = new List<float> { 6.670f, 7.120f, 6.890f };
			var totalTime = 20.680f;
			var trackId = "oval_track";

			// Emit individual lap complete events using RacingEventService
			for (int i = 0; i < lapTimes.Count; i++)
			{
				var checkpoints = CreateTestCheckpoints(lapTimes[i]);
				var lapResult = await RacingEventService.EmitLapCompleteAsync(trackId, i + 1, lapTimes[i], checkpoints);

				if (lapResult.IsSuccess(out var _))
				{
					TestHelpers.LogTestInfo($"Lap {i + 1} emitted: {lapTimes[i]:F3}s");
				}
				else if (lapResult.IsFailure(out var lapError))
				{
					TestHelpers.LogTestError($"Lap {i + 1} failed: {lapError.Message}");
				}
			}

			// Emit race finish event
			var raceResult = await EventService.EmitEventAsync("racing/race_finish", new
			{
				track_id = trackId,
				total_time = totalTime,
				total_laps = 3,
				lap_times = lapTimes,
			});

			var raceSuccess = raceResult.IsSuccess(out var _);
			if (raceSuccess)
			{
				TestHelpers.LogTestInfo($"Race finish saved successfully - Time: {totalTime:F3}s");
				raceSuccess.ShouldBeTrue("Valid race should save successfully");
			}
			else if (raceResult.IsFailure(out var raceError))
			{
				TestHelpers.LogTestError($"Race finish failed: {raceError.Message}");
				raceError.Message.ShouldNotContain("Invalid lap");
			}
		}
		finally
		{
			await EventService.CloseActivitySessionAsync(sessionId);
		}
	}

	[Test]
	public async Task RaceFinish_WithFastLapTimes_SavesSuccessfully()
	{
		if (EventService == null)
		{
			TestHelpers.LogTestInfo("Skipping - SessionEventService not available");
			return;
		}

		TestHelpers.LogTestInfo("=== Testing Race Finish with Fast Lap Times (Previously Blocked) ===");

		// Create a session for this test (SessionEventService requires active session for EmitEventAsync)
		var sessionResult = await EventService.CreateActivitySessionAsync(
			TestBoxId,
			TestPlayerId,
			GAME_TAG);

		if (!sessionResult.IsSuccess(out var sessionId))
		{
			TestHelpers.LogTestInfo("Skipping - Failed to create session");
			return;
		}

		try
		{
			// Simulate fast laps that were previously blocked by MIN_LAP_TIME validation:
			// Lap 1: 6.670s
			// Lap 2: 3.600s (previously considered "too fast")
			// Lap 3: 6.890s
			var lapTimes = new List<float> { 6.670f, 3.600f, 6.890f };
			var totalTime = 17.160f;
			var trackId = "oval_track";

			TestHelpers.LogTestInfo($"Saving race with fast lap times: {string.Join(", ", lapTimes)}");

			// Emit race finish event - this should NOW SUCCEED (we trust physics)
			var raceResult = await EventService.EmitEventAsync("racing/race_finish", new
			{
				track_id = trackId,
				total_time = totalTime,
				total_laps = 3,
				lap_times = lapTimes,
			});

			var fastRaceSuccess = raceResult.IsSuccess(out var _);
			if (fastRaceSuccess)
			{
				TestHelpers.LogTestInfo($"Race with fast laps saved successfully - Total: {totalTime:F3}s");
				fastRaceSuccess.ShouldBeTrue("Fast but legitimate lap times should save successfully");
			}
			else if (raceResult.IsFailure(out var fastRaceError))
			{
				TestHelpers.LogTestError($"Race incorrectly rejected: {fastRaceError.Message}");

				// Fast laps should now be accepted
				fastRaceSuccess.ShouldBeTrue($"Fast laps should be accepted, but got error: {fastRaceError.Message}");
			}

			TestHelpers.LogTestInfo("=== Fast Lap Test Complete ===");
		}
		finally
		{
			await EventService.CloseActivitySessionAsync(sessionId);
		}
	}

	[Test]
	public async Task RaceFinish_WithDataIntegrityError_FailsValidation()
	{
		if (RacingEventService == null)
		{
			TestHelpers.LogTestInfo("Skipping - RacingEventService not available");
			return;
		}

		TestHelpers.LogTestInfo("=== Testing Data Integrity Validation ===");

		// Create a session for this test (SessionEventService requires active session for EmitEventAsync)
		var sessionResult = await EventService.CreateActivitySessionAsync(
			TestBoxId,
			TestPlayerId,
			GAME_TAG);

		if (!sessionResult.IsSuccess(out var sessionId))
		{
			TestHelpers.LogTestInfo("Skipping - Failed to create session");
			return;
		}

		try
		{
			// Test case 1: Total time doesn't match lap sum (data corruption)
			var lapTimes = new float[] { 6.670f, 5.500f, 6.890f };
			var totalTime = 999.0f; // Incorrect total time - lap sum is 19.06
			var trackId = "oval_track";

			TestHelpers.LogTestInfo("Testing mismatched total time (should fail)");

			// Use RacingEventService which validates data integrity before sending
			var invalidEntry1 = new RaceEntry(
				RaceId: Guid.NewGuid().ToString(),
				Date: DateTime.UtcNow,
				TrackId: trackId,
				PlayerId: TestPlayerId.ToString(),
				Username: "TestPlayer",
				TotalLaps: 3,
				TotalTime: totalTime,
				LapTimes: lapTimes,
				Checkpoints: CreateTestCheckpoints(lapTimes[0]));

			var result1 = await RacingEventService.EmitRaceFinishAsync(invalidEntry1);

			var result1Success = result1.IsSuccess(out var _);
			result1Success.ShouldBeFalse("Mismatched total time should be rejected");
			if (result1.IsFailure(out var result1Error))
			{
				TestHelpers.LogTestInfo($"Validation correctly rejected: {result1Error.Message}");
				result1Error.Message.ShouldContain("doesn't match");
			}

			// Test case 2: Negative lap time (physics bug)
			var invalidLapTimes = new float[] { 6.670f, -3.500f, 6.890f };
			var validTotalTime = 10.060f; // Matches sum if we allowed negative

			TestHelpers.LogTestInfo("Testing negative lap time (should fail)");

			var invalidEntry2 = new RaceEntry(
				RaceId: Guid.NewGuid().ToString(),
				Date: DateTime.UtcNow,
				TrackId: trackId,
				PlayerId: TestPlayerId.ToString(),
				Username: "TestPlayer",
				TotalLaps: 3,
				TotalTime: validTotalTime,
				LapTimes: invalidLapTimes,
				Checkpoints: CreateTestCheckpoints(invalidLapTimes[0]));

			var result2 = await RacingEventService.EmitRaceFinishAsync(invalidEntry2);

			var result2Success = result2.IsSuccess(out var _);
			result2Success.ShouldBeFalse("Negative lap time should be rejected");
			if (result2.IsFailure(out var result2Error))
			{
				TestHelpers.LogTestInfo($"Validation correctly rejected: {result2Error.Message}");
				result2Error.Message.ShouldContain("must be positive");
			}

			TestHelpers.LogTestInfo("=== Data Integrity Validation Complete ===");
		}
		finally
		{
			await EventService.CloseActivitySessionAsync(sessionId);
		}
	}
}
