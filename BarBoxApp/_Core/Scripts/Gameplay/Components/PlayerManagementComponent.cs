using System.Collections.Generic;
using Godot;

/// <summary>
/// Optional component for games that need player tracking
/// Extracted from GameController to keep base class minimal
/// </summary>
public partial class PlayerManagementComponent : Node
{
	[Signal]
	public delegate void PlayerAddedEventHandler(string playerId);

	[Signal]
	public delegate void PlayerRemovedEventHandler(string playerId);

	private List<BasePlayer> _players = [];

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

	public void RemovePlayer(BasePlayer player)
	{
		if (player == null)
		{
			return;
		}

		if (_players.Remove(player))
		{
			EmitSignal(SignalName.PlayerRemoved, player.PlayerId);
		}
	}

	public void RemovePlayerById(string playerId)
	{
		var player = _players.Find(p => p.PlayerId == playerId);
		if (player != null)
		{
			RemovePlayer(player);
		}
	}

	public BasePlayer GetPlayer(string playerId)
	{
		return _players.Find(p => p.PlayerId == playerId);
	}

	/// <summary>
	/// Get all players (returns copy)
	/// </summary>
	public List<BasePlayer> GetPlayers()
	{
		return [.. _players];
	}

	public int GetPlayerCount()
	{
		return _players.Count;
	}

	public void ClearPlayers()
	{
		_players.Clear();
	}

	public bool HasPlayer(string playerId)
	{
		return _players.Exists(p => p.PlayerId == playerId);
	}
}
