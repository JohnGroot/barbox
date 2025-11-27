#!/bin/bash
set -e

# Start BarBox Frontend Game (export-only deployment)

BARBOX_ROOT="$HOME/Desktop/barbox"
FRONTEND_EXEC="$BARBOX_ROOT/current/BarBox.x86_64"
PID_FILE="/tmp/barbox-frontend.pid"
LOCK_FILE="/tmp/barbox-frontend.lock"
LOG_DIR="${HOME}/.local/share/barbox/logs"
LOG_FILE="${LOG_DIR}/frontend.log"

GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m'

log_info() {
	echo -e "${GREEN}[Frontend]${NC} $1"
}

log_warn() {
	echo -e "${YELLOW}[Frontend]${NC} $1"
}

log_error() {
	echo -e "${RED}[Frontend]${NC} $1"
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
is_frontend_running() {
	local pid="$1"

	if [ -z "$pid" ]; then
		return 1
	fi

	# Check if PID exists
	if ! kill -0 "$pid" 2>/dev/null; then
		return 1
	fi

	# Verify it's actually our frontend process (BarBox binary)
	if ps -p "$pid" -o command= | grep -q "BarBox\.x86_64"; then
		return 0
	fi

	return 1
}

# Check existing PID file
if [ -f "$PID_FILE" ]; then
	OLD_PID=$(cat "$PID_FILE")
	if is_frontend_running "$OLD_PID"; then
		log_error "Frontend is already running (PID: $OLD_PID)"
		exit 1
	else
		log_warn "Removing stale PID file"
		rm -f "$PID_FILE"
	fi
fi

# Double-check using pgrep
if pgrep -f "BarBox\.x86_64" > /dev/null; then
	log_error "Frontend appears to be running (found via pgrep)"
	log_error "Stop it first with: ./stop_all.sh"
	exit 1
fi

# Verify export binary exists
if [ ! -f "$FRONTEND_EXEC" ]; then
	log_error "Export binary not found at $FRONTEND_EXEC"
	log_error "Expected symlink: $BARBOX_ROOT/current -> releases/<version>/"
	log_error ""
	log_error "Deploy an export build with:"
	log_error "  cd BarBoxServices/deployment"
	log_error "  ./deploy.sh <ip> --build <version>"
	exit 1
fi

# Verify binary is executable
if [ ! -x "$FRONTEND_EXEC" ]; then
	log_error "Binary not executable: $FRONTEND_EXEC"
	log_warn "Attempting to fix permissions..."
	chmod +x "$FRONTEND_EXEC"
fi

# Show version info
if [ -f "$BARBOX_ROOT/current/VERSION" ]; then
	VERSION=$(head -n1 "$BARBOX_ROOT/current/VERSION")
	log_info "Starting BarBox $VERSION"
fi

# Check if .env.local exists
if [ ! -f "$BARBOX_ROOT/.env.local" ]; then
	log_error ".env.local not found in $BARBOX_ROOT"
	log_error "Run deployment to create configuration"
	exit 1
fi

# Check if backend is running (informational only)
if ! pgrep -f "fastapi.*bxctl" > /dev/null; then
	log_warn "Backend doesn't appear to be running"
	log_warn "Frontend will attempt to connect anyway"
	log_warn "Systemd will restart if connection fails (RestartSec=10s)"
fi

# Always proceed - designed for systemd automatic restarts

cd "$BARBOX_ROOT/current"

log_info "Starting frontend game..."
log_info "Binary: $FRONTEND_EXEC"
log_info "Working dir: $BARBOX_ROOT/current"
log_info "Logs: $LOG_FILE"

# Check display
if [ -z "$DISPLAY" ]; then
	log_warn "DISPLAY not set, using :0"
	export DISPLAY=:0
fi

log_info "Display: $DISPLAY"

# Load environment variables from .env.local
if [ -f "$BARBOX_ROOT/.env.local" ]; then
	log_info "Loading environment variables from .env.local"
	set -a  # Auto-export all variables
	source "$BARBOX_ROOT/.env.local"
	set +a  # Disable auto-export
	log_info "Environment loaded: BOX_ID=${BARBOX_BOX_ID:0:8}..., VENUE=$BARBOX_VENUE_NAME"
else
	log_error ".env.local not found at $BARBOX_ROOT/.env.local"
	exit 1
fi

# Set LD_LIBRARY_PATH for .NET runtime libraries
export LD_LIBRARY_PATH="$BARBOX_ROOT/current/data_BarBox_linuxbsd_x86_64:$LD_LIBRARY_PATH"

# Verify assemblies directory exists before launch
if [[ ! -d "$BARBOX_ROOT/current/data_BarBox_linuxbsd_x86_64" ]]; then
	log_error ".NET assemblies directory missing: $BARBOX_ROOT/current/data_BarBox_linuxbsd_x86_64"
	log_error "Re-deploy with: cd BarBoxServices/deployment && ./deploy.sh <ip> --build <version>"
	exit 1
fi

ASSEMBLY_COUNT=$(ls "$BARBOX_ROOT/current/data_BarBox_linuxbsd_x86_64" 2>/dev/null | wc -l)
log_info "Found $ASSEMBLY_COUNT .NET assembly files"

if [[ $ASSEMBLY_COUNT -lt 50 ]]; then
	log_error "Insufficient .NET assemblies: $ASSEMBLY_COUNT files (expected ~200)"
	exit 1
fi

# Start export binary (no nohup - script stays running for signal handling)
"$FRONTEND_EXEC" >> "$LOG_FILE" 2>&1 &
FRONTEND_PID=$!

# Write PID atomically (still useful for manual stop_all.sh operations)
echo "$FRONTEND_PID" > "${PID_FILE}.tmp"
mv "${PID_FILE}.tmp" "$PID_FILE"

log_info "Frontend started (PID: $FRONTEND_PID)"

# Set up signal forwarding - when this script receives SIGTERM/SIGINT,
# forward it to Godot so SessionManager can do graceful shutdown
cleanup() {
	log_info "Received shutdown signal, forwarding to Godot (PID: $FRONTEND_PID)..."
	kill -TERM "$FRONTEND_PID" 2>/dev/null || true
	# Wait for Godot to exit gracefully (SessionManager has 5s timeout)
	wait "$FRONTEND_PID" 2>/dev/null || true
	rm -f "$PID_FILE"
	log_info "Frontend shutdown complete"
	exit 0
}
trap cleanup TERM INT

# Wait and verify process started successfully
sleep 3
if is_frontend_running "$FRONTEND_PID"; then
	log_info "Frontend is running successfully"
else
	log_error "Frontend failed to start"
	log_error "Last 20 lines of log:"
	tail -n 20 "$LOG_FILE"
	rm -f "$PID_FILE"
	exit 1
fi

# Release lock after successful startup verification
flock -u 200

log_info "Script staying resident for signal handling (systemd Type=simple)"

# Wait for Godot to exit - this keeps the script running and able to receive signals
wait "$FRONTEND_PID"
EXIT_CODE=$?

# Godot exited on its own (crash or normal exit)
rm -f "$PID_FILE"
log_info "Frontend exited with code: $EXIT_CODE"
exit $EXIT_CODE
