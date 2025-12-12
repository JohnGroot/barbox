# Test: Checkout Session Creation
# Purpose: Verify checkout session endpoint validation and authentication
# Note: Full Stripe integration testing requires Stripe CLI webhook forwarding

# ============= SETUP =============
# Reset database for clean state
POST http://127.0.0.1:8000/test/reset
HTTP 200

# Seed test data (box and players)
POST http://127.0.0.1:8000/test/seed
HTTP 200
[Captures]
box-id: jsonpath "$['data']['box_id']"
box-api-key: jsonpath "$['data']['box_api_key']"
test-player-id: jsonpath "$['data']['player_ids'][0]"

# Login to get player JWT
POST http://127.0.0.1:8000/player/auth/login
X-Box-ID: {{box-id}}
X-Box-API-Key: {{box-api-key}}
{
    "phone_number": "+12125551111",
    "pin": "1111",
    "box_id": "{{box-id}}"
}
HTTP 200
[Captures]
player-jwt: jsonpath "$['access_token']"
player-id: jsonpath "$['player_id']"

# ============= TESTS =============

# Test 1: Missing authentication - no box API key
POST http://127.0.0.1:8000/payments/checkout/create
Authorization: Bearer {{player-jwt}}
{
    "pack_id": "pack_25"
}
HTTP 401

# Test 2: Missing authentication - no player JWT
POST http://127.0.0.1:8000/payments/checkout/create
X-Box-ID: {{box-id}}
X-Box-API-Key: {{box-api-key}}
{
    "pack_id": "pack_25"
}
HTTP 401

# Test 3: Invalid pack_id - non-retryable validation error
POST http://127.0.0.1:8000/payments/checkout/create
X-Box-ID: {{box-id}}
X-Box-API-Key: {{box-api-key}}
Authorization: Bearer {{player-jwt}}
{
    "pack_id": "invalid_pack"
}
HTTP 400
[Asserts]
jsonpath "$.detail.code" == "VALIDATION_ERROR"
jsonpath "$.detail.message" contains "Invalid pack_id"
jsonpath "$.detail.retryable" == false

# ============= STRIPE INTEGRATION TEST (REQUIRES STRIPE KEYS) =============
# Test 4: Valid request - creates real Stripe checkout session
#
# PREREQUISITES:
# - STRIPE_SECRET_KEY must be set in environment
# - STRIPE_PRICE_25_CREDITS must be configured with valid Stripe Price ID
# - Stripe webhook forwarding not required for this test (session creation only)
#
# To enable this test:
# 1. Set STRIPE_SECRET_KEY=sk_test_... in environment
# 2. Create test Stripe Price with metadata: {"credits": "25000", "bonus_credits": "3000"}
# 3. Set STRIPE_PRICE_25_CREDITS to the Price ID
# 4. Uncomment the test below
#
# POST http://127.0.0.1:8000/payments/checkout/create
# X-Box-ID: {{box-id}}
# X-Box-API-Key: {{box-api-key}}
# Authorization: Bearer {{player-jwt}}
# {
#     "pack_id": "pack_25"
# }
# HTTP 201
# [Asserts]
# jsonpath "$.session_id" exists
# jsonpath "$.session_url" startsWith "https://checkout.stripe.com/"
