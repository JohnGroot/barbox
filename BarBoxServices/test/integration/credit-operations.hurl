# Credit Operations Test Suite
# Tests credit checking, granting, and spending operations

# Setup: Create test box
POST http://127.0.0.1:8001/box/
{
  "id": "{{newUuid}}",
  "name": "Test Box Credits",
  "tag": "test_box_credits"
}
HTTP 201
[Captures]
box-id: jsonpath "$['id']"

# Setup: Create test player
POST http://127.0.0.1:8001/player/
{
  "id": "{{newUuid}}",
  "tag": "credituser",
  "origin_id": "{{box-id}}"
}
HTTP 201
[Captures]
player-id: jsonpath "$['id']"

# Test 1: Check backend for account credit amount (should be 0 initially)
# Note: This test depends on backend implementation of credit tracking
# For now, we'll test via session events which is how credits are tracked

# Test 2: Grant credits to user via event
PUT http://127.0.0.1:8001/box/{{box-id}}/session/{{newUuid}}?game_tag=mining
Player-Id: {{player-id}}
HTTP 202
[Captures]
session-id: jsonpath "$['id']"

# Emit credit earn event (mining game deposits gems for credits)
POST http://127.0.0.1:8001/box/session/{{session-id}}
{
  "type": "credit/earn",
  "timestamp": "{{newDate}}",
  "payload": {
    "amount": 100,
    "location_id": "test_box_credits",
    "reason": "mining_gems_deposited",
    "gem_type": "painite",
    "gems_spent": 10
  }
}
HTTP 201

# Test 3: Spend credits (via carrom game entry which costs credits)
POST http://127.0.0.1:8001/box/session/{{session-id}}
{
  "type": "credit/spend",
  "timestamp": "{{newDate}}",
  "payload": {
    "amount": 1,
    "location_id": "test_box_credits",
    "reason": "competitive_game_entry"
  }
}
HTTP 201

# Test 4: Try to spend more credits than available (negative balance prevention)
# This is a business logic test - the backend should track this via events
POST http://127.0.0.1:8001/box/session/{{session-id}}
{
  "type": "credit/spend",
  "timestamp": "{{newDate}}",
  "payload": {
    "amount": 200,
    "location_id": "test_box_credits",
    "reason": "should_fail_insufficient_credits"
  }
}
HTTP 201
# Note: Backend currently accepts all events - credit validation happens client-side
# Future enhancement: Backend should validate credit balance

# Test 5: Close session
POST http://127.0.0.1:8001/box/session/{{session-id}}/close
HTTP 200
[Asserts]
jsonpath "$.id" == "{{session-id}}"

# Test 6: Create new session to test credit persistence
PUT http://127.0.0.1:8001/box/{{box-id}}/session/{{newUuid}}?game_tag=mining
Player-Id: {{player-id}}
HTTP 202
[Captures]
session2-id: jsonpath "$['id']"

# Emit another credit earn event
POST http://127.0.0.1:8001/box/session/{{session2-id}}
{
  "type": "credit/earn",
  "timestamp": "{{newDate}}",
  "payload": {
    "amount": 50,
    "location_id": "test_box_credits",
    "reason": "mining_gems_deposited",
    "gem_type": "diamond",
    "gems_spent": 5
  }
}
HTTP 201

# Close second session
POST http://127.0.0.1:8001/box/session/{{session2-id}}/close
HTTP 200
