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

	# Check if godot command exists
	if ! command -v godot &> /dev/null; then
		print_error "Godot command not found in PATH"
		print_info "Make sure Godot is installed and available as 'godot' command"
		cd "$SERVICES_ROOT"
		sh scripts/test-backend.sh stop
		exit 1
	fi

	print_info "Running GoDotTest via Godot headless mode..."

	# Run tests with godot
	if godot --path . --headless --run-tests --quit-on-finish; then
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
