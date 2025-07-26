# Racing Game Technical Implementation Reference

**BarBox Racing Game** - A comprehensive 2D time trial racing system built with Godot 4.4 and C#. This document provides detailed technical specifications, implementation patterns, and API reference for developers.

## Table of Contents

- [System Architecture](#system-architecture)
- [Class Hierarchies & Dependencies](#class-hierarchies--dependencies)
- [Signal Definitions & Event Chains](#signal-definitions--event-chains)
- [Data Structures & Algorithms](#data-structures--algorithms)
- [Execution Flow & Processing](#execution-flow--processing)
- [API Contracts & Interfaces](#api-contracts--interfaces)
- [Performance Considerations](#performance-considerations)
- [Configuration Systems](#configuration-systems)

## System Architecture

### Component Dependency Graph
```
RacingGame (GameController)
├── RacingTimingSystem : Node
├── RacingCameraController : Node
├── RacingCarController : RacingPlayer : BasePlayer
├── RacingTrackValidationSystem : Node  
├── RacingUIManager : Node
├── RacingVisualFeedbackRenderer : Node2D
└── TrackDefinition : Node2D : ITrackDefinition
    ├── LineTrigger : Area2D (start/finish)
    └── CheckpointTrigger : LineTrigger
```

### Service Integration Points
- **InputManager** (Autoload): Touch/mouse input handling
- **CreditManager** (Service): Credit verification and spending
- **UserManager** (Autoload): Session management and idle timers
- **GameHost** (Service): Platform integration and menu navigation

## Class Hierarchies & Dependencies

### RacingGame.cs
**Inheritance**: `GameController`
**Key Dependencies**: All racing subsystems
**Initialization Order**:
1. [`SetupTimingSystem()`](RacingGame.cs:57) - Creates RacingTimingSystem with TargetLaps=3
2. [`SetupTrackValidationSystem()`](RacingGame.cs:62) - Configures penalty values  
3. [`SetupCameraController()`](RacingGame.cs:67) - Creates camera with ScreenEdgeColliderThickness=8.0f
4. [`SetupCarController()`](RacingGame.cs:72) - Requires camera and validation systems
5. [`SetupVisualRenderer()`](RacingGame.cs:82) - Depends on car controller
6. [`InitializeTrackSystem()`](RacingGame.cs:87) - Loads tracks and connects signals

**Core Configuration**:
```csharp
// System defaults applied during setup
_trackValidationSystem.CenterLineProximityRange = 40.0f;
_trackValidationSystem.CenterLineAccelerationBonus = 1.5f;
_trackValidationSystem.OffTrackSpeedPenalty = 0.3f;
_carController.MaxSpeed = 1800.0f;
_carController.AccelerationRate = 300.0f;
```

### RacingTimingSystem.cs  
**Inheritance**: `Node`
**State Management**: `RacingState` enum (Stopped, Countdown, Racing, Finished)
**Data Structures**:
```csharp
private Dictionary<string, int> _playerCurrentLap = new();
private Dictionary<string, List<float>> _playerLapTimes = new();
private Dictionary<string, float> _playerCurrentLapTime = new();
private Dictionary<string, float> _playerBestLapTime = new();
private Dictionary<string, float> _playerGapTime = new();
```

**Countdown Algorithm**:
```csharp
// RacingTimingSystem.cs:127
public bool UpdateCountdown(float delta, float countdownDuration = 4.0f)
{
    _countdownTime += delta;
    int newCountdownNumber = 4 - (int)(_countdownTime / 1.0f); // 3-2-1-GO
    bool numberChanged = (newCountdownNumber != _countdownNumber);
    return _countdownTime >= countdownDuration; // True when complete
}
```

### RacingCameraController.cs
**Inheritance**: `Node`
**Camera Positioning Algorithm**:
```csharp
// RacingCameraController.cs:89
public void PositionCameraOverTrack()
{
    Vector2 playableArea = new Vector2(viewportSize.X, viewportSize.Y - TOP_MENU_HEIGHT);
    Vector2 paddedTrackSize = trackBounds.Size * (1.0f + 0.15f * 2.0f); // 15% padding
    float scaleX = playableArea.X / paddedTrackSize.X;
    float scaleY = playableArea.Y / paddedTrackSize.Y;
    float zoom = Mathf.Clamp(Mathf.Min(scaleX, scaleY), 0.2f, 1.0f);
    _trackCamera.Zoom = new Vector2(zoom, zoom);
}
```

**Screen Edge Collider Creation**:
- Creates 4 StaticBody2D colliders around camera viewport
- Thickness controlled by `ScreenEdgeColliderThickness` property
- Positioned relative to camera view bounds accounting for TOP_MENU_HEIGHT (150px)

### RacingCarController.cs
**Inheritance**: `RacingPlayer : BasePlayer`
**Key Override Methods**:
```csharp
// RacingCarController.cs:24-32
protected override Vector2 TransformScreenToWorldPosition(Vector2 screenPosition)
    => _cameraController?.TransformScreenToWorldPosition(screenPosition) ?? screenPosition;

protected override float GetSpeedModifier()
    => _trackValidationSystem?.GetSpeedModifier(GetCarBody()?.GlobalPosition ?? Vector2.Zero) ?? 1.0f;

protected override float GetAccelerationModifier()
    => _trackValidationSystem?.GetAccelerationModifier(GetCarBody()?.GlobalPosition ?? Vector2.Zero) ?? 1.0f;
```

### RacingPlayer.cs
**Inheritance**: `BasePlayer`
**Physics Update Cycle**:
```csharp
// RacingPlayer.cs:178-201
protected virtual void UpdateCarPhysics(float delta)
{
    // 1. Calculate input direction and target speed
    var normalizedDistance = Mathf.Clamp(distanceToTarget / MaxInputDistance, 0.0f, 1.0f);
    targetSpeed = Mathf.Lerp(MinSpeed, MaxSpeed, normalizedDistance) * GetSpeedModifier();
    
    // 2. Update current speed with acceleration/deceleration
    if (targetSpeed > _currentSpeed)
        _currentSpeed = Mathf.MoveToward(_currentSpeed, targetSpeed, AccelerationRate * GetAccelerationModifier() * delta);
    else
        _currentSpeed = Mathf.MoveToward(_currentSpeed, targetSpeed, DecelerationRate * delta);
    
    // 3. Apply rotation with turn modifier
    var effectiveRotationSpeed = RotationLerpSpeed * GetTurnModifier() * delta;
    _carBody.Rotation = Mathf.LerpAngle(_carBody.Rotation, targetAngle, effectiveRotationSpeed);
    
    // 4. Calculate velocity and apply movement
    var carForward = new Vector2(Mathf.Sin(_carBody.Rotation), -Mathf.Cos(_carBody.Rotation));
    _velocity = carForward * _currentSpeed * GetTurnModifier();
    _carBody.Velocity = _velocity;
    _carBody.MoveAndSlide();
}
```

### RacingTrackValidationSystem.cs
**Penalty State Management**:
```csharp
// RacingTrackValidationSystem.cs:98-104
public void UpdateOffTrackPenalties(Vector2 carPosition, float delta)
{
    bool isOnTrack = IsOnTrack(carPosition);
    float targetSpeedMultiplier = isOnTrack ? 1.0f : OffTrackSpeedPenalty;
    float lerpSpeed = OffTrackPenaltyLerpSpeed * delta;
    _currentSpeedPenaltyMultiplier = Mathf.Lerp(_currentSpeedPenaltyMultiplier, targetSpeedMultiplier, lerpSpeed);
}
```

**Center Line Bonus Logic**:
```csharp
// RacingTrackValidationSystem.cs:144-154
public float GetAccelerationModifier(Vector2 carPosition)
{
    float baseModifier = _currentAccelerationPenaltyMultiplier;
    if (IsOnTrack(carPosition))
    {
        var distanceToCenter = GetDistanceToTrackCenterLine(carPosition);
        if (distanceToCenter <= CenterLineProximityRange)
            baseModifier = Mathf.Max(baseModifier, CenterLineAccelerationBonus);
    }
    return baseModifier;
}
```

## Signal Definitions & Event Chains

### Primary Signal Chain
```csharp
// RacingGame.cs
[Signal] public delegate void LapCompletedEventHandler(string playerId, int lapNumber, float lapTime);
[Signal] public delegate void RaceCompletedEventHandler(string playerId, float totalTime);
[Signal] public delegate void CheckpointCrossedEventHandler(string playerId, int checkpointIndex, float gapTime);

// RacingTimingSystem.cs - Same signatures, forwarded through RacingGame
[Signal] public delegate void LapCompletedEventHandler(string playerId, int lapNumber, float lapTime);
[Signal] public delegate void RaceCompletedEventHandler(string playerId, float totalTime);
[Signal] public delegate void CheckpointCrossedEventHandler(string playerId, int checkpointIndex, float gapTime);

// RacingPlayer.cs
[Signal] public delegate void CarMovedEventHandler(Vector2 position, Vector2 velocity);
[Signal] public delegate void CarRotatedEventHandler(float rotation);

// RacingUIManager.cs
[Signal] public delegate void TimeTrialRequestedEventHandler();
[Signal] public delegate void RestartRequestedEventHandler();
[Signal] public delegate void TrackSwitchRequestedEventHandler(int trackIndex);
```

### Signal Connection Patterns
```csharp
// RacingGame.cs:148-156 - Forward timing signals to maintain external compatibility
_timingSystem.LapCompleted += (playerId, lapNumber, lapTime) => 
{
    if (_currentGameMode == GameMode.Practice)
        UpdateScore(playerId, _timingSystem.GetPlayerBestLapTime(playerId));
    else
        UpdateScore(playerId, _timingSystem.CalculatePlayerScore(playerId, _currentGameMode));
    EmitSignal(SignalName.LapCompleted, playerId, lapNumber, lapTime);
};

// RacingGame.cs:164 - Car movement triggers visual feedback
_carController.CarMoved += OnCarMoved;
```

### Track Trigger Event Flow
```csharp
// LineTrigger.cs:28-32 - base implementation
protected virtual void OnBodyEntered(Node2D body)
{
    EmitSignal(SignalName.TriggerHit, body, this);
    EmitSignal(SignalName.StartLineCrossed, body);
}

// CheckpointTrigger.cs:31-35 - override
protected override void OnBodyEntered(Node2D body)
{
    EmitSignal(SignalName.TriggerHit, body, this);
    EmitSignal(SignalName.CheckpointCrossed, body, CheckpointIndex);
}
```

## Data Structures & Algorithms

### Track Definition & Validation
**ITrackDefinition Interface**:
```csharp
public interface ITrackDefinition
{
    void SetupTrack();
    Curve2D GetTrackCurve();
    bool IsValidTrackPoint(Vector2 point);
    Vector2 GetStartLinePosition();
    Vector2 GetStartLineDirection();
}
```

**Track Bounds Calculation**:
```csharp
// TrackDefinition.cs:67-80
public Rect2 GetTrackBounds()
{
    // Find min/max points from Line2D
    Vector2 minPoint = TrackLine.GetPointPosition(0);
    Vector2 maxPoint = TrackLine.GetPointPosition(0);
    for (int i = 1; i < TrackLine.GetPointCount(); i++)
    {
        Vector2 point = TrackLine.GetPointPosition(i);
        minPoint = new Vector2(Mathf.Min(minPoint.X, point.X), Mathf.Min(minPoint.Y, point.Y));
        maxPoint = new Vector2(Mathf.Max(maxPoint.X, point.X), Mathf.Max(maxPoint.Y, point.Y));
    }
    // Expand by track width
    float halfWidth = TrackLine.Width / 2.0f;
    return new Rect2(minPoint - Vector2.One * halfWidth, (maxPoint - minPoint) + Vector2.One * halfWidth * 2);
}
```

### Visual Feedback Rendering
**Tire Trail Data Structure**:
```csharp
private List<(Vector2 position, ulong timestamp)> _leftTireTrail = new List<(Vector2, ulong)>();
private List<(Vector2 position, ulong timestamp)> _rightTireTrail = new List<(Vector2, ulong)>();
```

**Trail Point Management**:
```csharp
// VisualFeedbackRenderer.cs:142-150
private void AddTrailPoint(List<(Vector2 position, ulong timestamp)> trail, Vector2 point, ulong timestamp)
{
    trail.Add((point, timestamp));
    while (trail.Count > MaxTrailPoints) // Default: 1000
        trail.RemoveAt(0);
}

// VisualFeedbackRenderer.cs:152-162
private void CleanupExpiredTrailPoints(List<(Vector2 position, ulong timestamp)> trail, ulong currentTime)
{
    for (int i = trail.Count - 1; i >= 0; i--)
    {
        var age = currentTime - trail[i].timestamp;
        if (age > TrailLifetime) // Default: 3000ms
            trail.RemoveAt(i);
        else break; // Chronological ordering allows early exit
    }
}
```

**Tire Position Calculation Algorithm**:
```csharp
// VisualFeedbackRenderer.cs:171-182
// Convert car rotation to directional vectors using rotation matrix
var carForward = new Vector2(Mathf.Sin(carBody.Rotation), -Mathf.Cos(carBody.Rotation));
var carRight = new Vector2(carForward.Y, -carForward.X);
var halfLength = carSize.Y * 0.5f;
var halfWidth = carSize.X * 0.5f;
var backOffset = carRight * -halfWidth * 0.75f - carForward * halfLength * 1.5f;
var carBackPosition = carPosition + backOffset;
var tireOffset = halfWidth * 0.625f;
var leftTirePosition = carBackPosition - carRight * tireOffset;
var rightTirePosition = carBackPosition + carRight * tireOffset;
```

### Checkpoint State Management
**CheckpointTrigger State Enum**:
```csharp
public enum CheckpointState
{
    Future,      // Not yet required (yellow)
    NextRequired, // Next required checkpoint (cyan)
    Crossed      // Already crossed (green)
}
```

**Visual State Update Algorithm**:
```csharp
// RacingTimingSystem.cs:198-208
private void UpdateCheckpointVisuals()
{
    for (int i = 0; i < _checkpointTriggers.Length; i++)
    {
        var checkpoint = _checkpointTriggers[i];
        if (i < _nextCheckpointIndex)
            checkpoint.MarkAsCrossed();
        else if (i == _nextCheckpointIndex && GetGameMode() == GameMode.TimeTrial)
            checkpoint.SetNextRequiredState();
        else
            checkpoint.SetFutureState();
    }
}
```

## Execution Flow & Processing

### Frame-by-Frame Processing Order
1. **RacingGame._Process(delta)** [`RacingGame.cs:249`]:
   - [`HandleDirectInput()`](RacingGame.cs:271) - Poll InputManager for touch state
   - [`_timingSystem.UpdateRacingTimers(delta, _players)`](RacingTimingSystem.cs:75) - Update all timing data
   - [`UpdateUI()`](RacingGame.cs:256) - Refresh UI displays

2. **RacingGame.UpdateGame(delta)** [`RacingGame.cs:280`]:
   - [`_trackValidationSystem.UpdateOffTrackPenalties(carPosition, delta)`](RacingTrackValidationSystem.cs:98) - Smooth penalty transitions
   - [`_visualRenderer.UpdateVisualFeedback(delta)`](VisualFeedbackRenderer.cs:85) - Cache calculations, update animations
   - [`_visualRenderer.UpdateTireTrails()`](VisualFeedbackRenderer.cs:130) - Add/cleanup trail points

3. **RacingPlayer._Process(delta)** [`RacingPlayer.cs:97`]:
   - Poll input position from InputManager
   - [`UpdateCarPhysics(delta)`](RacingPlayer.cs:178) - Physics simulation
   - Emit [`CarMoved`](RacingPlayer.cs:19)/[`CarRotated`](RacingPlayer.cs:20) signals

### Initialization Sequence
```csharp
// RacingGame.cs:40-55
public override void _Ready()
{
    // 1. Setup all subsystems (order matters for dependencies)
    SetupTimingSystem();
    SetupTrackValidationSystem(); 
    SetupCameraController();
    SetupCarController(); // Requires camera + validation systems
    SetupUI();
    InitializeTrackSystem();
    
    // 2. Call base to trigger InitializeGame() -> StartPractice()
    base._Ready();
    
    // 3. Context detection and adaptation
    DetectAndAdaptToContext();
}
```

### Track Loading & Signal Connection
```csharp
// RacingGame.cs:108-124
private void SetupLoadedTrack()
{
    _trackDefinition.SetupTrack();
    _trackCurve = _trackDefinition.GetTrackCurve();
    
    // Initialize subsystems with track data
    _trackValidationSystem.Initialize(_trackDefinition, _trackCurve);
    _cameraController.SetTrackDefinition(_trackDefinition);
    _cameraController.PositionCameraOverTrack();
    _cameraController.SetupScreenEdgeColliders();
    
    // Connect trigger signals
    _trackDefinition.StartLine.StartLineCrossed += OnStartLineTriggered;
    foreach(var checkpoint in _trackDefinition.CheckpointTriggers)
        checkpoint.CheckpointCrossed += OnCheckpointTriggered;
}
```

### Game Mode Transitions
```csharp
// RacingGame.cs:194-202 - Practice Mode: Immediate start, continuous laps
public virtual void StartPractice()
{
    SetGameMode(GameMode.Practice);
    ResetRacingData();
    StartGame();
    foreach (var player in _players)
        StartPlayerLap(player.PlayerId);
}

// RacingGame.cs:204-216 - Time Trial: Credit check, countdown, fixed lap count
public virtual async void StartTimeTrial()
{
    if (TimeTrialCreditCost > 0)
    {
        bool creditsSpent = await creditManager.CheckAndSpendCredits(TimeTrialCreditCost, "Time Trial Race");
        if (!creditsSpent) return;
    }
    SetGameMode(GameMode.TimeTrial);
    ResetRacingData();
    if (ShowCountdown) StartCountdown();
    else StartGame();
}
```

## API Contracts & Interfaces

### ITrackDefinition Contract
Required methods for track system integration:
- `void SetupTrack()` - Initialize track geometry and triggers
- `Curve2D GetTrackCurve()` - Provide racing line for validation
- `bool IsValidTrackPoint(Vector2 point)` - Track boundary checking  
- `Vector2 GetStartLinePosition()` - Car positioning reference
- `Vector2 GetStartLineDirection()` - Car orientation reference

### RacingTimingSystem Public API
```csharp
// State queries
public RacingState CurrentRacingState { get; }
public bool IsInCountdown { get; }
public int CountdownNumber { get; }

// Player data accessors  
public int GetPlayerCurrentLap(string playerId);
public float GetPlayerCurrentLapTime(string playerId);
public float GetPlayerBestLapTime(string playerId);
public float GetPlayerGapTime(string playerId);
public List<float> GetPlayerLapTimes(string playerId);

// Race control
public void StartCountdown(float countdownDuration = 4.0f);
public bool UpdateCountdown(float delta, float countdownDuration = 4.0f);
public void StartPlayerLap(string playerId);
public void CompletePlayerLap(string playerId, float lapTime);
```

### RacingTrackValidationSystem Public API
```csharp
// Configuration properties
public float CenterLineProximityRange { get; set; } = 40.0f;
public float CenterLineAccelerationBonus { get; set; } = 1.5f;
public float OffTrackSpeedPenalty { get; set; } = 0.3f;
public float OffTrackTurnPenalty { get; set; } = 0.3f;
public float OffTrackAccelerationPenalty { get; set; } = 0.3f;
public float OffTrackPenaltyLerpSpeed { get; set; } = 3.0f;

// Runtime state
public bool IsCurrentlyOffTrack { get; }
public float CurrentSpeedPenaltyMultiplier { get; }

// Core API
public void Initialize(ITrackDefinition trackDefinition, Curve2D trackCurve);
public bool IsOnTrack(Vector2 position);
public float GetDistanceToTrackCenterLine(Vector2 position);
public void UpdateOffTrackPenalties(Vector2 carPosition, float delta);
public float GetSpeedModifier(Vector2 carPosition);
public float GetAccelerationModifier(Vector2 carPosition);
public float GetTurnModifier(Vector2 carPosition);
```

### Input Validation & Error Handling
```csharp
// RacingCarController.cs:34-49 - validation pattern
public void Initialize(RacingCameraController cameraController, RacingTrackValidationSystem trackValidationSystem, string playerId = "player1", string playerName = "Player")
{
    if (cameraController == null)
        throw new System.ArgumentNullException(nameof(cameraController), "Camera controller is required for car initialization");
    if (trackValidationSystem == null)
        throw new System.ArgumentNullException(nameof(trackValidationSystem), "Track validation system is required for car initialization");
    if (string.IsNullOrEmpty(playerId))
        throw new System.ArgumentException("Player ID cannot be null or empty", nameof(playerId));
    
    _cameraController = cameraController;
    _trackValidationSystem = trackValidationSystem;
    // ... initialization continues
}
```

## Performance Considerations

### Rendering Optimizations
**Cached Visual Calculations**:
```csharp
// VisualFeedbackRenderer.cs:28-34 - maintains cached values to avoid recalculation
private Vector2 _cachedDirectionToTarget = Vector2.Zero;
private float _cachedMovementArcRotation = 0.0f;
private float _cachedArcSweepAngle = 0.0f;
private float _cachedCurrentSpeed = 0.0f;
private bool _cachedValuesValid = false;

// VisualFeedbackRenderer.cs:200-211
private void UpdateCachedValues()
{
    if (_cachedValuesValid) return;
    // Expensive calculations only when target/speed changes
    _cachedDirectionToTarget = (targetPosition - carBody.GlobalPosition).Normalized();
    _cachedMovementArcRotation = _cachedDirectionToTarget.Angle();
    _cachedCurrentSpeed = _carController.GetCarSpeed();
    var normalizedSpeed = Mathf.Clamp(_cachedCurrentSpeed / _carController.RacingCar.MaxSpeed, 0.0f, 1.0f);
    _cachedArcSweepAngle = Mathf.Lerp(0.0f, Mathf.Pi, normalizedSpeed);
    _cachedValuesValid = true;
}
```

**Conditional Rendering**:
```csharp
// VisualFeedbackRenderer.cs:93-95 - Only redraw when necessary
if (hasInput || _mouseStationaryTime < 2.0f || _carController.GetCarSpeed() > 1.0f)
    QueueRedraw();

// VisualFeedbackRenderer.cs:137-139 - Trail updates only at movement threshold
if (_lastTrailPosition.DistanceTo(carPosition) >= TrailUpdateDistance) // Default: 3.0f
    AddTrailPoint(_leftTireTrail, leftTirePosition, currentTime);
```

### Memory Management
**Trail Point Limits**:
- MaxTrailPoints: 1000 points per tire
- TrailLifetime: 3000ms automatic cleanup
- Chronological cleanup allows early exit optimization

**Node Reference Caching**:
```csharp
// TrackDefinition.cs:34-36 - caches expensive GetNode calls
private LineTrigger _startLineCache;
public LineTrigger StartLine => _startLineCache ??= GetNodeOrNull<LineTrigger>(StartLinePath);
```

### Update Frequency Optimization
- **Visual Feedback**: Every frame when active, paused when stationary
- **Tire Trails**: Distance-based updates (3.0f threshold)  
- **Track Validation**: Every frame for smooth penalty transitions
- **UI Updates**: Every frame for responsive display
- **Timing**: Every frame during active gameplay only

## Configuration Systems

### Export Categories & Property Groups
```csharp
// RacingGame.cs:26-32 - configuration
[ExportCategory("Racing Settings")]
[Export] public int TargetLaps { get; set; } = 3;
[Export] public bool ShowCountdown { get; set; } = true;
[Export] public float CountdownDuration { get; set; } = 4.0f;
[Export] public int TimeTrialCreditCost { get; set; } = 1;

[ExportCategory("Track Settings")]  
[Export] public Godot.Collections.Array<PackedScene> TrackScenes { get; set; } = new();

// RacingPlayer.cs:25-38 - car configuration
[ExportCategory("Car Settings")]
[Export] public Vector2 CarSize { get; set; } = new Vector2(80, 160);
[Export] public float MaxSpeed { get; set; } = 400.0f;
[Export] public float MinSpeed { get; set; } = 50.0f;
[Export] public float AccelerationRate { get; set; } = 800.0f;
[Export] public float DecelerationRate { get; set; } = 600.0f;
[Export] public float RotationLerpSpeed { get; set; } = 5.0f;

[ExportCategory("Input Settings")]
[Export] public float MaxInputDistance { get; set; } = 300.0f;
```

### Track Scene Configuration
```csharp
// TrackDefinition.cs:15-25 - scene setup
[ExportCategory("Track Setup")]
[Export] public Line2D TrackLine { get; set; }
[Export] public string TrackName { get; set; } = "Unnamed Track";
[Export] public int NumberOfLaps { get; set; } = 3;

[ExportCategory("Scene Node References")]
[Export] public NodePath StartLinePath { get; set; }
[Export] public NodePath FinishLinePath { get; set; }
[Export] public Godot.Collections.Array<NodePath> CheckpointTriggerPaths { get; set; } = new();
```

### Runtime Configuration Patterns
```csharp
// RacingPlayer.cs:231-239 - Settings updates through dedicated methods
public void UpdateSettings(float maxSpeed, float minSpeed, float maxInputDistance, 
    float accelerationRate, float decelerationRate, float rotationLerpSpeed, Vector2 carSize)
{
    MaxSpeed = maxSpeed;
    MinSpeed = minSpeed;
    // ... apply all settings
}

// RacingGame.cs:317-326 - Property-based configuration with automatic propagation
public int TargetLaps 
{ 
    get => _targetLaps; 
    set 
    { 
        _targetLaps = value; 
        if (_timingSystem != null) 
            _timingSystem.TargetLaps = value;
    } 
}
```

### Context-Aware Configuration
```csharp
// RacingGame.cs:347-361
private void DetectAndAdaptToContext()
{
    var gameHost = GameHost.GetInstance();
    if (gameHost != null)
    {
        // Production: Use platform player session
        var playerSession = gameHost.GetPlayerSession("default");
        if (playerSession != null)
        {
            racingCar.PlayerId = playerSession.PlayerId;
            _carController.SetUserData(playerSession.UserData);
        }
    }
    else
    {
        // Development: Use default player ID
        racingCar.PlayerId = "dev_player";
    }
}
```

This technical reference provides the implementation details needed to understand, modify, or extend the racing game system. All method signatures, data structures, and algorithms are documented as they exist in the actual codebase.