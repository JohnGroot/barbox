# Game Module Guide

## Adding a New Game

Each game module follows this structure:

```
games/{game_name}/
+-- __init__.py
+-- schemas.py    # Event types (Literal) + Pydantic models
+-- service.py    # Business logic & database queries
+-- router.py     # FastAPI endpoints
```

### Step-by-step

1. **Create directory**: `mkdir -p src/bxctl/games/foo && touch src/bxctl/games/foo/__init__.py`

2. **Define schemas** (`schemas.py`):
   ```python
   FooEventType = Literal["foo/game_start", "foo/score", "foo/game_end"]
   class FooScorePayload(BaseModel): ...
   class FooLeaderboardEntry(BaseModel): ...
   ```

3. **Implement service** (`service.py`): SQL queries with `common.safe_parse_leaderboard()`

4. **Create router** (`router.py`): `router = APIRouter(prefix="/game/foo", tags=["Game: Foo"])`

5. **Register events** (`structures.py`):
   ```python
   from bxctl.games.foo.schemas import FooEventType
   type SessionEventType = CoreEventType | CarromEventType | ... | FooEventType
   ```

6. **Register validation** (`games/validation.py`):
   ```python
   _EVENT_REGISTRY["foo"] = set(get_args(foo_schemas.FooEventType))
   ```

7. **Register router** (`web/main.py`): Add to `game_routers` tuple

8. **Test**: `curl http://localhost:8000/game/foo/leaderboard`

## Common Utilities (`games/common.py`)

- `parse_uuid_safe()` - Safe UUID parsing with zero-UUID fallback
- `parse_username_safe()` - Safe username parsing with "Unknown" fallback
- `safe_parse_leaderboard()` - Per-entry error handling (skips corrupt rows)
- `parse_float_list()` - For JSON array fields like lap_times

## Current Games

- **Carrom**: Multiplayer scoring, score/win leaderboards
- **Racing**: Per-track leaderboards, best lap/race time metrics
- **Mining**: Global inventory + per-location upgrades, offline progress

## Best Practices

- Event types as `Literal` types (single source of truth)
- Per-entry error handling in leaderboard parsing (never list comprehensions)
- Use CTEs for complex aggregations
- Router delegates to service layer; no business logic in endpoints
