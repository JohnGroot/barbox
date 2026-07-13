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

2. **Define schemas** (`schemas.py`) — include the canonical `EventType` alias
   (the validation registry reads it generically):
   ```python
   FooEventType = Literal["foo/game_start", "foo/score", "foo/game_end"]
   EventType = FooEventType  # canonical alias, required
   class FooScorePayload(BaseModel): ...
   class FooLeaderboardEntry(BaseModel): ...
   ```

3. **Implement service** (`service.py`): SQL queries with `common.safe_parse_leaderboard()`

4. **Create router** (`router.py`): `router = APIRouter(prefix="/game/foo", tags=["Game: Foo"])`

5. **Register in the `GAMES` dict** (`structures.py`) — single source of truth;
   router registration (`web/main.py`) and the event registry
   (`games/validation.py`) both auto-derive from it:
   ```python
   from bxctl.games import carrom, foo, mining, nines, racing

   GAMES = {
       ...,
       "foo": {"schemas": foo.schemas, "router": foo.router},
   }
   ```

6. **Extend the `SessionEventType` union** (`structures.py`):
   ```python
   type SessionEventType = (
       CoreEventType
       | ...
       | foo.schemas.FooEventType
   )
   ```
   Missing from the union = HTTP 422 on event submission.

7. **Classify payloads** (`games/validation.py`): every event type must appear
   in `EVENT_PAYLOAD_MODELS` (with a Pydantic payload model) or in
   `NO_PAYLOAD_EVENTS` (explicitly unvalidated). The import-time guard
   `_check_payload_model_coverage()` raises `RuntimeError` at startup if an
   event is in neither.

8. **Test**: `curl http://localhost:8000/game/foo/leaderboard`, then add a
   Hurl file under `test/02-feature/foo/` (auto-discovered by `scripts/test.sh`).

## Common Utilities (`games/common.py`)

- `parse_uuid_safe()` - Safe UUID parsing with zero-UUID fallback
- `parse_username_safe()` - Safe username parsing with "Unknown" fallback
- `safe_parse_leaderboard()` - Per-entry error handling (skips corrupt rows)
- `parse_float_list()` - For JSON array fields like lap_times

## Current Games

- **Carrom**: Multiplayer scoring, score/win leaderboards
- **Racing**: Per-track leaderboards, best lap/race time metrics
- **Mining**: Global inventory + per-location upgrades, offline progress
- **Nines**: Per-venue jackpot tracking (`nines/jackpot_won`; note: its
  `player_id` payload field is the winner's phone number, not a UUID)

## Best Practices

- Event types as `Literal` types (single source of truth)
- Per-entry error handling in leaderboard parsing (never list comprehensions)
- Use CTEs for complex aggregations
- Router delegates to service layer; no business logic in endpoints
