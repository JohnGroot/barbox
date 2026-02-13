# Web API Layer

## Structure

```
web/
+-- main.py          # FastAPI app, lifespan, router registration
+-- dependencies.py  # Dependency injection (Database, Now)
+-- box.py           # Box and session endpoints
+-- player.py        # Player registration endpoints
```

## Dependency Injection

```python
Database = Annotated[db.service.CRUD, Depends(_acquire_crud)]  # Auto-commits/rollbacks
Now = Annotated[datetime, Depends(_current_timestamp, use_cache=False)]  # Fresh per injection
```

## Endpoint Conventions

- POST with 201 for resource creation
- PUT with 202 for client-generated IDs (idempotent)
- Explicit status codes on all endpoints
- Return type annotations match response models
- Delegate business logic to service layer

## Key Patterns

- **Pydantic validation**: Automatic 422 for invalid requests
- **Not found**: `scalar_one()` raises -> automatic 404
- **Eager loading**: `joinedload()` + `.unique()` for relationships
- **Raw SQL**: `db_service.get_many_raw()` for aggregations

See `../../agent_docs/api-design-patterns.md` for full patterns and examples.
