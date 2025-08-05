/// <summary>
/// Simple interface for input controller to query game state
/// Replaces complex phase manager and input state dependencies
/// </summary>
public interface ICarromGameState
{
	/// <summary>
	/// Whether input can be accepted (true only in Ready state)
	/// </summary>
	bool CanAcceptInput { get; }
	
	/// <summary>
	/// Called when a strike is executed to begin physics monitoring
	/// </summary>
	void OnStrikeExecuted();
	
	/// <summary>
	/// Get current state name for debugging
	/// </summary>
	string GetCurrentStateName();
}