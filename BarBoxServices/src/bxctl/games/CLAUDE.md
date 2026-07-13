# Game Modules

Self-contained game modules with consistent structure:

```
games/{game_name}/
+-- schemas.py    # Event types (Literal) + Pydantic models
+-- service.py    # Business logic & database queries
+-- router.py     # FastAPI endpoints
```

## Current Games

- **Carrom** - Multiplayer scoring, score/win leaderboards
- **Racing** - Per-track leaderboards, best lap/race time metrics
- **Mining** - Global inventory + per-location upgrades, offline progress
- **Nines** - Per-venue jackpot tracking (payload `player_id` is a phone number, not a UUID)

## Adding a New Game

1. Create `games/{name}/` with `__init__.py`, `schemas.py`, `service.py`, `router.py`
2. Define event types as `Literal[...]` in schemas.py, plus the canonical `EventType = FooEventType` alias
3. Register the game in the `GAMES` dict in `structures.py` (auto-wires the router and validation event registry)
4. Extend the `SessionEventType` union in `structures.py`
5. Classify every event in `games/validation.py`: `EVENT_PAYLOAD_MODELS` (payload model) or `NO_PAYLOAD_EVENTS` (explicitly unvalidated) — an import-time guard raises if neither

See `../../agent_docs/game-module-guide.md` for complete step-by-step guide with code templates.

## Key Rules

- **Event types** are `Literal` types (single source of truth, auto-composed via `get_args()`)
- **Per-entry error handling** in leaderboard parsing (explicit loops, not list comprehensions)
- **Use common.py utilities**: `parse_uuid_safe()`, `safe_parse_leaderboard()`, `parse_float_list()`
- **Router delegates to service** layer; no business logic in endpoints

## Troubleshooting

- `ModuleNotFoundError`: Ensure `__init__.py` in `games/` and `games/{name}/`
- `404 Not Found`: Check the game is in the `GAMES` dict in `structures.py` (routers auto-register from it)
- `TypeError: JSON object must be str`: Use type-checking pattern (see `../../agent_docs/sqlite-json-handling.md`)
