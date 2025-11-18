# Test Fixtures

This directory contains reusable test setup patterns and templates.

## Common Setup Pattern

Most tests follow this pattern:

### 1. Create Box
```hurl
POST http://127.0.0.1:8000/box/
{
  "id": "{{newUuid}}",
  "name": "Test Box Name",
  "tag": "unique_test_tag"
}
HTTP 201
[Captures]
box-id: jsonpath "$['id']"
box-api-key: jsonpath "$['api_key']"
```

### 2. Create Player
```hurl
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
```

### 3. Create Game Session
```hurl
PUT http://127.0.0.1:8000/box/{{box-id}}/session/{{newUuid}}?game_tag=carrom
X-Box-API-Key: {{box-api-key}}
Player-Id: {{player-id}}
HTTP 202
[Captures]
session-id: jsonpath "$['id']"
```

### 4. Submit Events
```hurl
POST http://127.0.0.1:8000/box/session/{{session-id}}
X-Box-API-Key: {{box-api-key}}
{
  "type": "play/begin",
  "timestamp": "{{newDate}}",
  "payload": {"game": "carrom"}
}
HTTP 201
```

## Phone Number Conventions

To avoid conflicts, use distinct phone numbers per test:
- **00-smoke**: +1415555xxxx (0000-0099)
- **01-unit**: +1415555xxxx (0100-0199)
- **02-feature/auth**: +1415555xxxx (0200-0299)
- **02-feature/carrom**: +1415555xxxx (0300-0399)
- **02-feature/racing**: +1415555xxxx (0400-0499)
- **02-feature/mining**: +1415555xxxx (0500-0599)
- **02-feature/credits**: +1415555xxxx (0600-0699)
- **03-integration**: +1415555xxxx (0700-0799)

## UUID Management

Always use `{{newUuid}}` for generating fresh UUIDs. Never hardcode UUIDs unless testing specific ID handling logic.

## Test Naming

Use descriptive tags that indicate the test:
- **Box tags**: `{test_type}_{feature}_box` (e.g., `smoke_test_box`)
- **Player tags**: `{test_type}_{feature}_player` (e.g., `carrom_test_player1`)
- **Session IDs**: Use `{{newUuid}}` to generate unique IDs

## Game-Specific Event Types

### Carrom
```hurl
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

### Racing
```hurl
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

### Mining
```hurl
{
  "type": "mining/extract_complete",
  "payload": {
    "gem_type": "ruby",
    "amount": 5,
    "location_id": "main_shaft"
  }
}
```

## Future: Reusable Snippets

Hurl 4.0+ may support includes. When available, we can extract common patterns into reusable files:

```
fixtures/
├── setup-box.hurl
├── setup-player.hurl
├── setup-session.hurl
└── events/
    ├── carrom-round.hurl
    ├── racing-lap.hurl
    └── mining-extract.hurl
```

Then tests could import them:
```hurl
@import fixtures/setup-box.hurl
@import fixtures/setup-player.hurl
# box-id and player-id variables now available
```
