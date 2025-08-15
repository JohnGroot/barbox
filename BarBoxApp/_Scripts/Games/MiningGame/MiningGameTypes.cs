namespace BarBox.Games.MiningGame
{
	public enum GemType
	{
		Ruby,
		Sapphire,
		Emerald,
		Diamond,
		Amethyst
	}

	public enum UpgradeType
	{
		Capacity,       // Max gem storage capacity
		MiningSpeed,    // Mining tick frequency (reduces time)
		MiningAmount,   // Gems per tick
		CreditCharges   // Maximum credit charges available
	}

	public enum UpgradeTier
	{
		Tier1 = 1,      // Levels 1-5: Location gem only
		Tier2 = 2,      // Levels 6-10: Location gem + 1 other gem
		Tier3 = 3       // Levels 11-15: Location gem + 2 other gems
	}
}