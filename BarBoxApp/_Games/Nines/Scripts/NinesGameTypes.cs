#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using BarBox.Core.Gameplay;
using Godot;

namespace BarBox.Games.Nines;

#region Enums

public enum CardRank
{
	Two = 2,
	Three = 3,
	Four = 4,
	Five = 5,
	Six = 6,
	Seven = 7,
	Eight = 8,
	Nine = 9,
	Ten = 10,
	Jack = 11,
	Queen = 12,
	King = 13,
	Ace = 14  // Ace is high
}

public enum CardSuit
{
	Hearts,
	Diamonds,
	Clubs,
	Spades
}

/// <summary>
/// Simplified state machine: 5 phases
/// </summary>
public enum GamePhase
{
	Idle,       // Main menu, waiting to start
	Dealing,    // Dealing initial 9 cards
	TurnActive, // Player is taking their turn (use TurnSubState for details)
	Resolving,  // Processing prediction result, animations
	GameOver    // Win or lose
}

/// <summary>
/// Sub-states within TurnActive phase
/// </summary>
public enum TurnSubState
{
	SelectingStack,      // Player choosing which stack to predict
	SelectingPrediction, // Player choosing Higher/Lower/Same
	SelectingRevive      // Player choosing facedown stack to flip up (after correct Same)
}

public enum PredictionType
{
	Higher,
	Lower,
	Same
}

public enum PredictionResult
{
	Correct,     // Higher/Lower prediction was correct
	Wrong,       // Higher/Lower prediction was wrong
	SameCorrect, // Same prediction was correct - bonus!
	SameWrong    // Same prediction was wrong
}

public enum GameEndReason
{
	Win,     // Deck exhausted with stacks remaining
	Lose,    // All stacks flipped facedown
	Forfeit  // Player chose to forfeit
}

#endregion

#region Core Data Structures

/// <summary>
/// Immutable playing card representation
/// </summary>
public readonly struct PlayingCard
{
	public CardRank Rank { get; init; }
	public CardSuit Suit { get; init; }

	public int Value => (int)Rank;

	public bool IsHigherThan(PlayingCard other) => Value > other.Value;
	public bool IsLowerThan(PlayingCard other) => Value < other.Value;
	public bool IsSameAs(PlayingCard other) => Value == other.Value;

	public bool IsRed => Suit is CardSuit.Hearts or CardSuit.Diamonds;
	public bool IsBlack => Suit is CardSuit.Clubs or CardSuit.Spades;

	public string GetRankDisplay() => Rank switch
	{
		CardRank.Ace => "A",
		CardRank.King => "K",
		CardRank.Queen => "Q",
		CardRank.Jack => "J",
		_ => ((int)Rank).ToString()
	};

	public string GetSuitSymbol() => Suit switch
	{
		CardSuit.Hearts => "\u2665",   // ♥
		CardSuit.Diamonds => "\u2666", // ♦
		CardSuit.Clubs => "\u2663",    // ♣
		CardSuit.Spades => "\u2660",   // ♠
		_ => "?"
	};

	public override string ToString() => $"{GetRankDisplay()}{GetSuitSymbol()}";
}

/// <summary>
/// Encapsulated card stack with controlled mutations
/// </summary>
public class CardStack
{
	private readonly List<PlayingCard> _cards = new();

	public IReadOnlyList<PlayingCard> Cards => _cards;
	public Vector2I GridPosition { get; init; }
	public bool IsFaceUp { get; private set; } = true;
	public int Count => _cards.Count;

	public PlayingCard? TopCard => _cards.Count > 0 ? _cards[^1] : null;
	public bool IsActive => IsFaceUp && _cards.Count > 0;

	public void AddCard(PlayingCard card) => _cards.Add(card);

	public void FlipFaceDown() => IsFaceUp = false;

	public void FlipFaceUp() => IsFaceUp = true;
}

/// <summary>
/// Player data for multi-player support
/// </summary>
public class NinesPlayer : PlayerRosterEntry
{
	public int Credits { get; set; }

	// Analytics tracking
	public int CorrectPredictions { get; set; }
	public int WrongPredictions { get; set; }
	public int SameCorrectPredictions { get; set; }
}

#endregion

#region Backend Response Types

/// <summary>
/// Response from backend jackpot query endpoint.
/// Contains the last win timestamp used for time-based jackpot calculation.
/// </summary>
public class NinesJackpotResponse
{
	[JsonPropertyName("venue_name")]
	public string VenueName { get; set; } = string.Empty;

	[JsonPropertyName("last_win_timestamp")]
	public DateTime? LastWinTimestamp { get; set; }

	[JsonPropertyName("last_winner_name")]
	public string? LastWinnerName { get; set; }

	[JsonPropertyName("last_jackpot_amount")]
	public int? LastJackpotAmount { get; set; }
}

#endregion
