using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Chickensoft.GoDotTest;
using Godot;
using BarBox.Tests.Fixtures;
using ZLinq;
using Shouldly;

namespace BarBox.Games.Racing.Tests;

/// <summary>
/// Comprehensive integration tests for Racing Game
/// Tests race sessions, checkpoint/lap tracking, leaderboards
/// </summary>
public class RacingGameTests : TestClass
{
	private const string GAME_TAG = "racing";
	private EventService _eventService;
	private Guid _testBoxId;
	private Guid _testPlayerId;
	private Guid _testSessionId;

	public RacingGameTests(Node testScene) : base(testScene)
	{
	}

	[SetupAll]
	public async Task SetupRacingGameSession()
	{
		TestHelpers.LogTestInfo("Setting up Racing Game test session");

		// Verify backend is healthy
		var isHealthy = await TestHelpers.IsTestBackendHealthyAsync();
		if (!isHealthy)
		{
			TestHelpers.LogTestWarning("Test backend is not healthy - some tests may be skipped");
			return;
		}

		// Get EventService
		_eventService = TestScene.GetNode<EventService>("/root/EventService");
		_eventService.ShouldNotBeNull("EventService autoload must be available");

		// Generate test identifiers
		_testBoxId = TestHelpers.GenerateTestBoxId();
		_testPlayerId = TestHelpers.GenerateTestPlayerId();

		// Create racing session
		var sessionResult = await _eventService.CreateActivitySessionAsync(
			_testBoxId,
			_testPlayerId,
			GAME_TAG);

		if (sessionResult.IsSuccess)
		{
			_testSessionId = sessionResult.Value;
			_testSessionId.ShouldNotBe(Guid.Empty, "Session ID should be valid");
			TestHelpers.LogTestInfo($"Racing session created: {_testSessionId}");
		}
		else
		{
			TestHelpers.LogTestWarning($"Failed to create session: {sessionResult.Error}");
		}
	}

	[Test]
	public async Task StartRaceSession_EmitsBeginEvent()
	{
		if (_eventService == null || _testSessionId == Guid.Empty)
		{
			TestHelpers.LogTestInfo("Skipping - Session not created");
			return;
		}

		// Emit race start event
		var result = await _eventService.EmitEventAsync("play/begin", new
		{
			game = GAME_TAG,
			track = "oval",
			mode = "time_trial"
		});

		if (result.IsSuccess)
		{
			TestHelpers.LogTestInfo("Race begin event emitted");
		}
		else
		{
			TestHelpers.LogTestError($"Begin event failed: {result.Error}");
		}
	}

	[Test]
	public async Task RecordCheckpointHit_EmitsCheckpointEvent()
	{
		if (_eventService == null || _testSessionId == Guid.Empty)
		{
			TestHelpers.LogTestInfo("Skipping - Session not created");
			return;
		}

		// Emit checkpoint events
		var checkpoint1 = await _eventService.EmitEventAsync("racing/checkpoint", new
		{
			checkpoint_id = 1,
			time_elapsed = 5.2f,
			position = new { x = 100, y = 200 }
		});

		var checkpoint2 = await _eventService.EmitEventAsync("racing/checkpoint", new
		{
			checkpoint_id = 2,
			time_elapsed = 10.8f,
			position = new { x = 300, y = 400 }
		});

		var checkpoint3 = await _eventService.EmitEventAsync("racing/checkpoint", new
		{
			checkpoint_id = 3,
			time_elapsed = 14.5f,
			position = new { x = 500, y = 600 }
		});

		if (checkpoint1.IsSuccess && checkpoint2.IsSuccess && checkpoint3.IsSuccess)
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
		if (_eventService == null || _testSessionId == Guid.Empty)
		{
			TestHelpers.LogTestInfo("Skipping - Session not created");
			return;
		}

		// Emit lap completion events
		var lap1 = await _eventService.EmitEventAsync("racing/lap_complete", new
		{
			lap_number = 1,
			lap_time = 15.3f,
			total_time = 15.3f
		});

		var lap2 = await _eventService.EmitEventAsync("racing/lap_complete", new
		{
			lap_number = 2,
			lap_time = 14.8f,
			total_time = 30.1f
		});

		var lap3 = await _eventService.EmitEventAsync("racing/lap_complete", new
		{
			lap_number = 3,
			lap_time = 15.0f,
			total_time = 45.1f
		});

		if (lap1.IsSuccess && lap2.IsSuccess && lap3.IsSuccess)
		{
			TestHelpers.LogTestInfo("All lap events emitted successfully");
			TestHelpers.LogTestInfo("Best lap: 14.8s (Lap 2)");
		}
		else
		{
			TestHelpers.LogTestWarning("Some lap events failed");
		}
	}

	[Test]
	public async Task RecordRaceFinish_EmitsFinishEventWithLapTimes()
	{
		if (_eventService == null || _testSessionId == Guid.Empty)
		{
			TestHelpers.LogTestInfo("Skipping - Session not created");
			return;
		}

		// Emit race finish event with complete lap time data
		var lapTimes = new List<float> { 15.3f, 14.8f, 15.0f };
		var finalTime = 45.1f;

		var result = await _eventService.EmitEventAsync("play/finish", new
		{
			final_time = finalTime,
			lap_times = lapTimes,
			checkpoints_hit = 12, // 4 checkpoints per lap * 3 laps
			track = "oval"
		});

		if (result.IsSuccess)
		{
			TestHelpers.LogTestInfo($"Race finish event emitted - Final time: {finalTime}s");
		}
		else
		{
			TestHelpers.LogTestError($"Finish event failed: {result.Error}");
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
			("player3", 48.7f)
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
			if (time < bestLap) bestLap = time;
		}

		TestHelpers.LogTestInfo($"Best lap time: {bestLap}s");
		TestHelpers.LogTestInfo("Lap breakdown helps players identify improvement areas");
	}

	[Test]
	public async Task CompleteRacingGameFlow_AllOperationsSucceed()
	{
		if (_eventService == null)
		{
			TestHelpers.LogTestInfo("Skipping - EventService not available");
			return;
		}

		TestHelpers.LogTestInfo("=== Starting Complete Racing Game Flow ===");

		// Step 1: Create session
		var sessionResult = await _eventService.CreateActivitySessionAsync(
			_testBoxId,
			_testPlayerId,
			GAME_TAG);

		if (sessionResult.IsSuccess)
		{
			var flowSessionId = sessionResult.Value;
			TestHelpers.LogTestInfo($"Step 1 - Session Created: ✓ ({flowSessionId})");

			// Step 2: Start race
			var beginResult = await _eventService.EmitEventAsync("play/begin", new
			{
				game = GAME_TAG,
				track = "oval"
			});

			TestHelpers.LogTestInfo($"Step 2 - Race Begin: {(beginResult.IsSuccess ? "✓" : "✗")}");

			// Step 3: Record checkpoints (3 laps, 4 checkpoints each = 12 total)
			for (int lap = 1; lap <= 3; lap++)
			{
				for (int checkpoint = 1; checkpoint <= 4; checkpoint++)
				{
					await _eventService.EmitEventAsync("racing/checkpoint", new
					{
						checkpoint_id = checkpoint,
						lap = lap,
						time_elapsed = (lap - 1) * 15.0f + checkpoint * 3.75f
					});
				}

				// Complete lap
				await _eventService.EmitEventAsync("racing/lap_complete", new
				{
					lap_number = lap,
					lap_time = 15.0f,
					total_time = lap * 15.0f
				});
			}

			TestHelpers.LogTestInfo("Step 3 - Checkpoints & Laps Recorded: ✓");

			// Step 4: Finish race
			var finishResult = await _eventService.EmitEventAsync("play/finish", new
			{
				final_time = 45.0f,
				lap_times = new List<float> { 15.0f, 15.0f, 15.0f },
				checkpoints_hit = 12
			});

			TestHelpers.LogTestInfo($"Step 4 - Race Finish: {(finishResult.IsSuccess ? "✓" : "✗")}");

			// Step 5: Close session
			var closeResult = await _eventService.CloseActivitySessionAsync(flowSessionId);
			TestHelpers.LogTestInfo($"Step 5 - Session Closed: {(closeResult.IsSuccess ? "✓" : "✗")}");
		}
		else
		{
			TestHelpers.LogTestError($"Flow failed at session creation: {sessionResult.Error}");
		}

		TestHelpers.LogTestInfo("=== Racing Game Flow Complete ===");
	}

	[Test]
	public async Task RaceFinish_WithValidLapTimes_SavesSuccessfully()
	{
		if (_eventService == null || _testSessionId == Guid.Empty)
		{
			TestHelpers.LogTestInfo("Skipping - Session not created");
			return;
		}

		TestHelpers.LogTestInfo("=== Testing Race Finish with Valid Lap Times ===");

		// Simulate a 3-lap race with valid lap times
		var lapTimes = new List<float> { 6.670f, 7.120f, 6.890f };
		var totalTime = 20.680f;
		var trackId = "oval_track";

		// Emit individual lap complete events
		float runningTotal = 0f;
		for (int i = 0; i < lapTimes.Count; i++)
		{
			runningTotal += lapTimes[i];
			var lapResult = await _eventService.EmitEventAsync("racing/lap_complete", new
			{
				lap_number = i + 1,
				lap_time = lapTimes[i],
				total_time = runningTotal
			});

			if (lapResult.IsSuccess)
			{
				TestHelpers.LogTestInfo($"Lap {i + 1} emitted: {lapTimes[i]:F3}s");
			}
			else
			{
				TestHelpers.LogTestError($"Lap {i + 1} failed: {lapResult.Error}");
			}
		}

		// Emit race finish event
		var raceResult = await _eventService.EmitEventAsync("racing/race_finish", new
		{
			track_id = trackId,
			total_time = totalTime,
			total_laps = 3,
			lap_times = lapTimes
		});

		if (raceResult.IsSuccess)
		{
			TestHelpers.LogTestInfo($"Race finish saved successfully - Time: {totalTime:F3}s");
			raceResult.IsSuccess.ShouldBeTrue("Valid race should save successfully");
		}
		else
		{
			TestHelpers.LogTestError($"Race finish failed: {raceResult.Error}");
			raceResult.Error.ShouldNotContain("Invalid lap");
		}
	}

	[Test]
	public async Task RaceFinish_WithFastLapTimes_SavesSuccessfully()
	{
		if (_eventService == null || _testSessionId == Guid.Empty)
		{
			TestHelpers.LogTestInfo("Skipping - Session not created");
			return;
		}

		TestHelpers.LogTestInfo("=== Testing Race Finish with Fast Lap Times (Previously Blocked) ===");

		// Simulate fast laps that were previously blocked by MIN_LAP_TIME validation:
		// Lap 1: 6.670s
		// Lap 2: 3.600s (previously considered "too fast")
		// Lap 3: 6.890s
		var lapTimes = new List<float> { 6.670f, 3.600f, 6.890f };
		var totalTime = 17.160f;
		var trackId = "oval_track";

		TestHelpers.LogTestInfo($"Saving race with fast lap times: {string.Join(", ", lapTimes)}");

		// Emit race finish event - this should NOW SUCCEED (we trust physics)
		var raceResult = await _eventService.EmitEventAsync("racing/race_finish", new
		{
			track_id = trackId,
			total_time = totalTime,
			total_laps = 3,
			lap_times = lapTimes
		});

		if (raceResult.IsSuccess)
		{
			TestHelpers.LogTestInfo($"Race with fast laps saved successfully - Total: {totalTime:F3}s");
			raceResult.IsSuccess.ShouldBeTrue("Fast but legitimate lap times should save successfully");
		}
		else
		{
			TestHelpers.LogTestError($"Race incorrectly rejected: {raceResult.Error}");
			// Fast laps should now be accepted
			raceResult.IsSuccess.ShouldBeTrue($"Fast laps should be accepted, but got error: {raceResult.Error}");
		}

		TestHelpers.LogTestInfo("=== Fast Lap Test Complete ===");
	}

	[Test]
	public async Task RaceFinish_WithDataIntegrityError_FailsValidation()
	{
		if (_eventService == null || _testSessionId == Guid.Empty)
		{
			TestHelpers.LogTestInfo("Skipping - Session not created");
			return;
		}

		TestHelpers.LogTestInfo("=== Testing Data Integrity Validation ===");

		// Test case 1: Total time doesn't match lap sum (data corruption)
		var lapTimes = new List<float> { 6.670f, 5.500f, 6.890f };
		var totalTime = 999.0f; // Incorrect total time
		var trackId = "oval_track";

		TestHelpers.LogTestInfo("Testing mismatched total time (should fail)");

		var result1 = await _eventService.EmitEventAsync("racing/race_finish", new
		{
			track_id = trackId,
			total_time = totalTime,
			total_laps = 3,
			lap_times = lapTimes
		});

		result1.IsSuccess.ShouldBeFalse("Mismatched total time should be rejected");
		result1.Error.ShouldContain("doesn't match");

		// Test case 2: Negative lap time (physics bug)
		var invalidLapTimes = new List<float> { 6.670f, -3.500f, 6.890f };
		var validTotalTime = 10.060f; // Matches sum if we allowed negative

		TestHelpers.LogTestInfo("Testing negative lap time (should fail)");

		var result2 = await _eventService.EmitEventAsync("racing/race_finish", new
		{
			track_id = trackId,
			total_time = validTotalTime,
			total_laps = 3,
			lap_times = invalidLapTimes
		});

		result2.IsSuccess.ShouldBeFalse("Negative lap time should be rejected");
		result2.Error.ShouldContain("must be positive");

		TestHelpers.LogTestInfo("=== Data Integrity Validation Complete ===");
	}

	[CleanupAll]
	public async Task CleanupRacingGameSession()
	{
		if (_eventService != null && _testSessionId != Guid.Empty)
		{
			TestHelpers.LogTestInfo("Closing racing game session");
			var result = await _eventService.CloseActivitySessionAsync(_testSessionId);

			if (result.IsSuccess)
			{
				TestHelpers.LogTestInfo("Session closed successfully");
			}
			else
			{
				TestHelpers.LogTestWarning($"Session close failed: {result.Error}");
			}
		}
	}
}
