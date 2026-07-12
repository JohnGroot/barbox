# Test: Mining Location Registration
# Tests the /game/mining/location/register endpoint

# =============================================================================
# TEST 1: Register a new location with unique venue name
# =============================================================================
POST http://127.0.0.1:8000/game/mining/location/register
[QueryStringParams]
venue_name: test_venue_registration_1
HTTP 200
[Captures]
venue_name: jsonpath "$.venue_name"
gem_type: jsonpath "$.gem_type"
[Asserts]
jsonpath "$.venue_name" == "test_venue_registration_1"
jsonpath "$.gem_type" exists
jsonpath "$.display_name" exists
# Gem type must be one of the valid types
jsonpath "$.gem_type" matches "^(ruby|sapphire|emerald|diamond|amethyst)$"

# =============================================================================
# TEST 2: Idempotent registration - same venue returns same gem type
# =============================================================================
POST http://127.0.0.1:8000/game/mining/location/register
[QueryStringParams]
venue_name: idempotent_test_venue
HTTP 200
[Captures]
first_gem_type: jsonpath "$.gem_type"

# Register same venue again - should return same result
POST http://127.0.0.1:8000/game/mining/location/register
[QueryStringParams]
venue_name: idempotent_test_venue
HTTP 200
[Asserts]
jsonpath "$.gem_type" == {{first_gem_type}}
jsonpath "$.venue_name" == "idempotent_test_venue"

# =============================================================================
# TEST 3: Display name generation from venue_name (underscores to spaces, title case)
# =============================================================================
POST http://127.0.0.1:8000/game/mining/location/register
[QueryStringParams]
venue_name: best_intentions_bar
HTTP 200
[Asserts]
jsonpath "$.display_name" == "Best Intentions Bar"
jsonpath "$.venue_name" == "best_intentions_bar"

# =============================================================================
# TEST 4: Register another unique venue to test balancing
# =============================================================================
POST http://127.0.0.1:8000/game/mining/location/register
[QueryStringParams]
venue_name: test_venue_registration_2
HTTP 200
[Asserts]
jsonpath "$.venue_name" == "test_venue_registration_2"
jsonpath "$.gem_type" matches "^(ruby|sapphire|emerald|diamond|amethyst)$"
