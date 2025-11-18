# Test username availability endpoint
# Validates that SessionManager.IsUsernameAvailableAsync integrates correctly with backend

# First, create a box to get API key
POST http://127.0.0.1:8000/box/
Content-Type: application/json
{
  "id": "{{newUuid}}",
  "name": "Username Test Box",
  "tag": "username_test_box"
}
HTTP 201
[Captures]
box-id: jsonpath "$['id']"
box-api-key: jsonpath "$['api_key']"

# Then create a test player with a known username
POST http://127.0.0.1:8000/player/
X-Box-API-Key: {{box-api-key}}
Content-Type: application/json
{
  "id": "{{newUuid}}",
  "tag": "test_user_taken",
  "phone_number": "+19175559999",
  "pin": "1234",
  "origin_id": "{{box-id}}"
}
HTTP 201
[Captures]
player-id: jsonpath "$['id']"

# Test that the existing username is NOT available
GET http://127.0.0.1:8000/player/username/test_user_taken/available
HTTP 200
[Asserts]
jsonpath "$.username" == "test_user_taken"
jsonpath "$.is_available" == false

# Test that a new username IS available
GET http://127.0.0.1:8000/player/username/new_unique_user/available
HTTP 200
[Asserts]
jsonpath "$.username" == "new_unique_user"
jsonpath "$.is_available" == true

# Test with URL-encoded username (spaces/special chars)
GET http://127.0.0.1:8000/player/username/user%20with%20spaces/available
HTTP 200
[Asserts]
jsonpath "$.username" == "user with spaces"
jsonpath "$.is_available" == true
