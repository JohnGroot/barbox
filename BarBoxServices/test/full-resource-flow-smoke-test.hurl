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

# add a series of events
POST http://127.0.0.1:8000/box/session/{{session-id}}
{
  "type": "play/begin",
  "timestamp": "{{newDate}}",
  "payload": { "game": "carrom" }
}

POST http://127.0.0.1:8000/box/session/{{session-id}}
{
  "type": "play/score",
  "timestamp": "{{newDate}}",
  "payload": { "points": 10 }
}

POST http://127.0.0.1:8000/box/session/{{session-id}}
{
  "type": "play/finish",
  "timestamp": "{{newDate}}",
  "payload": null
}
HTTP 201

# check the session
GET http://127.0.0.1:8000/box/session/{{session-id}}
HTTP 200
[Asserts]
jsonpath "$.events" count == 3

# should all end up in the leaderboard
GET http://127.0.0.1:8000/game/leaderboard/carrom
HTTP 200
[Asserts]
jsonpath "$[0]['player']['id']" == "{{player-id}}"
jsonpath "$[0]['score']" == 10