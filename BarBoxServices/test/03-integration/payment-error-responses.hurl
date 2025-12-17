# Payment Error Responses Test Suite
# Tests error handling in payment endpoints
# NOTE: These tests only cover error paths that don't require actual Stripe calls

# Setup: Seed test data to get known box/player credentials
POST http://127.0.0.1:8000/test/seed
HTTP 200
[Asserts]
jsonpath "$.status" matches "success|skipped"
[Captures]
test-box-id: jsonpath "$.data.box_id"
test-box-api-key: jsonpath "$.data.box_api_key"

# Create a unique test player for payment tests
POST http://127.0.0.1:8000/player/
X-Box-API-Key: {{test-box-api-key}}
X-Box-ID: {{test-box-id}}
{
  "id": "{{newUuid}}",
  "tag": "paytest_{{newDate}}",
  "phone_number": "+14155559999",
  "pin": "9999",
  "origin_id": "{{test-box-id}}"
}
HTTP 201
[Captures]
player-id: jsonpath "$['id']"

# Login to get JWT token
POST http://127.0.0.1:8000/player/auth/login
X-Box-API-Key: {{test-box-api-key}}
X-Box-ID: {{test-box-id}}
{
  "phone_number": "+14155559999",
  "pin": "9999",
  "box_id": "{{test-box-id}}"
}
HTTP 200
[Captures]
auth-token: jsonpath "$['access_token']"

# Test 1: Invalid pack_id returns 400 with proper error structure
POST http://127.0.0.1:8000/payments/checkout/create
Authorization: Bearer {{auth-token}}
X-Box-API-Key: {{test-box-api-key}}
X-Box-ID: {{test-box-id}}
{
  "pack_id": "invalid_pack"
}
HTTP 400
[Asserts]
jsonpath "$.detail.code" == "VALIDATION_ERROR"
jsonpath "$.detail.retryable" == false
jsonpath "$.detail.message" contains "Invalid pack_id"

# Test 2: Checkout status for non-existent session returns pending (not 404)
GET http://127.0.0.1:8000/payments/checkout/cs_nonexistent_session_id/status
HTTP 200
[Asserts]
jsonpath "$.status" == "pending"
jsonpath "$.session_id" == "cs_nonexistent_session_id"

# Test 3: Missing player authentication returns 401
POST http://127.0.0.1:8000/payments/checkout/create
X-Box-API-Key: {{test-box-api-key}}
X-Box-ID: {{test-box-id}}
{
  "pack_id": "pack_5"
}
HTTP 401

# Test 4: Missing box auth returns 401
POST http://127.0.0.1:8000/payments/checkout/create
Authorization: Bearer {{auth-token}}
{
  "pack_id": "pack_5"
}
HTTP 401
