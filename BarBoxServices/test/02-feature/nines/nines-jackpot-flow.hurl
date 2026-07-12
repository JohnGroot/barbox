# Nines Game Jackpot Integration Test
# Tests jackpot state query and jackpot win event submission

# Setup: Create box
POST http://127.0.0.1:8000/box/
{
  "id": "{{newUuid}}",
  "name": "Nines Arcade",
  "tag": "nines_box"
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
  "tag": "nines_player",
  "phone_number": "+14045559001",
  "pin": "1234",
  "origin_id": "{{box-id}}"
}
HTTP 201
[Captures]
player-id: jsonpath "$['id']"

# Test 1: Query jackpot state for venue with no wins
GET http://127.0.0.1:8000/game/nines/jackpot/test_venue_nines
HTTP 200
[Asserts]
jsonpath "$.venue_name" == "test_venue_nines"
jsonpath "$.last_win_timestamp" == null
jsonpath "$.last_winner_name" == null
jsonpath "$.last_jackpot_amount" == null

# Create a nines game session
PUT http://127.0.0.1:8000/box/{{box-id}}/session/{{newUuid}}?game_tag=nines
X-Box-API-Key: {{box-api-key}}
Player-Id: {{player-id}}
HTTP 202
[Captures]
session-id: jsonpath "$['id']"

# Player wins jackpot
POST http://127.0.0.1:8000/box/session/{{session-id}}
X-Box-API-Key: {{box-api-key}}
{
  "type": "nines/jackpot_won",
  "timestamp": "{{newDate}}",
  "payload": {
    "venue_name": "test_venue_nines",
    "player_id": "+14045559001",
    "jackpot_amount": 7500,
    "timestamp": "2025-11-30T12:00:00Z"
  }
}
HTTP 201

# Test 2: Query jackpot state after win - should return win details
GET http://127.0.0.1:8000/game/nines/jackpot/test_venue_nines
HTTP 200
[Asserts]
jsonpath "$.venue_name" == "test_venue_nines"
jsonpath "$.last_win_timestamp" exists
jsonpath "$.last_jackpot_amount" == 7500
# Winner name resolves to the player's tag via the phone_number join
jsonpath "$.last_winner_name" == "nines_player"

# Test 3: Different venue should still have no wins
GET http://127.0.0.1:8000/game/nines/jackpot/other_venue
HTTP 200
[Asserts]
jsonpath "$.venue_name" == "other_venue"
jsonpath "$.last_win_timestamp" == null
jsonpath "$.last_jackpot_amount" == null
