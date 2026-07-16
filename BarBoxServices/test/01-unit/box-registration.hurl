# Test: PUT /box/{box_id} - Idempotent Box Registration
#
# This tests the idempotent box registration endpoint that:
# 1. Creates a new box on first call (returns API key + "Save" warning)
# 2. Returns same API key on subsequent calls (returns API key + "already registered" warning)
# 3. Warns if name/tag mismatch on re-registration (returns existing values)
# 4. Validates box_id in path matches request body

# Test 1: Register new box (first call) - should create and return API key with "Save" warning
# Requires X-Registration-Secret since box_id doesn't exist yet
PUT http://127.0.0.1:8000/box/00000000-0000-0000-0000-000000000099
Content-Type: application/json
X-Registration-Secret: dev-registration-secret-UNSAFE-change-in-production
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
# No X-Registration-Secret header - proves the recovery path stays unauthenticated
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
# Requires X-Registration-Secret since this box_id doesn't exist yet either
PUT http://127.0.0.1:8000/box/00000000-0000-0000-0000-000000000088
Content-Type: application/json
X-Registration-Secret: dev-registration-secret-UNSAFE-change-in-production
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
# No X-Registration-Secret header - recovery path stays unauthenticated
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

# Test 7: Register a brand-new box_id with no X-Registration-Secret - should be rejected
PUT http://127.0.0.1:8000/box/00000000-0000-0000-0000-000000000077
Content-Type: application/json
{
  "id": "00000000-0000-0000-0000-000000000077",
  "name": "Unauthorized Box",
  "tag": "unauthorized-box"
}
HTTP 401
[Asserts]
jsonpath "$.detail.code" == "UNAUTHORIZED"
jsonpath "$.detail.message" contains "X-Registration-Secret"

# Test 8: Same new box_id with a wrong secret - should also be rejected
PUT http://127.0.0.1:8000/box/00000000-0000-0000-0000-000000000077
Content-Type: application/json
X-Registration-Secret: wrong-secret
{
  "id": "00000000-0000-0000-0000-000000000077",
  "name": "Unauthorized Box",
  "tag": "unauthorized-box"
}
HTTP 401
[Asserts]
jsonpath "$.detail.code" == "UNAUTHORIZED"
jsonpath "$.detail.message" contains "Invalid registration secret"

# Test 9: Same new box_id with the correct secret - should succeed
PUT http://127.0.0.1:8000/box/00000000-0000-0000-0000-000000000077
Content-Type: application/json
X-Registration-Secret: dev-registration-secret-UNSAFE-change-in-production
{
  "id": "00000000-0000-0000-0000-000000000077",
  "name": "Unauthorized Box",
  "tag": "unauthorized-box"
}
HTTP 200
[Asserts]
jsonpath "$.id" == "00000000-0000-0000-0000-000000000077"
jsonpath "$.api_key" exists
jsonpath "$.warning" startsWith "Save"
