# Carrom Game Integration Test
# Tests carrom event submission and leaderboard aggregation

# Setup: Create box
POST http://127.0.0.1:8000/box/
{
  "id": "{{newUuid}}",
  "name": "Carrom Table",
  "tag": "carrom_box"
}
HTTP 201
[Captures]
box-id: jsonpath "$['id']"

# Setup: Create player 1
POST http://127.0.0.1:8000/player/
{
  "id": "{{newUuid}}",
  "tag": "carrom_player1",
  "origin_id": "{{box-id}}"
}
HTTP 201
[Captures]
player1-id: jsonpath "$['id']"

# Setup: Create player 2
POST http://127.0.0.1:8000/player/
{
  "id": "{{newUuid}}",
  "tag": "carrom_player2",
  "origin_id": "{{box-id}}"
}
HTTP 201
[Captures]
player2-id: jsonpath "$['id']"

# Player 1 starts session and plays round 1 (wins)
PUT http://127.0.0.1:8000/box/{{box-id}}/session/{{newUuid}}?game_tag=carrom
Player-Id: {{player1-id}}
HTTP 202
[Captures]
session1-id: jsonpath "$['id']"

POST http://127.0.0.1:8000/box/session/{{session1-id}}
{
  "type": "carrom/round_finish",
  "timestamp": "{{newDate}}",
  "payload": {
    "mode": "competitive",
    "winner": "{{player1-id}}",
    "scores": {
      "{{player1-id}}": 25,
      "{{player2-id}}": 18
    }
  }
}
HTTP 201

# Player 1 plays round 2 (wins again)
POST http://127.0.0.1:8000/box/session/{{session1-id}}
{
  "type": "carrom/round_finish",
  "timestamp": "{{newDate}}",
  "payload": {
    "mode": "competitive",
    "winner": "{{player1-id}}",
    "scores": {
      "{{player1-id}}": 22,
      "{{player2-id}}": 20
    }
  }
}
HTTP 201

# Player 2 starts session and plays round 1 (wins)
PUT http://127.0.0.1:8000/box/{{box-id}}/session/{{newUuid}}?game_tag=carrom
Player-Id: {{player2-id}}
HTTP 202
[Captures]
session2-id: jsonpath "$['id']"

POST http://127.0.0.1:8000/box/session/{{session2-id}}
{
  "type": "carrom/round_finish",
  "timestamp": "{{newDate}}",
  "payload": {
    "mode": "competitive",
    "winner": "{{player2-id}}",
    "scores": {
      "{{player1-id}}": 19,
      "{{player2-id}}": 24
    }
  }
}
HTTP 201

# Query total_score leaderboard
GET http://127.0.0.1:8000/game/carrom/leaderboard?metric=total_score&limit=10
HTTP 200
[Asserts]
jsonpath "$.metric" == "total_score"
jsonpath "$.leaderboard" count == 2

# Query total_wins leaderboard - player 1 should have 2 wins, player 2 should have 1 win
GET http://127.0.0.1:8000/game/carrom/leaderboard?metric=total_wins&limit=10
HTTP 200
[Asserts]
jsonpath "$.metric" == "total_wins"
jsonpath "$.leaderboard" count == 2
jsonpath "$.leaderboard[0].player_id" == "{{player1-id}}"
jsonpath "$.leaderboard[0].total_wins" == 2
jsonpath "$.leaderboard[1].player_id" == "{{player2-id}}"
jsonpath "$.leaderboard[1].total_wins" == 1
