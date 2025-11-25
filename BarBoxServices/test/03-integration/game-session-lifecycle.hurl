# Game Session Lifecycle Test Suite
# Tests entering game, emitting events, exiting game, and logging out

# Setup: Create test box
POST http://127.0.0.1:8000/box/
{
  "id": "{{newUuid}}",
  "name": "Test Box Sessions",
  "tag": "test_box_sessions"
}
HTTP 201
[Captures]
box-id: jsonpath "$['id']"
box-api-key: jsonpath "$['api_key']"

# Setup: Create test player
POST http://127.0.0.1:8000/player/
X-Box-API-Key: {{box-api-key}}
X-Box-ID: {{box-id}}
{
  "id": "{{newUuid}}",
  "tag": "sessionuser",
  "phone_number": "+14155550401",
  "pin": "1234",
  "origin_id": "{{box-id}}"
}
HTTP 201
[Captures]
player-id: jsonpath "$['id']"

# Test 1: Enter game (create activity session for mining)
PUT http://127.0.0.1:8000/box/{{box-id}}/session/{{newUuid}}?game_tag=mining
X-Box-API-Key: {{box-api-key}}
Player-Id: {{player-id}}
HTTP 202
[Captures]
mining-session-id: jsonpath "$['id']"
[Asserts]
jsonpath "$.game_tag" == "mining"

# Test 2: Emit game begin event
POST http://127.0.0.1:8000/box/session/{{mining-session-id}}
X-Box-API-Key: {{box-api-key}}
{
  "type": "play/begin",
  "timestamp": "{{newDate}}",
  "payload": {
    "game": "mining",
    "mode": "standard"
  }
}
HTTP 201

# Test 3: Emit gameplay events
POST http://127.0.0.1:8000/box/session/{{mining-session-id}}
X-Box-API-Key: {{box-api-key}}
{
  "type": "mining/extract_complete",
  "timestamp": "{{newDate}}",
  "payload": {
    "gem_type": "painite",
    "quantity": 5,
    "location_id": "test_cave"
  }
}
HTTP 201

# Test 4: Emit game finish event
POST http://127.0.0.1:8000/box/session/{{mining-session-id}}
X-Box-API-Key: {{box-api-key}}
{
  "type": "play/finish",
  "timestamp": "{{newDate}}",
  "payload": {
    "duration_seconds": 300,
    "gems_collected": 5
  }
}
HTTP 201

# Test 5: Exit game (close activity session)
POST http://127.0.0.1:8000/box/session/{{mining-session-id}}/close
X-Box-API-Key: {{box-api-key}}
HTTP 200
[Asserts]
jsonpath "$.id" == "{{mining-session-id}}"

# Test 6: Query closed session to verify events were recorded
GET http://127.0.0.1:8000/box/session/{{mining-session-id}}
HTTP 200
[Asserts]
jsonpath "$.game_tag" == "mining"
jsonpath "$.events" count >= 3

# Test 7: Enter different game (carrom) for same player
PUT http://127.0.0.1:8000/box/{{box-id}}/session/{{newUuid}}?game_tag=carrom
X-Box-API-Key: {{box-api-key}}
Player-Id: {{player-id}}
HTTP 202
[Captures]
carrom-session-id: jsonpath "$['id']"
[Asserts]
jsonpath "$.game_tag" == "carrom"

# Test 8: Emit carrom events
POST http://127.0.0.1:8000/box/session/{{carrom-session-id}}
X-Box-API-Key: {{box-api-key}}
{
  "type": "play/begin",
  "timestamp": "{{newDate}}",
  "payload": {
    "game": "carrom",
    "mode": "practice"
  }
}
HTTP 201

POST http://127.0.0.1:8000/box/session/{{carrom-session-id}}
X-Box-API-Key: {{box-api-key}}
{
  "type": "play/score",
  "timestamp": "{{newDate}}",
  "payload": {
    "player_id": "{{player-id}}",
    "points": 10,
    "piece_type": "white"
  }
}
HTTP 201

# Test 9: Exit carrom game
POST http://127.0.0.1:8000/box/session/{{carrom-session-id}}/close
X-Box-API-Key: {{box-api-key}}
HTTP 200

# Test 10: Enter racing game
PUT http://127.0.0.1:8000/box/{{box-id}}/session/{{newUuid}}?game_tag=racing
X-Box-API-Key: {{box-api-key}}
Player-Id: {{player-id}}
HTTP 202
[Captures]
racing-session-id: jsonpath "$['id']"

# Test 11: Emit racing events
POST http://127.0.0.1:8000/box/session/{{racing-session-id}}
X-Box-API-Key: {{box-api-key}}
{
  "type": "play/begin",
  "timestamp": "{{newDate}}",
  "payload": {
    "game": "racing",
    "track": "oval"
  }
}
HTTP 201

POST http://127.0.0.1:8000/box/session/{{racing-session-id}}
X-Box-API-Key: {{box-api-key}}
{
  "type": "play/finish",
  "timestamp": "{{newDate}}",
  "payload": {
    "final_time": 45.3,
    "lap_times": [15.1, 15.0, 15.2],
    "checkpoints_hit": 12
  }
}
HTTP 201

# Test 12: Exit racing game (logout simulation - close final session)
POST http://127.0.0.1:8000/box/session/{{racing-session-id}}/close
X-Box-API-Key: {{box-api-key}}
HTTP 200
[Asserts]
jsonpath "$.id" == "{{racing-session-id}}"

# Test 13: Verify all sessions were created for this player
# (This would require a player sessions query endpoint - future enhancement)
