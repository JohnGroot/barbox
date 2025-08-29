using Godot;

namespace BarBox.Games.MiningGame
{
	/// <summary>
	/// Resource for calculating upgrade costs with exponential scaling.
	/// Used by the mining game configuration system to determine gem costs for upgrades.
	/// </summary>
	[GlobalClass]
	public partial class UpgradeCostData : Resource
	{
		/// <summary>
		/// Base cost for level 0 upgrades.
		/// </summary>
		[Export] public int BaseCost { get; set; } = 100;
		
		/// <summary>
		/// Multiplier applied per level for exponential cost scaling.
		/// </summary>
		[Export] public float CostMultiplier { get; set; } = 1.5f;
		
		/// <summary>
		/// Calculates the cost for upgrading to the specified level.
		/// </summary>
		/// <param name="currentLevel">The current upgrade level (0-based)</param>
		/// <returns>The gem cost required for the upgrade</returns>
		public int CalculateCost(int currentLevel)
		{
			return (int)(BaseCost * Mathf.Pow(CostMultiplier, currentLevel));
		}
	}
}