using Godot;
using System;
using System.Threading.Tasks;

/// <summary>
/// Manages local FastAPI backend lifecycle
/// Ensures backend is running before game operations
/// Blocks app initialization if backend fails to start
/// </summary>
public partial class BackendManager : AutoloadBase
{
	private const string BACKEND_START_SCRIPT = "/Users/johngroot/Dev/barbox/BarBoxServices/scripts/dev.sh";
	private const string BACKEND_HEALTH_URL = "http://localhost:8000/api/health";
	private const int BACKEND_PORT = 8000;
	private const float STARTUP_TIMEOUT_SECONDS = 10.0f;
	private const float HEALTH_CHECK_INTERVAL_SECONDS = 0.5f;

	private bool _isBackendRunning = false;
	private HttpClient _healthCheckClient;

	[Signal] public delegate void BackendReadyEventHandler();
	[Signal] public delegate void BackendStartFailedEventHandler(string error);

	public static BackendManager Instance { get; private set; }

	protected override void OnServiceReady()
	{
		Instance = this;
	}

	protected override async void OnServiceInitialize()
	{
		LogInfo("BackendManager initializing...");
		_healthCheckClient = new HttpClient();
		await EnsureBackendRunningAsync();
	}

	/// <summary>
	/// Ensure backend is running, start if needed
	/// </summary>
	private async Task EnsureBackendRunningAsync()
	{
		try
		{
			// Check if already running
			if (await IsBackendHealthyAsync())
			{
				LogInfo("Backend already running and healthy");
				_isBackendRunning = true;
				CallDeferred(MethodName.EmitSignal, SignalName.BackendReady);
				return;
			}

			// Attempt to start backend
			LogInfo("Starting local backend service...");

			var output = new Godot.Collections.Array();
			var exitCode = OS.Execute(
				"sh",
				new string[] { BACKEND_START_SCRIPT },
				output,
				false  // Don't read stdout (script runs in background)
			);

			if (exitCode != 0)
			{
				var errorMsg = $"Backend start script failed with exit code {exitCode}";
				LogError(errorMsg);
				CallDeferred(GodotObject.MethodName.EmitSignal, SignalName.BackendStartFailed, errorMsg);
				return;
			}

			LogInfo("Backend startup script executed, waiting for health check...");

			// Wait for backend to become healthy
			var startTime = Time.GetTicksMsec();
			while (Time.GetTicksMsec() - startTime < STARTUP_TIMEOUT_SECONDS * 1000)
			{
				if (await IsBackendHealthyAsync())
				{
					_isBackendRunning = true;
					LogInfo("Backend started successfully and is healthy");
					CallDeferred(GodotObject.MethodName.EmitSignal, SignalName.BackendReady);
					return;
				}

				await DelayAsync(HEALTH_CHECK_INTERVAL_SECONDS);
			}

			var timeoutMsg = $"Backend failed to become healthy within {STARTUP_TIMEOUT_SECONDS}s";
			LogError(timeoutMsg);
			CallDeferred(GodotObject.MethodName.EmitSignal, SignalName.BackendStartFailed, timeoutMsg);
		}
		catch (Exception ex)
		{
			var errorMsg = $"Backend startup exception: {ex.Message}";
			LogError(errorMsg);
			CallDeferred(GodotObject.MethodName.EmitSignal, SignalName.BackendStartFailed, errorMsg);
		}
	}

	/// <summary>
	/// Check if backend is healthy via health endpoint
	/// </summary>
	private async Task<bool> IsBackendHealthyAsync()
	{
		try
		{
			// Connect if needed
			if (_healthCheckClient.GetStatus() != HttpClient.Status.Connected &&
			    _healthCheckClient.GetStatus() != HttpClient.Status.Connecting)
			{
				var error = _healthCheckClient.ConnectToHost("localhost", BACKEND_PORT);
				if (error != Error.Ok)
					return false;
			}

			// Wait for connection
			var attempts = 0;
			while (_healthCheckClient.GetStatus() == HttpClient.Status.Connecting && attempts < 20)
			{
				await DelayAsync(0.05f);
				attempts++;
			}

			if (_healthCheckClient.GetStatus() != HttpClient.Status.Connected)
				return false;

			// Make health check request
			var headers = new[] { "User-Agent: BarBox-Client/1.0" };
			var requestError = _healthCheckClient.Request(HttpClient.Method.Get, "/api/health", headers);
			if (requestError != Error.Ok)
				return false;

			// Poll for response
			var pollAttempts = 0;
			while (_healthCheckClient.GetStatus() == HttpClient.Status.Requesting && pollAttempts < 100)
			{
				await DelayAsync(0.01f);
				pollAttempts++;
			}

			if (_healthCheckClient.GetStatus() != HttpClient.Status.Body &&
			    _healthCheckClient.GetStatus() != HttpClient.Status.Connected)
				return false;

			var responseCode = _healthCheckClient.GetResponseCode();
			return responseCode == 200;
		}
		catch
		{
			return false;
		}
	}

	public bool IsBackendRunning() => _isBackendRunning;

	/// <summary>
	/// Get the singleton instance
	/// </summary>
	public static BackendManager GetInstance()
	{
		return GetAutoload<BackendManager>();
	}

	protected override void OnServiceDestroyed()
	{
		// Backend runs as separate process, don't kill it on app shutdown
		// It will auto-restart when app restarts

		_healthCheckClient?.Close();
		_healthCheckClient = null;

		Instance = null;
	}
}
