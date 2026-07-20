# bxctl Package

## Structure

```
bxctl/
+-- env.py           # Environment configuration (ENV, SQLITE_PATH)
+-- registry.py      # GAMES registry + SessionEventType composition + CoreEvent
+-- errors.py        # ErrorCode enum, ErrorDetail envelope, creation_error_boundary
+-- schemas.py       # Shared pydantic mixins (Named, Tagged, Identifiable)
+-- app/             # App assembly (see app/CLAUDE.md)
+-- boxes/           # Boxes & sessions feature
+-- players/         # Players feature
+-- credits/         # Machine-credit pots feature
+-- payments/        # Stripe payments feature (+ packs.py, webhook.py)
+-- testing/         # Dev/test-only endpoints + deterministic seeding
+-- db/              # Database layer (see db/CLAUDE.md)
+-- games/           # Game modules (see games/CLAUDE.md)
```

## Feature Package Shape

Every feature (and every game module) follows the same layout:

- **router.py**: thin FastAPI endpoints - DI/auth params + docstrings, delegate to service
- **service.py**: business logic and database queries; signatures take `db.service.CRUD` (never the DI aliases)
- **schemas.py**: that feature's pydantic request/response models

## Import Rules (keeps the graph acyclic)

- Leaf modules: `env`, `errors`, `schemas`, `db/` - import nothing from bxctl above them
- `app/dependencies.py` NEVER imports `registry` or feature packages
- Features never import other features (`testing -> payments.service` is the sole sanctioned exception - the mock webhook reuses real credit issuance)
- `registry.py` imports the game packages; `games/validation.py` imports `registry`
- `app/main.py` imports everything and assembles the app

## Module Responsibilities

- **env.py**: Environment variables, database URL configuration
- **registry.py**: `GAMES` dict (single source of truth for game wiring) + `SessionEventType` union + `CoreEvent` StrEnum (guarded at import time)
- **errors.py**: `ErrorCode` StrEnum, `ErrorDetail` envelope, `creation_error_boundary` (shared IntegrityError->409 / Exception->500 translation)
- **db/**: SQLAlchemy models, CRUD operations, database connectivity, persisted-value enums (`SessionType`, `PaymentStatus`)
- **games/**: Self-contained game modules (carrom, racing, mining, nines)

## Model Conventions

- **Create models**: `{Resource}Create` - request bodies
- **Detail models**: `{Resource}Detail` - response bodies
- **Composition**: Use `Identifiable`, `Named`, `Tagged` mixins from `bxctl.schemas`
- **Conversion**: `model_validate(result, from_attributes=True)` between layers
- Response-model field types stay `str`/`Literal` (not enums) so OpenAPI/JSON shapes stay stable; enums are producer-side

## Event Types

`SessionEventType` in registry.py is a union of core + game-specific Literal types. New games extend it by adding their event type to the union. Producer code references `CoreEvent` members (or the per-game `Final` constants in `games/{game}/schemas.py`) instead of raw strings; validation stays Literal-based.

See `../agent_docs/domain-model-reference.md` for full domain model.
