namespace BarBox.Games.Carrom;

/// <summary>
/// Simple interface for mode manager settlement processing
/// Used by CarromGameStateMachine for immediate settlement processing
/// </summary>
public interface ICarromModeManager
{
	void ProcessSettlement();
}