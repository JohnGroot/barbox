using System;
using System.Threading.Tasks;
using Chickensoft.GoDotTest;
using Godot;

namespace BarBox.Tests.Fixtures;

/// <summary>
/// Base for game-flow integration tests that need one activity session
/// created once for the whole suite and closed at the end. Extracted from
/// Mining/Nines/Racing's near-identical SetupAll/CleanupAll boilerplate
/// (health check, get SessionEventService, create session against the
/// seeded test box/player, close it after the suite). Carrom's session is
/// multiplayer and created per-test rather than once upfront, so it doesn't
/// fit this shape and stays on <see cref="BackendTestBase"/> directly.
/// </summary>
public abstract class GameSessionTestBase : BackendTestBase
{
	/// <summary>The game tag activity sessions are created against.</summary>
	protected abstract string GameTag { get; }

	protected Guid TestSessionId { get; private set; }
	protected SessionEventService EventService { get; private set; }

	protected GameSessionTestBase(Node testScene) : base(testScene)
	{
	}

	/// <summary>
	/// Re-declared (rather than relying on inherited [Setup] firing
	/// automatically) to match the established pattern in this test suite -
	/// see CreditServiceTests/FailureScenarioTestBase for the same idiom.
	/// </summary>
	[Setup]
	public new void SetupTestIdentifiers()
	{
		base.SetupTestIdentifiers();
	}

	[SetupAll]
	public async Task SetupGameSession()
	{
		await SetupBackendConnection();

		EventService = GetEventService();
		if (EventService == null)
			return;

		var (playerId, _, _, _) = TestHelpers.GetSeededTestPlayer(1);
		var sessionResult = await EventService.CreateActivitySessionAsync(
			TestHelpers.SeededTestBoxId, playerId, GameTag);

		if (sessionResult.IsSuccess(out var sessionId))
		{
			TestSessionId = sessionId;
			TestHelpers.LogTestInfo($"{GameTag} session created: {TestSessionId}");
		}
		else if (sessionResult.IsFailure(out var error))
		{
			TestHelpers.LogTestWarning($"Failed to create {GameTag} session: {error.Message}");
		}
	}

	[CleanupAll]
	public async Task CleanupGameSession()
	{
		if (EventService != null && TestSessionId != Guid.Empty)
		{
			var result = await EventService.CloseActivitySessionAsync(TestSessionId);
			if (result.IsSuccess(out var _))
				TestHelpers.LogTestInfo($"{GameTag} session closed successfully");
			else if (result.IsFailure(out var error))
				TestHelpers.LogTestWarning($"Session close failed: {error.Message}");
		}
	}
}
