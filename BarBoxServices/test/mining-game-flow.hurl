# Mining Game Integration Test
# Tests mining event submission and inventory aggregation

# Setup: Create box
POST http://127.0.0.1:8000/box/
{
  "id": "{{newUuid}}",
  "name": "Mining Station",
  "tag": "mining_box"
}
HTTP 201
[Captures]
box-id: jsonpath "$['id']"

# Setup: Create player
POST http://127.0.0.1:8000/player/
{
  "id": "{{newUuid}}",
  "tag": "miner1",
  "origin_id": "{{box-id}}"
}
HTTP 201
[Captures]
player-id: jsonpath "$['id']"

# Player starts mining session
PUT http://127.0.0.1:8000/box/{{box-id}}/session/{{newUuid}}?game_tag=mining
Player-Id: {{player-id}}
HTTP 202
[Captures]
session-id: jsonpath "$['id']"

# Player extracts ruby
POST http://127.0.0.1:8000/box/session/{{session-id}}
{
  "type": "mining/extract_complete",
  "timestamp": "{{newDate}}",
  "payload": {
    "gem_type": "ruby",
    "quantity": 5,
    "location_id": "mining_box"
  }
}
HTTP 201

# Player extracts sapphire
POST http://127.0.0.1:8000/box/session/{{session-id}}
{
  "type": "mining/extract_complete",
  "timestamp": "{{newDate}}",
  "payload": {
    "gem_type": "sapphire",
    "quantity": 3,
    "location_id": "mining_box"
  }
}
HTTP 201

# Player extracts more ruby
POST http://127.0.0.1:8000/box/session/{{session-id}}
{
  "type": "mining/extract_complete",
  "timestamp": "{{newDate}}",
  "payload": {
    "gem_type": "ruby",
    "quantity": 2,
    "location_id": "mining_box"
  }
}
HTTP 201

# Player purchases upgrade
POST http://127.0.0.1:8000/box/session/{{session-id}}
{
  "type": "mining/upgrade_purchase",
  "timestamp": "{{newDate}}",
  "payload": {
    "upgrade_type": "capacity",
    "level": 2,
    "cost": {
      "ruby": 3,
      "sapphire": 1
    }
  }
}
HTTP 201

# Query player inventory - should aggregate all extracts
GET http://127.0.0.1:8000/game/mining/player/{{player-id}}/inventory
HTTP 200
[Asserts]
jsonpath "$.player_id" == "{{player-id}}"
jsonpath "$.gems.ruby" == 7
jsonpath "$.gems.sapphire" == 3
