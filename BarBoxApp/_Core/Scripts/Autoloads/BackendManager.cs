using Godot;
using System;
using System.Threading.Tasks;

/// <summary>
/// Manages local FastAPI backend lifecycle
/// Ensures backend is running before game operations
/// Blocks app initialization if backend fails to start
///
/// Uses file-based readiness signaling for fast, reliable backend status detection.
/// The backend writes 'app.db.ready' file when fully initialized (database schema created).
/// This avoids HTTP complexity and provides immediate, zero-overhead status checks.
/// </summary>
public partial class BackendManager : AutoloadBase
{
	private const string BACKEND_START_SCRIPT = "/Users/johngroot/Dev/barbox/BarBoxServices/scripts/dev.sh";
	private const string BACKEND_READY_FILE = "/Users/johngroot/Dev/barbox/BarBoxServices/app.db.ready";
	private const float STARTUP_TIMEOUT_SECONDS = 10.0f;
	private const float READINESS_CHECK_INTERVAL_SECONDS = 0.1f; // Fast polling (file check is cheap)

	private bool _isBackendRunning = false;

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
		await EnsureBackendRunningAsync();
	}

	/// <summary>
	/// Check if backend is ready via file-based signaling
	/// </summary>
	private bool IsBackendReady()
	{
		return FileAccess.FileExists(BACKEND_READY_FILE);
	}

	/// <summary>
	/// Check if a port is in use using lsof
	/// </summary>
	private bool IsPortInUse(int port)
	{
		try
		{
			var output = new Godot.Collections.Array();
			var exitCode = OS.Execute("lsof", new string[] { "-i", $":{port}" }, output, true);

			// lsof returns 0 if processes found, 1 if none found
			return exitCode == 0 && output.Count > 0;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// Kill process using a specific port
	/// </summary>
	private void KillProcessOnPort(int port)
	{
		try
		{
			var output = new Godot.Collections.Array();

			// Get PID of process on port
			OS.Execute("sh", new string[] {
				"-c",
				$"lsof -ti :{port}"
			}, output, true);

			if (output.Count > 0)
			{
				var pid = output[0].ToString().Trim();
				if (!string.IsNullOrEmpty(pid))
				{
					LogWarning($"Killing stale process {pid} on port {port}");
					OS.Execute("kill", new string[] { pid }, new Godot.Collections.Array(), false);
				}
			}
		}
		catch (Exception ex)
		{
			LogError($"Failed to kill process on port {port}: {ex.Message}");
		}
	}

	/// <summary>
	/// Ensure backend is running, start if needed
	/// </summary>
	private async Task EnsureBackendRunningAsync()
	{
		try
		{
			// Check if already running via ready file
			if (IsBackendReady())
			{
				LogInfo("Backend already running and ready");
				_isBackendRunning = true;
				CallDeferred(MethodName.EmitSignal, SignalName.BackendReady);
				return;
			}

			// Check for stale process (port in use but no ready file)
			const int BACKEND_PORT = 8000;
			if (IsPortInUse(BACKEND_PORT) && !IsBackendReady())
			{
				LogWarning($"Port {BACKEND_PORT} in use but backend not ready - cleaning up stale process");
				KillProcessOnPort(BACKEND_PORT);
				await DelayAsync(1.0f); // Wait for port to be released
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
				// Check if backend is already running (script may fail if already started)
				if (IsBackendReady())
				{
					_isBackendRunning = true;
					LogInfo("Backend already running and ready");
					CallDeferred(GodotObject.MethodName.EmitSignal, SignalName.BackendReady);
					return;
				}

				var errorMsg = $"Backend start script failed with exit code {exitCode}";
				LogError(errorMsg);
				CallDeferred(GodotObject.MethodName.EmitSignal, SignalName.BackendStartFailed, errorMsg);
				return;
			}

			LogInfo("Backend startup script executed, waiting for ready signal...");

			// Poll for ready file (much faster than HTTP - 10x per second)
			var startTime = Time.GetTicksMsec();
			while (Time.GetTicksMsec() - startTime < STARTUP_TIMEOUT_SECONDS * 1000)
			{
				if (IsBackendReady())
				{
					_isBackendRunning = true;
					LogInfo("Backend started successfully and is ready");
					CallDeferred(GodotObject.MethodName.EmitSignal, SignalName.BackendReady);
					return;
				}

				await DelayAsync(READINESS_CHECK_INTERVAL_SECONDS);
			}

			var timeoutMsg = $"Backend failed to become ready within {STARTUP_TIMEOUT_SECONDS}s";
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

		Instance = null;
	}
}
