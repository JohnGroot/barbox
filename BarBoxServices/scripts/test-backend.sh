#!/usr/bin/env bash
# Test backend lifecycle management script
# Manages an isolated test backend instance with separate database

set -euo pipefail

PID_FILE="/tmp/barbox-test-backend.pid"
TEST_DB="/tmp/barbox-test.db"
TEST_PORT=8001
LOG_FILE="/tmp/barbox-test-backend.log"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

print_info() { echo -e "${BLUE}ℹ${NC} $1"; }
print_success() { echo -e "${GREEN}✓${NC} $1"; }
print_error() { echo -e "${RED}✗${NC} $1"; }
print_warning() { echo -e "${YELLOW}⚠${NC} $1"; }

# Check if test backend is running
is_running() {
	if [ -f "$PID_FILE" ]; then
		PID=$(cat "$PID_FILE")
		if ps -p "$PID" > /dev/null 2>&1; then
			return 0 # Running
		fi
	fi
	return 1 # Not running
}

# Wait for backend to be ready
wait_for_ready() {
	print_info "Waiting for test backend to be ready..."
	for i in {1..30}; do
		if curl -s "http://127.0.0.1:$TEST_PORT/alive" > /dev/null 2>&1; then
			print_success "Test backend is ready"
			return 0
		fi
		sleep 1
	done
	print_error "Test backend failed to start within 30 seconds"
	return 1
}

# Start test backend
start_backend() {
	if is_running; then
		print_warning "Test backend is already running (PID: $(cat "$PID_FILE"))"
		return 0
	fi

	print_info "Starting test backend on port $TEST_PORT..."

	# Clean up old database
	if [ -f "$TEST_DB" ]; then
		print_info "Removing old test database..."
		rm -f "$TEST_DB"
	fi

	# Get script directory to find project root
	SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
	PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

	# Start backend with test configuration
	cd "$PROJECT_ROOT"

	# Create log file
	touch "$LOG_FILE"

	# Start backend in background
	ENV=local DROP_DB_ON_STARTUP=1 SQLITE_PATH="$TEST_DB" \
		uv run python -m uvicorn bxctl.web.main:app \
		--host 127.0.0.1 \
		--port $TEST_PORT \
		> "$LOG_FILE" 2>&1 &

	echo $! > "$PID_FILE"
	print_success "Test backend started (PID: $!)"

	# Wait for it to be ready
	if wait_for_ready; then
		return 0
	else
		# Failed to start, clean up
		stop_backend
		return 1
	fi
}

# Stop test backend
stop_backend() {
	if ! is_running; then
		print_warning "Test backend is not running"
		return 0
	fi

	PID=$(cat "$PID_FILE")
	print_info "Stopping test backend (PID: $PID)..."

	# Try graceful shutdown first
	kill "$PID" 2>/dev/null || true

	# Wait up to 5 seconds for graceful shutdown
	for i in {1..5}; do
		if ! ps -p "$PID" > /dev/null 2>&1; then
			break
		fi
		sleep 1
	done

	# Force kill if still running
	if ps -p "$PID" > /dev/null 2>&1; then
		print_warning "Forcing backend shutdown..."
		kill -9 "$PID" 2>/dev/null || true
	fi

	# Clean up
	rm -f "$PID_FILE"
	rm -f "$TEST_DB"
	rm -f "$LOG_FILE"

	print_success "Test backend stopped"
}

# Get status
status() {
	if is_running; then
		PID=$(cat "$PID_FILE")
		print_success "Test backend is running (PID: $PID, Port: $TEST_PORT)"

		# Test connection
		if curl -s "http://127.0.0.1:$TEST_PORT/alive" > /dev/null 2>&1; then
			print_success "Backend is responding to health checks"
		else
			print_warning "Backend process exists but not responding"
		fi

		return 0
	else
		print_info "Test backend is not running"
		return 1
	fi
}

# Show logs
logs() {
	if [ -f "$LOG_FILE" ]; then
		tail -n 50 "$LOG_FILE"
	else
		print_warning "Log file not found: $LOG_FILE"
	fi
}

# Main command dispatcher
case "${1:-}" in
	start)
		start_backend
		;;
	stop)
		stop_backend
		;;
	restart)
		stop_backend
		sleep 1
		start_backend
		;;
	status)
		status
		;;
	logs)
		logs
		;;
	*)
		echo "Usage: $0 {start|stop|restart|status|logs}"
		echo ""
		echo "Commands:"
		echo "  start    - Start the test backend"
		echo "  stop     - Stop the test backend"
		echo "  restart  - Restart the test backend"
		echo "  status   - Check if test backend is running"
		echo "  logs     - Show test backend logs"
		exit 1
		;;
esac
