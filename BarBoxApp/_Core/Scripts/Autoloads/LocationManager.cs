using Godot;
using System;
using System.Threading.Tasks;

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
	private string _boxApiKey;
	private bool _isConfigLoaded = false;

	// Public typed accessors
	public Guid BoxId => _boxId;
	public string LocationId => _locationId;
	public string CurrentLocationId => _locationId; // Compatibility
	public string BackendUrl => _backendUrl;
	public string BoxApiKey => _boxApiKey;
	public bool IsConfigLoaded => _isConfigLoaded;

	protected override void OnServiceReady()
	{
		LoadEnvironmentConfiguration();
		var apiKeyStatus = string.IsNullOrEmpty(_boxApiKey) ? "NOT SET" : "SET";
		LogInfo($"LocationManager initialized - Location: {_locationId}, BoxId: {_boxId}, ApiKey: {apiKeyStatus}");

		// In production, schedule box verification after services are ready
		if (!OS.HasFeature("editor"))
		{
			CallDeferred(MethodName.VerifyBoxRegistrationDeferred);
		}
	}

	private void LoadEnvironmentConfiguration()
	{
		// Try loading .env file first (development)
		TryLoadDotEnvFile();

		// Load and validate configuration
		_locationId = LoadLocationId();
		_boxId = LoadBoxId();
		_backendUrl = LoadBackendUrl();
		_boxApiKey = LoadBoxApiKey();
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

	private string LoadBoxApiKey()
	{
		var apiKey = System.Environment.GetEnvironmentVariable("BARBOX_API_KEY");

		if (string.IsNullOrEmpty(apiKey))
		{
			// Development fallback
			if (OS.HasFeature("editor"))
			{
				LogError("╔════════════════════════════════════════════════════════════╗");
				LogError("║ CONFIGURATION ERROR: BARBOX_API_KEY is missing!           ║");
				LogError("║                                                            ║");
				LogError("║ All authenticated requests to the backend WILL FAIL!       ║");
				LogError("║                                                            ║");
				LogError("║ Fix by adding to .env.local:                               ║");
				LogError("║   BARBOX_API_KEY=<your-api-key-here>                      ║");
				LogError("║                                                            ║");
				LogError("║ Get API key from backend seed:                             ║");
				LogError("║   curl -X POST http://127.0.0.1:8000/test/seed            ║");
				LogError("╚════════════════════════════════════════════════════════════╝");
				return "";
			}

			throw new System.Exception("BARBOX_API_KEY required in production builds");
		}

		return apiKey;
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

	/// <summary>
	/// Verify box registration asynchronously without blocking startup.
	/// If API key is received (first registration), display it for operator.
	/// </summary>
	private async void VerifyBoxRegistrationDeferred()
	{
		try
		{
			// Wait for EventService to be ready
			await AutoloadBase.StaticDelayAsync(1.0f); // Frame-aware timing

			var eventService = EventService.GetInstance();
			if (eventService == null || !eventService.IsReady)
			{
				LogWarning("EventService not ready - skipping box verification");
				return;
			}

			var result = await eventService.RegisterBoxWithDetailAsync(_boxId, _locationId);

			if (result.IsSuccess(out var response))
			{
				// If API key returned, this is first registration - must save it
				if (!string.IsNullOrEmpty(response.ApiKey))
				{
					LogError("╔════════════════════════════════════════════════════════════╗");
					LogError("║ NEW BOX REGISTERED - ACTION REQUIRED                       ║");
					LogError("║                                                            ║");
					LogError("║ An API key was generated for this box.                    ║");
					LogError("║ Add this line to BarBoxApp/.env.local and RESTART:        ║");
					LogError("║                                                            ║");
					LogError($"║ BARBOX_API_KEY={response.ApiKey}");
					LogError("║                                                            ║");
					LogError("║ WARNING: Key will not be shown again!                     ║");
					LogError("╚════════════════════════════════════════════════════════════╝");
				}
				else
				{
					LogInfo($"Box verified: {_locationId}");
				}
			}
			else if (result.IsFailure(out var error))
			{
				LogWarning($"Box verification failed: {error.Message}");
				LogWarning("Authenticated requests may fail until box is registered");
			}
		}
		catch (Exception ex)
		{
			LogWarning($"Box verification exception: {ex.Message}");
		}
	}

	protected override void OnServiceDestroyed()
	{
	}
}
