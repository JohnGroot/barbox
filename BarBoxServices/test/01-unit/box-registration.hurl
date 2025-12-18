# Test: PUT /box/{box_id} - Idempotent Box Registration
#
# This tests the idempotent box registration endpoint that:
# 1. Creates a new box on first call (returns API key + "Save" warning)
# 2. Returns same API key on subsequent calls (returns API key + "already registered" warning)
# 3. Warns if name/tag mismatch on re-registration (returns existing values)
# 4. Validates box_id in path matches request body

# Test 1: Register new box (first call) - should create and return API key with "Save" warning
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
jsonpath "$.api_key" != ""
jsonpath "$.warning" startsWith "Save"
[Captures]
api-key: jsonpath "$.api_key"

# Test 2: Register same box again (idempotent) - should return SAME API key with "already registered" warning
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
jsonpath "$.api_key" exists
jsonpath "$.api_key" != ""
jsonpath "$.api_key" == {{api-key}}
jsonpath "$.warning" == "Box already registered"

# Test 3: Re-register with different name/tag - should return EXISTING values with mismatch warning
PUT http://127.0.0.1:8000/box/00000000-0000-0000-0000-000000000099
Content-Type: application/json
{
  "id": "00000000-0000-0000-0000-000000000099",
  "name": "Different Name",
  "tag": "different-tag"
}
HTTP 200
[Asserts]
# Should return EXISTING values, not the new ones
jsonpath "$.id" == "00000000-0000-0000-0000-000000000099"
jsonpath "$.name" == "Test Registration Box"
jsonpath "$.tag" == "test-reg-box"
jsonpath "$.api_key" == {{api-key}}
# Warning should indicate mismatch
jsonpath "$.warning" contains "different"
jsonpath "$.warning" contains "Test Registration Box"

# Test 4: Mismatched box ID (path vs body) - should return 400
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

# Test 5: Register another box to verify uniqueness
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
jsonpath "$.api_key" != ""
# Different box = different API key
jsonpath "$.api_key" != {{api-key}}
jsonpath "$.warning" startsWith "Save"

# Test 6: Verify second box can also be re-registered with same API key
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
jsonpath "$.warning" == "Box already registered"
