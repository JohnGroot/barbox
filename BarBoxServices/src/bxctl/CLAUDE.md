# bxctl Package

## Structure

```
bxctl/
+-- env.py           # Environment configuration (ENV, SQLITE_PATH)
+-- structures.py    # Pydantic API models (request/response + SessionEventType)
+-- db/              # Database layer (see db/CLAUDE.md)
+-- games/           # Game modules (see games/CLAUDE.md)
+-- web/             # API layer (see web/CLAUDE.md)
```

## Module Responsibilities

- **env.py**: Environment variables, database URL configuration
- **structures.py**: All Pydantic request/response models. Separation: structures.py = API layer, db/defs.py = data layer
- **db/**: SQLAlchemy models, CRUD operations, database connectivity
- **games/**: Self-contained game modules (carrom, racing, mining)
- **web/**: FastAPI routers, endpoints, dependency injection

## Model Conventions

- **Create models**: `{Resource}Create` - request bodies
- **Detail models**: `{Resource}Detail` - response bodies
- **Composition**: Use `Identifiable`, `Named`, `Tagged` mixins
- **Conversion**: `model_validate(result, from_attributes=True)` between layers

## Event Types

`SessionEventType` in structures.py is a union of core + game-specific Literal types. New games extend it by adding their event type to the union.

See `../agent_docs/domain-model-reference.md` for full domain model.
