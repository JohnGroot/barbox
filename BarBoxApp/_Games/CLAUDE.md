# Game Development Guide

Games are self-contained and context-aware, working both standalone (development) and with GameHost (production).

## Architecture Patterns

| Pattern | When to Use | Example |
|---------|-------------|---------|
| **Consolidation** (preferred for simple) | < 10 mechanics, idle/incremental | MiningGame |
| **Multi-Component** | Complex systems, multiple modes | CarromGame |
| **Physics-Heavy** | Real-time, performance-critical | RacingGame |

## Consolidation Pattern

Keep the file count small, organized around a core split (MiningGame is the
reference — ~10 files across `Logic/`, `Data/`, `Visual/`, `Services/`):

- `Game.cs` - Main game controller (extends `GameController`)
- `Engine.cs` / `State.cs` - Tick logic and domain state
- `GameUI.cs` - Complete UI with nested helpers
- `GameConfig.cs` - Unified config (mechanics + UI + locations)
- `*EventService.cs` - Backend calls (extends `GameEventServiceBase`)

## Game Lifecycle Rules

`GameController._Ready()`/`_ExitTree()` are sealed and own the phase order
(service discovery → components → UI/context → async init → activate; and
teardown → session close on exit). Plug in via the `On*` hooks instead of
writing your own start/stop flow:

1. Discover game-specific services in `OnDiscoverServices()`, build
   engine/state/UI in `OnInitializeComponents()`
2. Do async work (backend loads) in `OnInitializeAsync()`; start gameplay in
   `OnActivateGame()` — called after all initialization completes
3. In development (no `GameHost`), `OnActivateGame()` still fires — a
   standalone scene auto-activates without extra wiring
4. In production, `GameHost` drives the same lifecycle through the base
   class; games don't special-case "wait for GameHost to start"
5. Clean up game-specific state in `OnGameTeardown()` — the base class
   already closes the backend session

## UI Requirements

All games must include: HUD (score/timer/pause), pause overlay, game over overlay.

## Signal Guidelines

- **External only**: `GameStarted`, `GameEnded`, `ScoreChanged` for GameHost
- **Direct method calls** for all internal communication
- **Avoid signal chains** (A signals B signals C = use direct calls)
- **Cost caching is a code smell** for excessive signal usage

## Development Workflow

1. Load game scene directly in editor (auto-starts)
2. Test production integration through MainController
3. Verify graceful degradation when services unavailable

## Context Detection

`OnActivateGame()` runs automatically in both contexts — no manual deferral
needed. Branch only on what data source to use:

```csharp
protected override void OnActivateGame()
{
	if (Platform.Host != null) { /* production: use the real logged-in session */ }
	else { _playerId = "dev_player"; /* standalone: synthetic session */ }
}
```

## Service Discovery

Games access platform services exclusively through `Platform` (the
`GameContext` populated once in `GameController._Ready()`) —
`Platform.Session`, `Platform.Events`, `Platform.Credits`, `Platform.Host`,
`Platform.UI`, `Platform.Input`, `Platform.Location`, `Platform.Registry`,
`Platform.Notifications`.
Never call `GetInstance()`/`GetAutoload()` or reference a raw `/root/` node
path directly in `_Games/` — those go through `Platform.X` only.

## Reference

- Game patterns & input handling: `../agent_docs/game-development-patterns.md`
- Architectural tenets: `../agent_docs/architectural-tenets.md`
