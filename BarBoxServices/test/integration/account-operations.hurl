# Account Operations Test Suite
# Tests account creation, login validation, and error handling

# Setup: Create test box
POST http://127.0.0.1:8001/box/
{
  "id": "{{newUuid}}",
  "name": "Test Box",
  "tag": "test_box_accounts"
}
HTTP 201
[Captures]
box-id: jsonpath "$['id']"

# Test 1: Create new account successfully
POST http://127.0.0.1:8001/player/
{
  "id": "{{newUuid}}",
  "tag": "newuser",
  "origin_id": "{{box-id}}"
}
HTTP 201
[Captures]
player-id: jsonpath "$['id']"
[Asserts]
jsonpath "$.tag" == "newuser"
jsonpath "$.origin_id" == "{{box-id}}"

# Test 2: Try to create account that already exists (should fail with 409)
POST http://127.0.0.1:8001/player/
{
  "id": "{{newUuid}}",
  "tag": "newuser",
  "origin_id": "{{box-id}}"
}
HTTP 409

# Test 3: Login to account (validate player exists)
POST http://127.0.0.1:8001/player/validate
{
  "id": "{{newUuid}}",
  "tag": "newuser",
  "origin_id": "{{box-id}}"
}
HTTP 200
[Asserts]
jsonpath "$.valid" == false
jsonpath "$.errors[0].field" == "tag"

# Test 4: Try to login with wrong username (should fail)
POST http://127.0.0.1:8001/player/validate
{
  "id": "{{newUuid}}",
  "tag": "nonexistentuser",
  "origin_id": "{{box-id}}"
}
HTTP 200
[Asserts]
jsonpath "$.valid" == true

# Test 5: Check username availability (existing user)
GET http://127.0.0.1:8001/player/username/newuser/available
HTTP 200
[Asserts]
jsonpath "$.is_available" == false
jsonpath "$.username" == "newuser"

# Test 6: Check username availability (non-existing user)
GET http://127.0.0.1:8001/player/username/availableuser123/available
HTTP 200
[Asserts]
jsonpath "$.is_available" == true
jsonpath "$.username" == "availableuser123"

# Test 7: Try to create account with non-existent box (should fail)
POST http://127.0.0.1:8001/player/validate
{
  "id": "{{newUuid}}",
  "tag": "testuser2",
  "origin_id": "00000000-0000-0000-0000-000000000000"
}
HTTP 200
[Asserts]
jsonpath "$.valid" == false
jsonpath "$.errors[0].field" == "origin_id"

# Test 8: Create multiple accounts successfully
POST http://127.0.0.1:8001/player/
{
  "id": "{{newUuid}}",
  "tag": "user2",
  "origin_id": "{{box-id}}"
}
HTTP 201
[Captures]
player2-id: jsonpath "$['id']"

POST http://127.0.0.1:8001/player/
{
  "id": "{{newUuid}}",
  "tag": "user3",
  "origin_id": "{{box-id}}"
}
HTTP 201
[Captures]
player3-id: jsonpath "$['id']"
