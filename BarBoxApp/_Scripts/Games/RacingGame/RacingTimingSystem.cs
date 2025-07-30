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
	
	public enum RacingState { Stopped, Countdown, Racing, Finished }

	// ================================================================
	// PRIVATE FIELDS - RACING LOGIC
	// ================================================================
	
	// Racing state management
	private RacingState _racingState = RacingState.Stopped;
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

	// Game mode reference
	private GameController.GameMode _currentGameMode = GameController.GameMode.Practice;

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

	/// <summary>
	/// Start countdown sequence for time trials
	/// </summary>
	public void StartCountdown(float countdownDuration = 4.0f)
	{
		_inCountdown = true;
		_racingState = RacingState.Countdown;
		_countdownTime = 0.0f;
		_countdownNumber = 0;
	}

	/// <summary>
	/// Handle countdown timer and progression
	/// </summary>
	public bool UpdateCountdown(float delta, float countdownDuration = 4.0f)
	{
		if (!_inCountdown) return false;
		
		_countdownTime += delta;
		int newCountdownNumber = 4 - (int)(_countdownTime / 1.0f);
		
		bool numberChanged = false;
		if (newCountdownNumber != _countdownNumber)
		{
			_countdownNumber = newCountdownNumber;
			numberChanged = true;
		}

		// Check if countdown should end
		if (_countdownTime >= countdownDuration)
		{
			EndCountdown();
			return true; // Countdown completed
		}
		
		return numberChanged; // Return true if countdown number changed
	}

	/// <summary>
	/// End countdown and start the actual race
	/// </summary>
	public void EndCountdown()
	{
		_inCountdown = false;
		_racingState = RacingState.Racing;
	}

	// ================================================================
	// LAP MANAGEMENT
	// ================================================================

	/// <summary>
	/// Start a new lap for a player
	/// </summary>
	public void StartPlayerLap(string playerId)
	{
		if (!_playerCurrentLap.ContainsKey(playerId))
			_playerCurrentLap[playerId] = 0;
		
		_playerCurrentLap[playerId]++;
		_playerCurrentLapTime[playerId] = 0.0f;
		_playerGapTime[playerId] = 0.0f;
		
		if (!_playerLapTimes.ContainsKey(playerId))
			_playerLapTimes[playerId] = new List<float>();
	}

	/// <summary>
	/// Complete a lap for a player
	/// </summary>
	public void CompletePlayerLap(string playerId, float lapTime)
	{
		if (!_playerCurrentLap.ContainsKey(playerId)) return;
		
		int lapNumber = _playerCurrentLap[playerId];
		_playerLapTimes[playerId].Add(lapTime);
		
		// Update best lap time
		if (!_playerBestLapTime.ContainsKey(playerId) || lapTime < _playerBestLapTime[playerId])
		{
			_playerBestLapTime[playerId] = lapTime;
		}
		
		EmitSignal(SignalName.LapCompleted, playerId, lapNumber, lapTime);
		
		// Check if race is complete
		if (_currentGameMode == GameController.GameMode.TimeTrial && lapNumber >= TargetLaps)
		{
			CompletePlayerRace(playerId);
		}
	}

	/// <summary>
	/// Complete the race for a player
	/// </summary>
	public void CompletePlayerRace(string playerId, List<BasePlayer> allPlayers = null)
	{
		if (!_playerLapTimes.ContainsKey(playerId)) return;
		
		float totalTime = _playerLapTimes[playerId].Sum();
		
		EmitSignal(SignalName.RaceCompleted, playerId, totalTime);
		
		// If all players finished, end the race
		if (allPlayers != null)
		{
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
	}

	/// <summary>
	/// Player crossed a checkpoint
	/// </summary>
	public void OnPlayerCheckpointCrossed(string playerId, int checkpointIndex)
	{
		float gapTime = _playerGapTime.GetValueOrDefault(playerId, 0.0f);
		_playerGapTime[playerId] = 0.0f; // Reset gap timer
		
		EmitSignal(SignalName.CheckpointCrossed, playerId, checkpointIndex, gapTime);
	}

	/// <summary>
	/// Update racing timers for all players
	/// </summary>
	public void UpdateRacingTimers(float delta, List<BasePlayer> players)
	{
		foreach (var player in players)
		{
			string playerId = player.PlayerId;
			
			if (_playerCurrentLapTime.ContainsKey(playerId))
			{
				_playerCurrentLapTime[playerId] += delta;
			}
			
			if (_playerGapTime.ContainsKey(playerId))
			{
				_playerGapTime[playerId] += delta;
			}
		}
	}

	// ================================================================
	// RACE STATE MANAGEMENT
	// ================================================================

	/// <summary>
	/// Set the current game mode
	/// </summary>
	public void SetGameMode(GameController.GameMode mode)
	{
		_currentGameMode = mode;
	}

	/// <summary>
	/// Start racing state
	/// </summary>
	public void StartRacing()
	{
		_racingState = RacingState.Racing;
	}

	/// <summary>
	/// Stop racing and reset state
	/// </summary>
	public void StopRacing()
	{
		_racingState = RacingState.Stopped;
		_inCountdown = false;
	}

	/// <summary>
	/// Reset all racing-specific data
	/// </summary>
	public void ResetRacingData()
	{
		_playerCurrentLap.Clear();
		_playerLapTimes.Clear();
		_playerCurrentLapTime.Clear();
		_playerBestLapTime.Clear();
		_playerGapTime.Clear();
		_racingState = RacingState.Stopped;
	}

	// ================================================================
	// DATA GETTERS
	// ================================================================

	public int GetPlayerCurrentLap(string playerId) => _playerCurrentLap.GetValueOrDefault(playerId, 0);
	public float GetPlayerCurrentLapTime(string playerId) => _playerCurrentLapTime.GetValueOrDefault(playerId, 0.0f);
	public float GetPlayerBestLapTime(string playerId) => _playerBestLapTime.GetValueOrDefault(playerId, float.MaxValue);
	public float GetPlayerGapTime(string playerId) => _playerGapTime.GetValueOrDefault(playerId, 0.0f);
	public List<float> GetPlayerLapTimes(string playerId) => _playerLapTimes.GetValueOrDefault(playerId, new List<float>());

	/// <summary>
	/// Calculate player score based on game mode
	/// </summary>
	public float CalculatePlayerScore(string playerId, GameController.GameMode gameMode)
	{
		if (gameMode == GameController.GameMode.Practice)
		{
			// In practice mode, score is best lap time
			return GetPlayerBestLapTime(playerId);
		}
		else
		{
			// In time trial, score is total time
			var lapTimes = GetPlayerLapTimes(playerId);
			return lapTimes.Sum();
		}
	}
}
}