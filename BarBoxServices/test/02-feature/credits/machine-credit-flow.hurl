# Machine Credit Flow Test
# Tests the complete flow: player registration, credit earn, deposit to machine pot, consume from pot
# Tests per box+game credit pot isolation

# Step 1: Reset database for clean test
POST http://127.0.0.1:8000/test/reset
HTTP 200

# Step 2: Create box
POST http://127.0.0.1:8000/box/
Content-Type: application/json
{
  "id": "a206c3ee-733d-48a5-8906-a653f1b9f7ce",
  "name": "Test Box",
  "tag": "machine_credit_box"
}
HTTP 201
[Captures]
box-api-key: jsonpath "$['api_key']"

# Step 3: Register player 1
POST http://127.0.0.1:8000/player/
X-Box-API-Key: {{box-api-key}}
X-Box-ID: a206c3ee-733d-48a5-8906-a653f1b9f7ce
Content-Type: application/json
{
  "id": "06f25980-a5b0-2d84-f050-79903e7187ab",
  "tag": "player1",
  "phone_number": "+17185551234",
  "pin": "1234",
  "origin_id": "a206c3ee-733d-48a5-8906-a653f1b9f7ce"
}
HTTP 201

# Step 3.5: Login player 1 to get JWT token
POST http://127.0.0.1:8000/player/auth/login
X-Box-API-Key: {{box-api-key}}
X-Box-ID: a206c3ee-733d-48a5-8906-a653f1b9f7ce
Content-Type: application/json
{
  "phone_number": "+17185551234",
  "pin": "1234",
  "box_id": "a206c3ee-733d-48a5-8906-a653f1b9f7ce"
}
HTTP 200
[Captures]
access_token: jsonpath "$['access_token']"

# Step 4: Create lobby session for player 1
POST http://127.0.0.1:8000/box/a206c3ee-733d-48a5-8906-a653f1b9f7ce/lobby/session
X-Box-API-Key: {{box-api-key}}
Authorization: Bearer {{access_token}}
HTTP 201
[Captures]
lobby_session_1: jsonpath "$['id']"

# Step 5: Player 1 earns 25 credits
POST http://127.0.0.1:8000/box/session/{{lobby_session_1}}
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

# Step 6: Verify player 1 balance
GET http://127.0.0.1:8000/player/06f25980-a5b0-2d84-f050-79903e7187ab/credits?location_id=JohnGrootMachine
HTTP 200
[Asserts]
jsonpath "$.credits" == 25

# Step 7: Query machine credits (should be 0 initially)
GET http://127.0.0.1:8000/machine-credits/carrom?box_id=a206c3ee-733d-48a5-8906-a653f1b9f7ce
HTTP 200
[Asserts]
jsonpath "$.box_id" == "a206c3ee-733d-48a5-8906-a653f1b9f7ce"
jsonpath "$.game_tag" == "carrom"
jsonpath "$.balance" == 0
jsonpath "$.contributions" count == 0

# Step 8: Player 1 deposits 5 credits to machine pot
POST http://127.0.0.1:8000/machine-credits/carrom/deposit
X-Box-API-Key: {{box-api-key}}
X-Box-ID: a206c3ee-733d-48a5-8906-a653f1b9f7ce
Authorization: Bearer {{access_token}}
Content-Type: application/json
{
  "box_id": "a206c3ee-733d-48a5-8906-a653f1b9f7ce",
  "player_id": "06f25980-a5b0-2d84-f050-79903e7187ab",
  "amount": 5,
  "lobby_session_id": "{{lobby_session_1}}"
}
HTTP 201
[Asserts]
jsonpath "$.balance" == 5
jsonpath "$.contributions" count == 1
jsonpath "$.contributions[0].player_id" == "06f25980-a5b0-2d84-f050-79903e7187ab"
jsonpath "$.contributions[0].amount" == 5

# Step 9: Register player 2
POST http://127.0.0.1:8000/player/
X-Box-API-Key: {{box-api-key}}
X-Box-ID: a206c3ee-733d-48a5-8906-a653f1b9f7ce
Content-Type: application/json
{
  "id": "a72ed0d2-e24d-fac9-b1d8-02db969c18d4",
  "tag": "player2",
  "phone_number": "+13105551002",
  "pin": "1234",
  "origin_id": "a206c3ee-733d-48a5-8906-a653f1b9f7ce"
}
HTTP 201

# Step 9.5: Login player 2 to get JWT token
POST http://127.0.0.1:8000/player/auth/login
X-Box-API-Key: {{box-api-key}}
X-Box-ID: a206c3ee-733d-48a5-8906-a653f1b9f7ce
Content-Type: application/json
{
  "phone_number": "+13105551002",
  "pin": "1234",
  "box_id": "a206c3ee-733d-48a5-8906-a653f1b9f7ce"
}
HTTP 200
[Captures]
access_token_2: jsonpath "$['access_token']"

# Step 10: Create lobby session for player 2
POST http://127.0.0.1:8000/box/a206c3ee-733d-48a5-8906-a653f1b9f7ce/lobby/session
X-Box-API-Key: {{box-api-key}}
Authorization: Bearer {{access_token_2}}
HTTP 201
[Captures]
lobby_session_2: jsonpath "$['id']"

# Step 11: Player 2 earns 15 credits
POST http://127.0.0.1:8000/box/session/{{lobby_session_2}}
X-Box-API-Key: {{box-api-key}}
Content-Type: application/json
{
  "type": "credit/earn",
  "payload": {
    "location_id": "JohnGrootMachine",
    "amount": 15,
    "reason": "test purchase"
  }
}
HTTP 201

# Step 12: Player 2 deposits 3 credits to machine pot
POST http://127.0.0.1:8000/machine-credits/carrom/deposit
X-Box-API-Key: {{box-api-key}}
X-Box-ID: a206c3ee-733d-48a5-8906-a653f1b9f7ce
Authorization: Bearer {{access_token_2}}
Content-Type: application/json
{
  "box_id": "a206c3ee-733d-48a5-8906-a653f1b9f7ce",
  "player_id": "a72ed0d2-e24d-fac9-b1d8-02db969c18d4",
  "amount": 3,
  "lobby_session_id": "{{lobby_session_2}}"
}
HTTP 201
[Asserts]
jsonpath "$.balance" == 8
jsonpath "$.contributions" count == 2

# Step 13: Query machine credits (should show both contributions)
GET http://127.0.0.1:8000/machine-credits/carrom?box_id=a206c3ee-733d-48a5-8906-a653f1b9f7ce
HTTP 200
[Asserts]
jsonpath "$.balance" == 8
jsonpath "$.contributions" count == 2
jsonpath "$.contributions[*].player_id" contains "06f25980-a5b0-2d84-f050-79903e7187ab"
jsonpath "$.contributions[*].player_id" contains "a72ed0d2-e24d-fac9-b1d8-02db969c18d4"

# Step 14: Create game session for consuming credits
PUT http://127.0.0.1:8000/box/a206c3ee-733d-48a5-8906-a653f1b9f7ce/session/c1c1c1c1-c1c1-c1c1-c1c1-c1c1c1c1c1c1?game_tag=carrom
X-Box-API-Key: {{box-api-key}}
Player-Id: 06f25980-a5b0-2d84-f050-79903e7187ab
HTTP 202

# Step 15: Consume 8 credits from machine pot for game start
POST http://127.0.0.1:8000/machine-credits/carrom/consume
X-Box-API-Key: {{box-api-key}}
X-Box-ID: a206c3ee-733d-48a5-8906-a653f1b9f7ce
Content-Type: application/json
{
  "box_id": "a206c3ee-733d-48a5-8906-a653f1b9f7ce",
  "amount": 8,
  "game_session_id": "c1c1c1c1-c1c1-c1c1-c1c1-c1c1c1c1c1c1"
}
HTTP 200
[Asserts]
jsonpath "$.balance" == 0

# Step 16: Verify machine credits consumed
GET http://127.0.0.1:8000/machine-credits/carrom?box_id=a206c3ee-733d-48a5-8906-a653f1b9f7ce
HTTP 200
[Asserts]
jsonpath "$.balance" == 0

# Step 17: Test insufficient balance error
POST http://127.0.0.1:8000/machine-credits/carrom/consume
X-Box-API-Key: {{box-api-key}}
X-Box-ID: a206c3ee-733d-48a5-8906-a653f1b9f7ce
Content-Type: application/json
{
  "box_id": "a206c3ee-733d-48a5-8906-a653f1b9f7ce",
  "amount": 10,
  "game_session_id": "c1c1c1c1-c1c1-c1c1-c1c1-c1c1c1c1c1c1"
}
HTTP 400
[Asserts]
jsonpath "$.detail.code" == "INSUFFICIENT_CREDITS"
jsonpath "$.detail.message" contains "Insufficient machine credits"

# Step 18: Test box+game isolation - different game should have separate pot
GET http://127.0.0.1:8000/machine-credits/racing?box_id=a206c3ee-733d-48a5-8906-a653f1b9f7ce
HTTP 200
[Asserts]
jsonpath "$.balance" == 0
jsonpath "$.game_tag" == "racing"

# Step 19: Create second box to test box isolation
POST http://127.0.0.1:8000/box/
Content-Type: application/json
{
  "id": "b206c3ee-733d-48a5-8906-a653f1b9f7ce",
  "name": "Test Box 2",
  "tag": "test-box-2"
}
HTTP 201

# Step 20: Query carrom credits on box 2 (should be 0 - box isolated)
GET http://127.0.0.1:8000/machine-credits/carrom?box_id=b206c3ee-733d-48a5-8906-a653f1b9f7ce
HTTP 200
[Asserts]
jsonpath "$.balance" == 0
jsonpath "$.box_id" == "b206c3ee-733d-48a5-8906-a653f1b9f7ce"
