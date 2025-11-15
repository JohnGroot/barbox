# Test credits management endpoints
# Validates that credit operations integrate correctly with backend event sourcing

# Setup: Create test box, player, and session
POST http://127.0.0.1:8000/box/
Content-Type: application/json
{
  "id": "{{newUuid}}",
  "name": "Test Box",
  "tag": "testbox"
}
HTTP 201
[Captures]
box-id: jsonpath "$['id']"

POST http://127.0.0.1:8000/player/
Content-Type: application/json
{
  "id": "{{newUuid}}",
  "tag": "credit_test_user",
  "origin_id": "{{box-id}}"
}
HTTP 201
[Captures]
player-id: jsonpath "$['id']"

PUT http://127.0.0.1:8000/box/{{box-id}}/session/{{newUuid}}
Player-Id: {{player-id}}
HTTP 202
[Captures]
session-id: jsonpath "$['id']"

# Test 1: Query credits when balance is zero
GET http://127.0.0.1:8000/player/{{player-id}}/credits?location_id=test_location
HTTP 200
[Asserts]
jsonpath "$.player_id" == "{{player-id}}"
jsonpath "$.location_id" == "test_location"
jsonpath "$.credits" == 0

# Test 2: Add credits via event emission
POST http://127.0.0.1:8000/box/session/{{session-id}}
Content-Type: application/json
{
  "type": "credit/earn",
  "payload": {
    "player_id": "{{player-id}}",
    "location_id": "test_location",
    "amount": 50,
    "reason": "test_deposit"
  }
}
HTTP 201

# Test 3: Verify credits increased
GET http://127.0.0.1:8000/player/{{player-id}}/credits?location_id=test_location
HTTP 200
[Asserts]
jsonpath "$.credits" == 50

# Test 4: Spend some credits
POST http://127.0.0.1:8000/box/session/{{session-id}}
Content-Type: application/json
{
  "type": "credit/spend",
  "payload": {
    "player_id": "{{player-id}}",
    "location_id": "test_location",
    "amount": 20,
    "reason": "game_purchase"
  }
}
HTTP 201

# Test 5: Verify credits decreased
GET http://127.0.0.1:8000/player/{{player-id}}/credits?location_id=test_location
HTTP 200
[Asserts]
jsonpath "$.credits" == 30

# Test 6: Add more credits
POST http://127.0.0.1:8000/box/session/{{session-id}}
Content-Type: application/json
{
  "type": "credit/earn",
  "payload": {
    "player_id": "{{player-id}}",
    "location_id": "test_location",
    "amount": 100,
    "reason": "test_bonus"
  }
}
HTTP 201

# Test 7: Verify final balance
GET http://127.0.0.1:8000/player/{{player-id}}/credits?location_id=test_location
HTTP 200
[Asserts]
jsonpath "$.credits" == 130

# Test 8: Verify location isolation (different location should have 0 credits)
GET http://127.0.0.1:8000/player/{{player-id}}/credits?location_id=different_location
HTTP 200
[Asserts]
jsonpath "$.location_id" == "different_location"
jsonpath "$.credits" == 0
