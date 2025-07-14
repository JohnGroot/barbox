using Godot;

/// <summary>
/// Optional interface for games that want explicit lifecycle management.
/// Games can implement this interface OR just emit the appropriate signals - both approaches work with GameHost.
/// 
/// Signal-based protocol (no interface required):
/// - GameStarted() - Game has started
/// - GameEnded() - Game has ended
/// - GamePaused() - Game has paused
/// - GameResumed() - Game has resumed
/// - ScoreChanged(string playerId, int newScore) - Player score changed
/// - PlayerAdded(string playerId) - Player joined the game
/// - PlayerRemoved(string playerId) - Player left the game
/// 
/// Method-based protocol (optional, GameHost will call these if they exist):
/// - StartGame() - Host wants to start the game
/// - EndGame() - Host wants to end the game
/// - PauseGame() - Host wants to pause the game
/// - ResumeGame() - Host wants to resume the game
/// 
/// Signal listening (optional, games can listen for these from GameHost):
/// - HostStartGame() - Host is starting the game
/// - HostEndGame() - Host is ending the game
/// - HostPauseGame() - Host is pausing the game
/// - HostResumeGame() - Host is resuming the game
/// </summary>
public interface IHostedGame
{
	/// <summary>
	/// Called when the GameHost wants to start the game
	/// </summary>
	void StartGame();
	
	/// <summary>
	/// Called when the GameHost wants to end the game
	/// </summary>
	void EndGame();
	
	/// <summary>
	/// Called when the GameHost wants to pause the game
	/// </summary>
	void PauseGame();
	
	/// <summary>
	/// Called when the GameHost wants to resume the game
	/// </summary>
	void ResumeGame();
}