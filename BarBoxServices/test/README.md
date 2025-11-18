# BarBoxServices Test Suite

Integration tests for the BarBoxServices API using [Hurl](https://hurl.dev).

## Quick Start

### Running All Tests (Recommended)

```bash
# From BarBoxServices/ directory
sh scripts/test-isolated.sh
```

This runs all tests with **database isolation** - each test gets a clean database state.

### Running Individual Tests

```bash
# Reset database first
curl -X POST http://127.0.0.1:8000/test/reset

# Run specific test (note: paths changed due to reorganization)
hurl test/02-feature/carrom/carrom-game-flow.hurl
```

### Running Test Categories

```bash
# Run only smoke tests
hurl test/00-smoke/*.hurl

# Run only unit tests
hurl test/01-unit/*.hurl

# Run all carrom tests
hurl test/02-feature/carrom/*.hurl

# Run all integration tests
hurl test/03-integration/*.hurl
```

**Note**: When running subset of tests, remember to reset database first:
```bash
curl -X POST http://127.0.0.1:8000/test/reset && hurl test/02-feature/carrom/*.hurl
```

### Running Tests Without Isolation (Not Recommended)

```bash
# Runs all tests against shared database
sh scripts/test.sh
```

⚠️ **Warning**: Tests will share database state and may fail due to data contamination.

## Test Organization

### Test Categories

Tests are organized into a hierarchical structure by priority and scope:

```
test/
├── 00-smoke/                       # Critical path validation
│   └── full-resource-flow-smoke-test.hurl
│
├── 01-unit/                        # Single endpoint tests
│   ├── eventservice-query.hurl
│   └── username-availability.hurl
│
├── 02-feature/                     # Game-specific and feature tests
│   ├── auth/
│   │   ├── admin-authentication.hurl
│   │   └── auth-flow.hurl
│   ├── carrom/
│   │   ├── carrom-game-flow.hurl
│   │   └── carrom-leaderboard.hurl
│   ├── credits/
│   │   ├── credit-purchase-flow.hurl
│   │   ├── credits-management.hurl
│   │   └── machine-credit-flow.hurl
│   ├── mining/
│   │   └── mining-game-flow.hurl
│   └── racing/
│       ├── racing-game-flow.hurl
│       └── racing-leaderboard.hurl
│
├── 03-integration/                 # Multi-step integration tests
│   ├── account-creation-flow.hurl
│   ├── account-operations.hurl
│   ├── credit-operations.hurl
│   ├── game-session-lifecycle.hurl
│   └── session-architecture-test.hurl
│
└── fixtures/                       # Test templates and patterns
    └── README.md
```

### Test Execution Order

Tests run in **alphabetical order by directory**:
1. **00-smoke/** runs first (critical path)
2. **01-unit/** runs second (fast validation)
3. **02-feature/** runs third (game-specific flows)
4. **03-integration/** runs last (complex workflows)

Within each directory, tests run alphabetically by filename.

### Test Types

| Type | Description | Examples |
|------|-------------|----------|
| **Smoke Tests** | Critical path validation | full-resource-flow-smoke-test.hurl |
| **Feature Tests** | Game-specific flows | carrom-game-flow.hurl, racing-game-flow.hurl |
| **Unit Tests** | Single endpoint validation | username-availability.hurl |
| **Integration Tests** | Multi-step workflows | integration/* |

## Test Isolation

### Why Isolation Matters

Tests share a database instance. Without isolation:
- **Data bleeding**: Earlier tests create data that affects later tests
- **Order dependency**: Tests may pass/fail based on execution order
- **Non-deterministic failures**: Same test may pass or fail depending on what ran before

### How Isolation Works

The `test-isolated.sh` script:

1. Checks if backend is running
2. For each test file:
   - Calls `POST /test/reset` to drop and recreate database
   - Runs the test
   - Reports pass/fail
3. Summarizes results

### Manual Database Reset

```bash
# Reset database between test runs
curl -X POST http://127.0.0.1:8000/test/reset

# Verify environment
curl http://127.0.0.1:8000/test/environment
```

## Writing New Tests

### Test Structure Template

```hurl
# Test description and purpose
# Author: Your Name
# Date: YYYY-MM-DD

# Setup: Create resources
POST http://127.0.0.1:8000/box/
{
  "id": "{{newUuid}}",
  "name": "Test Box",
  "tag": "test_box"
}
HTTP 201
[Captures]
box-id: jsonpath "$['id']"
box-api-key: jsonpath "$['api_key']"

# Test: Perform action
POST http://127.0.0.1:8000/player/
X-Box-API-Key: {{box-api-key}}
{
  "id": "{{newUuid}}",
  "tag": "test_player",
  "phone_number": "+14155550123",
  "pin": "1234",
  "origin_id": "{{box-id}}"
}
HTTP 201
[Captures]
player-id: jsonpath "$['id']"

# Verify: Check results
GET http://127.0.0.1:8000/player/{{player-id}}
HTTP 200
[Asserts]
jsonpath "$.tag" == "test_player"
```

### Best Practices

#### 1. Use Descriptive Comments

```hurl
# Setup: Create test box for carrom game
POST http://127.0.0.1:8000/box/
{...}

# Test: Submit carrom round finish event with player1 winning
POST http://127.0.0.1:8000/box/session/{{session-id}}
{...}

# Verify: Player1 should appear at rank #1 on leaderboard
GET http://127.0.0.1:8000/game/carrom/leaderboard
[Asserts]
jsonpath "$.leaderboard[0]['player_id']" == "{{player1-id}}"
```

#### 2. Use Meaningful Variable Names

```hurl
# Good: Descriptive variable names
[Captures]
box-id: jsonpath "$['id']"
player1-id: jsonpath "$['id']"
carrom-session-id: jsonpath "$['id']"

# Bad: Generic names
[Captures]
id1: jsonpath "$['id']"
id2: jsonpath "$['id']"
id3: jsonpath "$['id']"
```

#### 3. Verify Intermediate Steps

```hurl
# Don't just submit events - verify they were created
POST http://127.0.0.1:8000/box/session/{{session-id}}
{
  "type": "carrom/round_finish",
  "payload": {...}
}
HTTP 201
[Asserts]
jsonpath "$.id" exists
jsonpath "$.type" == "carrom/round_finish"
```

#### 4. Use Appropriate HTTP Status Codes

```hurl
# Resource created
POST /box/
HTTP 201

# Resource retrieved
GET /box/{{id}}
HTTP 200

# Resource not found
GET /box/invalid-id
HTTP 404

# Validation error
POST /player/
{"invalid": "data"}
HTTP 422
```

#### 5. Test Error Cases

```hurl
# Test success case
POST /player/
{...valid data...}
HTTP 201

# Test duplicate username
POST /player/
{...same username...}
HTTP 409

# Test invalid origin box
POST /player/
{...invalid box id...}
HTTP 422
```

## Game-Specific Event Types

Different games use different event types for scoring. Make sure your test uses the correct event type for the leaderboard you're querying.

### Carrom Events

```hurl
# Carrom leaderboard queries carrom/round_finish events
POST /box/session/{{session-id}}
{
  "type": "carrom/round_finish",
  "payload": {
    "mode": "competitive",
    "winner": "{{player1-id}}",
    "scores": {
      "{{player1-id}}": 25,
      "{{player2-id}}": 18
    }
  }
}
```

### Racing Events

```hurl
# Racing leaderboard queries racing/race_finish events
POST /box/session/{{session-id}}
{
  "type": "racing/race_finish",
  "payload": {
    "track_id": "gocart_track",
    "lap_count": 3,
    "lap_times": [45.3, 44.8, 45.5],
    "total_time": 135.6
  }
}
```

### Mining Events

```hurl
# Mining tracks multiple event types
POST /box/session/{{session-id}}
{
  "type": "mining/extract_complete",
  "payload": {
    "gem_type": "ruby",
    "amount": 5,
    "location_id": "main_shaft"
  }
}
```

## Common Patterns

### Creating Test Fixtures

```hurl
# Pattern: Create box + player + session
POST http://127.0.0.1:8000/box/
{
  "id": "{{newUuid}}",
  "name": "Test Box",
  "tag": "test_box"
}
HTTP 201
[Captures]
box-id: jsonpath "$['id']"
box-api-key: jsonpath "$['api_key']"

POST http://127.0.0.1:8000/player/
X-Box-API-Key: {{box-api-key}}
{
  "id": "{{newUuid}}",
  "tag": "test_player",
  "phone_number": "+14155550100",
  "pin": "1234",
  "origin_id": "{{box-id}}"
}
HTTP 201
[Captures]
player-id: jsonpath "$['id']"

PUT http://127.0.0.1:8000/box/{{box-id}}/session/{{newUuid}}?game_tag=carrom
X-Box-API-Key: {{box-api-key}}
Player-Id: {{player-id}}
HTTP 202
[Captures]
session-id: jsonpath "$['id']"
```

### Querying Leaderboards

```hurl
# Query with limit
GET http://127.0.0.1:8000/game/carrom/leaderboard?metric=total_score&limit=10
HTTP 200
[Asserts]
jsonpath "$.leaderboard" count <= 10

# Query with track filter (racing)
GET http://127.0.0.1:8000/game/racing/leaderboard?track_id=mountain_circuit&metric=best_lap
HTTP 200
[Asserts]
jsonpath "$.track_id" == "mountain_circuit"

# Check player exists in leaderboard
GET http://127.0.0.1:8000/game/carrom/leaderboard
HTTP 200
[Asserts]
jsonpath "$.leaderboard[?(@.player_id == '{{player-id}}')]" exists
```

### Testing Authentication

```hurl
# Request requires API key
POST /player/
# Missing X-Box-API-Key header
{...}
HTTP 401

# Request with valid API key
POST /player/
X-Box-API-Key: {{box-api-key}}
{...}
HTTP 201

# Request with invalid API key
POST /player/
X-Box-API-Key: invalid_key
{...}
HTTP 401
```

## Troubleshooting

### Test Fails With "Unexpected data in leaderboard"

**Problem**: Test expects empty database but finds existing data

**Solution**: Run test with isolation:
```bash
sh scripts/test-isolated.sh
```

Or manually reset before running:
```bash
curl -X POST http://127.0.0.1:8000/test/reset
hurl test/your-test.hurl
```

### Test Fails With "Unknown event type"

**Problem**: Using wrong event type for game

**Solution**: Check game module documentation:
- Carrom uses `carrom/round_finish` for scoring
- Racing uses `racing/race_finish` for leaderboard
- Mining uses various `mining/*` events

### Test Fails With "Connection refused"

**Problem**: Backend server not running

**Solution**: Start the backend:
```bash
sh scripts/dev.sh
```

Wait for "Backend ready" message before running tests.

### Test Fails With "404 Not Found on /test/reset"

**Problem**: Server running in production mode

**Solution**: Start server in local/test mode:
```bash
ENV=local sh scripts/dev.sh
```

## Environment Requirements

### Backend Server

Tests require the backend server running in `local` or `test` mode:

```bash
# Start backend
cd /Users/johngroot/Dev/barbox/BarBoxServices
ENV=local sh scripts/dev.sh
```

### Required Endpoints

Tests use these special endpoints (only available in local/test mode):

- `POST /test/reset` - Drop and recreate database
- `GET /test/environment` - Get environment info
- `POST /test/seed` - Seed with test data (some tests)

## Performance

### Test Execution Time

- **Individual test**: ~1-3 seconds
- **Full suite (18 tests)**: ~30-60 seconds
- **With isolation**: +1-2 seconds per test (database reset overhead)

### Optimization Tips

1. **Run specific tests during development**:
   ```bash
   hurl test/carrom-game-flow.hurl
   ```

2. **Run full suite before committing**:
   ```bash
   sh scripts/test-isolated.sh
   ```

3. **Skip slow tests during iteration**:
   ```bash
   hurl test/username-availability.hurl test/account-creation-flow.hurl
   ```

## CI/CD Integration

### GitHub Actions Example

```yaml
name: Integration Tests

on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3

      - name: Start Backend
        run: |
          ENV=test sh scripts/dev.sh &
          # Wait for backend to be ready
          timeout 30 bash -c 'until curl -s http://127.0.0.1:8000/alive; do sleep 1; done'

      - name: Run Tests
        run: sh scripts/test-isolated.sh
```

## Related Documentation

- **Project README**: `../README.md` - Project overview and setup
- **API Documentation**: http://localhost:8000/redoc - API reference
- **Hurl Documentation**: https://hurl.dev - Hurl syntax and features

## Contributing

When adding new tests:

1. Follow naming conventions: `{feature}-{action}.hurl`
2. Add descriptive comments explaining test purpose
3. Use meaningful variable names
4. Verify tests pass in isolation
5. Update this README if adding new test categories
