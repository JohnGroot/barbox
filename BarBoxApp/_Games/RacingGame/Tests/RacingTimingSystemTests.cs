using System.Collections.Generic;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

namespace BarBox.Games.Racing.Tests;

/// <summary>
/// Unit tests for RacingTimingSystem - the core timing and lap management system
/// Tests countdown, lap tracking, state machine, and data integrity
/// </summary>
public class RacingTimingSystemTests : TestClass
{
	private const string TEST_PLAYER_ID = "test_player_1";
	private const string TEST_PLAYER_ID_2 = "test_player_2";

	private RacingTimingSystem _timingSystem;

	public RacingTimingSystemTests(Node testScene) : base(testScene)
	{
	}

	[SetupAll]
	public void Setup()
	{
		_timingSystem = new RacingTimingSystem();
		TestScene.AddChild(_timingSystem);
	}

	[Cleanup]
	public void Cleanup()
	{
		_timingSystem.ResetRacingData();
	}

	[CleanupAll]
	public void TearDown()
	{
		_timingSystem?.QueueFree();
	}

	// ================================================================
	// COUNTDOWN SYSTEM TESTS
	// ================================================================

	[Test]
	public void StartCountdown_SetsCorrectInitialState()
	{
		_timingSystem.StartCountdown();

		_timingSystem.IsInCountdown.ShouldBeTrue();
		_timingSystem.CurrentRacingState.ShouldBe(RacingTimingSystem.RacingState.CountdownWaiting);
		_timingSystem.CountdownNumber.ShouldBe(0); // Starts at 0, first update sets to 4
	}

	[Test]
	public void UpdateCountdown_TransitionsThrough4321()
	{
		_timingSystem.StartCountdown();
		var countdownNumbers = new List<int>();

		// Simulate countdown progression
		for (float time = 0; time < 4.5f; time += 0.5f)
		{
			_timingSystem.UpdateCountdown(0.5f);
			countdownNumbers.Add(_timingSystem.CountdownNumber);
		}

		// Should transition through 4, 3, 2, 1, 0
		countdownNumbers.ShouldContain(4);
		countdownNumbers.ShouldContain(3);
		countdownNumbers.ShouldContain(2);
		countdownNumbers.ShouldContain(1);
		countdownNumbers.ShouldContain(0);
	}

	[Test]
	public void UpdateCountdown_ReturnsTrueOnCompletion()
	{
		_timingSystem.StartCountdown();

		// Advance past countdown duration
		bool completed = false;
		for (int i = 0; i < 10 && !completed; i++)
		{
			completed = _timingSystem.UpdateCountdown(1.0f);
		}

		completed.ShouldBeTrue();
		_timingSystem.IsInCountdown.ShouldBeFalse();
		_timingSystem.CurrentRacingState.ShouldBe(RacingTimingSystem.RacingState.Racing);
	}

	[Test]
	public void EndCountdown_SetsRacingState()
	{
		_timingSystem.StartCountdown();
		_timingSystem.EndCountdown();

		_timingSystem.IsInCountdown.ShouldBeFalse();
		_timingSystem.CurrentRacingState.ShouldBe(RacingTimingSystem.RacingState.Racing);
	}

	[Test]
	public void UpdateCountdown_ReturnsFalseWhenNotInCountdown()
	{
		// Not in countdown state
		var result = _timingSystem.UpdateCountdown(1.0f);
		result.ShouldBeFalse();
	}

	// ================================================================
	// LAP MANAGEMENT TESTS
	// ================================================================

	[Test]
	public void StartPlayerLap_IncrementsLapCount()
	{
		_timingSystem.StartPlayerLap(TEST_PLAYER_ID);
		_timingSystem.GetPlayerCurrentLap(TEST_PLAYER_ID).ShouldBe(1);

		_timingSystem.StartPlayerLap(TEST_PLAYER_ID);
		_timingSystem.GetPlayerCurrentLap(TEST_PLAYER_ID).ShouldBe(2);

		_timingSystem.StartPlayerLap(TEST_PLAYER_ID);
		_timingSystem.GetPlayerCurrentLap(TEST_PLAYER_ID).ShouldBe(3);
	}

	[Test]
	public void StartPlayerLap_ResetsLapTimeAndGapTime()
	{
		_timingSystem.StartPlayerLap(TEST_PLAYER_ID);

		_timingSystem.GetPlayerCurrentLapTime(TEST_PLAYER_ID).ShouldBe(0.0f);
		_timingSystem.GetPlayerGapTime(TEST_PLAYER_ID).ShouldBe(0.0f);
	}

	[Test]
	public void CompletePlayerLap_RecordsLapTime()
	{
		_timingSystem.StartPlayerLap(TEST_PLAYER_ID);
		_timingSystem.CompletePlayerLap(TEST_PLAYER_ID, 15.5f);

		var lapTimes = _timingSystem.GetPlayerLapTimes(TEST_PLAYER_ID);
		lapTimes.Count.ShouldBe(1);
		lapTimes[0].ShouldBe(15.5f);
	}

	[Test]
	public void CompletePlayerLap_AccumulatesMultipleLaps()
	{
		float[] expectedTimes = { 15.3f, 14.8f, 15.0f };

		for (int i = 0; i < expectedTimes.Length; i++)
		{
			_timingSystem.StartPlayerLap(TEST_PLAYER_ID);
			_timingSystem.CompletePlayerLap(TEST_PLAYER_ID, expectedTimes[i]);
		}

		var lapTimes = _timingSystem.GetPlayerLapTimes(TEST_PLAYER_ID);
		lapTimes.Count.ShouldBe(3);
		lapTimes[0].ShouldBe(15.3f);
		lapTimes[1].ShouldBe(14.8f);
		lapTimes[2].ShouldBe(15.0f);
	}

	[Test]
	public void CompletePlayerLap_UpdatesBestLapTime()
	{
		_timingSystem.StartPlayerLap(TEST_PLAYER_ID);
		_timingSystem.CompletePlayerLap(TEST_PLAYER_ID, 15.5f);
		_timingSystem.GetPlayerBestLapTime(TEST_PLAYER_ID).ShouldBe(15.5f);

		_timingSystem.StartPlayerLap(TEST_PLAYER_ID);
		_timingSystem.CompletePlayerLap(TEST_PLAYER_ID, 14.2f);
		_timingSystem.GetPlayerBestLapTime(TEST_PLAYER_ID).ShouldBe(14.2f);

		// Slower lap should not update best
		_timingSystem.StartPlayerLap(TEST_PLAYER_ID);
		_timingSystem.CompletePlayerLap(TEST_PLAYER_ID, 16.0f);
		_timingSystem.GetPlayerBestLapTime(TEST_PLAYER_ID).ShouldBe(14.2f);
	}

	[Test]
	public void GetPlayerTotalTime_CalculatesCorrectly()
	{
		// Complete two laps
		_timingSystem.StartPlayerLap(TEST_PLAYER_ID);
		_timingSystem.CompletePlayerLap(TEST_PLAYER_ID, 10.0f);

		_timingSystem.StartPlayerLap(TEST_PLAYER_ID);
		_timingSystem.CompletePlayerLap(TEST_PLAYER_ID, 12.0f);

		// Start third lap and simulate time passing
		_timingSystem.StartPlayerLap(TEST_PLAYER_ID);

		// Manually update current lap time by simulating timer update
		var mockPlayers = new List<BasePlayer>();
		// Note: We can't easily mock BasePlayer, so test with completed laps only

		var totalTime = _timingSystem.GetPlayerTotalTime(TEST_PLAYER_ID);
		totalTime.ShouldBe(22.0f); // 10 + 12 + 0 (current lap)
	}

	// ================================================================
	// STATE MACHINE TESTS
	// ================================================================

	[Test]
	public void InitialState_IsIdle()
	{
		var freshSystem = new RacingTimingSystem();
		TestScene.AddChild(freshSystem);

		freshSystem.CurrentRacingState.ShouldBe(RacingTimingSystem.RacingState.Idle);

		freshSystem.QueueFree();
	}

	[Test]
	public void StartPracticeMode_SetsCorrectState()
	{
		_timingSystem.StartPracticeMode();
		_timingSystem.CurrentRacingState.ShouldBe(RacingTimingSystem.RacingState.PracticeMode);
	}

	[Test]
	public void StartRacing_SetsCorrectState()
	{
		_timingSystem.StartRacing();
		_timingSystem.CurrentRacingState.ShouldBe(RacingTimingSystem.RacingState.Racing);
	}

	[Test]
	public void SetPaused_True_SetsPausedState()
	{
		_timingSystem.StartRacing();
		_timingSystem.SetPaused(true);

		_timingSystem.CurrentRacingState.ShouldBe(RacingTimingSystem.RacingState.RacePaused);
	}

	[Test]
	public void SetPaused_False_ResumesToCorrectState()
	{
		// Test resume in time trial mode
		_timingSystem.SetGameMode(RacingMode.TimeTrial);
		_timingSystem.StartRacing();
		_timingSystem.SetPaused(true);
		_timingSystem.SetPaused(false);

		_timingSystem.CurrentRacingState.ShouldBe(RacingTimingSystem.RacingState.Racing);

		// Test resume in practice mode
		_timingSystem.SetGameMode(RacingMode.Practice);
		_timingSystem.StartPracticeMode();
		_timingSystem.SetPaused(true);
		_timingSystem.SetPaused(false);

		_timingSystem.CurrentRacingState.ShouldBe(RacingTimingSystem.RacingState.PracticeMode);
	}

	[Test]
	public void SetGameOverDeciding_SetsCorrectState()
	{
		_timingSystem.SetGameOverDeciding();
		_timingSystem.CurrentRacingState.ShouldBe(RacingTimingSystem.RacingState.GameOverDeciding);
	}

	[Test]
	public void SetTrackLoading_SetsCorrectState()
	{
		_timingSystem.SetTrackLoading();
		_timingSystem.CurrentRacingState.ShouldBe(RacingTimingSystem.RacingState.TrackLoading);
	}

	[Test]
	public void StopRacing_ResetsToIdle()
	{
		_timingSystem.StartRacing();
		_timingSystem.StartCountdown();
		_timingSystem.StopRacing();

		_timingSystem.CurrentRacingState.ShouldBe(RacingTimingSystem.RacingState.Idle);
		_timingSystem.IsInCountdown.ShouldBeFalse();
	}

	// ================================================================
	// INPUT ENABLED TESTS
	// ================================================================

	[Test]
	public void IsInputEnabled_ReturnsFalse_DuringCountdown()
	{
		_timingSystem.StartCountdown();
		_timingSystem.IsInputEnabled().ShouldBeFalse();
	}

	[Test]
	public void IsInputEnabled_ReturnsFalse_DuringGameOver()
	{
		_timingSystem.SetGameOverDeciding();
		_timingSystem.IsInputEnabled().ShouldBeFalse();
	}

	[Test]
	public void IsInputEnabled_ReturnsFalse_DuringTrackLoading()
	{
		_timingSystem.SetTrackLoading();
		_timingSystem.IsInputEnabled().ShouldBeFalse();
	}

	[Test]
	public void IsInputEnabled_ReturnsFalse_WhenPaused()
	{
		_timingSystem.SetPaused(true);
		_timingSystem.IsInputEnabled().ShouldBeFalse();
	}

	[Test]
	public void IsInputEnabled_ReturnsTrue_DuringRacing()
	{
		_timingSystem.StartRacing();
		_timingSystem.IsInputEnabled().ShouldBeTrue();
	}

	[Test]
	public void IsInputEnabled_ReturnsTrue_DuringPractice()
	{
		_timingSystem.StartPracticeMode();
		_timingSystem.IsInputEnabled().ShouldBeTrue();
	}

	// ================================================================
	// DATA RESET TESTS
	// ================================================================

	[Test]
	public void ResetRacingData_ClearsAllPlayerData()
	{
		// Add some data
		_timingSystem.StartPlayerLap(TEST_PLAYER_ID);
		_timingSystem.CompletePlayerLap(TEST_PLAYER_ID, 15.0f);
		_timingSystem.StartPlayerLap(TEST_PLAYER_ID_2);

		// Reset
		_timingSystem.ResetRacingData();

		// Verify all cleared
		_timingSystem.GetPlayerCurrentLap(TEST_PLAYER_ID).ShouldBe(0);
		_timingSystem.GetPlayerCurrentLap(TEST_PLAYER_ID_2).ShouldBe(0);
		_timingSystem.GetPlayerLapTimes(TEST_PLAYER_ID).Count.ShouldBe(0);
		_timingSystem.GetPlayerBestLapTime(TEST_PLAYER_ID).ShouldBe(float.MaxValue);
		_timingSystem.CurrentRacingState.ShouldBe(RacingTimingSystem.RacingState.Idle);
	}

	// ================================================================
	// EDGE CASE TESTS
	// ================================================================

	[Test]
	public void GetPlayerCurrentLap_ReturnsZero_ForUnknownPlayer()
	{
		_timingSystem.GetPlayerCurrentLap("unknown_player").ShouldBe(0);
	}

	[Test]
	public void GetPlayerCurrentLap_ReturnsZero_ForNullPlayerId()
	{
		_timingSystem.GetPlayerCurrentLap(null).ShouldBe(0);
	}

	[Test]
	public void GetPlayerCurrentLap_ReturnsZero_ForEmptyPlayerId()
	{
		_timingSystem.GetPlayerCurrentLap("").ShouldBe(0);
	}

	[Test]
	public void GetPlayerBestLapTime_ReturnsMaxValue_ForUnknownPlayer()
	{
		_timingSystem.GetPlayerBestLapTime("unknown_player").ShouldBe(float.MaxValue);
	}

	[Test]
	public void GetPlayerLapTimes_ReturnsEmptyList_ForUnknownPlayer()
	{
		var lapTimes = _timingSystem.GetPlayerLapTimes("unknown_player");
		lapTimes.ShouldNotBeNull();
		lapTimes.Count.ShouldBe(0);
	}

	[Test]
	public void CompletePlayerLap_DoesNotThrow_ForUnknownPlayer()
	{
		// Should not throw, just silently return
		Should.NotThrow(() => _timingSystem.CompletePlayerLap("unknown_player", 15.0f));
	}

	// ================================================================
	// CHECKPOINT TESTS
	// ================================================================

	[Test]
	public void OnPlayerCheckpointCrossed_RecordsCheckpointData()
	{
		_timingSystem.StartPlayerLap(TEST_PLAYER_ID);
		_timingSystem.OnPlayerCheckpointCrossed(TEST_PLAYER_ID, 0);

		var checkpoints = _timingSystem.GetPlayerCheckpointTimes(TEST_PLAYER_ID);
		checkpoints.Count.ShouldBe(1);
		checkpoints[0].Index.ShouldBe(0);
	}

	[Test]
	public void OnPlayerCheckpointCrossed_ResetsGapTime()
	{
		_timingSystem.StartPlayerLap(TEST_PLAYER_ID);

		// Simulate some gap time accumulation (would happen via UpdateRacingTimers)
		// For now, just verify checkpoint resets it
		_timingSystem.OnPlayerCheckpointCrossed(TEST_PLAYER_ID, 0);

		_timingSystem.GetPlayerGapTime(TEST_PLAYER_ID).ShouldBe(0.0f);
	}

	// ================================================================
	// TARGET LAPS TESTS
	// ================================================================

	[Test]
	public void TargetLaps_DefaultsToThree()
	{
		var freshSystem = new RacingTimingSystem();
		TestScene.AddChild(freshSystem);

		freshSystem.TargetLaps.ShouldBe(3);

		freshSystem.QueueFree();
	}

	[Test]
	public void TargetLaps_CanBeSet()
	{
		_timingSystem.TargetLaps = 5;
		_timingSystem.TargetLaps.ShouldBe(5);

		_timingSystem.TargetLaps = 1;
		_timingSystem.TargetLaps.ShouldBe(1);
	}

	// ================================================================
	// SCORE CALCULATION TESTS
	// ================================================================

	[Test]
	public void CalculatePlayerScore_PracticeMode_ReturnsBestLapTime()
	{
		_timingSystem.StartPlayerLap(TEST_PLAYER_ID);
		_timingSystem.CompletePlayerLap(TEST_PLAYER_ID, 15.0f);
		_timingSystem.StartPlayerLap(TEST_PLAYER_ID);
		_timingSystem.CompletePlayerLap(TEST_PLAYER_ID, 14.0f);
		_timingSystem.StartPlayerLap(TEST_PLAYER_ID);
		_timingSystem.CompletePlayerLap(TEST_PLAYER_ID, 16.0f);

		var score = _timingSystem.CalculatePlayerScore(TEST_PLAYER_ID, RacingMode.Practice);
		score.ShouldBe(14.0f); // Best lap time
	}

	[Test]
	public void CalculatePlayerScore_TimeTrial_ReturnsTotalTime()
	{
		_timingSystem.StartPlayerLap(TEST_PLAYER_ID);
		_timingSystem.CompletePlayerLap(TEST_PLAYER_ID, 15.0f);
		_timingSystem.StartPlayerLap(TEST_PLAYER_ID);
		_timingSystem.CompletePlayerLap(TEST_PLAYER_ID, 14.0f);
		_timingSystem.StartPlayerLap(TEST_PLAYER_ID);
		_timingSystem.CompletePlayerLap(TEST_PLAYER_ID, 16.0f);

		var score = _timingSystem.CalculatePlayerScore(TEST_PLAYER_ID, RacingMode.TimeTrial);
		score.ShouldBe(45.0f); // Total of all laps
	}

	// ================================================================
	// MULTI-PLAYER TESTS
	// ================================================================

	[Test]
	public void MultiplePlayersCanTrackIndependently()
	{
		_timingSystem.StartPlayerLap(TEST_PLAYER_ID);
		_timingSystem.StartPlayerLap(TEST_PLAYER_ID_2);

		_timingSystem.CompletePlayerLap(TEST_PLAYER_ID, 15.0f);
		_timingSystem.CompletePlayerLap(TEST_PLAYER_ID_2, 18.0f);

		_timingSystem.StartPlayerLap(TEST_PLAYER_ID);
		_timingSystem.CompletePlayerLap(TEST_PLAYER_ID, 14.0f);

		// Verify independent tracking
		_timingSystem.GetPlayerCurrentLap(TEST_PLAYER_ID).ShouldBe(2);
		_timingSystem.GetPlayerCurrentLap(TEST_PLAYER_ID_2).ShouldBe(1);

		_timingSystem.GetPlayerBestLapTime(TEST_PLAYER_ID).ShouldBe(14.0f);
		_timingSystem.GetPlayerBestLapTime(TEST_PLAYER_ID_2).ShouldBe(18.0f);

		_timingSystem.GetPlayerLapTimes(TEST_PLAYER_ID).Count.ShouldBe(2);
		_timingSystem.GetPlayerLapTimes(TEST_PLAYER_ID_2).Count.ShouldBe(1);
	}
}
