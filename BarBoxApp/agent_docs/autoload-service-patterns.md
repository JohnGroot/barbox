# AutoloadBase & Service Patterns

## AutoloadBase Foundation

All autoload services extend `AutoloadBase` for consistent initialization, logging, and service discovery.

### Basic Usage

```csharp
public partial class MyService : AutoloadBase
{
	public static MyService Instance { get; private set; }

	protected override void OnServiceReady()
	{
		Instance = this;
		LogInfo("MyService initialized");
	}

	protected override void OnServiceDestroyed()
	{
		// Cleanup timers, disconnect signals, clear collections
	}
}
```

### Lifecycle

1. `_Ready()` -> `AddToGroup(ServiceName)` -> `OnServiceReady()`
2. Runtime: services discover each other via `GetAutoload<T>()`
3. `_ExitTree()` -> `OnServiceDestroyed()` -> `base._ExitTree()`

### Logging

```csharp
LogInfo("Service operation completed");     // [ServiceName] message
LogError("Critical component failed");      // [ServiceName] ERROR: message
LogWarning("Using fallback configuration"); // [ServiceName] WARNING: message
```

## Service Discovery

Dual-discovery resilience pattern:

```csharp
// Primary: Group-based (fast, flexible)
var service = GetAutoload<MyService>();

// Fallback: Direct node path (/root/ServiceName)
// Happens automatically inside GetAutoload<T>()
```

### Usage

```csharp
// Required service - fail fast
var sessionManager = SessionManager.GetInstance();
if (sessionManager == null)
	throw new InvalidOperationException("SessionManager required");

// Optional service - graceful degradation
var gameHost = GameHost.GetInstance();
_gameHost?.NotifyGameStarted();
```

## Signal Management

### TryConnectSignal (safe connection)

```csharp
TryConnectSignal(inputManager, "TouchStarted", nameof(OnTouchStarted),
	Callable.From<Vector2, int>(OnTouchStarted));
```

- Validates node is not null and is valid
- Checks signal exists before connecting
- Tracks connections for cleanup
- Returns false on failure (doesn't throw)

### Cleanup

```csharp
public override void _Notification(int what)
{
	if (what == NotificationExitTree)
	{
		var inputManager = GetAutoload<InputManager>();
		if (GodotObject.IsInstanceValid(inputManager))
			inputManager.TouchStarted -= OnTouchStarted;
	}
}
```

## Context Detection

```csharp
if (GameHost.IsDevelopmentContext())
{
	// Enhanced logging, debug features, bypass credits
}

if (GameHost.IsProductionContext())
{
	// Validate required services, real credit system
}
```

## DelayAsync Utilities

```csharp
// For AutoloadBase-derived classes
await DelayAsync(1.0f);

// For utility classes not derived from AutoloadBase
await AutoloadBase.StaticDelayAsync(1.0f);
```

Both use Godot timers (frame-aware, respects pause). See `agent_docs/godot-async-patterns.md` for full async patterns.
