namespace BarBox.Games.Carrom;

/// <summary>
/// Simple interface for input controller to query game state
/// Replaces complex phase manager and input state dependencies
/// </summary>
public interface ICarromGameState
{
	bool CanAcceptInput { get; }

	void OnStrikeExecuted();

	string GetCurrentStateName();

	GameStateManager.GamePhase GetCurrentPhase();
}