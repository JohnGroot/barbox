using Godot;
using System;
using System.Threading;
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

	// Cached URI to avoid repeated parsing and potential exceptions
	private Uri _backendUri;

	/// <summary>
	/// Backend host extracted from BackendUrl
	/// Used by BackendManager for connection
	/// </summary>
	public string BackendHost => _backendUri?.Host ?? "127.0.0.1";

	/// <summary>
	/// Backend port extracted from BackendUrl
	/// Uses default port 8000 if not specified or URL is invalid
	/// </summary>
	public int BackendPort => _backendUri?.Port ?? 8000;

	/// <summary>
	/// Whether HTTPS should be used (determined from BackendUrl scheme)
	/// </summary>
	public bool UseHttps => _backendUri != null &&
		_backendUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Whether test mode is enabled (from BARBOX_TEST_MODE env var)
	/// </summary>
	public bool IsTestMode => !string.IsNullOrEmpty(
		System.Environment.GetEnvironmentVariable("BARBOX_TEST_MODE"));

	// Note: .env loading moved to ApplicationBootstrap.OnServiceEnterTree()
	// This ensures env vars are set before ANY service (including LocationManager) initializes

	protected override Task OnServiceInitializeAsync(CancellationToken cancellationToken = default)
	{
		// Load configuration values during async init phase
		// Env vars are already set by ApplicationBootstrap.OnServiceEnterTree()
		LoadEnvironmentConfiguration();
		return Task.CompletedTask;
	}

	protected override void OnServiceReady()
	{
		// Config already loaded in OnServiceInitializeAsync
		// Show masked API key preview for debugging (helps confirm correct key)
		var apiKeyStatus = string.IsNullOrEmpty(_boxApiKey)
			? "NOT SET"
			: _boxApiKey.Length > 12
				? $"{_boxApiKey[..8]}..."
				: "SET (short)";
		LogInfo($"LocationManager initialized - Venue: {_venueName}, BoxName: {_boxName}, BoxId: {_boxId}, ApiKey: {apiKeyStatus}");

		// In production, schedule box verification after services are ready
		if (!OS.HasFeature("editor"))
		{
			CallDeferred(MethodName.VerifyBoxRegistrationDeferred);
		}
	}

	private void LoadEnvironmentConfiguration()
	{
		// .env file already loaded in OnServiceEnterTree() - just load config values
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
		var url = System.Environment.GetEnvironmentVariable("BARBOX_BACKEND_URL") ??
		          "http://localhost:8000";

		// Validate and cache the URI
		try
		{
			_backendUri = new Uri(url);
		}
		catch (UriFormatException ex)
		{
			LogError($"Invalid BARBOX_BACKEND_URL format: {url}");
			LogError($"URL parse error: {ex.Message}");
			LogError("Using fallback: http://localhost:8000");

			url = "http://localhost:8000";
			_backendUri = new Uri(url);
		}

		return url;
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
				// Detect first-time registration vs re-registration via warning message
				// First registration: warning starts with "Save this API key..."
				// Re-registration: warning contains "already registered" or "exists with different"
				var isFirstRegistration = !string.IsNullOrEmpty(response.Warning)
					&& response.Warning.StartsWith("Save", StringComparison.OrdinalIgnoreCase);

				if (isFirstRegistration && !string.IsNullOrEmpty(response.ApiKey))
				{
					// First registration - show action required banner with masked key
					var maskedKey = response.ApiKey.Length > 12
						? $"{response.ApiKey[..8]}...{response.ApiKey[^4..]}"
						: "********";

					LogError("╔════════════════════════════════════════════════════════════╗");
					LogError("║ NEW BOX REGISTERED - ACTION REQUIRED                       ║");
					LogError("║                                                            ║");
					LogError("║ An API key was generated for this box.                    ║");
					LogError("║ Add this line to BarBoxApp/.env.local and RESTART:        ║");
					LogError("║                                                            ║");
					LogError($"║ BARBOX_API_KEY={maskedKey}");
					LogError("║ (Full key shown in Godot Output console below)            ║");
					LogError("╚════════════════════════════════════════════════════════════╝");

					// Print full key to console only (not persisted logs)
					GD.Print($"BARBOX_API_KEY={response.ApiKey}");
				}
				else
				{
					// Re-registration - just log verification with masked key
					var maskedKey = !string.IsNullOrEmpty(response.ApiKey) && response.ApiKey.Length > 12
						? $"{response.ApiKey[..8]}...{response.ApiKey[^4..]}"
						: "***";
					LogInfo($"Box verified: {_venueName} (BoxName: {_boxName}), key: {maskedKey}");

					// Log warning if name/tag mismatch was detected
					if (!string.IsNullOrEmpty(response.Warning) && response.Warning.Contains("different"))
					{
						LogWarning($"Box registration note: {response.Warning}");
					}
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
