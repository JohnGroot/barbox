using System;
using System.Collections.Generic;
using System.Linq;

namespace BarBox.Core.Gameplay;

/// <summary>
/// Base identity for an entry in a multi-player game's roster. UUID-keyed
/// (<see cref="PlayerId"/>) with phone number as an attribute, not the key -
/// games that need to key on phone number at a backend emit boundary
/// (e.g. a leaderboard join) still can, without every lookup in the game
/// needing to special-case anonymous/phoneless players.
/// </summary>
public abstract class PlayerRosterEntry
{
	public Guid PlayerId { get; init; }

	public string PhoneNumber { get; set; } = string.Empty;

	public string DisplayName { get; set; } = "Player";

	public int SlotIndex { get; init; }

	public bool IsLoggedIn => PlayerId != Guid.Empty;
}

/// <summary>
/// Shared roster of players for multi-player games - Add/Remove/Find, keyed
/// by <see cref="PlayerRosterEntry.PlayerId"/>. Extracted from Nines' hand-rolled
/// phone-keyed player list; Carrom's competitive-mode identity (fixed
/// "player1"/"player2" turn labels, decoupled from session identity) doesn't
/// map onto this shape and is not migrated here.
/// </summary>
public class PlayerRoster<T>
	where T : PlayerRosterEntry
{
	private readonly List<T> _players = [];

	public IReadOnlyList<T> Players => _players;

	public int Count => _players.Count;

	public void Add(T player) => _players.Add(player);

	public bool Remove(T player) => _players.Remove(player);

	public void Clear() => _players.Clear();

	public bool Contains(Guid playerId) => _players.Any(p => p.PlayerId == playerId);

	public T Find(Guid playerId) => _players.FirstOrDefault(p => p.PlayerId == playerId);

	public T FindByPhone(string phoneNumber) => _players.FirstOrDefault(p => p.PhoneNumber == phoneNumber);
}
