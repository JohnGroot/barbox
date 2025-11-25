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

echo ""
log_info "Stopping BarBox services..."

# Stop frontend
if [ -f /tmp/barbox-frontend.pid ]; then
	FRONTEND_PID=$(cat /tmp/barbox-frontend.pid)
	if kill -0 $FRONTEND_PID 2>/dev/null; then
		log_info "Stopping frontend (PID: $FRONTEND_PID)..."
		kill $FRONTEND_PID 2>/dev/null || true
		sleep 1
		# Force kill if still running
		if kill -0 $FRONTEND_PID 2>/dev/null; then
			kill -9 $FRONTEND_PID 2>/dev/null || true
		fi
		log_info "Frontend stopped"
	fi
	rm -f /tmp/barbox-frontend.pid
else
	# Try to find and kill Godot process
	if pgrep -f "Godot.*BarBoxApp" > /dev/null; then
		log_info "Stopping frontend..."
		pkill -f "Godot.*BarBoxApp" || true
		log_info "Frontend stopped"
	else
		log_warn "Frontend not running"
	fi
fi

# Stop backend
if [ -f /tmp/barbox-backend.pid ]; then
	BACKEND_PID=$(cat /tmp/barbox-backend.pid)
	if kill -0 $BACKEND_PID 2>/dev/null; then
		log_info "Stopping backend (PID: $BACKEND_PID)..."
		kill $BACKEND_PID 2>/dev/null || true
		sleep 1
		# Force kill if still running
		if kill -0 $BACKEND_PID 2>/dev/null; then
			kill -9 $BACKEND_PID 2>/dev/null || true
		fi
		log_info "Backend stopped"
	fi
	rm -f /tmp/barbox-backend.pid
else
	# Try to find and kill fastapi process
	if pgrep -f "fastapi.*bxctl" > /dev/null; then
		log_info "Stopping backend..."
		pkill -f "fastapi.*bxctl" || true
		log_info "Backend stopped"
	else
		log_warn "Backend not running"
	fi
fi

echo ""
log_info "All BarBox services stopped"
echo ""
