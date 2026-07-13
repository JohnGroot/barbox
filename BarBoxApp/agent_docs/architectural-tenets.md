# Architectural Tenets

Six core tenets that guide BarBox game development.

## 1. Phased Initialization with Guarantees

`GameController._Ready()` is sealed and owns the phase order (service
context, registry lookup, `OnDiscoverServices`, `OnInitializeComponents`,
UI/context setup, `OnInitializeAsync`, `OnActivateGame`). Games plug into
this by overriding the `On*` hooks, not by re-deriving their own lifecycle.
Validate requirements at phase boundaries, not during runtime.

```csharp
/// PHASE: Service Discovery - Platform is already populated here.
/// POST-GUARANTEES: _eventService and _miningEventService exist and are valid (REQUIRED)
protected override void OnDiscoverServices()
{
	_eventService = Platform.Events;
	if (_eventService == null)
		throw new InvalidOperationException("SessionEventService is required but not available");

	_miningEventService = new MiningEventService(_eventService);
}

/// PHASE: Component Initialization
/// POST-GUARANTEES: _engine, _state, _ui exist and are valid
protected override void OnInitializeComponents()
{
	if (_locationDataRegistry.Count == 0)
		throw new InvalidOperationException("No LocationDataResources configured.");

	_engine = new MiningEngine(this);
	_state = new MiningState(this);
	_ui = GetNode<MiningGameUI>(UIPath);
}
```

**Benefits:** Errors surface at startup, clear contracts per phase, eliminates defensive null checks in hot paths.

## 2. Sequential Execution for Traceability

Logic executes sequentially for easy tracing. Avoid deferred execution in favor of explicit phase ordering.

```csharp
// GOOD: Sequential async - clear execution flow
private async void InitializeGameSession()
{
	var sessionResult = await _eventService.CreateActivitySessionAsync(...);
	if (!sessionResult.IsSuccess) { GD.PrintErr($"Failed: {sessionResult.Error}"); return; }
	await _state.LoadUserDataAsync();
	if (!IsInstanceValid(_state)) return;
	_engine.StartMining();
}

// AVOID: Parallel/deferred - hard to trace
var task1 = LoadDataAsync();
var task2 = CreateSessionAsync();
await Task.WhenAll(task1, task2);
CallDeferred(MethodName.StartEngine);
```

**Key rules:** Always `await` each step. Avoid `Task.WhenAll()` unless parallelism explicitly needed. Use explicit phase ordering instead of `CallDeferred`.

## 3. Guaranteed Components, Minimal Null Checks

Architecture ensures components exist via lifecycle phases. Remove defensive null checks where guarantees apply.

**When to null-check:**
- After `await` - user can logout, scene can be destroyed
- During cleanup (`_Notification(NotificationExitTree)`) - objects may be freeing

**Never null-check:**
- In hot paths (`_Process`, `_PhysicsProcess`) - guaranteed by initialization
- In public API methods - guaranteed by initialization

**Null semantics:** Null checks indicate *optionality*, not defensive programming.

```csharp
_gameHost?.NotifyEvent();           // GameHost IS optional in dev mode
_engine?.StartMining();             // If _engine is required, this HIDES BUGS
```

**Rule of thumb:** If removing `?.` would cause a crash that should never happen, the `?.` is hiding a bug.

**Service categories:**

| Category | Examples | Strategy |
|----------|----------|----------|
| Required (Production) | SessionManager, SessionEventService | Throw in `OnDiscoverServices()` if null |
| Optional Platform | GameHost, CreditService | Null-conditional `?.` |
| Game Components | _engine, _state, _ui | No checks after init |

## 4. Result<T> Over Exceptions

Game logic uses Result<T> pattern. Exceptions only for backend I/O.

```csharp
// Game logic - use Result<T>
public Result<int> CalculateScore()
{
	if (!_gameActive) return Result.Failure("Game not active");
	return Result.Success(_score);
}

// Backend I/O - try/catch acceptable
private async Task<Result<SessionData>> CreateBackendSessionAsync()
{
	try { ... }
	catch (Exception ex) { return Result.Failure($"Backend error: {ex.Message}"); }
}
```

## 5. Configuration as Contract

Missing configuration is a developer error. Fail fast at startup, not during gameplay.

```csharp
// Validate configuration at initialization
protected override void OnInitializeComponents()
{
	if (LocationDataResources.Count == 0)
		throw new InvalidOperationException("Location data not configured");
	if (Config.UpgradeCosts.Count == 0)
		throw new InvalidOperationException("Upgrade costs not configured");
}
```

No silent fallbacks that hide bugs.

## 6. Minimal API Surface

Expose only what game implementers need. Hide implementation details.

- Expose only what games need to call
- Mark implementation details as `internal` or `private`
- Keep public interfaces focused and digestible
- Document what each public method guarantees
