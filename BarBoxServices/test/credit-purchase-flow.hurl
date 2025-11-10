# Credit Purchase Flow Test
# Tests the complete flow: player registration, session creation, credit events, balance queries
# This test reproduces the bug where credit balance returns 0 even after credit/earn events

# Step 1: Create box
POST http://127.0.0.1:8000/box/
Content-Type: application/json
{
  "id": "a206c3ee-733d-48a5-8906-a653f1b9f7ce",
  "name": "Test Box",
  "tag": "test-box"
}
HTTP 201

# Step 2: Register player via first_play (tests PlayerDetail fix)
POST http://127.0.0.1:8000/player/first_play
Content-Type: application/json
{
  "id": "06f25980-a5b0-2d84-f050-79903e7187ab",
  "tag": "testuser",
  "origin_id": "a206c3ee-733d-48a5-8906-a653f1b9f7ce"
}
HTTP 201
[Asserts]
jsonpath "$.id" == "06f25980-a5b0-2d84-f050-79903e7187ab"
jsonpath "$.tag" == "testuser"
jsonpath "$.origin_id" == "a206c3ee-733d-48a5-8906-a653f1b9f7ce"

# Step 3: Create lobby session
POST http://127.0.0.1:8000/box/a206c3ee-733d-48a5-8906-a653f1b9f7ce/lobby/session
Player-Id: 06f25980-a5b0-2d84-f050-79903e7187ab
HTTP 201
[Captures]
session_id: jsonpath "$['id']"

# Step 4: Submit credit/earn event (exactly as C# does)
POST http://127.0.0.1:8000/box/session/{{session_id}}
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
GET http://127.0.0.1:8000/player/06f25980-a5b0-2d84-f050-79903e7187ab/credits?location_id=JohnGrootMachine
HTTP 200
[Asserts]
jsonpath "$.player_id" == "06f25980-a5b0-2d84-f050-79903e7187ab"
jsonpath "$.location_id" == "JohnGrootMachine"
jsonpath "$.credits" == 25  # THIS WILL FAIL with current code (returns 0)

# Step 6: Add second credit event and verify cumulative balance
POST http://127.0.0.1:8000/box/session/{{session_id}}
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

GET http://127.0.0.1:8000/player/06f25980-a5b0-2d84-f050-79903e7187ab/credits?location_id=JohnGrootMachine
HTTP 200
[Asserts]
jsonpath "$.credits" == 35  # Should be 25 + 10

# Step 7: Test credit/spend
POST http://127.0.0.1:8000/box/session/{{session_id}}
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

GET http://127.0.0.1:8000/player/06f25980-a5b0-2d84-f050-79903e7187ab/credits?location_id=JohnGrootMachine
HTTP 200
[Asserts]
jsonpath "$.credits" == 30  # Should be 35 - 5

# Step 8: Test with different location_id (should return 0)
GET http://127.0.0.1:8000/player/06f25980-a5b0-2d84-f050-79903e7187ab/credits?location_id=DifferentLocation
HTTP 200
[Asserts]
jsonpath "$.credits" == 0  # Different location should have 0 credits
