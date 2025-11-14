using Godot;
using System.Collections.Generic;

/// <summary>
/// Optional component for games that need player tracking
/// Extracted from GameController to keep base class minimal
/// </summary>
public partial class PlayerManagementComponent : Node
{
	[Signal] public delegate void PlayerAddedEventHandler(string playerId);
	[Signal] public delegate void PlayerRemovedEventHandler(string playerId);

	private List<BasePlayer> _players = new();

	/// <summary>
	/// Add a player to the game
	/// </summary>
	public void AddPlayer(BasePlayer player)
	{
		if (player == null)
		{
			GD.PrintErr("[PlayerManagementComponent] Cannot add null player");
			return;
		}

		if (_players.Contains(player))
		{
			GD.PrintErr($"[PlayerManagementComponent] Player {player.PlayerId} already added");
			return;
		}

		_players.Add(player);
		EmitSignal(SignalName.PlayerAdded, player.PlayerId);
	}

	/// <summary>
	/// Remove a player from the game
	/// </summary>
	public void RemovePlayer(BasePlayer player)
	{
		if (player == null)
			return;

		if (_players.Remove(player))
		{
			EmitSignal(SignalName.PlayerRemoved, player.PlayerId);
		}
	}

	/// <summary>
	/// Remove a player by ID
	/// </summary>
	public void RemovePlayerById(string playerId)
	{
		var player = _players.Find(p => p.PlayerId == playerId);
		if (player != null)
		{
			RemovePlayer(player);
		}
	}

	/// <summary>
	/// Get a player by ID
	/// </summary>
	public BasePlayer GetPlayer(string playerId)
	{
		return _players.Find(p => p.PlayerId == playerId);
	}

	/// <summary>
	/// Get all players (returns copy)
	/// </summary>
	public List<BasePlayer> GetPlayers()
	{
		return new List<BasePlayer>(_players);
	}

	/// <summary>
	/// Get player count
	/// </summary>
	public int GetPlayerCount()
	{
		return _players.Count;
	}

	/// <summary>
	/// Clear all players
	/// </summary>
	public void ClearPlayers()
	{
		_players.Clear();
	}

	/// <summary>
	/// Check if a player exists
	/// </summary>
	public bool HasPlayer(string playerId)
	{
		return _players.Exists(p => p.PlayerId == playerId);
	}
}
