#!/usr/bin/env bash
# BarBox Comprehensive Test Suite Runner
# Runs backend Hurl tests and Godot C# tests with GoDotTest

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

print_header() { echo -e "${CYAN}━━━ $1 ━━━${NC}"; }
print_info() { echo -e "${BLUE}ℹ${NC} $1"; }
print_success() { echo -e "${GREEN}✓${NC} $1"; }
print_error() { echo -e "${RED}✗${NC} $1"; }
print_warning() { echo -e "${YELLOW}⚠${NC} $1"; }

# Get script directory
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
APP_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
SERVICES_ROOT="$(cd "$APP_ROOT/../BarBoxServices" && pwd)"

# Check if we should run only specific tests
TEST_MODE="${1:-all}" # all, backend, frontend, quick

echo ""
print_header "BarBox Comprehensive Test Suite"
echo ""

# Phase 1: Start test backend
print_header "Phase 1: Starting Test Backend"
echo ""

cd "$SERVICES_ROOT"
if ! sh scripts/test-backend.sh start; then
	print_error "Failed to start test backend"
	exit 1
fi

echo ""

# CRITICAL: Wait for backend to be FULLY ready before starting Godot
print_info "Waiting for test backend health check..."
MAX_WAIT_SECONDS=30
WAIT_COUNT=0

while [ $WAIT_COUNT -lt $MAX_WAIT_SECONDS ]; do
	if curl -sf --max-time 2 "http://127.0.0.1:8001/alive" > /dev/null 2>&1; then
		print_success "Test backend is healthy and ready"
		break
	fi

	if [ $WAIT_COUNT -eq $((MAX_WAIT_SECONDS - 1)) ]; then
		print_error "Test backend failed to become healthy within ${MAX_WAIT_SECONDS} seconds"
		print_info "Last 20 lines of backend log:"
		sh scripts/test-backend.sh logs
		sh scripts/test-backend.sh stop
		exit 1
	fi

	WAIT_COUNT=$((WAIT_COUNT + 1))
	sleep 1
done

echo ""

# Phase 1.5: Seed test database with known API key
print_header "Phase 1.5: Seeding Test Database"
echo ""

# Reset database to ensure clean state
print_info "Resetting test database to clean state..."
RESET_RESPONSE=$(curl -sf -X POST "http://127.0.0.1:8001/test/reset" 2>&1)
if [ $? -eq 0 ]; then
	print_success "Test database reset successfully"
else
	print_error "Failed to reset test database"
	print_info "Response: $RESET_RESPONSE"
	sh scripts/test-backend.sh stop
	exit 1
fi

print_info "Seeding test database with deterministic data..."
SEED_RESPONSE=$(curl -sf -X POST "http://127.0.0.1:8001/test/seed" 2>&1)
if [ $? -eq 0 ]; then
	print_success "Test database seeded successfully"

	# Extract API key and Box ID from response using Python
	export BARBOX_API_KEY=$(echo "$SEED_RESPONSE" | python3 -c "import sys, json; print(json.load(sys.stdin)['data']['box_api_key'])" 2>/dev/null)
	export BARBOX_BOX_ID=$(echo "$SEED_RESPONSE" | python3 -c "import sys, json; print(json.load(sys.stdin)['data']['box_id'])" 2>/dev/null)

	if [ -n "$BARBOX_API_KEY" ] && [ -n "$BARBOX_BOX_ID" ]; then
		print_success "Captured API key: ${BARBOX_API_KEY:0:8}..."
		print_success "Captured Box ID: $BARBOX_BOX_ID"
	else
		print_warning "Failed to extract API key from seed response"
		print_info "Response: $SEED_RESPONSE"
	fi
else
	print_error "Failed to seed test database"
	print_info "Response: $SEED_RESPONSE"
	sh scripts/test-backend.sh stop
	exit 1
fi

echo ""

# Phase 2: Run backend Hurl tests (if not frontend-only)
if [ "$TEST_MODE" != "frontend" ]; then
	print_header "Phase 2: Running Backend Integration Tests (Hurl)"
	echo ""

	cd "$SERVICES_ROOT"
	if [ -d "test/integration" ]; then
		# Check if hurl is installed
		if command -v hurl &> /dev/null; then
			print_info "Running Hurl integration tests..."
			if hurl --error-format long test/integration/*.hurl; then
				print_success "Backend integration tests passed"
			else
				print_error "Backend integration tests failed"
				sh scripts/test-backend.sh stop
				exit 1
			fi
		else
			print_warning "Hurl not installed, skipping backend integration tests"
			print_info "Install with: brew install hurl (macOS) or visit https://hurl.dev"
		fi
	else
		print_warning "No backend integration tests found"
	fi

	echo ""
fi

# Phase 3: Build C# project (if not backend-only)
if [ "$TEST_MODE" != "backend" ]; then
	print_header "Phase 3: Building C# Project"
	echo ""

	cd "$APP_ROOT"
	print_info "Running dotnet build..."
	if dotnet build; then
		print_success "Build successful"
	else
		print_error "Build failed"
		cd "$SERVICES_ROOT"
		sh scripts/test-backend.sh stop
		exit 1
	fi

	echo ""
fi

# Phase 4: Run Godot C# tests with GoDotTest (if not backend-only)
if [ "$TEST_MODE" != "backend" ]; then
	print_header "Phase 4: Running Godot C# Tests (GoDotTest)"
	echo ""

	cd "$APP_ROOT"

	# Resolve Godot binary: env override > PATH > GodotEnv (macOS) > GodotEnv (Linux)
	GODOT_CMD=""
	if [[ -n "$GODOT_BIN" ]]; then
		GODOT_CMD="$GODOT_BIN"
	elif command -v godot &> /dev/null; then
		GODOT_CMD="$(command -v godot)"
	elif [[ -x "$HOME/Library/Application Support/godotenv/godot/bin/godot" ]]; then
		GODOT_CMD="$HOME/Library/Application Support/godotenv/godot/bin/godot"
	elif [[ -x "$HOME/.config/godotenv/godot/bin/godot" ]]; then
		GODOT_CMD="$HOME/.config/godotenv/godot/bin/godot"
	else
		print_error "Godot not found. Install via GodotEnv: godotenv godot install 4.6.0"
		print_info "Or set GODOT_BIN environment variable"
		cd "$SERVICES_ROOT"
		sh scripts/test-backend.sh stop
		exit 1
	fi
	print_info "Using Godot from: $GODOT_CMD"

	print_info "Running GoDotTest via Godot headless mode..."

	# Set environment variables for test backend
	# CRITICAL: Set BARBOX_BACKEND_URL to override .env.local which may point to staging
	export BARBOX_BACKEND_URL=http://127.0.0.1:8001
	export BARBOX_TEST_MODE=1
	# BARBOX_API_KEY and BARBOX_BOX_ID already exported from seed step

	# Debug: Verify environment variables are set
	print_info "Environment variables for Godot:"
	print_info "  BARBOX_API_KEY=${BARBOX_API_KEY:0:8}..."
	print_info "  BARBOX_BOX_ID=$BARBOX_BOX_ID"
	print_info "  BARBOX_BACKEND_URL=$BARBOX_BACKEND_URL"

	# Run tests with godot
	if "$GODOT_CMD" --path . --headless --run-tests --quit-on-finish; then
		print_success "Godot C# tests passed"
	else
		print_error "Godot C# tests failed"
		cd "$SERVICES_ROOT"
		sh scripts/test-backend.sh stop
		exit 1
	fi

	echo ""
fi

# Phase 5: Cleanup
print_header "Phase 5: Cleanup"
echo ""

cd "$SERVICES_ROOT"
sh scripts/test-backend.sh stop

echo ""
print_header "All Tests Complete"
print_success "Test suite finished successfully!"
echo ""

# Show summary
echo "Test Summary:"
if [ "$TEST_MODE" != "frontend" ]; then
	echo "  ✓ Backend integration tests (Hurl)"
fi
if [ "$TEST_MODE" != "backend" ]; then
	echo "  ✓ C# build verification"
	echo "  ✓ Godot C# tests (GoDotTest)"
fi
echo ""
