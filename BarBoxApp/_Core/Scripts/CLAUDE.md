# Core Systems

Service-oriented architecture using Godot's autoload pattern with optional framework integration. Games can work standalone or leverage the full platform.

## Key Patterns

- **AutoloadBase**: All services extend it for initialization, logging, service discovery, signal management
- **Service Discovery**: `GetAutoload<T>()` with group-based primary + node-path fallback
- **Template-Instance**: Resources for config, Nodes for runtime state
- **Optional Integration**: Services gracefully handle missing dependencies

## Core Services

| Service | Purpose |
|---------|---------|
| `GameHost` | Game orchestration, overlay loading, credit management, player sessions |
| `GameRegistry` | Game metadata, availability checking, scene path resolution |
| `SessionManager` | Phone/Username/PIN authentication, session timeout management |
| `UserManager` | Compatibility wrapper bridging to SessionManager |
| `InputManager` | Unified touch/mouse input, multi-touch support |

## Components

| Component | Purpose |
|-----------|---------|
| `InputComponent` | Reusable input handling with configurable radius |
| `MovementComponent` | Physics-based movement and collision |

## Game Development Base Classes

- **GameController** (`Gameplay/GameController.cs`) - Game lifecycle, player management, scoring, timer
- **BasePlayer** (`Gameplay/BasePlayer.cs`) - Component composition, user session integration, input handling

## Signal Protocol

Games optionally emit for GameHost integration:
- `GameStarted`, `GameEnded`, `GamePaused`, `GameResumed`
- `ScoreChanged(playerId, newScore)`, `PlayerAdded(playerId)`, `PlayerRemoved(playerId)`

## Development Mode

Games work without framework services:
- Direct scene loading in editor
- Auto-start for immediate testing
- No authentication or credit system required
- Full game functionality standalone

## Reference

- Service patterns: `../agent_docs/autoload-service-patterns.md`
- Async patterns: `../agent_docs/godot-async-patterns.md`
- Backend process management: `../agent_docs/backend-process-management.md`
