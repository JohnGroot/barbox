# Authentication Flow Integration Test
# Tests the complete authentication system including:
# - Box creation with API key generation
# - Player registration with Box authentication
# - Player login with PIN verification
# - Player logout (client-side only - token remains valid until expiry)
# - Endpoint protection verification

# Setup: Reset database for clean test
POST http://127.0.0.1:8000/test/reset
HTTP 200

# Test 1: Create a box and receive API key
POST http://127.0.0.1:8000/box/
X-Registration-Secret: dev-registration-secret-UNSAFE-change-in-production
Content-Type: application/json
{
  "id": "00000000-0000-0000-0000-000000000099",
  "name": "Test Auth Box",
  "tag": "test-auth-box"
}
HTTP 201
[Asserts]
jsonpath "$.id" == "00000000-0000-0000-0000-000000000099"
jsonpath "$.name" == "Test Auth Box"
jsonpath "$.tag" == "test-auth-box"
jsonpath "$.api_key" exists
jsonpath "$.warning" exists
[Captures]
box_id: jsonpath "$.id"
box_api_key: jsonpath "$.api_key"

# Test 2: Verify we can't create a duplicate box
POST http://127.0.0.1:8000/box/
X-Registration-Secret: dev-registration-secret-UNSAFE-change-in-production
Content-Type: application/json
{
  "id": "00000000-0000-0000-0000-000000000099",
  "name": "Duplicate Box",
  "tag": "duplicate-box"
}
HTTP 409

# Test 3: Register a player WITH Box authentication (should succeed)
# Note: Player ID must match hash of phone number "5551234567" for login to work
POST http://127.0.0.1:8000/player/
Content-Type: application/json
X-Box-API-Key: {{box_api_key}}
X-Box-ID: {{box_id}}
{
  "id": "3c95277d-a5fd-0da6-a1a4-4ee3fdf56d20",
  "tag": "testplayer",
  "phone_number": "+12125551234",
  "origin_id": "{{box_id}}",
  "pin": "1234"
}
HTTP 201
[Asserts]
jsonpath "$.id" == "3c95277d-a5fd-0da6-a1a4-4ee3fdf56d20"
jsonpath "$.tag" == "testplayer"
[Captures]
player_id: jsonpath "$.id"

# Test 4: Try to register player WITHOUT Box authentication (should fail)
POST http://127.0.0.1:8000/player/
Content-Type: application/json
{
  "id": "00000000-0000-0000-0000-000000000005",
  "tag": "unauthorized-player",
  "phone_number": "+15551234568",
  "origin_id": "{{box_id}}",
  "pin": "5678"
}
HTTP 401

# Test 5: Try to register player with WRONG Box API key (should fail)
POST http://127.0.0.1:8000/player/
Content-Type: application/json
X-Box-API-Key: wrong-api-key
X-Box-ID: {{box_id}}
{
  "id": "00000000-0000-0000-0000-000000000006",
  "tag": "wrong-key-player",
  "phone_number": "+15551234569",
  "origin_id": "{{box_id}}",
  "pin": "9999"
}
HTTP 401

# Test 6: Login with correct credentials (should succeed)
POST http://127.0.0.1:8000/player/auth/login
Content-Type: application/json
X-Box-API-Key: {{box_api_key}}
X-Box-ID: {{box_id}}
{
  "phone_number": "+12125551234",
  "pin": "1234",
  "box_id": "{{box_id}}"
}
HTTP 200
[Asserts]
jsonpath "$.access_token" exists
jsonpath "$.player_id" == "{{player_id}}"
jsonpath "$.expires_at" exists
[Captures]
jwt_token: jsonpath "$.access_token"

# Test 7: Login with WRONG PIN (should fail)
POST http://127.0.0.1:8000/player/auth/login
Content-Type: application/json
X-Box-API-Key: {{box_api_key}}
X-Box-ID: {{box_id}}
{
  "phone_number": "+12125551234",
  "pin": "0000",
  "box_id": "{{box_id}}"
}
HTTP 401

# Test 8: Login WITHOUT Box authentication (should fail)
POST http://127.0.0.1:8000/player/auth/login
Content-Type: application/json
{
  "phone_number": "+12125551234",
  "pin": "1234",
  "box_id": "{{box_id}}"
}
HTTP 401

# Test 9: Create lobby session WITH dual authentication (should succeed)
POST http://127.0.0.1:8000/box/{{box_id}}/lobby/session
Content-Type: application/json
X-Box-API-Key: {{box_api_key}}
Authorization: Bearer {{jwt_token}}
HTTP 201
[Asserts]
jsonpath "$.id" exists
jsonpath "$.game_tag" == "lobby"
[Captures]
lobby_session_id: jsonpath "$.id"

# Test 10: Try to create lobby session WITHOUT Box auth (should fail)
POST http://127.0.0.1:8000/box/{{box_id}}/lobby/session
Content-Type: application/json
Authorization: Bearer {{jwt_token}}
HTTP 401

# Test 11: Try to create lobby session WITHOUT Player auth (should fail)
POST http://127.0.0.1:8000/box/{{box_id}}/lobby/session
Content-Type: application/json
X-Box-API-Key: {{box_api_key}}
HTTP 401

# Test 12: Logout player (should succeed)
POST http://127.0.0.1:8000/player/auth/logout
Content-Type: application/json
Authorization: Bearer {{jwt_token}}
HTTP 200
[Asserts]
jsonpath "$.message" exists
jsonpath "$.player_id" == "{{player_id}}"

# Test 13: Use token after logout (client-side logout means token remains valid)
# With client-side logout, tokens are valid until natural expiry (2 hours) or idle timeout (10 min)
POST http://127.0.0.1:8000/box/{{box_id}}/lobby/session
Content-Type: application/json
X-Box-API-Key: {{box_api_key}}
Authorization: Bearer {{jwt_token}}
HTTP 201
[Asserts]
jsonpath "$.id" exists
jsonpath "$.game_tag" == "lobby"

# Test 14: Login again to get fresh token
POST http://127.0.0.1:8000/player/auth/login
Content-Type: application/json
X-Box-API-Key: {{box_api_key}}
X-Box-ID: {{box_id}}
{
  "phone_number": "+12125551234",
  "pin": "1234",
  "box_id": "{{box_id}}"
}
HTTP 200
[Captures]
fresh_token: jsonpath "$.access_token"

# Test 15: Create game session WITH Box auth (should succeed)
PUT http://127.0.0.1:8000/box/{{box_id}}/session/00000000-0000-0000-0000-000000000111?game_tag=racing
Content-Type: application/json
X-Box-API-Key: {{box_api_key}}
Player-Id: {{player_id}}
HTTP 202
[Asserts]
jsonpath "$.id" == "00000000-0000-0000-0000-000000000111"

# Test 16: Try to create game session WITHOUT Box auth (should fail)
PUT http://127.0.0.1:8000/box/{{box_id}}/session/00000000-0000-0000-0000-000000000222?game_tag=racing
Content-Type: application/json
Player-Id: {{player_id}}
HTTP 401

# Test 17: Add event to session WITH Box auth (should succeed)
POST http://127.0.0.1:8000/box/session/00000000-0000-0000-0000-000000000111
Content-Type: application/json
X-Box-API-Key: {{box_api_key}}
{
  "type": "play/begin",
  "payload": {"game": "racing"}
}
HTTP 201
[Asserts]
jsonpath "$.id" exists

# Test 18: Try to add event WITHOUT Box auth (should fail)
POST http://127.0.0.1:8000/box/session/00000000-0000-0000-0000-000000000111
Content-Type: application/json
{
  "type": "play/score",
  "payload": {"points": 100}
}
HTTP 401

# Test 19: Close session WITH Box auth (should succeed)
POST http://127.0.0.1:8000/box/session/00000000-0000-0000-0000-000000000111/close
Content-Type: application/json
X-Box-API-Key: {{box_api_key}}
HTTP 200
[Asserts]
jsonpath "$.id" == "00000000-0000-0000-0000-000000000111"

# Test 20: Try to close session WITHOUT Box auth (should fail)
POST http://127.0.0.1:8000/box/session/00000000-0000-0000-0000-000000000111/close
Content-Type: application/json
HTTP 401
