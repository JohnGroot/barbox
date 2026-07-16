# Session Architecture Test
# Tests new two-tier session model with game_tag and close endpoint

# Setup: Create box
POST http://127.0.0.1:8000/box/
X-Registration-Secret: dev-registration-secret-UNSAFE-change-in-production
{
  "id": "{{newUuid}}",
  "name": "Test Box",
  "tag": "test_box"
}
HTTP 201
[Captures]
box-id: jsonpath "$['id']"
box-api-key: jsonpath "$['api_key']"

# Setup: Create player
POST http://127.0.0.1:8000/player/
X-Box-API-Key: {{box-api-key}}
X-Box-ID: {{box-id}}
{
  "id": "{{newUuid}}",
  "tag": "testplayer",
  "phone_number": "+13035555001",
  "pin": "1234",
  "origin_id": "{{box-id}}"
}
HTTP 201
[Captures]
player-id: jsonpath "$['id']"

# Test 1: Create session WITH game_tag (racing)
PUT http://127.0.0.1:8000/box/{{box-id}}/session/{{newUuid}}?game_tag=racing
X-Box-API-Key: {{box-api-key}}
Player-Id: {{player-id}}
HTTP 202
[Captures]
session1-id: jsonpath "$['id']"

# Test 2: Emit event to session
POST http://127.0.0.1:8000/box/session/{{session1-id}}
X-Box-API-Key: {{box-api-key}}
{
  "type": "play/begin",
  "timestamp": "{{newDate}}",
  "payload": {
    "game": "racing"
  }
}
HTTP 201

# Test 3: Close session explicitly
POST http://127.0.0.1:8000/box/session/{{session1-id}}/close
X-Box-API-Key: {{box-api-key}}
HTTP 200
[Asserts]
jsonpath "$.id" == "{{session1-id}}"

# Test 4: Create multiplayer session with game_tag (carrom)
PUT http://127.0.0.1:8000/box/{{box-id}}/session/{{newUuid}}?game_tag=carrom&player_ids=["{{player-id}}"]
X-Box-API-Key: {{box-api-key}}
Player-Id: {{player-id}}
HTTP 202
[Captures]
session2-id: jsonpath "$['id']"

# Test 5: Emit carrom event
POST http://127.0.0.1:8000/box/session/{{session2-id}}
X-Box-API-Key: {{box-api-key}}
{
  "type": "play/score",
  "timestamp": "{{newDate}}",
  "payload": {
    "game": "carrom",
    "score": 5
  }
}
HTTP 201

# Test 6: Close multiplayer session
POST http://127.0.0.1:8000/box/session/{{session2-id}}/close
X-Box-API-Key: {{box-api-key}}
HTTP 200
[Asserts]
jsonpath "$.id" == "{{session2-id}}"

# Test 7: Create session WITH game_tag (mining)
PUT http://127.0.0.1:8000/box/{{box-id}}/session/{{newUuid}}?game_tag=mining
X-Box-API-Key: {{box-api-key}}
Player-Id: {{player-id}}
HTTP 202
[Captures]
session3-id: jsonpath "$['id']"

# Test 8: Close mining session
POST http://127.0.0.1:8000/box/session/{{session3-id}}/close
X-Box-API-Key: {{box-api-key}}
HTTP 200
[Asserts]
jsonpath "$.id" == "{{session3-id}}"
