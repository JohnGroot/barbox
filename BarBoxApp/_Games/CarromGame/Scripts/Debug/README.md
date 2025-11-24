# CarromGame Debug Tools

## Overview

This directory contains debug tools for testing CarromGame match state scenarios during gameplay. The debug overlay appears in-game (editor/debug builds only) and enables rapid iteration and validation of complex game rules without playing through full matches.

## Setup

The debug overlay is automatically included as part of the CarromGame scene. It only appears in editor and debug builds - it's completely hidden in release builds.

### How to Use

1. **Open CarromGame scene**: Load `CarromGame.tscn` in Godot editor
2. **Run the scene**: Press F6 to start the game
3. **Toggle debug overlay**: Press **F12** to show/hide the debug panel
4. **Use debug buttons**: Click scenario buttons to test specific game states

The overlay appears as a semi-transparent panel in the top-right corner with real-time game state information.

## Available Test Scenarios

### Foul Testing

#### Striker Foul
**Button**: `⚠️ Striker Foul`

**What it does**:
- Gives the current player 2 pieces so penalty can be applied
- Sets game to Ready state

**How to test**:
1. Run the scene (F6)
2. Start a competitive match
3. Press F12 to show debug overlay
4. Click "⚠️ Striker Foul" button
5. Pocket the striker
6. Verify: One piece returns to center, turn passes to next player

#### Opponent Piece Foul
**Button**: `⚠️ Opponent Piece Foul`

**What it does**:
- Prepares scenario for testing opponent piece pocketing
- Sets game to Ready state

**How to test**:
1. Run the scene (F6)
2. Start competitive match
3. Press F12 to show debug overlay
4. Click "⚠️ Opponent Piece Foul" button
5. Pocket an opponent's piece
6. Verify: Opponent piece returns to center, penalty applied, turn passes

### Queen Covering Tests

#### Queen Covering Success
**Button**: `👑 Queen Covering (Success)`

**What it does**:
- Ensures player has at least 1 piece (eligibility for queen)
- Sets game to Ready state
- Prints instructions to console

**How to test**:
1. Run the scene (F6)
2. Start competitive match
3. Press F12 to show debug overlay
4. Click "👑 Queen Covering (Success)" button
5. Pocket the red queen
6. Immediately pocket your assigned piece (white or black)
7. Verify: Player's queen status shows "Covered", turn continues

#### Queen Covering Failure
**Button**: `👑 Queen Covering (Fail)`

**What it does**:
- Ensures player has at least 1 piece
- Prepares for queen covering failure test

**How to test**:
1. Run the scene (F6)
2. Start competitive match
3. Press F12 to show debug overlay
4. Click "👑 Queen Covering (Fail)" button
5. Pocket the red queen
6. DON'T pocket your piece (miss or foul)
7. Click "Pass Turn" button
8. Verify: Queen returns to center, player no longer has queen

### Breaking Turn Test

**Button**: `🔄 Breaking Turn Test`

**What it does**:
- Resets the game to the initial breaking turn state
- All pieces at center formation
- 3 attempts available

**How to test**:
1. Run the scene (F6)
2. Press F12 to show debug overlay
3. Click "🔄 Breaking Turn Test" button
4. Try strikes that don't disturb pieces (weak shots)
5. Verify: Can attempt 3 times before turn passes
6. Verify: Strong strike that disturbs pieces ends breaking phase

### Win Condition Test

**Button**: `🏆 Trigger Win Condition`

**What it does**:
- Sets player1 to winning state (9 pieces + covered queen)
- Triggers game over sequence

**How to test**:
1. Run the scene (F6)
2. Start competitive match
3. Press F12 to show debug overlay
4. Click "🏆 Trigger Win Condition" button
5. Verify: Game over screen appears
6. Verify: Final statistics are correct
7. Verify: "Return to Practice" button works

## Player State Manipulation

### Switch Player
**Button**: `➡️ Switch to Player 2`

Switches active player to Player 2 for testing turn transitions.

### Give Pieces
**Button**: `📊 Give Player 5 Pieces`

Sets current player to 5 pieces pocketed for mid-game testing.

### Near-Win State
**Button**: `🎯 Set Near-Win State`

Sets player1 to 8 pieces + covered queen (one piece away from winning).

**Use case**: Test final piece pocketing and win condition triggers.

## Utilities

### Return to Practice
**Button**: `🔄 Return to Practice`

Immediately returns to practice mode without confirmation dialog.

**Use case**: Quick reset between test scenarios.

### Print State
**Button**: `📋 Print State to Console`

Prints complete game state to Godot console.

**Output includes**:
- Current game mode
- Game state machine state
- Input availability
- Current player
- All player states (pieces, queen status, accuracy, fouls)

## Real-Time State Display

The bottom section of the debug panel shows real-time game state that updates twice per second:

```
=== CARROM GAME STATE ===
Mode: Competitive
Game State: Ready
Can Accept Input: True

Current Player: player1

PLAYER STATES:
  player1:
    Pieces: 3/9
    Queen: Uncovered
    Accuracy: 75.0%
    Fouls: 1
  player2:
    Pieces: 2/9
    Queen: None
    Accuracy: 66.7%
    Fouls: 0
========================
```

## Direct Method Access (Advanced)

If you need more control, you can call debug methods directly from the Godot debugger console:

```csharp
// Get CarromGame instance
var game = GetNode<CarromGame>("/root/CarromGame");

// Setup specific scenario
game.DEBUG_SetupFoulScenario(FoulType.StrikerPocketed);

// Force player state
game.DEBUG_ForcePlayerState("player1", piecesPocketed: 8, hasQueen: true, queenCovered: true);

// Trigger win
game.DEBUG_TriggerWinCondition("player1");

// Get state
var state = game.DEBUG_GetGameState();
GD.Print(state);
```

## Common Testing Workflows

### Test Complete Match Flow

1. Run scene (F6), start 2-player match
2. Press F12 to show debug overlay
3. Click "🔄 Breaking Turn Test"
4. Test breaking turn (3 attempts)
5. Press F12, click "📊 Give Player 5 Pieces"
6. Continue playing
7. Press F12, click "🎯 Set Near-Win State"
8. Pocket one more piece to test win condition

### Test Queen Mechanics End-to-End

1. Run scene (F6), start competitive match
2. Press F12 to show debug overlay
3. Click "👑 Queen Covering (Success)"
4. Pocket queen, then cover it
5. Verify queen stays pocketed
6. Press F12, click "🔄 Return to Practice"
7. Run scene again, press F12
8. Click "👑 Queen Covering (Fail)"
9. Pocket queen, don't cover
10. Pass turn, verify queen returns

### Test Foul Penalties

1. Run scene (F6), start match
2. Press F12 to show debug overlay
3. Click "⚠️ Striker Foul"
4. Pocket striker
5. Verify penalty piece returns
6. Verify turn passes
7. Check score display updates

## Tips

- **Press F12 to toggle** - The overlay starts hidden, press F12 anytime during gameplay to show/hide it
- **Watch console output** - Debug methods print detailed information about what they're doing
- **Use "Print State"** frequently to verify game state matches expectations
- **Combine buttons** - Set up complex scenarios by clicking multiple setup buttons during gameplay
- **Real-time updates** - The state display updates automatically every 0.5 seconds while visible

## Troubleshooting

### Overlay doesn't appear when pressing F12
- Ensure you're running the scene (F6), not just viewing it in editor
- Check console for `[CarromDebugOverlay] Debug overlay loaded` message
- Verify the build completed successfully: `dotnet build`
- Check that you're in editor/debug build (overlay auto-hides in release builds)

### Debug methods not working
- Verify you're in Competitive mode (some methods require it)
- Check the scene is running (press F6)
- Look for error messages in console with `[CarromDebugOverlay]` prefix
- Press F12 to verify overlay is visible

### Overlay shows "Game instance not found"
- This shouldn't happen with the current implementation
- If it does, check console for errors and restart the scene

## Implementation Details

### Files
- `CarromDebugOverlay.cs` - In-game CanvasLayer overlay that creates the debug UI
- Debug methods in `CarromGame.cs` (lines 2445-2704) wrapped in `#if TOOLS`
- Added as child node in `CarromGame.tscn`

### Visibility Logic
```csharp
// Only shows in editor/debug builds
bool shouldShow = Engine.IsEditorHint() || OS.IsDebugBuild();
```

Debug overlay is completely removed in release builds - zero overhead in production.

### Architecture
- **CanvasLayer** at layer 1000 (top of rendering stack)
- **Parent reference** to CarromGame for direct method calls
- **F12 input handling** for show/hide toggle
- **Timer-based refresh** for real-time state display (0.5s intervals)

## Future Enhancements

Potential additions for Phase 2+:
- Save/load test scenarios as JSON
- Automated test sequences
- Performance profiling integration
- Visual piece placement tool
- Replay system for bug reproduction
