# Database Layer

## Structure

```
db/
+-- defs.py         # SQLAlchemy ORM models (Base, Box, Player, BoxSession, BoxSessionEvent)
|                   # + persisted-value StrEnums (SessionType, PaymentStatus)
+-- service.py      # CRUD operations (create, get, get_many_raw)
+-- connectivity.py # Engine configuration, session factory
```

Persisted-value enums: columns stay `Mapped[str]` - `SessionType` and
`PaymentStatus` are producer-side StrEnums whose values match what's stored,
so no migration is implied by the enum types. In raw SQL, bind enum values as
parameters (`.value`) rather than interpolating literals.

## Stack

- **SQLAlchemy 2.0** async ORM with `MappedAsDataclass` base
- **SQLite** with aiosqlite adapter, file-based (`app.db`)
- Auto table names: `BoxSession` class -> `box_session` table
- UUID primary keys via `uuid4()`

## Key Patterns

- **Foreign key helper**: `type BoxFk = Annotated[UUID, fk_to(Box)]` for type-safe FKs
- **JSON fields**: `Mapped[JsonObject]` maps to `sqlite.JSON`
- **CRUD service**: `db_service.create(target=Model, data=pydantic_model, read_as=ResponseModel)`
- **Raw SQL**: `db_service.get_many_raw(sql, params)` for complex aggregations

## Critical Rules

- Always use `unique()` with `joinedload` joins to avoid duplicate results
- Use `scalar_one()` not `scalar()` to get explicit 404 on missing resources
- Client must provide UUID `id` for creation (no auto-increment)
- All operations must be `async/await`

## Session Lifecycle

FastAPI injects `CRUD(session)` via dependency. Auto-commits on success, auto-rollbacks on exception.

## Schema Management

Development: Created on startup, dropped on shutdown (lifespan in `app/main.py`).

See `../../agent_docs/database-patterns.md` for full patterns and examples.
