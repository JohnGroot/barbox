# ============================================
# SMOKE TEST: Full Resource Flow
# ============================================
# Purpose: End-to-end validation of critical path
# - Box registration
# - Player account creation
# - Game session lifecycle
# - Event submission
# - Leaderboard aggregation
#
# This test verifies the complete happy path through
# the system. If this test fails, core functionality
# is broken.
# ============================================

# Step 1: Box registration
# A physical game terminal registers with the backend
POST http://127.0.0.1:8000/box/
{
  "id": "{{newUuid}}",
  "name": "Best Intentions",
  "tag": "besties"
}
HTTP 201
[Captures]
box-id: jsonpath "$['id']"
box-api-key: jsonpath "$['api_key']"

# Step 2: Player account creation
# A player creates an account at this box
POST http://127.0.0.1:8000/player/
X-Box-API-Key: {{box-api-key}}
{
  "id": "{{newUuid}}",
  "tag": "ainx",
  "phone_number": "+13105553001",
  "pin": "1234",
  "origin_id": "{{box-id}}"
}
HTTP 201
[Captures]
player-id: jsonpath "$['id']"

# Step 3: Start game session
# Player begins a carrom game session
PUT http://127.0.0.1:8000/box/{{box-id}}/session/{{newUuid}}?game_tag=carrom
X-Box-API-Key: {{box-api-key}}
Player-Id: {{player-id}}
HTTP 202
[Captures]
session-id: jsonpath "$['id']"

# Step 4: Submit gameplay events
# Step 4a: Game begins
POST http://127.0.0.1:8000/box/session/{{session-id}}
X-Box-API-Key: {{box-api-key}}
{
  "type": "play/begin",
  "timestamp": "{{newDate}}",
  "payload": { "game": "carrom" }
}

# Step 4b: Submit scoring event
# Carrom leaderboard aggregates carrom/round_finish events
POST http://127.0.0.1:8000/box/session/{{session-id}}
X-Box-API-Key: {{box-api-key}}
{
  "type": "carrom/round_finish",
  "timestamp": "{{newDate}}",
  "payload": {
    "mode": "practice",
    "winner": "{{player-id}}",
    "scores": {
      "{{player-id}}": 10
    }
  }
}

# Step 4c: Game finishes
POST http://127.0.0.1:8000/box/session/{{session-id}}
X-Box-API-Key: {{box-api-key}}
{
  "type": "play/finish",
  "timestamp": "{{newDate}}",
  "payload": null
}
HTTP 201

# Step 5: Verify session recorded all events
GET http://127.0.0.1:8000/box/session/{{session-id}}
HTTP 200
[Asserts]
jsonpath "$.events" count == 3

# Step 6: Verify leaderboard aggregation
# Player's score should appear in leaderboard
# With database isolation, this player is the only entry
GET http://127.0.0.1:8000/game/carrom/leaderboard?metric=total_score&limit=1
HTTP 200
[Asserts]
jsonpath "$.leaderboard" count == 1
jsonpath "$.leaderboard[0]['player_id']" == "{{player-id}}"
jsonpath "$.leaderboard[0]['total_score']" == 10