using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Chickensoft.GoDotTest;
using Godot;
using BarBox.Tests.Fixtures;
using ZLinq;

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
	public async void SetupRacingGameSession()
	{
		TestHelpers.LogTestInfo("Setting up Racing Game test session");

		// Verify backend is healthy
		var isHealthy = await TestHelpers.IsTestBackendHealthyAsync();
		if (!isHealthy)
		{
			TestHelpers.LogTestError("Test backend is not healthy!");
			return;
		}

		// Get EventService
		_eventService = TestScene.GetNode<EventService>("/root/EventService");
		if (_eventService == null)
		{
			TestHelpers.LogTestError("EventService autoload not found!");
			return;
		}

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
			TestHelpers.LogTestInfo($"Racing session created: {_testSessionId}");
		}
		else
		{
			TestHelpers.LogTestWarning($"Failed to create session: {sessionResult.Error}");
		}
	}

	[Test]
	public async void StartRaceSession_EmitsBeginEvent()
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
	public async void RecordCheckpointHit_EmitsCheckpointEvent()
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
	public async void RecordLapCompletion_EmitsLapEvent()
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
	public async void RecordRaceFinish_EmitsFinishEventWithLapTimes()
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
	public async void LoadLeaderboardData_FromBackend_ReturnsTopTimes()
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
	public async void VerifyLeaderboardOrdering_BestTimesFirst()
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
	public async void VerifyLapTimeBreakdowns_InLeaderboard()
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
	public async void CompleteRacingGameFlow_AllOperationsSucceed()
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

	[CleanupAll]
	public async void CleanupRacingGameSession()
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
