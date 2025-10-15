# box is built, deployed, and associated to a bar
POST http://127.0.0.1:8000/box/
{
  "name": "Best Intentions",
  "tag": "besties"
}
HTTP 201
[Captures]
box-id: jsonpath "$['id']"

# we push a new game
POST http://127.0.0.1:8000/game/
{
  "name": "Carrom: So Sick",
  "tag": "carrom"
}
HTTP 201
[Captures]
game-id: jsonpath "$['id']"

# a player signs up
POST http://127.0.0.1:8000/player/
{
  "name": "Ainsley McGrath",
  "tag": "ainx",
  "origin_id": "{{box-id}}"
}
HTTP 201
[Captures]
player-id: jsonpath "$['id']"

# begin a session
PUT http://127.0.0.1:8000/box/{{box-id}}/session
Player-Id: {{player-id}}
HTTP 202
[Captures]
session-id: jsonpath "$['id']"

# check the session
GET http://127.0.0.1:8000/box/session/{{session-id}}
[Options]
retry: 3   
retry-interval: 500ms
HTTP 200