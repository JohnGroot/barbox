using Godot;
using System;
using System.Threading.Tasks;

namespace BarBox.Core.Autoloads;

/// <summary>
/// Environment configuration and location management service
/// Loads configuration from .env files and environment variables
/// Provides typed, validated accessors for all configuration values
/// </summary>
public partial class LocationManager : AutoloadBase
{
	// Cached configuration values
	private Guid _boxId;
	private string _boxName;
	private string _venueName;
	private string _backendUrl;
	private string _boxApiKey;
	private bool _isConfigLoaded = false;

	// Public typed accessors

	/// <summary>
	/// Box ID (UUID) - Required for backend authentication and database foreign keys
	/// This is the secure, globally unique identifier for this physical machine
	/// </summary>
	public Guid BoxId => _boxId;

	/// <summary>
	/// Box Name (human-readable) - Used for display and logging
	/// Example: "besties_box_1", "arcade_terminal_2"
	/// </summary>
	public string BoxName => _boxName;

	/// <summary>
	/// Venue Name (human-readable location identifier) - Used for venue-scoped data
	/// Example: "best_intentions", "johnnys_arcade"
	/// Mining progress and other venue-scoped data follows players across machines at the same venue
	/// </summary>
	public string VenueName => _venueName;

	public string BackendUrl => _backendUrl;
	public string BoxApiKey => _boxApiKey;
	public bool IsConfigLoaded => _isConfigLoaded;

	protected override void OnServiceReady()
	{
		LoadEnvironmentConfiguration();
		var apiKeyStatus = string.IsNullOrEmpty(_boxApiKey) ? "NOT SET" : "SET";
		LogInfo($"LocationManager initialized - Venue: {_venueName}, BoxName: {_boxName}, BoxId: {_boxId}, ApiKey: {apiKeyStatus}");

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
		_venueName = LoadVenueName();
		_boxName = LoadBoxName();
		_boxId = LoadBoxId();
		_backendUrl = LoadBackendUrl();
		_boxApiKey = LoadBoxApiKey();
		_isConfigLoaded = true;
	}

	private void TryLoadDotEnvFile()
	{
		string[] envPaths = ["res://.env.local", "res://.env", "user://.env.local"];

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

	private string LoadVenueName()
	{
		var venueName = System.Environment.GetEnvironmentVariable("BARBOX_VENUE_NAME");

		// Development fallback only - production must configure explicitly
		if (string.IsNullOrEmpty(venueName))
		{
			if (OS.HasFeature("editor"))
			{
				const string DEV_DEFAULT = "default";
				LogWarning($"BARBOX_VENUE_NAME not set - using development default: {DEV_DEFAULT}");
				LogWarning("Set BARBOX_VENUE_NAME in .env.local for venue-specific configuration");
				return DEV_DEFAULT;
			}

			throw new InvalidOperationException("BARBOX_VENUE_NAME environment variable is required in production builds");
		}

		return venueName;
	}

	private string LoadBoxName()
	{
		var boxName = System.Environment.GetEnvironmentVariable("BARBOX_BOX_NAME");

		// Development fallback only - production must configure explicitly
		if (string.IsNullOrEmpty(boxName))
		{
			if (OS.HasFeature("editor"))
			{
				const string DEV_DEFAULT = "default";
				LogWarning($"BARBOX_BOX_NAME not set - using development default: {DEV_DEFAULT}");
				LogWarning("Set BARBOX_BOX_NAME in .env.local for box-specific configuration");
				return DEV_DEFAULT;
			}

			throw new InvalidOperationException("BARBOX_BOX_NAME environment variable is required in production builds");
		}

		return boxName;
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

			throw new InvalidOperationException("BARBOX_BOX_ID required in production builds");
		}

		if (!Guid.TryParse(boxIdStr, out var boxId))
		{
			throw new InvalidOperationException($"BARBOX_BOX_ID must be valid GUID, got: {boxIdStr}");
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

			throw new InvalidOperationException("BARBOX_API_KEY required in production builds");
		}

		return apiKey;
	}

	/// <summary>
	/// Get venue name for venue-scoped data
	/// </summary>
	public string GetVenueName() => _venueName;

	public static LocationManager GetAutoload()
	{
		return AutoloadBase.GetAutoload<LocationManager>();
	}

	/// <summary>
	/// Displays one-time API key on first registration - save to .env.local
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

			var result = await eventService.RegisterBoxWithDetailAsync(_boxId, _venueName);

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
					LogInfo($"Box verified: {_venueName} (BoxName: {_boxName})");
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
