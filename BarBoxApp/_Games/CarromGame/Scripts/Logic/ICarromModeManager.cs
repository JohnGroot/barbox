/// <summary>
/// Simple interface for mode manager settlement processing
/// Used by CarromGameStateMachine for immediate settlement processing
/// </summary>
public interface ICarromModeManager
{
	/// <summary>
	/// Process settlement immediately and synchronously
	/// No async operations, no complex state management
	/// </summary>
	void ProcessSettlement();
}