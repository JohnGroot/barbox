using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// Carrom game-specific event service
/// Provides type-safe methods for emitting carrom events
/// </summary>
public partial class CarromEventService : Node
{
	private EventService _eventService;

	public override void _Ready()
	{
		_eventService = EventService.GetInstance();
		if (_eventService == null)
		{
			GD.PrintErr("CarromEventService: EventService not found");
		}
	}

	/// <summary>
	/// Emit round start event
	/// </summary>
	public async Task<Result<bool>> EmitRoundStartAsync(string mode, string[] playerIds)
	{
		if (_eventService == null)
			return Result<bool>.Failure("EventService not initialized");

		var payload = new
		{
			mode = mode,
			players = playerIds
		};

		return await _eventService.EmitEventAsync("carrom/round_start", payload);
	}

	/// <summary>
	/// Emit piece pocketed event
	/// </summary>
	public async Task<Result<bool>> EmitPiecePocketedAsync(string pieceType, int playerIndex, int points)
	{
		if (_eventService == null)
			return Result<bool>.Failure("EventService not initialized");

		var payload = new
		{
			piece_type = pieceType,
			player_idx = playerIndex,
			points = points
		};

		return await _eventService.EmitEventAsync("carrom/piece_pocketed", payload);
	}

	/// <summary>
	/// Emit round finish event
	/// </summary>
	public async Task<Result<bool>> EmitRoundFinishAsync(
		string mode,
		string winnerId,
		Dictionary<string, int> scores)
	{
		if (_eventService == null)
			return Result<bool>.Failure("EventService not initialized");

		var payload = new
		{
			mode = mode,
			winner = winnerId,
			scores = scores
		};

		return await _eventService.EmitEventAsync("carrom/round_finish", payload);
	}

	/// <summary>
	/// Get carrom leaderboard from backend
	/// </summary>
	public async Task<Result<CarromLeaderboardData>> GetLeaderboardAsync(
		string metric = "total_score",
		int limit = 10)
	{
		// TODO: Implement HTTP GET to /game/carrom/leaderboard
		// For now, return placeholder
		await Task.Delay(1);
		return Result<CarromLeaderboardData>.Failure("Not implemented");
	}
}

/// <summary>
/// Carrom leaderboard data structure
/// </summary>
public class CarromLeaderboardData
{
	public string Metric { get; set; }
	public List<CarromLeaderboardEntry> Leaderboard { get; set; }
}

/// <summary>
/// Carrom leaderboard entry
/// </summary>
public class CarromLeaderboardEntry
{
	public Guid PlayerId { get; set; }
	public string Username { get; set; }
	public int TotalScore { get; set; }
	public int? TotalWins { get; set; }
}
