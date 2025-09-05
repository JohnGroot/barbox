using BarBox.Games.Carrom;

/// <summary>
/// DataStore extensions for CarromGame - adds carrom-specific properties to core DataStore classes
/// These extend the nested classes inside DataStore
/// </summary>
public partial class DataStore
{
	public partial class GlobalUserData
	{
		// Carrom game global data (cross-location)
		public CarromGlobalDataStore Carrom { get; set; } = new();
	}

	public partial class LocalUserData  
	{
		// Carrom game local data (machine-specific)
		public CarromLocalData Carrom { get; set; } = new();
	}
}