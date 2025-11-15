using Godot;
using System;

/// <summary>
/// Environment configuration and location management service
/// Loads configuration from .env files and environment variables
/// Provides typed, validated accessors for all configuration values
/// </summary>
public partial class LocationManager : AutoloadBase
{
	// Cached configuration values
	private Guid _boxId;
	private string _locationId;
	private string _backendUrl;
	private bool _isConfigLoaded = false;

	// Public typed accessors
	public Guid BoxId => _boxId;
	public string LocationId => _locationId;
	public string CurrentLocationId => _locationId; // Compatibility
	public string BackendUrl => _backendUrl;
	public bool IsConfigLoaded => _isConfigLoaded;

	protected override void OnServiceReady()
	{
		LoadEnvironmentConfiguration();
		LogInfo($"LocationManager initialized - Location: {_locationId}, BoxId: {_boxId}");
	}

	private void LoadEnvironmentConfiguration()
	{
		// Try loading .env file first (development)
		TryLoadDotEnvFile();

		// Load and validate configuration
		_locationId = LoadLocationId();
		_boxId = LoadBoxId();
		_backendUrl = LoadBackendUrl();
		_isConfigLoaded = true;
	}

	private void TryLoadDotEnvFile()
	{
		var envPaths = new[] { "res://.env.local", "res://.env", "user://.env.local" };

		foreach (var path in envPaths)
		{
			if (FileAccess.FileExists(path))
			{
				LoadEnvFile(path);
				LogInfo($"Loaded environment from: {path}");
				return;
			}
		}

		LogInfo("No .env file found, using system environment variables");
	}

	private void LoadEnvFile(string path)
	{
		using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
		if (file == null)
		{
			LogWarning($"Failed to open .env file at: {path}");
			return;
		}

		while (!file.EofReached())
		{
			var line = file.GetLine().Trim();

			// Skip comments and empty lines
			if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
				continue;

			// Parse KEY=VALUE format
			var parts = line.Split('=', 2);
			if (parts.Length == 2)
			{
				var key = parts[0].Trim();
				var value = parts[1].Trim().Trim('"', '\'');
				System.Environment.SetEnvironmentVariable(key, value);
			}
		}
	}

	private string LoadLocationId()
	{
		return System.Environment.GetEnvironmentVariable("BARBOX_LOCATION_ID") ??
		       System.Environment.MachineName.ToLowerInvariant().Replace("-", "_");
	}

	private Guid LoadBoxId()
	{
		var boxIdStr = System.Environment.GetEnvironmentVariable("BARBOX_BOX_ID");

		if (string.IsNullOrEmpty(boxIdStr))
		{
			// Development fallback
			if (OS.HasFeature("editor"))
			{
				var devGuid = new Guid("00000000-0000-0000-0000-000000000001");
				LogWarning($"BARBOX_BOX_ID not set, using dev fallback: {devGuid}");
				return devGuid;
			}

			throw new System.Exception("BARBOX_BOX_ID required in production builds");
		}

		if (!Guid.TryParse(boxIdStr, out var boxId))
		{
			throw new System.Exception($"BARBOX_BOX_ID must be valid GUID, got: {boxIdStr}");
		}

		return boxId;
	}

	private string LoadBackendUrl()
	{
		return System.Environment.GetEnvironmentVariable("BARBOX_BACKEND_URL") ??
		       "http://localhost:8000";
	}

	// Keep compatibility method
	public string GetCurrentLocationId() => _locationId;

	/// <summary>
	/// Compatibility stub for location data access
	/// </summary>
	public T GetLocationData<T>(string locationId) where T : Resource
	{
		LogInfo($"GetLocationData<{typeof(T).Name}> called for location '{locationId}' (functionality moved to DataStore)");
		return null;
	}

	public static LocationManager GetAutoload()
	{
		return AutoloadBase.GetAutoload<LocationManager>();
	}

	protected override void OnServiceDestroyed()
	{
	}
}
