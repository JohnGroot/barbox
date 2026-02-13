# Backend Process Management

## OS.Execute vs OS.CreateProcess

Godot provides two methods for executing external processes with fundamentally different blocking behavior.

### OS.Execute() - Blocking

**ALWAYS blocking** - waits for process to complete before returning, regardless of parameters.

```csharp
// Use for: Short-lived commands (lsof, kill, curl)
var output = new Godot.Collections.Array();
var exitCode = OS.Execute("lsof", new string[] { "-i", $":{port}", "-t" }, output);
```

### OS.CreateProcess() - Non-Blocking

Returns immediately with PID. Use for long-running processes.

```csharp
// Use for: Backend servers, background processes
var pid = OS.CreateProcess(startScript, new string[] {});
if (pid == -1) { LogError("Failed to create process"); return false; }
_backendProcessId = pid;
```

### Quick Reference

| Use Case | Method | Blocking? | Returns |
|----------|--------|-----------|---------|
| Backend server startup | `OS.CreateProcess()` | No | PID |
| Port checking | `OS.Execute("lsof")` | Yes | Exit code |
| Process cleanup | `OS.Execute("kill")` | Yes | Exit code |

**Golden Rule:** If the process runs longer than a few seconds, use `OS.CreateProcess()`.

## Backend Auto-Start Pattern

Reference: `BackendManager.cs` (`EnsureBackendRunningAsync` method)

1. Check if backend is already healthy
2. Kill stale processes on the backend port
3. Start via `OS.CreateProcess(startScript, ...)`
4. Poll for health check with timeout

## Port Management

```csharp
private bool IsPortInUse(int port)
{
	var output = new Godot.Collections.Array();
	var exitCode = OS.Execute("lsof", new string[] { "-i", $":{port}", "-t" }, output);
	return exitCode == 0 && output.Count > 0 && !string.IsNullOrEmpty(output[0].ToString().Trim());
}

private void KillProcessOnPort(int port)
{
	var output = new Godot.Collections.Array();
	OS.Execute("lsof", new string[] { "-i", $":{port}", "-t" }, output);
	if (output.Count > 0)
	{
		var pid = output[0].ToString().Trim();
		if (!string.IsNullOrEmpty(pid))
		{
			OS.Execute("kill", new string[] { "-9", pid });
			System.Threading.Thread.Sleep(1000);
		}
	}
}
```

## Common Mistake

```csharp
// WRONG: Hangs forever because dev.sh runs a long-running server
var exitCode = OS.Execute("sh", new string[] { startScript }, output, false, true);
// Symptom: Backend starts, but BackendManager reports failure with exit code 1
```
