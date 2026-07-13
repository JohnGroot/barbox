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

1. Check `_gameActive` before state changes
2. Reset state in `StartGame()`, cleanup in `EndGame()`
3. Auto-start in dev context via `CallDeferred(MethodName.StartGame)`
4. In production, wait for GameHost to trigger start

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

```csharp
if (Platform.Host != null) { /* production */ }
else { _playerId = "dev_player"; CallDeferred(MethodName.StartGame); }
```

## Service Discovery

Games access platform services exclusively through `Platform` (the
`GameContext` populated once in `GameController._Ready()`) —
`Platform.Session`, `Platform.Events`, `Platform.Credits`, `Platform.Host`,
`Platform.UI`, `Platform.Input`, `Platform.Location`, `Platform.Registry`.
Never call `GetInstance()`/`GetAutoload()` or reference a raw `/root/` node
path directly in `_Games/` — those go through `Platform.X` only.

## Reference

- Game patterns & input handling: `../agent_docs/game-development-patterns.md`
- Architectural tenets: `../agent_docs/architectural-tenets.md`
