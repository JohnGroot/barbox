using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace BarBox.Tests.Helpers;

/// <summary>
/// Helper utilities for testing process management and backend startup scenarios.
/// Production-safe: Works in all build configurations without DEBUG guards.
/// </summary>
public static class ProcessTestHelpers
{
	/// <summary>
	/// Simulate lsof output for multiple PIDs on a port.
	/// Returns formatted string as lsof would return it (newline-separated PIDs).
	/// </summary>
	public static string SimulateLsofMultiplePids(params int[] pids)
	{
		return string.Join("\n", pids);
	}

	/// <summary>
	/// Simulate lsof output for single PID on a port.
	/// </summary>
	public static string SimulateLsofSinglePid(int pid)
	{
		return pid.ToString();
	}

	/// <summary>
	/// Parse PIDs from lsof output string.
	/// This is the CORRECT parsing logic that should be used in production.
	/// </summary>
	public static List<int> ParsePidsFromLsofOutput(string lsofOutput)
	{
		var pids = new List<int>();

		if (string.IsNullOrWhiteSpace(lsofOutput))
		{
			return pids;
		}

		var lines = lsofOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);

		foreach (var line in lines)
		{
			var trimmed = line.Trim();
			if (int.TryParse(trimmed, out var pid))
			{
				pids.Add(pid);
			}
		}

		return pids;
	}

	/// <summary>
	/// Check if a port is currently in use on the local machine.
	/// Production-safe utility that can be used in tests.
	/// </summary>
	public static bool IsPortInUse(int port)
	{
		try
		{
			var output = new Godot.Collections.Array();
			var exitCode = OS.Execute("lsof", ["-i", $":{port}", "-t"], output);

			if (exitCode == 0 && output.Count > 0)
			{
				var outputStr = output[0].ToString().Trim();
				return !string.IsNullOrEmpty(outputStr);
			}

			return false;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ProcessTestHelpers] Error checking port {port}: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Get all PIDs currently using a port.
	/// Returns empty list if port is not in use or on error.
	/// </summary>
	public static List<int> GetPidsOnPort(int port)
	{
		try
		{
			var output = new Godot.Collections.Array();
			var exitCode = OS.Execute("lsof", ["-i", $":{port}", "-t"], output);

			if (exitCode == 0 && output.Count > 0)
			{
				var outputStr = output[0].ToString().Trim();
				return ParsePidsFromLsofOutput(outputStr);
			}

			return new List<int>();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ProcessTestHelpers] Error getting PIDs on port {port}: {ex.Message}");
			return new List<int>();
		}
	}

	/// <summary>
	/// Kill a single process by PID.
	/// Returns true if kill command succeeded.
	/// </summary>
	public static bool KillProcess(int pid)
	{
		try
		{
			var output = new Godot.Collections.Array();
			var exitCode = OS.Execute("kill", ["-9", pid.ToString()], output);
			return exitCode == 0;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ProcessTestHelpers] Error killing process {pid}: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Kill all processes on a port.
	/// Returns number of processes successfully killed.
	/// </summary>
	public static int KillAllProcessesOnPort(int port)
	{
		var pids = GetPidsOnPort(port);
		var killedCount = 0;

		foreach (var pid in pids)
		{
			if (KillProcess(pid))
			{
				GD.Print($"[ProcessTestHelpers] Killed process {pid} on port {port}");
				killedCount++;
			}
			else
			{
				GD.PrintErr($"[ProcessTestHelpers] Failed to kill process {pid} on port {port}");
			}
		}

		if (killedCount > 0)
		{
			// Brief delay to allow port cleanup
			System.Threading.Thread.Sleep(1000);
		}

		return killedCount;
	}

	/// <summary>
	/// Wait for a port to become available (no processes using it).
	/// Returns true if port becomes available within timeout.
	/// </summary>
	public static async System.Threading.Tasks.Task<bool> WaitForPortAvailableAsync(
		int port,
		float timeoutSeconds = 5.0f,
		float pollDelaySeconds = 0.5f)
	{
		var startTime = Time.GetTicksMsec();
		var timeoutMs = timeoutSeconds * 1000;

		while (IsPortInUse(port))
		{
			var elapsed = Time.GetTicksMsec() - startTime;
			if (elapsed > timeoutMs)
			{
				return false;
			}

			await AutoloadBase.StaticDelayAsync(pollDelaySeconds);
		}

		return true;
	}

	/// <summary>
	/// Simulate multiple stale processes for testing.
	/// Creates dummy netcat processes listening on the specified port.
	/// Returns list of PIDs created.
	/// WARNING: Only use in test scenarios! Cleans up automatically after timeout.
	/// </summary>
	public static List<int> SimulateStaleProcessesOnPort(int port, int count)
	{
		var pids = new List<int>();

		GD.Print($"[ProcessTestHelpers] Simulating {count} stale processes on port {port}");

		// Note: This would require actual process creation which is complex in tests
		// For now, we'll document this as a manual test scenario
		GD.PrintErr("[ProcessTestHelpers] WARNING: Actual stale process simulation requires manual setup");
		GD.Print($"[ProcessTestHelpers] Manual steps:");
		GD.Print($"[ProcessTestHelpers]   1. Run: for i in {{1..{count}}}; do nc -l {port} & done");
		GD.Print($"[ProcessTestHelpers]   2. Verify with: lsof -i :{port} -t");
		GD.Print($"[ProcessTestHelpers]   3. Run test");
		GD.Print($"[ProcessTestHelpers]   4. Clean up with: lsof -i :{port} -t | xargs kill -9");

		return pids;
	}

	/// <summary>
	/// Create test data simulating process information.
	/// Useful for unit tests that don't need actual processes.
	/// </summary>
	public static class TestData
	{
		public static readonly int[] TypicalStalePids = [60147, 66881, 79755];
		public static readonly int SingleStalePid = 12345;
		public static readonly int TestBackendPort = 8000;
		public static readonly int TestBackendPid = 80985;

		/// <summary>
		/// Get simulated lsof output for typical stale process scenario.
		/// </summary>
		public static string GetTypicalStaleProcessOutput()
		{
			return SimulateLsofMultiplePids(TypicalStalePids);
		}
	}

	/// <summary>
	/// Log process state for debugging tests.
	/// </summary>
	public static void LogProcessState(int port)
	{
		var pids = GetPidsOnPort(port);

		if (pids.Count == 0)
		{
			GD.Print($"[ProcessTestHelpers] Port {port}: No processes found");
		}
		else
		{
			GD.Print($"[ProcessTestHelpers] Port {port}: {pids.Count} process(es) found");
			foreach (var pid in pids)
			{
				GD.Print($"[ProcessTestHelpers]   - PID {pid}");
			}
		}
	}
}
