# Credit Purchase Flow Test
# Tests the complete flow: player registration, session creation, credit events, balance queries
# This test reproduces the bug where credit balance returns 0 even after credit/earn events

# Step 1: Create box
POST http://127.0.0.1:8000/box/
Content-Type: application/json
{
  "id": "{{newUuid}}",
  "name": "Test Box",
  "tag": "credit_purchase_box"
}
HTTP 201
[Captures]
box-id: jsonpath "$['id']"
box-api-key: jsonpath "$['api_key']"

# Step 2: Register player (tests PlayerDetail fix)
POST http://127.0.0.1:8000/player/
X-Box-API-Key: {{box-api-key}}
X-Box-ID: {{box-id}}
Content-Type: application/json
{
  "id": "{{newUuid}}",
  "tag": "testuser",
  "phone_number": "+16465551234",
  "pin": "1234",
  "origin_id": "{{box-id}}"
}
HTTP 201
[Captures]
player-id: jsonpath "$['id']"
[Asserts]
jsonpath "$.tag" == "testuser"
jsonpath "$.origin_id" == "{{box-id}}"

# Step 3: Login player to get JWT token
POST http://127.0.0.1:8000/player/auth/login
X-Box-API-Key: {{box-api-key}}
X-Box-ID: {{box-id}}
Content-Type: application/json
{
  "phone_number": "+16465551234",
  "pin": "1234",
  "box_id": "{{box-id}}"
}
HTTP 200
[Captures]
access_token: jsonpath "$['access_token']"

# Step 4: Create lobby session
POST http://127.0.0.1:8000/box/{{box-id}}/lobby/session
X-Box-API-Key: {{box-api-key}}
Authorization: Bearer {{access_token}}
HTTP 201
[Captures]
session_id: jsonpath "$['id']"

# Step 4: Submit credit/earn event (exactly as C# does)
POST http://127.0.0.1:8000/box/session/{{session_id}}
X-Box-API-Key: {{box-api-key}}
Content-Type: application/json
{
  "type": "credit/earn",
  "payload": {
    "location_id": "JohnGrootMachine",
    "amount": 25,
    "reason": "test purchase"
  }
}
HTTP 201

# Step 5: IMMEDIATELY query balance (reproduces race condition / type mismatch bug)
GET http://127.0.0.1:8000/player/{{player-id}}/credits?location_id=JohnGrootMachine
HTTP 200
[Asserts]
jsonpath "$.player_id" == "{{player-id}}"
jsonpath "$.location_id" == "JohnGrootMachine"
jsonpath "$.credits" == 25  # THIS WILL FAIL with current code (returns 0)

# Step 6: Add second credit event and verify cumulative balance
POST http://127.0.0.1:8000/box/session/{{session_id}}
X-Box-API-Key: {{box-api-key}}
Content-Type: application/json
{
  "type": "credit/earn",
  "payload": {
    "location_id": "JohnGrootMachine",
    "amount": 10,
    "reason": "bonus"
  }
}
HTTP 201

GET http://127.0.0.1:8000/player/{{player-id}}/credits?location_id=JohnGrootMachine
HTTP 200
[Asserts]
jsonpath "$.credits" == 35  # Should be 25 + 10

# Step 7: Test credit/spend
POST http://127.0.0.1:8000/box/session/{{session_id}}
X-Box-API-Key: {{box-api-key}}
Content-Type: application/json
{
  "type": "credit/spend",
  "payload": {
    "location_id": "JohnGrootMachine",
    "amount": 5,
    "reason": "game play"
  }
}
HTTP 201

GET http://127.0.0.1:8000/player/{{player-id}}/credits?location_id=JohnGrootMachine
HTTP 200
[Asserts]
jsonpath "$.credits" == 30  # Should be 35 - 5

# Step 8: Test with different location_id (should return 0)
GET http://127.0.0.1:8000/player/{{player-id}}/credits?location_id=DifferentLocation
HTTP 200
[Asserts]
jsonpath "$.credits" == 0  # Different location should have 0 credits
