# BarBoxServices

Implementation of the REST/RPC Api for BarBox.

## Start the local server

```bash
sh scripts/dev.sh
```

Pretty, human readable docs: http://localhost:8000/redoc

Ugly, interactive docs: http://localhost:8000/docs

For machines, agents, assistants: http://localhost:8000/openapi.json

## Run tests

...Soon

## The Stack

**[FastAPI](https://fastapi.tiangolo.com)** is the web framework. A major perk of this library is documentation generated from source code. **Documentation links mentioned above are the authoritative manual for this application.**

**[SQL Alchemy](https://www.sqlalchemy.org)** is the database access & schema definition layer.

**[SQLite](https://www.sqlite.org)** runs in-memory as the primary form of persistence.

**[Hurl](https://hurl.dev)** is used for integration tests.

## Limitations / Pitfalls

...for now.

- When the application restarts, the database is wiped and recreated.
