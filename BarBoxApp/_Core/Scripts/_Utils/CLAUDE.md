# Utility Classes

## AutoloadBase

Standardized foundation for all autoload services:
- Lifecycle management (`OnServiceReady` / `OnServiceDestroyed`)
- Logging with service prefix (`LogInfo`, `LogError`, `LogWarning`)
- Service discovery (`GetAutoload<T>()` with dual-discovery resilience)
- Signal management (`TryConnectSignal` / `TryDisconnectSignal`)
- Async utilities (`DelayAsync` / `StaticDelayAsync` for frame-aware timing)

See `../agent_docs/autoload-service-patterns.md` for full patterns and examples.

## TweenConstants

Cached constants for tween properties to reduce GC allocation:
- `StringName` properties: `ModulateAlpha`, `GlobalPosition`, `Scale`, etc.
- Transition types (no boxing): `TransSine`, `TransBack`, `TransElastic`, etc.
- Ease types (no boxing): `EaseIn`, `EaseOut`, `EaseInOut`
- Duration constants: `QuickDuration` (0.1f) through `VerySlowDuration` (1.0f)

```csharp
tween.TweenProperty(node, TweenConstants.ModulateAlpha, 1.0f, TweenConstants.MediumDuration);
tween.SetTrans(TweenConstants.TransSine);
tween.SetEase(TweenConstants.EaseOut);
```

See `../agent_docs/tween-constants-reference.md` for full reference.

## LineUtils

Geometric utility for track boundary checks and distance calculations:
- `ClosestPointOnLineSegment` - Uses `LengthSquared()` to avoid sqrt
- `IsPointNearLine` - Domain-aware with Godot `Line2D` semantics
- All methods use performance-first approach (squared distances, pre-calculated segment properties)
