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

Run the whole suite:

```bash
sh scripts/test.sh
```

Run a particular test with `hurl path/to/test.hurl`

## Logging

[WIP] Logs will be written simultaneously in styled human output and as a JSON stream to buffered files in this directory. Users and agents can `tail` when working.

## The Stack

**[FastAPI](https://fastapi.tiangolo.com)** is the web framework. A major perk of this library is documentation generated from source code. **Documentation links mentioned above are the authoritative manual for this application.**

**[SQL Alchemy](https://www.sqlalchemy.org)** is the database access & schema definition layer.

**[Structlog](https://www.structlog.org/en/stable/)** produces human and machine readable logs.

**[SQLite](https://www.sqlite.org)** runs in-memory as the primary form of persistence.

**[Hurl](https://hurl.dev)** is used for integration tests.

## Limitations / Pitfalls

...for now.

**When the application restarts, the database is wiped and recreated.** If you're just working on the game, that's not a problem. If you're editing `BarBoxServices` code _and_ the game at the same time, beware.
