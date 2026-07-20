# bxctl Package

## Structure

```
bxctl/
+-- env.py           # Environment configuration (ENV, SQLITE_PATH)
+-- registry.py      # GAMES registry + SessionEventType composition
+-- errors.py        # ErrorCode + structured error envelope
+-- structures.py    # Pydantic API models (request/response)
+-- db/              # Database layer (see db/CLAUDE.md)
+-- games/           # Game modules (see games/CLAUDE.md)
+-- web/             # API layer (see web/CLAUDE.md)
```

## Module Responsibilities

- **env.py**: Environment variables, database URL configuration
- **registry.py**: `GAMES` dict (single source of truth for game wiring) + `SessionEventType` union + `CoreEvent`
- **errors.py**: `ErrorCode` enum and `ErrorDetail` error-envelope model
- **structures.py**: Pydantic request/response models. Separation: structures.py = API layer, db/defs.py = data layer
- **db/**: SQLAlchemy models, CRUD operations, database connectivity
- **games/**: Self-contained game modules (carrom, racing, mining, nines)
- **web/**: FastAPI routers, endpoints, dependency injection

## Model Conventions

- **Create models**: `{Resource}Create` - request bodies
- **Detail models**: `{Resource}Detail` - response bodies
- **Composition**: Use `Identifiable`, `Named`, `Tagged` mixins
- **Conversion**: `model_validate(result, from_attributes=True)` between layers

## Event Types

`SessionEventType` in registry.py is a union of core + game-specific Literal types. New games extend it by adding their event type to the union.

See `../agent_docs/domain-model-reference.md` for full domain model.
