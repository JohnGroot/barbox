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
box-api-key: jsonpath "$['api_key']"

# Setup: Create player
POST http://127.0.0.1:8000/player/
X-Box-API-Key: {{box-api-key}}
X-Box-ID: {{box-id}}
{
  "id": "{{newUuid}}",
  "tag": "miner1",
  "phone_number": "+14045556001",
  "pin": "1234",
  "origin_id": "{{box-id}}"
}
HTTP 201
[Captures]
player-id: jsonpath "$['id']"

# Player starts mining session
PUT http://127.0.0.1:8000/box/{{box-id}}/session/{{newUuid}}?game_tag=mining
X-Box-API-Key: {{box-api-key}}
Player-Id: {{player-id}}
HTTP 202
[Captures]
session-id: jsonpath "$['id']"

# Player extracts ruby
POST http://127.0.0.1:8000/box/session/{{session-id}}
X-Box-API-Key: {{box-api-key}}
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
X-Box-API-Key: {{box-api-key}}
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
X-Box-API-Key: {{box-api-key}}
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
X-Box-API-Key: {{box-api-key}}
{
  "type": "mining/upgrade_purchase",
  "timestamp": "{{newDate}}",
  "payload": {
    "upgrade_type": "capacity",
    "level": 2,
    "cost": {
      "ruby": 3,
      "sapphire": 1
    },
    "location_id": "mining_box"
  }
}
HTTP 201

# Query player state - should aggregate all extracts minus upgrade costs
# Math: Ruby (5 + 2 - 3 upgrade cost = 4), Sapphire (3 - 1 upgrade cost = 2)
GET http://127.0.0.1:8000/game/mining/player/{{player-id}}/state?location_id=mining_box
HTTP 200
[Asserts]
jsonpath "$.player_id" == "{{player-id}}"
jsonpath "$.location_id" == "mining_box"
jsonpath "$.inventory.ruby" == 4
jsonpath "$.inventory.sapphire" == 2
jsonpath "$.upgrades.capacity" == 2
jsonpath "$.metadata.has_received_bonus_at_location" exists
jsonpath "$.last_extraction_time" exists
