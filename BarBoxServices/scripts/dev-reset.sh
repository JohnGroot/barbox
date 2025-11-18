#!/usr/bin/env bash
#
# dev-reset.sh - Reset development database and seed test data
#
# This script provides an explicit way to reset the development database
# to a clean state and seed it with deterministic test data.
#
# Usage:
#   sh scripts/dev-reset.sh
#
# What it does:
#   1. Drops all database tables
#   2. Recreates database schema
#   3. Seeds test data (box, players)
#
# Requirements:
#   - Backend must be running (sh scripts/dev.sh)
#   - Backend must be in development mode (ENV=local)
#

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Backend URL
BACKEND_URL="${BACKEND_URL:-http://127.0.0.1:8000}"

echo -e "${BLUE}━━━ BarBox Development Database Reset ━━━${NC}"
echo

# Check if backend is running
echo -e "${BLUE}Checking backend health...${NC}"
if ! curl -s -f "${BACKEND_URL}/alive" > /dev/null 2>&1; then
    echo -e "${RED}✗ Backend not available at ${BACKEND_URL}${NC}"
    echo -e "${YELLOW}Start the backend first: sh scripts/dev.sh${NC}"
    exit 1
fi
echo -e "${GREEN}✓ Backend is running${NC}"
echo

# Reset database
echo -e "${BLUE}Resetting database (dropping all tables)...${NC}"
RESET_RESPONSE=$(curl -s -X POST "${BACKEND_URL}/test/reset")
RESET_STATUS=$(echo "$RESET_RESPONSE" | grep -o '"status":"[^"]*"' | cut -d'"' -f4)

if [ "$RESET_STATUS" = "success" ]; then
    echo -e "${GREEN}✓ Database reset successfully${NC}"
else
    echo -e "${RED}✗ Database reset failed${NC}"
    echo "$RESET_RESPONSE"
    exit 1
fi
echo

# Seed test data
echo -e "${BLUE}Seeding test data...${NC}"
SEED_RESPONSE=$(curl -s -X POST "${BACKEND_URL}/test/seed")
SEED_STATUS=$(echo "$SEED_RESPONSE" | grep -o '"status":"[^"]*"' | cut -d'"' -f4)

if [ "$SEED_STATUS" = "success" ]; then
    echo -e "${GREEN}✓ Test data seeded successfully${NC}"
    echo
    echo -e "${BLUE}Created resources:${NC}"
    echo "$SEED_RESPONSE" | grep -o '"box_id":"[^"]*"' | cut -d'"' -f4 | xargs -I {} echo "  • Box: {}"
    echo "$SEED_RESPONSE" | grep -o '"player_ids":\[[^]]*\]' | sed 's/"player_ids":\[//;s/\]//;s/"//g' | tr ',' '\n' | xargs -I {} echo "  • Player: {}"
else
    echo -e "${RED}✗ Test data seed failed${NC}"
    echo "$SEED_RESPONSE"
    exit 1
fi
echo

echo -e "${GREEN}━━━ Development environment ready! ━━━${NC}"
echo
echo -e "${BLUE}Test credentials:${NC}"
echo "  • API Key: ndE63953HvBEqNP5XKPFe3vN4Ei9bDF-g9p13KoOmKs"
echo "  • Box ID:  00000000-0000-0000-0000-000000000001"
echo
echo -e "${BLUE}You can now:${NC}"
echo "  • Open Godot editor and test player registration"
echo "  • Run integration tests: sh scripts/test.sh"
echo "  • Access API docs: ${BACKEND_URL}/redoc"
