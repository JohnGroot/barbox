using System;
using System.Threading.Tasks;
using BarBox.Core.Autoloads;
using LightResults;

namespace BarBox.Games.Nines;

/// <summary>
/// Nines game-specific event service.
/// Emits nines results through the shared, guarded GameEventServiceBase path
/// (replacing the previous raw SessionEventService.EmitEventAsync call in game logic).
/// </summary>
public class NinesEventService : GameEventServiceBase
{
	private const string EVENT_JACKPOT_WON = "nines/jackpot_won";

	public NinesEventService(SessionEventService eventService = null)
		: base(eventService)
	{
	}

	/// <summary>
	/// Record a jackpot win. Note the Nines convention: <paramref name="playerId"/>
	/// is the winner's PHONE NUMBER (E.164 string), not a UUID — the backend
	/// leaderboard join resolves it against player.phone_number.
	/// </summary>
	public async Task<Result<bool>> EmitJackpotWonAsync(string venueName, string playerId, string playerName, int jackpotAmount)
	{
		if (string.IsNullOrEmpty(venueName))
		{
			return Result.Failure<bool>(ValidationMessages.Required("Venue name"));
		}

		var payload = new
		{
			venue_name = venueName,
			player_id = playerId ?? "unknown",
			player_name = playerName ?? "Unknown",
			jackpot_amount = jackpotAmount,
			timestamp = DateTime.UtcNow.ToString("o"),
		};

		return await SubmitResultAsync(EVENT_JACKPOT_WON, payload);
	}
}
