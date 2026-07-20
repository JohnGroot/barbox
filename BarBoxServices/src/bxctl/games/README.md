# Adding a New Game

## Quick Start

1. **Create game module:**
   ```bash
   mkdir -p games/foo
   touch games/foo/{__init__.py,schemas.py,service.py,router.py}
   ```

2. **Define event types in `foo/schemas.py`:**
   ```python
   from typing import Literal

   FooEventType = Literal["foo/start", "foo/score", "foo/end"]
   EventType = FooEventType  # Required canonical name
   ```

3. **Export from `foo/__init__.py`:**
   ```python
   from . import router, schemas
   ```

4. **Register in `registry.py` (ONLY FILE TO EDIT):**
   ```python
   from bxctl.games import foo  # Add import

   GAMES = {
       ...,
       "foo": {"schemas": foo.schemas, "router": foo.router},  # Add to registry
   }

   type SessionEventType = ... | foo.schemas.FooEventType  # Add to type union
   ```

**That's it!** validation.py and main.py auto-update from the GAMES registry.

## Module Structure

```
games/foo/
├── __init__.py       # Export schemas and router
├── schemas.py        # EventType + Pydantic models
├── service.py        # Business logic & SQL queries
└── router.py         # FastAPI endpoints
```
