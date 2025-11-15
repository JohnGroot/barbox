using Godot;
using System;
using System.Threading;
using System.Threading.Tasks;

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
	// Configuration with environment variable support
	private static readonly string BACKEND_HOST =
		System.Environment.GetEnvironmentVariable("BARBOX_BACKEND_HOST") ?? "127.0.0.1";

	private static readonly int BACKEND_PORT =
		int.TryParse(System.Environment.GetEnvironmentVariable("BARBOX_BACKEND_PORT"), out var port)
			? port
			: 8000;

	private static readonly string BACKEND_READY_FILE =
		System.Environment.GetEnvironmentVariable("BARBOX_BACKEND_READY_FILE")
		?? "/Users/johngroot/Dev/barbox/BarBoxServices/app.db.ready";

	// Test mode detection - when set, skip auto-start and wait for external backend
	private static readonly bool IS_TEST_MODE =
		!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("BARBOX_TEST_MODE"));

	private const float CONNECTION_TIMEOUT_SECONDS = 5.0f;
	private const float HEALTH_CHECK_TIMEOUT_SECONDS = 5.0f;

	private bool _isBackendRunning = false;
	private int _backendProcessId = -1;  // Track backend process for lifecycle management

	[Signal] public delegate void BackendReadyEventHandler();
	[Signal] public delegate void BackendStartFailedEventHandler(string error);

	public static BackendManager Instance { get; private set; }

	protected override void OnServiceReady()
	{
		Instance = this;
	}

	protected override async Task OnServiceInitializeAsync(CancellationToken cancellationToken = default)
	{
		LogInfo("BackendManager initializing...");

		bool success;

		// In test mode, skip auto-start and wait for external backend
		if (IS_TEST_MODE)
		{
			LogInfo("Test mode detected - waiting for external backend (no auto-start)");
			success = await WaitForExternalBackendAsync(cancellationToken);
		}
		else
		{
			// Normal mode: auto-start if needed
			success = await EnsureBackendRunningAsync(cancellationToken);
		}

		if (!success)
		{
			// Backend failed to start - show error UI and throw exception
			LogError($"Backend failed to become healthy within 30 seconds");
			LogWarning($"Backend not available at {BACKEND_HOST}:{BACKEND_PORT}");
			LogInfo("╔════════════════════════════════════════════════════════╗");
			LogInfo("║  Backend Service Not Running                           ║");
			LogInfo("╠════════════════════════════════════════════════════════╣");
			LogInfo("║  Backend auto-start failed.                            ║");
			LogInfo("║                                                        ║");
			LogInfo("║  Possible causes:                                      ║");
			LogInfo("║    - Backend script not found                          ║");
			LogInfo("║    - Port 8000 blocked by firewall                     ║");
			LogInfo("║    - Python dependencies missing                       ║");
			LogInfo("║                                                        ║");
			LogInfo("║  To start manually:                                    ║");
			LogInfo("║    cd BarBoxServices                                   ║");
			LogInfo("║    sh scripts/dev.sh                                   ║");
			LogInfo("║                                                        ║");
			LogInfo("║  Then restart the app or click Retry Connection       ║");
			LogInfo("║                                                        ║");
			LogInfo("║  Features requiring backend will be unavailable:       ║");
			LogInfo("║    - Credit system                                     ║");
			LogInfo("║    - Session persistence                               ║");
			LogInfo("║    - Leaderboards                                      ║");
			LogInfo("║    - Progress saving                                   ║");
			LogInfo("╚════════════════════════════════════════════════════════╝");

			CallDeferred(MethodName.EmitSignal, SignalName.BackendStartFailed,
				"Backend auto-start failed. Start manually with: cd BarBoxServices && sh scripts/dev.sh");

			throw new Exception($"Backend failed to start at {BACKEND_HOST}:{BACKEND_PORT}");
		}

		_isBackendRunning = true;
		LogInfo($"Backend is available and healthy at {BACKEND_HOST}:{BACKEND_PORT}");
		CallDeferred(MethodName.EmitSignal, SignalName.BackendReady);
	}

	/// <summary>
	/// Check if backend is ready via file-based signaling (legacy support)
	/// </summary>
	private bool IsBackendReady()
	{
		return FileAccess.FileExists(BACKEND_READY_FILE);
	}

	/// <summary>
	/// Wait for external backend to become healthy (test mode only).
	/// This method does NOT start a backend - it assumes an external test runner
	/// has already started the backend and waits for it to become healthy.
	/// </summary>
	private async Task<bool> WaitForExternalBackendAsync(CancellationToken cancellationToken = default)
	{
		LogInfo($"Waiting for external test backend at {BACKEND_HOST}:{BACKEND_PORT}...");
		const float EXTERNAL_BACKEND_TIMEOUT = 30.0f;
		var startTime = Time.GetTicksMsec();

		while (Time.GetTicksMsec() - startTime < EXTERNAL_BACKEND_TIMEOUT * 1000)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				LogWarning("Backend initialization cancelled");
				return false;
			}

			if (await IsBackendHealthyAsync())
			{
				_isBackendRunning = true;
				LogInfo($"External backend ready at {BACKEND_HOST}:{BACKEND_PORT}");
				return true;
			}

			await DelayAsync(0.5f);
		}

		LogError($"External backend not ready within {EXTERNAL_BACKEND_TIMEOUT} seconds");
		LogError("Ensure test backend is started before running tests:");
		LogError("  cd BarBoxServices && sh scripts/test-backend.sh start");
		return false;
	}

	/// <summary>
	/// Check if a port is in use on the local machine
	/// </summary>
	private bool IsPortInUse(int port)
	{
		try
		{
			var output = new Godot.Collections.Array();
			var exitCode = OS.Execute("lsof", new string[] { "-i", $":{port}", "-t" }, output);

			if (exitCode == 0 && output.Count > 0)
			{
				var outputStr = output[0].ToString().Trim();
				return !string.IsNullOrEmpty(outputStr);
			}

			return false;
		}
		catch (Exception ex)
		{
			LogWarning($"Failed to check port {port}: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Kill process running on specified port.
	/// Handles multiple processes correctly by parsing PIDs individually.
	/// Waits and verifies port is actually free before returning.
	/// </summary>
	private async void KillProcessOnPort(int port)
	{
		try
		{
			var output = new Godot.Collections.Array();
			var exitCode = OS.Execute("lsof", new string[] { "-i", $":{port}", "-t" }, output);

			if (exitCode == 0 && output.Count > 0)
			{
				var pidsString = output[0].ToString().Trim();
				if (!string.IsNullOrEmpty(pidsString))
				{
					// Split by newlines to handle multiple PIDs
					// lsof -t returns one PID per line when multiple processes use the port
					var pids = pidsString.Split('\n', StringSplitOptions.RemoveEmptyEntries);

					LogWarning($"Found {pids.Length} stale process(es) on port {port}");

					foreach (var pidStr in pids)
					{
						var trimmedPid = pidStr.Trim();
						if (!string.IsNullOrEmpty(trimmedPid))
						{
							LogWarning($"Killing stale process on port {port} (PID: {trimmedPid})");
							OS.Execute("kill", new string[] { "-9", trimmedPid });
						}
					}

					// Wait for port to be fully released (TCP TIME_WAIT state cleanup)
					// Multiple processes may need more time to release port bindings
					LogInfo($"Waiting for port {port} to be fully released...");
					const int MAX_WAIT_ATTEMPTS = 10; // 5 seconds total (10 * 500ms)
					for (int i = 0; i < MAX_WAIT_ATTEMPTS; i++)
					{
						await DelayAsync(0.5f); // Frame-aware async delay instead of blocking Thread.Sleep

						if (!IsPortInUse(port))
						{
							LogInfo($"Port {port} released after {(i + 1) * 500}ms");
							return;
						}
					}

					// Port still in use after max wait time
					if (IsPortInUse(port))
					{
						LogWarning($"Port {port} still in use after {MAX_WAIT_ATTEMPTS * 500}ms wait - may cause startup issues");
					}
				}
			}
		}
		catch (Exception ex)
		{
			LogError($"Failed to kill process on port {port}: {ex.Message}");
		}
	}

	/// <summary>
	/// Ensure backend is running, starting it automatically if needed.
	/// This method will:
	/// 1. Check if backend is already healthy
	/// 2. Kill any stale processes on the backend port
	/// 3. Execute the backend start script
	/// 4. Poll for health check with timeout
	/// </summary>
	private async Task<bool> EnsureBackendRunningAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			// Check if backend already healthy
			if (await IsBackendHealthyAsync())
			{
				LogInfo("Backend already running and healthy");
				return true;
			}

			// Check for stale processes (but NOT in test mode!)
			if (IsPortInUse(BACKEND_PORT))
			{
				if (IS_TEST_MODE)
				{
					// In test mode, assume port usage is legitimate (test backend starting)
					LogInfo($"Port {BACKEND_PORT} in use (test mode) - waiting for health check");

					// Wait for health instead of killing
					const float WAIT_TIMEOUT = 10.0f;
					var waitStartTime = Time.GetTicksMsec();

					while (Time.GetTicksMsec() - waitStartTime < WAIT_TIMEOUT * 1000)
					{
						if (cancellationToken.IsCancellationRequested)
						{
							LogWarning("Backend health check cancelled");
							return false;
						}

						if (await IsBackendHealthyAsync())
						{
							LogInfo("Backend became healthy");
							return true;
						}
						await DelayAsync(0.5f);
					}

					LogError("Backend on port but never became healthy");
					return false;
				}
				else
				{
					// Normal mode: kill stale processes
					LogWarning($"Port {BACKEND_PORT} is in use but backend not healthy - killing stale process");
					KillProcessOnPort(BACKEND_PORT);
				}
			}

			// Verify port is actually free before starting new backend
			if (IsPortInUse(BACKEND_PORT))
			{
				LogError($"Port {BACKEND_PORT} still in use after cleanup - cannot start backend");
				LogError("Please manually kill processes on port 8000 and try again");
				return false;
			}

			// Find the backend start script
			var projectPath = ProjectSettings.GlobalizePath("res://");
			var backendServicesPath = System.IO.Path.Combine(
				System.IO.Path.GetDirectoryName(projectPath.TrimEnd('/')),
				"BarBoxServices"
			);
			var startScript = System.IO.Path.Combine(backendServicesPath, "scripts", "dev.sh");

			if (!System.IO.File.Exists(startScript))
			{
				LogError($"Backend start script not found: {startScript}");
				return false;
			}

			LogInfo($"Starting backend via: {startScript}");

			// Create backend process as independent background process
			// Note: OS.Execute would block forever since dev.sh runs a long-running server
			// OS.CreateProcess returns immediately and gives us the PID for process tracking
			var pid = OS.CreateProcess(startScript, new string[] {});

			if (pid == -1)
			{
				LogError("Failed to create backend process");
				LogError($"Script path: {startScript}");
				LogError($"Script exists: {System.IO.File.Exists(startScript)}");
				return false;
			}

			LogInfo($"Backend process started with PID: {pid}");
			_backendProcessId = pid;

			// Wait for backend to become healthy
			LogInfo("Waiting for backend to start...");
			const float STARTUP_TIMEOUT = 30.0f; // Increased from 10.0f to accommodate first-run uv dependency installation
			var startTime = Time.GetTicksMsec();

			while (Time.GetTicksMsec() - startTime < STARTUP_TIMEOUT * 1000)
			{
				if (cancellationToken.IsCancellationRequested)
				{
					LogWarning("Backend startup cancelled");
					return false;
				}

				if (await IsBackendHealthyAsync())
				{
					LogInfo($"Backend started successfully at {BACKEND_HOST}:{BACKEND_PORT}");
					return true;
				}

				await DelayAsync(0.5f);
			}

			LogError($"Backend failed to become healthy within {STARTUP_TIMEOUT} seconds");
			return false;
		}
		catch (Exception ex)
		{
			LogError($"Exception during backend startup: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Check if backend is healthy via HTTP /alive endpoint
	/// </summary>
	private async Task<bool> IsBackendHealthyAsync()
	{
		try
		{
			var httpClient = new HttpClient();
			var error = httpClient.ConnectToHost(BACKEND_HOST, BACKEND_PORT);

			if (error != Error.Ok)
			{
				httpClient.Close();
				return false;
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
					// Request /alive endpoint
					var headers = new[] { "Accept: application/json" };
					var requestError = httpClient.Request(HttpClient.Method.Get, "/alive", headers);

					if (requestError != Error.Ok)
					{
						httpClient.Close();
						return false;
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
							httpClient.Close();
							return responseCode == 200;
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
							httpClient.Close();
							return false;
						}

						await DelayAsync(0.05f);
					}

					httpClient.Close();
					return false;
				}

				if (status == HttpClient.Status.CantConnect ||
				    status == HttpClient.Status.CantResolve ||
				    status == HttpClient.Status.ConnectionError)
				{
					httpClient.Close();
					return false;
				}

				await DelayAsync(0.05f);
			}

			httpClient.Close();
			return false;
		}
		catch (Exception ex)
		{
			LogError($"Backend health check failed: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Retry connecting to backend (useful for UI "Retry" button).
	/// This will attempt to auto-start the backend if not running.
	/// </summary>
	public async void RetryConnection()
	{
		LogInfo("Retrying backend connection...");

		var success = await EnsureBackendRunningAsync();

		if (success)
		{
			_isBackendRunning = true;
			LogInfo($"Backend reconnected successfully at {BACKEND_HOST}:{BACKEND_PORT}");
			CallDeferred(MethodName.EmitSignal, SignalName.BackendReady);
		}
		else
		{
			LogWarning("Backend retry failed - still not available");
			CallDeferred(MethodName.EmitSignal, SignalName.BackendStartFailed,
				"Backend retry failed. Check logs for details.");
		}
	}

	public bool IsBackendRunning() => _isBackendRunning;

	/// <summary>
	/// Get backend connection info for diagnostics
	/// </summary>
	public string GetBackendInfo()
	{
		return $"{BACKEND_HOST}:{BACKEND_PORT}";
	}

	/// <summary>
	/// Get the singleton instance
	/// </summary>
	public static BackendManager GetInstance()
	{
		return GetAutoload<BackendManager>();
	}

	protected override void OnServiceDestroyed()
	{
		// We don't manage backend lifecycle, so nothing to clean up
		Instance = null;
	}
}
