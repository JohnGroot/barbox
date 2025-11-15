# Test username availability endpoint
# Validates that SessionManager.IsUsernameAvailableAsync integrates correctly with backend

# First, create a test player with a known username
POST http://127.0.0.1:8000/player/
Content-Type: application/json
{
  "id": "{{newUuid}}",
  "tag": "test_user_taken",
  "origin_id": "{{newUuid}}"
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
