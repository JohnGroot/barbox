# Racing Game Integration Test
# Tests racing event submission and leaderboard aggregation

# Setup: Create box
POST http://127.0.0.1:8000/box/
X-Registration-Secret: dev-registration-secret-UNSAFE-change-in-production
{
  "id": "{{newUuid}}",
  "name": "Racing Arcade",
  "tag": "racing_box"
}
HTTP 201
[Captures]
box-id: jsonpath "$['id']"
box-api-key: jsonpath "$['api_key']"

# Setup: Create player 1
POST http://127.0.0.1:8000/player/
X-Box-API-Key: {{box-api-key}}
X-Box-ID: {{box-id}}
{
  "id": "{{newUuid}}",
  "tag": "racer1",
  "phone_number": "+16175554001",
  "pin": "1234",
  "origin_id": "{{box-id}}"
}
HTTP 201
[Captures]
player1-id: jsonpath "$['id']"

# Setup: Create player 2
POST http://127.0.0.1:8000/player/
X-Box-API-Key: {{box-api-key}}
X-Box-ID: {{box-id}}
{
  "id": "{{newUuid}}",
  "tag": "racer2",
  "phone_number": "+13105559999",
  "pin": "1234",
  "origin_id": "{{box-id}}"
}
HTTP 201
[Captures]
player2-id: jsonpath "$['id']"

# Player 1 starts racing session
PUT http://127.0.0.1:8000/box/{{box-id}}/session/{{newUuid}}?game_tag=racing
X-Box-API-Key: {{box-api-key}}
Player-Id: {{player1-id}}
HTTP 202
[Captures]
session1-id: jsonpath "$['id']"

# Player 1 completes lap 1
POST http://127.0.0.1:8000/box/session/{{session1-id}}
X-Box-API-Key: {{box-api-key}}
{
  "type": "racing/lap_complete",
  "timestamp": "{{newDate}}",
  "payload": {
    "lap_num": 1,
    "lap_time": 45.3,
    "checkpoints": [
      {"index": 0, "time": 10.2, "gap": 0.0},
      {"index": 1, "time": 25.1, "gap": 14.9}
    ]
  }
}
HTTP 201

# Player 1 completes lap 2
POST http://127.0.0.1:8000/box/session/{{session1-id}}
X-Box-API-Key: {{box-api-key}}
{
  "type": "racing/lap_complete",
  "timestamp": "{{newDate}}",
  "payload": {
    "lap_num": 2,
    "lap_time": 44.8,
    "checkpoints": [
      {"index": 0, "time": 10.0, "gap": 0.0},
      {"index": 1, "time": 24.8, "gap": 14.8}
    ]
  }
}
HTTP 201

# Player 1 finishes race
POST http://127.0.0.1:8000/box/session/{{session1-id}}
X-Box-API-Key: {{box-api-key}}
{
  "type": "racing/race_finish",
  "timestamp": "{{newDate}}",
  "payload": {
    "track_id": "gocart_track",
    "total_time": 135.6,
    "total_laps": 3,
    "lap_times": [45.3, 44.8, 45.5]
  }
}
HTTP 201

# Player 2 starts racing session
PUT http://127.0.0.1:8000/box/{{box-id}}/session/{{newUuid}}?game_tag=racing
X-Box-API-Key: {{box-api-key}}
Player-Id: {{player2-id}}
HTTP 202
[Captures]
session2-id: jsonpath "$['id']"

# Player 2 finishes race with worse time
POST http://127.0.0.1:8000/box/session/{{session2-id}}
X-Box-API-Key: {{box-api-key}}
{
  "type": "racing/race_finish",
  "timestamp": "{{newDate}}",
  "payload": {
    "track_id": "gocart_track",
    "total_time": 142.1,
    "total_laps": 3,
    "lap_times": [47.2, 46.9, 48.0]
  }
}
HTTP 201

# Query best_race leaderboard for 3-lap races
GET http://127.0.0.1:8000/game/racing/leaderboard?track_id=gocart_track&metric=best_race&laps=3&limit=10
HTTP 200
[Asserts]
jsonpath "$.track_id" == "gocart_track"
jsonpath "$.metric" == "best_race"
jsonpath "$.leaderboard" count == 2
jsonpath "$.leaderboard[0].player_id" == "{{player1-id}}"
jsonpath "$.leaderboard[0].metric_value" == 135.6
jsonpath "$.leaderboard[1].player_id" == "{{player2-id}}"
jsonpath "$.leaderboard[1].metric_value" == 142.1
