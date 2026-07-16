using Godot;

namespace BarBox.Games.MiningGame;

/// <summary>
/// Resource for calculating upgrade costs with exponential scaling.
/// Used by the mining game configuration system to determine gem costs for upgrades.
/// </summary>
[GlobalClass]
public partial class UpgradeCostData : Resource
{
	[Export]
	public int BaseCost { get; set; } = 100;

	[Export]
	public float CostMultiplier { get; set; } = 1.5f;

	/// <summary>
	/// Calculates the cost for upgrading to the specified level.
	/// </summary>
	/// <param name="currentLevel">The current upgrade level (0-based)</param>
	/// <returns>The gem cost required for the upgrade</returns>
	public int CalculateCost(int currentLevel)
	{
		if (currentLevel < 0)
		{
			throw new System.ArgumentOutOfRangeException(nameof(currentLevel), "Level cannot be negative");
		}

		return (int)(BaseCost * Mathf.Pow(CostMultiplier, currentLevel));
	}
}
