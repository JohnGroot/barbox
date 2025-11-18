#!/usr/bin/env bash
set -euo pipefail

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

echo -e "${BLUE}Running isolated HTTP integration tests...${NC}"
echo ""

# Check if backend is running
if ! curl -s http://127.0.0.1:8000/alive > /dev/null 2>&1; then
    echo -e "${RED}ERROR: Backend server is not running${NC}"
    echo "Start it with: sh scripts/dev.sh"
    exit 1
fi

# Check if we're in test mode (required for /test/reset endpoint)
ENV_CHECK=$(curl -s http://127.0.0.1:8000/test/environment 2>&1 || echo "")
if [[ ! "$ENV_CHECK" =~ "local"|"test" ]]; then
    echo -e "${RED}ERROR: Backend must be running in local or test mode${NC}"
    echo "Set ENV=local or ENV=test before starting backend"
    exit 1
fi

# Get all test files (sorted alphabetically, excluding fixtures)
TEST_FILES=($(find test -name "*.hurl" -not -path "*/fixtures/*" | sort))

echo -e "${GREEN}Found ${#TEST_FILES[@]} test files${NC}"
echo ""

FAILED_TESTS=()
PASSED_TESTS=()
START_TIME=$(date +%s)

for test_file in "${TEST_FILES[@]}"; do
    echo -e "${YELLOW}==========================================${NC}"
    echo -e "${YELLOW}TEST: $test_file${NC}"
    echo -e "${YELLOW}==========================================${NC}"

    # Reset database before each test file
    echo "Resetting database..."
    RESET_RESPONSE=$(curl -s -X POST http://127.0.0.1:8000/test/reset 2>&1 || echo '{"status":"error"}')

    if ! echo "$RESET_RESPONSE" | grep -q '"status":"success"'; then
        echo -e "${RED}ERROR: Failed to reset database${NC}"
        echo "Response: $RESET_RESPONSE"
        exit 1
    fi

    echo -e "${GREEN}✓ Database reset complete${NC}"

    # Seed test data after reset
    echo "Seeding test data..."
    "$(dirname "$0")/seed-test-data.sh" > /dev/null 2>&1 || {
        echo -e "${YELLOW}⚠ Test data seeding failed (non-critical)${NC}"
    }
    echo -e "${GREEN}✓ Test data seeded${NC}"

    # Run the test
    echo "Running test..."
    if hurl --error-format long "$test_file" 2>&1; then
        PASSED_TESTS+=("$test_file")
        echo -e "${GREEN}✓ PASSED${NC}"
    else
        FAILED_TESTS+=("$test_file")
        echo -e "${RED}✗ FAILED${NC}"
    fi

    echo ""
done

END_TIME=$(date +%s)
DURATION=$((END_TIME - START_TIME))

echo -e "${YELLOW}==========================================${NC}"
echo -e "${YELLOW}TEST SUMMARY${NC}"
echo -e "${YELLOW}==========================================${NC}"
echo -e "${GREEN}PASSED: ${#PASSED_TESTS[@]}/${#TEST_FILES[@]}${NC}"

if [ ${#FAILED_TESTS[@]} -gt 0 ]; then
    echo -e "${RED}FAILED: ${#FAILED_TESTS[@]}/${#TEST_FILES[@]}${NC}"
fi

echo "DURATION: ${DURATION}s"
echo ""

if [ ${#FAILED_TESTS[@]} -gt 0 ]; then
    echo -e "${RED}Failed tests:${NC}"
    for test in "${FAILED_TESTS[@]}"; do
        echo -e "  ${RED}✗${NC} $test"
    done
    echo ""
    exit 1
fi

echo -e "${GREEN}All tests passed!${NC}"
exit 0
