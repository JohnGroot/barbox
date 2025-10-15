# MiningGame Architecture

**MiningGame** is an idle mining game implementation for the BarBox platform that demonstrates exemplary adherence to the platform's architectural patterns and coding standards.

## Overview

The MiningGame implements a real-time idle mining system where players accumulate gems over time, purchase upgrades, and buy credits for use in other BarBox games. The implementation showcases the platform's preferred consolidated architecture pattern, reducing complexity while maintaining clear separation of concerns.

## Architecture Highlights

### 🏗️ **Consolidated File Structure**
Following BarBox's 4-file consolidation pattern:

```
MiningGame/
├── MiningGameTypes.cs      # All enums and data structures
├── MiningGameUI.cs         # Complete UI system with nested classes
├── MiningGame.cs           # Main game with nested Engine/State classes
├── MiningGameConfig.cs     # Unified configuration
├── Supporting Files:
│   ├── MiningGlobalData.cs     # Global player data
│   ├── MiningLocationData.cs   # Location-specific configuration
│   ├── MiningLocationState.cs  # Local machine state
│   ├── CreditPurchaseTimer.cs  # Timer management
│   └── UpgradeCostData.cs     # Upgrade cost calculations
```

### 🎯 **Core Design Patterns**

#### **1. Context-Aware Architecture**
```csharp
public override void _Ready()
{
    DetectAndAdaptToContext();
    // Graceful fallback to development mode
    // Production integration through GameHost discovery
}
```

#### **2. Nested Component Structure**
```csharp
public partial class MiningGame : GameController
{
    public partial class GameEngine : Node 
    { 
        // Core mining logic, gem accumulation, upgrade processing
    }
    
    public partial class GameState : Node 
    { 
        // Data management, user state, persistence
    }
}
```

#### **3. Signal Protocol (External Integration Only)**
```csharp
// ✅ External signals for GameHost integration
[Signal] public delegate void GemsExtractedEventHandler(string gemType, int amount);
[Signal] public delegate void CreditPurchasedEventHandler();

// ✅ Direct method calls for internal communication
_ui.UpdateAllUI();  // Not via signals
```

#### **4. Service Discovery Pattern**
```csharp
var gameHost = GameHost.GetInstance();
if (GodotObject.IsInstanceValid(gameHost))
{
    // Optional integration - graceful degradation when unavailable
    gameHost.SomeMethod();
}
```

## Data Architecture

### **Local vs Global Data Separation**

The game implements a sophisticated data architecture that separates location-specific state from global player data:

#### **Global Data** (`MiningGlobalData.cs`)
- Player's total gem inventory across all locations
- Persistent across sessions and locations
- Stored in UserData meta system

#### **Location-Specific Data** (`MiningLocationData.cs` + `MiningLocationState.cs`)
- Machine-specific upgrade levels and mining capacity
- Local gem accumulation ready for extraction
- Per-location theming and gem types
- Persisted with location-specific keys: `mining_state_{locationId}`

### **Data Flow**
```
Real-time Mining → Local Capacity → Extraction → Global Inventory → Credit Purchase
                ↓                              ↓
         Location Upgrades                Player Account
```

## Performance & Caching Strategy

### **Intelligent Caching System**
```csharp
private bool _cacheValid = false;
private int _cachedMaxCapacity;

private void RefreshCacheIfNeeded()
{
    if (!_cacheValid && _locationTemplate != null)
    {
        _cachedMaxCapacity = _locationTemplate.GetMaxCapacity(/*...*/);
        _cacheValid = true;
    }
}

private void InvalidateCache() => _cacheValid = false;
```

### **Race Condition Prevention**
```csharp
private bool _isProcessingUserChange = false;

private void OnUserLoggedIn(UserData userData)
{
    if (_isProcessingUserChange) return;
    _isProcessingUserChange = true;
    try { /* user change logic */ }
    finally { _isProcessingUserChange = false; }
}
```

## Game Mechanics Implementation

### **Real-Time Mining System**
- **Tick Interval**: 2 hours (configurable)
- **Gem Accumulation**: Location-specific gem types
- **Capacity Management**: Local extraction limits with global inventory

### **Upgrade System**
- **15 Levels**: Per upgrade type (Capacity, Mining Amount, Mining Speed)
- **3 Tiers**: Increasingly complex cost structures
  - Tier 1: Single gem type
  - Tier 2: Location gem + 1 random other type
  - Tier 3: Location gem + 2 random other types

### **Credit Purchase System**
- **Cost**: 150 gems per credit
- **Unlimited Purchases**: Buy credits anytime you have enough gems
- **No Timers**: No recharge delays between purchases

## UI Architecture

### **Theme System Integration**
```csharp
private Color GetBackgroundColor() => 
    _locationData?.BackgroundColor ?? new Color(0.1f, 0.1f, 0.15f, 0.95f);
```

### **State Management**
```csharp
public void SetEnabled(bool enabled)
{
    _isEnabled = enabled;
    Modulate = enabled ? Colors.White : new Color(0.6f, 0.6f, 0.6f, 0.8f);
    SetInteractiveElementsEnabled(enabled);
}
```

### **UI Components**
- **Progress Tracking**: Real-time mining progress with time remaining
- **Interactive Elements**: Extract, Purchase Credits, Buy Upgrades buttons
- **Dynamic Content**: Credit timers, upgrade costs, capacity displays
- **Context-Aware**: All UI disabled when user not logged in

## Configuration System

### **Unified Configuration** (`MiningGameConfig.cs`)
```csharp
[ExportCategory("Mining Settings")]
[Export] public double MiningIntervalHours { get; set; } = 2.0;
[Export] public int BaseGemsPerTick { get; set; } = 15;

[ExportCategory("Economy")]
[Export] public int CreditsPrice { get; set; } = 150;
[Export] public double CreditRechargeHours { get; set; } = 24.0;

[ExportCategory("Upgrades")]
[Export] public int MaxUpgradeLevel { get; set; } = 15;
```

## Integration Points

### **BarBox Platform Services**
- **GameHost**: User management, credit system integration
- **UserDataService**: Persistent storage for global gem inventory
- **LocationManager**: Location-specific configuration and theming

### **Development vs Production**
- **Development Mode**: Auto-start with debug user, enhanced logging
- **Production Mode**: Full platform integration, user authentication required

## Code Quality Standards

### **Naming Conventions**
- **Classes**: PascalCase (`MiningGame`, `GameEngine`)
- **Methods/Properties**: PascalCase (`UpdateUI`, `GetMaxCapacity`)
- **Public Fields**: PascalCase (`_Transform`)
- **Private Fields**: camelCase with underscore (`_isEnabled`, `_cachedMaxCapacity`)

### **Error Handling**
- Early returns with null checks
- Graceful degradation when services unavailable
- Proper Godot object validity checking with `GodotObject.IsInstanceValid()`

### **Performance Considerations**
- Cached calculations for frequently accessed values
- Minimal signal usage to reduce overhead
- Direct method calls for internal communication
- Efficient UI update patterns

## Example Usage

### **Integration Setup**
```csharp
// Game auto-adapts to context
var miningGame = GetNode<MiningGame>("MiningGame");
// Ready() automatically detects development vs production
// Sets up appropriate user authentication flow
```

### **Accessing Game State**
```csharp
// Global gems across all locations
var globalGems = userData.GetMetaValue("global_gems_ruby", 0);

// Location-specific state
var locationState = GetLocationState(locationId);
var localGems = locationState.ReadyToExtractGems;
var upgradeLevel = locationState.GetUpgradeLevel(UpgradeType.MiningAmount);
```

## Design Philosophy

The MiningGame exemplifies BarBox's architectural philosophy:

- **Simplicity over Complexity**: Consolidated files, direct method calls, minimal abstractions
- **Context Awareness**: Seamless development and production modes
- **Optional Integration**: Works standalone or with full platform
- **Performance Focus**: Caching, efficient updates, minimal overhead
- **Clear Separation**: Local vs global data, internal vs external communication

This implementation serves as a reference for other BarBox games, demonstrating how to build complex mechanics while adhering to the platform's architectural preferences and maintaining high code quality.

**GENERATED BY CLAUDE** xD