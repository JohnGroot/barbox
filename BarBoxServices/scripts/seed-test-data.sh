#!/bin/bash
set -e

# Seed Test Data Script
# This script populates the test database with fixed test entities for integration tests

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

echo "=================================================="
echo " BarBox Test Data Seeder"
echo "=================================================="
echo ""

# Check if backend is running
if ! curl -s http://127.0.0.1:8000/alive > /dev/null 2>&1; then
	echo "❌ Backend is not running. Please start it first with:"
	echo "   cd $PROJECT_ROOT && ./scripts/dev.sh"
	exit 1
fi

echo "✓ Backend is running"
echo ""

# Load test constants
export PYTHONPATH="$PROJECT_ROOT/test:$PYTHONPATH"

# Seed boxes
echo "Seeding test boxes..."
curl -s -X PUT "http://127.0.0.1:8000/box/00000000-0000-0000-0000-000000000001" \
	-H "Content-Type: application/json" \
	-d '{
		"id": "00000000-0000-0000-0000-000000000001",
		"name": "Test Box 1",
		"tag": "test_box_1"
	}' > /dev/null || echo "  Box 1 already exists"

curl -s -X PUT "http://127.0.0.1:8000/box/00000000-0000-0000-0000-000000000002" \
	-H "Content-Type: application/json" \
	-d '{
		"id": "00000000-0000-0000-0000-000000000002",
		"name": "Test Box 2",
		"tag": "test_box_2"
	}' > /dev/null || echo "  Box 2 already exists"

curl -s -X PUT "http://127.0.0.1:8000/box/00000000-0000-0000-0000-000000000003" \
	-H "Content-Type: application/json" \
	-d '{
		"id": "00000000-0000-0000-0000-000000000003",
		"name": "Test Box 3",
		"tag": "test_box_3"
	}' > /dev/null || echo "  Box 3 already exists"

echo "✓ Test boxes seeded"
echo ""

# Seed players
echo "Seeding test players..."
curl -s -X POST "http://127.0.0.1:8000/player/" \
	-H "Content-Type: application/json" \
	-d '{
		"id": "10000000-0000-0000-0000-000000000001",
		"tag": "test1",
		"origin_id": "00000000-0000-0000-0000-000000000001",
		"pin": "1111",
		"phone_number": "5555550001"
	}' > /dev/null 2>&1 || echo "  Player 1 already exists"

curl -s -X POST "http://127.0.0.1:8000/player/" \
	-H "Content-Type: application/json" \
	-d '{
		"id": "10000000-0000-0000-0000-000000000002",
		"tag": "test2",
		"origin_id": "00000000-0000-0000-0000-000000000002",
		"pin": "2222",
		"phone_number": "5555550002"
	}' > /dev/null 2>&1 || echo "  Player 2 already exists"

curl -s -X POST "http://127.0.0.1:8000/player/" \
	-H "Content-Type: application/json" \
	-d '{
		"id": "10000000-0000-0000-0000-000000000003",
		"tag": "test3",
		"origin_id": "00000000-0000-0000-0000-000000000003",
		"pin": "3333",
		"phone_number": "5555550003"
	}' > /dev/null 2>&1 || echo "  Player 3 already exists"

echo "✓ Test players seeded"
echo ""

# Give each test player some initial credits
echo "Seeding test player credits..."
for player_id in "10000000-0000-0000-0000-000000000001" "10000000-0000-0000-0000-000000000002" "10000000-0000-0000-0000-000000000003"; do
	curl -s -X POST "http://127.0.0.1:8000/test/grant-credits" \
		-H "Content-Type: application/json" \
		-d "{
			\"player_id\": \"$player_id\",
			\"location_id\": \"test_location_1\",
			\"amount\": 100
		}" > /dev/null 2>&1 || true
done

echo "✓ Test player credits seeded"
echo ""

echo "=================================================="
echo " Test Data Seeding Complete"
echo "=================================================="
echo ""
echo "Test entities created:"
echo "  - 3 test boxes (IDs: 00000000-0000-0000-0000-000000000001-003)"
echo "  - 3 test players (IDs: 10000000-0000-0000-0000-000000000001-003)"
echo "  - 100 credits per player at test_location_1"
echo ""
echo "You can now run integration tests with:"
echo "  cd $PROJECT_ROOT && ./scripts/test.sh"
echo ""
