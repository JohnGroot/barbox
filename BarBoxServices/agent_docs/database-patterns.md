# Database Patterns

## Stack

- **SQLAlchemy 2.0**: Async ORM with type hints, `MappedAsDataclass` base
- **SQLite**: File-based (`app.db`), JSON1 extension for payload queries
- **aiosqlite**: Async adapter

## Model Conventions

```python
class Base(PkMixin, MappedAsDataclass, DeclarativeBase):
    # Auto table names: BoxSession -> box_session
    # UUID primary keys via uuid4()
    # Type mapping: JsonObject -> sqlite.JSON

# Foreign key helper
type BoxFk = Annotated[UUID, fk_to(Box)]

class Player(Base):
    tag: Mapped[str]
    origin_id: Mapped[BoxFk]  # Type-safe FK
```

## CRUD Service

```python
# Create and read back
box = await db_service.create(target=db.defs.Player, data=new_player, read_as=schemas.PlayerDetail)

# Get with eager loading
result = await db_service.session.execute(
    select(BoxSession).options(joinedload(BoxSession.events)).where(BoxSession.id == id)
)
session = result.unique().scalar_one()

# Raw SQL for aggregations
result = await db_service.get_many_raw(sql, params)
```

## Session Lifecycle

Request -> FastAPI creates session -> endpoint executes -> commit on success / rollback on error -> session closed.

## Common Pitfalls

- **Forgetting `unique()`** with joins: duplicate results. Always `.unique().scalars().all()`
- **Using `scalar()` instead of `scalar_one()`**: silent None vs explicit 404
- **N+1 queries**: Use `joinedload` for relationships
- **Missing async/await**: `session.execute()` must be awaited

## JSON Path Queries

```python
sql = "SELECT * FROM box_session_event WHERE json_extract(payload, '$.points') > :min"
```

## Schema Management

Development: Schema created on startup, dropped on shutdown (see lifespan in `main.py`).
Production (future): Alembic migrations.
