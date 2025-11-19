using Godot;
using System.Collections.Generic;
using System.Linq;

namespace BarBox.Games.Racing
{
	/// <summary>
	/// Handles all racing timing logic, lap management, and race state progression
	/// Extracted from RacingGame to follow single responsibility principle
	/// </summary>
	[GlobalClass]
	public partial class RacingTimingSystem : Node
{
	// ================================================================
	// SIGNALS
	// ================================================================
	
	[Signal] public delegate void LapCompletedEventHandler(string playerId, int lapNumber, float lapTime);
	[Signal] public delegate void RaceCompletedEventHandler(string playerId, float totalTime);
	[Signal] public delegate void CheckpointCrossedEventHandler(string playerId, int checkpointIndex, float gapTime);

	// ================================================================
	// RACING STATE AND ENUMS
	// ================================================================
	
	public enum RacingState 
	{ 
		Idle,                    // No active session
		PracticeMode,           // Free practice, no credits required
		WaitingForCredits,      // Checking affordability for time trial
		CountdownWaiting,       // Pre-race countdown (input locked)
		Racing,                 // Active race/practice
		RacePaused,            // Paused during race
		CrossingFinish,        // Processing finish line crossing
		GameOverDeciding,      // Showing results, awaiting user choice (input locked)
		HighScoreDisplay,      // Viewing high scores (input locked)
		TrackLoading,          // Loading new track (input locked)
		Finished               // Race completed (legacy state for compatibility)
	}

	// ================================================================
	// CONSTANTS
	// ================================================================

	private const int COUNTDOWN_START_NUMBER = 4;
	private const float COUNTDOWN_INTERVAL_SECONDS = 1.0f;

	private static readonly List<float> EMPTY_LAP_TIMES = [];
	private static readonly List<CheckpointTime> EMPTY_CHECKPOINTS = [];

	// ================================================================
	// PRIVATE FIELDS - RACING LOGIC
	// ================================================================

	// Racing state management
	private RacingState _racingState = RacingState.Idle;
	private bool _inCountdown = false;
	private float _countdownTime = 0.0f;
	private int _countdownNumber = 0;
	private int _targetLaps = 3;
	
	// Lap and timing tracking per player
	private Dictionary<string, int> _playerCurrentLap = new();
	private Dictionary<string, List<float>> _playerLapTimes = new();
	private Dictionary<string, float> _playerCurrentLapTime = new();
	private Dictionary<string, float> _playerBestLapTime = new();
	private Dictionary<string, float> _playerGapTime = new(); // Time since last checkpoint in practice mode
	private Dictionary<string, List<CheckpointTime>> _playerCheckpointTimes = new();
	private Dictionary<string, float> _playerLastCheckpointTime = new();

	// Game mode reference
	private RacingMode _currentGameMode = RacingMode.Practice;

	// ================================================================
	// PUBLIC PROPERTIES
	// ================================================================

	public RacingState CurrentRacingState => _racingState;
	public bool IsInCountdown => _inCountdown;
	public int CountdownNumber => _countdownNumber;
	public int TargetLaps 
	{ 
		get => _targetLaps; 
		set => _targetLaps = value; 
	}

	// ================================================================
	// COUNTDOWN SYSTEM
	// ================================================================

	public void StartCountdown(float countdownDuration = 4.0f)
	{
		_inCountdown = true;
		_racingState = RacingState.CountdownWaiting;
		_countdownTime = 0.0f;
		_countdownNumber = 0;
	}

	/// <summary>
	/// Handle countdown timer and progression
	/// Returns true if countdown completed, false otherwise
	/// </summary>
	public bool UpdateCountdown(float delta, float countdownDuration = 4.0f)
	{
		if (!_inCountdown) return false;

		_countdownTime += delta;
		int newCountdownNumber = COUNTDOWN_START_NUMBER - (int)(_countdownTime / COUNTDOWN_INTERVAL_SECONDS);

		bool numberChanged = (newCountdownNumber != _countdownNumber);
		if (numberChanged)
		{
			_countdownNumber = newCountdownNumber;
		}

		// Check if countdown should end
		if (_countdownTime >= countdownDuration)
		{
			EndCountdown();
			return true; // Countdown completed
		}

		return numberChanged; // Return true if countdown number changed
	}

	public void EndCountdown()
	{
		_inCountdown = false;
		_racingState = RacingState.Racing;
	}

	// ================================================================
	// LAP MANAGEMENT
	// ================================================================

	public void StartPlayerLap(string playerId)
	{
		_playerCurrentLap.TryAdd(playerId, 0);

		_playerCurrentLap[playerId]++;
		_playerCurrentLapTime[playerId] = 0.0f;
		_playerGapTime[playerId] = 0.0f;

		if (!_playerLapTimes.ContainsKey(playerId))
			_playerLapTimes[playerId] = new List<float>();
	}

	public void CompletePlayerLap(string playerId, float lapTime)
	{
		if (!_playerCurrentLap.TryGetValue(playerId, out int lapNumber))
			return;

		// Ensure lap times list exists before adding
		if (!_playerLapTimes.ContainsKey(playerId))
			_playerLapTimes[playerId] = [];
		_playerLapTimes[playerId].Add(lapTime);

		// Update best lap time
		if (!_playerBestLapTime.TryGetValue(playerId, out float value) || lapTime < value)
		{ 
			value = lapTime;
			_playerBestLapTime[playerId] = value;
		}

		EmitSignal(SignalName.LapCompleted, playerId, lapNumber, lapTime);
	}

	public void CompletePlayerRace(string playerId, List<BasePlayer> allPlayers = null)
	{
		if (!_playerLapTimes.TryGetValue(playerId, out var time)) 
			return;
		
		float totalTime = time.Sum();
		
		EmitSignal(SignalName.RaceCompleted, playerId, totalTime);
		
		// If all players finished, end the race
		if (allPlayers == null)
			return;

		bool allPlayersFinished = true;
		foreach (var player in allPlayers)
		{
			if (GetPlayerCurrentLap(player.PlayerId) < TargetLaps)
			{
				allPlayersFinished = false;
				break;
			}
		}
			
		if (allPlayersFinished)
		{
			_racingState = RacingState.Finished;
		}
	}

	public void OnPlayerCheckpointCrossed(string playerId, int checkpointIndex)
	{
		float gapTime = _playerGapTime.GetValueOrDefault(playerId, 0.0f);
		_playerGapTime[playerId] = 0.0f; // Reset gap timer

		// Calculate total time from race start to this checkpoint
		float totalTime = GetPlayerTotalTime(playerId);

		// Store checkpoint time data
		if (!_playerCheckpointTimes.ContainsKey(playerId))
			_playerCheckpointTimes[playerId] = new List<CheckpointTime>();

		var checkpointTime = new CheckpointTime(checkpointIndex, totalTime, gapTime);
		_playerCheckpointTimes[playerId].Add(checkpointTime);
		_playerLastCheckpointTime[playerId] = totalTime;

		EmitSignal(SignalName.CheckpointCrossed, playerId, checkpointIndex, gapTime);
	}

	public void UpdateRacingTimers(float delta, List<BasePlayer> players)
	{
		foreach (var player in players)
		{
			string playerId = player.PlayerId;

			if (_playerCurrentLapTime.TryGetValue(playerId, out float currentLapTime))
			{
				_playerCurrentLapTime[playerId] = currentLapTime + delta;
			}

			if (_playerGapTime.TryGetValue(playerId, out float gapTime))
			{
				_playerGapTime[playerId] = gapTime + delta;
			}
		}
	}

	// ================================================================
	// RACE STATE MANAGEMENT
	// ================================================================

	public void SetGameMode(RacingMode mode)
	{
		_currentGameMode = mode;
	}

	public void StartRacing()
	{
		_racingState = RacingState.Racing;
	}

	public void StartPracticeMode()
	{
		_racingState = RacingState.PracticeMode;
	}

	public void SetWaitingForCredits()
	{
		_racingState = RacingState.WaitingForCredits;
	}

	public void SetPaused(bool paused)
	{
		if (paused)
		{
			_racingState = RacingState.RacePaused;
		}
		else
		{
			// Resume to appropriate state based on game mode
			_racingState = _currentGameMode == RacingMode.Practice ? RacingState.PracticeMode : RacingState.Racing;
		}
	}

	public void SetGameOverDeciding()
	{
		_racingState = RacingState.GameOverDeciding;
	}

	public void SetHighScoreDisplay()
	{
		_racingState = RacingState.HighScoreDisplay;
	}

	public void SetTrackLoading()
	{
		_racingState = RacingState.TrackLoading;
	}

	public void SetCrossingFinish()
	{
		_racingState = RacingState.CrossingFinish;
	}

	public bool IsInputEnabled()
	{
		return _racingState switch
		{
			RacingState.CountdownWaiting => false,
			RacingState.GameOverDeciding => false,
			RacingState.TrackLoading => false,
			RacingState.WaitingForCredits => false,
			RacingState.HighScoreDisplay => false,
			RacingState.RacePaused => false,
			RacingState.CrossingFinish => false,
			_ => true
		};
	}

	public void StopRacing()
	{
		_racingState = RacingState.Idle;
		_inCountdown = false;
	}

	public void ResetRacingData()
	{
		_playerCurrentLap.Clear();
		_playerLapTimes.Clear();
		_playerCurrentLapTime.Clear();
		_playerBestLapTime.Clear();
		_playerGapTime.Clear();
		_playerCheckpointTimes.Clear();
		_playerLastCheckpointTime.Clear();
		_racingState = RacingState.Idle;
	}

	// ================================================================
	// DATA GETTERS
	// ================================================================

	public int GetPlayerCurrentLap(string playerId) => string.IsNullOrEmpty(playerId) ? 0 : _playerCurrentLap.GetValueOrDefault(playerId, 0);
	public float GetPlayerCurrentLapTime(string playerId) => string.IsNullOrEmpty(playerId) ? 0.0f : _playerCurrentLapTime.GetValueOrDefault(playerId, 0.0f);
	public float GetPlayerBestLapTime(string playerId) => string.IsNullOrEmpty(playerId) ? float.MaxValue : _playerBestLapTime.GetValueOrDefault(playerId, float.MaxValue);
	public float GetPlayerGapTime(string playerId) => string.IsNullOrEmpty(playerId) ? 0.0f : _playerGapTime.GetValueOrDefault(playerId, 0.0f);
	public List<float> GetPlayerLapTimes(string playerId) => string.IsNullOrEmpty(playerId) ? EMPTY_LAP_TIMES : _playerLapTimes.GetValueOrDefault(playerId, EMPTY_LAP_TIMES);
	public List<CheckpointTime> GetPlayerCheckpointTimes(string playerId) => string.IsNullOrEmpty(playerId) ? EMPTY_CHECKPOINTS : _playerCheckpointTimes.GetValueOrDefault(playerId, EMPTY_CHECKPOINTS);

	/// <summary>
	/// Get total time from race start for a player (lap time + current lap progress)
	/// </summary>
	public float GetPlayerTotalTime(string playerId)
	{
		if (string.IsNullOrEmpty(playerId)) return 0.0f;

		var lapTimes = GetPlayerLapTimes(playerId);
		var currentLapTime = GetPlayerCurrentLapTime(playerId);
		return lapTimes.Sum() + currentLapTime;
	}

	/// <summary>
	/// Calculate player score based on game mode
	/// </summary>
	public float CalculatePlayerScore(string playerId, RacingMode gameMode)
	{
		if (gameMode == RacingMode.Practice)
		{
			// In practice mode, score is best lap time
			return GetPlayerBestLapTime(playerId);
		}

		// In time trial, score is total time
		var lapTimes = GetPlayerLapTimes(playerId);
		return lapTimes.Sum();
	}
}
}