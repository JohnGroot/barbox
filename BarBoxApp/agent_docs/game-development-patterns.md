# Game Development Patterns

## Progressive Complexity Patterns

### Single-File Pattern
For rapid prototyping and games < 500 lines. Complete game in one file with built-in UI. (No current game uses this; it's a prototyping starting point.)

### Multi-Component Pattern (CarromGame)
For complex games with distinct systems. Delegate to specialized managers (PhaseManager, InputController, CameraController).

### Physics-Heavy Pattern (RacingGame)
For performance-critical real-time games. Specialized systems with direct input polling for minimal latency.

### Consolidation Pattern (MiningGame) - Preferred for simple games
Small core split (MiningGame is ~10 files across `Logic/`, `Data/`, `Visual/`, `Services/`):
- `Game.cs` - Main game controller (extends `GameController`)
- `Engine.cs` / `State.cs` - Tick logic and domain state
- `GameUI.cs` - Complete UI system with nested helper classes
- `GameConfig.cs` - Unified configuration (mechanics + UI + locations)
- `*EventService.cs` - Backend calls (extends `GameEventServiceBase`)

**Key principles:** Direct method calls over signals, nested classes for related functionality, unified configuration, eliminated indirection.

## Choosing a Pattern

| Criteria | Pattern |
|----------|---------|
| Simple idle/incremental, < 10 mechanics | Consolidation |
| Complex systems, multiple game modes | Multi-Component |
| Rapid prototyping, < 500 lines | Single-File |
| Real-time physics, performance-critical | Physics-Heavy |

## Game Lifecycle

```csharp
public void StartGame()
{
	if (_gameActive) return;
	_gameActive = true;
	_gamePaused = false;
	_score = 0;
	EmitSignal(SignalName.PlayerAdded, _playerId);
	EmitSignal(SignalName.GameStarted);
}
```

## Context-Aware Design

```csharp
private void DetectAndAdaptToContext()
{
	_gameHost = GameHost.GetInstance();
	if (_gameHost != null && GodotObject.IsInstanceValid(_gameHost))
	{
		_isProductionContext = true;
		_playerId = _gameHost.GetPlayerSession("default")?.PlayerId;
	}
	else
	{
		_isProductionContext = false;
		_playerId = "dev_player";
		CallDeferred(MethodName.StartGame);  // Auto-start for testing
	}
}
```

## Signal Guidelines

- **External signals only** for GameHost integration (GameStarted, GameEnded, ScoreChanged)
- **Direct method calls** for internal game communication
- **Avoid signal chains** - if A signals B which signals C, use direct calls
- **Cost caching is a code smell** - usually indicates excessive signal usage

## Input Handling

### Simple games
Handle `InputEventScreenTouch` and `InputEventMouseButton` uniformly in `_Input()`.

### Complex games (CarromGame)
Gesture recognition with mode detection (PowerAiming vs LateralMovement).

### Performance-critical (RacingGame)
Direct polling via `InputManager.GetAutoload()` for minimal latency. Optional smoothing via Lerp.

### Component-based
Use `InputComponent` for consistent handling with configurable radius and acceptance.

## Built-in UI Requirements

All games should include:
- HUD (score, timer, pause button)
- Pause overlay with resume/restart
- Game over overlay with final score and play again
