# TweenConstants Reference

`TweenConstants` provides cached constants for tween properties to reduce GC allocation during animations.

## Why

- Eliminates repeated `StringName` creation for tween properties
- Reduces enum boxing when passing transition/ease types
- Provides consistent duration constants

## Property Names (StringName)

| Constant | Value |
|----------|-------|
| `Position` | `"position"` |
| `GlobalPosition` | `"global_position"` |
| `Rotation` | `"rotation"` |
| `Scale` | `"scale"` |
| `Modulate` | `"modulate"` |
| `ModulateAlpha` | `"modulate:a"` |
| `Visible` | `"visible"` |
| `Size` | `"size"` |
| `Zoom` | `"zoom"` |
| `Offset` | `"offset"` |
| `VolumeDb` | `"volume_db"` |
| `PitchScale` | `"pitch_scale"` |

## Transition Types (no boxing)

`TransLinear`, `TransSine`, `TransQuad`, `TransCubic`, `TransQuart`, `TransQuint`, `TransExpo`, `TransCirc`, `TransBack`, `TransElastic`, `TransBounce`

## Ease Types (no boxing)

`EaseIn`, `EaseOut`, `EaseInOut`, `EaseOutIn`

## Duration Constants

| Constant | Value |
|----------|-------|
| `QuickDuration` | 0.1f |
| `FastDuration` | 0.2f |
| `MediumDuration` | 0.3f |
| `SlowDuration` | 0.5f |
| `VerySlowDuration` | 1.0f |

## Usage

```csharp
// Instead of:
tween.TweenProperty(node, "modulate:a", 1.0f, 0.3f);
tween.SetTrans(Tween.TransitionType.Sine);

// Use:
tween.TweenProperty(node, TweenConstants.ModulateAlpha, 1.0f, TweenConstants.MediumDuration);
tween.SetTrans(TweenConstants.TransSine);
tween.SetEase(TweenConstants.EaseOut);
```
