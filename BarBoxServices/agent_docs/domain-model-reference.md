# Domain Model Reference

## Entity Relationships

```
Box (physical terminal)
  +-> BoxSession (gameplay session)
  |     +-> Player (user account)
  |     +-> BoxSessionEvent[] (event stream)
  +-> Player[] (originated players)

Game (game type definition)
  +-> Leaderboard (aggregated scores)
```

## Core Models

### Box - Physical Terminal
```python
# API (structures.py)
class BoxCreate(Identifiable, Named, Tagged):
    id: UUID        # Client-generated
    name: str       # Human-readable ("Best Intentions")
    tag: str        # Short identifier ("besties")

# Database (db/defs.py)
class Box(Base):
    id: Mapped[UUID]
    name: Mapped[str]
    tag: Mapped[str]
```

### Player - User Account
```python
class PlayerCreate(Identifiable, Tagged):
    id: UUID
    tag: str           # Username/handle
    origin_id: UUID    # Box where player first signed up

class Player(Base):
    id: Mapped[UUID]
    tag: Mapped[str]
    origin_id: Mapped[BoxFk]
```

### BoxSession - Gameplay Session
```python
class BoxSession(Base):
    id: Mapped[UUID]
    box_id: Mapped[BoxFk]
    player_id: Mapped[UUID]
    start_time: Mapped[datetime]
    end_time: Mapped[datetime | None]
    events: Mapped[list["BoxSessionEvent"]] = relationship(...)
```

### BoxSessionEvent - Event Stream
```python
class BoxSessionEvent(Base):
    id: Mapped[UUID]
    session_id: Mapped[UUID]
    type: Mapped[str]         # SessionEventType Literal
    timestamp: Mapped[datetime]
    payload: Mapped[JsonObject]  # Stored as JSON in SQLite
```

## Session Event Lifecycle

```
1. PUT  /box/{box_id}/session/{session_id}  -> Create session
2. POST /box/session/{session_id}           -> {"type": "play/begin", "payload": {"game": "carrom"}}
3. POST /box/session/{session_id}           -> {"type": "play/score", "payload": {"points": 10}}
4. POST /box/session/{session_id}           -> {"type": "play/finish"}
```

## Data Flow

```
Request:  HTTP JSON -> Pydantic (structures.py) -> CRUD (db/service.py) -> SQLAlchemy (db/defs.py) -> DB
Response: DB -> SQLAlchemy -> model_validate(from_attributes=True) -> Pydantic -> JSON
```

## Mining Location Model

- **Global aggregation**: `mining/extract_complete` adds to player's global gem inventory (portable across all locations)
- **Location-scoped**: `mining/upgrade_purchase` only affects upgrades at that venue
- **Location-scoped**: `mining/first_time_bonus` tracked per venue
