# Account Creation Flow Integration Tests
# Tests the full account creation workflow including validation and error handling

# Test 1: Get environment info (should be local/test)
GET http://127.0.0.1:8000/test/environment
HTTP 200
[Asserts]
jsonpath "$.is_production" == false

# Test 2: Reset database to clean state
POST http://127.0.0.1:8000/test/reset
HTTP 200
[Asserts]
jsonpath "$.status" == "success"

# Test 3: Seed test data (creates test box for player creation)
# Note: After reset, auto-seeding occurs, so seed returns "skipped" status
POST http://127.0.0.1:8000/test/seed
HTTP 200
[Asserts]
jsonpath "$.status" matches "success|skipped"
[Captures]
test-box-id: jsonpath "$.data.box_id"
test-box-api-key: jsonpath "$.data.box_api_key"

# Test 4: Validate player creation with valid data
POST http://127.0.0.1:8000/player/validate
X-Box-API-Key: {{test-box-api-key}}
Content-Type: application/json
{
  "id": "33333333-3333-3333-3333-333333333333",
  "tag": "validuser",
  "phone_number": "+12025551001",
  "pin": "1234",
  "origin_id": "{{test-box-id}}"
}
HTTP 200
[Asserts]
jsonpath "$.valid" == true
jsonpath "$.errors" count == 0

# Test 5: Validate player creation with non-existent box (should fail)
POST http://127.0.0.1:8000/player/validate
X-Box-API-Key: {{test-box-api-key}}
Content-Type: application/json
{
  "id": "44444444-4444-4444-4444-444444444444",
  "tag": "orphanuser",
  "phone_number": "+13105551002",
  "pin": "1234",
  "origin_id": "99999999-9999-9999-9999-999999999999"
}
HTTP 200
[Asserts]
jsonpath "$.valid" == false
jsonpath "$.errors" count > 0
jsonpath "$.errors[0].field" == "origin_id"

# Test 6: Create player successfully
POST http://127.0.0.1:8000/player/
X-Box-API-Key: {{test-box-api-key}}
Content-Type: application/json
{
  "id": "33333333-3333-3333-3333-333333333333",
  "tag": "validuser",
  "phone_number": "+12025551001",
  "pin": "1234",
  "origin_id": "{{test-box-id}}"
}
HTTP 201
[Asserts]
jsonpath "$.id" == "33333333-3333-3333-3333-333333333333"

# Test 7: Validate duplicate username (should fail)
POST http://127.0.0.1:8000/player/validate
X-Box-API-Key: {{test-box-api-key}}
Content-Type: application/json
{
  "id": "55555555-5555-5555-5555-555555555555",
  "tag": "validuser",
  "phone_number": "+14155551003",
  "pin": "1234",
  "origin_id": "{{test-box-id}}"
}
HTTP 200
[Asserts]
jsonpath "$.valid" == false
jsonpath "$.errors" count > 0
jsonpath "$.errors[0].field" == "tag"

# Test 8: Validate duplicate player ID (should fail)
POST http://127.0.0.1:8000/player/validate
X-Box-API-Key: {{test-box-api-key}}
Content-Type: application/json
{
  "id": "33333333-3333-3333-3333-333333333333",
  "tag": "differentuser",
  "phone_number": "+16175551004",
  "pin": "1234",
  "origin_id": "{{test-box-id}}"
}
HTTP 200
[Asserts]
jsonpath "$.valid" == false
jsonpath "$.errors" count > 0
jsonpath "$.errors[0].field" == "id"

# Test 9: Attempt to create duplicate player (should return 409)
POST http://127.0.0.1:8000/player/
X-Box-API-Key: {{test-box-api-key}}
Content-Type: application/json
{
  "id": "33333333-3333-3333-3333-333333333333",
  "tag": "validuser",
  "phone_number": "+12025551001",
  "pin": "1234",
  "origin_id": "{{test-box-id}}"
}
HTTP 409
[Asserts]
jsonpath "$.detail.code" == "DUPLICATE_RESOURCE"

# Test 10: Attempt to create player with mismatched origin box (should return 403)
POST http://127.0.0.1:8000/player/
X-Box-API-Key: {{test-box-api-key}}
Content-Type: application/json
{
  "id": "66666666-6666-6666-6666-666666666666",
  "tag": "orphanuser",
  "phone_number": "+13035551005",
  "pin": "1234",
  "origin_id": "99999999-9999-9999-9999-999999999999"
}
HTTP 403
[Asserts]
jsonpath "$.detail.code" == "VALIDATION_ERROR"

# Test 11: Check username availability for existing user
GET http://127.0.0.1:8000/player/username/validuser/available
HTTP 200
[Asserts]
jsonpath "$.username" == "validuser"
jsonpath "$.is_available" == false

# Test 12: Check username availability for new user
GET http://127.0.0.1:8000/player/username/newuser/available
HTTP 200
[Asserts]
jsonpath "$.username" == "newuser"
jsonpath "$.is_available" == true

# Test 13: Create another valid player
POST http://127.0.0.1:8000/player/
X-Box-API-Key: {{test-box-api-key}}
Content-Type: application/json
{
  "id": "77777777-7777-7777-7777-777777777777",
  "tag": "newuser",
  "phone_number": "+14045551006",
  "pin": "1234",
  "origin_id": "{{test-box-id}}"
}
HTTP 201
[Asserts]
jsonpath "$.id" == "77777777-7777-7777-7777-777777777777"

# Test 14: Verify the new username is now unavailable
GET http://127.0.0.1:8000/player/username/newuser/available
HTTP 200
[Asserts]
jsonpath "$.username" == "newuser"
jsonpath "$.is_available" == false
