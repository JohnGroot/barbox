using Godot;

/// <summary>
/// Compatibility stub for LocationManager
/// Existing code will reference this but functionality is moved to DataStore/SessionManager
/// </summary>
public partial class LocationManager : AutoloadBase
{
	public static LocationManager Instance { get; private set; }

	public static LocationManager GetAutoload()
	{
		return AutoloadBase.GetAutoload<LocationManager>();
	}

	// Basic compatibility for existing code
	public string GetCurrentLocationId()
	{
		var dataStore = DataStore.GetInstance();
		return dataStore?.GetCurrentLocationId() ?? "unknown";
	}

	public string CurrentLocationId => GetCurrentLocationId();

	/// <summary>
	/// Compatibility stub for location data access
	/// </summary>
	public T GetLocationData<T>(string locationId) where T : Resource
	{
		LogInfo($"GetLocationData<{typeof(T).Name}> called for location '{locationId}' (functionality moved to DataStore)");
		return null;
	}

	protected override void OnServiceReady()
	{
		Instance = this;
		LogInfo("LocationManager compatibility stub initialized");
	}

	protected override void OnServiceDestroyed()
	{
		Instance = null;
	}
}