using BarBox.Games.MiningGame;

/// <summary>
/// DataStore extensions for MiningGame - adds mining-specific properties to core DataStore classes
/// These extend the nested classes inside DataStore
/// </summary>
public partial class DataStore
{
	public partial class GlobalUserData
	{
		// Mining game global data (cross-location) - using the original class name for compatibility
		public MiningGlobalDataStore Mining { get; set; } = new();
	}

	public partial class LocalUserData  
	{
		// Mining game local data (machine-specific)
		public MiningLocalData Mining { get; set; } = new();
	}
}