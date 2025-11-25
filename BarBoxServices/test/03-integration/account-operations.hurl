# Account Operations Test Suite
# Tests account creation, login validation, and error handling

# Setup: Reset database for clean test
POST http://127.0.0.1:8000/test/reset
HTTP 200

# Setup: Create test box
POST http://127.0.0.1:8000/box/
{
  "id": "{{newUuid}}",
  "name": "Test Box",
  "tag": "test_box_accounts"
}
HTTP 201
[Captures]
box-id: jsonpath "$['id']"
box-api-key: jsonpath "$['api_key']"

# Test 1: Create new account successfully
POST http://127.0.0.1:8000/player/
X-Box-API-Key: {{box-api-key}}
X-Box-ID: {{box-id}}
{
  "id": "{{newUuid}}",
  "tag": "newuser",
  "phone_number": "+14155550201",
  "pin": "1234",
  "origin_id": "{{box-id}}"
}
HTTP 201
[Captures]
player-id: jsonpath "$['id']"
[Asserts]
jsonpath "$.tag" == "newuser"
jsonpath "$.origin_id" == "{{box-id}}"

# Test 2: Try to create account that already exists (should fail with 409)
POST http://127.0.0.1:8000/player/
X-Box-API-Key: {{box-api-key}}
X-Box-ID: {{box-id}}
{
  "id": "{{newUuid}}",
  "tag": "newuser",
  "phone_number": "+14155550202",
  "pin": "1234",
  "origin_id": "{{box-id}}"
}
HTTP 409

# Test 3: Validate existing player (should fail with duplicate username error)
POST http://127.0.0.1:8000/player/validate
X-Box-API-Key: {{box-api-key}}
X-Box-ID: {{box-id}}
{
  "id": "{{newUuid}}",
  "tag": "newuser",
  "phone_number": "+14155550299",
  "pin": "1234",
  "origin_id": "{{box-id}}"
}
HTTP 200
[Asserts]
jsonpath "$.valid" == false
jsonpath "$.errors[0].field" == "tag"

# Test 4: Try to validate with non-existent username (should pass validation)
POST http://127.0.0.1:8000/player/validate
X-Box-API-Key: {{box-api-key}}
X-Box-ID: {{box-id}}
{
  "id": "{{newUuid}}",
  "tag": "nonexistentuser",
  "phone_number": "+14155550298",
  "pin": "1234",
  "origin_id": "{{box-id}}"
}
HTTP 200
[Asserts]
jsonpath "$.valid" == true

# Test 5: Check username availability (existing user)
GET http://127.0.0.1:8000/player/username/newuser/available
HTTP 200
[Asserts]
jsonpath "$.is_available" == false
jsonpath "$.username" == "newuser"

# Test 6: Check username availability (non-existing user)
GET http://127.0.0.1:8000/player/username/availableuser123/available
HTTP 200
[Asserts]
jsonpath "$.is_available" == true
jsonpath "$.username" == "availableuser123"

# Test 7: Try to validate account with non-existent box (should fail)
POST http://127.0.0.1:8000/player/validate
X-Box-API-Key: {{box-api-key}}
X-Box-ID: {{box-id}}
{
  "id": "{{newUuid}}",
  "tag": "testuser2",
  "phone_number": "+14155550297",
  "pin": "1234",
  "origin_id": "00000000-0000-0000-0000-000000000000"
}
HTTP 200
[Asserts]
jsonpath "$.valid" == false
jsonpath "$.errors[0].field" == "origin_id"

# Test 8: Create multiple accounts successfully
POST http://127.0.0.1:8000/player/
X-Box-API-Key: {{box-api-key}}
X-Box-ID: {{box-id}}
{
  "id": "{{newUuid}}",
  "tag": "user2",
  "phone_number": "+14155550203",
  "pin": "1234",
  "origin_id": "{{box-id}}"
}
HTTP 201
[Captures]
player2-id: jsonpath "$['id']"

POST http://127.0.0.1:8000/player/
X-Box-API-Key: {{box-api-key}}
X-Box-ID: {{box-id}}
{
  "id": "{{newUuid}}",
  "tag": "user3",
  "phone_number": "+14155550204",
  "pin": "1234",
  "origin_id": "{{box-id}}"
}
HTTP 201
[Captures]
player3-id: jsonpath "$['id']"
