# Test: Payment Reconciliation Admin Endpoint
# Purpose: Verify reconciliation endpoint works and returns proper structure
# Note: Admin endpoints are localhost-only

# ============= SETUP =============
# Reset database for clean state
POST http://127.0.0.1:8000/test/reset
HTTP 200

# ============= TESTS =============

# Test 1: Reconciliation report - empty state
GET http://127.0.0.1:8000/admin/payments/reconciliation
HTTP 200
[Asserts]
jsonpath "$.payments_without_credits" isCollection
jsonpath "$.orphan_credit_events" isCollection
jsonpath "$.total_missing_credits" == 0
jsonpath "$.report_generated_at" exists

# Test 2: Retry credit endpoint - payment not found
POST http://127.0.0.1:8000/admin/payments/00000000-0000-0000-0000-000000000000/retry-credit
HTTP 200
[Asserts]
jsonpath "$.success" == false
jsonpath "$.error" contains "not found"
