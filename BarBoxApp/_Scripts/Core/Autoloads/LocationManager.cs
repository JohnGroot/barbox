using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Platform-level location management service providing centralized access to location-specific data.
/// Designed as abstraction layer for future backend integration while supporting current resource-based configs.
/// All games should access location data through this service rather than directly from registries.
/// </summary>
public partial class LocationManager : AutoloadBase
{
	[Signal] public delegate void LocationChangedEventHandler(string newLocationId, string previousLocationId);
	[Signal] public delegate void LocationDataUpdatedEventHandler(string locationId, string dataType);

	public static LocationManager Instance { get; private set; }

	private string _currentLocationId = "default";
	private Dictionary<string, Dictionary<string, Resource>> _locationDataCache = new();
	private Dictionary<string, ILocationDataProvider> _dataProviders = new();
	private GameHost _gameHost;
	private UserManager _userManager;

	/// <summary>
	/// Get the current active location ID for the platform
	/// </summary>
	public string CurrentLocationId => _currentLocationId;

	protected override void OnServiceReady()
	{
		Instance = this;
		
		// Connect to other platform services
		_gameHost = GetAutoload<GameHost>();
		_userManager = GetAutoload<UserManager>();
		
		// Register default data provider for resource-based configs
		RegisterDataProvider("default", new ResourceLocationDataProvider());
		
		// Load user's last location preference if available
		LoadUserLocationPreference();
		
		LogInfo($"LocationManager initialized with current location: {_currentLocationId}");
	}

	/// <summary>
	/// Get location-specific data of type T for the given location ID.
	/// This is the primary method all games should use for location data access.
	/// </summary>
	/// <typeparam name="T">Resource type to retrieve</typeparam>
	/// <param name="locationId">Location identifier</param>
	/// <returns>Location-specific resource or null if not found</returns>
	public T GetLocationData<T>(string locationId) where T : Resource
	{
		if (string.IsNullOrEmpty(locationId))
			locationId = _currentLocationId;

		// Check cache first
		if (_locationDataCache.TryGetValue(locationId, out var locationCache))
		{
			if (locationCache.TryGetValue(typeof(T).Name, out var cachedResource))
			{
				return cachedResource as T;
			}
		}

		// Get from data provider
		var provider = GetDataProviderForLocation(locationId);
		var data = provider?.GetLocationData<T>(locationId);
		
		if (data != null)
		{
			CacheLocationData(locationId, data);
		}
		
		return data;
	}

	/// <summary>
	/// Set location-specific data for the given location ID.
	/// Future backend integration point for data persistence.
	/// </summary>
	/// <typeparam name="T">Resource type to store</typeparam>
	/// <param name="locationId">Location identifier</param>
	/// <param name="data">Resource data to store</param>
	public void SetLocationData<T>(string locationId, T data) where T : Resource
	{
		if (string.IsNullOrEmpty(locationId) || data == null)
			return;

		// Cache the data
		CacheLocationData(locationId, data);

		// Update through data provider
		var provider = GetDataProviderForLocation(locationId);
		provider?.SetLocationData(locationId, data);

		// Emit update signal
		EmitSignal(SignalName.LocationDataUpdated, locationId, typeof(T).Name);
	}

	/// <summary>
	/// Switch to a different location, updating platform-wide location context.
	/// Validates location exists and emits change signals.
	/// </summary>
	/// <param name="locationId">Location to switch to</param>
	/// <returns>True if location switch succeeded</returns>
	public bool SwitchToLocation(string locationId)
	{
		if (string.IsNullOrEmpty(locationId) || locationId == _currentLocationId)
			return false;

		// Validate location exists
		if (!IsLocationAvailable(locationId))
		{
			LogError($"Cannot switch to location '{locationId}' - location not available");
			return false;
		}

		var previousLocation = _currentLocationId;
		_currentLocationId = locationId;

		// Save user preference
		SaveUserLocationPreference();

		// Emit location change signal
		EmitSignal(SignalName.LocationChanged, _currentLocationId, previousLocation);
		
		LogInfo($"Switched location from '{previousLocation}' to '{_currentLocationId}'");
		return true;
	}

	/// <summary>
	/// Get all available location IDs from all registered data providers.
	/// </summary>
	/// <returns>Array of available location identifiers</returns>
	public string[] GetAvailableLocations()
	{
		var locations = new HashSet<string>();
		
		foreach (var provider in _dataProviders.Values)
		{
			var providerLocations = provider.GetAvailableLocations();
			foreach (var location in providerLocations)
			{
				locations.Add(location);
			}
		}
		
		return locations.ToArray();
	}

	/// <summary>
	/// Check if a location ID is available through any registered data provider.
	/// </summary>
	/// <param name="locationId">Location to check</param>
	/// <returns>True if location is available</returns>
	public bool IsLocationAvailable(string locationId)
	{
		if (string.IsNullOrEmpty(locationId))
			return false;

		foreach (var provider in _dataProviders.Values)
		{
			if (provider.HasLocation(locationId))
				return true;
		}

		return false;
	}

	/// <summary>
	/// Register a data provider for location data access.
	/// Future backend integration point.
	/// </summary>
	/// <param name="providerName">Unique provider identifier</param>
	/// <param name="provider">Data provider implementation</param>
	public void RegisterDataProvider(string providerName, ILocationDataProvider provider)
	{
		if (string.IsNullOrEmpty(providerName) || provider == null)
			return;

		_dataProviders[providerName] = provider;
		LogInfo($"Registered location data provider: {providerName}");
	}

	/// <summary>
	/// Clear cached location data to force refresh on next access.
	/// Useful for development and testing.
	/// </summary>
	/// <param name="locationId">Location to clear, or null for all locations</param>
	public void ClearLocationCache(string locationId = null)
	{
		if (string.IsNullOrEmpty(locationId))
		{
			_locationDataCache.Clear();
			LogInfo("Cleared all location data cache");
		}
		else if (_locationDataCache.ContainsKey(locationId))
		{
			_locationDataCache.Remove(locationId);
			LogInfo($"Cleared location data cache for: {locationId}");
		}
	}

	/// <summary>
	/// Get debug information about current location state.
	/// </summary>
	/// <returns>Debug information string</returns>
	public string GetDebugInfo()
	{
		var info = new System.Text.StringBuilder();
		info.AppendLine($"Current Location: {_currentLocationId}");
		info.AppendLine($"Available Locations: {string.Join(", ", GetAvailableLocations())}");
		info.AppendLine($"Registered Providers: {string.Join(", ", _dataProviders.Keys)}");
		info.AppendLine($"Cached Locations: {_locationDataCache.Count}");
		
		foreach (var kvp in _locationDataCache)
		{
			info.AppendLine($"  {kvp.Key}: {kvp.Value.Count} cached resources");
		}
		
		return info.ToString();
	}

	// Private helper methods

	private void CacheLocationData<T>(string locationId, T data) where T : Resource
	{
		if (!_locationDataCache.ContainsKey(locationId))
		{
			_locationDataCache[locationId] = new Dictionary<string, Resource>();
		}
		
		_locationDataCache[locationId][typeof(T).Name] = data;
	}

	private ILocationDataProvider GetDataProviderForLocation(string locationId)
	{
		// For now, use default provider for all locations
		// Future: Route to specific providers based on location or backend availability
		return _dataProviders.GetValueOrDefault("default");
	}

	private void LoadUserLocationPreference()
	{
		// Load from user metadata if available
		if (_userManager != null)
		{
			var currentUser = _userManager.GetCurrentUser();
			if (currentUser != null && currentUser.HasMeta("preferred_location"))
			{
				var preferredLocation = currentUser.GetMeta("preferred_location").AsString();
				if (IsLocationAvailable(preferredLocation))
				{
					_currentLocationId = preferredLocation;
				}
			}
		}
	}

	private void SaveUserLocationPreference()
	{
		// Save to user metadata if available
		if (_userManager != null)
		{
			var currentUser = _userManager.GetCurrentUser();
			if (currentUser != null)
			{
				currentUser.SetMeta("preferred_location", _currentLocationId);
			}
		}
	}

	protected override void OnServiceDestroyed()
	{
		// Clear all cached data
		_locationDataCache.Clear();
		_dataProviders.Clear();
		
		Instance = null;
	}
}

/// <summary>
/// Interface for location data providers.
/// Abstraction layer for future backend integration.
/// </summary>
public interface ILocationDataProvider
{
	T GetLocationData<T>(string locationId) where T : Resource;
	void SetLocationData<T>(string locationId, T data) where T : Resource;
	string[] GetAvailableLocations();
	bool HasLocation(string locationId);
}

/// <summary>
/// Default implementation using Godot Resource system.
/// Current resource-based configuration approach.
/// </summary>
public partial class ResourceLocationDataProvider : ILocationDataProvider
{
	private Dictionary<string, Resource> _registryCache = new();

	public T GetLocationData<T>(string locationId) where T : Resource
	{
		// Try to find a registry that can provide this data type for this location
		// This will be enhanced when we implement the full hierarchy
		
		// TODO: Mining game integration will be added here
		/*
		if (typeof(T) == typeof(MiningLocationConfig))
		{
			return GetMiningLocationConfig(locationId) as T;
		}
		
		if (typeof(T) == typeof(MiningGameConfig))
		{
			return GetMiningGameConfig(locationId) as T;
		}
		
		if (typeof(T) == typeof(MiningUITheme))
		{
			return GetMiningUITheme(locationId) as T;
		}
		*/
		
		return null;
	}

	public void SetLocationData<T>(string locationId, T data) where T : Resource
	{
		// For now, resource-based provider is read-only
		// Future backend provider will implement persistence
		GD.Print($"[ResourceLocationDataProvider] SetLocationData not implemented for resource provider");
	}

	public string[] GetAvailableLocations()
	{
		// This will be enhanced when MiningGameRegistry is updated
		return new[] { "default", "mountain", "cave", "ocean" };
	}

	public bool HasLocation(string locationId)
	{
		var availableLocations = GetAvailableLocations();
		return Array.Exists(availableLocations, loc => loc == locationId);
	}

	// Helper methods - will be enhanced with full hierarchy implementation
	
	// TODO: These methods are placeholders for future Mining Game integration
	/*
	private MiningLocationConfig GetMiningLocationConfig(string locationId)
	{
		// Placeholder - will integrate with enhanced MiningGameRegistry
		return new MiningLocationConfig { LocationId = locationId };
	}

	private MiningGameConfig GetMiningGameConfig(string locationId)
	{
		// Placeholder - will integrate with enhanced MiningGameRegistry
		return new MiningGameConfig();
	}

	private MiningUITheme GetMiningUITheme(string locationId)
	{
		// Placeholder - will integrate with enhanced MiningGameRegistry
		return new MiningUITheme();
	}
	*/
}