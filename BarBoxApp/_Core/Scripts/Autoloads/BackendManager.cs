using Godot;
using LightResults;
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

	protected override async Task OnServiceInitializeAsync(CancellationToken cancellationToken = default)
	{
		Result<bool> result;

		// In test mode, skip auto-start and wait for external backend
		if (IS_TEST_MODE)
		{
			LogInfo("Test mode detected - waiting for external backend (no auto-start)");
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
			LogError($"Backend not available at {BACKEND_HOST}:{BACKEND_PORT}");
			LogError("To start manually: cd BarBoxServices && sh scripts/dev.sh");

			CallDeferred(MethodName.EmitSignal, SignalName.BackendStartFailed, error.Message);

			throw new Exception($"Backend failed: {error.Message}");
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
	private async Task<Result<bool>> WaitForExternalBackendAsync(CancellationToken cancellationToken = default)
	{
		LogInfo($"Waiting for external test backend at {BACKEND_HOST}:{BACKEND_PORT}...");
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
				LogInfo($"External backend ready at {BACKEND_HOST}:{BACKEND_PORT}");
				return Result.Success(true);
			}

			await DelayAsync(0.5f);
		}

		return Result.Failure<bool>(
			$"External backend not ready within {EXTERNAL_BACKEND_TIMEOUT}s. " +
			"Ensure test backend is started: cd BarBoxServices && sh scripts/test-backend.sh start");
	}

	/// <summary>
	/// Check if a port is in use on the local machine
	/// </summary>
	private bool IsPortInUse(int port)
	{
		try
		{
			var output = new Godot.Collections.Array();
			var exitCode = OS.Execute("lsof", ["-i", $":{port}", "-t"], output);

			if (exitCode != 0 || output.Count <= 0)
				return false;

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
	/// Kill backend process on specified port if we own it.
	/// Only kills processes that match our tracked _backendProcessId.
	/// This prevents BackendManager from killing manually-started dev.sh processes.
	/// </summary>
	private async void KillOwnedProcessOnPort(int port)
	{
		try
		{
			// If we haven't started a backend, don't kill anything
			if (_backendProcessId == -1)
			{
				LogInfo($"No tracked backend process - skipping port {port} cleanup");
				return;
			}

			var output = new Godot.Collections.Array();
			var exitCode = OS.Execute("lsof", ["-i", $":{port}", "-t"], output);

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
								// This is our process - kill it
								LogWarning($"Killing our backend process on port {port} (PID: {pid})");
								OS.Execute("kill", ["-9", trimmedPid]);
								killedOurProcess = true;
								_backendProcessId = -1; // Clear tracked PID
							}
							else
							{
								// Not our process - leave it alone
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
	/// Ensure backend is running, starting it automatically if needed.
	/// This method will:
	/// 1. Check if backend is already healthy
	/// 2. Kill any stale processes on the backend port
	/// 3. Execute the backend start script
	/// 4. Poll for health check with timeout
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
				LogWarning($"Port {BACKEND_PORT} is in use but backend not healthy - attempting cleanup");
				KillOwnedProcessOnPort(BACKEND_PORT);
			}
		}

		// Verify port is actually free before starting new backend
		if (IsPortInUse(BACKEND_PORT))
		{
			// Port still in use - must be owned by another process (e.g., manually-started dev.sh)
			LogInfo($"Port {BACKEND_PORT} in use by external process - waiting for health check");

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
				$"Port {BACKEND_PORT} in use by external process but backend not healthy. " +
				"Please stop the conflicting process and restart.");
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
			return Result.Failure<bool>($"Backend start script not found: {startScript}");
		}

		LogInfo($"Starting backend via: {startScript}");

		// Create backend process as independent background process
		// Note: OS.Execute would block forever since dev.sh runs a long-running server
		// OS.CreateProcess returns immediately and gives us the PID for process tracking
		var pid = OS.CreateProcess(startScript, new string[] {});

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
				LogInfo($"Backend started successfully at {BACKEND_HOST}:{BACKEND_PORT}");
				return Result.Success(true);
			}

			await DelayAsync(0.5f);
		}

		return Result.Failure<bool>($"Backend failed to become healthy within {STARTUP_TIMEOUT} seconds");
	}

	/// <summary>
	/// Check if backend is healthy via HTTP /alive endpoint
	/// </summary>
	private async Task<Result<bool>> IsBackendHealthyAsync()
	{
		var httpClient = new HttpClient();
		var error = httpClient.ConnectToHost(BACKEND_HOST, BACKEND_PORT);

		if (error != Godot.Error.Ok)
		{
			httpClient.Close();
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
				// Request /alive endpoint
				var headers = new[] { "Accept: application/json" };
				var requestError = httpClient.Request(HttpClient.Method.Get, "/alive", headers);

				if (requestError != Godot.Error.Ok)
				{
					httpClient.Close();
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
						httpClient.Close();

						if (responseCode == 200)
							return Result.Success(true);

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
						httpClient.Close();
						return Result.Failure<bool>("Connection error while waiting for response");
					}

					await DelayAsync(0.05f);
				}

				httpClient.Close();
				return Result.Failure<bool>($"Health check timeout after {HEALTH_CHECK_TIMEOUT_SECONDS}s");
			}

			if (status == HttpClient.Status.CantConnect ||
			    status == HttpClient.Status.CantResolve ||
			    status == HttpClient.Status.ConnectionError)
			{
				httpClient.Close();
				return Result.Failure<bool>($"Connection failed: {status}");
			}

			await DelayAsync(0.05f);
		}

		httpClient.Close();
		return Result.Failure<bool>($"Connection timeout after {CONNECTION_TIMEOUT_SECONDS}s");
	}

	/// <summary>
	/// Retry connecting to backend (useful for UI "Retry" button).
	/// This will attempt to auto-start the backend if not running.
	/// </summary>
	public async void RetryConnection()
	{
		LogInfo("Retrying backend connection...");

		var result = await EnsureBackendRunningAsync();

		if (result.IsSuccess(out _))
		{
			_isBackendRunning = true;
			LogInfo($"Backend reconnected successfully at {BACKEND_HOST}:{BACKEND_PORT}");
			CallDeferred(MethodName.EmitSignal, SignalName.BackendReady);
		}
		else if (result.IsFailure(out var error))
		{
			LogWarning($"Backend retry failed: {error.Message}");
			CallDeferred(MethodName.EmitSignal, SignalName.BackendStartFailed, error.Message);
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
