using System;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using LightResults;

namespace BarBox.Core.Autoloads;

/// <summary>
/// Manages lifecycle and connection to the BarBox backend service.
///
/// This class automatically starts the backend if not running, using:
///   BarBoxServices/scripts/dev.sh
///
/// Lifecycle Management:
///   - Auto-starts backend on app launch if not detected
///   - Detects and kills stale backend processes
///   - Monitors backend health via HTTP /alive endpoint
///   - Provides retry mechanism for transient failures
///   - Backend persists after app shutdown for faster restarts
///
/// Configuration via environment variables:
///   BARBOX_BACKEND_HOST - Backend hostname (default: 127.0.0.1)
///   BARBOX_BACKEND_PORT - Backend port (default: 8000)
///   BARBOX_BACKEND_READY_FILE - Ready file path (optional, legacy support)
///
/// If auto-start fails, app continues with degraded functionality and shows
/// error UI with retry button.
/// </summary>
public partial class BackendManager : AutoloadBase
{
	// System commands
	private const string CMD_LSOF = "lsof";
	private const string CMD_KILL = "kill";
	private static readonly string[] HEALTH_CHECK_HEADERS = ["Accept: application/json"];

	// Configuration - initialized in OnServiceInitializeAsync after LocationManager loads .env files
	// (Static readonly fields would capture env vars before .env is loaded!)
	private string _backendHost;
	private int _backendPort;
	private bool _isTestMode;
	private bool _useHttps;

	private const float CONNECTION_TIMEOUT_SECONDS = 5.0f;
	private const float HEALTH_CHECK_TIMEOUT_SECONDS = 5.0f;

	private bool _isBackendRunning = false;
	private int _backendProcessId = -1;  // Track backend process for lifecycle management

	protected override async Task OnServiceInitializeAsync(CancellationToken cancellationToken = default)
	{
		// Get configuration from LocationManager (centralized config source)
		// LocationManager is initialized before BackendManager in Phase 1
		var locationManager = LocationManager.GetAutoload() ?? throw new InvalidOperationException("LocationManager required but not available - cannot get backend configuration");
		_backendHost = locationManager.BackendHost;
		_backendPort = locationManager.BackendPort;
		_isTestMode = locationManager.IsTestMode;
		_useHttps = locationManager.UseHttps;

		LogInfo($"Backend configuration: {_backendHost}:{_backendPort}, TestMode: {_isTestMode}, HTTPS: {_useHttps}");

		Result<bool> result;

		// Check if connecting to remote backend (not localhost)
		bool isRemoteBackend = !IsLocalBackend(_backendHost);

		// In test mode, skip auto-start and wait for external backend
		if (_isTestMode)
		{
			LogInfo("Test mode detected - waiting for external backend (no auto-start)");
			result = await WaitForExternalBackendAsync(cancellationToken);
		}
		else if (isRemoteBackend)
		{
			// Remote backend (e.g., Fly.io) - don't try to auto-start local dev.sh
			LogInfo($"Remote backend detected ({_backendHost}) - waiting for connection (no auto-start)");
			result = await WaitForExternalBackendAsync(cancellationToken);
		}
		else
		{
			// Normal mode: auto-start if needed
			result = await EnsureBackendRunningAsync(cancellationToken);
		}

		if (result.IsFailure(out var error))
		{
			// Backend failed to start - show concise error and throw exception
			LogError($"Backend initialization failed: {error.Message}");
			LogError($"Backend not available at {_backendHost}:{_backendPort}");
			LogError("To start manually: cd BarBoxServices && sh scripts/dev.sh");

			throw new Exception($"Backend failed: {error.Message}");
		}

		_isBackendRunning = true;
		LogInfo($"Backend is available and healthy at {_backendHost}:{_backendPort}");
	}

	/// <summary>
	/// Check if host is local (localhost/127.0.0.1/etc) vs remote
	/// </summary>
	private static bool IsLocalBackend(string host)
	{
		if (string.IsNullOrEmpty(host))
		{
			return true; // Assume local if not set
		}

		return host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
			   host.Equals("127.0.0.1", StringComparison.Ordinal) ||
			   host.Equals("::1", StringComparison.Ordinal) ||
			   host.StartsWith("192.168.", StringComparison.Ordinal) ||
			   host.StartsWith("10.", StringComparison.Ordinal);
	}

	/// <summary>
	/// Wait for external backend to become healthy (test mode or remote backend).
	/// This method does NOT start a backend - it assumes an external process
	/// (test runner or remote service) has already started the backend.
	/// </summary>
	private async Task<Result<bool>> WaitForExternalBackendAsync(CancellationToken cancellationToken = default)
	{
		LogInfo($"Waiting for backend at {_backendHost}:{_backendPort}...");
		const float EXTERNAL_BACKEND_TIMEOUT = 30.0f;
		var startTime = Time.GetTicksMsec();

		while (Time.GetTicksMsec() - startTime < EXTERNAL_BACKEND_TIMEOUT * 1000)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return Result.Failure<bool>("Backend initialization cancelled");
			}

			var healthResult = await IsBackendHealthyAsync();
			if (healthResult.IsSuccess(out var isHealthy) && isHealthy)
			{
				_isBackendRunning = true;
				LogInfo($"External backend ready at {_backendHost}:{_backendPort}");
				return Result.Success(true);
			}

			await DelayAsync(0.5f);
		}

		return Result.Failure<bool>(
			$"External backend not ready within {EXTERNAL_BACKEND_TIMEOUT}s. " +
			"Ensure test backend is started: cd BarBoxServices && sh scripts/test-backend.sh start");
	}

	/// <summary>
	/// Find backend start script, trying both source and export deployment paths.
	/// Source deployment: BarBoxApp/../BarBoxServices/scripts/dev.sh
	/// Export deployment: BarBoxApp/releases/vX.Y.Z/../../BarBoxServices/scripts/dev.sh
	/// </summary>
	private string FindBackendStartScript()
	{
		var projectPath = ProjectSettings.GlobalizePath("res://");
		if (string.IsNullOrEmpty(projectPath))
		{
			LogWarning("Could not resolve project path");
			return null;
		}

		var projectDir = System.IO.Path.GetDirectoryName(projectPath.TrimEnd('/'));
		if (string.IsNullOrEmpty(projectDir))
		{
			LogWarning("Could not determine project directory");
			return null;
		}

		// Try source deployment path first (BarBoxApp/../BarBoxServices)
		var sourceBackendPath = System.IO.Path.Combine(projectDir, "BarBoxServices", "scripts", "dev.sh");
		if (System.IO.File.Exists(sourceBackendPath))
		{
			LogInfo($"Found backend script (source deployment): {sourceBackendPath}");
			return sourceBackendPath;
		}

		// Try export deployment path (from releases/vX.Y.Z/ -> ../../BarBoxServices)
		var parentDir = System.IO.Path.GetDirectoryName(projectDir);
		if (!string.IsNullOrEmpty(parentDir))
		{
			var exportBackendPath = System.IO.Path.Combine(parentDir, "BarBoxServices", "scripts", "dev.sh");
			if (System.IO.File.Exists(exportBackendPath))
			{
				LogInfo($"Found backend script (export deployment): {exportBackendPath}");
				return exportBackendPath;
			}

			LogError("Backend start script not found. Checked:");
			LogError($"  Source: {sourceBackendPath}");
			LogError($"  Export: {exportBackendPath}");
		}
		else
		{
			LogError("Backend start script not found. Checked:");
			LogError($"  Source: {sourceBackendPath}");
			LogError("  Export: (could not determine parent directory)");
		}

		return null;
	}

	private bool IsPortInUse(int port)
	{
		try
		{
			var output = new Godot.Collections.Array();
			var exitCode = OS.Execute(CMD_LSOF, ["-i", $":{port}", "-t"], output);

			if (exitCode != 0 || output.Count <= 0)
			{
				return false;
			}

			var outputStr = output[0].ToString().Trim();
			return !string.IsNullOrEmpty(outputStr);
		}
		catch (Exception ex)
		{
			LogWarning($"Failed to check port {port}: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Only kills processes matching _backendProcessId to avoid killing external dev.sh instances
	/// </summary>
	private async void KillOwnedProcessOnPort(int port)
	{
		try
		{
			if (_backendProcessId == -1)
			{
				LogInfo($"No tracked backend process - skipping port {port} cleanup");
				return;
			}

			var output = new Godot.Collections.Array();
			var exitCode = OS.Execute(CMD_LSOF, ["-i", $":{port}", "-t"], output);

			if (exitCode == 0 && output.Count > 0)
			{
				var pidsString = output[0].ToString().Trim();
				if (!string.IsNullOrEmpty(pidsString))
				{
					// Split by newlines to handle multiple PIDs
					var pids = pidsString.Split('\n', StringSplitOptions.RemoveEmptyEntries);

					bool killedOurProcess = false;

					foreach (var pidStr in pids)
					{
						var trimmedPid = pidStr.Trim();
						if (!string.IsNullOrEmpty(trimmedPid) && int.TryParse(trimmedPid, out var pid))
						{
							if (pid == _backendProcessId)
							{
								LogWarning($"Killing our backend process on port {port} (PID: {pid})");
								OS.Execute(CMD_KILL, ["-9", trimmedPid]);
								killedOurProcess = true;
								_backendProcessId = -1;
							}
							else
							{
								LogInfo($"Port {port} in use by PID {pid} (not our process) - leaving it alone");
							}
						}
					}

					if (!killedOurProcess)
					{
						LogInfo($"Port {port} in use but not by our process - skipping cleanup");
						return;
					}

					// Wait for port to be fully released (TCP TIME_WAIT state cleanup)
					const int MAX_WAIT_ATTEMPTS = 10; // 2.5 seconds total (10 * 250ms)
					for (int i = 0; i < MAX_WAIT_ATTEMPTS; i++)
					{
						await DelayAsync(0.25f);

						if (!IsPortInUse(port))
						{
							LogInfo($"Port {port} released successfully");
							return;
						}
					}

					// Port still in use after max wait time
					if (IsPortInUse(port))
					{
						LogWarning($"Port {port} still in use after {MAX_WAIT_ATTEMPTS * 250}ms");
					}
				}
			}
		}
		catch (Exception ex)
		{
			LogWarning($"Port cleanup failed: {ex.Message}");
		}
	}

	/// <summary>
	/// Auto-starts backend via dev.sh if not healthy, with stale process cleanup
	/// Returns failure if backend doesn't respond within 30s timeout
	/// </summary>
	private async Task<Result<bool>> EnsureBackendRunningAsync(CancellationToken cancellationToken = default)
	{
		// Check if backend already healthy
		var healthResult = await IsBackendHealthyAsync();
		if (healthResult.IsSuccess(out var isHealthy) && isHealthy)
		{
			return Result.Success(true);
		}

		// Check for stale processes (but NOT in test mode!)
		if (IsPortInUse(_backendPort))
		{
			if (_isTestMode)
			{
				// In test mode, assume port usage is legitimate (test backend starting)
				LogInfo($"Port {_backendPort} in use (test mode) - waiting for health check");

				// Wait for health instead of killing
				const float WAIT_TIMEOUT = 10.0f;
				var waitStartTime = Time.GetTicksMsec();

				while (Time.GetTicksMsec() - waitStartTime < WAIT_TIMEOUT * 1000)
				{
					if (cancellationToken.IsCancellationRequested)
					{
						return Result.Failure<bool>("Backend health check cancelled");
					}

					healthResult = await IsBackendHealthyAsync();
					if (healthResult.IsSuccess(out isHealthy) && isHealthy)
					{
						LogInfo("Backend became healthy");
						return Result.Success(true);
					}

					await DelayAsync(0.5f);
				}

				return Result.Failure<bool>("Backend on port but never became healthy within 10s");
			}
			else
			{
				// Normal mode: only kill processes we own
				LogWarning($"Port {_backendPort} is in use but backend not healthy - attempting cleanup");
				KillOwnedProcessOnPort(_backendPort);
			}
		}

		// Verify port is actually free before starting new backend
		if (IsPortInUse(_backendPort))
		{
			// Port still in use - must be owned by another process (e.g., manually-started dev.sh)
			LogInfo($"Port {_backendPort} in use by external process - waiting for health check");

			// Wait for the external backend to become healthy
			const float EXTERNAL_WAIT_TIMEOUT = 30.0f;
			var waitStartTime = Time.GetTicksMsec();

			while (Time.GetTicksMsec() - waitStartTime < EXTERNAL_WAIT_TIMEOUT * 1000)
			{
				if (cancellationToken.IsCancellationRequested)
				{
					return Result.Failure<bool>("Backend health check cancelled");
				}

				healthResult = await IsBackendHealthyAsync();
				if (healthResult.IsSuccess(out isHealthy) && isHealthy)
				{
					LogInfo("External backend is healthy - using existing instance");
					return Result.Success(true);
				}

				await DelayAsync(0.5f);
			}

			return Result.Failure<bool>(
				$"Port {_backendPort} in use by external process but backend not healthy. " +
				"Please stop the conflicting process and restart.");
		}

		// Find the backend start script (handles both source and export deployment)
		var startScript = FindBackendStartScript();
		if (startScript == null)
		{
			return Result.Failure<bool>("Backend start script not found. Searched source and export deployment paths.");
		}

		LogInfo($"Starting backend via: {startScript}");

		// Create backend process as independent background process
		// Note: OS.Execute would block forever since dev.sh runs a long-running server
		// OS.CreateProcess returns immediately and gives us the PID for process tracking
		var pid = OS.CreateProcess(startScript, []);

		if (pid == -1)
		{
			return Result.Failure<bool>("Failed to create backend process");
		}

		LogInfo($"Backend process started with PID: {pid}");
		_backendProcessId = pid;

		// Wait for backend to become healthy
		const float STARTUP_TIMEOUT = 30.0f; // Increased from 10.0f to accommodate first-run uv dependency installation
		var startTime = Time.GetTicksMsec();

		while (Time.GetTicksMsec() - startTime < STARTUP_TIMEOUT * 1000)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return Result.Failure<bool>("Backend startup cancelled");
			}

			healthResult = await IsBackendHealthyAsync();
			if (healthResult.IsSuccess(out isHealthy) && isHealthy)
			{
				LogInfo($"Backend started successfully at {_backendHost}:{_backendPort}");
				return Result.Success(true);
			}

			await DelayAsync(0.5f);
		}

		return Result.Failure<bool>($"Backend failed to become healthy within {STARTUP_TIMEOUT} seconds");
	}

	private async Task<Result<bool>> IsBackendHealthyAsync()
	{
		var httpClient = new HttpClient();
		try
		{
			// Configure TLS options for HTTPS
			TlsOptions tlsOptions = null;
			if (_useHttps)
			{
				// Pass hostname for SNI (Server Name Indication) - required for Fly.io's shared IP
				tlsOptions = TlsOptions.Client(null, _backendHost);
			}

			var error = httpClient.ConnectToHost(_backendHost, _backendPort, tlsOptions);

			if (error != Godot.Error.Ok)
			{
				return Result.Failure<bool>($"Failed to initiate connection: {error}");
			}

			// Poll for connection
			var startTime = Time.GetTicksMsec();
			while (Time.GetTicksMsec() - startTime < CONNECTION_TIMEOUT_SECONDS * 1000)
			{
				httpClient.Poll();
				var status = httpClient.GetStatus();

				if (status == HttpClient.Status.Resolving ||
					status == HttpClient.Status.Connecting)
				{
					await DelayAsync(0.05f);
					continue;
				}

				if (status == HttpClient.Status.Connected)
				{
					var requestError = httpClient.Request(HttpClient.Method.Get, "/alive", HEALTH_CHECK_HEADERS);

					if (requestError != Godot.Error.Ok)
					{
						return Result.Failure<bool>($"Failed to send request: {requestError}");
					}

					// Wait for response
					var responseStartTime = Time.GetTicksMsec();
					while (Time.GetTicksMsec() - responseStartTime < HEALTH_CHECK_TIMEOUT_SECONDS * 1000)
					{
						httpClient.Poll();
						var currentStatus = httpClient.GetStatus();

						// Success: got response body
						if (currentStatus == HttpClient.Status.Body)
						{
							var responseCode = httpClient.GetResponseCode();

							if (responseCode == 200)
							{
								return Result.Success(true);
							}

							return Result.Failure<bool>($"Unexpected response code: {responseCode}");
						}

						// Still processing - continue waiting
						if (currentStatus == HttpClient.Status.Requesting ||
							currentStatus == HttpClient.Status.Connected)
						{
							await DelayAsync(0.05f);
							continue;
						}

						// Error states - bail out early
						if (currentStatus == HttpClient.Status.CantConnect ||
							currentStatus == HttpClient.Status.ConnectionError)
						{
							return Result.Failure<bool>("Connection error while waiting for response");
						}

						await DelayAsync(0.05f);
					}

					return Result.Failure<bool>($"Health check timeout after {HEALTH_CHECK_TIMEOUT_SECONDS}s");
				}

				if (status == HttpClient.Status.CantConnect ||
					status == HttpClient.Status.CantResolve ||
					status == HttpClient.Status.ConnectionError)
				{
					return Result.Failure<bool>($"Connection failed: {status}");
				}

				await DelayAsync(0.05f);
			}

			return Result.Failure<bool>($"Connection timeout after {CONNECTION_TIMEOUT_SECONDS}s");
		}
		finally
		{
			httpClient.Close();
		}
	}

	/// <summary>
	/// UI retry handler - auto-starts backend if needed
	/// </summary>
	public async void RetryConnection()
	{
		LogInfo("Retrying backend connection...");

		var result = await EnsureBackendRunningAsync();

		if (result.IsSuccess(out _))
		{
			_isBackendRunning = true;
			LogInfo($"Backend reconnected successfully at {_backendHost}:{_backendPort}");
		}
		else if (result.IsFailure(out var error))
		{
			LogWarning($"Backend retry failed: {error.Message}");
		}
	}

	public bool IsBackendRunning() => _isBackendRunning;

	public string GetBackendInfo()
	{
		return $"{_backendHost}:{_backendPort}";
	}

	public static BackendManager GetInstance()
	{
		return GetAutoload<BackendManager>();
	}

	protected override void OnServiceDestroyed()
	{
		// Send graceful shutdown signal to backend process we started
		if (_backendProcessId > 0)
		{
			try
			{
				LogInfo($"Sending SIGTERM to backend process {_backendProcessId}");
				OS.Execute("kill", ["-TERM", _backendProcessId.ToString()]);
				_backendProcessId = -1;
			}
			catch (Exception ex)
			{
				LogWarning($"Failed to terminate backend process: {ex.Message}");
			}
		}
	}
}
