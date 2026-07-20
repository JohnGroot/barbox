#!/bin/bash
set -e

# Start BarBox Backend Service

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BACKEND_DIR="$(dirname "$SCRIPT_DIR")"  # Parent directory is BarBoxServices
PID_FILE="/tmp/barbox-backend.pid"
LOCK_FILE="/tmp/barbox-backend.lock"
LOG_DIR="${HOME}/.local/share/barbox/logs"
LOG_FILE="${LOG_DIR}/backend.log"

GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

log_info() {
	echo -e "${GREEN}[Backend]${NC} $1"
}

log_warn() {
	echo -e "${YELLOW}[Backend]${NC} $1"
}

log_error() {
	echo -e "${RED}[Backend]${NC} $1"
}

# Create log directory
mkdir -p "$LOG_DIR"

# Acquire lock to prevent concurrent starts
exec 200>"$LOCK_FILE"
if ! flock -n 200; then
	log_error "Another instance is starting or stopping"
	exit 1
fi

# Function to check if process is actually running
is_backend_running() {
	local pid="$1"

	if [ -z "$pid" ]; then
		return 1
	fi

	# Check if PID exists
	if ! kill -0 "$pid" 2>/dev/null; then
		return 1
	fi

	# Verify it's actually our backend process
	if ps -p "$pid" -o command= | grep -q "fastapi.*bxctl"; then
		return 0
	fi

	return 1
}

# Check existing PID file
if [ -f "$PID_FILE" ]; then
	OLD_PID=$(cat "$PID_FILE")
	if is_backend_running "$OLD_PID"; then
		log_error "Backend is already running (PID: $OLD_PID)"
		exit 1
	else
		log_warn "Removing stale PID file"
		rm -f "$PID_FILE"
	fi
fi

# Double-check using pgrep
if pgrep -f "fastapi.*bxctl" > /dev/null; then
	log_error "Backend appears to be running (found via pgrep)"
	log_error "Stop it first with: ./stop_all.sh"
	exit 1
fi

# Check if backend directory exists
if [ ! -d "$BACKEND_DIR" ]; then
	log_error "Backend directory not found: $BACKEND_DIR"
	exit 1
fi

# Check if virtual environment exists
if [ ! -d "$BACKEND_DIR/.venv" ]; then
	log_error "Python virtual environment not found"
	echo "Run ./remote_setup.sh first"
	exit 1
fi

cd "$BACKEND_DIR"

log_info "Starting backend service..."
log_info "Backend directory: $BACKEND_DIR"
log_info "Mode: production (ENV=prod)"
log_info "Logs: $LOG_FILE"

# Activate virtual environment and start backend
source .venv/bin/activate

# Start backend in production mode with proper logging
# NOTE: Do NOT override ENV here - it must be loaded from .env file
# (environment variables have higher precedence than .env values)
nohup python -m fastapi run src/bxctl/app/main.py >> "$LOG_FILE" 2>&1 &

BACKEND_PID=$!

# Write PID atomically
echo "$BACKEND_PID" > "${PID_FILE}.tmp"
mv "${PID_FILE}.tmp" "$PID_FILE"

log_info "Backend started (PID: $BACKEND_PID)"
log_info "Backend listening on http://127.0.0.1:8000"

# Wait and verify process started successfully
sleep 2
if is_backend_running "$BACKEND_PID"; then
	log_info "Backend is running successfully"
else
	log_error "Backend failed to start"
	log_error "Last 20 lines of log:"
	tail -n 20 "$LOG_FILE"
	rm -f "$PID_FILE"
	exit 1
fi

# Release lock
flock -u 200
