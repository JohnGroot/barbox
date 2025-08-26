using BarBox.Games.Racing;

/// <summary>
/// DataStore extensions for RacingGame - adds racing-specific properties to core DataStore classes
/// These extend the nested classes inside DataStore
/// </summary>
public partial class DataStore
{
	public partial class GlobalUserData
	{
		// Racing game global data (cross-location)
		public RacingGlobalDataStore Racing { get; set; } = new();
	}

	public partial class LocalUserData  
	{
		// Racing game local data (machine-specific)
		public RacingLocalData Racing { get; set; } = new();
	}
}