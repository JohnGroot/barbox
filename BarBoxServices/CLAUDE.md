# BarBoxServices

Python FastAPI backend for the BarBox game platform. Provides REST/RPC APIs for session management, leaderboards, and persistent game state across physical game terminals.

## Core Responsibilities

- **Box Management** - Register and track physical game terminal installations
- **Player Profiles** - User accounts with origin box tracking
- **Session Tracking** - Real-time game session events and scoring
- **Leaderboards** - Aggregate scores across sessions and players

## Game Module Architecture

Self-contained game modules in `src/bxctl/games/`:

```
games/{game_name}/
+-- schemas.py    # Event types (Literal) + Pydantic models
+-- service.py    # Business logic & database queries
+-- router.py     # FastAPI endpoints
```

Current games: carrom, racing, mining, nines. Adding a game touches three core
registration points (see below); routers and the validation event registry
auto-derive from the `GAMES` dict.
See `agent_docs/game-module-guide.md` for complete guide.

## Session Event Lifecycle

1. `play/begin` - Game starts (includes game tag)
2. `play/score` - Score events during gameplay
3. `play/finish` - Game completes
4. `quit` - Player exits session

## CRITICAL: Event Type Registration

**New event types MUST be registered in these locations:**

1. **Game schemas**: `src/bxctl/games/{game}/schemas.py` - `Literal[...]` type
   plus the canonical `EventType = FooEventType` alias
2. **Master validation**: `src/bxctl/registry.py` - `SessionEventType` union
3. **Payload classification**: `src/bxctl/games/validation.py` - every event in
   `EVENT_PAYLOAD_MODELS` (Pydantic payload model) or `NO_PAYLOAD_EVENTS`
   (explicitly unvalidated)

New *games* additionally register in the `GAMES` dict (`registry.py`), which
auto-wires their router and event registry.

Missing from `registry.py` = HTTP 422 validation error. Missing payload
classification = `RuntimeError` at import (`_check_payload_model_coverage`).

## CRITICAL: API Path Synchronization

When modifying endpoint paths, grep and update ALL usages across BOTH codebases:
- `BarBoxServices/` - Route definitions, integration tests
- `BarBoxApp/` - HTTP client calls, API path constants

## Quick Start

```bash
sh scripts/dev.sh              # Start dev server (http://localhost:8000)
sh scripts/dev-reset.sh        # Reset database and re-seed
sh scripts/test.sh             # Run all integration tests
hurl test/account-creation-flow.hurl  # Run specific test
```

API docs: http://localhost:8000/redoc (human) | http://localhost:8000/docs (interactive)

## Code Style

- **Type hints**: Required for all function signatures
- **Modern Python**: 3.13+ features (type aliases, generic syntax)
- **Async**: Always `async/await` for I/O operations
- **Pydantic**: All API models and validation
- **Logging**: structlog for structured logging
- **Naming**: Modules snake_case, Classes PascalCase, Functions snake_case, Constants UPPER_CASE

## Comment Philosophy

A comment earns its place only by stating what the code *cannot*: a constraint, a non-obvious rationale, or an external contract (e.g. a webhook payload shape, a lazy-load gotcha). Everything else is noise.

- **Delete on sight**: restatement comments, change-log narration ("now managed by", "no longer", "moved to X"), fix/process markers (`CRITICAL FIX:`, `PHASE n:`, `OPTIMIZED:`), docstrings that just repeat the signature, commented-out code.
- **Keep**: constraints, timing/ordering requirements, external contracts (backend/webhook payload shapes, cwd-relative paths, timing-attack mitigations), reference-data-only notes.
- No `# ====` banner comments.

## Tech Stack

- **FastAPI** - Web framework with OpenAPI docs
- **SQLAlchemy 2.0** - Async ORM with dataclass mapping
- **SQLite** - File-based database (future: PostgreSQL)
- **Hurl** - HTTP integration testing
- **uv** - Package manager
- **Ruff** - Linter/formatter

## Project Structure

```
BarBoxServices/
+-- src/bxctl/
|   +-- registry.py      # GAMES registry + SessionEventType composition
|   +-- errors.py        # ErrorCode + error envelope + creation_error_boundary
|   +-- schemas.py       # Shared pydantic mixins (Named, Tagged, Identifiable)
|   +-- env.py           # Environment configuration
|   +-- app/             # App assembly (main.py, dependencies.py, auth.py)
|   +-- boxes/           # Boxes & sessions feature (router/service/schemas)
|   +-- players/         # Players feature (router/service/schemas)
|   +-- credits/         # Machine-credit pots feature (router/service/schemas)
|   +-- payments/        # Stripe feature (router/service/schemas/packs/webhook)
|   +-- testing/         # Dev/test-only endpoints + deterministic seeding
|   +-- games/           # Game modules (carrom, racing, mining, nines)
|   +-- db/              # Database layer
+-- test/                # Hurl integration tests
+-- scripts/             # Dev scripts
```

Feature packages mirror the games-module shape (`router.py` thin endpoints,
`service.py` business logic, `schemas.py` API models; deliberate exceptions:
payments' checkout/webhook endpoints keep Stripe-SDK glue inline, and
testing/ is router + seeding only). Import rules: features
never import other features (`testing -> payments.service` is the sole
sanctioned exception); `app/dependencies.py` never imports `registry` or
feature packages (this keeps the import graph acyclic).

## Testing Philosophy

- Integration tests over unit tests (full HTTP request/response cycles)
- Hurl for API tests (plain text, readable, version-controlled)
- Test realistic flows that mirror client usage

## Reference Documentation

Detailed patterns and examples in `agent_docs/`:

| File | Contents |
|------|----------|
| `domain-model-reference.md` | Full domain model, entity relationships, data flow |
| `sqlite-json-handling.md` | json_extract() behavior + robust parsing patterns |
| `api-design-patterns.md` | Endpoint patterns, status codes, error handling |
| `database-patterns.md` | SQLAlchemy conventions, CRUD, query patterns |
| `game-module-guide.md` | Step-by-step guide for adding new game modules |
| `account-creation-flow.md` | Validation + creation API with error codes |
| `environment-modes.md` | Local/test/production differences, test endpoints |
