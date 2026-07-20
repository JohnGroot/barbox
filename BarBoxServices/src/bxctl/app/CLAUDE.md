# App Assembly Layer

## Structure

```
app/
+-- main.py             # FastAPI app, lifespan, middleware, router registration
+-- dependencies.py     # Dependency injection (Database, Now, box/player auth)
+-- auth.py             # Box API key derivation, PIN hashing, JWT, phone normalization
```

Feature routers live in their feature packages (`bxctl/{boxes,players,credits,payments,testing}/router.py`); main.py imports and registers them, plus the game routers auto-registered from the `GAMES` dict in `registry.py`.

## Dependency Injection

```python
Database = Annotated[db.service.CRUD, Depends(_acquire_crud)]  # Auto-commits/rollbacks
Now = Annotated[datetime, Depends(_current_timestamp, use_cache=False)]  # Fresh per injection
```

Routers use these Annotated aliases; service functions take the concrete `db.service.CRUD` / `datetime` so they never import this module for types.

`dependencies.py` must NOT import `registry` or feature packages - it sits below them in the import graph (game routers import it at registry-build time).

## Endpoint Conventions

- POST with 201 for resource creation
- PUT with 202 for client-generated IDs (idempotent)
- Explicit status codes on all endpoints
- Return type annotations match response models
- Delegate business logic to the feature's service layer

## Key Patterns

- **Pydantic validation**: Automatic 422 for invalid requests
- **Not found**: `scalar_one()` raises -> automatic 404
- **Eager loading**: `joinedload()` + `.unique()` for relationships
- **Raw SQL**: `db_service.get_many_raw()` for aggregations
- **Creation errors**: wrap creation blocks in `errors.creation_error_boundary` for the shared IntegrityError->409 / Exception->500 translation

See `../../agent_docs/api-design-patterns.md` for full patterns and examples.
