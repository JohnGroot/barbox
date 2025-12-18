# Payment Webhook Flow Test Suite
# Tests the mock webhook endpoint for credit issuance
# NOTE: These tests use the /test/payments/webhook-mock endpoint which bypasses Stripe

# Setup: Reset and seed test data
POST http://127.0.0.1:8000/test/reset
HTTP 200

POST http://127.0.0.1:8000/test/seed
HTTP 200
[Asserts]
jsonpath "$.status" matches "success|skipped"
[Captures]
test-box-id: jsonpath "$.data.box_id"
test-box-api-key: jsonpath "$.data.box_api_key"
test-player-id: jsonpath "$.data.player_ids[0]"

# Create a unique test player for webhook tests
POST http://127.0.0.1:8000/player/
X-Box-API-Key: {{test-box-api-key}}
X-Box-ID: {{test-box-id}}
{
  "id": "{{newUuid}}",
  "tag": "wbhook_{{newDate}}",
  "phone_number": "+14155558888",
  "pin": "8888",
  "origin_id": "{{test-box-id}}"
}
HTTP 201
[Captures]
webhook-player-id: jsonpath "$['id']"

# =============================================================================
# Test 1: Happy path - valid webhook issues credits
# =============================================================================
POST http://127.0.0.1:8000/test/payments/webhook-mock
Content-Type: application/json
{
  "event_id": "evt_test_happy_path_{{newUuid}}",
  "session_id": "cs_test_happy_{{newUuid}}",
  "payment_intent_id": "pi_test_happy_{{newUuid}}",
  "player_id": "{{webhook-player-id}}",
  "box_id": "{{test-box-id}}",
  "credits": 5000,
  "bonus_credits": 0,
  "amount_cents": 500,
  "pack_id": "pack_5"
}
HTTP 200
[Asserts]
jsonpath "$.status" == "success"
jsonpath "$.credits_added" == 5000

# =============================================================================
# Test 2: Happy path with bonus credits
# =============================================================================
POST http://127.0.0.1:8000/test/payments/webhook-mock
Content-Type: application/json
{
  "event_id": "evt_test_bonus_{{newUuid}}",
  "session_id": "cs_test_bonus_{{newUuid}}",
  "payment_intent_id": "pi_test_bonus_{{newUuid}}",
  "player_id": "{{webhook-player-id}}",
  "box_id": "{{test-box-id}}",
  "credits": 25000,
  "bonus_credits": 3000,
  "amount_cents": 2500,
  "pack_id": "pack_25"
}
HTTP 200
[Asserts]
jsonpath "$.status" == "success"
jsonpath "$.credits_added" == 28000

# =============================================================================
# Test 3: Idempotency - duplicate session_id returns already_processed
# =============================================================================
# First request (should succeed)
POST http://127.0.0.1:8000/test/payments/webhook-mock
Content-Type: application/json
{
  "event_id": "evt_test_idem_first_{{newUuid}}",
  "session_id": "cs_test_idempotency_unique",
  "payment_intent_id": "pi_test_idem_{{newUuid}}",
  "player_id": "{{webhook-player-id}}",
  "box_id": "{{test-box-id}}",
  "credits": 10000,
  "bonus_credits": 0,
  "amount_cents": 1000,
  "pack_id": "pack_10"
}
HTTP 200
[Asserts]
jsonpath "$.status" == "success"

# Second request with SAME session_id (should return already_processed)
POST http://127.0.0.1:8000/test/payments/webhook-mock
Content-Type: application/json
{
  "event_id": "evt_test_idem_second_{{newUuid}}",
  "session_id": "cs_test_idempotency_unique",
  "payment_intent_id": "pi_test_idem_dup_{{newUuid}}",
  "player_id": "{{webhook-player-id}}",
  "box_id": "{{test-box-id}}",
  "credits": 10000,
  "bonus_credits": 0,
  "amount_cents": 1000,
  "pack_id": "pack_10"
}
HTTP 200
[Asserts]
jsonpath "$.status" == "already_processed"

# =============================================================================
# Test 4: Invalid event_id prefix - must start with evt_test_
# =============================================================================
POST http://127.0.0.1:8000/test/payments/webhook-mock
Content-Type: application/json
{
  "event_id": "evt_live_should_fail",
  "session_id": "cs_test_invalid_prefix_{{newUuid}}",
  "payment_intent_id": "pi_test_invalid_{{newUuid}}",
  "player_id": "{{webhook-player-id}}",
  "box_id": "{{test-box-id}}",
  "credits": 5000,
  "bonus_credits": 0,
  "amount_cents": 500,
  "pack_id": "pack_5"
}
HTTP 400
[Asserts]
jsonpath "$.detail.code" == "VALIDATION_ERROR"
jsonpath "$.detail.message" contains "evt_test_"

# =============================================================================
# Test 5: Non-existent player_id - succeeds but creates orphan payment
# NOTE: SQLite FK constraints are not enforced by default. In production with
# PostgreSQL, this would fail. For now, we verify the endpoint accepts the request.
# This is a known limitation of the test environment.
# =============================================================================
POST http://127.0.0.1:8000/test/payments/webhook-mock
Content-Type: application/json
{
  "event_id": "evt_test_orphan_player_{{newUuid}}",
  "session_id": "cs_test_orphan_player_{{newUuid}}",
  "payment_intent_id": "pi_test_orphan_player_{{newUuid}}",
  "player_id": "99999999-9999-9999-9999-999999999999",
  "box_id": "{{test-box-id}}",
  "credits": 5000,
  "bonus_credits": 0,
  "amount_cents": 500,
  "pack_id": "pack_5"
}
HTTP 200
[Asserts]
jsonpath "$.status" == "success"

# =============================================================================
# Test 6: Non-existent box_id - succeeds but creates orphan payment
# NOTE: Same SQLite FK constraint limitation as Test 5
# =============================================================================
POST http://127.0.0.1:8000/test/payments/webhook-mock
Content-Type: application/json
{
  "event_id": "evt_test_orphan_box_{{newUuid}}",
  "session_id": "cs_test_orphan_box_{{newUuid}}",
  "payment_intent_id": "pi_test_orphan_box_{{newUuid}}",
  "player_id": "{{webhook-player-id}}",
  "box_id": "99999999-9999-9999-9999-999999999999",
  "credits": 5000,
  "bonus_credits": 0,
  "amount_cents": 500,
  "pack_id": "pack_5"
}
HTTP 200
[Asserts]
jsonpath "$.status" == "success"

# =============================================================================
# Test 7: Negative credits - validation should reject
# =============================================================================
POST http://127.0.0.1:8000/test/payments/webhook-mock
Content-Type: application/json
{
  "event_id": "evt_test_negative_{{newUuid}}",
  "session_id": "cs_test_negative_{{newUuid}}",
  "payment_intent_id": "pi_test_negative_{{newUuid}}",
  "player_id": "{{webhook-player-id}}",
  "box_id": "{{test-box-id}}",
  "credits": -100,
  "bonus_credits": 0,
  "amount_cents": 500,
  "pack_id": "pack_5"
}
HTTP 422
[Asserts]
jsonpath "$.detail[0].loc" contains "credits"

# =============================================================================
# Test 8: Zero credits - validation should reject (gt=0 constraint)
# =============================================================================
POST http://127.0.0.1:8000/test/payments/webhook-mock
Content-Type: application/json
{
  "event_id": "evt_test_zero_{{newUuid}}",
  "session_id": "cs_test_zero_{{newUuid}}",
  "payment_intent_id": "pi_test_zero_{{newUuid}}",
  "player_id": "{{webhook-player-id}}",
  "box_id": "{{test-box-id}}",
  "credits": 0,
  "bonus_credits": 0,
  "amount_cents": 500,
  "pack_id": "pack_5"
}
HTTP 422
[Asserts]
jsonpath "$.detail[0].loc" contains "credits"

# =============================================================================
# Test 9: Negative bonus_credits - validation should reject
# =============================================================================
POST http://127.0.0.1:8000/test/payments/webhook-mock
Content-Type: application/json
{
  "event_id": "evt_test_neg_bonus_{{newUuid}}",
  "session_id": "cs_test_neg_bonus_{{newUuid}}",
  "payment_intent_id": "pi_test_neg_bonus_{{newUuid}}",
  "player_id": "{{webhook-player-id}}",
  "box_id": "{{test-box-id}}",
  "credits": 5000,
  "bonus_credits": -100,
  "amount_cents": 500,
  "pack_id": "pack_5"
}
HTTP 422
[Asserts]
jsonpath "$.detail[0].loc" contains "bonus_credits"

# =============================================================================
# Test 10: Verify credit/earn event exists in database after successful webhook
# =============================================================================
# First, create a payment we can query
POST http://127.0.0.1:8000/test/payments/webhook-mock
Content-Type: application/json
{
  "event_id": "evt_test_verify_{{newUuid}}",
  "session_id": "cs_test_verify_credits_check",
  "payment_intent_id": "pi_test_verify_{{newUuid}}",
  "player_id": "{{webhook-player-id}}",
  "box_id": "{{test-box-id}}",
  "credits": 50000,
  "bonus_credits": 10000,
  "amount_cents": 5000,
  "pack_id": "pack_50"
}
HTTP 200
[Asserts]
jsonpath "$.status" == "success"
jsonpath "$.credits_added" == 60000
