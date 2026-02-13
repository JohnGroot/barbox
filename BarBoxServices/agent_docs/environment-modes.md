# Environment Modes

Set via `ENV` environment variable (defaults to `local`).

## Configuration

```bash
ENV=local sh scripts/dev.sh   # Local development (default)
ENV=test sh scripts/dev.sh    # Testing
ENV=prod uv run fastapi run src/bxctl/web/main.py  # Production
```

## Environment-Specific Behavior

| Feature | Local/Test | Production |
|---------|-----------|------------|
| Test Endpoints (`/test/*`) | Available | 404 Not Found |
| Database Reset | Allowed | Not Available |
| Test Data Seeding | Allowed | Not Available |
| Error Details | Full | User-Friendly Only |

## Test Endpoints (Dev/Test Only)

- `POST /test/reset` - Drop and recreate all tables
- `POST /test/seed` - Seed deterministic test data
- `GET /test/environment` - Current environment config

## Test Credentials (auto-seeded)

- **Box ID**: `00000000-0000-0000-0000-000000000001`
- **API Key**: Get from `curl -X POST http://127.0.0.1:8000/test/seed`
- **Test Players**: `testuser1`, `testuser2`

API keys are deterministically derived from box_id using HMAC-SHA256 (64-char hex string).
