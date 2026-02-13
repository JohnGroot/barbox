# Game Development Guide

Games are self-contained and context-aware, working both standalone (development) and with GameHost (production).

## Architecture Patterns

| Pattern | When to Use | Example |
|---------|-------------|---------|
| **Consolidation** (preferred for simple) | < 10 mechanics, idle/incremental | MiningGame (4 files) |
| **Multi-Component** | Complex systems, multiple modes | CarromGame |
| **Physics-Heavy** | Real-time, performance-critical | RacingGame |
| **Single-File** | Rapid prototyping, < 500 lines | SimpleSampleGame |

## Consolidation Pattern (4 files max)

- `GameTypes.cs` - All enums, data structures
- `GameUI.cs` - Complete UI with nested helpers
- `Game.cs` - Main game with nested Engine/State
- `GameConfig.cs` - Unified config (mechanics + UI + locations)

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
var gameHost = GameHost.GetInstance();
if (gameHost != null) { /* production */ }
else { _playerId = "dev_player"; CallDeferred(MethodName.StartGame); }
```

## Reference

- Game patterns & input handling: `../agent_docs/game-development-patterns.md`
- Architectural tenets: `../agent_docs/architectural-tenets.md`
