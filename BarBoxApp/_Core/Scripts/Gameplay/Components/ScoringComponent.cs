using Godot;
using System.Collections.Generic;

/// <summary>
/// Optional component for games that need score tracking
/// Extracted from GameController to keep base class minimal
/// </summary>
public partial class ScoringComponent : Node
{
	[Signal] public delegate void ScoreChangedEventHandler(string playerId, float score);

	private Dictionary<string, float> _playerScores = new();

	/// <summary>
	/// Initialize scores for a player
	/// </summary>
	public void InitializePlayer(string playerId, float initialScore = 0.0f)
	{
		_playerScores[playerId] = initialScore;
		EmitSignal(SignalName.ScoreChanged, playerId, initialScore);
	}

	/// <summary>
	/// Update a player's score to a specific value
	/// </summary>
	public void UpdateScore(string playerId, float score)
	{
		if (!_playerScores.ContainsKey(playerId))
		{
			GD.PrintErr($"[ScoringComponent] Player {playerId} not initialized");
			return;
		}

		_playerScores[playerId] = score;
		EmitSignal(SignalName.ScoreChanged, playerId, score);
	}

	/// <summary>
	/// Add points to a player's score
	/// </summary>
	public void AddScore(string playerId, float points)
	{
		if (!_playerScores.ContainsKey(playerId))
		{
			GD.PrintErr($"[ScoringComponent] Player {playerId} not initialized");
			return;
		}

		_playerScores[playerId] += points;
		EmitSignal(SignalName.ScoreChanged, playerId, _playerScores[playerId]);
	}

	/// <summary>
	/// Get a player's current score
	/// </summary>
	public float GetPlayerScore(string playerId)
	{
		return _playerScores.GetValueOrDefault(playerId, 0.0f);
	}

	/// <summary>
	/// Get all player scores
	/// </summary>
	public Dictionary<string, float> GetAllScores()
	{
		return new Dictionary<string, float>(_playerScores);
	}

	/// <summary>
	/// Clear all scores
	/// </summary>
	public void ClearScores()
	{
		_playerScores.Clear();
	}

	/// <summary>
	/// Remove a player's score
	/// </summary>
	public void RemovePlayer(string playerId)
	{
		_playerScores.Remove(playerId);
	}

	/// <summary>
	/// Save scores to UserManager
	/// </summary>
	public void SaveScores(string gameId)
	{
		var userManager = UserManager.GetAutoload();
		if (userManager == null || string.IsNullOrEmpty(gameId))
			return;

		foreach (var kvp in _playerScores)
		{
			// Convert float score to int for UserManager compatibility
			int intScore = (int)kvp.Value;
			userManager.UpdateHighScore(gameId, intScore);
		}
	}
}
