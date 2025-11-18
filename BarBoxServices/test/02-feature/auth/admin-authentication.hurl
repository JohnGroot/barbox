# Admin Authentication Integration Test
# Tests admin endpoint protection with localhost-only access
# - Verify cleanup endpoints are accessible from localhost (127.0.0.1)
# - Note: Non-localhost requests return 403 (tested manually - hurl runs from localhost)

# Test 1: Cleanup failed login attempts from localhost (should succeed)
# This tests that the endpoint works when called from localhost
POST http://127.0.0.1:8000/admin/cleanup-failed-login-attempts
HTTP 200
[Asserts]
jsonpath "$.status" == "success"
jsonpath "$.attempts_removed" exists
jsonpath "$.cleanup_time" exists
jsonpath "$.window_minutes" exists

# Test 2: Verify endpoint exists and is callable
# Second call to confirm idempotency
POST http://127.0.0.1:8000/admin/cleanup-failed-login-attempts
HTTP 200
[Asserts]
jsonpath "$.status" == "success"
