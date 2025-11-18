# Test: PUT /box/{box_id} - Idempotent Box Registration
#
# This tests the idempotent box registration endpoint that:
# 1. Creates a new box on first call (returns API key)
# 2. Returns existing box on subsequent calls (no API key)
# 3. Validates box_id in path matches request body

# Test 1: Register new box (first call) - should create and return API key
PUT http://127.0.0.1:8000/box/00000000-0000-0000-0000-000000000099
Content-Type: application/json
{
  "id": "00000000-0000-0000-0000-000000000099",
  "name": "Test Registration Box",
  "tag": "test-reg-box"
}
HTTP 200
[Asserts]
jsonpath "$.id" == "00000000-0000-0000-0000-000000000099"
jsonpath "$.name" == "Test Registration Box"
jsonpath "$.tag" == "test-reg-box"
jsonpath "$.api_key" exists
[Captures]
api-key: jsonpath "$.api_key"

# Test 2: Register same box again (idempotent) - should return existing box WITHOUT API key
PUT http://127.0.0.1:8000/box/00000000-0000-0000-0000-000000000099
Content-Type: application/json
{
  "id": "00000000-0000-0000-0000-000000000099",
  "name": "Test Registration Box",
  "tag": "test-reg-box"
}
HTTP 200
[Asserts]
jsonpath "$.id" == "00000000-0000-0000-0000-000000000099"
jsonpath "$.api_key" not exists

# Test 3: Mismatched box ID (path vs body) - should return 400
PUT http://127.0.0.1:8000/box/00000000-0000-0000-0000-000000000088
Content-Type: application/json
{
  "id": "00000000-0000-0000-0000-000000000099",
  "name": "Mismatched ID Box",
  "tag": "mismatch"
}
HTTP 400
[Asserts]
jsonpath "$.detail.code" == "VALIDATION_ERROR"
jsonpath "$.detail.message" contains "does not match"

# Test 4: Register another box to verify uniqueness
PUT http://127.0.0.1:8000/box/00000000-0000-0000-0000-000000000088
Content-Type: application/json
{
  "id": "00000000-0000-0000-0000-000000000088",
  "name": "Second Test Box",
  "tag": "second-box"
}
HTTP 200
[Asserts]
jsonpath "$.id" == "00000000-0000-0000-0000-000000000088"
jsonpath "$.api_key" exists
