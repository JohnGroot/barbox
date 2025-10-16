# box is built, deployed, and associated to a bar
POST http://127.0.0.1:8000/box/
{
  "id": "{{newUuid}}",
  "name": "Best Intentions",
  "tag": "besties"
}
HTTP 201
[Captures]
box-id: jsonpath "$['id']"

# # we push a new game
# POST http://127.0.0.1:8000/game/
# {
#   "name": "Carrom: So Sick",
#   "tag": "carrom"
# }
# HTTP 201
# [Captures]
# game-id: jsonpath "$['id']"

# a player signs up
POST http://127.0.0.1:8000/player/
{
  "id": "{{newUuid}}",
  "name": "Ainsley McGrath",
  "tag": "ainx",
  "origin_id": "{{box-id}}"
}
HTTP 201
[Captures]
player-id: jsonpath "$['id']"

# begin a session
PUT http://127.0.0.1:8000/box/{{box-id}}/session/{{newUuid}}
Player-Id: {{player-id}}
HTTP 202
[Captures]
session-id: jsonpath "$['id']"

# add an event to the session
POST http://127.0.0.1:8000/box/session/{{session-id}}
{
  "event": {
    "type": "play/begin",
    "game": "carrom",
    "timestamp": "{{newDate}}"
  }
}
HTTP 201

# check the session
GET http://127.0.0.1:8000/box/session/{{session-id}}
HTTP 200