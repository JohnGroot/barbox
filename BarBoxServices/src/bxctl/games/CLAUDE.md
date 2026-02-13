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

## Adding a New Game

1. Create `games/{name}/` with `__init__.py`, `schemas.py`, `service.py`, `router.py`
2. Define event types as `Literal[...]` in schemas.py
3. Register events in `structures.py` SessionEventType union
4. Register in `games/validation.py` event registry
5. Register router in `web/main.py` game_routers tuple

See `../../agent_docs/game-module-guide.md` for complete step-by-step guide with code templates.

## Key Rules

- **Event types** are `Literal` types (single source of truth, auto-composed via `get_args()`)
- **Per-entry error handling** in leaderboard parsing (explicit loops, not list comprehensions)
- **Use common.py utilities**: `parse_uuid_safe()`, `safe_parse_leaderboard()`, `parse_float_list()`
- **Router delegates to service** layer; no business logic in endpoints

## Troubleshooting

- `ModuleNotFoundError`: Ensure `__init__.py` in `games/` and `games/{name}/`
- `404 Not Found`: Check router registered in `main.py`
- `TypeError: JSON object must be str`: Use type-checking pattern (see `../../agent_docs/sqlite-json-handling.md`)
