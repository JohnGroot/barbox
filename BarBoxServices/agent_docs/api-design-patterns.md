# API Design Patterns

## RESTful Resource Naming

- **Nouns for resources**: `/box`, `/player`, `/game`
- **Hierarchical relationships**: `/box/{id}/session/{id}`
- **Actions via HTTP methods**: POST=create, GET=read, PUT=update

## Status Codes

| Code | Meaning | Usage |
|------|---------|-------|
| 200 | OK | Successful GET (default) |
| 201 | Created | Successful POST (resource created) |
| 202 | Accepted | PUT with client-generated ID |
| 404 | Not Found | Resource doesn't exist (via `scalar_one()`) |
| 422 | Unprocessable | Pydantic validation failed (automatic) |

## Endpoint Patterns

### Create (POST)
```python
@router.post("/", status_code=201)
async def create_box(
    new_box: schemas.BoxCreate,  # from the feature's schemas.py
    db_service: dependencies.Database,
) -> schemas.BoxDetailWithAPIKey:
    return await service.create_box(new_box, db_service)  # router delegates to service
```

### Create with Client ID (PUT)
```python
@router.put("/{box_id}/session/{session_id}", status_code=202)
async def create_session(
    box_id: UUID,
    session_id: UUID,
    player_id: Annotated[UUID, Header()],
    db_service: dependencies.Database,
    now: dependencies.Now,
) -> Identifiable:  # from bxctl.schemas
    await db_service.create(target=db.defs.BoxSession, data={...})
    return Identifiable(id=session_id)
```

### Retrieve (GET)
Use `joinedload` for relationships, `.unique().scalar_one()` for deduplication.

## Dependency Injection

```python
Database = Annotated[db.service.CRUD, Depends(_acquire_crud)]
Now = Annotated[datetime, Depends(_current_timestamp, use_cache=False)]
```

`use_cache=False` on `Now` ensures fresh timestamp per injection point.

## Error Handling

- **422**: Automatic from Pydantic validation
- **404**: Automatic from `scalar_one()` raising `NoResultFound`
- **400**: Manual via `HTTPException(status_code=400, detail="...")`
- **500**: Unhandled exceptions (use structlog for debugging)

## Response Models

- **Pydantic model** (preferred): Type-safe, validated, documented
- **Dict** (rare): Prototyping, dynamic schemas only
