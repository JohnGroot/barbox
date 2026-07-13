# BarBox Core API Reference

This document serves as the single source of truth for game developers building on the BarBox platform. It covers the GameController lifecycle, service access patterns, and common integration scenarios.

## Quick Start

Every game extends `GameController` and gets automatic access to platform services via the `Platform` property:

```csharp
public partial class MyGame : GameController
{
    protected override string GetGameId() => "my_game";

    protected override void OnInitializeComponents()
    {
        // Platform is already available - no boilerplate needed
        _ui = GetNode<MyGameUI>("UI");
        _state = new MyGameState();
    }

    protected override void OnActivateGame()
    {
        // Start gameplay after all initialization complete
        if (Platform.HasActiveUser)
            StartGameplay();
        else
            ShowLoginPrompt();
    }

    protected override void OnUserLoggedIn(UserSession session)
    {
        // Auto-connected by base class - no signal wiring needed
        _ = StartSessionAsync(session);
    }
}
```

---

## Platform Property

The `Platform` property provides access to all platform services. It is populated in `_Ready()` before any game code executes.

```csharp
protected GameContext Platform { get; private set; }
```

### Available Services

| Service | Purpose | Access |
|---------|---------|--------|
| `Platform.Session` | User authentication, sessions | `SessionManager` |
| `Platform.Events` | Event sourcing, backend communication | `SessionEventService` |
| `Platform.Credits` | Credit management | `CreditService` |
| `Platform.Location` | Box/venue configuration | `LocationManager` |
| `Platform.Host` | Game orchestration, lifecycle | `GameHost` |
| `Platform.UI` | UI overlays, modals | `UIManager` |
| `Platform.Input` | Unified touch/mouse input | `InputManager` |
| `Platform.Registry` | Game metadata | `GameRegistry` |

### Convenience Properties

```csharp
// Context detection (cached via BuildContext)
Platform.IsProduction      // True for exported builds
Platform.IsDevelopment     // True for editor play or tool scripts
Platform.ShouldBypassCredits  // True in development

// Location information
Platform.VenueName         // e.g., "best_intentions"
Platform.BoxName           // e.g., "besties_box_1"
Platform.BoxId            // UUID for backend authentication

// User session
Platform.PrimaryUser       // Current logged-in user (UserSession)
Platform.HasActiveUser     // True if a user is logged in
```

---

## Game Lifecycle

GameController uses sealed orchestration to ensure proper initialization flow. Override the `On*` methods to hook into each phase.

### Initialization Phases (in `_Ready()`)

```
Phase 0: Platform context created (services available)
    │
    ▼
Phase 1: Game metadata loaded, registered with GameHost
    │
    ▼
Phase 2: OnDiscoverServices() - additional service discovery
    │
    ▼
Phase 3: OnInitializeComponents() - create game components
    │
    ▼
Phase 4: UI context setup (top menu, help content)
    │
    ▼
Phase 5: OnInitializeAsync() → OnActivateGame()
```

### Virtual Override Points

| Method | When Called | Purpose |
|--------|-------------|---------|
| `GetGameId()` | Phase 1 | **Required.** Return game ID for registry lookup |
| `OnDiscoverServices()` | Phase 2 | Discover additional game-specific services |
| `OnInitializeComponents()` | Phase 3 | Create game components (engine, state, UI) |
| `OnInitializeAsync()` | Phase 5 (async) | Async initialization (backend calls, data loading) |
| `OnActivateGame()` | Phase 5 (after async) | Start gameplay or show initial state |
| `OnUserLoggedIn(session)` | Runtime | Handle user login (auto-connected signal) |
| `OnUserLoggedOut(phoneNumber)` | Runtime | Handle user logout (auto-connected signal) |
| `OnGameTeardown()` | `_ExitTree()` | Game-specific cleanup |
| `OnInitializationFailed(ex)` | On async error | Handle async initialization failure |

### Cleanup Phase (in `_ExitTree()`)

```
Phase 1: UI context cleared (top menu, help)
    │
    ▼
Phase 2: User signals disconnected
    │
    ▼
Phase 3: OnGameTeardown() - game-specific cleanup
```

---

## Common Patterns

### Backend Event Emission

```csharp
private async Task SaveScoreAsync(int score)
{
    var result = await Platform.Events.EmitEventAsync(
        Platform.BoxId,
        Platform.PrimaryUser.PlayerId,
        "game/score",
        new { score, gameId = GameId }
    );

    if (!result.IsSuccess)
        GD.PrintErr($"Failed to save score: {result.Error}");
}
```

### Credit Checking

```csharp
private async Task<bool> TrySpendCreditsAsync(int amount)
{
    if (Platform.ShouldBypassCredits)
        return true;  // Dev mode - bypass credit system

    if (Platform.Credits == null)
        return true;  // Credits unavailable - allow play

    return await Platform.Credits.TrySpendAsync(
        Platform.PrimaryUser.PhoneNumber,
        amount,
        $"{GameId}_play"
    );
}
```

### User Session Handling

```csharp
protected override void OnUserLoggedIn(UserSession session)
{
    // Session contains user data
    var phoneNumber = session.PhoneNumber;
    var username = session.GlobalData?.UserName ?? "Player";

    // Start game session
    _ = StartUserSessionAsync(session);
}

protected override void OnUserLoggedOut(string phoneNumber)
{
    // Clean up user-specific state
    ResetGameState();
    ShowLoginPrompt();
}
```

### Context-Aware Development

```csharp
protected override void OnActivateGame()
{
    if (Platform.IsDevelopment)
    {
        // Auto-start for testing in editor
        StartGameplay();
    }
    else
    {
        // Production - wait for user login
        if (Platform.HasActiveUser)
            StartGameplay();
        else
            ShowLoginPrompt();
    }
}
```

### Help Content

```csharp
protected override HelpContentData GetHelpContent()
{
    return new HelpContentData("My Game Help")
        .AddSection("How to Play", "Touch the screen to move your character.")
        .AddSection("Scoring", "Collect gems to earn points.")
        .AddSection("Power-ups", "Blue orbs give temporary invincibility.");
}
```

### Custom Context Buttons

```csharp
public override ContextButtonData[] GetContextButtons()
{
    // IsPaused is managed by base class Pause()/Resume() methods
    var pauseButton = IsPaused
        ? GameContextButton.CreateResumeButton(() => Resume())
        : GameContextButton.CreatePauseButton(() => Pause());

    return [
        pauseButton,
        GameContextButton.CreateReturnToMenuButton(() => {
            Platform.Session?.ResetAllIdleTimers();
            ReturnToMainMenu();
        })
    ];
}
```

---

## Pause/Resume System

GameController provides built-in pause management:

```csharp
// Base class methods
public void Pause();   // Sets IsPaused = true, calls OnPause()
public void Resume();  // Sets IsPaused = false, calls OnResume()

// Override for game-specific behavior
protected override void OnPause()
{
    _engine.StopProcessing();
    Platform.Host?.NotifyGamePaused();
}

protected override void OnResume()
{
    _engine.StartProcessing();
    Platform.Host?.NotifyGameResumed();
}
```

---

## Platform Signals

Notify the platform about game lifecycle events:

```csharp
// Called when domain-specific game session starts
Platform.Host?.NotifyGameStarted();

// Called when domain-specific game session ends
Platform.Host?.NotifyGameEnded();

// Called when game is paused
Platform.Host?.NotifyGamePaused();

// Called when game is resumed
Platform.Host?.NotifyGameResumed();
```

---

## BuildContext

For context detection outside of GameController, use `BuildContext` directly:

```csharp
using BarBox.Core.Gameplay;

// Cached context detection (values never change at runtime)
if (BuildContext.IsDevelopment)
{
    EnableDebugFeatures();
}

if (BuildContext.IsProduction)
{
    InitializeAnalytics();
}

// Human-readable context description
GD.Print(BuildContext.GetContextDescription());
// → "Development: Launched from Godot editor"
// → "Production: Exported standalone build"
```

---

## Best Practices

1. **Don't override `_Ready()` or `_ExitTree()`** - They're sealed. Use the `On*` methods instead.

2. **Trust lifecycle guarantees** - Platform services are available in all `On*` methods. Don't add defensive null checks for Platform.

3. **Use null-conditional for optional services** - Services inside Platform may be null in development:
   ```csharp
   Platform.Host?.NotifyGameStarted();  // Host may be null
   Platform.Credits?.GetBalance();       // Credits may be null
   ```

4. **Check `Platform.HasActiveUser`** - Before accessing `Platform.PrimaryUser`.

5. **Let base class manage signals** - Don't manually connect to SessionManager signals.

6. **Use `OnGameTeardown()` for cleanup** - Not `_ExitTree()`.

7. **Check context for dev features**:
   ```csharp
   if (Platform.IsDevelopment)
       EnableDebugOverlay();
   ```

---

## File Reference

| File | Purpose |
|------|---------|
| `_Core/Scripts/Gameplay/GameContext.cs` | Service container for Platform property |
| `_Core/Scripts/Gameplay/GameController.cs` | Base class with sealed orchestration |
| `_Core/Scripts/_Utils/BuildContext.cs` | Cached build context detection |
| `_Core/Scripts/Autoloads/GameHost.cs` | Game lifecycle orchestration |
| `_Core/Scripts/Autoloads/SessionManager.cs` | User authentication and sessions |
| `_Core/Scripts/Autoloads/SessionEventService.cs` | Backend event emission |
