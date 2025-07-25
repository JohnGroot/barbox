using Godot;

/// <summary>
/// Interface for standardized game UI integration with the top menu bar
/// Provides a clean contract for games to specify their UI requirements
/// </summary>
public interface IGameUIIntegration
{
	/// <summary>
	/// Gets the display title for the game in the top menu bar
	/// </summary>
	string GetGameTitle();

	/// <summary>
	/// Gets the context buttons this game wants to display in the top menu bar
	/// Return null or empty array for no context buttons
	/// </summary>
	ContextButtonData[] GetContextButtons();

	/// <summary>
	/// Called when the game's UI context is being set up
	/// Use this to perform any additional UI initialization
	/// </summary>
	void OnUIContextSetup();

	/// <summary>
	/// Called when the game's UI context is being torn down
	/// Use this to perform any UI cleanup
	/// </summary>
	void OnUIContextTeardown();

	/// <summary>
	/// Called to update the game's UI state (e.g., button enabled/disabled states)
	/// This may be called periodically or in response to game state changes
	/// </summary>
	void UpdateUIState();
}