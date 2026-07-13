# Box Identity Naming Convention

BarBox uses a consistent naming convention for all identifiers:
- **"id" suffix** = UUID/GUID (security, database, backend)
- **"name" suffix** = human-readable string (display, logging, configuration)

## The Three Identifiers

**BoxId** (`Guid`): Secure UUID for backend authentication and database foreign keys
- Example: `12345678-1234-1234-1234-123456789012`
- Never display to users
- Access: `SessionEventService.GetInstance().GetBoxId()`

**BoxName** (`string`): Human-readable machine identifier for display and logging
- Example: `besties_box_1`, `arcade_terminal_2`
- Format: lowercase_with_underscores
- Access: `SessionEventService.GetInstance().GetBoxName()`

**VenueName** (`string`): Human-readable venue identifier for venue-scoped data
- Example: `best_intentions`, `johnnys_arcade`
- Format: lowercase_with_underscores
- Venue-scoped data follows players across machines at same venue
- Access: `SessionEventService.GetInstance().GetVenueName()`

## Terminal-Scoped vs Venue-Scoped Data

**Terminal-Scoped** (use BoxId):
- Machine credits - locked to specific physical machine
- Machine-specific settings, revenue protection

**Venue-Scoped** (use VenueName):
- Mining game progress - follows player across machines at venue
- Upgrade levels, venue-wide leaderboards

## Quick Reference

```csharp
var eventService = SessionEventService.GetInstance();
var venueName = eventService.GetVenueName();  // Venue-scoped queries
var boxName = eventService.GetBoxName();      // Display and logging
var boxId = eventService.GetBoxId();          // Backend auth (rarely needed by games)
```

See also [BOX_IDENTITY_GUIDE.md](../BOX_IDENTITY_GUIDE.md) for complete documentation with decision trees.
