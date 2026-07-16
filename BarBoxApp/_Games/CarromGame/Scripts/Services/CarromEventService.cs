using System.Collections.Generic;
using System.Threading.Tasks;
using BarBox.Core.Autoloads;
using LightResults;

namespace BarBox.Games.Carrom;

/// <summary>
/// Carrom game-specific event service.
/// Provides a type-safe method for emitting carrom results through the shared,
/// guarded GameEventServiceBase path, replacing the previous raw fire-and-forget
/// SessionEventService.EmitEventAsync call in game logic.
/// </summary>
public class CarromEventService : GameEventServiceBase
{
	private const string EVENT_ROUND_FINISH = "carrom/round_finish";

	public CarromEventService(SessionEventService eventService = null)
		: base(eventService)
	{
	}

	public async Task<Result<bool>> EmitRoundFinishAsync(string mode, string winner, Dictionary<string, int> scores)
	{
		if (string.IsNullOrEmpty(mode))
		{
			return Result.Failure<bool>(ValidationMessages.Required("Mode"));
		}

		var payload = new
		{
			mode = mode,
			winner = winner ?? string.Empty,
			scores = scores ?? new Dictionary<string, int>(),
		};

		return await SubmitResultAsync(EVENT_ROUND_FINISH, payload);
	}
}
