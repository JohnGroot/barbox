#!/bin/bash

# Stop all BarBox services

GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

log_info() {
	echo -e "${GREEN}[BarBox]${NC} $1"
}

log_warn() {
	echo -e "${YELLOW}[BarBox]${NC} $1"
}

log_error() {
	echo -e "${RED}[BarBox]${NC} $1"
}

# Check if a systemd service is managed (enabled or running)
is_systemd_service_active() {
	local service_name="$1"

	if ! command -v systemctl &> /dev/null; then
		return 1
	fi

	# Check if service is loaded (enabled or running)
	# This catches services that are enabled but failing, in auto-restart, etc.
	systemctl --user list-units --all "$service_name" | grep -q "$service_name"
	return $?
}

# Stop service via systemd (and cleanup any manual processes too)
stop_via_systemd() {
	local service_name="$1"
	local process_pattern="$2"

	log_info "Stopping $service_name via systemd..."
	systemctl --user stop "$service_name" 2>/dev/null
	sleep 1

	# Also kill any manual processes that might be running outside systemd
	if [ -n "$process_pattern" ] && pgrep -f "$process_pattern" > /dev/null; then
		log_info "Cleaning up manual processes matching: $process_pattern"
		# Try SIGTERM first
		pkill -f "$process_pattern" || true
		sleep 1
		# Force kill if still running
		if pgrep -f "$process_pattern" > /dev/null; then
			log_info "Force killing processes matching: $process_pattern"
			pkill -9 -f "$process_pattern" || true
			sleep 1
		fi
	fi

	if systemctl --user is-active "$service_name" &> /dev/null; then
		log_warn "$service_name still active after systemctl stop"
		return 1
	else
		log_info "$service_name stopped successfully"
		return 0
	fi
}

echo ""
log_info "Stopping BarBox services..."

# Stop frontend
if is_systemd_service_active "barbox-frontend.service"; then
	log_info "Frontend is managed by systemd"
	stop_via_systemd "barbox-frontend.service" "BarBox\.x86_64"
else
	log_info "Frontend not managed by systemd, using manual stop"

	if [ -f /tmp/barbox-frontend.pid ]; then
		FRONTEND_PID=$(cat /tmp/barbox-frontend.pid)
		if kill -0 $FRONTEND_PID 2>/dev/null; then
			log_info "Stopping frontend (PID: $FRONTEND_PID)..."
			kill $FRONTEND_PID 2>/dev/null || true
			# Wait up to 8 seconds for graceful shutdown (SessionManager has 5s timeout)
			for i in {1..8}; do
				if ! kill -0 $FRONTEND_PID 2>/dev/null; then
					break
				fi
				sleep 1
			done
			if kill -0 $FRONTEND_PID 2>/dev/null; then
				log_warn "Frontend didn't exit gracefully, force killing..."
				kill -9 $FRONTEND_PID 2>/dev/null || true
			fi
			log_info "Frontend stopped"
		fi
		rm -f /tmp/barbox-frontend.pid
	else
		if pgrep -f "BarBox\.x86_64" > /dev/null; then
			log_info "Stopping frontend..."
			pkill -f "BarBox\.x86_64" || true
			log_info "Frontend stopped"
		else
			log_warn "Frontend not running"
		fi
	fi
fi

# Stop backend
if is_systemd_service_active "barbox-backend.service"; then
	log_info "Backend is managed by systemd"
	stop_via_systemd "barbox-backend.service" "fastapi.*bxctl"
else
	log_info "Backend not managed by systemd, using manual stop"

	if [ -f /tmp/barbox-backend.pid ]; then
		BACKEND_PID=$(cat /tmp/barbox-backend.pid)
		if kill -0 $BACKEND_PID 2>/dev/null; then
			log_info "Stopping backend (PID: $BACKEND_PID)..."
			kill $BACKEND_PID 2>/dev/null || true
			sleep 1
			if kill -0 $BACKEND_PID 2>/dev/null; then
				kill -9 $BACKEND_PID 2>/dev/null || true
			fi
			log_info "Backend stopped"
		fi
		rm -f /tmp/barbox-backend.pid
	else
		if pgrep -f "fastapi.*bxctl" > /dev/null; then
			log_info "Stopping backend..."
			pkill -f "fastapi.*bxctl" || true
			log_info "Backend stopped"
		else
			log_warn "Backend not running"
		fi
	fi
fi

# Clean up stale PID and lock files
rm -f /tmp/barbox-frontend.pid /tmp/barbox-backend.pid
rm -f /tmp/barbox-frontend.lock /tmp/barbox-backend.lock

echo ""
log_info "All BarBox services stopped"
echo ""
