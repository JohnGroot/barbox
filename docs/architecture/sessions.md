# Session Architecture - BarBox Platform

## Overview

The BarBox platform uses a **two-tier session model** that cleanly separates authentication from gameplay:

- **UserSession** (Frontend) - Authentication session (login → logout)
- **ActivitySession** (Backend) - Gameplay session (game launch → game exit)

## Architecture Diagram

```
User logs in → UserSession created (SessionManager)
  ↓
User launches game → ActivitySession created (game code + SessionEventService)
  ↓
User plays game → Events emitted to ActivitySession
  ↓
User exits game → ActivitySession closed (game code)
  ↓
User logs out → UserSession destroyed (SessionManager)
```

## Session Types

### UserSession (Tier 1: Authentication)

**What**: Represents a logged-in user on the client
**Lifetime**: Login → Logout/Timeout (minutes to hours)
**Storage**: In-memory (SessionManager)
**Purpose**: Who is logged into this machine

**Structure**:
```csharp
public class UserSession
{
    public string PhoneNumber { get; set; }  // Primary identifier
    public string UserName { get; set; }     // Display name
    public Guid PlayerId { get; set; }       // Backend player UUID
    public string LocationId { get; set; }   // Which box
    public DateTime LoginTime { get; set; }
    public DateTime LastActivity { get; set; }
    public int Credits { get; set; }         // Cached balance
}
```

**Key Points**:
- Frontend-only, not persisted to backend
- Supports multiple concurrent logins (multiplayer)
- Auto-logout after 10 minutes idle
- Credits are cached (use CreditService for operations)

### ActivitySession (Tier 2: Gameplay)

**What**: Backend record of gameplay activity
**Lifetime**: Game launch → Game exit (variable duration)
**Storage**: Database (permanent)
**Purpose**: Track gameplay, events, leaderboards

**Backend Schema** (`BarBoxServices/src/bxctl/db/defs.py`):
```python
class BoxSession(Base):  # Will be renamed to ActivitySession
    box_id: Mapped[BoxFk]
    host_player_id: Mapped[UUID]
    player_ids: Mapped[PlayerIdArray]
    game_tag: Mapped[str]  # NEW: Game identifier
    start_time: Mapped[datetime]
    end_time: Mapped[datetime | None]
    events: Mapped[list["BoxSessionEvent"]]
```

**Key Points**:
- Created by games, not by login
- Multiplayer-aware (player_ids array)
- Events stream during gameplay
- Must be explicitly closed on game exit

## Service Responsibilities

### SessionManager
**Responsibility**: Authentication and user session management
**Location**: `BarBoxApp/_Core/Scripts/Autoloads/SessionManager.cs`

**Methods**:
- `LoginUserByPhoneAsync()` - Authenticate user, create UserSession
- `LogoutUserAsync()` - Destroy UserSession, emit analytics event
- `GetUserSession(phoneNumber)` - Retrieve specific user
- `GetPrimaryUserSession()` - Get first logged-in user

**Changed**: No longer creates ActivitySession on login

### CreditService
**Responsibility**: Credit operations with smart caching
**Location**: `BarBoxApp/_Core/Scripts/Autoloads/CreditService.cs`

**Methods**:
- `GetBalanceAsync(playerId)` - Query balance (30s cache TTL)
- `SpendAsync(playerId, amount, reason)` - Spend credits
- `AddAsync(playerId, amount, reason)` - Add credits
- `ReconcileBalanceAsync(playerId)` - Force sync with backend

**New Service**: Extracted from SessionManager for single responsibility

### SessionEventService
**Responsibility**: Backend communication
**Location**: `BarBoxApp/_Core/Scripts/Autoloads/SessionEventService.cs`

**Key Methods**:
- `EmitEventAsync(eventType, payload)` - Stream events

**Note**: ActivitySession create/close is no longer called directly on
`SessionEventService`. `GameController.StartBackendSessionAsync` (base class,
`BarBoxApp/_Core/Scripts/Gameplay/GameController.cs`) wraps
`Platform.Events.CreateActivitySessionAsync` and owns the session id; close
happens automatically in the base `_ExitTree`. See Game Integration Guide
below.

### GameHost
**Responsibility**: Game orchestration
**Location**: `BarBoxApp/_Core/Scripts/Autoloads/GameHost.cs`

**No Changes**: Works with UserSession directly

## Backend API Changes

### New ActivitySession Endpoints

#### Create ActivitySession
```
PUT /box/{box_id}/session/{session_id}?game_tag={tag}
Header: Player-Id: {uuid}
Query: player_ids=["uuid1","uuid2"] (optional, for multiplayer)

Response: {"id": "session-uuid"}
```

**Required**: `game_tag` parameter (e.g., "carrom", "racing", "mining")

#### Close ActivitySession
```
POST /box/session/{session_id}/close

Response: {"id": "session-uuid"}
```

**New Endpoint**: Properly closes ActivitySession (sets end_time)

## Game Integration Guide

Every game subclasses `GameController` (`BarBoxApp/_Core/Scripts/Gameplay/GameController.cs`),
which owns the ActivitySession lifecycle: `StartBackendSessionAsync` creates
it and stores the id; the base `_ExitTree` closes it automatically after
`OnGameTeardown` runs. A game only calls `StartBackendSessionAsync` when its
own gameplay begins (timing varies — Racing/Carrom create lazily at match
start) and never closes the session itself.

### Single-Player Game Pattern (Racing, Mining)

```csharp
public partial class RacingGame : GameController
{
    protected override string GetGameId() => "racing";

    private async Task StartGameSessionAsync()
    {
        var userSession = Platform.Session.GetPrimaryUserSession();
        if (userSession == null)
        {
            LogError("No user logged in");
            return;
        }

        // Create + track the ActivitySession (base class owns the id and close)
        var result = await StartBackendSessionAsync(
            boxId: Platform.Location.BoxId,
            playerId: userSession.PlayerId
        );

        if (!result.IsSuccess)
        {
            LogError($"Failed to create game session: {result.Error}");
            return;
        }

        await Platform.Events.EmitEventAsync("play/begin", new { game = "racing" });
    }

    private async void OnGameEnd()
    {
        // Emit play/finish; session close happens automatically in _ExitTree
        await Platform.Events.EmitEventAsync("play/finish", new {
            final_time = _raceTime
        });
    }
}
```

### Multiplayer Game Pattern (Carrom)

```csharp
public partial class CarromGame : GameController
{
    protected override string GetGameId() => "carrom";

    private List<Guid> _playerIds = new();

    private async Task StartMultiplayerSessionAsync()
    {
        // Collect all logged-in players
        foreach (var phoneNumber in Platform.Session.GetActivePhoneNumbers())
        {
            var userSession = Platform.Session.GetUserSession(phoneNumber);
            if (userSession != null)
                _playerIds.Add(userSession.PlayerId);
        }

        var result = await StartBackendSessionAsync(
            boxId: Platform.Location.BoxId,
            playerId: _playerIds[0],  // Host player (first)
            playerIds: _playerIds.Select(id => id.ToString()).ToList()
        );

        if (!result.IsSuccess)
        {
            LogError($"Failed to create multiplayer session: {result.Error}");
            return;
        }

        await Platform.Events.EmitEventAsync("play/begin", new {
            game = "carrom",
            mode = "competitive",
            player_count = _playerIds.Count
        });
    }
}
```

## Migration Checklist

### ✅ Completed
- [x] Backend: Add `game_tag` field to BoxSession
- [x] Backend: Add `POST /box/session/{id}/close` endpoint
- [x] Frontend: Remove BoxSession creation from login
- [x] Frontend: Remove `BackendSessionId` from UserSession
- [x] Frontend: Extract CreditService from SessionManager
- [x] Frontend: centralize ActivitySession create/close into
  `GameController.StartBackendSessionAsync` + base `_ExitTree`; all four
  games (Racing, Mining, Carrom, Nines) migrated onto it
- [x] Frontend: all games route credit spend through `CreditService`'s
  `SpendWithConfirmationAsync`/`SpendManyWithRollbackAsync`/`TransferToMachineAsync`
  primitives instead of hand-rolled balance/spend logic

## Benefits of New Architecture

### Clear Separation of Concerns
- **Login ≠ Game Launch**: Authentication and gameplay are distinct
- **UserSession**: Answers "who is logged in?"
- **ActivitySession**: Answers "what game is being played?"

### Improved Data Quality
- **game_tag** field enables efficient leaderboard queries
- No more inferring game type from first event
- Explicit session lifecycle (start_time, end_time)

### Better Analytics
- Track multiple games per login session
- Distinguish between login duration and gameplay duration
- Support menu browsing (UserSession without ActivitySession)

### Multiplayer Support
- ActivitySession naturally supports multiple players
- Host player clearly identified (host_player_id)
- Credit pooling tracked (table_credits, credit_contributions)

### Cleaner Code
- SessionManager: ~300 lines (was 885)
- CreditService: Focused responsibility
- Games: Own their session lifecycle

## Common Patterns

### Checking User Login
```csharp
var sessionManager = SessionManager.GetInstance();
if (sessionManager == null)
{
    LogError("SessionManager not available");
    return;
}

var userSession = sessionManager.GetPrimaryUserSession();
if (userSession == null)
{
    ShowError("Please log in to play");
    return;
}
```

### Credit Operations
```csharp
var creditService = CreditService.GetInstance();
if (creditService == null)
{
    LogError("CreditService not available");
    return;
}

// Get balance with cache
var balanceResult = await creditService.GetBalanceAsync(userSession.PlayerId);
if (!balanceResult.IsSuccess)
{
    ShowError($"Failed to check balance: {balanceResult.Error}");
    return;
}

// Spend credits
var spendResult = await creditService.SpendAsync(
    playerId: userSession.PlayerId,
    amount: 1,
    reason: "game_start_carrom"
);
```

### Session Lifecycle
```csharp
public partial class MyGame : GameController
{
    protected override string GetGameId() => "my_game";

    private async Task StartSessionAsync()
    {
        var userSession = Platform.Session.GetPrimaryUserSession();
        if (userSession == null)
            return;

        // GameController stores the session id and closes it automatically
        // in the base _ExitTree — no manual close call needed.
        await StartBackendSessionAsync(Platform.Location.BoxId, userSession.PlayerId);
    }
}
```

## Future Enhancements

### Session Timeout/Heartbeat
- Backend job to close sessions with `end_time IS NULL` and old `start_time`
- Client sends periodic heartbeat to keep session alive

### Retry Logic
- Exponential backoff for transient network failures
- Idempotency keys for credit operations

### Offline Mode
- Queue events locally when backend unavailable
- Sync when connection restored

### Box Lifetime Tracking
- Add `Box.last_seen` timestamp
- Query ActivitySessions by box_id for analytics

## Troubleshooting

### "Game session creation failed"
- Check SessionEventService is initialized: `SessionEventService.GetInstance() != null`
- Check LocationManager has box ID: `LocationManager.IsConfigLoaded`
- Check backend is running: `curl http://127.0.0.1:8000/alive`
- Check game_tag is provided (required parameter)

### "Credits not updating"
- Use CreditService instead of UserSession.Credits
- CreditService has 30s cache - use `forceRefresh: true` if needed
- Check SessionEventService connectivity

### "Orphaned sessions in database"
- `GameController`'s sealed `_ExitTree` closes the active session automatically
  — a game only orphans a session if it never called `StartBackendSessionAsync`
  in the first place, or bypassed `GameController` entirely
- Backend cleanup job coming soon

## References

- Backend Session Schema: `BarBoxServices/src/bxctl/db/defs.py`
- Session API Endpoints: `BarBoxServices/src/bxctl/boxes/router.py` (logic in `boxes/service.py`)
- SessionManager: `BarBoxApp/_Core/Scripts/Autoloads/SessionManager.cs`
- CreditService: `BarBoxApp/_Core/Scripts/Autoloads/CreditService.cs`
- SessionEventService: `BarBoxApp/_Core/Scripts/Autoloads/SessionEventService.cs`
