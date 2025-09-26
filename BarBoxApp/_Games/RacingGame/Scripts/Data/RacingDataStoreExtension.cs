using BarBox.Games.Racing;

/// <summary>
/// DataStore extensions for RacingGame - adds racing-specific properties to core DataStore classes
/// These extend the nested classes inside DataStore
/// </summary>
public partial class DataStore
{
	public partial class GlobalUserData
	{
		// Racing game database (cross-location)
		public RaceDatabase RaceDb { get; set; } = new();
	}
}